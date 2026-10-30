import pandas as pd

pd.set_option("display.width", 240)
pd.set_option("display.max_columns", 40)
df = pd.read_parquet("results/orb_fine.parquet")
print("fine grid", df.shape)
ok = df[df.is_trades >= 80].copy()
ok["min_year"] = ok[["y2023_sharpe", "y2024_sharpe", "y2025_sharpe"]].min(axis=1)
params = ["tf", "or_min", "band_mode", "band_mult", "band_lb_min", "chan", "exit_mode",
          "stop_mode", "buf", "target_r", "max_trades"]
show = params + ["is_trades", "is_sharpe", "is_net", "is_maxdd", "y2023_sharpe", "y2024_sharpe",
                 "y2025_sharpe", "y2025_net", "all_sharpe", "all_net", "all_maxdd", "min_year"]
print("\n=== top 20 by 2023-2024 Sharpe ===")
print(ok.sort_values("is_sharpe", ascending=False).head(20)[show].round(2).to_string(index=False))
print("\n=== top 15 by worst year Sharpe ===")
print(ok.sort_values("min_year", ascending=False).head(15)[show].round(2).to_string(index=False))
print("\n=== top 15 by all-period Sharpe with min_year > 1.5 ===")
g = ok[ok.min_year > 1.5].sort_values("all_sharpe", ascending=False).head(15)
print(g[show].round(2).to_string(index=False))
print("\nparameter medians on rows with min_year > 1.5:")
sub = ok[ok.min_year > 1.5]
for p in params:
    print(p, sub.groupby(p).agg(n=("all_sharpe", "size"), med=("all_sharpe", "median"),
                                p90=("all_sharpe", lambda x: x.quantile(0.9))).round(2).to_string())
