# NoBuffsOnlyChalk

A MelonLoader mod for **Cairn** that eliminates all stat buffs while preserving chalk effects. Experience the mountain as a true free solo climb.

## Important Note

Buff icons from food/drinks will still appear in the UI, but after consumption they disappear instantly — the stat effect is completely blocked.

## Features

- **Automatically active** — just install and play
- **Blocks all food boosts** — Focus, Grip, Temperature, Grit, and hidden boosts
- **Removes Grit shield completely** — HP and impact protection from Toughness
- **Preserves chalk boosts** — Stamina and SuperGrip from chalk remain active
- **Zero configuration** — no hotkeys, no settings, pure challenge
- **Lightweight** — minimal performance impact

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) v0.7.2 (Nightly builds only)
2. Place `NoBuffsOnlyChalk.dll` in your `Cairn/Mods/` folder
3. Launch the game

## How It Works

In Cairn, boosts have a `remainingBoostUnits` field that tracks their duration:
- **Chalk boosts** are measured in **grabs** (typically 12-24)
- **Food boosts** are measured in **seconds** (typically 100-300)

The mod checks this value and removes any boost with `remaining > 24`, which reliably identifies food-based boosts.

**Special case:** Toughness (boostType 5) grants a shield with low `remainingBoostUnits` (impacts, not seconds). These are always removed regardless of the threshold, and `OnShieldLost` is fired to update the UI.

### Why Not Filter by Boost Type?

Some food items give **hidden boosts** beyond what the UI displays. For example:
- Chocolate shows a Grip boost, but internally **also grants Stamina**
- Energy bars may provide multiple hidden stat bonuses

Since chalk also gives Stamina boosts, we can't simply block all "Stamina" boosts without also removing chalk effects. The duration-based approach solves this problem elegantly.

## Inspiration

This mod was inspired by a comment from **yked**:
> "I would like to play completely no-buff, but I'd have to throw away some food which I find ridiculous. So, I just ignore the buffs completely except for the grit. No grit."

**Note:** Playing with chalk or without is your choice — the mod only blocks food buffs ;)

## Planned Improvements

- Remove food/drink buff icons from the UI entirely

## Bug Reports & Feedback

Found a bug or something feels off? Please [open an issue](https://github.com/zhexirox/NoBuffsOnlyChalk-Cairn/issues) — any feedback helps improve the mod!

## Technical Details

- **Game**: Cairn (Unity 6, IL2CPP)
- **Mod Loader**: MelonLoader 0.7.2
- **Data Path**: `GameDataManager.gameData.climberData.boosts`
- **Structure**: `Dictionary<BoostType, List<BoostData>>`
- **Shield Path**: `climberData.shieldHpRemaining` / `shieldImpactsRemaining`
- **Events**: `OnShieldLost`, `OnBoostRemoved`

### BoostType Enum
| Index | Name | Blocked? |
|-------|------|----------|
| 0 | Stamina | Food only (chalk preserved) |
| 1 | Strength | Food only |
| 2 | RestSpeed | Food only |
| 3 | Grip | Food only |
| 4 | Temperature | Food only |
| 5 | Toughness | Always (grants shield) |
| 6 | Burst | Food only |
| 7 | SuperGrip | Food only (chalk preserved) |
| 8 | PsychBoost | Food only |

### BoostData Fields
| Field | Type | Description |
|-------|------|-------------|
| remainingBoostUnits | float | Duration remaining — grabs (≤24 = chalk) or seconds (>24 = food) |
| source | BoostSource | Origin of the boost (Item, Status, etc.) |
| itemId | int | ID of the item that granted the boost |
| limitType | BoostLimitType | Seconds or Grabs |
| value | float | Boost strength multiplier |
| fromSuperGripper | bool | Whether from super gripper chalk |

## Building from Source

```bash
cd Cairn/ModSource/TrueFreeSolo
dotnet build -c Release
copy bin\Release\TrueFreeSolo.dll ..\..\Mods\
```

## License

MIT License — Feel free to use, modify, and distribute.

## Credits

- **Author**: Zhexirox
- **Game**: Cairn by The Game Bakers
