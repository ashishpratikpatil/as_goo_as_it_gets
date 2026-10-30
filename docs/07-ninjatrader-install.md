# Install into NinjaTrader 8

Live compile directory (do not replace this with the git copy as the only source):

```
C:\Users\ashis\OneDrive\Documents\NinjaTrader 8\bin\Custom
```

This repo is a **snapshot**. Edit and compile in NinjaTrader; copy back here when you want git to see the change.

## Copy

| Repo file | Destination |
|---|---|
| `ninjascript/Indicators/AgigTosRevAmtSwingMarks.cs` | `Custom\Indicators\` |
| `ninjascript/Indicators/AdvancedSmoothedChannel.cs` | `Custom\Indicators\` |
| `ninjascript/Indicators/AgigRegimeFilter.cs` | `Custom\Indicators\` |
| `ninjascript/Strategies/AgigTosRevAmtSwingStrategy.cs` | `Custom\Strategies\` |
| `ninjascript/Strategies/AgigOrbSd2Strategy.cs` | `Custom\Strategies\` |
| `ninjascript/Strategies/AgigRegimeSwitchStrategy.cs` | `Custom\Strategies\` |

NinjaTrader.Custom.csproj already lists these names after a successful compile. If you add a **new** file, compile once in the NinjaTrader UI so the project file picks it up.

## Compile

1. Close charts that hold an old version if NinjaTrader has the script locked.
2. In NinjaTrader: **New → NinjaScript Editor → compile (F5)**.
3. Do not send F5 if NinjaTrader is not the foreground app.
4. Do not hand-edit the generated `#region NinjaScript generated code` at the bottom of each file. Duplicate generated regions cause `CS0111` / `CS0102`.
5. Set `BarsRequiredToTrade` only in `SetDefaults` or `Configure`, never in `DataLoaded`.

After a green compile, add the overlay or strategy to a chart from the Indicators / Strategies lists. Names:

- AGIG TOS RevAmt Swing Marks
- AdvancedSmoothedChannel
- AGIG Regime Filter
- AGIG TOS RevAmt Swing Strategy
- AGIG ORB SD2 Strategy
- AGIG Regime Switch Strategy

## Chart time zone

ORB and regime session clocks use **bar open** in the chart’s time zone. Use a template whose session is US/Eastern (or equivalent) for NY 09:30–16:00 to match the Python engine.

## What not to commit from Custom

`bin\`, `obj\`, `NinjaTrader.Custom.dll`, and NinjaTrader’s database. Only the six `.cs` files above belong in this repo.
