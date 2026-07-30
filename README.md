# Sons Of The Forest — Live Editor

A **live** save editor for Sons Of The Forest. Everything applies to the running game
instantly — no saving, no reloading, no quitting to the menu. Give yourself items,
spawn things in front of you, edit your health, run any console command, and toggle
cheats, all from a separate desktop window while the game keeps running.

It is two pieces:

| | |
|---|---|
| **The mod** | An in-game plugin (RedLoader) that exposes the game's own systems over a local socket. It has **no GUI** and draws nothing on screen. |
| **The app** | A standalone Windows window with all the controls. It talks to the mod over `127.0.0.1`. |

Splitting it this way means the editor is a real window you can alt-tab to, resize,
and put on a second monitor — instead of an in-game overlay fighting the game for
your mouse.

> **📄 [SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt](SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt) — every item ID and console command for the current build.**
> 308 items and 418 commands, read out of a running game and cross-checked against two
> independent decompiles. Useful on its own even if you never install the mod. See
> [How the reference was built](#how-the-reference-was-built) for why it disagrees with
> the lists that get copied around.

---

## Install

1. Download `SOTF-LiveEditor.zip` from [**Releases**](../../releases/latest).
2. Unzip it anywhere.
3. Run **`INSTALL.bat`**.

That is the whole thing. The installer finds your game on any drive (~50 ms, via
Steam's registry entry and library list), installs RedLoader if you don't already
have it, drops the mod into `Mods\`, installs the app, and makes a Desktop shortcut.
It asks nothing and needs no admin rights.

Then: **start the game, load a save, open "SOTF Live Editor" from your Desktop.**

<details>
<summary>If the installer can't find your game</summary>

Point it at the folder and run it again:

```bat
setx SOTF_GAME_DIR "D:\Games\Sons Of The Forest"
```

The app has its own escape hatch too — put the path in a `gamedir.txt` next to
`LiveEditorApp.exe`.
</details>

**Uninstall:** delete `Mods\LiveEditor.dll`, `Mods\LiveEditor\` and `LiveEditorApp\`
from your game folder. Nothing is written to your saves, so they are unaffected.

---

## What it does

### Items
The full item database, searchable, with the game's real display names ("Modern Axe",
not `ModernAxe`). Multi-select works like File Explorer — click, shift-click for a
range, ctrl-click to pick individually — and every action applies to the whole
selection.

- **Add** to your inventory, any quantity
- **Remove** from your inventory
- **Spawn** in the world, either where you're looking or in front of you

Results report what actually happened, not just that the command ran. Adding at your
stack cap reports `0 added — already at max` rather than claiming success.

### Item Data
Per-instance item data — the thing that makes your axe different from another axe.
Ten module types, six of them plain on/off toggles:

`plating` (solafite coating) · `pauseDecay` (food never rots) · `arrowUpgradeActive` ·
`gpsActive` / `gpsPulse` / `gpsBeep` · `limbFresh`, plus numeric `ammoCount`,
`ammoType`, `variant` and `fill`.

Selecting an item loads its real current values in, so you only change what you touch.
You can apply to an item you already own — **including the one in your hands** — or
build a brand new one with the settings baked in. Applying to a held item repaints it
immediately.

### Player
Read and set health, stamina, hydration, fullness, rest, vitality, sickness and
strength; temperature is read-only because the game exposes no setter for it. A
**Behaviour** column shows each stat's regen/fade rate and health's regen target, so
it's obvious which stats the game will keep moving on its own.

Health is a two-value system in this game — a current value and a `targetHealth`
ceiling that natural regeneration heals up to. Writes go through the game's own
`Vitals` setters and raise the target first, so the value actually sticks.

### Console
Every console command the game has, with its argument type, run from the app with the
output captured back. Includes the 59 commands that only exist at runtime and are
missed by lists built from static scans — `addcharacter` among them.

### Tools & Cheats
- **Cheat gate bypass** — the console normally refuses to work without cheats enabled,
  and refuses again when you're not the host. This re-opens it.
- **Spawn unlock** — 60 items (artifact pieces, blueprints, story notes) carry a
  `canBeSpawned = false` flag that `spawnitem` obeys. Flipping it makes them work
  through the real console command, which means they replicate in multiplayer.
- **Restored commands** — `buildhack` was removed from the console but its machinery
  still exists on `StructureCraftingSystem`. It's revived here as
  `restored.buildhack`, along with `restored.instantbuild`.

### Multiplayer
Works in multiplayer. Spawning picks a strategy automatically: direct instantiation in
singleplayer (position-exact), Bolt networked instantiation when you're the host, and
console passthrough as a client. You need to be the host or have cheats enabled — the
same requirement the vanilla console has.

---

## How it works

```
┌──────────────────┐   NDJSON over TCP    ┌────────────────────────────┐
│  LiveEditorApp   │ ───────────────────► │  LiveEditor plugin         │
│  (WPF, separate  │   127.0.0.1:8271     │  (RedLoader, in-process)   │
│   process)       │ ◄─────────────────── │                            │
└──────────────────┘   responses + push   └─────────────┬──────────────┘
                                                        │ marshalled to
                                                        │ the main thread
                                                  ┌─────▼──────┐
                                                  │  the game  │
                                                  └────────────┘
```

- **Transport** — newline-delimited JSON over a loopback TCP socket. One JSON object
  per line, `id`-correlated so responses can come back out of order, plus unsolicited
  push events (`world.entered`, `world.exited`, `session.changed`).
- **Auth** — the plugin writes a random 128-bit token to `UserData\LiveEditor.port` at
  startup and requires it in a `hello` frame. Loopback only; the listener is never
  reachable from another machine.
- **Threading** — Unity is not thread-safe, and calling into IL2CPP off the main thread
  is a hard native crash, not an exception. Every command is queued onto the game's
  update loop and awaited. Requests are pipelined, so a 1000-command batch takes
  ~200 ms rather than one command per frame.

26 commands are exposed: `ping`, `state.get`, `describe`, `item.*` (list, resolve,
count, add, remove, plate, modules, spawn), `spawn.unlock`, `spawn.restore`,
`recon.prefab`, `console.*` (list, exec, introspect, unlock), `restored.*`,
`dev.loadsave`, `dump.items`, `player.*` (get, set, restore) and `cheats.force`.

`describe` returns the live schema for all of them, so the protocol is
self-documenting — connect and ask.

Full protocol spec, architecture notes and the reverse-engineering findings are in
**[PLAN.md](PLAN.md)**.

---

## How the reference was built

[`SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt`](SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt) is
not a copy of the lists that circulate on wikis and forums. It was generated from:

- **Command existence** — the live console's own registry in a running game.
- **Command arguments** — cross-checked against two independent decompiles
  (Il2CppInterop proxy assemblies and an Il2CppDumper dump). Zero disagreements.
- **Items** — the live `ItemDatabaseManager`. Item definitions live in Unity assets
  rather than in code, so a running game is the only authoritative source; no
  decompile can produce them.

Consequences worth knowing:

- **308 items, not 352.** The commonly-copied lists carry debug and unrealized IDs that
  don't exist in this build.
- **418 commands, not ~314.** Lists built by scanning for `_<name>` methods miss every
  command registered at runtime.
- **No invented descriptions.** A command is only described if it was executed and
  observed, or its actual source was read. A blank means "not verified", not "does
  nothing" — which is more honest than the filler text in most lists.
- **RedLoader-added commands are tagged** `[RedLoader-only]`, because 16 of them don't
  exist in the vanilla game.

Regenerate it with `tools/generate-reference.ps1` after a game patch.

---

## Build from source

Needs the [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) and a copy of
the game with RedLoader already installed (the plugin compiles against the loader's
generated interop assemblies).

```bash
dotnet build plugin/LiveEditor/LiveEditor.csproj -c Release
dotnet publish app/LiveEditorApp/LiveEditorApp.csproj -c Release -o dist/app
```

The plugin build copies itself into your `Mods\` folder automatically. The game folder
is detected the same way the installer does it; override with `-p:GameDir="..."` or the
`SOTF_GAME_DIR` environment variable.

`tools/` holds the scripts used to inspect the game and regenerate the reference data:

| Script | Purpose |
|---|---|
| `Find-GameDir.ps1` | Shared game-folder detection |
| `dump-type.ps1` | Dump a type's fields/methods from the interop assemblies |
| `enum-console-commands.ps1` | Enumerate the build's console commands |
| `generate-reference.ps1` | Rebuild the root reference txt |
| `test-client.ps1` | Minimal NDJSON client for poking the plugin |

---

## Compatibility

Built and verified against Steam buildid **20228174** (Unity 2022.2.16f1, IL2CPP) with
**RedLoader 0.8.6**.

Game updates that change `GameAssembly.dll` can break the plugin, because it binds to
game internals by name. It fails loudly rather than silently doing the wrong thing.

## Safety

Nothing here writes to your save files. Every change is made to the live session, so
the worst case is that you quit without saving and lose the change. That said — it is
a cheat tool that mutates game state, so keep a backup of saves you care about.

## Credits

- [**RedLoader**](https://github.com/ToniMacaroni/RedLoader) by ToniMacaroni — the
  mod loader and SonsSdk this is built on.
- [**Il2CppDumper**](https://github.com/Perfare/Il2CppDumper) by Perfare — used for the
  second, independent decompile that command arguments were cross-checked against.

## License

[MIT](LICENSE). Unofficial and fan-made; not affiliated with or endorsed by Endnight
Games. No game assets or game code are distributed in this repository.
