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

// One strategy, two engines, gated by AgigRegimeEngine.
// Run on a 5-minute NQ chart. Adds 15-minute bars for mean-revert and daily bars for ADX / 200-SMA.
// ORB (NY): Fade + 2R when the last 20 sessions are directional and the range expands past the opening range.
// Mean-revert (15-min): longs to the opposite band when daily ADX is high and the 200-SMA is rising.
// One position at a time. ORB has priority during NY if its regime is on.

namespace NinjaTrader.NinjaScript.Strategies
{
	public class AgigRegimeSwitchStrategy : Strategy
	{
		private const string OrbLongSignal = "ORB Long";
		private const string OrbShortSignal = "ORB Short";
		private const string MrLongSignal = "MR Long";
		private const string MrShortSignal = "MR Short";

		private AgigRegimeEngine regime;
		private AgigZigzagEngine zz5;
		private AgigZigzagEngine zz15;
		private ATR atr5;
		private ATR atr15;
		private ADX adxDaily;
		private SMA smaDaily;
		private AdvancedSmoothedChannel ch5;
		private AdvancedSmoothedChannel ch15;
		private SimpleFont textFont;

		private bool prevInSession;
		private DateTime prevSessionKey;
		private int barsIntoSession;
		private double orHighBuild;
		private double orLowBuild;
		private bool orLocked;
		private double orHigh;
		private double orLow;
		private int tradesThisSession;
		private int tradedMrShort;
		private int tradedMrLong;
		private string liveSignal;
		private bool prevOrbOn;
		private bool prevMrLong;
		private bool prevMrShort;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Runs AGIG ORB Fade and AGIG mean-revert together. A NY-session trend-day / OR-extension gate turns ORB on. Daily ADX and 200-SMA slope turn mean-revert longs on. Apply to a 5-minute chart.";
				Name = "AGIG Regime Switch";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				StartBehavior = StartBehavior.WaitUntilFlat;
				BarsRequiredToTrade = 300;
				DefaultQuantity = 1;

				UseRegimeFilter = true;
				EnableOrb = true;
				EnableMr = true;
				OrbHasPriorityInNy = true;
				ExitMrForOrb = true;
				ExitWhenRegimeOff = false;

				LookbackSessions = 20;
				MinSessions = 10;
				TrendBody = 0.60;
				OrbOnRate = 0.33;
				OrbOffRate = 0.30;
				OrbOnExtension = 3.0;
				OrbOffExtension = 2.5;
				AdxLength = 14;
				AdxOn = 23;
				AdxOff = 20;
				SmaLength = 200;
				SmaSlopeDays = 5;
				EnableMrShorts = false;

				AtrReversal = 2;
				EmaLength = 5;
				PercentageReversal = 0.01;
				AbsoluteReversal = 0.05;

				OrbSession = AgigOrbSession.NY;
				OrWindow = AgigOrbWindow.Min5;
				UseSafeEntryWindow = true;
				MaxTradesPerSession = 1;
				ExitAtSessionEnd = true;
				UseBandFilter = true;
				BandMode = AgigOrbBandMode.Fade;
				BandLookbackBars = 3;
				OrbChannelLength = 300;
				OrbOuterMult = 2.8;
				RequireAgigDirection = false;
				ExitMode = AgigOrbExit.ScanArrowsStop;
				StopMode = AgigOrbStop.OrOpposite;
				OrbStopBufferTicks = 0;
				TargetRMultiple = 2;
				OrbAtrLength = 5;

				MrAtrLength = 14;
				MrAtrReversal = 1.5;
				MrChannelLength = 200;
				MrOuterMult = 2.0;
				MrChannelTarget = AgigChannelTarget.OppositeBand;
				MrSwingStopBufferTicks = 4;
				MrReverseOnOpposite = true;

				TradeQuantity = 1;
				DrawSignals = true;

