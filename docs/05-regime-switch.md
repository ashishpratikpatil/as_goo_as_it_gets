# Regime switch and day-trade filters

`AgigRegimeSwitchStrategy` is one NinjaScript that can run **ORB** and **mean-revert** together. `AgigRegimeEngine` latches which sleeve is allowed.

## What it measures

On each NY session (09:30–16:00):

- **Trend-day:** `|Close − Open| / (High − Low) ≥ TrendBody` (default 0.60).
- **OR extension:** session range / locked opening-range width.
- Rolling rates over `Lookback` sessions (default 20, need `MinSessions` 10).

Daily (secondary series or rebuilt from minutes):

- **ADX** (Wilder) vs `AdxOn` / `AdxOff` (23 / 20).
- **200-SMA slope** — mean-revert longs need a rising SMA; shorts need a falling SMA (`EnableMrShorts`).

Hysteresis: ORB turns **on** when trend-day rate ≥ `OrbOnRate` (0.33) and median extension ≥ `OrbOnExt` (3.0); turns **off** below `OrbOffRate` / `OrbOffExt`.

## What it did not do

On NQ 2020–2026 in NinjaTrader Strategy Analyzer, the combined strategy still lost through **early 2023** and made money in the later rally — the same shape as running the sleeves unfiltered. Python year splits (`docs/regime.json`) show ADX, trend-day %, Hurst, and lag-1 autocorrelation **do not** cleanly label 2020–2022 as “mean-revert” and 2023–2025 as “trend.”

Use this code as a **scaffold**. Do not treat the default gates as a solved regime detector.

## Better filters to try next

These came out of the Python `regime.py` pass. They are **recommendations**, not compiled into the live strategy yet.

1. **1R hit rate** on the last 10–20 ORB trades. Skip ORB when fewer than ~40% of recent trades reached 1R before stop.
2. **False-break %** — OR close-crosses that reverse back inside the OR within N bars. High false-break → Fade or flat, not Breakout.
3. **OR / ATR in 0.25–0.70.** Tiny OR (noise) and huge OR (already extended) both skip.
4. **Gap skip** — overnight gap beyond a fraction of ATR, skip the first ORB of the day.
5. **VIX as skip / size, not as a strategy switch.**
   - VIX > ~25: skip ORB (wide, two-sided).
   - VIX is not a clean ORB vs mean-revert toggle. 2021 median VIX ~18.7 still lost ORB on the NT sample.
   - Size down when VIX is high; do not flip from fade to breakout on a VIX print.
6. **Day-of-week / FOMC / CPI** — optional calendar skip; not in the engine.

Python: `python/nq_backtest/regime.py`. Snapshot: `docs/regime.json`.

## Files

- `ninjascript/Indicators/AgigRegimeFilter.cs` (`AgigRegimeEngine`, overlay)
- `ninjascript/Strategies/AgigRegimeSwitchStrategy.cs`
