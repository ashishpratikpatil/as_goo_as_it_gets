"""Year-by-year walk-forward: pick the top settings on one year, see how they do the next year."""
import sys
import pandas as pd

name = sys.argv[1]
min_trades = int(sys.argv[2]) if len(sys.argv) > 2 else 50
df = pd.read_parquet(f"results/{name}.parquet")
for train, test in (("y2023", "y2024"), ("y2024", "y2025"), ("is", "y2025")):
    ok = df[df[f"{train}_trades"] >= min_trades].sort_values(f"{train}_sharpe", ascending=False)
    line = [f"pick on {train:5s} -> test {test}:"]
    for n in (1, 10, 100, 1000):
        t = ok.head(n)
        line.append(f"top{n}: train {t[f'{train}_sharpe'].median():.2f} test {t[f'{test}_sharpe'].median():.2f} "
                    f"({(t[f'{test}_sharpe'] > 0).mean():.0%}>0)")
    print("  ".join(line))
    print(f"   all settings median test Sharpe {df[f'{test}_sharpe'].median():.2f}")
