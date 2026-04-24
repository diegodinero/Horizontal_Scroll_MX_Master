// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Drawing;
using TradingPlatform.BusinessLayer;

namespace Horizontal_Scroll_MX_Master
{
    /// <summary>
    /// An example of blank indicator. Add your code, compile it and use on the charts in the assigned trading terminal.
    /// Information about API you can find here: http://api.quantower.com
    /// Code samples: https://github.com/Quantower/Examples
    /// </summary>
	public class Horizontal_Scroll_MX_Master : Indicator
    {
        /// <summary>
        /// Indicator's constructor. Contains general information: name, description, LineSeries etc. 
        /// </summary>
        public Horizontal_Scroll_MX_Master()
            : base()
        {
            // Defines indicator's name and description.
            Name = "Horizontal_Scroll_MX_Master";
            Description = "My indicator's annotation";

            // Defines line on demand with particular parameters.
            AddLineSeries("line1", Color.CadetBlue, 1, LineStyle.Solid);

            // By default indicator will be applied on main window of the chart
            SeparateWindow = false;
        }

        /// <summary>
        /// This function will be called after creating an indicator as well as after its input params reset or chart (symbol or timeframe) updates.
        /// </summary>
        protected override void OnInit()
        {
            // Add your initialization code here
        }

        /// <summary>
        /// Calculation entry point. This function is called when a price data updates. 
        /// Will be runing under the HistoricalBar mode during history loading. 
        /// Under NewTick during realtime. 
        /// Under NewBar if start of the new bar is required.
        /// </summary>
        /// <param name="args">Provides data of updating reason and incoming price.</param>
        protected override void OnUpdate(UpdateArgs args)
        {
            // Add your calculations here.         

            //
            // An example of accessing the prices          
            // ----------------------------
            //
            // double bid = Bid();                          // To get current Bid price
            // double open = Open(5);                       // To get open price for the fifth bar before the current
            // 

            //
            // An example of settings values for indicator's lines
            // -----------------------------------------------
            //            
            // SetValue(1.43);                              // To set value for first line of the indicator
            // SetValue(1.43, 1);                           // To set value for second line of the indicator
            // SetValue(1.43, 1, 5);                        // To set value for fifth bar before the current for second line of the indicator
        }
    }
}
