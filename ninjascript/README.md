# NinjaScript (C# / NinjaTrader 8)

All live overlays and strategies for this stack. Copy these files into the matching folders under `NinjaTrader 8\bin\Custom`, then compile in NinjaTrader. Generated `#region NinjaScript generated code` blocks at the bottom of each file must stay in sync with NinjaTrader; do not hand-edit them.

## Indicators

| File | Chart name | What it plots |
|---|---|---|
| `Indicators/AgigTosRevAmtSwingMarks.cs` | AGIG TOS RevAmt Swing Marks | Confirmed Short / Long at swing extremes |
| `Indicators/AdvancedSmoothedChannel.cs` | AdvancedSmoothedChannel | Mean, inner (1.0), outer (2.415 ≈ 2 SD) |
| `Indicators/AgigRegimeFilter.cs` | AGIG Regime Filter | OrbOn / MrLongOn / MrShortOn plus ADX and slope |

`AgigRegimeFilter.cs` also contains `AgigRegimeEngine` and `AgigZigzagEngine`. The combined strategy uses those types, so this file must compile even if you never add the indicator to a chart.

## Strategies

| File | Chart name | Role |
|---|---|---|
| `Strategies/AgigTosRevAmtSwingStrategy.cs` | AGIG TOS RevAmt Swing Strategy | Fade outer-band AGIG swings |
| `Strategies/AgigOrbSd2Strategy.cs` | AGIG ORB SD2 Strategy | Opening-range cross + band + optional AGIG direction |
| `Strategies/AgigRegimeSwitchStrategy.cs` | AGIG Regime Switch Strategy | Runs ORB and/or mean-revert from one script |

## Shared rules

- `Calculate = OnBarClose`. Historical fills are next-bar open.
- `BarsRequiredToTrade` is set in `SetDefaults` / `Configure` only (never in `DataLoaded`).
- ZigZag RevAmt defaults: ATR reversal 2, EMA 5, ATR length 5, percentage 0.01, absolute 0.05.
- Channel default: SuperSmoother, length 200, inner 1.0, outer 2.415.
- Session times use the **bar open** in the chart time zone. NY RTH is 09:30–16:00.

Install steps: [docs/07-ninjatrader-install.md](../docs/07-ninjatrader-install.md).
