# Mean-revert: AGIG TOS RevAmt Swing Strategy

Fade AGIG swings that print outside the outer SuperSmoother band.

## Entry

1. ZigZag confirms a swing (Short on high, Long on low).
2. Channel filter (if on): high ≥ R2 for shorts, low ≤ S2 for longs.
3. Order submitted on that bar’s close; fill at **next bar open**.

`AllowLong` / `AllowShort` gate direction. `ReverseOnOppositeSignal` (default on) flips the position when the opposite mark prints.

## Exit

- **Swing stop:** long stop = swing low − buffer ticks; short stop = swing high + buffer ticks. Default buffer 4 ticks.
- **Target:** `AgigChannelTarget.Mean` (default) or `OppositeBand`.
- Optional extra `StopLossTicks` / `ProfitTargetTicks` (0 = off).
- If a bar hits stop and target, the model assumes **stop first** (same as the Python engine).

## Live defaults (NinjaTrader)

| Parameter | Default |
|---|---|
| ATR reversal / EMA / ATR length | 2 / 5 / 5 |
| % / absolute reversal | 0.01 / 0.05 |
| Channel | SuperSmoother 200, inner 1.0, outer 2.415 |
| Target | Mean |
| Swing stop buffer | 4 ticks |
| Directions | long and short |

## Python-grid set (NQ 1-min CSV, 2022-12 → 2025-12)

15-minute bars, **longs only**, ATR length **14**, ATR reversal **1.5**, channel 200, outer **2.0**, target **OppositeBand**.

The 2026 chart trial (target Mean, channel 150, outer 2.5) was a **net loser** on this CSV. Do not treat those knobs as validated.

## Files

- `ninjascript/Strategies/AgigTosRevAmtSwingStrategy.cs`
- Python: `sim_mr` plus `mr_drill.py` / `mr_long.py`
