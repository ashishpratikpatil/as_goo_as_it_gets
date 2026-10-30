import numpy as np
import pandas as pd
import detail as D
import data
import engine as E

BASE = dict(tf=5, atr_len=5, atr_rev=2.0, or_min=5, band_mode=2, band_mult=2.415, band_lb_min=15, chan=300,
            req_agig=0, exit_mode=2, stop_mode=1, buf=0, target_r=2.0, max_trades=1)

CANDIDATES = {
    "Fade 5m OR5 S2 lookback15 ch300 scan+ORstop 2R": BASE,
    "same, band filter off": {**BASE, "band_mode": 0},
    "same, Breakout band": {**BASE, "band_mode": 1},
    "same, no 2R target": {**BASE, "target_r": 0.0},
    "same, AGIG direction on": {**BASE, "req_agig": 1},
    "ORB NT defaults (Breakout, AGIG, AgigFlip)": dict(
        tf=5, atr_len=5, atr_rev=2.0, or_min=5, band_mode=1, band_mult=2.415, band_lb_min=0,
        chan=200, req_agig=1, exit_mode=0, stop_mode=0, buf=4, target_r=0.0, max_trades=1),
}

curves = {}
for label, p in CANDIDATES.items():
    a, tr = D.run_orb(p)
    D.report(label, a, tr)
    curves[label] = (a, tr)
D.equity_plot(curves, "results/orb_equity.png",
              "ORB candidates, NQ 1 contract, net of $4.50 + 1 tick per market fill")

print("\n=== Cost sensitivity for the Fade setting ===")
for slip_ticks, comm in ((0, 0.0), (1, 4.5), (2, 4.5), (3, 5.0)):
    a, tr = D.run_orb(BASE, slip=slip_ticks * 0.25, comm=comm)
    s = D.seg_stats(a, tr)
    print(f"slip {slip_ticks} tick, comm ${comm}: " +
          "  ".join(f"{k} {s.loc[k, 'sharpe']:.2f}" for k in data.SEG_NAMES) +
          f"   all net ${s.loc['all', 'net']:,.0f}")

a, tr = D.run_orb(BASE)
ts = pd.to_datetime(a["close_ns"])
ent = ts[tr[:, E.T_ENTRY_I].astype(int)]
open_t = ent - pd.Timedelta(minutes=5)
print("\nentry time-of-day (bar open):")
print(pd.Series(open_t.hour * 100 + open_t.minute).value_counts().sort_index().to_string())
held = (tr[:, E.T_EXIT_I] - tr[:, E.T_ENTRY_I]).astype(int)
print("bars held:", {k: round(v, 1) for k, v in pd.Series(held).describe().to_dict().items()})
net = tr[:, E.T_GROSS] - tr[:, E.T_COST]
print(f"avg win ${net[net > 0].mean():,.0f}  avg loss ${net[net <= 0].mean():,.0f}  largest ${net.min():,.0f} / ${net.max():,.0f}")
rows = []
for r in tr[:12]:
    ei, xi = int(r[E.T_ENTRY_I]), int(r[E.T_EXIT_I])
    rows.append((str(ts[ei] - pd.Timedelta(minutes=5)), "L" if r[E.T_DIR] > 0 else "S",
                 a["o"][ei], D.KINDS[int(r[E.T_KIND])], round(r[E.T_GROSS] / 20, 2)))
print(pd.DataFrame(rows, columns=["entry_open", "dir", "px", "how", "pts"]).to_string())
