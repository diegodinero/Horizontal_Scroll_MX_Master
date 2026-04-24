// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace Horizontal_Scroll_MX_Master
{
    /// <summary>
    /// Intercepts the Logitech MX Master thumb wheel (horizontal scroll) and maps it to
    /// Shift + vertical scroll, which is the combination Quantower uses to scroll charts
    /// horizontally. Uses a Windows low-level mouse hook (WH_MOUSE_LL) and SendInput.
    /// Information about API: http://api.quantower.com
    /// </summary>
    public class Horizontal_Scroll_MX_Master : Indicator
    {
        // ── Windows constants ──────────────────────────────────────────────────────
        private const int    WH_MOUSE_LL       = 14;
        private const int    WM_MOUSEHWHEEL    = 0x020E;
        private const int    WM_QUIT           = 0x0012;
        private const uint   INPUT_MOUSE       = 0;
        private const uint   INPUT_KEYBOARD    = 1;
        private const uint   MOUSEEVENTF_WHEEL = 0x0800;
        private const uint   KEYEVENTF_KEYUP   = 0x0002;
        private const ushort VK_SHIFT          = 0x10;

        // ── P/Invoke ───────────────────────────────────────────────────────────────
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
                                                      IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                                    IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint idThread, uint Msg,
                                                     IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs,
                                             [MarshalAs(UnmanagedType.LPArray)] INPUT[] pInputs,
                                             int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd,
                                              uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpmsg);

        // ── Structs ────────────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int     pt_x;
            public int     pt_y;
            public uint    mouseData;   // HIWORD = wheel delta for WM_MOUSEHWHEEL
            public uint    flags;
            public uint    time;
            public IntPtr  dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int    dx;
            public int    dy;
            public uint   mouseData;
            public uint   dwFlags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint   dwFlags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        // The Windows INPUT struct contains a union whose size is driven by MOUSEINPUT
        // (the largest member). Using [LayoutKind.Explicit] here ensures the union is
        // correctly sized so Marshal.SizeOf<INPUT>() matches the native sizeof(INPUT)
        // that SendInput validates via its cbSize parameter.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT  mi;
            [FieldOffset(0)] public KEYBDINPUT  ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint      type;
            public INPUTUNION union;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint   message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint   time;
            public int    pt_x;
            public int    pt_y;
        }

        // ── State ──────────────────────────────────────────────────────────────────
        private Thread            _hookThread;
        private uint              _hookThreadId;
        private IntPtr            _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc _hookProc;  // kept alive to prevent GC
        private ManualResetEventSlim _hookReady = new ManualResetEventSlim(false);

        // ── Constructor ────────────────────────────────────────────────────────────
        public Horizontal_Scroll_MX_Master()
            : base()
        {
            Name        = "Horizontal_Scroll_MX_Master";
            Description = "Maps the Logitech MX Master thumb wheel to Shift + vertical scroll (Quantower horizontal scroll)";
            SeparateWindow = false;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        protected override void OnInit()
        {
            // Start a dedicated STA thread that owns the hook and runs a message pump.
            _hookProc  = HookCallback; // pin delegate in a field so GC won't collect it
            _hookThread = new Thread(HookThreadProc)
            {
                IsBackground = true,
                Name         = "MX_Master_HookThread"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            // Wait until the hook is installed before returning.
            if (!_hookReady.Wait(TimeSpan.FromSeconds(5)))
                Trace.WriteLine("MX Master hook: timed out waiting for hook thread to start.");
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            // No price calculations needed; this indicator only handles mouse input.
        }

        protected override void OnClear()
        {
            StopHookThread();
        }

        // ── Hook thread ────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs on a dedicated thread: installs the hook, runs a message pump, then
        /// cleans up when the pump is stopped.
        /// </summary>
        private void HookThreadProc()
        {
            _hookThreadId = GetCurrentThreadId();
            _hookHandle   = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);

            _hookReady.Set(); // signal OnInit that the hook is installed

            if (_hookHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Trace.WriteLine($"MX Master hook: SetWindowsHookEx failed (Win32 error {err}).");
                return;
            }

            // Message pump — required for WH_MOUSE_LL callbacks to fire.
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Signals the hook thread to exit and waits for it.
        /// </summary>
        private void StopHookThread()
        {
            if (_hookThread == null || !_hookThread.IsAlive)
                return;

            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!_hookThread.Join(TimeSpan.FromSeconds(3)))
                Trace.WriteLine("MX Master hook: hook thread did not exit within the timeout.");

            _hookThread = null;

            _hookReady?.Dispose();
            _hookReady = null;
        }

        // ── Hook callback ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called for every low-level mouse event. Filters WM_MOUSEHWHEEL and synthesizes
        /// Shift + vertical scroll, which Quantower interprets as horizontal chart scroll.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == WM_MOUSEHWHEEL)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // HIWORD of mouseData is a signed wheel delta (positive = right/forward).
                short delta = (short)((hookStruct.mouseData >> 16) & 0xFFFF);

                if (delta != 0)
                    SendShiftScroll(delta);
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Synthesizes Shift-down + WM_MOUSEWHEEL(delta) + Shift-up in a single SendInput
        /// call. Quantower maps this combination to horizontal chart scrolling.
        /// </summary>
        private static void SendShiftScroll(short delta)
        {
            var inputs = new INPUT[3];

            // Shift key down
            inputs[0].type           = INPUT_KEYBOARD;
            inputs[0].union.ki.wVk   = VK_SHIFT;

            // Vertical mouse wheel with the horizontal delta
            inputs[1].type                  = INPUT_MOUSE;
            inputs[1].union.mi.dwFlags      = MOUSEEVENTF_WHEEL;
            inputs[1].union.mi.mouseData    = (uint)(int)delta;

            // Shift key up
            inputs[2].type           = INPUT_KEYBOARD;
            inputs[2].union.ki.wVk   = VK_SHIFT;
            inputs[2].union.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }
}
