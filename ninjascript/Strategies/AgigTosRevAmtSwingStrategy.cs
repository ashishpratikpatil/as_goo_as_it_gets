#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// Strategy for AGIG TOS RevAmt Swing Marks.
// RevAmt = close * (0.01 / 100) + ATR(5) * 2 + 0.05
// Enter on the bar after the swing candle. Opposite signal can reverse or only exit.
// Swing stop: long stop is the swing low minus a tick buffer. Short stop is the swing high plus that buffer.
// Channel filter: short only when the swing high is at or above the outer band (2 SD / R2).
// Long only when the swing low is at or below the outer band (2 SD / S2).
// The target is either the channel mean or the opposite outer band.
// The Short label is drawn on the swing-high candle. The Long label is drawn on the swing-low candle.

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum AgigChannelTarget
	{
		Mean,
		OppositeBand
	}

	public class AgigTosRevAmtSwingStrategy : Strategy
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
		private int tradedShortExtreme;
		private int tradedLongExtreme;
		private AdvancedSmoothedChannel channel;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Trades the AGIG TOS RevAmt swing marks on the next bar, only when the swing prints outside the AdvancedSmoothedChannel outer band. Shorts require the swing high at or above R2. Longs require the swing low at or below S2. The target is the channel mean or the opposite outer band. RevAmt = close * (0.01/100) + ATR(5) * 2 + 0.05.";
				Name = "AGIG TOS RevAmt Swing Strategy";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				StartBehavior = StartBehavior.WaitUntilFlat;
				BarsRequiredToTrade = 200;
				DefaultQuantity = 1;

				AtrReversal = 2;
				EmaLength = 5;
				AtrLength = 5;
				PercentageReversal = 0.01;
				AbsoluteReversal = 0.05;
				TradeQuantity = 1;
				StopLossTicks = 0;
				ProfitTargetTicks = 0;
				AllowLong = true;
				AllowShort = true;
				ReverseOnOppositeSignal = true;
				UseSwingStop = true;
				SwingStopBufferTicks = 4;
				DrawSignals = true;
				UseChannelFilter = true;
				ChannelFilterType = AdvFilterType.SuperSmoother;
				ChannelLength = 200;
				ChannelInnerMult = 1.0;
				ChannelOuterMult = 2.415;
				ChannelTarget = AgigChannelTarget.Mean;
			}
			else if (State == State.Configure)
			{
				BarsRequiredToTrade = Math.Max(5, ChannelLength);
				if (!UseSwingStop && StopLossTicks > 0)
					SetStopLoss(CalculationMode.Ticks, StopLossTicks);
				if (!UseChannelFilter && ProfitTargetTicks > 0)
					SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
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
				textFont = new SimpleFont("Arial", 11);
				openShortBar = -1;
				openLongBar = -1;
				tradedShortExtreme = -1;
				tradedLongExtreme = -1;
				channel = AdvancedSmoothedChannel(ChannelFilterType, Math.Max(1, ChannelLength), ChannelInnerMult, ChannelOuterMult, true);
			}
		}

		protected override void OnBarUpdate()
		{
			RefreshChannelTarget();

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
			double atrValue = atrInd.IsValidDataPoint(0) ? atrInd[0] : 0;
			bool canReverse = AtrReversal == 0 || atrInd.IsValidDataPoint(0);
			double reversalDist = closePx * (PercentageReversal / 100.0) + atrValue * AtrReversal + AbsoluteReversal;

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

			// The mark is knowable on the bar after the swing candle. Enter then,
			// not later when price has already traveled the full reversal distance.
			bool shortVisible = highBar >= 0 && highBar < CurrentBar && (st == StUp || flippedDown);
			bool longVisible = lowBar >= 0 && lowBar < CurrentBar && (st == StDn || flippedUp);
			bool shortEntryBar = shortVisible && highBar != tradedShortExtreme && (highBar == CurrentBar - 1 || flippedDown);
			bool longEntryBar = longVisible && lowBar != tradedLongExtreme && (lowBar == CurrentBar - 1 || flippedUp);

			if (CurrentBar >= BarsRequiredToTrade && shortEntryBar)
			{
				tradedShortExtreme = highBar;
				if (SwingOutsideBand(highBar, true))
					SubmitShort(highBar);
			}
			if (CurrentBar >= BarsRequiredToTrade && longEntryBar)
			{
				tradedLongExtreme = lowBar;
				if (SwingOutsideBand(lowBar, false))
					SubmitLong(lowBar);
			}

			if (!DrawSignals)
				return;

			if (flippedDown && highBar >= 0)
				PaintPivot(highBar, true, "AGIG_S" + highBar);
			if (flippedUp && lowBar >= 0)
				PaintPivot(lowBar, false, "AGIG_L" + lowBar);

			int liveShort = st == StUp && highBar >= 0 && highBar < CurrentBar ? highBar : -1;
			int liveLong = st == StDn && lowBar >= 0 && lowBar < CurrentBar ? lowBar : -1;
			MoveOpen(ref openShortBar, liveShort, true, "AGIG_OpenS");
			MoveOpen(ref openLongBar, liveLong, false, "AGIG_OpenL");
		}

		private void SubmitShort(int swingBar)
		{
			if (Position.MarketPosition == MarketPosition.Short)
				return;
			if (Position.MarketPosition == MarketPosition.Long && !ReverseOnOppositeSignal)
			{
				ExitLong("Opposite", "Long");
				return;
			}
			if (!AllowShort)
				return;
			if (!ArmChannelTarget(true))
				return;
			ArmSwingStop(true, swingBar);
			EnterShort(Math.Max(1, TradeQuantity), "Short");
		}

		private void SubmitLong(int swingBar)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				return;
			if (Position.MarketPosition == MarketPosition.Short && !ReverseOnOppositeSignal)
			{
				ExitShort("Opposite", "Short");
				return;
			}
			if (!AllowLong)
				return;
			if (!ArmChannelTarget(false))
				return;
			ArmSwingStop(false, swingBar);
			EnterLong(Math.Max(1, TradeQuantity), "Long");
		}

		private bool ChannelReady()
		{
			return UseChannelFilter && channel != null && channel.MeanPlot.IsValidDataPoint(0) && channel.R2Plot.IsValidDataPoint(0) && channel.S2Plot.IsValidDataPoint(0);
		}

		private bool SwingOutsideBand(int swingBar, bool isShort)
		{
			if (!UseChannelFilter)
				return true;
			int ago = CurrentBar - swingBar;
			if (ago < 0 || ago > CurrentBar || channel == null)
				return false;
			if (isShort)
				return channel.R2Plot.IsValidDataPoint(ago) && channel.R2Plot[ago] > 0 && High[ago] >= channel.R2Plot[ago];
			return channel.S2Plot.IsValidDataPoint(ago) && channel.S2Plot[ago] > 0 && Low[ago] <= channel.S2Plot[ago];
		}

		private double ChannelTargetPrice(bool isShort)
		{
			if (ChannelTarget == AgigChannelTarget.OppositeBand)
				return isShort ? channel.S2Plot[0] : channel.R2Plot[0];
			return channel.MeanPlot[0];
		}

		private bool ArmChannelTarget(bool isShort)
		{
			if (!UseChannelFilter)
				return true;
			if (!ChannelReady())
				return false;
			double target = Instrument.MasterInstrument.RoundToTickSize(ChannelTargetPrice(isShort));
			if (target <= 0)
				return false;
			if (isShort && target >= Close[0])
				return false;
			if (!isShort && target <= Close[0])
				return false;
			SetProfitTarget(isShort ? "Short" : "Long", CalculationMode.Price, target);
			return true;
		}

		private void RefreshChannelTarget()
		{
			if (!ChannelReady())
				return;
			if (Position.MarketPosition == MarketPosition.Short)
			{
				double target = Instrument.MasterInstrument.RoundToTickSize(ChannelTargetPrice(true));
				if (target <= 0)
					return;
				if (Close[0] <= target)
					ExitShort("Channel", "Short");
				else
					SetProfitTarget("Short", CalculationMode.Price, target);
			}
			else if (Position.MarketPosition == MarketPosition.Long)
			{
				double target = Instrument.MasterInstrument.RoundToTickSize(ChannelTargetPrice(false));
				if (target <= 0)
					return;
				if (Close[0] >= target)
					ExitLong("Channel", "Long");
				else
					SetProfitTarget("Long", CalculationMode.Price, target);
			}
		}

		private void ArmSwingStop(bool isShort, int swingBar)
		{
			if (!UseSwingStop)
				return;
			int ago = CurrentBar - swingBar;
			if (ago < 0 || ago > CurrentBar)
				return;
			double buffer = Math.Max(0, SwingStopBufferTicks) * TickSize;
			if (isShort)
				SetStopLoss("Short", CalculationMode.Price, High[ago] + buffer, false);
			else
				SetStopLoss("Long", CalculationMode.Price, Low[ago] - buffer, false);
		}

		private void MoveOpen(ref int paintedBar, int bar, bool isShort, string prefix)
		{
			if (paintedBar == bar)
				return;
			if (paintedBar >= 0)
				ErasePivot(prefix + paintedBar);
			paintedBar = bar;
			if (bar >= 0)
				PaintPivot(bar, isShort, prefix + bar);
		}

		private void PaintPivot(int bar, bool isShort, string tag)
		{
			int ago = CurrentBar - bar;
			if (ago < 0 || ago > CurrentBar)
				return;

			double anchor = isShort ? High[ago] : Low[ago];
			Brush brush = isShort ? Brushes.OrangeRed : Brushes.LimeGreen;
			Draw.Text(this, tag + "tx", false, isShort ? "Short" : "Long", ago, anchor, isShort ? 12 : -12, brush, textFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
			if (isShort)
				Draw.ArrowDown(this, tag + "ar", false, ago, anchor + TickSize, brush);
			else
				Draw.ArrowUp(this, tag + "ar", false, ago, anchor - TickSize, brush);
		}

		private void ErasePivot(string tag)
		{
			RemoveDrawObject(tag + "tx");
			RemoveDrawObject(tag + "ar");
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "ATR Reversal", GroupName = "1. Swing", Order = 1)]
		public double AtrReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA Length", GroupName = "1. Swing", Order = 2)]
		public int EmaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ATR Length", GroupName = "1. Swing", Order = 3)]
		public int AtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Percentage Reversal", Description = "0.01 means 0.01 percent of close.", GroupName = "1. Swing", Order = 4)]
		public double PercentageReversal { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Absolute Reversal", Description = "Dollars added to the reversal. Default 0.05.", GroupName = "1. Swing", Order = 5)]
		public double AbsoluteReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade Quantity", GroupName = "2. Orders", Order = 1)]
		public int TradeQuantity { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Stop Loss Ticks", Description = "Fixed tick stop, used only when Use Swing Stop is off. 0 disables it.", GroupName = "2. Orders", Order = 2)]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Profit Target Ticks", Description = "Fixed tick target, used only when Use Channel Filter is off. 0 disables it.", GroupName = "2. Orders", Order = 3)]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Reverse On Opposite Signal", Description = "On: a Short signal closes a long and enters short, and the other way around. Off: the opposite signal closes the trade and leaves you flat.", GroupName = "2. Orders", Order = 4)]
		public bool ReverseOnOppositeSignal { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Swing Stop", Description = "Long stop is the swing-low candle's low minus the buffer. Short stop is the swing-high candle's high plus the buffer.", GroupName = "2. Orders", Order = 5)]
		public bool UseSwingStop { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Swing Stop Buffer Ticks", Description = "Extra ticks beyond the swing low for longs, and beyond the swing high for shorts.", GroupName = "2. Orders", Order = 6)]
		public int SwingStopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Long", GroupName = "2. Orders", Order = 7)]
		public bool AllowLong { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Short", GroupName = "2. Orders", Order = 8)]
		public bool AllowShort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Signals", GroupName = "3. Visuals", Order = 1)]
		public bool DrawSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Channel Filter", Description = "Only short when the swing high is at or above the outer band, and only long when the swing low is at or below it. The channel target replaces Profit Target Ticks.", GroupName = "4. Channel", Order = 1)]
		public bool UseChannelFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Filter Type", GroupName = "4. Channel", Order = 2)]
		public AdvFilterType ChannelFilterType { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Length", Description = "Same length as AdvancedSmoothedChannel. Default 200.", GroupName = "4. Channel", Order = 3)]
		public int ChannelLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "Inner Multiplier", Description = "Draws the inner band. The trade filter uses the outer band.", GroupName = "4. Channel", Order = 4)]
		public double ChannelInnerMult { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "Outer Multiplier (2 SD)", Description = "R2 and S2. 2.415 matches AdvancedSmoothedChannel. A short needs the swing high at or above R2. A long needs the swing low at or below S2.", GroupName = "4. Channel", Order = 5)]
		public double ChannelOuterMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Channel Target", Description = "Mean exits at the yellow line. OppositeBand exits at the far outer band: S2 for a short, R2 for a long.", GroupName = "4. Channel", Order = 6)]
		public AgigChannelTarget ChannelTarget { get; set; }
		#endregion
	}
}
