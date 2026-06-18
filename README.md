# sn2-extract

A C# / [CUE4Parse](https://github.com/FabianFG/CUE4Parse) pipeline that extracts
creature ("fauna") data from **Subnautica 2** (Unreal Engine 5.6) and joins it
into a single structured JSON catalog: name, categories, databank description,
scan duration, stats, and abilities, one record per creature.

Built as a from-scratch reverse-engineering exercise: run the general
game-data-extraction methodology on a brand-new title where the off-the-shelf
tools don't work yet.

> This repo contains **code only**. It does not redistribute Subnautica 2 game
> data or the community mappings file. You supply those locally (see Setup).

## Why this was non-trivial (the interesting part)

Subnautica 2 is bleeding-edge UE5.6, so every packaged tool was too old. The
walls, in order, and how each was cleared:

1. **pyUE4Parse (the Python port) couldn't read it at all.** Its IoStore
   table-of-contents reader didn't support **TOC version 8**, which SN2 ships. It
   only saw the tiny stub `.pak` (~4,900 files), never the IoStore `.ucas` where
   the real game data lives.
2. **The CUE4Parse NuGet package (1.2.2) couldn't read the mappings.** SN2's
   `.usmap` is **format version 4**; the published package is too old, and NuGet
   had nothing newer because the package lags the source.
3. **Fix: build CUE4Parse from source.** The current `FabianFG/CUE4Parse` targets
   **.NET 10** and supports both TOC v8 and usmap v4. Reference it directly
   instead of the NuGet package. This is the "be first on a day-one game"
   capability: when the tool is too old, use the live source.

Mounted result: **39,572 files** indexed (vs ~4,900 from the stale Python tool).

## How it works

```
Content/Paks (global.* + Subnautica2-Windows.{pak,utoc,ucas})
   -> DefaultFileProvider, EGame.GAME_UE5_6, zero AES key (unencrypted)
   -> FileUsmapTypeMappingsProvider(<the .usmap>)        # the schema/decoder
   -> Initialize()                                        # mount IoStore + pak
   -> roster from /Data/ScanData/Fauna/                   # ~47 fauna
   -> per creature, join its scattered DataAssets by name -> flatten -> JSON
```

Subnautica 2 uses the `DA_` **DataAsset** convention (not Palworld-style `DT_`
DataTables), and one creature's data is spread across several assets, joined by
the creature's name:

| Source | Provides |
|--------|----------|
| `Data/BioScans/DA_<name>_BioScanData` | name + icon |
| `Data/DatabankEntry/DA_<name>_DatabankEntry` | categories + full PDA description |
| `Data/ScanData/Fauna/DA_<name>_ScanData` | scan duration |
| `Blueprints/.../InitialAttributes/GE_<name>InitialAttributes` | stats (via the Gameplay Ability System) |
| `Data/AbilitySets/Creatures/DA_<name>AbilitySet` | granted abilities |

Stats live in Unreal's **Gameplay Ability System (GAS)**: the
`GE_<name>InitialAttributes` GameplayEffect sets starting attribute values via
Modifiers, which the pipeline flattens into `{ AttributeName: value }` (e.g.
Hammerhead: MaxHealth 1000, Food 100, Bulk 30, MaxSwimSpeed 500, MaxStamina 100).

### The join gotcha (the real RE detail)

The game names its own assets inconsistently across tables: the scan list calls
one creature `Deepwing`, but its databank is `DA_DeepwingBrooder_DatabankEntry`
and its ability set is `DA_DeepwingLeviathanAbilitySet`. A naive exact-name join
silently drops that data.

The fix is a **fail-safe join**: prefer the exact name; if there's no exact
match, fall back to a **unique** prefix match within the right folder; if the
prefix is ambiguous (more than one candidate), refuse to guess and leave it
null. So it recovers `Deepwing -> DeepwingBrooder` without ever risking mapping
the wrong creature's data onto another (e.g. `Hammerhead` vs
`HammerheadHeatVariant`). Missing data is acceptable; wrong data is not.

## Verification

Verified field-for-field against the community wiki on a creature **not** used
during development, the **Collector Leviathan**:

- databank description: verbatim match
- scan duration: 4s (matches)
- category: Cephalopods (matches)
- stats: empty, and **correctly so** the Collector cannot be killed, so it has
  no health attribute and no `InitialAttributes` asset

## Results

- **47 fauna entries**, collapsing to **~41 distinct species** (the remainder are
  heat/sub-variants with their own scan entries).
- ~33 have databank descriptions; 10 define GAS stats (the aggressive creatures;
  passive fauna genuinely have none); 15 have ability sets.
- Empty fields are characterized, not mysterious: variants inherit base-creature
  lore, passive creatures lack combat stats, a few early-access entries have no
  written lore yet.

## Setup (supply these locally; not committed)

1. **.NET 10 SDK.**
2. **CUE4Parse from source**, next to this folder:
   `git clone --recursive https://github.com/FabianFG/CUE4Parse`
   (the `.csproj` references `..\CUE4Parse\CUE4Parse\CUE4Parse.csproj`).
3. **A Subnautica 2 `.usmap`** for your game build, placed in `mappings/`, and
   point `UsmapPath` in `Program.cs` at it.
4. Set `PakDir` to your `Subnautica2\Content\Paks` folder.
5. `dotnet run` -> writes `creatures.json`.

## Known limitations / next steps

- Paths are hardcoded; would move to config/CLI args for automation.
- Verification is manual; would add automated assertions (e.g. Collector
  scan == 4, Hammerhead health == 1000) so a game patch that breaks extraction
  fails loudly.
- Abilities are reference names, including generic ones; would resolve them to
  their underlying values and filter boilerplate.
- Fauna only; the same pattern extends to flora, items, and biomes.

## Disclaimer

For educational / reverse-engineering practice. Subnautica 2 and its assets are
property of Unknown Worlds Entertainment. No game data or third-party mapping
files are distributed in this repository.
