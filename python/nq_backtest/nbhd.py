"""One-parameter neighborhoods around the ORB Fade winner and the MR 15-minute candidate."""
import pandas as pd

pd.set_option("display.width", 220)
pd.set_option("display.max_columns", 30)
pd.set_option("display.max_rows", 80)

show = ["is_trades", "is_sharpe", "is_net", "is_maxdd", "y2023_sharpe", "y2024_sharpe",
        "y2025_sharpe", "y2025_net", "all_sharpe", "all_net"]


def slice_eq(df, **kw):
    m = pd.Series(True, index=df.index)
    for k, v in kw.items():
        m &= df[k] == v
    return df[m]


print("===== ORB neighborhood around Fade 5m OR5 / S2 lookback 15 / ch300 / scan / OR stop / 2R =====")
orb = pd.read_parquet("results/orb.parquet")
base = dict(tf=5, or_min=5, band_mode=2, band_mult=2.415, band_lb_min=15, chan=300,
            req_agig=0, exit_mode=2, stop_mode=1, buf=0, target_r=2.0, max_trades=1)
# ATR is unused when RequireAgigDirection is off; pin one pair
pinned = dict(atr_len=5, atr_rev=2.0, **base)
row = slice_eq(orb, **pinned)
print("base row:\n", row[list(base) + show].round(2).to_string(index=False))

for p in ("tf", "or_min", "band_mode", "band_mult", "band_lb_min", "chan", "req_agig",
          "exit_mode", "stop_mode", "buf", "target_r", "max_trades"):
    kw = {k: v for k, v in pinned.items() if k != p}
    # nearby tfs only make sense with matching or_min; skip impossible combos later
    g = slice_eq(orb, **kw)
    if p == "tf":
        g = g[g.or_min % g.tf == 0]
    cols = [p] + show
    print(f"\n-- vary {p} --")
    print(g[cols].drop_duplicates(p).sort_values(p).round(2).to_string(index=False))

print("\n===== MR neighborhood around 15m ATR14/1.5 ch200 outer2 OppositeBand buf4 =====")
mr = pd.read_parquet("results/mr.parquet")
mbase = dict(tf=15, atr_len=14, atr_rev=1.5, chan=200, outer=2.0, tmode=3, buf=4, reverse=0, rth=0)
print("base:\n", slice_eq(mr, **mbase)[list(mbase) + show].round(2).to_string(index=False))
for p in ("tf", "atr_len", "atr_rev", "chan", "outer", "tmode", "buf", "rth"):
    kw = {k: v for k, v in mbase.items() if k != p}
    g = slice_eq(mr, **kw)
    print(f"\n-- vary {p} --")
    print(g[[p] + show].drop_duplicates(p).sort_values(p).round(2).to_string(index=False))
