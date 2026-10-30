#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ORB + AGIG RevAmt swing direction + AdvancedSmoothedChannel outer band (2 SD / R2 and S2).
// Opening range: builds over the first OR window of the session, then locks (same as the TOS ORB study).
// ORB Long = close crosses above the locked OR high. ORB Short = close crosses below the locked OR low.
// Band Mode Breakout: long needs price at or above R2, short needs price at or below S2.
// Band Mode Fade: long needs price at or below S2, short needs price at or above R2.
// AGIG direction: up after a confirmed Long mark, down after a confirmed Short mark
// (RevAmt = close * (0.01 / 100) + ATR(5) * 2 + 0.05).
// The trade is held while the exit condition has not fired. AgigFlip exits when AGIG prints the opposite confirmed mark.
// Session times are compared with the bar's open time in the chart's time zone.

namespace NinjaTrader.NinjaScript.Strategies
{
	public enum AgigOrbSession
	{
		NY,
		London,
		Asian,
		Globex,
		Custom
	}

	public enum AgigOrbWindow
	{
		Auto,
		Min1,
		Min5,
		Min10,
		Min15,
		Min30,
		Min60
	}

	public enum AgigOrbBandMode
	{
		Breakout,
		Fade
	}

	public enum AgigOrbExit
	{
		AgigFlip,
		OrbOpposite,
		ScanArrowsStop
	}

	public enum AgigOrbStop
	{
		SwingPoint,
		OrOpposite,
		FixedTicks
	}

	public class AgigOrbSd2Strategy : Strategy
	{
		private const int StUndef = 1;
		private const int StUp = 2;
		private const int StDn = 3;
		private const string LongSignal = "ORB Long";
		private const string ShortSignal = "ORB Short";

		private Series<double> emaHighS;
		private Series<double> emaLowS;
		private ATR atrInd;
		private Series<double> stateS;
		private Series<double> maxS;
		private Series<double> minS;
		private Series<double> highBarS;
		private Series<double> lowBarS;
		private AdvancedSmoothedChannel channel;
		private SimpleFont textFont;
		private int openShortBar;
		private int openLongBar;

		private bool prevInSession;
		private DateTime prevSessionKey;
		private int barsIntoSession;
		private double orHighBuild;
		private double orLowBuild;
		private bool orLocked;
		private double orHigh;
		private double orLow;
		private int tradesThisSession;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Opening range breakout filtered by the AdvancedSmoothedChannel outer band (2 SD) and the AGIG TOS RevAmt swing direction. Long on an ORB Long close-cross with price beyond the band for the chosen mode, held until AGIG prints the opposite confirmed mark (or the chosen exit fires).";
				Name = "AGIG ORB SD2 Strategy";
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

				OrbSession = AgigOrbSession.NY;
				CustomStart = 830;
				CustomEnd = 1500;
				OrWindow = AgigOrbWindow.Auto;
				UseSafeEntryWindow = true;
				MaxTradesPerSession = 1;
				ExitAtSessionEnd = true;

				UseBandFilter = true;
				BandMode = AgigOrbBandMode.Breakout;
				BandLookbackBars = 0;
				ChannelFilterType = AdvFilterType.SuperSmoother;
				ChannelLength = 200;
				ChannelInnerMult = 1.0;
				ChannelOuterMult = 2.415;

				TradeQuantity = 1;
				AllowLong = true;
				AllowShort = true;
				RequireAgigDirection = true;
				ExitMode = AgigOrbExit.AgigFlip;
				StopMode = AgigOrbStop.SwingPoint;
				StopBufferTicks = 4;
				StopLossTicks = 0;
				TargetRMultiple = 0;

				DrawSignals = true;

				AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Hash, "OR High");
				AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Hash, "OR Low");
				AddPlot(new Stroke(Brushes.LimeGreen, DashStyleHelper.Dash, 1), PlotStyle.Hash, "Up R");
				AddPlot(new Stroke(Brushes.Red, DashStyleHelper.Dash, 1), PlotStyle.Hash, "Down R");
			}
			else if (State == State.Configure)
			{
				BarsRequiredToTrade = Math.Max(5, ChannelLength);
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
				prevInSession = false;
				prevSessionKey = DateTime.MinValue;
				barsIntoSession = 0;
				orLocked = false;
				orHigh = double.NaN;
				orLow = double.NaN;
				tradesThisSession = 0;
				channel = AdvancedSmoothedChannel(ChannelFilterType, Math.Max(1, ChannelLength), ChannelInnerMult, ChannelOuterMult, true);
				if (DrawSignals)
					AddChartIndicator(channel);
			}
		}

		protected override void OnBarUpdate()
		{
			int st;
			bool flippedUp;
			bool flippedDown;
			UpdateSwing(out st, out flippedUp, out flippedDown);
			int highBar = (int)highBarS[0];
			int lowBar = (int)lowBarS[0];

			int aggMin = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Math.Max(1, BarsPeriod.Value) : 1;
			DateTime barOpen = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Time[0].AddMinutes(-aggMin) : Time[0];
			int t = barOpen.Hour * 60 + barOpen.Minute;
			int sessStart = HhmmToMinutes(SessionStartHhmm());
			int sessEnd = HhmmToMinutes(SessionEndHhmm());
			bool crossesMidnight = sessEnd <= sessStart;
			bool inSession = !crossesMidnight ? (t >= sessStart && t < sessEnd) : (t >= sessStart || t < sessEnd);
			DateTime sessionKey = crossesMidnight && t < sessStart ? barOpen.Date.AddDays(-1) : barOpen.Date;
			bool newSession = inSession && (!prevInSession || sessionKey != prevSessionKey);
			int orMin = OrMinutes(aggMin);
			int barsInOR = Math.Max(1, (int)Math.Round(orMin / (double)aggMin, MidpointRounding.AwayFromZero));

			if (newSession)
			{
				barsIntoSession = 1;
				orHighBuild = High[0];
				orLowBuild = Low[0];
				orLocked = false;
				orHigh = double.NaN;
				orLow = double.NaN;
				tradesThisSession = 0;
			}
			else if (inSession)
			{
				barsIntoSession++;
			}
			else
			{
				barsIntoSession = 0;
				orLocked = false;
				orHigh = double.NaN;
				orLow = double.NaN;
			}

			bool buildingOR = inSession && barsIntoSession <= barsInOR;
			if (buildingOR && !newSession)
			{
				orHighBuild = Math.Max(orHighBuild, High[0]);
				orLowBuild = Math.Min(orLowBuild, Low[0]);
			}
			if (inSession && barsIntoSession > barsInOR && !orLocked)
			{
				orLocked = true;
				orHigh = orHighBuild;
				orLow = orLowBuild;
			}

			prevInSession = inSession;
			prevSessionKey = sessionKey;

			int minutesToEnd = ((sessEnd - t) % 1440 + 1440) % 1440;
			bool locked = inSession && orLocked;
			bool safeEntry = !UseSafeEntryWindow || (locked && minutesToEnd > orMin * 3.5);
			bool crossUp = locked && CurrentBar > 0 && Close[1] <= orHigh && Close[0] > orHigh;
			bool crossDown = locked && CurrentBar > 0 && Close[1] >= orLow && Close[0] < orLow;
			bool orbLong = crossUp && safeEntry;
			bool orbShort = crossDown && safeEntry;

			PlotLevels(inSession, buildingOR, locked);

			bool exited = ManagePosition(st, inSession, locked, crossUp, crossDown, minutesToEnd <= aggMin);

			if (!exited && CurrentBar >= BarsRequiredToTrade && Position.MarketPosition == MarketPosition.Flat && tradesThisSession < MaxTradesPerSession)
			{
				if (AllowLong && orbLong && BandOk(true) && (!RequireAgigDirection || st == StUp))
				{
					ArmStop(true, lowBar);
					ArmTarget(true);
					EnterLong(Math.Max(1, TradeQuantity), LongSignal);
					tradesThisSession++;
				}
				else if (AllowShort && orbShort && BandOk(false) && (!RequireAgigDirection || st == StDn))
				{
					ArmStop(false, highBar);
					ArmTarget(false);
					EnterShort(Math.Max(1, TradeQuantity), ShortSignal);
					tradesThisSession++;
				}
			}

			if (!DrawSignals)
				return;

			if (orbLong)
				Draw.Text(this, "ORB_L" + CurrentBar, false, "ORB Long", 0, Low[0], -24, Brushes.LimeGreen, textFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
			if (orbShort)
				Draw.Text(this, "ORB_S" + CurrentBar, false, "ORB Short", 0, High[0], 24, Brushes.Red, textFont, System.Windows.TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);

			if (flippedDown && highBar >= 0)
				PaintPivot(highBar, true, "AGIG_S" + highBar);
			if (flippedUp && lowBar >= 0)
				PaintPivot(lowBar, false, "AGIG_L" + lowBar);

			int liveShort = st == StUp && highBar >= 0 && highBar < CurrentBar ? highBar : -1;
			int liveLong = st == StDn && lowBar >= 0 && lowBar < CurrentBar ? lowBar : -1;
			MoveOpen(ref openShortBar, liveShort, true, "AGIG_OpenS");
			MoveOpen(ref openLongBar, liveLong, false, "AGIG_OpenL");
		}

		private bool ManagePosition(int st, bool inSession, bool locked, bool crossUp, bool crossDown, bool lastBarOfSession)
		{
			if (Position.MarketPosition == MarketPosition.Flat)
				return false;

			bool isLong = Position.MarketPosition == MarketPosition.Long;
			string exitName = null;

			if (ExitAtSessionEnd && (!inSession || lastBarOfSession))
				exitName = "Session End";
			else if (ExitMode == AgigOrbExit.AgigFlip && st == (isLong ? StDn : StUp))
				exitName = "AGIG Flip";
			else if (ExitMode == AgigOrbExit.OrbOpposite && (isLong ? crossDown : crossUp))
				exitName = "ORB Opposite";
			else if (ExitMode == AgigOrbExit.ScanArrowsStop && !(locked && (isLong ? High[0] > orHigh : Low[0] < orLow)))
				exitName = "Scan Stop";

			if (exitName == null)
				return false;

			if (isLong)
				ExitLong(exitName, LongSignal);
			else
				ExitShort(exitName, ShortSignal);
			return true;
		}

		private bool BandOk(bool isLong)
		{
			if (!UseBandFilter)
				return true;
			if (channel == null)
				return false;

			bool needAbove = (BandMode == AgigOrbBandMode.Breakout) == isLong;
			int lookback = Math.Min(Math.Max(0, BandLookbackBars), CurrentBar);
			for (int i = 0; i <= lookback; i++)
			{
				if (needAbove)
				{
					if (channel.R2Plot.IsValidDataPoint(i) && channel.R2Plot[i] > 0 && High[i] >= channel.R2Plot[i])
						return true;
				}
				else if (channel.S2Plot.IsValidDataPoint(i) && channel.S2Plot[i] > 0 && Low[i] <= channel.S2Plot[i])
				{
					return true;
				}
			}
			return false;
		}

		private void ArmStop(bool isLong, int swingBar)
		{
			string signal = isLong ? LongSignal : ShortSignal;
			if (StopMode == AgigOrbStop.FixedTicks)
			{
				if (StopLossTicks > 0)
					SetStopLoss(signal, CalculationMode.Ticks, StopLossTicks, false);
				return;
			}

			double buffer = Math.Max(0, StopBufferTicks) * TickSize;
			double stop = double.NaN;
			if (StopMode == AgigOrbStop.SwingPoint)
			{
				int ago = CurrentBar - swingBar;
				if (swingBar >= 0 && ago >= 0 && ago <= CurrentBar)
					stop = isLong ? Low[ago] - buffer : High[ago] + buffer;
				if (!double.IsNaN(stop) && (isLong ? stop >= Close[0] : stop <= Close[0]))
					stop = double.NaN;
			}
			// The opposite side of the range is always behind an ORB cross, so it backs up a bad swing stop.
			if (double.IsNaN(stop))
				stop = isLong ? orLow - buffer : orHigh + buffer;

			SetStopLoss(signal, CalculationMode.Price, Instrument.MasterInstrument.RoundToTickSize(stop), false);
		}

		private void ArmTarget(bool isLong)
		{
			if (TargetRMultiple <= 0)
				return;
			double range = orHigh - orLow;
			if (!(range > 0))
				return;

			double step = range * TargetRMultiple;
			double target = isLong ? orHigh + step : orLow - step;
			while (isLong ? target <= Close[0] : target >= Close[0])
				target += isLong ? step : -step;

			SetProfitTarget(isLong ? LongSignal : ShortSignal, CalculationMode.Price, Instrument.MasterInstrument.RoundToTickSize(target));
		}

		private void PlotLevels(bool inSession, bool buildingOR, bool locked)
		{
			if (buildingOR)
			{
				Values[0][0] = orHighBuild;
				Values[1][0] = orLowBuild;
				return;
			}
			if (!locked)
				return;

			Values[0][0] = orHigh;
			Values[1][0] = orLow;
			double range = orHigh - orLow;
			if (range > 0)
			{
				double mult = TargetRMultiple > 0 ? TargetRMultiple : 1.0;
				Values[2][0] = orHigh + range * mult;
				Values[3][0] = orLow - range * mult;
			}
		}

		private void UpdateSwing(out int st, out bool flippedUp, out bool flippedDown)
		{
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

			flippedDown = CurrentBar > 0 && (int)stateS[1] == StUp && st == StDn;
			flippedUp = CurrentBar > 0 && (int)stateS[1] == StDn && st == StUp;
		}

		private int SessionStartHhmm()
		{
			switch (OrbSession)
			{
				case AgigOrbSession.NY: return 930;
				case AgigOrbSession.London: return 300;
				case AgigOrbSession.Asian: return 1900;
				case AgigOrbSession.Globex: return 1800;
				default: return CustomStart;
			}
		}

		private int SessionEndHhmm()
		{
			switch (OrbSession)
			{
				case AgigOrbSession.NY: return 1600;
				case AgigOrbSession.London: return 1200;
				case AgigOrbSession.Asian: return 400;
				case AgigOrbSession.Globex: return 1700;
				default: return CustomEnd;
			}
		}

		private static int HhmmToMinutes(int hhmm)
		{
			return (hhmm / 100) * 60 + hhmm % 100;
		}

		private int OrMinutes(int aggMin)
		{
			switch (OrWindow)
			{
				case AgigOrbWindow.Min1: return 1;
				case AgigOrbWindow.Min5: return 5;
				case AgigOrbWindow.Min10: return 10;
				case AgigOrbWindow.Min15: return 15;
				case AgigOrbWindow.Min30: return 30;
				case AgigOrbWindow.Min60: return 60;
				default: return aggMin;
			}
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
		[Display(Name = "Session", Description = "NY 0930-1600, London 0300-1200, Asian 1900-0400, Globex 1800-1700, or Custom. Times are in the chart's time zone.", GroupName = "2. ORB", Order = 1)]
		public AgigOrbSession OrbSession { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Custom Start (HHmm)", GroupName = "2. ORB", Order = 2)]
		public int CustomStart { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Custom End (HHmm)", GroupName = "2. ORB", Order = 3)]
		public int CustomEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "OR Window", Description = "Auto uses one chart bar, like the TOS study.", GroupName = "2. ORB", Order = 4)]
		public AgigOrbWindow OrWindow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Safe Entry Window", Description = "No new entries in the last 3.5 OR windows of the session.", GroupName = "2. ORB", Order = 5)]
		public bool UseSafeEntryWindow { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max Trades Per Session", GroupName = "2. ORB", Order = 6)]
		public int MaxTradesPerSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit At Session End", GroupName = "2. ORB", Order = 7)]
		public bool ExitAtSessionEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Band Filter", Description = "Off trades every ORB signal that passes the AGIG direction check.", GroupName = "3. Channel (2 SD)", Order = 1)]
		public bool UseBandFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Band Mode", Description = "Breakout: long at or above R2, short at or below S2. Fade: long at or below S2, short at or above R2.", GroupName = "3. Channel (2 SD)", Order = 2)]
		public AgigOrbBandMode BandMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Band Lookback Bars", Description = "0 = the ORB signal bar must touch the band. N = the band touch can be up to N bars earlier.", GroupName = "3. Channel (2 SD)", Order = 3)]
		public int BandLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Filter Type", GroupName = "3. Channel (2 SD)", Order = 4)]
		public AdvFilterType ChannelFilterType { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Length", Description = "Same length as AdvancedSmoothedChannel. Default 200.", GroupName = "3. Channel (2 SD)", Order = 5)]
		public int ChannelLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "Inner Multiplier", Description = "Draws the inner band. The filter uses the outer band.", GroupName = "3. Channel (2 SD)", Order = 6)]
		public double ChannelInnerMult { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "Outer Multiplier (2 SD)", Description = "R2 and S2. 2.415 matches AdvancedSmoothedChannel.", GroupName = "3. Channel (2 SD)", Order = 7)]
		public double ChannelOuterMult { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade Quantity", GroupName = "4. Orders", Order = 1)]
		public int TradeQuantity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Long", GroupName = "4. Orders", Order = 2)]
		public bool AllowLong { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Short", GroupName = "4. Orders", Order = 3)]
		public bool AllowShort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require AGIG Direction", Description = "Long only after a confirmed AGIG Long mark (direction up). Short only after a confirmed AGIG Short mark.", GroupName = "4. Orders", Order = 4)]
		public bool RequireAgigDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit Mode", Description = "AgigFlip: hold until AGIG confirms the opposite mark. OrbOpposite: hold until the opposite ORB cross. ScanArrowsStop: exit on the first bar that is no longer beyond the OR level.", GroupName = "4. Orders", Order = 5)]
		public AgigOrbExit ExitMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Mode", Description = "SwingPoint: last AGIG swing low/high plus the buffer, falling back to the opposite OR side. OrOpposite: OR low for longs, OR high for shorts. FixedTicks: Stop Loss Ticks.", GroupName = "4. Orders", Order = 6)]
		public AgigOrbStop StopMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Stop Buffer Ticks", GroupName = "4. Orders", Order = 7)]
		public int StopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Stop Loss Ticks", Description = "Used only by Stop Mode FixedTicks. 0 = no stop.", GroupName = "4. Orders", Order = 8)]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Target R Multiple", Description = "0 = no target. 1 = OR high + 1 x range for longs (the TOS R1 line).", GroupName = "4. Orders", Order = 9)]
		public double TargetRMultiple { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Signals", GroupName = "5. Visuals", Order = 1)]
		public bool DrawSignals { get; set; }
		#endregion
	}
}
