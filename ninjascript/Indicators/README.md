# Indicators

Drop these three files into `NinjaTrader 8\bin\Custom\Indicators`.

## AgigTosRevAmtSwingMarks

TOS `ZigZagHighLow` dollar-RevAmt overlay.

- Smooths High and Low with EMA length 5 (`k = 2 / (EmaLen + 1)`).
- `reversalDist = close * (PercentageReversal / 100) + ATR * AtrReversal + AbsoluteReversal`.
- State machine: Undef → Up / Dn. A confirmed **Short** prints on a swing high; a confirmed **Long** prints on a swing low.
- Optional debug bubbles every 10 bars.

This is a plot-only indicator. The strategies recompute the same zigzag internally so they can trade without a chart overlay.

## AdvancedSmoothedChannel

SuperSmoother (Ehlers) mean of typical price, plus a SuperSmoothed true-range envelope.

- Inner band: `mean ± ssTR * π * 1.0`
- Outer band: `mean ± ssTR * π * 2.415` (the “2 SD” / R2 / S2 used by both strategies)

Also exposes `AdvFilterType` (SuperSmoother is the only type the strategies use).

## AgigRegimeFilter

Daily / session regime gates used by `AgigRegimeSwitchStrategy`.

- **Trend-day:** `|close − open| / (high − low) ≥ 0.60` on NY RTH.
- **OR extension:** RTH range vs the locked opening range.
- **Daily ADX + 200-SMA slope** for mean-revert permission.

Plots `OrbOn`, `MrLongOn`, `MrShortOn`. See [docs/05-regime-switch.md](../../docs/05-regime-switch.md) for why these gates did not split 2020–2023 from 2023–2026 on NQ.
