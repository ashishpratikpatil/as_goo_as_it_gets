import pandas as pd
import detail as D
import data

CANDIDATES = {
    "IS-best 5m atr14/2.5 ch50 o1.5 oppBand buf0": dict(tf=5, atr_len=14, atr_rev=2.5, chan=50, outer=1.5, tmode=3, buf=0),
    "15m atr14/1.5 ch200 o2.0 oppBand buf4": dict(tf=15, atr_len=14, atr_rev=1.5, chan=200, outer=2.0, tmode=3, buf=4),
    "15m atr14/1.5 ch200 o2.0 farSD1 buf0": dict(tf=15, atr_len=14, atr_rev=1.5, chan=200, outer=2.0, tmode=2, buf=0),
    "10m atr14/1.5 ch300 o1.0 oppBand buf0": dict(tf=10, atr_len=14, atr_rev=1.5, chan=300, outer=1.0, tmode=3, buf=0),
    "NT 2026 best: 5m atr5/2 ch150 o2.5 mean buf8": dict(tf=5, atr_len=5, atr_rev=2.0, chan=150, outer=2.5, tmode=1, buf=8),
}

print(D.buy_hold(data.load_tf(15)).round(2).to_string())
curves = {}
for label, p in CANDIDATES.items():
    a, tr = D.run_mr(p)
    D.report(label, a, tr)
    curves[label] = (a, tr)
D.equity_plot(curves, "results/mr_equity.png", "Mean-revert candidates, NQ 1 contract, net of $4.50 + 1 tick per market fill")