				AddPlot(Brushes.DodgerBlue, "TrendDayRate");
				AddPlot(Brushes.Orange, "OrExtension");
				AddPlot(Brushes.Goldenrod, "Adx");
				AddPlot(Brushes.LimeGreen, "OrbOn");
				AddPlot(Brushes.MediumOrchid, "MrLongOn");
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Day, 1);
				BarsRequiredToTrade = Math.Max(300, Math.Max(OrbChannelLength, MrChannelLength));
			}
			else if (State == State.DataLoaded)
			{
				regime = new AgigRegimeEngine();
				zz5 = new AgigZigzagEngine();
				zz15 = new AgigZigzagEngine();
				ApplyRegimeSettings();
				atr5 = ATR(Math.Max(1, OrbAtrLength));
				atr15 = ATR(BarsArray[1], Math.Max(1, MrAtrLength));
				adxDaily = ADX(BarsArray[2], Math.Max(1, AdxLength));
				smaDaily = SMA(BarsArray[2], Math.Max(2, SmaLength));
				ch5 = AdvancedSmoothedChannel(AdvFilterType.SuperSmoother, Math.Max(1, OrbChannelLength), 1.0, OrbOuterMult, DrawSignals);
				ch15 = AdvancedSmoothedChannel(Closes[1], AdvFilterType.SuperSmoother, Math.Max(1, MrChannelLength), 1.0, MrOuterMult, false);
				textFont = new SimpleFont("Arial", 11);
				tradedMrShort = -1;
				tradedMrLong = -1;
				liveSignal = null;
				orHigh = double.NaN;
				orLow = double.NaN;
				if (DrawSignals)
					AddChartIndicator(ch5);
			}
		}

		protected override void OnBarUpdate()
		{
			if (regime == null)
				return;
			ApplyRegimeSettings();

			if (BarsInProgress == 2)
			{
				int slopeAgo = Math.Min(Math.Max(1, SmaSlopeDays), CurrentBar);
				bool ready = CurrentBar >= Math.Max(SmaLength, AdxLength) && smaDaily.IsValidDataPoint(slopeAgo);
				double adx = adxDaily.IsValidDataPoint(0) ? adxDaily[0] : 0;
				regime.UpdateDaily(adx, ready ? smaDaily[0] : 0, ready ? smaDaily[slopeAgo] : 0, ready);
				return;
			}

			if (BarsInProgress == 1)
			{
				RunMeanRevert();
				return;
			}

			if (BarsInProgress != 0)
				return;

			if (Position.MarketPosition == MarketPosition.Flat)
				liveSignal = null;

			int aggMin = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Math.Max(1, BarsPeriod.Value) : 1;
			DateTime barOpen = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Time[0].AddMinutes(-aggMin) : Time[0];
			int orMin = OrMinutes(aggMin);
			regime.UpdateSessionBar(barOpen, aggMin, orMin, Open[0], High[0], Low[0], Close[0]);

			double atr = atr5.IsValidDataPoint(0) ? atr5[0] : 0;
			zz5.Update(CurrentBar, High[0], Low[0], Close[0], atr, EmaLength, AtrReversal, PercentageReversal, AbsoluteReversal);

			RunOpeningRange(barOpen, aggMin, orMin);
			PlotRegime();
			LogRegimeChange();
			RunOrb(barOpen, aggMin, orMin);
		}

		private void RunOpeningRange(DateTime barOpen, int aggMin, int orMin)
		{
			int t = barOpen.Hour * 60 + barOpen.Minute;
			int sessStart = 570;
			int sessEnd = 960;
			bool inSession = t >= sessStart && t < sessEnd;
			DateTime sessionKey = barOpen.Date;
			bool newSession = inSession && (!prevInSession || sessionKey != prevSessionKey);
			int barsInOr = Math.Max(1, (int)Math.Round(orMin / (double)aggMin, MidpointRounding.AwayFromZero));

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

			bool buildingOr = inSession && barsIntoSession <= barsInOr;
			if (buildingOr && !newSession)
			{
				orHighBuild = Math.Max(orHighBuild, High[0]);
				orLowBuild = Math.Min(orLowBuild, Low[0]);
			}
			if (inSession && barsIntoSession > barsInOr && !orLocked)
			{
				orLocked = true;
				orHigh = orHighBuild;
				orLow = orLowBuild;
			}

			prevInSession = inSession;
			prevSessionKey = sessionKey;
		}

		private void RunOrb(DateTime barOpen, int aggMin, int orMin)
		{
			int t = barOpen.Hour * 60 + barOpen.Minute;
			bool inSession = t >= 570 && t < 960;
			int minutesToEnd = ((960 - t) % 1440 + 1440) % 1440;
			bool locked = inSession && orLocked;
			bool safeEntry = !UseSafeEntryWindow || (locked && minutesToEnd > orMin * 3.5);
			bool crossUp = locked && CurrentBar > 0 && Close[1] <= orHigh && Close[0] > orHigh;
			bool crossDown = locked && CurrentBar > 0 && Close[1] >= orLow && Close[0] < orLow;
			bool orbLong = crossUp && safeEntry;
			bool orbShort = crossDown && safeEntry;
			bool lastBar = minutesToEnd <= aggMin;

			if (IsOrbTrade())
			{
				string exitName = null;
				bool isLong = Position.MarketPosition == MarketPosition.Long;
				if (ExitAtSessionEnd && (!inSession || lastBar))
					exitName = "Session End";
				else if (ExitWhenRegimeOff && UseRegimeFilter && !regime.OrbOn)
					exitName = "Regime Off";
				else if (ExitMode == AgigOrbExit.AgigFlip && zz5.State == (isLong ? AgigZigzagEngine.Dn : AgigZigzagEngine.Up))
					exitName = "AGIG Flip";
				else if (ExitMode == AgigOrbExit.OrbOpposite && (isLong ? crossDown : crossUp))
					exitName = "ORB Opposite";
				else if (ExitMode == AgigOrbExit.ScanArrowsStop && !(locked && (isLong ? High[0] > orHigh : Low[0] < orLow)))
					exitName = "Scan Stop";

				if (exitName != null)
				{
					if (isLong)
						ExitLong(exitName, liveSignal);
					else
						ExitShort(exitName, liveSignal);
					return;
				}
			}

			bool orbAllowed = EnableOrb && (!UseRegimeFilter || regime.OrbOn);
			if (!orbAllowed || CurrentBar < Math.Max(5, OrbChannelLength) || tradesThisSession >= MaxTradesPerSession)
				return;
			if (Position.MarketPosition != MarketPosition.Flat && !(IsMrTrade() && ExitMrForOrb))
				return;

			if (AllowOrbLong() && orbLong && OrbBandOk(true) && (!RequireAgigDirection || zz5.State == AgigZigzagEngine.Up))
			{
				if (IsMrTrade() && !ExitMrForOrb)
					return;
				ArmOrbStop(true);
				ArmOrbTarget(true);
				EnterLong(Math.Max(1, TradeQuantity), OrbLongSignal);
				liveSignal = OrbLongSignal;
				tradesThisSession++;
			}
			else if (AllowOrbShort() && orbShort && OrbBandOk(false) && (!RequireAgigDirection || zz5.State == AgigZigzagEngine.Dn))
			{
				if (IsMrTrade() && !ExitMrForOrb)
					return;
				ArmOrbStop(false);
				ArmOrbTarget(false);
				EnterShort(Math.Max(1, TradeQuantity), OrbShortSignal);
				liveSignal = OrbShortSignal;
				tradesThisSession++;
			}
		}

		private void RunMeanRevert()
		{
			double atr = atr15.IsValidDataPoint(0) ? atr15[0] : 0;
			zz15.Update(CurrentBar, High[0], Low[0], Close[0], atr, EmaLength, MrAtrReversal, PercentageReversal, AbsoluteReversal);

			if (IsMrTrade())
				RefreshMrTarget();

			if (CurrentBar < Math.Max(5, MrChannelLength))
				return;

			int highBar = zz15.HighBar;
			int lowBar = zz15.LowBar;
			bool shortVisible = highBar >= 0 && highBar < CurrentBar && (zz15.State == AgigZigzagEngine.Up || zz15.FlippedDown);
			bool longVisible = lowBar >= 0 && lowBar < CurrentBar && (zz15.State == AgigZigzagEngine.Dn || zz15.FlippedUp);
			bool shortEntry = shortVisible && highBar != tradedMrShort && (highBar == CurrentBar - 1 || zz15.FlippedDown);
			bool longEntry = longVisible && lowBar != tradedMrLong && (lowBar == CurrentBar - 1 || zz15.FlippedUp);

			bool inNy = Time[0].Hour * 60 + Time[0].Minute >= 570 && Time[0].Hour * 60 + Time[0].Minute < 960;
			bool blockForOrb = UseRegimeFilter && OrbHasPriorityInNy && regime.OrbOn && inNy;
			bool mrLongOk = EnableMr && (!UseRegimeFilter || regime.MrLongOn);
			bool mrShortOk = EnableMr && EnableMrShorts && (!UseRegimeFilter || regime.MrShortOn);

			if (IsOrbTrade())
				return;

			if (shortEntry)
			{
				tradedMrShort = highBar;
				if (mrShortOk && !blockForOrb && MrSwingOutside(highBar, true))
					SubmitMrShort(highBar);
			}
			if (longEntry)
			{
				tradedMrLong = lowBar;
				if (mrLongOk && !blockForOrb && MrSwingOutside(lowBar, false))
					SubmitMrLong(lowBar);
			}
		}

		private void SubmitMrShort(int swingBar)
		{
			if (Position.MarketPosition == MarketPosition.Short)
				return;
			if (Position.MarketPosition == MarketPosition.Long)
			{
				if (!MrReverseOnOpposite)
				{
					ExitLong("Opposite", liveSignal ?? MrLongSignal);
					return;
				}
			}
			if (ExitWhenRegimeOff && UseRegimeFilter && !regime.MrShortOn)
				return;
			if (!ArmMrTarget(true))
				return;
			ArmMrStop(true, swingBar);
			EnterShort(Math.Max(1, TradeQuantity), MrShortSignal);
			liveSignal = MrShortSignal;
		}

		private void SubmitMrLong(int swingBar)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				return;
			if (Position.MarketPosition == MarketPosition.Short)
			{
				if (!MrReverseOnOpposite)
				{
					ExitShort("Opposite", liveSignal ?? MrShortSignal);
					return;
				}
			}
			if (ExitWhenRegimeOff && UseRegimeFilter && !regime.MrLongOn)
				return;
			if (!ArmMrTarget(false))
				return;
			ArmMrStop(false, swingBar);
			EnterLong(Math.Max(1, TradeQuantity), MrLongSignal);
			liveSignal = MrLongSignal;
		}

		private bool MrSwingOutside(int swingBar, bool isShort)
		{
			int ago = CurrentBar - swingBar;
			if (ago < 0 || ago > CurrentBar || ch15 == null)
				return false;
			if (isShort)
				return ch15.R2Plot.IsValidDataPoint(ago) && ch15.R2Plot[ago] > 0 && High[ago] >= ch15.R2Plot[ago];
			return ch15.S2Plot.IsValidDataPoint(ago) && ch15.S2Plot[ago] > 0 && Low[ago] <= ch15.S2Plot[ago];
		}

		private double MrTargetPrice(bool isShort)
		{
			if (MrChannelTarget == AgigChannelTarget.OppositeBand)
				return isShort ? ch15.S2Plot[0] : ch15.R2Plot[0];
			return ch15.MeanPlot[0];
		}

		private bool ArmMrTarget(bool isShort)
		{
			if (ch15 == null || !ch15.MeanPlot.IsValidDataPoint(0))
				return false;
			double target = Instrument.MasterInstrument.RoundToTickSize(MrTargetPrice(isShort));
			if (target <= 0)
				return false;
			if (isShort && target >= Close[0])
				return false;
			if (!isShort && target <= Close[0])
				return false;
			SetProfitTarget(isShort ? MrShortSignal : MrLongSignal, CalculationMode.Price, target);
			return true;
		}

		private void RefreshMrTarget()
		{
			if (ch15 == null || !ch15.MeanPlot.IsValidDataPoint(0) || Position.MarketPosition == MarketPosition.Flat)
				return;
			bool isShort = Position.MarketPosition == MarketPosition.Short;
			double target = Instrument.MasterInstrument.RoundToTickSize(MrTargetPrice(isShort));
			if (target <= 0)
				return;
			if (isShort && Close[0] <= target)
				ExitShort("Channel", liveSignal ?? MrShortSignal);
			else if (!isShort && Close[0] >= target)
				ExitLong("Channel", liveSignal ?? MrLongSignal);
			else
				SetProfitTarget(isShort ? MrShortSignal : MrLongSignal, CalculationMode.Price, target);
		}

		private void ArmMrStop(bool isShort, int swingBar)
		{
			int ago = CurrentBar - swingBar;
			if (ago < 0 || ago > CurrentBar)
				return;
			double buffer = Math.Max(0, MrSwingStopBufferTicks) * TickSize;
			if (isShort)
				SetStopLoss(MrShortSignal, CalculationMode.Price, High[ago] + buffer, false);
			else
				SetStopLoss(MrLongSignal, CalculationMode.Price, Low[ago] - buffer, false);
		}

		private bool OrbBandOk(bool isLong)
		{
			if (!UseBandFilter)
				return true;
			if (ch5 == null)
				return false;
			bool needAbove = (BandMode == AgigOrbBandMode.Breakout) == isLong;
			int lookback = Math.Min(Math.Max(0, BandLookbackBars), CurrentBar);
			for (int i = 0; i <= lookback; i++)
			{
				if (needAbove)
				{
					if (ch5.R2Plot.IsValidDataPoint(i) && ch5.R2Plot[i] > 0 && High[i] >= ch5.R2Plot[i])
						return true;
				}
				else if (ch5.S2Plot.IsValidDataPoint(i) && ch5.S2Plot[i] > 0 && Low[i] <= ch5.S2Plot[i])
				{
					return true;
				}
			}
			return false;
		}

		private void ArmOrbStop(bool isLong)
		{
			string signal = isLong ? OrbLongSignal : OrbShortSignal;
			double buffer = Math.Max(0, OrbStopBufferTicks) * TickSize;
			double stop = double.NaN;
			if (StopMode == AgigOrbStop.SwingPoint)
			{
				int swingBar = isLong ? zz5.LowBar : zz5.HighBar;
				int ago = CurrentBar - swingBar;
				if (swingBar >= 0 && ago >= 0 && ago <= CurrentBar)
					stop = isLong ? Low[ago] - buffer : High[ago] + buffer;
				if (!double.IsNaN(stop) && (isLong ? stop >= Close[0] : stop <= Close[0]))
					stop = double.NaN;
			}
			if (double.IsNaN(stop))
				stop = isLong ? orLow - buffer : orHigh + buffer;
			SetStopLoss(signal, CalculationMode.Price, Instrument.MasterInstrument.RoundToTickSize(stop), false);
		}

		private void ArmOrbTarget(bool isLong)
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
			SetProfitTarget(isLong ? OrbLongSignal : OrbShortSignal, CalculationMode.Price, Instrument.MasterInstrument.RoundToTickSize(target));
		}

		private bool IsOrbTrade()
		{
			return Position.MarketPosition != MarketPosition.Flat && liveSignal != null && liveSignal.StartsWith("ORB");
		}

		private bool IsMrTrade()
		{
			return Position.MarketPosition != MarketPosition.Flat && liveSignal != null && liveSignal.StartsWith("MR");
		}

		private bool AllowOrbLong()
		{
			return true;
		}

		private bool AllowOrbShort()
		{
			return true;
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

		private void PlotRegime()
		{
			Values[0][0] = regime.TrendDayRate;
			Values[1][0] = regime.MedianExtension;
			Values[2][0] = regime.Adx;
			Values[3][0] = regime.OrbOn ? 1 : 0;
			Values[4][0] = regime.MrLongOn ? 1 : 0;
			if (!DrawSignals)
				return;
			Draw.TextFixed(this, "AgigSwitch",
				string.Format("ORB {0}  |  MR {1}  |  trend-day {2:P0} of {3}  |  OR ext {4:0.0}x  |  ADX {5:0.0}  |  SMA200 {6}  |  pos {7}",
					regime.OrbOn ? "ON" : "off",
					regime.MrLongOn ? "long" : (regime.MrShortOn ? "short" : "off"),
					double.IsNaN(regime.TrendDayRate) ? 0 : regime.TrendDayRate,
					regime.Sessions,
					double.IsNaN(regime.MedianExtension) ? 0 : regime.MedianExtension,
					regime.Adx,
					regime.SmaSlope > 0 ? "up" : (regime.SmaSlope < 0 ? "down" : "flat"),
					liveSignal ?? "flat"),
				TextPosition.TopLeft, Brushes.White, textFont, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void LogRegimeChange()
		{
			if (regime.OrbOn != prevOrbOn || regime.MrLongOn != prevMrLong || regime.MrShortOn != prevMrShort)
			{
				Print(string.Format("{0} regime  ORB={1}  MR long={2}  MR short={3}  trend-day={4:P0}  ext={5:0.00}  ADX={6:0.0}",
					Time[0], regime.OrbOn, regime.MrLongOn, regime.MrShortOn, regime.TrendDayRate, regime.MedianExtension, regime.Adx));
				prevOrbOn = regime.OrbOn;
				prevMrLong = regime.MrLongOn;
				prevMrShort = regime.MrShortOn;
			}
		}

		private void ApplyRegimeSettings()
		{
			regime.Lookback = LookbackSessions;
			regime.MinSessions = MinSessions;
			regime.TrendBody = TrendBody;
			regime.OrbOnRate = OrbOnRate;
			regime.OrbOffRate = OrbOffRate;
			regime.OrbOnExt = OrbOnExtension;
			regime.OrbOffExt = OrbOffExtension;
			regime.AdxOn = AdxOn;
			regime.AdxOff = AdxOff;
			regime.EnableMrShorts = EnableMrShorts;
		}

		protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
		{
			if (marketPosition == MarketPosition.Flat)
				liveSignal = null;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Use Regime Filter", GroupName = "0. Switch", Order = 1)]
		public bool UseRegimeFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable ORB", GroupName = "0. Switch", Order = 2)]
		public bool EnableOrb { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Mean-Revert", GroupName = "0. Switch", Order = 3)]
		public bool EnableMr { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ORB Has Priority In NY", Description = "When the ORB regime is on, skip new mean-revert entries during 09:30-16:00.", GroupName = "0. Switch", Order = 4)]
		public bool OrbHasPriorityInNy { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit MR For ORB", GroupName = "0. Switch", Order = 5)]
		public bool ExitMrForOrb { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit When Regime Off", GroupName = "0. Switch", Order = 6)]
		public bool ExitWhenRegimeOff { get; set; }

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "Lookback Sessions", GroupName = "1. Regime", Order = 1)]
		public int LookbackSessions { get; set; }

		[NinjaScriptProperty]
		[Range(2, 100)]
		[Display(Name = "Min Sessions", GroupName = "1. Regime", Order = 2)]
		public int MinSessions { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 1)]
		[Display(Name = "Trend-Day Body", GroupName = "1. Regime", Order = 3)]
		public double TrendBody { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1)]
		[Display(Name = "ORB On Rate", GroupName = "1. Regime", Order = 4)]
		public double OrbOnRate { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1)]
		[Display(Name = "ORB Off Rate", GroupName = "1. Regime", Order = 5)]
		public double OrbOffRate { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20)]
		[Display(Name = "ORB On Extension", GroupName = "1. Regime", Order = 6)]
		public double OrbOnExtension { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20)]
		[Display(Name = "ORB Off Extension", GroupName = "1. Regime", Order = 7)]
		public double OrbOffExtension { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "ADX Length", GroupName = "1. Regime", Order = 8)]
		public int AdxLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "ADX On", GroupName = "1. Regime", Order = 9)]
		public double AdxOn { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "ADX Off", GroupName = "1. Regime", Order = 10)]
		public double AdxOff { get; set; }

		[NinjaScriptProperty]
		[Range(20, 400)]
		[Display(Name = "SMA Length", GroupName = "1. Regime", Order = 11)]
		public int SmaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, 40)]
		[Display(Name = "SMA Slope Days", GroupName = "1. Regime", Order = 12)]
		public int SmaSlopeDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable MR Shorts", GroupName = "1. Regime", Order = 13)]
		public bool EnableMrShorts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Session", GroupName = "2. ORB", Order = 1)]
		public AgigOrbSession OrbSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "OR Window", GroupName = "2. ORB", Order = 2)]
		public AgigOrbWindow OrWindow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Safe Entry Window", GroupName = "2. ORB", Order = 3)]
		public bool UseSafeEntryWindow { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Max Trades Per Session", GroupName = "2. ORB", Order = 4)]
		public int MaxTradesPerSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit At Session End", GroupName = "2. ORB", Order = 5)]
		public bool ExitAtSessionEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Band Filter", GroupName = "2. ORB", Order = 6)]
		public bool UseBandFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Band Mode", GroupName = "2. ORB", Order = 7)]
		public AgigOrbBandMode BandMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Band Lookback Bars", GroupName = "2. ORB", Order = 8)]
		public int BandLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ORB Channel Length", GroupName = "2. ORB", Order = 9)]
		public int OrbChannelLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "ORB Outer Mult", GroupName = "2. ORB", Order = 10)]
		public double OrbOuterMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require AGIG Direction", GroupName = "2. ORB", Order = 11)]
		public bool RequireAgigDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Exit Mode", GroupName = "2. ORB", Order = 12)]
		public AgigOrbExit ExitMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Mode", GroupName = "2. ORB", Order = 13)]
		public AgigOrbStop StopMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "ORB Stop Buffer Ticks", GroupName = "2. ORB", Order = 14)]
		public int OrbStopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Target R Multiple", GroupName = "2. ORB", Order = 15)]
		public double TargetRMultiple { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "ORB ATR Length", GroupName = "2. ORB", Order = 16)]
		public int OrbAtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MR ATR Length", GroupName = "3. Mean-revert", Order = 1)]
		public int MrAtrLength { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "MR ATR Reversal", GroupName = "3. Mean-revert", Order = 2)]
		public double MrAtrReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MR Channel Length", GroupName = "3. Mean-revert", Order = 3)]
		public int MrChannelLength { get; set; }

		[NinjaScriptProperty]
		[Range(0.001, double.MaxValue)]
		[Display(Name = "MR Outer Mult", GroupName = "3. Mean-revert", Order = 4)]
		public double MrOuterMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "MR Channel Target", GroupName = "3. Mean-revert", Order = 5)]
		public AgigChannelTarget MrChannelTarget { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "MR Swing Stop Buffer Ticks", GroupName = "3. Mean-revert", Order = 6)]
		public int MrSwingStopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "MR Reverse On Opposite", GroupName = "3. Mean-revert", Order = 7)]
		public bool MrReverseOnOpposite { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "ATR Reversal", GroupName = "4. Swing", Order = 1)]
		public double AtrReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA Length", GroupName = "4. Swing", Order = 2)]
		public int EmaLength { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Percentage Reversal", GroupName = "4. Swing", Order = 3)]
		public double PercentageReversal { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Absolute Reversal", GroupName = "4. Swing", Order = 4)]
		public double AbsoluteReversal { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade Quantity", GroupName = "5. Orders", Order = 1)]
		public int TradeQuantity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw Signals", GroupName = "5. Orders", Order = 2)]
		public bool DrawSignals { get; set; }
		#endregion
	}
}
