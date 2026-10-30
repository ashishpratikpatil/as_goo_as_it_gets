# Python research engine

Numba ports of the NinjaTrader zigzag, SuperSmoother channel, mean-revert strategy, and ORB strategy. Fills match **OnBarClose / next-bar open**. Use this folder to grid, drill, and walk-forward on the NQ 1-minute CSV. It is not a live trading client.

## Setup

```powershell
cd agig\python
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Point at the CSV (not stored in git):

```powershell
$env:AGIG_NQ_CSV = "C:\Users\ashis\Downloads\archive\Dataset_NQ_1min_2022_2025.csv"
```

The file used in the 2022–2025 study is 1,048,575 rows, 2022-12-26 18:01 through 2025-12-11 20:52, timestamps as **bar close** in US/Eastern.

Run scripts from `nq_backtest` (they import `data` and `engine` as siblings):

```powershell
cd nq_backtest
python sanity.py
python final_compare.py
python grid.py orb
python regime.py
```

## Package map

| Module | Purpose |
|---|---|
| `nq_backtest/data.py` | Load CSV, resample 1/2/3/5/10/15-min, cache `.npz` |
| `nq_backtest/engine.py` | Zigzag, channel, `sim_mr`, `sim_orb`, stats |
| `nq_backtest/sanity.py` | Smoke test against a short slice |
| `nq_backtest/grid.py` | Coarse MR + ORB Sharpe grid |
| `nq_backtest/analyze.py` / `analyze_fine.py` | Rank parquet grids |
| `nq_backtest/orb_fine.py` / `orb_drill.py` | ORB neighborhood and equity |
| `nq_backtest/mr_drill.py` / `mr_long.py` | Mean-revert neighborhood; longs-only |
| `nq_backtest/nbhd.py` | Local parameter neighborhood |
| `nq_backtest/robust.py` / `wf.py` | Robustness and walk-forward |
| `nq_backtest/detail.py` | Single-config run + report |
| `nq_backtest/final_compare.py` | Print the recommended ORB configs |
| `nq_backtest/regime.py` | Year splits, VIX, day-trade filters |

Cache (`cache/*.npz`) and grid dumps (`results/*.parquet`) are gitignored. Rebuild with `data.py` / `grid.py`. Equity PNGs from the last study live in `docs/images/`.

## Fill model (must match NT historical)

- Signal on bar `i` close → fill at bar `i+1` open.
- Stop and target are live from the entry bar. If a bar hits both, **stop fills first**.
- A gap through stop or target fills at the open.
- Costs: NQ **$20/point**, **$4.50** round-turn plus **1 tick** slippage.
- Sharpe is **daily**, annualized. NinjaTrader Strategy Analyzer often reports **monthly** Sharpe — do not mix them.

## Costs and instrument

Hard-coded for NQ (CME Nasdaq-100 futures). Change multipliers in `engine.py` if you switch products.

## What this is not

- Not a C++ engine.
- Not a replacement for NinjaTrader live execution (no DOM, no order management, no IB/Rithmic).
- Not a claim that the 2022–2025 grid will hold in 2020 or 2026. The 2020–early-2023 NT sample lost money on the same ideas.
