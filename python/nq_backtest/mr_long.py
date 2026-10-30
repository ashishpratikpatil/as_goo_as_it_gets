import detail as D
import data

P = dict(tf=15, atr_len=14, atr_rev=1.5, chan=200, outer=2.0, tmode=3, buf=4, reverse=0, rth=0)
print(D.buy_hold(data.load_tf(15)).round(2).to_string())
curves = {}
for label, extra in (
    ("15m both sides, OppositeBand", {}),
    ("15m longs only, OppositeBand", {"allow_short": False}),
    ("15m shorts only, OppositeBand", {"allow_long": False}),
    ("15m longs only, far SD1", {"allow_short": False, "tmode": 2}),
    ("15m longs only, Mean target", {"allow_short": False, "tmode": 1}),
):
    a, tr = D.run_mr({**P, **extra})
    D.report(label, a, tr)
    curves[label] = (a, tr)
D.equity_plot(curves, "results/mr_long_equity.png",
              "Mean-revert 15m ch200 outer2, long-only vs both, NQ 1 contract net")
