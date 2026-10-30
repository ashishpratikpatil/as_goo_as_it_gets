# TOS ZigZagHighLow dollar RevAmt

This is the swing engine shared by the overlay, both strategies, and the Python port. It is **not** NinjaTrader’s built-in ZigZag.

## Formula

EMA on High and Low:

```
k = 2 / (EmaLength + 1)
emaHigh = emaHigh[1] + k * (High - emaHigh[1])
emaLow  = emaLow[1]  + k * (Low  - emaLow[1])
```

Reversal distance (dollars):

```
reversalDist = Close * (PercentageReversal / 100)
             + ATR(AtrLength) * AtrReversal
             + AbsoluteReversal
```

Defaults: `AtrReversal = 2`, `EmaLength = 5`, `AtrLength = 5`, `PercentageReversal = 0.01`, `AbsoluteReversal = 0.05`.

`TickReversal` exists on the overlay for TOS parity; the strategies use the dollar form above.

## State

| State | Meaning |
|---|---|
| Undef (1) | No confirmed swing yet |
| Up (2) | Last confirmed swing was a low (up-leg in progress) |
| Dn (3) | Last confirmed swing was a high (down-leg in progress) |

A new **swing high** (Short mark) confirms when price reverses down by `reversalDist` from the running max of the up-leg. A new **swing low** (Long mark) confirms when price reverses up by `reversalDist` from the running min of the down-leg.

The **Short** label sits on the swing-high candle. The **Long** label sits on the swing-low candle. Strategies enter on the **following** bar.

## ATR

NinjaTrader Wilder smoothing (same as `ATR()` in NinjaScript and `nt_atr` in Python):

```
TR[0] = max(High-Low, |High-Close[1]|, |Low-Close[1]|)
ATR[i] = ((min(i+1, period) - 1) * ATR[i-1] + TR[i]) / min(i+1, period)
```

## Files

- Overlay: `ninjascript/Indicators/AgigTosRevAmtSwingMarks.cs`
- Inlined in both strategies and in `AgigZigzagEngine` (`AgigRegimeFilter.cs`)
- Python: `agig_zigzag` in `python/nq_backtest/engine.py`
