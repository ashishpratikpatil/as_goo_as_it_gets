"""Drill into single settings: long vs short, exit types, per-year results, equity curve vs buy-and-hold."""
import os
import numpy as np
import pandas as pd

import data
import engine as E
from grid import TICK, PV, SLIP, COMM

KINDS = {0: "market", 1: "stop", 2: "target", 3: "end"}


def run_mr(p, slip=SLIP, comm=COMM):
    a = data.load_tf(int(p["tf"]))
    st, hb, lb = E.agig_zigzag(a["h"], a["l"], a["c"], int(p.get("ema", 5)), int(p["atr_len"]), float(p["atr_rev"]), 0.01, 0.05)
    mean, unit = E.ss_channel(a["h"], a["l"], a["c"], int(p["chan"]))
    tr = E.sim_mr(a["o"], a["h"], a["l"], a["c"], st, hb, lb, mean, unit, a["open_min"], a["day"],
                  float(p["outer"]), 1.0, int(p["tmode"]), int(p["buf"]) * TICK, bool(p.get("reverse", 1)),
                  bool(p.get("rth", 0)), max(5, int(p["chan"])), TICK, slip, comm, PV,
                  bool(p.get("allow_long", True)), bool(p.get("allow_short", True)))
    return a, tr


def run_orb(p, slip=SLIP, comm=COMM):
    tf = int(p["tf"])
    a = data.load_tf(tf)
    st, hb, lb = E.agig_zigzag(a["h"], a["l"], a["c"], int(p.get("ema", 5)), int(p["atr_len"]), float(p["atr_rev"]), 0.01, 0.05)
    mean, unit = E.ss_channel(a["h"], a["l"], a["c"], int(p["chan"]))
    tr = E.sim_orb(a["o"], a["h"], a["l"], a["c"], st, hb, lb, mean, unit, a["open_min"], a["open_date"], a["day"],
                   int(p.get("sess_start", 570)), int(p.get("sess_end", 960)), tf, int(p["or_min"]), True,
                   int(p["max_trades"]), True, int(p["band_mode"]), float(p["band_mult"]),
                   int(p["band_lb_min"]) // tf, bool(p["req_agig"]), int(p["exit_mode"]), int(p["stop_mode"]),
                   int(p["buf"]) * TICK, float(p["target_r"]), max(5, int(p["chan"])), TICK, slip, comm, PV)
    return a, tr


def seg_stats(a, tr):
    lo, hi = data.segments(a["days_ns"])
    return pd.DataFrame(E.stats(tr, len(a["days_ns"]), a["day_month"], lo, hi),
                        index=data.SEG_NAMES, columns=E.METRICS)


def buy_hold(a):
    """Daily Sharpe of holding 1 NQ contract (close-to-close by trading day)."""
    s = pd.Series(a["c"]).groupby(a["day"]).last()
    pnl = s.diff().fillna(0).to_numpy() * PV
    lo, hi = data.segments(a["days_ns"])
    out = {}
    for name, l_, h_ in zip(data.SEG_NAMES, lo, hi):
        x = pnl[l_:h_]
        out[name] = x.mean() / x.std(ddof=1) * np.sqrt(252)
    return pd.Series(out, name="buy_hold_sharpe")


def report(label, a, tr):
    print(f"\n##### {label}")
    print(seg_stats(a, tr).round(2).to_string())
    for d, nm in ((1, "longs"), (-1, "shorts")):
        sub = tr[tr[:, E.T_DIR] == d]
        s = seg_stats(a, sub)
        print(f"  {nm}: " + "  ".join(f"{k} n={int(s.loc[k, 'trades'])} net=${s.loc[k, 'net']:,.0f}"
                                     for k in ("y2023", "y2024", "y2025")))
    kinds = pd.Series(tr[:, E.T_KIND]).map(KINDS).value_counts()
    net = tr[:, E.T_GROSS] - tr[:, E.T_COST]
    bars = tr[:, E.T_EXIT_I] - tr[:, E.T_ENTRY_I]
    print("  exits:", kinds.to_dict(), f" avg net/trade ${net.mean():,.0f}  median bars held {np.median(bars):.0f}")


def equity_plot(curves, path, title):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(figsize=(12, 6))
    for label, (a, tr) in curves.items():
        ts = pd.to_datetime(a["close_ns"][tr[:, E.T_EXIT_I].astype(int)])
        ax.plot(ts, np.cumsum(tr[:, E.T_GROSS] - tr[:, E.T_COST]), label=label, lw=1.2)
    ax.axvline(pd.Timestamp("2025-01-01"), color="k", ls="--", lw=0.8)
    ax.text(pd.Timestamp("2025-01-10"), ax.get_ylim()[1] * 0.95, "2025 out-of-sample", fontsize=9)
    ax.set_title(title)
    ax.set_ylabel("Net P&L, 1 NQ contract ($)")
    ax.grid(alpha=0.3)
    ax.legend(fontsize=8, loc="upper left")
    fig.tight_layout()
    fig.savefig(path, dpi=110)
    plt.close(fig)
    print("saved", path)


if __name__ == "__main__":
    a5 = data.load_tf(5)
    print(buy_hold(a5).round(2).to_string())
