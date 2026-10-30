#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript
{
	/// <summary>
	/// Rolling NY-session trend-day rate and opening-range extension, plus daily ADX / 200-SMA slope.
	/// ORB turns on when the last N sessions trend often and the range expands past the opening range.
	/// Mean-revert longs turn on when ADX is high and the 200-SMA is rising; shorts need a falling 200-SMA.
	/// </summary>
	public class AgigRegimeEngine
	{
		private readonly List<double> trendDays = new List<double>();
		private readonly List<double> extensions = new List<double>();
		private bool prevInNy;
		private DateTime prevKey = DateTime.MinValue;
		private int barsInto;
		private double rthOpen;
		private double rthHigh;
		private double rthLow;
		private double rthClose;
		private double orHighBuild;
		private double orLowBuild;
		private bool orLocked;
		private double orHigh;
		private double orLow;
		private bool sessionOpen;
		private bool orbLatched;
		private bool mrLongLatched;
		private bool mrShortLatched;

		public int Lookback { get; set; }
		public int MinSessions { get; set; }
		public double TrendBody { get; set; }
		public double OrbOnRate { get; set; }
		public double OrbOffRate { get; set; }
		public double OrbOnExt { get; set; }
		public double OrbOffExt { get; set; }
		public double AdxOn { get; set; }
		public double AdxOff { get; set; }
		public bool EnableMrShorts { get; set; }

		public double TrendDayRate { get; private set; }
		public double MedianExtension { get; private set; }
		public double Adx { get; private set; }
		public double SmaSlope { get; private set; }
		public bool OrbOn { get { return orbLatched; } }
		public bool MrLongOn { get { return mrLongLatched; } }
		public bool MrShortOn { get { return mrShortLatched; } }
		public int Sessions { get { return trendDays.Count; } }

		public AgigRegimeEngine()
		{
			Lookback = 20;
			MinSessions = 10;
			TrendBody = 0.60;
			OrbOnRate = 0.33;
			OrbOffRate = 0.30;
			OrbOnExt = 3.0;
			OrbOffExt = 2.5;
			AdxOn = 23;
			AdxOff = 20;
		}

		public void UpdateSessionBar(DateTime barOpen, int aggMin, int orMin, double o, double h, double l, double c)
		{
			int t = barOpen.Hour * 60 + barOpen.Minute;
			bool inNy = t >= 570 && t < 960;
			DateTime key = barOpen.Date;
			bool newSession = inNy && (!prevInNy || key != prevKey);

			if (newSession)
			{
				if (sessionOpen)
					FinalizeSession();
				sessionOpen = true;
				barsInto = 1;
				rthOpen = o;
				rthHigh = h;
				rthLow = l;
				rthClose = c;
				orHighBuild = h;
				orLowBuild = l;
				orLocked = false;
				orHigh = double.NaN;
				orLow = double.NaN;
			}
			else if (inNy)
			{
				barsInto++;
				rthHigh = Math.Max(rthHigh, h);
				rthLow = Math.Min(rthLow, l);
				rthClose = c;
			}
			else if (sessionOpen)
			{
				FinalizeSession();
				sessionOpen = false;
				barsInto = 0;
			}

			int barsInOr = Math.Max(1, (int)Math.Round(orMin / (double)Math.Max(1, aggMin), MidpointRounding.AwayFromZero));
			bool building = inNy && barsInto <= barsInOr;
			if (building && !newSession)
			{
				orHighBuild = Math.Max(orHighBuild, h);
				orLowBuild = Math.Min(orLowBuild, l);
			}
			if (inNy && barsInto > barsInOr && !orLocked)
			{
				orLocked = true;
				orHigh = orHighBuild;
				orLow = orLowBuild;
			}

			prevInNy = inNy;
			prevKey = key;
			RefreshOrbLatch();
		}

		public void UpdateDaily(double adxValue, double smaNow, double smaThen, bool ready)
		{
			Adx = adxValue;
			SmaSlope = smaNow - smaThen;
			if (!ready)
			{
				mrLongLatched = false;
				mrShortLatched = false;
				return;
			}

			bool up = SmaSlope > 0;
			bool dn = SmaSlope < 0;
			if (mrLongLatched)
				mrLongLatched = Adx >= AdxOff && up;
			else
				mrLongLatched = Adx >= AdxOn && up;

			if (!EnableMrShorts)
			{
				mrShortLatched = false;
				return;
			}
			if (mrShortLatched)
				mrShortLatched = Adx >= AdxOff && dn;
			else
				mrShortLatched = Adx >= AdxOn && dn;
		}

		private void FinalizeSession()
		{
			double rng = rthHigh - rthLow;
			if (!(rng > 0))
				return;

			double body = Math.Abs(rthClose - rthOpen);
			Push(trendDays, body / rng >= TrendBody ? 1.0 : 0.0);

			double orRng = orHigh - orLow;
			if (!(orRng > 0) && orHighBuild > orLowBuild)
				orRng = orHighBuild - orLowBuild;
			if (orRng > 0)
				Push(extensions, rng / orRng);

			TrendDayRate = Mean(trendDays);
			MedianExtension = Median(extensions);
		}

		private void RefreshOrbLatch()
		{
			if (trendDays.Count < Math.Max(1, MinSessions))
			{
				orbLatched = false;
				return;
			}

			double rate = TrendDayRate;
			double ext = MedianExtension;
			bool extReady = !double.IsNaN(ext);
			if (orbLatched)
				orbLatched = rate >= OrbOffRate && (!extReady || ext >= OrbOffExt);
			else
				orbLatched = rate >= OrbOnRate && (!extReady || ext >= OrbOnExt);
		}

		private void Push(List<double> xs, double v)
		{
			xs.Add(v);
			int cap = Math.Max(2, Lookback);
			while (xs.Count > cap)
				xs.RemoveAt(0);
		}

		private static double Mean(List<double> xs)
		{
			if (xs.Count == 0)
				return double.NaN;
			double s = 0;
			for (int i = 0; i < xs.Count; i++)
				s += xs[i];
			return s / xs.Count;
		}

		private static double Median(List<double> xs)
		{
			int n = xs.Count;
			if (n == 0)
				return double.NaN;
			double[] a = xs.ToArray();
			Array.Sort(a);
			if ((n & 1) == 1)
				return a[n / 2];
			return 0.5 * (a[n / 2 - 1] + a[n / 2]);
		}
	}

	public class AgigZigzagEngine
	{
		public const int Undef = 1;
		public const int Up = 2;
		public const int Dn = 3;

		public int State = Undef;
		public int HighBar = -1;
		public int LowBar = -1;
		public bool FlippedUp;
		public bool FlippedDown;
		private double emaH;
		private double emaL;
		private double maxP;
		private double minP;
		private bool seeded;

		public void Update(int bar, double h, double l, double c, double atr, int emaLen, double atrRev, double pctRev, double absRev)
		{
			FlippedUp = false;
			FlippedDown = false;
			double k = 2.0 / (Math.Max(1, emaLen) + 1.0);
			if (!seeded)
			{
				emaH = h;
				emaL = l;
				maxP = emaH;
				minP = emaL;
				State = Undef;
				HighBar = -1;
				LowBar = -1;
				seeded = true;
				return;
			}

			int prior = State;
			emaH = h * k + emaH * (1.0 - k);
			emaL = l * k + emaL * (1.0 - k);
			double closePx = c > 0 ? c : emaH;
			double rd = closePx * (pctRev / 100.0) + atr * atrRev + absRev;
			bool newMax = false;
			bool newMin = false;

			if (prior == Undef)
			{
				if (emaH >= maxP)
				{
					State = Up;
					maxP = emaH;
					newMax = true;
				}
				else if (emaL <= minP)
				{
					State = Dn;
					minP = emaL;
					newMin = true;
				}
			}
			else if (prior == Up)
			{
				if (emaL <= maxP - rd)
				{
					State = Dn;
					minP = emaL;
					newMin = true;
				}
				else if (emaH >= maxP)
				{
					maxP = emaH;
					newMax = true;
				}
			}
			else
			{
				if (emaH >= minP + rd)
				{
					State = Up;
					maxP = emaH;
					newMax = true;
				}
				else if (emaL <= minP)
				{
					minP = emaL;
					newMin = true;
				}
			}

			if (newMax)
				HighBar = bar;
			if (newMin)
				LowBar = bar;
			FlippedDown = prior == Up && State == Dn;
			FlippedUp = prior == Dn && State == Up;
		}
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class AgigRegimeFilter : Indicator
	{
		private AgigRegimeEngine engine;
		private ADX adxInd;
		private SMA smaInd;
		private SimpleFont font;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "NY-session trend-day rate and opening-range extension, with daily ADX and 200-SMA slope. ORB On when sessions trend and expand. Mean-revert longs when ADX is high and the 200-SMA is rising.";
				Name = "AGIG Regime Filter";
				Calculate = Calculate.OnBarClose;
				IsOverlay = false;
				DisplayInDataBox = true;
				DrawOnPricePanel = false;
				IsSuspendedWhileInactive = true;

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
				OrMinutes = 5;
				EnableMrShorts = false;

				AddPlot(Brushes.DodgerBlue, "TrendDayRate");
				AddPlot(Brushes.Orange, "OrExtension");
				AddPlot(Brushes.Goldenrod, "Adx");
				AddPlot(Brushes.LimeGreen, "OrbOn");
				AddPlot(Brushes.MediumOrchid, "MrLongOn");
				AddPlot(Brushes.IndianRed, "MrShortOn");
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Day, 1);
			}
			else if (State == State.DataLoaded)
			{
				engine = new AgigRegimeEngine();
				ApplySettings();
				adxInd = ADX(BarsArray[1], Math.Max(1, AdxLength));
				smaInd = SMA(BarsArray[1], Math.Max(2, SmaLength));
				font = new Gui.Tools.SimpleFont("Arial", 11);
			}
		}

		protected override void OnBarUpdate()
		{
			if (engine == null)
				return;
			ApplySettings();

			if (BarsInProgress == 1)
			{
				int slopeAgo = Math.Min(Math.Max(1, SmaSlopeDays), CurrentBar);
				bool ready = CurrentBar >= Math.Max(SmaLength, AdxLength) && smaInd.IsValidDataPoint(slopeAgo);
				double adx = adxInd.IsValidDataPoint(0) ? adxInd[0] : 0;
				double sma0 = ready ? smaInd[0] : 0;
				double smaN = ready ? smaInd[slopeAgo] : 0;
				engine.UpdateDaily(adx, sma0, smaN, ready);
				return;
			}

			if (BarsInProgress != 0 || CurrentBar < 1)
				return;

			int aggMin = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Math.Max(1, BarsPeriod.Value) : 1;
			DateTime barOpen = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? Time[0].AddMinutes(-aggMin) : Time[0];
			engine.UpdateSessionBar(barOpen, aggMin, Math.Max(1, OrMinutes), Open[0], High[0], Low[0], Close[0]);

			Values[0][0] = engine.TrendDayRate;
			Values[1][0] = engine.MedianExtension;
			Values[2][0] = engine.Adx;
			Values[3][0] = engine.OrbOn ? 1 : 0;
			Values[4][0] = engine.MrLongOn ? 1 : 0;
			Values[5][0] = engine.MrShortOn ? 1 : 0;

			Draw.TextFixed(this, "AgigRegime",
				string.Format("ORB {0}  |  MR {1}  |  trend-day {2:P0}  |  OR ext {3:0.0}x  |  ADX {4:0.0}  |  SMA200 {5}",
					engine.OrbOn ? "ON" : "off",
					engine.MrLongOn ? "long" : (engine.MrShortOn ? "short" : "off"),
					double.IsNaN(engine.TrendDayRate) ? 0 : engine.TrendDayRate,
					double.IsNaN(engine.MedianExtension) ? 0 : engine.MedianExtension,
					engine.Adx,
					engine.SmaSlope > 0 ? "up" : (engine.SmaSlope < 0 ? "down" : "flat")),
				TextPosition.TopLeft, Brushes.White, font, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void ApplySettings()
		{
			engine.Lookback = LookbackSessions;
			engine.MinSessions = MinSessions;
			engine.TrendBody = TrendBody;
			engine.OrbOnRate = OrbOnRate;
			engine.OrbOffRate = OrbOffRate;
			engine.OrbOnExt = OrbOnExtension;
			engine.OrbOffExt = OrbOffExtension;
			engine.AdxOn = AdxOn;
			engine.AdxOff = AdxOff;
			engine.EnableMrShorts = EnableMrShorts;
		}

		#region Properties
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
		[Display(Name = "Trend-Day Body", Description = "|RTH close-open| / (high-low). 0.60 is a directional day.", GroupName = "1. Regime", Order = 3)]
		public double TrendBody { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1)]
		[Display(Name = "ORB On Rate", GroupName = "2. ORB gate", Order = 1)]
		public double OrbOnRate { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1)]
		[Display(Name = "ORB Off Rate", GroupName = "2. ORB gate", Order = 2)]
		public double OrbOffRate { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20)]
		[Display(Name = "ORB On Extension", Description = "Median RTH range / opening range needed to turn ORB on.", GroupName = "2. ORB gate", Order = 3)]
		public double OrbOnExtension { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 20)]
		[Display(Name = "ORB Off Extension", GroupName = "2. ORB gate", Order = 4)]
		public double OrbOffExtension { get; set; }

		[NinjaScriptProperty]
		[Range(1, 120)]
		[Display(Name = "OR Minutes", GroupName = "2. ORB gate", Order = 5)]
		public int OrMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "ADX Length", GroupName = "3. Mean-revert gate", Order = 1)]
		public int AdxLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "ADX On", GroupName = "3. Mean-revert gate", Order = 2)]
		public double AdxOn { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "ADX Off", GroupName = "3. Mean-revert gate", Order = 3)]
		public double AdxOff { get; set; }

		[NinjaScriptProperty]
		[Range(20, 400)]
		[Display(Name = "SMA Length", GroupName = "3. Mean-revert gate", Order = 4)]
		public int SmaLength { get; set; }

		[NinjaScriptProperty]
		[Range(1, 40)]
		[Display(Name = "SMA Slope Days", GroupName = "3. Mean-revert gate", Order = 5)]
		public int SmaSlopeDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable MR Shorts", GroupName = "3. Mean-revert gate", Order = 6)]
		public bool EnableMrShorts { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AgigRegimeFilter[] cacheAgigRegimeFilter;
		public AgigRegimeFilter AgigRegimeFilter(int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			return AgigRegimeFilter(Input, lookbackSessions, minSessions, trendBody, orbOnRate, orbOffRate, orbOnExtension, orbOffExtension, orMinutes, adxLength, adxOn, adxOff, smaLength, smaSlopeDays, enableMrShorts);
		}

		public AgigRegimeFilter AgigRegimeFilter(ISeries<double> input, int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			if (cacheAgigRegimeFilter != null)
				for (int idx = 0; idx < cacheAgigRegimeFilter.Length; idx++)
					if (cacheAgigRegimeFilter[idx] != null && cacheAgigRegimeFilter[idx].LookbackSessions == lookbackSessions && cacheAgigRegimeFilter[idx].MinSessions == minSessions && cacheAgigRegimeFilter[idx].TrendBody == trendBody && cacheAgigRegimeFilter[idx].OrbOnRate == orbOnRate && cacheAgigRegimeFilter[idx].OrbOffRate == orbOffRate && cacheAgigRegimeFilter[idx].OrbOnExtension == orbOnExtension && cacheAgigRegimeFilter[idx].OrbOffExtension == orbOffExtension && cacheAgigRegimeFilter[idx].OrMinutes == orMinutes && cacheAgigRegimeFilter[idx].AdxLength == adxLength && cacheAgigRegimeFilter[idx].AdxOn == adxOn && cacheAgigRegimeFilter[idx].AdxOff == adxOff && cacheAgigRegimeFilter[idx].SmaLength == smaLength && cacheAgigRegimeFilter[idx].SmaSlopeDays == smaSlopeDays && cacheAgigRegimeFilter[idx].EnableMrShorts == enableMrShorts && cacheAgigRegimeFilter[idx].EqualsInput(input))
						return cacheAgigRegimeFilter[idx];
			return CacheIndicator<AgigRegimeFilter>(new AgigRegimeFilter(){ LookbackSessions = lookbackSessions, MinSessions = minSessions, TrendBody = trendBody, OrbOnRate = orbOnRate, OrbOffRate = orbOffRate, OrbOnExtension = orbOnExtension, OrbOffExtension = orbOffExtension, OrMinutes = orMinutes, AdxLength = adxLength, AdxOn = adxOn, AdxOff = adxOff, SmaLength = smaLength, SmaSlopeDays = smaSlopeDays, EnableMrShorts = enableMrShorts }, input, ref cacheAgigRegimeFilter);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AgigRegimeFilter AgigRegimeFilter(int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			return indicator.AgigRegimeFilter(Input, lookbackSessions, minSessions, trendBody, orbOnRate, orbOffRate, orbOnExtension, orbOffExtension, orMinutes, adxLength, adxOn, adxOff, smaLength, smaSlopeDays, enableMrShorts);
		}

		public Indicators.AgigRegimeFilter AgigRegimeFilter(ISeries<double> input , int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			return indicator.AgigRegimeFilter(input, lookbackSessions, minSessions, trendBody, orbOnRate, orbOffRate, orbOnExtension, orbOffExtension, orMinutes, adxLength, adxOn, adxOff, smaLength, smaSlopeDays, enableMrShorts);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AgigRegimeFilter AgigRegimeFilter(int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			return indicator.AgigRegimeFilter(Input, lookbackSessions, minSessions, trendBody, orbOnRate, orbOffRate, orbOnExtension, orbOffExtension, orMinutes, adxLength, adxOn, adxOff, smaLength, smaSlopeDays, enableMrShorts);
		}

		public Indicators.AgigRegimeFilter AgigRegimeFilter(ISeries<double> input , int lookbackSessions, int minSessions, double trendBody, double orbOnRate, double orbOffRate, double orbOnExtension, double orbOffExtension, int orMinutes, int adxLength, double adxOn, double adxOff, int smaLength, int smaSlopeDays, bool enableMrShorts)
		{
			return indicator.AgigRegimeFilter(input, lookbackSessions, minSessions, trendBody, orbOnRate, orbOffRate, orbOnExtension, orbOffExtension, orMinutes, adxLength, adxOn, adxOff, smaLength, smaSlopeDays, enableMrShorts);
		}
	}
}

#endregion
