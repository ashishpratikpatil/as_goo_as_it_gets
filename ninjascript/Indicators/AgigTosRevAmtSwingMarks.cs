#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Thinkorswim AgigTosRevAmtSwingMarks.
// RevAmt = close * (0.01 / 100) + ATR(5) * 2 + 0.05
// Short is drawn on the swing-high candle. Long is drawn on the swing-low candle.

namespace NinjaTrader.NinjaScript.Indicators
{
	public class AgigTosRevAmtSwingMarks : Indicator
	{
		private const int StUndef = 1;
		private const int StUp = 2;
		private const int StDn = 3;

		private Series<double> emaHighS;
		private Series<double> emaLowS;
		private ATR atrInd;
		private Series<double> stateS;
		private Series<double> maxS;
		private Series<double> minS;
		private Series<double> highBarS;
		private Series<double> lowBarS;
		private SimpleFont textFont;
		private int openShortBar;
		private int openLongBar;
		private int lastShortAlertBar;
		private int lastLongAlertBar;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "TOS AGIG swing marks. Short is drawn on the EMA swing-high candle and Long on the EMA swing-low candle. Reversal dollars match the TOS debug bubbles: close * (0.01/100) + ATR(5) * 2 + 0.05. Gray text every 10 bars prints Bar, EMA_H, EMA_L, ATR, and RevAmt.";
				Name = "AGIG TOS RevAmt Swing Marks";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				BarsRequiredToPlot = 5;

				AtrReversal = 2;
				EmaLength = 5;
				AtrLength = 5;
				PercentageReversal = 0.01;
				AbsoluteReversal = 0.05;
				TickReversal = 0;
				EnableAlerts = true;
				ShowArrows = true;
				ShowBubbles = true;
				ShowSwings = true;
				ShowDebugBubbles = true;
				DebugEveryNBars = 10;
				BubbleOffsetPixels = 12;
				MarkerBrush = Brushes.White;

