# Python backtest

Research replica of the NinjaTrader strategies. It is built so a grid on the NQ 1-minute CSV is cheap enough to run overnight.

## Dataset

Default path (override with `AGIG_NQ_CSV`):

```
C:\Users\ashis\Downloads\archive\Dataset_NQ_1min_2022_2025.csv
```

- 1,048,575 rows
- 2022-12-26 18:01 → 2025-12-11 20:52
- Timestamps are **bar close** in US/Eastern (first Globex minute stamps 18:01, last RTH minute 16:00, cash close 17:00)

Resample with `closed="right", label="right"` so 5-minute and 15-minute bars keep close stamps.

The CSV is **not** in git. Cache `.npz` files are gitignored; `data.py` rebuilds them.

## Fill model

Matches NinjaTrader historical, standard fill resolution, `Calculate = OnBarClose`:

1. Decision on bar `i` close.
2. Market entry fills at bar `i+1` open.
3. Stop and target are active on the entry bar and every bar after.
4. If a bar’s range touches stop **and** target, **stop fills first**.
5. A bar that opens through a stop or target fills at that open.

## Costs

| Item | Value |
|---|---|
| Point value | $20 (NQ) |
| Round-turn commission | $4.50 |
| Slippage | 1 tick (0.25 pt → $5) |

Sharpe is **daily close-to-close equity**, annualized (`sqrt(252)`). NinjaTrader Strategy Analyzer often shows **monthly** Sharpe. They are not comparable.

## How to run

From `agig/python/nq_backtest`:

```powershell
$env:AGIG_NQ_CSV = "C:\path\to\Dataset_NQ_1min_2022_2025.csv"
python sanity.py
python grid.py orb
python final_compare.py
python regime.py
```

`IS_END = 2025-01-01` in `data.py` is the in-sample cut some scripts use so 2025 is held out.

## Engine map

See `python/nq_backtest/README.md`. The three Numba sims (`sim_mr`, `sim_orb`, stats) are the portable spec if you ever port this stack to another language. There is **no C++** implementation here.

## Limitations vs live NT

- No intra-bar stop/target race except the stop-first rule.
- No order-book, partial fills, or session-template quirks.
- OR clock uses bar-open minutes derived from close-stamped bars (`open_minute = close_stamp − tf`).
- VIX in `regime.py` comes from Yahoo (`yfinance`) and is aligned approximately; it is not the CBOE futures series NinjaTrader would plot.
