# TrueFreeSolo

A MelonLoader mod for **Cairn** that removes all food-based stat boosts while preserving chalk effects. Experience the mountain as a true free solo climb.

## Features

- **Automatically active** - just install and play
- **Blocks all food boosts** - RestSpeed, Grip, Temperature, and hidden Stamina from food
- **Preserves chalk boosts** - Stamina and SuperGrip from chalk remain active
- **Zero configuration** - no hotkeys, no settings, pure challenge
- **Lightweight** - minimal performance impact

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) (v0.7.x recommended)
2. Place `TrueFreeSolo.dll` in your `Cairn/Mods/` folder
3. Launch the game

## How It Works

In Cairn, boosts have a `remainingBoostUnits` field that tracks their duration:
- **Chalk boosts** are measured in **grabs** (typically 12-24)
- **Food boosts** are measured in **seconds** (typically 100-300)

The mod checks this value and removes any boost with `remaining > 30`, which reliably identifies food-based boosts.

### Why Not Filter by Boost Type?

Some food items give **hidden boosts** beyond what the UI displays. For example:
- Chocolate shows a Grip boost, but internally **also grants Stamina**
- Energy bars may provide multiple hidden stat bonuses

Since chalk also gives Stamina boosts, we can't simply block all "Stamina" boosts without also removing chalk effects. The duration-based approach solves this problem elegantly.

## Technical Details

- **Game**: Cairn (Unity 6, IL2CPP)
- **Mod Loader**: MelonLoader 0.7.x
- **Data Path**: `GameDataManager.gameData.climberData.boosts`
- **Structure**: `Dictionary<BoostType, List<BoostData>>`

### BoostType Enum
| Index | Name |
|-------|------|
| 0 | Stamina |
| 1 | Strength |
| 2 | RestSpeed |
| 3 | Grip |
| 4 | Temperature |
| 5 | Toughness |
| 6 | Burst |
| 7 | SuperGrip |
| 8 | PsychBoost |

### BoostData Fields
| Field | Type | Description |
|-------|------|-------------|
| source | BoostSource | Origin of the boost (Item, Status, etc.) |
| itemId | int | ID of the item that granted the boost |
| limitType | BoostLimitType | Seconds or Grabs |
| value | float | Boost strength multiplier |
| remainingBoostUnits | float | Duration remaining (seconds or grabs) |
| fromSuperGripper | bool | Whether from super gripper chalk |

## Building from Source

```bash
cd Cairn/ModSource/TrueFreeSolo
dotnet build -c Release
copy bin\Release\TrueFreeSolo.dll ..\..\Mods\
```

## License

MIT License - Feel free to use, modify, and distribute.

## Credits

- **Author**: Zhexirox
- **Game**: Cairn by The Game Bakers