				AddPlot(Brushes.White, "Short");
				AddPlot(Brushes.White, "Long");
				Plots[0].PlotStyle = PlotStyle.Square;
				Plots[1].PlotStyle = PlotStyle.Dot;
				Plots[0].Width = 3;
				Plots[1].Width = 3;
			}
			else if (State == State.DataLoaded)
			{
				emaHighS = new Series<double>(this);
				emaLowS = new Series<double>(this);
				atrInd = ATR(Math.Max(1, AtrLength));
				stateS = new Series<double>(this);
				maxS = new Series<double>(this);
				minS = new Series<double>(this);
				highBarS = new Series<double>(this);
				lowBarS = new Series<double>(this);
				textFont = new SimpleFont("Arial", 11) { Bold = true };

				openShortBar = -1;
				openLongBar = -1;
				lastShortAlertBar = -1;
				lastLongAlertBar = -1;
			}
		}

		public override string DisplayName
		{
			get { return Name; }
		}

		protected override void OnBarUpdate()
		{
			ShortPivot.Reset(0);
			LongPivot.Reset(0);

			double k = 2.0 / (Math.Max(1, EmaLength) + 1.0);
			if (CurrentBar == 0)
			{
				emaHighS[0] = High[0];
				emaLowS[0] = Low[0];
			}
			else
			{
				emaHighS[0] = High[0] * k + emaHighS[1] * (1.0 - k);
				emaLowS[0] = Low[0] * k + emaLowS[1] * (1.0 - k);
			}

			double priceH = emaHighS[0];
			double priceL = emaLowS[0];
			double closePx = Close[0] > 0 ? Close[0] : priceH;
			double absRev = AbsoluteReversal != 0 ? AbsoluteReversal : TickReversal * TickSize;
			bool atrReady = atrInd.IsValidDataPoint(0);
			double atrValue = atrReady ? atrInd[0] : 0;
			double reversalDist = closePx * (PercentageReversal / 100.0) + atrValue * AtrReversal + absRev;
			bool canReverse = AtrReversal == 0 || atrReady;

			int st;
			double maxP;
			double minP;
			bool newMax = false;
			bool newMin = false;

			if (CurrentBar == 0)
			{
				st = StUndef;
				maxP = priceH;
				minP = priceL;
			}
			else
			{
				int prior = (int)stateS[1];
				double prevMax = maxS[1];
				double prevMin = minS[1];
				st = prior;
				maxP = prevMax;
				minP = prevMin;

				if (prior == StUndef)
				{
					if (priceH >= prevMax)
					{
						st = StUp;
						maxP = priceH;
						newMax = true;
					}
					else if (priceL <= prevMin)
					{
						st = StDn;
						minP = priceL;
						newMin = true;
					}
				}
				else if (prior == StUp)
				{
					if (canReverse && priceL <= prevMax - reversalDist)
					{
						st = StDn;
						minP = priceL;
						newMin = true;
					}
					else if (priceH >= prevMax)
					{
						st = StUp;
						maxP = priceH;
						newMax = true;
					}
				}
				else
				{
					if (canReverse && priceH >= prevMin + reversalDist)
					{
						st = StUp;
						maxP = priceH;
						newMax = true;
					}
					else if (priceL <= prevMin)
					{
						st = StDn;
						minP = priceL;
						newMin = true;
					}
				}
			}

			stateS[0] = st;
			maxS[0] = maxP;
			minS[0] = minP;
			highBarS[0] = newMax ? CurrentBar : (CurrentBar == 0 ? -1 : highBarS[1]);
			lowBarS[0] = newMin ? CurrentBar : (CurrentBar == 0 ? -1 : lowBarS[1]);

			int highBar = (int)highBarS[0];
			int lowBar = (int)lowBarS[0];
			bool flippedDown = CurrentBar > 0 && (int)stateS[1] == StUp && st == StDn;
			bool flippedUp = CurrentBar > 0 && (int)stateS[1] == StDn && st == StUp;

			if (flippedDown && highBar >= 0)
			{
				PaintPivot(highBar, true, "AGIG_S" + highBar);
				MaybeAlert(true, highBar);
			}
			if (flippedUp && lowBar >= 0)
			{
				PaintPivot(lowBar, false, "AGIG_L" + lowBar);
				MaybeAlert(false, lowBar);
			}

			// Thinkorswim hides lastH/lastL while that extreme is still the last bar.
			// A new high on this candle removes the open Short until the next candle.
			int liveShort = st == StUp && highBar >= 0 && highBar < CurrentBar ? highBar : -1;
			int liveLong = st == StDn && lowBar >= 0 && lowBar < CurrentBar ? lowBar : -1;
			MoveOpen(ref openShortBar, liveShort, true, "AGIG_OpenS");
			MoveOpen(ref openLongBar, liveLong, false, "AGIG_OpenL");

			DrawDebugBubble(atrValue, reversalDist);
		}

		private void MoveOpen(ref int paintedBar, int bar, bool isShort, string prefix)
		{
			if (paintedBar == bar)
				return;
			if (paintedBar >= 0)
				ErasePivot(paintedBar, isShort, prefix + paintedBar);
			paintedBar = bar;
			if (bar >= 0)
				PaintPivot(bar, isShort, prefix + bar);
		}

		private void PaintPivot(int bar, bool isShort, string tag)
		{
			int ago = CurrentBar - bar;
			if (ago < 0 || ago > CurrentBar)
				return;

			Brush brush = MarkerBrush ?? Brushes.White;
			double pivotPrice = isShort ? emaHighS[ago] : emaLowS[ago];
			double anchor = isShort ? High[ago] : Low[ago];
			if (isShort)
				ShortPivot[ago] = pivotPrice;
			else
				LongPivot[ago] = pivotPrice;

			if (ShowSwings)
			{
				if (isShort)
					Draw.Square(this, tag + "sq", false, ago, pivotPrice, brush);
				else
					Draw.Dot(this, tag + "dt", false, ago, pivotPrice, brush);
			}
			if (ShowArrows)
			{
				if (isShort)
					Draw.ArrowDown(this, tag + "ar", false, ago, anchor + TickSize, brush);
				else
					Draw.ArrowUp(this, tag + "ar", false, ago, anchor - TickSize, brush);
			}
			if (ShowBubbles)
				Draw.Text(this, tag + "tx", false, isShort ? "Short" : "Long", ago, anchor, isShort ? BubbleOffsetPixels : -BubbleOffsetPixels, brush, textFont, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void ErasePivot(int bar, bool isShort, string tag)
		{
			RemoveDrawObject(tag + "sq");
			RemoveDrawObject(tag + "dt");
			RemoveDrawObject(tag + "ar");
			RemoveDrawObject(tag + "tx");
			int ago = CurrentBar - bar;
			if (ago < 0 || ago > CurrentBar)
				return;
			if (isShort)
				ShortPivot.Reset(ago);
			else
				LongPivot.Reset(ago);
		}

		private void DrawDebugBubble(double atrValue, double reversalDist)
		{
			int every = Math.Max(1, DebugEveryNBars);
			if (!ShowDebugBubbles || (CurrentBar + 1) % every != 0)
				return;

			string text = "Bar: " + (CurrentBar + 1)
				+ "\nEMA_H: " + emaHighS[0].ToString("0.00")
				+ "\nEMA_L: " + emaLowS[0].ToString("0.00")
				+ "\nATR: " + atrValue.ToString("0.00")
				+ "\nRevAmt: " + reversalDist.ToString("0.00");
			Draw.Text(this, "AGIG_Dbg_" + CurrentBar, false, text, 0, High[0], 30, Brushes.Gainsboro, textFont, TextAlignment.Left, Brushes.Gray, Brushes.DimGray, 80);
		}

		private void MaybeAlert(bool isShort, int bar)
		{
			if (!EnableAlerts || State != State.Realtime || bar < 0)
				return;
			if (isShort)
			{
				if (lastShortAlertBar == bar)
					return;
				lastShortAlertBar = bar;
				Alert("AGIG_Short_" + bar, Priority.Medium, "Short Signal", NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert3.wav", 0, Brushes.Black, MarkerBrush ?? Brushes.White);
			}
			else
			{
				if (lastLongAlertBar == bar)
					return;
				lastLongAlertBar = bar;
				Alert("AGIG_Long_" + bar, Priority.Medium, "Long Signal", NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 0, Brushes.Black, MarkerBrush ?? Brushes.White);
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "ATR Reversal", Description = "TOS: reversal dollars = close * (percent/100) + ATR * this + absolute.", GroupName = "ZigZag", Order = 1)]
		public double AtrReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA Length", GroupName = "ZigZag", Order = 2)]
		public int EmaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR Length", GroupName = "ZigZag", Order = 3)]
		public int AtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Percentage Reversal", Description = "0.01 means 0.01 percent of close.", GroupName = "ZigZag", Order = 4)]
		public double PercentageReversal { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Absolute Reversal", Description = "Dollars added to the reversal. Thinkorswim default 0.05.", GroupName = "ZigZag", Order = 5)]
		public double AbsoluteReversal { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Tick Reversal", Description = "Used only when Absolute Reversal is 0.", GroupName = "ZigZag", Order = 6)]
		public int TickReversal { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Alerts", GroupName = "Signals", Order = 1)]
		public bool EnableAlerts { get; set; }

		[Display(Name = "Show Arrows", GroupName = "Signals", Order = 2)]
		public bool ShowArrows { get; set; }

		[Display(Name = "Show Bubbles", GroupName = "Signals", Order = 3)]
		public bool ShowBubbles { get; set; }

		[Display(Name = "Show Square And Dot", GroupName = "Signals", Order = 4)]
		public bool ShowSwings { get; set; }

		[Display(Name = "Show Debug Bubbles", Description = "Gray text every N bars with bar, EMA high, EMA low, ATR, and RevAmt.", GroupName = "Signals", Order = 7)]
		public bool ShowDebugBubbles { get; set; }

		[Range(1, 500)]
		[Display(Name = "Debug Every N Bars", GroupName = "Signals", Order = 8)]
		public int DebugEveryNBars { get; set; }

		[Range(0, 200)]
		[Display(Name = "Bubble Offset (pixels)", GroupName = "Signals", Order = 5)]
		public int BubbleOffsetPixels { get; set; }

		[XmlIgnore]
		[Display(Name = "Marker Color", GroupName = "Signals", Order = 6)]
		public Brush MarkerBrush { get; set; }

		[Browsable(false)]
		public string MarkerBrushSerializable
		{
			get { return Serialize.BrushToString(MarkerBrush); }
			set { MarkerBrush = Serialize.StringToBrush(value); }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ShortPivot
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LongPivot
		{
			get { return Values[1]; }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AgigTosRevAmtSwingMarks[] cacheAgigTosRevAmtSwingMarks;
		public AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input, double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			if (cacheAgigTosRevAmtSwingMarks != null)
				for (int idx = 0; idx < cacheAgigTosRevAmtSwingMarks.Length; idx++)
					if (cacheAgigTosRevAmtSwingMarks[idx] != null && cacheAgigTosRevAmtSwingMarks[idx].AtrReversal == atrReversal && cacheAgigTosRevAmtSwingMarks[idx].EmaLength == emaLength && cacheAgigTosRevAmtSwingMarks[idx].AtrLength == atrLength && cacheAgigTosRevAmtSwingMarks[idx].PercentageReversal == percentageReversal && cacheAgigTosRevAmtSwingMarks[idx].AbsoluteReversal == absoluteReversal && cacheAgigTosRevAmtSwingMarks[idx].TickReversal == tickReversal && cacheAgigTosRevAmtSwingMarks[idx].EnableAlerts == enableAlerts && cacheAgigTosRevAmtSwingMarks[idx].EqualsInput(input))
						return cacheAgigTosRevAmtSwingMarks[idx];
			return CacheIndicator<AgigTosRevAmtSwingMarks>(new AgigTosRevAmtSwingMarks(){ AtrReversal = atrReversal, EmaLength = emaLength, AtrLength = atrLength, PercentageReversal = percentageReversal, AbsoluteReversal = absoluteReversal, TickReversal = tickReversal, EnableAlerts = enableAlerts }, input, ref cacheAgigTosRevAmtSwingMarks);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input , double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input , double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}
	}
}

#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AgigTosRevAmtSwingMarks[] cacheAgigTosRevAmtSwingMarks;
		public AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input, double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			if (cacheAgigTosRevAmtSwingMarks != null)
				for (int idx = 0; idx < cacheAgigTosRevAmtSwingMarks.Length; idx++)
					if (cacheAgigTosRevAmtSwingMarks[idx] != null && cacheAgigTosRevAmtSwingMarks[idx].AtrReversal == atrReversal && cacheAgigTosRevAmtSwingMarks[idx].EmaLength == emaLength && cacheAgigTosRevAmtSwingMarks[idx].AtrLength == atrLength && cacheAgigTosRevAmtSwingMarks[idx].PercentageReversal == percentageReversal && cacheAgigTosRevAmtSwingMarks[idx].AbsoluteReversal == absoluteReversal && cacheAgigTosRevAmtSwingMarks[idx].TickReversal == tickReversal && cacheAgigTosRevAmtSwingMarks[idx].EnableAlerts == enableAlerts && cacheAgigTosRevAmtSwingMarks[idx].EqualsInput(input))
						return cacheAgigTosRevAmtSwingMarks[idx];
			return CacheIndicator<AgigTosRevAmtSwingMarks>(new AgigTosRevAmtSwingMarks(){ AtrReversal = atrReversal, EmaLength = emaLength, AtrLength = atrLength, PercentageReversal = percentageReversal, AbsoluteReversal = absoluteReversal, TickReversal = tickReversal, EnableAlerts = enableAlerts }, input, ref cacheAgigTosRevAmtSwingMarks);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input , double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(Input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}

		public Indicators.AgigTosRevAmtSwingMarks AgigTosRevAmtSwingMarks(ISeries<double> input , double atrReversal, int emaLength, int atrLength, double percentageReversal, double absoluteReversal, int tickReversal, bool enableAlerts)
		{
			return indicator.AgigTosRevAmtSwingMarks(input, atrReversal, emaLength, atrLength, percentageReversal, absoluteReversal, tickReversal, enableAlerts);
		}
	}
}

#endregion
