# Strategies

Drop these three files into `NinjaTrader 8\bin\Custom\Strategies`. They need the indicators in `../Indicators` to compile.

## AgigTosRevAmtSwingStrategy

Mean-revert fade of AGIG swings that print outside the outer channel.

- **Short** if a confirmed swing **high ≥ R2**.
- **Long** if a confirmed swing **low ≤ S2**.
- Entry on the **next bar** (`OnBarClose`).
- Stop: swing extreme ± `SwingStopBufferTicks` (default 4).
- Target: channel **Mean** (default) or **OppositeBand**.
- Opposite AGIG mark can reverse or only flatten.

Python-grid note for NQ 2022–2025: 15-minute, **longs only**, ATR 14 / reversal 1.5, outer 2.0, OppositeBand worked better than the chart default. See [docs/03-mean-revert-strategy.md](../../docs/03-mean-revert-strategy.md).

## AgigOrbSd2Strategy

Opening-range breakout/fade with the same AGIG zigzag and channel.

- Session clock: NY 09:30–16:00 (also London / Asian / Globex / Custom).
- OR builds for `OrWindow` minutes (Auto follows the chart bar size), then **locks**.
- **ORB Long:** close crosses above locked OR high. **ORB Short:** close crosses below locked OR low.
- **Breakout** band: long at/above R2, short at/below S2. **Fade** band: the reverse.
- Hold until `AgigFlip`, `OrbOpposite`, or `ScanArrowsStop`.
- Stops: swing, opposite OR, or fixed ticks. Optional `TargetRMultiple`.
- `MaxTradesPerSession` default 1. Safe window: no new entries in the last 3.5 OR lengths before session end.

Python-grid note: 5-minute Fade, channel 300, outer 2.8, ScanArrowsStop, OrOpposite, 2R, AGIG direction **off**. See [docs/04-orb-strategy.md](../../docs/04-orb-strategy.md).

## AgigRegimeSwitchStrategy

Single strategy that instantiates the ORB and mean-revert engines and lets `AgigRegimeEngine` decide which is allowed each session.

- ORB when recent NY sessions trend often and extend past the opening range.
- Mean-revert longs when daily ADX is elevated and the 200-SMA is rising; shorts need a falling SMA.

On 2020–2026 NQ these gates did **not** skip the 2020–early-2023 losing stretch. Treat this as a scaffold, not a finished regime model. Recommendations: [docs/05-regime-switch.md](../../docs/05-regime-switch.md).
