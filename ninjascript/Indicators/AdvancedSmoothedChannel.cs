#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Define the Enum for the dropdown menu
public enum AdvFilterType
{
    SuperSmoother, EhlersEMA, Gaussian, Butterworth, BandStop, SMA, EMA, RMA
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdvancedSmoothedChannel : Indicator
    {
        // Series to hold our recursive math values
        private Series<double> ssPrice;
        private Series<double> ssTR;
        private Series<double> ehlersEmaSeries;
        private Series<double> gaussianSeries;
        private Series<double> butterworthSeries;
        private Series<double> bandStopSeries;
        private Series<double> rmaSeries;

        // Math Constants
        private double s_a1, s_b1, s_c1, s_c2, s_c3;
        private double alphaEhlers, alphaGauss, alphaButter, alphaBand, betaBand;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Advanced DSP Smoothed Volatility Channel";
                Name                                        = "AdvancedSmoothedChannel";
                Calculate                                   = Calculate.OnBarClose;
                IsOverlay                                   = true;
                DisplayInDataBox                            = true;
                DrawOnPricePanel                            = true;
                DrawHorizontalGridLines                     = true;
                DrawVerticalGridLines                       = true;
                PaintPriceMarkers                           = true;
                ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive                    = true;

                // Inputs
                FilterType      = AdvFilterType.SuperSmoother;
                FilterLength    = 200;
                InnerMult       = 1.0;
                OuterMult       = 2.415;
                DrawChannel     = true;

                // Plots
                AddPlot(new Stroke(Brushes.Yellow, 2), PlotStyle.Line, "Mean");
                AddPlot(new Stroke(Brushes.LimeGreen, DashStyleHelper.Dash, 1), PlotStyle.Line, "R1");
                AddPlot(new Stroke(Brushes.LimeGreen, DashStyleHelper.Dash, 1), PlotStyle.Line, "S1");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "R2");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "S2");
            }
            else if (State == State.Configure)
            {
                // Initialize Series
                ssPrice             = new Series<double>(this, MaximumBarsLookBack.Infinite);
                ssTR                = new Series<double>(this, MaximumBarsLookBack.Infinite);
                ehlersEmaSeries     = new Series<double>(this, MaximumBarsLookBack.Infinite);
                gaussianSeries      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                butterworthSeries   = new Series<double>(this, MaximumBarsLookBack.Infinite);
                bandStopSeries      = new Series<double>(this, MaximumBarsLookBack.Infinite);
                rmaSeries           = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
            else if (State == State.DataLoaded)
            {
                // Pre-calculate math constants that don't change per bar
                double pi = Math.PI;
                double cycle = 2 * pi / FilterLength;

                s_a1 = Math.Exp(-Math.Sqrt(2) * pi / FilterLength);
                s_b1 = 2 * s_a1 * Math.Cos(Math.Sqrt(2) * pi / FilterLength);
                s_c3 = -Math.Pow(s_a1, 2);
                s_c2 = s_b1;
                s_c1 = 1 - s_c2 - s_c3;

                alphaEhlers = (Math.Cos(cycle) + Math.Sin(cycle) - 1) / Math.Cos(cycle);

                double betaGauss = 2.415 * (1 - Math.Cos(cycle));
                alphaGauss = -betaGauss + Math.Sqrt(betaGauss * betaGauss + 2 * betaGauss);

                double betaButter = 2.415 * (1 - Math.Cos(cycle));
                alphaButter = -betaButter + Math.Sqrt(betaButter * betaButter + 2 * betaButter);

                betaBand = Math.Cos(cycle);
                double gammaBand = 1 / Math.Cos(cycle * 2 * 0.1);
                alphaBand = gammaBand - Math.Sqrt(gammaBand * gammaBand - 1);
            }
        }

        protected override void OnBarUpdate()
        {
            // Need at least 2 bars of history for recursive math
            if (CurrentBar < 2)
            {
                ssPrice[0] = Input[0];
                ssTR[0] = High[0] - Low[0];
                ehlersEmaSeries[0] = Input[0];
                gaussianSeries[0] = Input[0];
                butterworthSeries[0] = Input[0];
                bandStopSeries[0] = Input[0];
                rmaSeries[0] = Input[0];
                return;
            }

            double price = Input[0]; // Typical Price / HLC3 passed via Input
            
            // True Range calculation
            double tr = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));

            // Update Recursive Filters
            ssPrice[0] = s_c1 * price + s_c2 * ssPrice[1] + s_c3 * ssPrice[2];
            ssTR[0] = s_c1 * tr + s_c2 * ssTR[1] + s_c3 * ssTR[2];

            ehlersEmaSeries[0] = alphaEhlers * price + (1 - alphaEhlers) * ehlersEmaSeries[1];

            gaussianSeries[0] = (alphaGauss * alphaGauss) * price + 2 * (1 - alphaGauss) * gaussianSeries[1] - Math.Pow(1 - alphaGauss, 2) * gaussianSeries[2];

            butterworthSeries[0] = (alphaButter * alphaButter / 4) * (price + 2 * Input[1] + Input[2]) + 2 * (1 - alphaButter) * butterworthSeries[1] - Math.Pow(1 - alphaButter, 2) * butterworthSeries[2];

            bandStopSeries[0] = ((1 + alphaBand) / 2) * (price - 2 * betaBand * Input[1] + Input[2]) + (betaBand * (1 + alphaBand)) * bandStopSeries[1] - alphaBand * bandStopSeries[2];

            rmaSeries[0] = ((FilterLength - 1) * rmaSeries[1] + price) / FilterLength;

            // Determine Mean Line based on user selection
            double meanLine = 0;
            switch (FilterType)
            {
                case AdvFilterType.SuperSmoother:   meanLine = ssPrice[0]; break;
                case AdvFilterType.EhlersEMA:       meanLine = ehlersEmaSeries[0]; break;
                case AdvFilterType.Gaussian:        meanLine = gaussianSeries[0]; break;
                case AdvFilterType.Butterworth:     meanLine = butterworthSeries[0]; break;
                case AdvFilterType.BandStop:        meanLine = bandStopSeries[0]; break;
                case AdvFilterType.SMA:             meanLine = SMA(FilterLength)[0]; break;
                case AdvFilterType.EMA:             meanLine = EMA(FilterLength)[0]; break;
                case AdvFilterType.RMA:             meanLine = rmaSeries[0]; break;
            }

            double meanRange = ssTR[0];
            double pi = Math.PI;

            // Calculate Bands
            double upBand1 = meanLine + meanRange * (pi * InnerMult);
            double loBand1 = meanLine - meanRange * (pi * InnerMult);
            double upBand2 = meanLine + meanRange * (pi * OuterMult);
            double loBand2 = meanLine - meanRange * (pi * OuterMult);

            // Assign to Plots if enabled
            if (DrawChannel)
            {
                MeanPlot[0] = meanLine;
                R1Plot[0] = upBand1;
                S1Plot[0] = loBand1;
                R2Plot[0] = upBand2;
                S2Plot[0] = loBand2;
            }

            // Dashboard Label Logic
            int condition = 0;
            if (Close[0] > meanLine)
            {
                if (High[0] >= upBand2) condition = 3; // Simplified from 18 zones for performance
                else if (High[0] >= upBand1) condition = 2;
                else if (Close[0] <= meanLine + meanRange) condition = 4;
                else condition = 5;
            }
            else if (Close[0] < meanLine)
            {
                if (Low[0] <= loBand2) condition = -3;
                else if (Low[0] <= loBand1) condition = -2;
                else if (Close[0] >= meanLine - meanRange) condition = -4;
                else condition = -5;
            }

            string statusText = "Price at Mean";
            Brush textColor = Brushes.Gray;

            if (condition >= 2) { statusText = "Overbought"; textColor = Brushes.Red; }
            else if (condition == 1) { statusText = "Overbought (Weak)"; textColor = Brushes.Orange; }
            else if (condition <= -2) { statusText = "Oversold"; textColor = Brushes.Cyan; }
            else if (condition == -1) { statusText = "Oversold (Weak)"; textColor = Brushes.LimeGreen; }
            else if (condition == 5) { statusText = "Price Above Mean"; textColor = Brushes.White; }
            else if (condition == -5) { statusText = "Price Below Mean"; textColor = Brushes.White; }
            else if (condition == 4 || condition == -4) { statusText = "Price Near Mean"; textColor = Brushes.White; }

            double distFromMean = ((Close[0] - meanLine) / Close[0]) * 100;

            string label = $"{statusText}\nDist from Mean: {distFromMean.ToString("N2")}%";
            
            // Draw text on the chart
            Draw.TextFixed(this, "StatusLabel", label, TextPosition.TopRight, textColor, new Gui.Tools.SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Transparent, 0);
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="Filter Type", Order=1, GroupName="Parameters")]
        public AdvFilterType FilterType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Length", Order=2, GroupName="Parameters")]
        public int FilterLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, double.MaxValue)]
        [Display(Name="Inner Multiplier", Order=3, GroupName="Parameters")]
        public double InnerMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, double.MaxValue)]
        [Display(Name="Outer Multiplier", Order=4, GroupName="Parameters")]
        public double OuterMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Draw Channel", Order=5, GroupName="Parameters")]
        public bool DrawChannel { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MeanPlot { get { return Values[0]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> R1Plot { get { return Values[1]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> S1Plot { get { return Values[2]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> R2Plot { get { return Values[3]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> S2Plot { get { return Values[4]; } }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AdvancedSmoothedChannel[] cacheAdvancedSmoothedChannel;
		public AdvancedSmoothedChannel AdvancedSmoothedChannel(AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			return AdvancedSmoothedChannel(Input, filterType, filterLength, innerMult, outerMult, drawChannel);
		}

		public AdvancedSmoothedChannel AdvancedSmoothedChannel(ISeries<double> input, AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			if (cacheAdvancedSmoothedChannel != null)
				for (int idx = 0; idx < cacheAdvancedSmoothedChannel.Length; idx++)
					if (cacheAdvancedSmoothedChannel[idx] != null && cacheAdvancedSmoothedChannel[idx].FilterType == filterType && cacheAdvancedSmoothedChannel[idx].FilterLength == filterLength && cacheAdvancedSmoothedChannel[idx].InnerMult == innerMult && cacheAdvancedSmoothedChannel[idx].OuterMult == outerMult && cacheAdvancedSmoothedChannel[idx].DrawChannel == drawChannel && cacheAdvancedSmoothedChannel[idx].EqualsInput(input))
						return cacheAdvancedSmoothedChannel[idx];
			return CacheIndicator<AdvancedSmoothedChannel>(new AdvancedSmoothedChannel(){ FilterType = filterType, FilterLength = filterLength, InnerMult = innerMult, OuterMult = outerMult, DrawChannel = drawChannel }, input, ref cacheAdvancedSmoothedChannel);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AdvancedSmoothedChannel AdvancedSmoothedChannel(AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			return indicator.AdvancedSmoothedChannel(Input, filterType, filterLength, innerMult, outerMult, drawChannel);
		}

		public Indicators.AdvancedSmoothedChannel AdvancedSmoothedChannel(ISeries<double> input , AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			return indicator.AdvancedSmoothedChannel(input, filterType, filterLength, innerMult, outerMult, drawChannel);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AdvancedSmoothedChannel AdvancedSmoothedChannel(AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			return indicator.AdvancedSmoothedChannel(Input, filterType, filterLength, innerMult, outerMult, drawChannel);
		}

		public Indicators.AdvancedSmoothedChannel AdvancedSmoothedChannel(ISeries<double> input , AdvFilterType filterType, int filterLength, double innerMult, double outerMult, bool drawChannel)
		{
			return indicator.AdvancedSmoothedChannel(input, filterType, filterLength, innerMult, outerMult, drawChannel);
		}
	}
}

#endregion
