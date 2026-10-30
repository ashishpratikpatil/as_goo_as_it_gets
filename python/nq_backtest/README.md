# `nq_backtest`

Scripts import sibling modules (`import data`, `import engine`). Run them from this folder:

```powershell
cd agig\python\nq_backtest
python sanity.py
python final_compare.py
python regime.py
python grid.py mr
python grid.py orb
```

## Core (`engine.py`)

- `nt_atr` — NinjaTrader Wilder ATR.
- `agig_zigzag` — TOS RevAmt state machine (`ST_UNDEF/UP/DN`).
- `ss_channel` — SuperSmoother mean and TR envelope.
- `sim_mr` — mean-revert strategy.
- `sim_orb` — ORB strategy (NY 09:30–16:00, OR lock, Fade/Breakout, exits, R target).
- `stats` — net, Sharpe, drawdown, trade counts.

## Data (`data.py`)

`AGIG_NQ_CSV` overrides the default Downloads path. Resamples with `closed="right", label="right"` so bar timestamps stay **close** stamps, matching NinjaTrader minute bars.

In-sample cut `IS_END = 2025-01-01` is used by some grid scripts to hold out 2025.

## Scripts

One-off research drivers. They write parquet/png under `results/` when that folder exists. Create it locally:

```powershell
mkdir nq_backtest\results, nq_backtest\cache
```
