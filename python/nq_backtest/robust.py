"""Plateau ranking: score each setting by the mean 2023-2024 Sharpe of itself and its one-step neighbours
along the numeric parameters, then show how the best plateaus did in 2025 (out-of-sample)."""
import sys
import numpy as np
import pandas as pd

pd.set_option("display.width", 250)
pd.set_option("display.max_columns", 40)

NUMERIC = {
    "mr": ["tf", "atr_len", "atr_rev", "chan", "outer", "buf"],
    "orb": ["tf", "atr_len", "atr_rev", "or_min", "band_mult", "band_lb_min", "chan", "buf", "target_r", "max_trades"],
}


def plateau(df, params, numeric, col="is_sharpe"):
    card = {}
    idx = {}
    for p in params:
        vals = np.sort(df[p].unique())
        card[p] = len(vals)
        idx[p] = np.searchsorted(vals, df[p].to_numpy())
    radix = {}
    r = 1
    for p in params:
        radix[p] = r
        r *= card[p]
    key = np.zeros(len(df), np.int64)
    for p in params:
        key += idx[p].astype(np.int64) * radix[p]
    s = pd.Series(df[col].to_numpy(np.float64), index=key)
    total = df[col].to_numpy(np.float64).copy()
    count = np.ones(len(df))
    worst = total.copy()
    for p in numeric:
        for sh in (-1, 1):
            ok = (idx[p] + sh >= 0) & (idx[p] + sh < card[p])
            nk = key + sh * radix[p]
            v = s.reindex(nk[ok]).to_numpy()
            fill = np.full(len(df), np.nan)
            fill[ok] = v
            m = ~np.isnan(fill)
            total[m] += fill[m]
            count[m] += 1
            worst[m] = np.minimum(worst[m], fill[m])
    return total / count, worst, count


def main():
    name = sys.argv[1]
    min_trades = int(sys.argv[2]) if len(sys.argv) > 2 else 100
    df = pd.read_parquet(f"results/{name}.parquet")
    params = [c for c in df.columns if not any(c.startswith(s + "_") for s in ("y2023", "y2024", "y2025", "is", "all"))]
    df["plateau"], df["plateau_worst"], df["n_nb"] = plateau(df, params, NUMERIC[name])
    ok = df[(df.is_trades >= min_trades) & (df.n_nb >= 5)].copy()
    show = params + ["plateau", "plateau_worst", "is_trades", "is_sharpe", "y2023_sharpe", "y2024_sharpe",
                     "y2025_trades", "y2025_net", "y2025_sharpe", "y2025_maxdd", "all_net", "all_sharpe", "all_maxdd"]
    srt = ok.sort_values("plateau", ascending=False)
    print(f"=== {name}: top 30 plateaus by 2023-2024 Sharpe (>= {min_trades} trades) ===")
    print(srt.head(30)[show].round(2).to_string(index=False))
    for n in (10, 50, 200):
        t = srt.head(n)
        print(f"top {n:4d} plateaus: IS median {t.is_sharpe.median():.2f}  2025 median {t.y2025_sharpe.median():.2f}  "
              f"2025 > 0: {(t.y2025_sharpe > 0).mean():.0%}")
    df.to_parquet(f"results/{name}_plateau.parquet")


if __name__ == "__main__":
    main()
