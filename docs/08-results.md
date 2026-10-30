# Results snapshot (NQ 1-minute CSV, 2022-12-26 → 2025-12-11)

These are research prints from `python/nq_backtest`, not a live track record. Costs: $20/pt, $4.50 RT + 1 tick. Sharpe is daily annualized.

Equity curves from that run:

- Mean-revert (grid pick): `docs/images/mr_equity.png`
- Mean-revert longs only: `docs/images/mr_long_equity.png`
- ORB (grid pick): `docs/images/orb_equity.png`

Year / VIX / Hurst diagnostics: `docs/regime.json`.

## Settings that won the grid (this sample)

**ORB** — 5-minute, Fade, band lookback 3, channel 300, outer 2.8, ScanArrowsStop, OrOpposite stop, 2R target, AGIG direction off.

**Mean-revert** — 15-minute, longs only, ATR 14 / reversal 1.5, channel 200, outer 2.0, target OppositeBand.

## Settings that did not

- NT 2026 chart mean-revert: target Mean, channel 150, outer 2.5 — net loser on this CSV.
- Default ORB (Breakout + AGIG direction + AgigFlip + swing stop) was behind Fade / ScanArrows / OrOpposite / 2R.
- Regime ADX + trend-day gates did not remove the 2020–early-2023 losing stretch on the longer NT sample.

## How to reproduce

```powershell
cd agig\python\nq_backtest
$env:AGIG_NQ_CSV = "C:\Users\ashis\Downloads\archive\Dataset_NQ_1min_2022_2025.csv"
python final_compare.py
python mr_long.py
```

Full grids write parquet files locally (`results/*.parquet`, gitignored). Re-run `grid.py` / `orb_fine.py` if you need the ranking tables again.

Treat 2023–2025 strength as **one regime**, not proof the same knobs work in 2020–2022. See [05-regime-switch.md](05-regime-switch.md).
