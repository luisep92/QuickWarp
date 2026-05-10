# QuickWarp — Hollow Knight: Silksong

A small BepInEx mod that lets you save and reload a position waypoint with one keystroke. Useful for boss practice, route runs, and skipping bench walks.

[**Download on Nexus Mods →**](https://www.nexusmods.com/hollowknightsilksong/mods/99)
*3,589 unique downloads · 59 endorsements · 26,777 views · current version 1.2.0*

---

## What it does

Press a key (default **F6**) to save your current scene + position. Press another key (default **F7**) to teleport back — the mod switches scene if needed and drops you exactly where you saved.

It does *not* save game state. It only restores your character's location. See [Caveats](#caveats).

## Why it exists

Silksong has long bench walks and boss reattempts that involve repeating the same traversal again and again. I wanted something lighter than a save-state mod and simpler than DebugMod for the specific case of *"warp me back to where I just was"*.

## Stack

C# · [BepInEx 6](https://github.com/BepInEx/BepInEx) · Unity (Hollow Knight: Silksong) · Newtonsoft.Json

## How it's built

Layered architecture, deliberately heavier than a 200-line side mod usually warrants. Two reasons: it keeps my own discipline honest, and Silksong updates are likely to require swapping the scene-switch implementation — clean boundaries make that a 5-minute change.

```text
QuickWarp.cs                    # BepInEx plugin entry, key polling, debounce
├── Application/
│   └── WarpController          # use cases: SaveHere(), LoadSaved (coroutine)
├── Domain/
│   ├── IWarpStorage            # persistence port
│   ├── ISceneSwitcher          # scene-switch port
│   └── WarpData                # plain entity (Scene, Position, Velocity)
├── Infrastructure/
│   ├── JsonWarpStorage         # Newtonsoft.Json file persistence
│   └── SceneSwitcher           # GameManager.BeginSceneTransition + 10s timeout
└── Configuration/
    └── ModConfiguration        # parses QuickWarp.cfg with KeyCode fallback
```

`Application` and `Domain` only depend on Unity for `Vector2/3`; `Infrastructure` is the only layer that touches `GameManager`, `HeroController`, or the filesystem. `Try*` patterns on storage so a corrupted save file doesn't throw on load.

## Install

1. Install [BepInEx](https://github.com/BepInEx/BepInEx/releases).
2. Download the [latest release from Nexus Mods](https://www.nexusmods.com/hollowknightsilksong/mods/99).
3. Unzip into the game's root folder. The `.dll` should land at:

   ```text
   <GameFolder>/BepInEx/plugins/QuickWarp/QuickWarp.dll
   ```

## Configuration

After running the game once, a config file is generated at `<GameFolder>/BepInEx/config/QuickWarp.cfg`:

```ini
saveWarpKey = F6
loadWarpKey = F7
```

Any [Unity `KeyCode`](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/KeyCode.html) name works.

## Usage

| Action | Default key |
|---|---|
| Save current waypoint | **F6** |
| Load saved waypoint | **F7** |

Common uses:

- Save before a boss → press load on death to skip the walk back.
- Save a tricky platforming spot → practice it without replaying the lead-up every time.
- Save before a fork in the path → explore both options without committing.

## Caveats

QuickWarp moves your character; it does **not** save or restore game state. That means:

- **Boss fights** triggered by entering a room may not re-trigger correctly when you warp back mid-fight.
- **Scripted events** (cutscenes, NPC dialog state, room flags) stay in whatever state the game already considers active — they don't rewind with you.

Use it for traversal and practice, not as a save-state replacement.

## License

[MIT](LICENSE). Not affiliated with Team Cherry.
