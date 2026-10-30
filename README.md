# AGIG + ORB (NinjaTrader + Python)

Live NinjaTrader 8 strategies and indicators for the **TOS ZigZagHighLow dollar RevAmt** swing, the **AdvancedSmoothedChannel**, the **opening-range** strategy, and a **regime switch** that can run both. The same logic is ported to **Python** for grid and walk-forward research on NQ 1-minute data.

NinjaTrader NinjaScript is **C#**, not C++. There is no C++ port in this tree. The compiled live copy stays in OneDrive `NinjaTrader 8\bin\Custom`. This folder is the documented snapshot you push to git.

## Layout

```
agig/
  README.md                         this file
  .gitignore
  docs/                             formulas, install, backtest notes
  ninjascript/
    Indicators/                     overlay + channel + regime filter
    Strategies/                     mean-revert, ORB, combined switch
  python/
    requirements.txt
    nq_backtest/                    Numba engine + grid / drill scripts
```

| Path | Role |
|---|---|
| `ninjascript/Indicators/AgigTosRevAmtSwingMarks.cs` | TOS RevAmt zigzag overlay (Short / Long plots) |
| `ninjascript/Indicators/AdvancedSmoothedChannel.cs` | SuperSmoother mean ± inner / outer bands |
| `ninjascript/Indicators/AgigRegimeFilter.cs` | Trend-day / OR-extension / daily ADX+SMA gates |
| `ninjascript/Strategies/AgigTosRevAmtSwingStrategy.cs` | Mean-revert: fade outer-band swings |
| `ninjascript/Strategies/AgigOrbSd2Strategy.cs` | ORB + channel + optional AGIG direction |
| `ninjascript/Strategies/AgigRegimeSwitchStrategy.cs` | One strategy that runs ORB and/or mean-revert |
| `python/nq_backtest/` | Research engine matching OnBarClose fills |

## Source of truth

- **ZigZag:** Thinkorswim `ZigZagHighLow` dollar RevAmt. Do not use the NinjaTrader ZigZag indicator or the old AsGoodAsItGets script.
- **Live compile:** files under `C:\Users\ashis\OneDrive\Documents\NinjaTrader 8\bin\Custom`. Copy from this repo into that folder, then compile in NinjaTrader (F5). Do not edit NinjaTrader-generated `#region` blocks by hand.
- **Research:** `python/nq_backtest/engine.py` (Numba). Dataset default is `Dataset_NQ_1min_2022_2025.csv` (override with `AGIG_NQ_CSV`).

## Quick start

**NinjaTrader** — copy the six `.cs` files into `bin\Custom\Indicators` and `bin\Custom\Strategies` as documented in [docs/07-ninjatrader-install.md](docs/07-ninjatrader-install.md), then compile.

**Python**

```powershell
cd agig\python
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:AGIG_NQ_CSV = "C:\Users\ashis\Downloads\archive\Dataset_NQ_1min_2022_2025.csv"
cd nq_backtest
python sanity.py
python final_compare.py
python regime.py
```

Scripts import `data` / `engine` as sibling modules, so run them from inside `python/nq_backtest`. Grid: `python grid.py mr` or `python grid.py orb`.

## Research vs live defaults

NinjaTrader ships conservative chart defaults. The Python grid on NQ 2022-12-26 → 2025-12-11 (close-stamped ET) preferred different knobs. Use the live defaults on a chart; use the research set only after you have accepted the fill model in `docs/06-python-backtest.md`.

| Strategy | Live NT default | Python-grid recommendation (that sample) |
|---|---|---|
| ORB | NY session, Auto OR, Breakout, AGIG dir on, AgigFlip, swing stop, outer 2.415, channel 200 | 5-min bars, Fade, lookback 3, channel 300, outer 2.8, ScanArrowsStop, OrOpposite stop, 2R, AGIG dir off |
| Mean-revert | ATR rev 2 / len 5, channel 200, outer 2.415, target Mean, longs and shorts | 15-min, longs only, ATR 14 / rev 1.5, channel 200, outer 2.0, target OppositeBand |

The 2026 NT chart settings (Mean, channel 150, outer 2.5) lost money on this 2022–2025 CSV. Regime ADX / trend-day gates did **not** cleanly split 2020–early-2023 losses from the later rally. See [docs/05-regime-switch.md](docs/05-regime-switch.md).

## Docs

1. [TOS RevAmt zigzag](docs/01-zigzag-revamt.md)
2. [AdvancedSmoothedChannel](docs/02-smoothed-channel.md)
3. [Mean-revert strategy](docs/03-mean-revert-strategy.md)
4. [ORB strategy](docs/04-orb-strategy.md)
5. [Regime switch and filters](docs/05-regime-switch.md)
6. [Python backtest](docs/06-python-backtest.md)
7. [Install into NinjaTrader](docs/07-ninjatrader-install.md)
8. [Results snapshot](docs/08-results.md)

## What is not in this repo

- Kronos, third-party studies, old AGIG variants, L2 code
- `cache/*.npz` and `results/*.parquet` (tens to hundreds of MB; rebuild locally)
- The 1-minute CSV itself (keep it outside git)
- Compiled NinjaTrader DLLs

## Git

This directory is meant to be its own repository. From `agig/`:

```powershell
git init
git add .
git status
```

Do not commit until you have reviewed `git status`. Push only after a remote is added (`git remote add origin …` then `git push -u origin HEAD`).
