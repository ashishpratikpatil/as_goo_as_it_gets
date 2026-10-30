"""Parameter grids for the mean-revert (mr) and ORB (orb) strategies.

Usage: python grid.py mr | orb
Writes results/<name>.parquet with one row per parameter set and metrics per segment.
"""
import itertools
import os
import sys
import time
from multiprocessing import Pool

import numpy as np
import pandas as pd

import data
import engine as E

TICK, PV = 0.25, 20.0
SLIP, COMM = 0.25, 4.50          # 1 tick per market fill, $4.50 round-trip commission
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "results")
KEEP = ("trades", "net", "sharpe", "sharpe_gross", "maxdd", "pf", "msharpe")
KEEP_IDX = [E.METRICS.index(k) for k in KEEP]

MR_TF = (1, 2, 3, 5, 10, 15)
MR_ATR_LEN = (3, 5, 8, 14)
MR_ATR_REV = (1.5, 2.0, 2.5, 3.0)
MR_CHAN = (50, 100, 150, 200, 300)
MR_OUTER = (1.0, 1.5, 2.0, 2.415, 3.0)
MR_TMODE = (0, 1, 2, 3)          # SD1 near, Mean, SD1 far, opposite outer band
MR_BUF = (0, 4, 8, 16)
MR_REV = (0, 1)
MR_RTH = (0, 1)
MR_PARAMS = ("tf", "atr_len", "atr_rev", "chan", "outer", "tmode", "buf", "reverse", "rth")

ORB_TF = (1, 2, 3, 5, 10, 15)
ORB_OR = (5, 10, 15, 30, 60)
ORB_ATR = ((5, 2.0), (14, 2.0), (5, 3.0), (14, 3.0))
ORB_BAND = [(0, 0.0, 0, 200)] + [(m, mult, lbm, ch) for m in (1, 2) for mult in (1.0, 2.0, 2.415)
                                  for lbm in (0, 15, 60, 240) for ch in (100, 200, 300)]
ORB_REQ = (0, 1)
ORB_EXIT = (0, 1, 2)             # AgigFlip, OrbOpposite, ScanArrowsStop
ORB_STOP = (0, 1)                # SwingPoint, OrOpposite
ORB_BUF = (0, 8)
ORB_TGT = (0.0, 1.0, 2.0)
ORB_MAXT = (1, 2)
ORB_PARAMS = ("tf", "atr_len", "atr_rev", "or_min", "band_mode", "band_mult", "band_lb_min", "chan",
              "req_agig", "exit_mode", "stop_mode", "buf", "target_r", "max_trades")


def _row(params, s):
    return list(params) + [float(v) for v in s[:, KEEP_IDX].ravel()]


def columns(param_names):
    return list(param_names) + [f"{seg}_{m}" for seg in data.SEG_NAMES for m in KEEP]


def mr_task(args):
    tf, atr_len, atr_rev = args
    a = data.load_tf(tf)
    o, h, l, c = a["o"], a["h"], a["l"], a["c"]
    seg_lo, seg_hi = data.segments(a["days_ns"])
    nd = len(a["days_ns"])
    st, hb, lb = E.agig_zigzag(h, l, c, 5, atr_len, atr_rev, 0.01, 0.05)
    rows = []
    for chan in MR_CHAN:
        mean, unit = E.ss_channel(h, l, c, chan)
        brt = max(5, chan)
        for outer, tmode, buf, rev, rth in itertools.product(MR_OUTER, MR_TMODE, MR_BUF, MR_REV, MR_RTH):
            tr = E.sim_mr(o, h, l, c, st, hb, lb, mean, unit, a["open_min"], a["day"],
                          outer, 1.0, tmode, buf * TICK, bool(rev), bool(rth), brt, TICK, SLIP, COMM, PV)
            s = E.stats(tr, nd, a["day_month"], seg_lo, seg_hi)
            rows.append(_row((tf, atr_len, atr_rev, chan, outer, tmode, buf, rev, rth), s))
    return rows


def orb_task(args):
    tf, atr_len, atr_rev, or_min = args
    a = data.load_tf(tf)
    o, h, l, c = a["o"], a["h"], a["l"], a["c"]
    seg_lo, seg_hi = data.segments(a["days_ns"])
    nd = len(a["days_ns"])
    st, hb, lb = E.agig_zigzag(h, l, c, 5, atr_len, atr_rev, 0.01, 0.05)
    chans = {}
    rows = []
    for band_mode, mult, lbm, chan in ORB_BAND:
        if chan not in chans:
            chans[chan] = E.ss_channel(h, l, c, chan)
        mean, unit = chans[chan]
        brt = max(5, chan)
        lb_bars = lbm // tf
        for req, ex, sm, buf, tgt, mx in itertools.product(ORB_REQ, ORB_EXIT, ORB_STOP, ORB_BUF, ORB_TGT, ORB_MAXT):
            tr = E.sim_orb(o, h, l, c, st, hb, lb, mean, unit, a["open_min"], a["open_date"], a["day"],
                           570, 960, tf, or_min, True, mx, True,
                           band_mode, mult, lb_bars, bool(req), ex, sm, buf * TICK, tgt, brt,
                           TICK, SLIP, COMM, PV)
            s = E.stats(tr, nd, a["day_month"], seg_lo, seg_hi)
            rows.append(_row((tf, atr_len, atr_rev, or_min, band_mode, mult, lbm, chan,
                              req, ex, sm, buf, tgt, mx), s))
    return rows


def run(name):
    os.makedirs(OUT, exist_ok=True)
    if name == "mr":
        tasks = list(itertools.product(MR_TF, MR_ATR_LEN, MR_ATR_REV))
        fn, params = mr_task, MR_PARAMS
    else:
        tasks = [(tf, al, ar, om) for tf in ORB_TF for (al, ar) in ORB_ATR for om in ORB_OR if om % tf == 0]
        fn, params = orb_task, ORB_PARAMS
    # Largest jobs (1-minute bars) first so the pool finishes evenly.
    tasks.sort(key=lambda t: t[0])
    fn(tasks[-1])  # compile once in the parent so workers load the numba cache
    t0 = time.time()
    rows = []
    with Pool(15) as pool:
        for k, r in enumerate(pool.imap_unordered(fn, tasks), 1):
            rows.extend(r)
            if k % 8 == 0 or k == len(tasks):
                print(f"{name}: {k}/{len(tasks)} tasks, {len(rows)} runs, {time.time() - t0:.0f}s", flush=True)
    df = pd.DataFrame(rows, columns=columns(params)).astype(np.float32)
    path = os.path.join(OUT, f"{name}.parquet")
    df.to_parquet(path)
    print("saved", path, df.shape)


if __name__ == "__main__":
    run(sys.argv[1])
