"""Summaries of a grid: best rows by 2023-2024 Sharpe, how each parameter value does on average,
and how the 2023-2024 winners hold up in 2025."""
import sys
import numpy as np
import pandas as pd

pd.set_option("display.width", 250)
pd.set_option("display.max_columns", 40)

name = sys.argv[1]
min_is_trades = int(sys.argv[2]) if len(sys.argv) > 2 else 100
df = pd.read_parquet(f"results/{name}.parquet")
params = [c for c in df.columns if not any(c.startswith(s + "_") for s in ("y2023", "y2024", "y2025", "is", "all"))]
print(df.shape, "params:", params)

ok = df[df["is_trades"] >= min_is_trades].copy()
print(f"rows with >= {min_is_trades} trades in 2023-2024:", len(ok))

ok["min_year"] = ok[["y2023_sharpe", "y2024_sharpe", "y2025_sharpe"]].min(axis=1)
show = params + ["is_trades", "is_net", "is_sharpe", "is_maxdd", "y2023_sharpe", "y2024_sharpe",
                 "y2025_trades", "y2025_net", "y2025_sharpe", "y2025_maxdd", "all_sharpe", "all_msharpe",
                 "all_sharpe_gross"]

print("\n=== Top 25 by 2023-2024 net Sharpe (2025 is out-of-sample) ===")
print(ok.sort_values("is_sharpe", ascending=False).head(25)[show].round(2).to_string(index=False))

print("\n=== Out-of-sample check: 2025 Sharpe of the top-N by 2023-2024 Sharpe ===")
srt = ok.sort_values("is_sharpe", ascending=False)
for n in (10, 50, 200, 1000):
    t = srt.head(n)
    print(f"top {n:5d}: IS median {t.is_sharpe.median():.2f}  2025 median {t.y2025_sharpe.median():.2f}  "
          f"2025 >0: {(t.y2025_sharpe > 0).mean():.0%}")
print(f"all rows: IS median {ok.is_sharpe.median():.2f}  2025 median {ok.y2025_sharpe.median():.2f}")

print("\n=== Each parameter value: median and 90th pct 2023-2024 Sharpe, median 2025 Sharpe ===")
for p in params:
    g = ok.groupby(p).agg(n=("is_sharpe", "size"), is_med=("is_sharpe", "median"),
                          is_p90=("is_sharpe", lambda x: x.quantile(0.9)),
                          oos_med=("y2025_sharpe", "median"),
                          all_med=("all_sharpe", "median"))
    print(f"\n{p}\n" + g.round(3).to_string())

print("\n=== Top 25 by worst single-year Sharpe (consistency across 2023, 2024, 2025) ===")
print(ok.sort_values("min_year", ascending=False).head(25)[show + ["min_year"]].round(2).to_string(index=False))
