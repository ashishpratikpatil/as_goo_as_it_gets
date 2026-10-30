# AdvancedSmoothedChannel

Ehlers SuperSmoother of typical price, plus a SuperSmoothed true-range envelope. Used as the **2 SD** (outer) filter for both strategies.

## Bands

```
mean  = SuperSmoother(TypicalPrice, Length)
ssTR  = SuperSmoother(TrueRange, Length)
inner = mean ± ssTR * π * InnerMult     # default InnerMult = 1.0
outer = mean ± ssTR * π * OuterMult     # default OuterMult = 2.415
```

On a chart the outer high is **R2** and the outer low is **S2**. “2 SD” in conversation means this outer band, not a statistical standard deviation of closes.

Default `Length = 200`. `BarsRequiredToTrade` follows `ChannelLength`.

## How strategies use it

**Mean-revert**

- Short only if swing high ≥ outer high (R2).
- Long only if swing low ≤ outer low (S2).
- Target is the **mean** or the **opposite outer** band.

**ORB**

- **Breakout:** long needs price at/above R2; short at/below S2.
- **Fade:** long needs price at/below S2; short at/above R2.
- Optional lookback: the band condition can be required on the signal bar or on a prior bar (`BandLookbackBars` / Python `band_lb_min`).

## Files

- `ninjascript/Indicators/AdvancedSmoothedChannel.cs`
- Python: `ss_channel` in `python/nq_backtest/engine.py`
