"""Run the NinjaTrader optimizer's best mean-revert setting and the ORB defaults on 5-minute bars
and print trades so entries, stops and targets can be checked by eye."""
import sys
import time
import numpy as np
import pandas as pd
import data
import engine as E

TICK, PV = 0.25, 20.0
SLIP, COMM = 0.25, 4.50


def show(tr, a, label, k=12):
    ts = pd.to_datetime(a["close_ns"])
    seg_lo, seg_hi = data.segments(a["days_ns"])
    s = E.stats(tr, len(a["days_ns"]), a["day_month"], seg_lo, seg_hi)
    print(f"\n== {label}: {len(tr)} trades")
    print(pd.DataFrame(s, index=data.SEG_NAMES, columns=E.METRICS).round(3).to_string())
    kinds = {0: "market", 1: "stop", 2: "target", 3: "eod"}
    rows = []
    for r in tr[:k]:
        ei, xi = int(r[E.T_ENTRY_I]), int(r[E.T_EXIT_I])
        rows.append((ts[ei], "L" if r[E.T_DIR] > 0 else "S", a["o"][ei], ts[xi], kinds[int(r[E.T_KIND])],
                     round(r[E.T_GROSS] / PV, 2)))
    print(pd.DataFrame(rows, columns=["entry_bar_close", "dir", "entry_px", "exit_bar_close", "how", "pts"]).to_string())


def main():
    tf = int(sys.argv[1]) if len(sys.argv) > 1 else 5
    a = data.load_tf(tf)
    o, h, l, c = a["o"], a["h"], a["l"], a["c"]
    t0 = time.time()
    st, hb, lb = E.agig_zigzag(h, l, c, 5, 5, 2.0, 0.01, 0.05)
    mean, unit = E.ss_channel(h, l, c, 150)
    t1 = time.time()
    tr = E.sim_mr(o, h, l, c, st, hb, lb, mean, unit, a["open_min"], a["day"],
                  2.5, 1.0, 1, 8 * TICK, True, False, 150, TICK, SLIP, COMM, PV)
    t2 = time.time()
    tr = E.sim_mr(o, h, l, c, st, hb, lb, mean, unit, a["open_min"], a["day"],
                  2.5, 1.0, 1, 8 * TICK, True, False, 150, TICK, SLIP, COMM, PV)
    t3 = time.time()
    print(f"indicators+jit {t1 - t0:.2f}s, first sim {t2 - t1:.2f}s, second sim {1000 * (t3 - t2):.1f} ms")
    show(tr, a, f"MR {tf}m buf8 len150 outer2.5 mean")
    months = len(np.unique(a["day_month"]))
    print("trades per month:", round(len(tr) / months, 1))

    mean2, unit2 = E.ss_channel(h, l, c, 200)
    tr2 = E.sim_orb(o, h, l, c, st, hb, lb, mean2, unit2, a["open_min"], a["open_date"], a["day"],
                    570, 960, tf, tf, True, 1, True,
                    1, 2.415, 0, True, 0, 0, 4 * TICK, 0.0, 200, TICK, SLIP, COMM, PV)
    show(tr2, a, f"ORB {tf}m defaults (Breakout, AgigFlip, swing stop)")
    tr3 = E.sim_orb(o, h, l, c, st, hb, lb, mean2, unit2, a["open_min"], a["open_date"], a["day"],
                    570, 960, tf, 15, True, 1, True,
                    0, 2.415, 0, True, 0, 0, 4 * TICK, 0.0, 200, TICK, SLIP, COMM, PV)
    show(tr3, a, f"ORB {tf}m OR15 no band filter")


if __name__ == "__main__":
    main()
