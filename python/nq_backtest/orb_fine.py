"""Finer ORB grid around the Fade / ScanArrowsStop / OR-opposite-stop group."""
import itertools
import os
import time
from multiprocessing import Pool

import numpy as np
import pandas as pd

import data
import engine as E
from grid import TICK, PV, SLIP, COMM, KEEP_IDX, columns

TF = (3, 5, 10)
OR_MIN = (5, 10, 15)
BAND = [(0, 0.0, 0, 300)] + [(m, mult, lbm, ch) for m in (1, 2) for mult in (1.75, 2.0, 2.25, 2.415, 2.6, 2.8, 3.0)
                             for lbm in (0, 5, 10, 15, 20, 30, 45, 60) for ch in (150, 200, 250, 300, 400, 500)]
EXIT = (1, 2)
STOP = (0, 1)
BUF = (0, 4)
TGT = (0.0, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0)
MAXT = (1, 2, 3)
PARAMS = ("tf", "or_min", "band_mode", "band_mult", "band_lb_min", "chan", "exit_mode", "stop_mode", "buf",
          "target_r", "max_trades")


def task(args):
    tf, or_min, band_mode = args
    a = data.load_tf(tf)
    o, h, l, c = a["o"], a["h"], a["l"], a["c"]
    seg_lo, seg_hi = data.segments(a["days_ns"])
    nd = len(a["days_ns"])
    st, hb, lb = E.agig_zigzag(h, l, c, 5, 5, 2.0, 0.01, 0.05)
    chans = {}
    rows = []
    for bm, mult, lbm, chan in BAND:
        if bm != band_mode or (lbm % tf != 0):
            continue
        if chan not in chans:
            chans[chan] = E.ss_channel(h, l, c, chan)
        mean, unit = chans[chan]
        for ex, sm, buf, tgt, mx in itertools.product(EXIT, STOP, BUF, TGT, MAXT):
            tr = E.sim_orb(o, h, l, c, st, hb, lb, mean, unit, a["open_min"], a["open_date"], a["day"],
                           570, 960, tf, or_min, True, mx, True, bm, mult, lbm // tf, False, ex, sm,
                           buf * TICK, tgt, max(5, chan), TICK, SLIP, COMM, PV)
            s = E.stats(tr, nd, a["day_month"], seg_lo, seg_hi)
            rows.append([tf, or_min, bm, mult, lbm, chan, ex, sm, buf, tgt, mx] + [float(v) for v in s[:, KEEP_IDX].ravel()])
    return rows


if __name__ == "__main__":
    tasks = [(tf, om, bm) for tf in TF for om in OR_MIN for bm in (2, 1, 0) if om % tf == 0 or om > tf]
    tasks.sort(key=lambda t: (t[0], -t[2]))
    task(tasks[-1])
    t0 = time.time()
    rows = []
    with Pool(15) as pool:
        for k, r in enumerate(pool.imap_unordered(task, tasks), 1):
            rows.extend(r)
            print(f"{k}/{len(tasks)} tasks, {len(rows)} runs, {time.time() - t0:.0f}s", flush=True)
    df = pd.DataFrame(rows, columns=columns(PARAMS)).astype(np.float32)
    df.to_parquet("results/orb_fine.parquet")
    print("saved", df.shape)
