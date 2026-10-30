# ORB: AGIG ORB SD2 Strategy

Opening-range breakout or fade, filtered by the outer channel and optionally by AGIG swing direction. Ported from the TOS ORB study (session OR lock, close-cross of OR high/low) and wired to the same AGIG + channel stack as the mean-revert strategy.

## Session and opening range

Times are compared to the **bar open** in the chart time zone.

| Session | Clock (chart TZ) |
|---|---|
| NY (default) | 09:30–16:00 |
| London | 03:00–11:00 |
| Asian | 19:00–03:00 |
| Globex | 18:00–17:00 |
| Custom | `CustomStart` / `CustomEnd` as HHmm |

The OR builds on the first `OrWindow` minutes of the session, then **locks**. `Auto` uses the chart bar size (1 / 5 / 10 / 15 / 30 / 60). After lock:

- **ORB Long** when close crosses **above** locked OR high.
- **ORB Short** when close crosses **below** locked OR low.

`UseSafeEntryWindow` (default on) blocks new entries in the last **3.5 × OR length** before session end. `MaxTradesPerSession` default **1**. `ExitAtSessionEnd` default on.

## Band and AGIG filters

`BandMode`:

- **Breakout** — long at/above R2, short at/below S2.
- **Fade** — long at/below S2, short at/above R2.

`RequireAgigDirection` (NT default **on**): long only after a confirmed Long mark, short only after a confirmed Short mark. The Python grid preferred this **off**.

## Exits and stops

| `ExitMode` | Flatten when |
|---|---|
| `AgigFlip` (NT default) | Opposite confirmed AGIG mark |
| `OrbOpposite` | Opposite OR close-cross |
| `ScanArrowsStop` | Same-direction arrows stop / opposite appears (TOS-style) |

| `StopMode` | Stop |
|---|---|
| `SwingPoint` (NT default) | AGIG swing ± buffer |
| `OrOpposite` | Opposite side of the locked OR |
| `FixedTicks` | `StopLossTicks` |

`TargetRMultiple` 0 = no R target. Python grid used **2R** with OrOpposite stops.

## Live defaults vs Python grid (NQ 2022–2025)

| Knob | NT default | Grid recommendation |
|---|---|---|
| Timeframe | chart | 5-minute |
| Band mode | Breakout | Fade |
| Channel length / outer | 200 / 2.415 | 300 / 2.8 |
| Band lookback | 0 | 3 bars (≈ 15 min on 5-min) |
| AGIG direction | on | off |
| Exit | AgigFlip | ScanArrowsStop |
| Stop | SwingPoint | OrOpposite |
| Target | none | 2R |

## Files

- `ninjascript/Strategies/AgigOrbSd2Strategy.cs`
- Python: `sim_orb`, `orb_fine.py`, `orb_drill.py`, `final_compare.py`
