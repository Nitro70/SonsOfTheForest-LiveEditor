# Sons of the Forest — Live Session Control Mod
## Planning & Research Document (pre-build)

Date: 2026-07-22

> **This is the original design document, kept as written before any code existed.**
> The project has since been built and works — see the [README](README.md) for what
> actually shipped. This file is retained because its findings, protocol spec and
> risk register are still the reference for how and why the mod is put together, and
> because the reverse-engineering notes are useful to anyone poking at this game.
> Where the built code diverges from the plan, the code is correct.

Verified against: a Steam install of the game,
Steam buildid **20228174** (public branch, current as of this date), Unity **2022.2.16f1**,
with RedLoader **0.8.6** interop assemblies generated on-disk.

Evidence method: unless otherwise labeled, "VERIFIED" claims below come from direct
Mono.Cecil metadata inspection of the Il2CppInterop-generated proxy assemblies in
`_Redloader\Game\` (exact type/member signatures from the current build), from files on
this machine, or from a named primary source (official repo/docs/SteamDB). "COMMUNITY"
claims name their source. "ASSUMED" claims are inferences that must be confirmed at
runtime before they are load-bearing — each one states how to confirm it.

Important caveat on VERIFIED scope: interop assemblies expose exact *signatures* but
their method bodies are marshalling stubs — game *behavior* cannot be read from them.
Signature claims are VERIFIED; behavior claims are labeled separately.

---

# 1. FINDINGS REPORT

## 1.1 Scripting backend, engine, build

| Fact | Value | Evidence |
|---|---|---|
| Backend | **IL2CPP** | VERIFIED — `GameAssembly.dll` (100 MB) + `SonsOfTheForest_Data\il2cpp_data\` present; no `Managed\` folder |
| Unity | **2022.2.16f1** (d535843d11e1) | VERIFIED — `SonsOfTheForest.exe` VersionInfo.ProductVersion |
| Steam build | **20228174**, installed 2025-10-18 | VERIFIED — `appmanifest_1326470.acf` |
| Build currency | **Current.** 20228174 is the latest public build as of 2026-07-22 | VERIFIED — steamcmd API (`api.steamcmd.net/v1/info/1326470`, timeupdated ≈ 2025-10-03) |
| What 20228174 changed | "Unity security update", 2025-10-03 — replaced **UnityPlayer.dll only** (fix for CVE-2025-59489). **GameAssembly.dll is unchanged since Patch 16 Hotfix 2 (2025-01-21)** | VERIFIED (SteamDB patchnotes/20228174, unity.com/security/sept-2025-01) + COMMUNITY (sonsoftheforest.wiki.gg/wiki/Updates) |

IL2CPP implications (VERIFIED from how RedLoader/Il2CppInterop work, see 1.2):
- Mod code runs in a **real .NET 6 CoreCLR** that RedLoader injects via UnityDoorstop.
  Full BCL is available — `System.Net.Sockets`, threads, `System.Text.Json`, etc. The
  network layer of the plugin is ordinary .NET code with no il2cpp involvement.
- Game types are called through **Il2CppInterop proxy assemblies** (already generated in
  `_Redloader\Game\`). Il2cpp fields surface as C# properties; strings marshal
  automatically; `null` maps to il2cpp null.
- Delegates passed *into* game APIs must be il2cpp delegates, e.g.
  `(Il2CppSystem.Func<string, bool>)managedMethod` — this exact cast is what SonsSdk
  itself does when registering console commands (VERIFIED — `GameCommands.cs` source).
- Generic game methods are AOT-compiled: only instantiations that exist in the shipped
  binary can be invoked. The APIs this project needs are all non-generic (verified in
  the signature dumps below), so this constraint does not bite.
- Harmony patching works through `Il2CppInterop.HarmonySupport` (native detours), with
  the usual IL2CPP caveats (inlined methods can't be hooked). This project's design
  **calls** game methods and does not depend on patching for its core path.

## 1.2 Loader ecosystem (current state, 2026-07)

- **RedLoader** (ToniMacaroni) — SOTF-specific fork of MelonLoader, .NET 6,
  Il2CppInterop-based, injected via UnityDoorstop `version.dll`. Latest release
  **0.8.6, 2025-02-26**. Repo dormant since ~2025-03 but not archived; no
  game-patch-breakage issues filed since (consistent with GameAssembly being frozen
  since Jan 2025). Docs: https://tonimacaroni.github.io/RedLoader/ .
  VERIFIED — github.com/ToniMacaroni/RedLoader releases/API.
- **This machine already has RedLoader 0.8.6 installed and proven**: `_Redloader\` with
  interop assemblies, `_Redloader\Latest.log` shows a clean run under Unity 2022.2.16f1
  loading Toni Macaroni's ItemSpawner 1.0.1 mod, item database initialized. VERIFIED —
  files on disk. **However, the loader is currently parked**: there is no `version.dll`
  or `winhttp.dll` at the game root. A Doorstop proxy set (`version.dll` +
  `doorstop_config.ini`) is stashed in the `redloaderhttp's\` subfolder; a separate
  BepInEx 6 install is stashed in `beplnexmods\` (with `winhttp.dll` proxy). Restoring
  the RedLoader pair to the game root re-enables it.
- **BepInEx 6 IL2CPP (bleeding edge)** — works with SOTF (Thunderstore
  `BepInExPack_IL2CPP` v6.0.755, updated 2026-03; 762k downloads), but there is no
  SOTF-specific SDK on that side, most SOTF BepInEx packages are 2023-era, and the
  community consolidated on RedLoader. be.755 is the recommended build; some newer
  bleeding-edge builds (~be.785) have IL2CPP regressions. VERIFIED/COMMUNITY —
  thunderstore.io, builds.bepinex.dev, BepInEx issue #725.
- **Vanilla MelonLoader** — no evidence of anyone running it on post-1.0 builds; the
  SOTF mod hub (sotf-mods.com, 243 mods, active uploads into June 2026) targets
  RedLoader exclusively. COMMUNITY.
- **Known active risk**: RedLoader issue #48 (2026-07-20) — a mid-July 2026 **Windows**
  update prevents RedLoader from injecting for some users; rolling the Windows patch
  back restores it. Track before building. COMMUNITY — github.com/ToniMacaroni/RedLoader/issues/48.
- **No existing mod exposes external/remote control** (no RCON/WebSocket/HTTP bridge
  found on sotf-mods.com, Thunderstore, or GitHub). Closest primitives: RedLoader's
  `boot.txt` (console commands run at startup) and launch args. COMMUNITY (absence
  across searches). This project fills an empty niche.

## 1.3 Inventory system

All signatures VERIFIED from `Sons.dll` interop:

- **`TheForest.Items.Inventory.PlayerInventory`** (MonoBehaviour) is the owner of player
  item state. Reached via **`TheForest.Utils.LocalPlayer.Inventory`** (LocalPlayer is a
  singleton MonoBehaviour with a static `_instance` and static convenience accessors).
- **The correct grant entry point:**
  ```csharp
  bool PlayerInventory.AddItem(int itemId, int amount = 1, bool preventAutoEquip = false,
                               bool wasCrafted = false, ItemInstance itemInstance = null)
  ```
  (default values VERIFIED from the NeuralBinary decompiled dump), plus a convenience
  overload directly on the player:
  `bool LocalPlayer.AddItem(int itemId, int amount, bool preventAutoEquip)`.
- **Production precedent** (VERIFIED — public mod source): ZombieMode_SOTF calls
  `LocalPlayer.Inventory.AddItem((int)ItemsId.PistolAmmo, 140);` and
  `AddItem((int)id, amount, true)`; RedNodeEditor calls
  `LocalPlayer._instance.AddItem(id, count)`. Passing no `ItemInstance` is the normal
  community pattern, which materially de-risks the null-instance question
  (github.com/ImAxel0/ZombieMode_SOTF `Gameplay/CustomInventory.cs`,
  github.com/ImAxel0/RedNodeEditor `RedNodeLoader/InventoryNodes/AddItemNode.cs`).
- Also available: `DropItemFromInventory(int itemId, out GameObject outResultingItem)`
  (VERIFIED — dump + used by RedNodeEditor's DropItemNode) — returns a handle to the
  dropped world object; load-bearing for spawn path C in 1.6.
- Related, same class: `RemoveItem(int, int, …)`, `Owns(int)`, `AmountOf(int itemId,
  bool includeEquipped, bool checkNextSlot)`, `HasRoomFor(int, int)`,
  `GetMaxAmountOf(int)`, `RefreshItemCache()`, `RemoveAllItems()`.
- **Authoritative count storage**: `PlayerInventory._itemInstanceManager` →
  **`Sons.Inventory.ItemInstanceManager`** with `TryAddItems(int itemId, int count,
  bool suppressCallback)`, `GetItemCount(int)`, `GetAllItems()`,
  `GetSerializedItems()`. Items are **`Sons.Inventory.ItemInstance`** objects (class,
  not struct); IDs are **`int`**; stack caps come from `ItemData.MaxAmount`.
- **Why AddItem and not TryAddItems directly**: `AddItem` is the layer that also raises
  the inventory events (below), handles auto-equip, quick-slot and instance creation
  logic; `TryAddItems(suppressCallback)` exists underneath it. Poking the manager
  directly risks state the UI/crafting layer never hears about. (Behavior inference —
  ASSUMED, high confidence from the signature layering; confirmed in Phase 2 testing.)
- **Change notifications** (VERIFIED signatures):
  `OnItemAddedEvent : UnityEvent<ItemInstance, int>`, `OnItemRemovedEvent`,
  `OnItemEquippedEvent`, `OnItemUnequippedEvent` (same shape). These fire from the
  add/remove path and are the hook the game's own systems use.
- **Consistency**: there is no carry-weight system in SOTF. Crafting availability and
  the inventory mat read possessed-item state that `AddItem` maintains
  (`_possessedItems`, `RefreshItemCache`). Achievements: no evidence that console/cheat
  use disables them — COMMUNITY silence, treated as unknown in the risk register.

## 1.4 UI refresh path

- The inventory UI is not a HUD widget — it is a **3D "bag mat" scene** rendered by a
  dedicated inventory camera (`LocalPlayer.InventoryCam`,
  `Sons.Inventory.InventoryCameraController`, `InventoryFakeGround`,
  `InventoryLayoutItem` / `InventoryLayoutItemGroup` per item stack). VERIFIED — type
  inventory in `Sons.dll`.
- Layout groups represent per-item visual stacks and are driven by inventory state;
  opening the inventory (a `PlayerViews` view change on
  `PlayerInventory._currentView`) activates the mat and its layout groups.
- **What this means for the no-reload requirement**: the authoritative state changes
  the moment `AddItem` returns; `OnItemAddedEvent` fires immediately. Whether the mat
  visually updates *while already open* is unconfirmed (ASSUMED: layout groups
  subscribe to the events and update live, since the game itself adds items while the
  mat is open when combining/crafting). Worst case is exactly your stated ceiling:
  close and reopen the inventory, which rebuilds/reactivates the layout from
  authoritative state. **Runtime confirmation**: add an item via console `additem`
  with the inventory open and watch the stack count — Phase 2 test, 30 seconds.

## 1.5 Item database

VERIFIED from `Sons.Item.dll` interop:

- **`Sons.Items.Core.ItemDatabaseManager`** — static API:
  `Initialize()`, `IsItemIdValid(int)`, `TryFindItemById(int, out ItemData)`,
  `ItemById(int)`, `ItemByName(string)`, `ItemIdByName(string)`,
  `ItemsByType(Types mask)`, `ItemsWithTag(string)`, `Items : List<ItemData>`.
- **`Sons.Items.Core.ItemData`** (ScriptableObject) — the item definition:
  `Id : int`, `Name : string`, `_aliases : List<…>`, `_tags`, `Type`,
  `MaxAmount : int` (stack cap), `_uiData : ItemUiData` (icon),
  **`PickupPrefab : Transform`**, `PickupBundlePrefab`, `HeldPrefab`, `PropPrefab`,
  `_canBeSpawned : bool`, `_maxWorldPickups : int`, `_maxVirtualPickups`,
  `_eachInstanceIsUnique : bool` (weapons/tools with per-instance state),
  `_isPerishable`, `_isVolumeContainer`, etc.
- Initialization is lazy and costs ~2.5 s (VERIFIED — `_Redloader\Latest.log`:
  "ItemDatabaseManager initialized took 2549ms" from the installed ItemSpawner mod).
  The plugin must call `Initialize()` once (or wait for the game to) before resolving.
- **Mapping your text file onto this**: your IDs are these `ItemData.Id` ints — the
  file's own provenance says it derives from a decompiled export, and spot checks line
  up (356 Modern Axe, 78 Log, etc. are the community-standard IDs). Per-entry
  validation is a runtime job (`IsItemIdValid`) because item definitions live in game
  assets, not in code — the framework does this at startup (section 5) and reports
  stale entries instead of hardcoding trust. ASSUMED (that all 352 entries resolve) —
  the startup report replaces assumption with fact per entry.

## 1.6 Spawn path (world item at look-at point)

- The game's own spawn route: console method `_spawnitem(string)` →
  `IEnumerator SpawnItemInternal(ItemData itemData, int count)` (both on
  `TheForest.DebugConsole`, VERIFIED signatures). It spawns "on the ground in front of
  you" (COMMUNITY — wiki/EIP, matches your file). Position is chosen internally; there
  is no position parameter.
- **How the SDK author's own mod spawns items** (VERIFIED — IL call-graph of the
  installed `Mods\ItemSpawner.dll` extracted with Cecil):
  1. reads `ItemData._canBeSpawned`; if false, **sets it to true** (temporarily
     defeating the "can't be spawned" flag on restricted items),
  2. calls `DebugConsole.Instance._spawnitem(itemData.Id.ToString())` — the underlying
     command method invoked **directly as a method**, bypassing console-open state and
     input parsing,
  3. gates everything on `LocalPlayer.IsInWorld`.
  This is the strongest possible community precedent: the loader author ships this.
- **Prefab source**: `ItemData.PickupPrefab : Transform` is the world-pickup prefab
  (VERIFIED property; `SonsSdk.ItemTools.GetPickupPrefab(int)` wraps it). Direct
  `UnityEngine.Object.Instantiate(pickupPrefab, pos, rot)` is therefore available as a
  position-controlled spawn.
- **Look-at position** (VERIFIED signatures): `LocalPlayer.MainCamTr : Transform` /
  `MainCam : Camera` give the view ray. SonsSdk ships exactly the helper needed:
  `SonsSdk.SonsTools.CastToTerrainFromCamera(float maxDistance) : Vector3` and
  `CastToTerrain(Vector3 origin, Vector3 dir, float maxDistance)`. A plain
  `Physics.Raycast` from `MainCamTr` is the fallback if the terrain-only mask proves
  too narrow (e.g., aiming at a structure floor); the exact layer mask to use is
  ASSUMED until tested — start with SonsTools, add a raycast variant if needed.
- **Direct-instantiate has production precedent** (VERIFIED — public mod source): the
  IngameShop mod ships exactly this:
  ```csharp
  Transform t = LocalPlayer._instance._mainCam.transform;
  Physics.Raycast(t.position, t.forward, out hit, 25f, LayerMask.GetMask("Terrain"));
  Instantiate(ItemDatabaseManager.ItemById(id).PickupPrefab.gameObject,
              hit.point + Vector3.up * 0.1f, LocalPlayer.Transform.rotation);
  ```
  (github.com/move123456789/IngameShop `IngameTools/AttatchToShop.cs`). The `"Terrain"`
  layer mask is the known-good mask; world pickups carry a `PickUp` component with
  `_itemId`, `_itemDataCached`, `ItemInstance` fields (VERIFIED — RedLoader
  `ItemBuilder.SetupPickup` rewires exactly those).
- **Three candidate designs**, decided by a Phase 2 test, all planned:
  - **A (position-exact, primary candidate)**: raycast → `Instantiate(PickupPrefab)`
    at hit point + small up-offset, let physics settle — the IngameShop pattern above.
    Remaining risk: whether a directly instantiated pickup is registered with
    world-persistence bookkeeping (`_maxWorldPickups` accounting, world-object
    locators), i.e. does it survive save/reload. ASSUMED-risk (no community evidence
    either way); the test is: spawn, save, reload, check the object survives and is
    interactable.
  - **B (game-blessed console path)**: `_canBeSpawned` flip + `_spawnitem` (the
    ItemSpawner technique) → item lands near the player, position not controllable.
    Persistence is whatever the game natively does. Relocating what it spawned
    requires diffing nearby `PickUp` instances after the coroutine — brittle; only
    used if both A and C fail.
  - **C (game-blessed with a handle)**: `AddItem(id, 1)` →
    `DropItemFromInventory(id, out GameObject)` → teleport the returned object to the
    raycast point. Fully game-created pickup (persistence should match any
    player-dropped item) *and* we get the exact GameObject back. Side-effect
    suppression knobs exist on `PlayerInventory` (VERIFIED properties):
    `SkipNextAddItemWoosh`, `DontShowDrop`, `BlockDrop` — test which are needed to
    make it silent. Per-item caveat: items the inventory refuses (full stack,
    `_canNotBeStored`) need the A path anyway.
  Decision rule (single-player): A if it passes save/load + interactability
  (simplest, no inventory side effects); C if A's persistence fails; B as last
  resort. In multiplayer, B is the default regardless — game-native replication wins
  over position control (see 3.4). `item.spawn` reports which mode executed either way.
- **Unique-item lock**: your file notes most weapons/tools can't be re-spawned once
  collected ("unique-item lock"); `_maxWorldPickups`/`_maxVirtualPickups` and
  `_eachInstanceIsUnique` are the visible knobs (VERIFIED fields, ASSUMED semantics).
  The `_canBeSpawned` flip covers the console-side restriction; whether world-pickup
  caps also gate direct instantiation is part of the same Phase 2 test.

## 1.7 Developer console

VERIFIED from `Sons.dll` interop unless noted:

- Class **`TheForest.DebugConsole`** (MonoBehaviour, singleton `Instance` property).
  It still ships in the current build, fully populated.
- **Built-in commands are instance methods named `_<command>`** — e.g.
  `_additem(string nameOrId)`, `_spawnitem(string itemIdentifier)`,
  `_godmode(string onoff)`, `_goto(string)`, `_save(string slotIndexArg)`,
  `_cheats(string toggle)`. I enumerated **359** of them on the current build. The
  console builds its dispatch dictionary (`_availableConsoleMethods`) from these; the
  in-game text path is `HandleConsoleInput(string)` / `SendCommand(string)`.
- **Dynamic commands**: `static RegisterCommand(string, Il2CppSystem.Func<string,bool>,
  Il2CppSystem.Object sender)`, `RegisterBoolCommand`, `UnregisterCommand`,
  `TryGetDynamicCommands`, stored in `_dynamicCommands`. This is the game's own
  extension point — SonsSdk's `[DebugCommand]`/`GameCommands.RegisterFromType` route
  registers into it (VERIFIED — SonsSdk source, GameCommands.cs).
- **Gating**: `IsConsoleAllowed()`, `IsConsoleBlocked()`, `SetCheatsAllowed(bool)`,
  `SetBlockConsole(bool)`; SdkEvents exposes `OnCheatsEnabledChanged`. The manual
  activation ritual (`cheatstick` + F1, US layout caveat) is COMMUNITY-documented but
  moot here: **RedLoader itself already opens the gate** (VERIFIED — RedLoader
  `SonsSdk/Private/SdkEntryPoint.cs`): in single-player
  (`!BoltNetwork.isRunning`) it unconditionally runs
  `DebugConsole.SetCheatsAllowed(true); DebugConsole.Instance.SetBlockConsole(false);`,
  and its `boot.txt` feature replays commands through `SendCommand(line)` with the
  console UI never open — proving `SendCommand` works headless. In multiplayer the
  cheat state follows the host's "Allow Cheats" setting (Harmony patch on
  `GameSetupManager.GetMultiplayerCheatsSetting` → `OnCheatsEnabledChanged`).
  Additional precedent for direct method calls: AxelModMenu calls
  `DebugConsole.Instance._season("spring")` and `SendCommand("togglegrass")` in
  production (VERIFIED — github.com/ImAxel0/AxelModMenu `Environment.cs`). The design
  defaults to **direct method invocation** with `SendCommand` for dynamic commands;
  Phase 2e is now a confirmation pass, not a discovery pass.
- **Console-routing vs direct-internals comparison** (design decision, section 3):
  routing through the console surface gives all 359 commands for free with one
  dispatcher and inherits the game's own arg parsing; calling internals directly gives
  typed arguments, real return values, and structured errors, but only for paths we
  hand-write. The plan uses **both**: typed internal calls for the headline operations
  (add/spawn/remove/list), generic console passthrough for the long tail.
- **Output capture**: command feedback goes through `LogCommandInfo`/`LogCommandFailed`
  into the console's log queue (`_logs`) and Unity's log. For passthrough results the
  plugin captures `Application.logMessageReceived` (managed subscription via interop)
  during command execution — best-effort text capture, labeled as such in the
  protocol. ASSUMED (that command output reliably reaches the Unity log) — verified in
  Phase 4; if it doesn't, passthrough returns only success/failure.

## 1.8 Save serialization

- **`Sons.Save.SaveGameManager`** (singleton, VERIFIED) holds registered serializer
  lists with before/after save+load callbacks and events, plus static entry points
  (VERIFIED — NeuralBinary dump + RedLoader Harmony patches on them):
  `Save(SaveGameType, string gameName, int saveId = 0)`, `Load(int saveId,
  SaveGameType)`, **`TriggerQuickSave()`**, `TriggerQuickLoad()`,
  `GetData<T>(SaveGameType, int saveId)`.
  **`PlayerInventory` itself implements
  `Sons.Save.ISaveGameSerializer<PlayerInventorySaveData>`** with
  `OnSerialize()`/`OnDeserialize(data)` (VERIFIED — interface on the interop type;
  method bodies present in the decompiled dump at lines 1458/1466).
- **Implication**: saving serializes *current in-memory state*. Anything added via
  `AddItem` is in that state; **no explicit flush is needed** — the next manual/auto
  save picks it up. Corroborated by absence: no surveyed community mod does anything
  special to persist granted items. Residual ASSUMED sliver closed by a trivial
  Phase 2 test: add item → save → reload → item present.
- Programmatic save for testing and for `save.now`: `SaveGameManager.TriggerQuickSave()`
  (preferred — writes the quicksave slot, doesn't touch named saves) or console
  `_save(string slotIndexArg)`; SonsSdk exposes `SonsSaveTools`,
  `SonsTools.TryGetSaveGameIds`, and `SdkEvents.BeforeSaveLoading/AfterLoadSave`
  hooks. VERIFIED signatures.
- This project intentionally does **not** write save files. The "live save editor" is
  live-state editing; serialization is the game's job.

## 1.9 Threading

- Rule: **every game API call happens on the Unity main thread.** The plugin's socket
  I/O lives on ordinary .NET background threads (safe — pure BCL), and marshals work
  via a queue drained by a main-thread callback.
- The loader-native mechanism (VERIFIED — `RedLoader.dll` types):
  **`RedLoader.GlobalEvents.OnUpdate`** (a `MelonEvent` fired every frame on the main
  thread; also `OnFixedUpdate`, `OnLateUpdate`) and **`RedLoader.Coroutines`**
  (`Start(IEnumerator)`, `Stop`) for multi-frame operations (needed for
  `SpawnItemInternal`, which is a coroutine). SonsSdk adds `SdkEvents.OnInWorldUpdate`
  (fires only while in a world) — the natural drain point for gameplay commands.
- Do **not** rely on il2cpp thread-attach to call game code from socket threads; Unity
  APIs are main-thread-only regardless. The dispatcher (section 3) is the only
  component that touches game state.

## 1.10 Your item/command file vs. the live build

File: `SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt` (repo root)
(1,318 lines). Three machine-relevant shapes (all VERIFIED by reading it):

1. **Part 1 item table** (lines ~32–386): fixed-width `ID  NAME  NOTES`, 352 entries,
   IDs 78–749, notes carry provenance/category/hidden-removed flags in semi-structured
   `[source]` bracket tags.
2. **Part 2 sections 1–5** (lines ~427–1050): block format — bare command name line,
   then indented `Usage:` and `Does:` lines; interrupted by loose prose lists
   (`addcharacter` variants, notes).
3. **Part 2 final table** (lines ~1060–1310): fixed-width
   `command  argtype argname  description` — a decompiled export mirror. The argtypes
   (`string onoff` / `object o` / `int`) match the real method signatures I dumped.

**Cross-check against the live build** (file table vs. the 359 enumerated `_` methods):

- **21 file commands do not exist in the current build's DebugConsole**:
  `addallbookpages, addallstoryitems, addvirginia, aidodgetest, aighostplayer,
  aipause, ammohack, applydefaultmaterials, buildhack, combatteststart, enablecheats,
  listitems, listitemswithtags, removeallstoryitems, spawnpickup, trailer3, veganmode,
  virginiagiveitem, virginiasentiment, virginiavisit, vrfps`.
  Some may exist as *dynamic* commands registered at runtime by other systems (not
  visible statically — the startup reconciliation in section 5 settles each one);
  the rest are genuinely pre-1.0 leftovers. Corroboration that the file partly mirrors
  an old build: the public NeuralBinary decompiled dump (pre-1.0 era) contains
  `_spawnpickup`, `_buildhack`, `_enableCheats` — all absent from today's build,
  all present in your file. Note `virginiasentiment`/`virginiavisit` live on in
  SonsSdk's own `GameCommands` (VERIFIED — methods exist there), i.e. they are
  re-provided by the SDK, not the game.
- **66 live commands are missing from the file** (regenerate any time with
  `tools\enum-console-commands.ps1 -RefFile <txt>`):
  `aiattentiondebug, ailognavcuts, ailogspawntimes, aishowanimtags, aishowaudio,
  aivisiondebug, autodriveraft, billboardlogalivechanges, burnbodyradius, cheats,
  colorgrade, creepyattackparty, dagdebug, dagtestmode, dagtestnext, dagtestprev,
  damageradius, doorclose, dooropen, dumpmeshinfo, dumptransforminfo,
  dynamicrescameradebug, enablecollisionbasedkillbox, equipitem, fakedrown, fakegpu,
  fogfakenotsupported, gibradius, golfcartnetworkdebug, greeblelayer, greebles,
  greeblezone, inventoryspeedsensitivity, iswaterdisplacementenabled,
  itemgroupinteractionaudio, localvoicedebugging, logworldobjectlocatormanagerdata,
  materialsnapshot, meshcollidermeshlog, mipmapstreamingdebug,
  networktransformlodupdate, newfogrendering, oceancolliderdebug, playerkillstats,
  playerstriggertraps, regrowalltrees, resetheldanim, resetinputaxes,
  restoreallworldlocators, robbystateinfo, setfirsttimeseenitem, setplayerdataflag,
  setterrainaniso, shockradius, showvitals, skinnedmeshesinfo, skunksmell,
  sleepcooldown, stonehack, testautobuildeffigy, timmypathdebug,
  treemapdistributionfilter, uiforcelastupdate, uiforcelateupdate,
  virtualpickupdebug, virtualplayers`.
- **Conclusion that drives the design**: the file cannot be the source of truth. At
  runtime the plugin enumerates the *actual* command set and item DB, and your file is
  loaded as a **metadata overlay** (descriptions, usage text, warnings). Stale entries
  are reported, not trusted. No hand-maintained list ever goes stale silently again.

---

# 2. DECISIONS (answered 2026-07-22 — sections 3–7 are final under these)

1. **Multiplayer: IN SCOPE for v1.** Rationale (user): console `additem`/`spawnitem`
   already work in multiplayer when the host allows cheats, so the plugin must too.
   Consequences threaded through the plan:
   - a **session-state model** (SP / MP-host / MP-client × cheats-enabled) replaces
     the simple in-world gate — see 3.4;
   - per-command **MP annotations** in the registry and `describe` — see 4.4/5.1;
   - in MP the spawn command **defaults to the game-native console path** (whatever
     replication `_spawnitem` natively has) instead of direct instantiation — see 3.4;
   - a dedicated **multiplayer validation phase** in the build guide (Phase 8);
   - new protocol error `E_CHEATS_DISABLED` and a `session.changed` push event.
   Known facts feeding this (VERIFIED from RedLoader source): networking is Photon
   Bolt; in SP RedLoader force-enables cheats/console; in MP the cheat gate follows
   the host's "Allow Cheats" setting (observed via the
   `GameSetupManager.GetMultiplayerCheatsSetting` patch → `OnCheatsEnabledChanged`)
   and non-permitted clients get the console blocked; locally instantiated pickups
   and `AddItem` calls are client-local — replication needs the game's own paths or
   custom Bolt packets.
2. **Client trust model: token file** (loopback bind + per-launch 128-bit token in
   `UserData\LiveEditor.port`, ACL'd to user). Decided as specified in 4.2.
3. **Steam updates: fix-on-break.** Keep an archived copy of the current
   `_Redloader\Game\` interop set; on a future patch, Cecil-diff old vs new for the
   rename list. No depot pinning.
4. **Dead commands: surface as `unavailable` in v1.** Emulation layer
   (`addallstoryitems`/`addallbookpages` via `ItemsByType` + `AddItem`, `spawnpickup`
   ≡ `item.spawn`, `ammohack` via `RangedWeaponItemInstanceModule`) is a recorded v2
   candidate, not in scope.
5. **External app stack: not planned now.** Protocol stays language-agnostic
   NDJSON/TCP; build guide delivers only a minimal REPL client for testing. (Also
   accepted: the reference txt is copied into `UserData\LiveEditor\`; original on
   The repo-root copy remains the master.)

No open questions remain. Remaining unknowns are runtime facts, not decisions — each
is an ASSUMED label in section 1 paired with a Phase 2 or Phase 8 test.

---

# 3. ARCHITECTURE DOCUMENT

## 3.1 Component breakdown

```
┌─────────────────────────── game process (SonsOfTheForest.exe) ───────────────────────────┐
│  RedLoader (.NET 6 CoreCLR, Doorstop)                                                    │
│  └─ LiveEditor.Plugin (SonsMod)                                                          │
│     ├─ Net/        TcpListenerHost      loopback listener, NDJSON framing, auth token    │
│     │              ClientSession        per-connection read loop, write queue            │
│     ├─ Core/      MainThreadDispatcher  ConcurrentQueue<Job> drained in OnUpdate;        │
│     │                                   per-frame time budget; TCS completion            │
│     │              CommandRegistry      name → ICommandHandler; describe + MP metadata   │
│     │              SessionState         SP/MP-host/MP-client × cheats × in-world ×       │
│     │                                   db-ready; gates every command (3.4)              │
│     ├─ Commands/  TypedCommands         item.add / item.spawn / item.remove / item.list  │
│     │                                   state.get / save.now …  (direct API calls)       │
│     │              ConsolePassthrough   console.exec + auto-registered console.<cmd>     │
│     │                                   from runtime enumeration                         │
│     ├─ Data/      OverlayFileLoader     parses your txt (3 shapes) → metadata            │
│     │              Reconciler           overlay × live DB/console → per-entry status     │
│     └─ Push/      EventPublisher        world.entered/exited, item events, log capture   │
└──────────────────────────────────────────────────────────────────────────────────────────┘
                         ▲ TCP 127.0.0.1:<port>, NDJSON, request/response + push
┌────────────────────────┴─────────────────────────────────────────────────────────────────┐
│  External Control App (separate process; all UI lives here)                              │
│  protocol client · command palette fed by `describe` · item picker fed by `item.list`    │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

Plugin has **no GUI, no overlay, no keybinds** (SUI is available but unused by design).
One optional exception: a config flag to print the listener port/status line into the
loader log at startup.

## 3.2 Loader recommendation: RedLoader 0.8.6

Chosen over the alternatives because:
- **Proven on this exact build on this exact machine** (Latest.log), interop already
  generated, and the reference implementation for our spawn path (ItemSpawner) ships
  as a RedLoader mod by the loader's author.
- SonsSdk provides, for free: lifecycle events tied to game state
  (`OnGameStart`, `OnInWorldUpdate`, `OnWorldExited`, save-load events), console
  registration (`[DebugCommand]`/`GameCommands.RegisterFromType`), item helpers
  (`ItemTools`), position helpers (`CastToTerrainFromCamera`), and hot-reload
  scripting (0.8.6) that will shorten the spike phase.
- The mod ecosystem/community (sotf-mods.com) is RedLoader-only — future references,
  debugging help, and example code all live there.

Against the alternatives:
- **BepInEx 6 IL2CPP**: viable (be.755 works), same Il2CppInterop core, but no
  game-specific SDK — we'd re-implement SdkEvents/gating/helpers ourselves, and the
  SOTF BepInEx ecosystem is stale. Kept as the documented **fallback loader**: the
  plugin's game-facing code isolates SonsSdk usage behind small wrappers so a BepInEx
  port stays mechanical (listed in the risk register, not built now).
- **Vanilla MelonLoader**: no evidence it runs on current builds; RedLoader *is* the
  maintained MelonLoader lineage for this game. Rejected.
- Loader failure modes:
  - RedLoader is dormant (last release 2025-02). If a future game patch breaks it,
    fixes may be slow/none → mitigation: GameAssembly frozen since 2025-01; BepInEx
    fallback; interop regeneration is automatic on hash change.
  - July 2026 Windows Update injection issue (#48) → check before Phase 0; workaround
    currently is rolling back the specific Windows patch.

## 3.3 Threading model

- **Socket threads** (accept + per-client read): pure BCL, never touch game state.
- **MainThreadDispatcher**: `ConcurrentQueue<Job>`; subscribed to
  `GlobalEvents.OnUpdate`. Each Job = { command, args, TaskCompletionSource<Result> }.
  Drain loop: execute jobs until queue empty **or frame budget (default 3 ms)
  exhausted** — bulk scripts from the app can't hitch the game. Shape (for the
  executor; this is the whole trick):
  ```csharp
  // socket thread:                       // main thread (OnUpdate subscriber):
  var job = new Job(cmd, args);           while (queue.TryDequeue(out var job)
  queue.Enqueue(job);                            && budget.ElapsedMilliseconds < 3)
  var result = await job.Tcs.Task;            try { job.Tcs.SetResult(job.Run()); }
  // (with timeout)                           catch (Exception e) { job.Tcs.SetException(e); }
  ```
- Commands that are coroutines under the hood (spawn path B) run via
  `RedLoader.Coroutines.Start` and complete their TCS from the coroutine's end.
- Every job wrapped in try/catch: il2cpp-side exceptions surface as protocol errors,
  never unhandled on the game loop. Per-job timeout (default 10 s) returns
  `E_TIMEOUT` to the client while logging the stuck job.
- Responses are completed onto the session's outbound queue; the socket write thread
  serializes. No game-thread blocking on network I/O, ever.

## 3.4 Multiplayer model

Session states, detected at world entry and on change (`BoltNetwork.isRunning` /
`isServer` via interop; cheats via `SdkEvents.OnCheatsEnabledChanged` + initial read):

| State | Cheats | What the plugin allows |
|---|---|---|
| `sp` | always (RedLoader force-enables) | everything |
| `mp-host` | host's "Allow Cheats" setting | everything when on; cheat-class commands → `E_CHEATS_DISABLED` when off |
| `mp-client` | follows host setting (pushed by server) | same gate as host; commands execute with the game's own client-side semantics |

Design rules:
- **The plugin never forces cheats in MP.** RedLoader deliberately follows the host
  setting there; overriding it client-side is (a) unproven and (b) griefing-adjacent.
  The gate reports `E_CHEATS_DISABLED` and `state.get`/`session.changed` tell the app
  why. The host toggles Allow Cheats in game options (or `setgamesetupsetting
  GameSetting.Multiplayer.Cheats true`).
- **MP capability comes from riding the game's own paths.** The premise of MP support
  is that the console's `additem`/`spawnitem` already behave correctly in MP
  (COMMUNITY + user experience). Therefore in MP:
  - `item.add` uses `AddItem` (local-player inventory is client-local state,
    client-saved — `SaveGameType.MultiplayerClient` exists precisely for it; VERIFIED
    save-folder suffixes + `savePlayerOnly` save overload);
  - `item.spawn` **defaults to the console-native path** (B: `_canBeSpawned` flip +
    `_spawnitem`) so replication/persistence is whatever the game does natively;
    `at:"look"` degrades to spawn-in-front unless Phase 8 proves relocation of the
    spawned entity replicates. Direct-instantiate (path A) in MP is refused by
    default (`E_UNSUPPORTED`) because a client-local ghost object visible only to one
    player is worse than a refusal — Phase 8 can relax this per finding.
  - `console.exec` passes through untouched — identical to typing in the console,
    inheriting whatever MP semantics each command has. Per-command MP behavior is
    annotated in `describe` as `mp: "works" | "host-only" | "local-only" | "untested"`,
    seeded `untested` and updated from the Phase 8 matrix (persisted in
    `UserData\LiveEditor\mp-matrix.json` so results survive restarts).
- **Spawn-mode selection is therefore session-aware**: SP → A (or C per Phase 2);
  MP → B. The `item.spawn` result's `mode` field always reports what ran.

## 3.5 Failure modes by component

| Component | Failure | Behavior |
|---|---|---|
| Listener | port in use | log + increment port (bounded), surface via log; app discovers via port file (4.2) |
| ClientSession | malformed frame | error response if id parseable, else close connection; game unaffected |
| Dispatcher | job throws | structured `E_EXEC_FAILED` with exception string; queue continues |
| Dispatcher | job hangs (coroutine never ends) | TCS timeout → `E_TIMEOUT`; coroutine stopped via token |
| SessionState | command while not in world | `E_NOT_IN_WORLD` without touching game APIs |
| SessionState | cheat-class command in MP with host cheats off | `E_CHEATS_DISABLED` without touching game APIs |
| Reconciler | overlay file missing/corrupt | plugin fully functional; `describe` marks metadata absent; warning pushed |
| Game update | interop member renamed | plugin fails fast at startup with a version-mismatch log line, listener still answers `describe`/`state.get` with `degraded: true` |

---

# 4. PROTOCOL SPECIFICATION

## 4.1 Transport & framing

- **TCP, 127.0.0.1 only** (hard-coded bind address), default port **8271**,
  configurable in `UserData\LiveEditor.cfg`.
  - Why TCP over the alternatives: named pipes are Windows-idiomatic but complicate
    any future non-.NET client and tooling (no `telnet`/netcat/scripting); WebSocket/
    HTTP add framing+server weight the plugin doesn't need. Loopback TCP + NDJSON is
    inspectable with any tool and trivially clientable from every language. Your TCP
    assumption stands.
- **Framing: NDJSON** — every message is exactly one line of UTF-8 JSON terminated by
  `\n`. JSON string escaping guarantees no raw newlines inside a message. Max line
  length 1 MiB (larger → connection error). No binary; icons/images are not
  transferred in v1 (`item.list` carries names/ids only).
- Multiple concurrent clients allowed (each gets its own session); commands from all
  clients funnel through the single dispatcher FIFO.

## 4.2 Connection lifecycle

1. Plugin writes `UserData\LiveEditor.port` containing `<port>\n<token>` at listener
   start (token = random 128-bit hex, regenerated per launch; file ACL'd to user).
   The external app reads this file — no port scanning, no config drift.
2. Client connects, must send `hello` within 5 s:
   `{"id":1,"cmd":"hello","args":{"protocol":1,"token":"<hex>","client":"myapp/0.1"}}`
3. Server replies with capabilities (below). Wrong token or protocol → error + close.
4. Thereafter: requests, responses, and unsolicited push events interleave freely on
   the line stream. Responses correlate by `id`; pushes have no `id`.

## 4.3 Envelopes

Request:  `{"id": <int, client-chosen, monotonic>, "cmd": "<name>", "args": {…}?}`
Response: `{"id": <same>, "ok": true,  "result": {…}}`
       or `{"id": <same>, "ok": false, "error": {"code": "<E_*>", "message": "<human>", "data": {…}?}}`
Push:     `{"event": "<name>", "data": {…}}`

Error codes (closed set, additive-only across protocol versions):
`E_AUTH, E_PROTOCOL, E_BAD_ARGS, E_UNKNOWN_CMD, E_UNKNOWN_ITEM, E_NOT_IN_WORLD,
E_DB_NOT_READY, E_CHEATS_DISABLED, E_EXEC_FAILED, E_TIMEOUT, E_UNSUPPORTED, E_BUSY`.
(`E_CHEATS_DISABLED` carries `data:{mode:"mp-client"|"mp-host"}` so the app can tell
the user who has to flip the host's Allow Cheats setting.)

## 4.4 Command set (v1)

Meta / session:
- `hello {protocol, token, client}` → `{protocol:1, plugin:"<semver>", loader:"RedLoader 0.8.6",
  game:{build:20228174, unity:"2022.2.16f1"}, degraded:false}`
- `ping {}` → `{t:<server ms>}`
- `describe {}` → full command registry:
  `{commands:[{name, kind:"typed"|"console"|"console-dynamic", args:[{name,type,required}],
  doc:<from overlay|null>, status:"ok"|"stale"|"unlisted"|"unavailable",
  mp:"works"|"host-only"|"local-only"|"untested"}], protocol:1}`
  This is how the app discovers capabilities; it must render from this, not hardcode.
  `mp` annotations come from the persisted Phase 8 matrix (3.4), default `untested`.
- `state.get {}` → `{inWorld, dbReady, mode:"sp"|"mp-host"|"mp-client",
  cheatsEnabled, playerPos:[x,y,z]|null, view}`

Items (all gated on inWorld + dbReady):
- `item.list {filter?:string, type?:string}` → `{items:[{id, name, aliases, type,
  maxAmount, canBeSpawned, uniqueInstances, overlay:{notes}?}]}` — live DB, overlay-annotated.
- `item.resolve {q:"356"|"modern axe"}` → `{id, name}` (id → alias → name, case-insensitive)
- `item.add {item:<id|name>, qty:int=1, preventAutoEquip:bool=true}` →
  `{added:int, newCount:int, capped:bool}` (AddItem loop honoring MaxAmount; returns
  actual delta via ItemInstanceManager.GetItemCount before/after)
- `item.remove {item, qty:int=1}` → `{removed:int, newCount:int}`
- `item.count {item}` → `{count:int, owned:bool}`
- `item.spawn {item, qty:int=1, at:"look"|"front"=look, maxDist:float=30}` →
  `{spawned:int, pos:[x,y,z]|null, mode:"direct"|"dropped"|"console"}` — mode reports
  which spawn path executed (1.6/3.4). Session-aware: SP uses the Phase-2 winner
  (`direct`/`dropped`, position-exact); MP uses `console` (game-native replication),
  where `at:"look"` degrades to spawn-in-front and `pos` is null unless Phase 8
  proves relocation replicates. `at:"look"` with no raycast hit → `E_BAD_ARGS`.
- `item.equip {item}` → `{}` (via `_equipitem` / TryEquip)

Console:
- `console.list {}` → `{builtin:[…names], dynamic:[…names]}` (runtime enumeration)
- `console.exec {line:"godmode on"}` → `{output:[…captured lines], durationMs}` —
  dispatches to the `_<cmd>` method directly; falls back to `SendCommand` for dynamic
  commands. Output capture is best-effort (1.7).

Save:
- `save.now {}` → `{}` (`SaveGameManager.TriggerQuickSave()` — quicksave slot only,
  never named saves) — for test automation; app should confirm with user. MP note:
  on a client this saves client player-data only (the game's `savePlayerOnly` /
  `SaveGameType.MultiplayerClient` path); world state is the host's to save.

Push events (v1): `world.entered {mode, cheatsEnabled}`, `world.exited {}`,
`session.changed {mode, cheatsEnabled}` (host toggles Allow Cheats mid-session —
wired to `SdkEvents.OnCheatsEnabledChanged`), `db.ready {itemCount}`,
`reconcile.report {ok:int, stale:[…], unlisted:[…]}` (once per world load),
`item.added {id, qty}` / `item.removed {…}` (from PlayerInventory events, so the app
can live-refresh), `plugin.degraded {reason}`.

## 4.5 Versioning & compatibility

- `protocol` int, currently **1**. Server refuses `hello` with higher-than-supported
  protocol (`E_PROTOCOL` + `{supported:1}`), accepts lower and answers in that version.
- Within a protocol version: fields are add-only; unknown JSON fields are ignored by
  both sides; commands never change argument meaning (new behavior = new command name).
- `describe.status` communicates per-command availability so the app degrades
  gracefully when a game update kills a command instead of breaking the session.

## 4.6 Example session

```
→ {"id":1,"cmd":"hello","args":{"protocol":1,"token":"9f…","client":"repl/0.1"}}
← {"id":1,"ok":true,"result":{"protocol":1,"plugin":"0.1.0","game":{"build":20228174}}}
← {"event":"world.entered","data":{}}
← {"event":"db.ready","data":{"itemCount":352}}
→ {"id":2,"cmd":"item.add","args":{"item":"modern axe","qty":1}}
← {"id":2,"ok":true,"result":{"added":1,"newCount":1,"capped":false}}
→ {"id":3,"cmd":"item.spawn","args":{"item":78,"qty":3,"at":"look"}}
← {"id":3,"ok":true,"result":{"spawned":3,"pos":[1462.2,88.1,-712.9],"mode":"direct"}}
→ {"id":4,"cmd":"console.exec","args":{"line":"godmode on"}}
← {"id":4,"ok":true,"result":{"output":["godmode: on"],"durationMs":2}}
```

---

# 5. COMMAND FRAMEWORK DESIGN

## 5.1 Registration model

- `ICommandHandler { string Name; ArgSpec[] Args; CommandKind Kind;
  Task<Result> Execute(Args, Ctx); }` — instances registered into `CommandRegistry`.
- **Typed commands** are hand-written classes registered explicitly at plugin init
  (attribute-scan is unnecessary ceremony for ~12 commands; revisit if the set grows).
- **Console passthrough commands** are *generated at runtime*: after `db.ready`, the
  registry enumerates
  1. built-ins: interop reflection over `TheForest.DebugConsole`'s methods matching
     `^_[a-zA-Z]` (the same set the game's autocomplete uses), argtype derived from
     the parameter (`string` vs `Il2CppSystem.Object` no-arg),
  2. dynamics: `DebugConsole.TryGetDynamicCommands` / `_dynamicCommands` keys.
  Each becomes a `console`-kind handler dispatching per 4.4. Registered names are
  namespaced (`console.<name>`) so they can never shadow typed commands.
- Re-enumeration runs on every world load (dynamic commands appear at runtime), and
  `describe` responses always reflect the current registry. A `registry.changed` push
  is a v2 nicety, not in v1.
- Every handler carries an `MpPolicy` (`AllowAlways` — read-only commands like
  `item.count`; `AllowWhenCheats` — the default for everything cheat-class;
  `RefuseInMp` — direct-instantiate spawn mode) plus the observed `mp` annotation
  from the Phase 8 matrix file. SessionState consults the policy before dispatch;
  the annotation is advisory metadata for the app, never a gate.

## 5.2 Overlay file: loading, parsing, validation

- Source: your txt (copied to `UserData\LiveEditor\reference.txt` per open question 6;
  path configurable; file optional).
- Parsed at plugin init on a background thread (pure text work — no game APIs):
  - **Items**: from Part 1, regex per line `^(\d+)\s{2,}(.+?)\s{2,}(.*)$` between the
    table header and Part 2 marker → `{id, name, notes}`; `[bracket]` tags in notes
    preserved as provenance flags; `REMOVED`/`Hidden` substrings → `flags`.
  - **Command docs**: from Part 2 blocks — a bare `^[a-z][a-zA-Z0-9]+$` line followed
    by indented `Usage:`/`Does:` lines → `{name, usage, does}`.
  - **Command table**: from the final fixed-width table
    `^(\w+)\s{2,}(string|int|object|float)\s+(\w+)\s+(.*)$` → `{name, argtype,
    argname, desc}`; merged with block docs (block wins on conflict).
  - Everything unmatched is ignored; parse never throws; a per-section counter of
    parsed/skipped lines goes into the log for sanity.
- **Reconciliation** (main thread, after `db.ready`):
  - each overlay item id → `ItemDatabaseManager.IsItemIdValid` → `ok` / `stale`;
    name mismatches (overlay name ≠ `ItemData.Name`, alias-aware) → `renamed` note.
  - each overlay command → present in enumerated registry → `ok` / `stale`;
    registry entries absent from overlay → `unlisted` (fully usable, just undocumented).
  - Result: the `reconcile.report` push + persisted `UserData\LiveEditor\reconcile.json`
    so you can eyeball what your file gets wrong without connecting an app.
- **Unresolvable entries** stay visible in `item.list`/`describe` with
  `status:"stale"` — the UI can show-and-disable them rather than silently dropping.

## 5.3 Surviving game updates

- Nothing from the overlay file is ever trusted over runtime state; ids and commands
  are re-derived from the live process each launch. A game update therefore cannot
  make the framework *lie* — worst case entries flip to `stale`/`unlisted`.
- The typed layer touches exactly these interop members (the full break-surface):
  `LocalPlayer.{_instance, Inventory, MainCamTr, IsInWorld}`,
  `PlayerInventory.{AddItem, RemoveItem, AmountOf, TryEquip, OnItemAdded/RemovedEvent}`,
  `ItemInstanceManager.GetItemCount`, `ItemDatabaseManager.{Initialize, Items,
  ItemById, ItemIdByName, IsItemIdValid}`, `ItemData.{Id, Name, MaxAmount,
  PickupPrefab, _canBeSpawned}`, `DebugConsole.{Instance, RegisterCommand-family,
  _spawnitem, SendCommand}`, `SonsTools.CastToTerrainFromCamera`,
  `GlobalEvents.OnUpdate`, `Coroutines.Start`, SdkEvents lifecycle events.
  A startup **self-check** resolves each via interop reflection and logs pass/fail;
  any miss → `degraded:true` in `hello` with the surviving command subset still up.
  Post-update diagnosis = read that one log block, fix the renamed members.
- Keep the current `_Redloader\Game\*.dll` set archived before any game update;
  Cecil-diff old vs new to get an exact rename list in minutes (the technique used
  throughout this document).

---

# 6. STEP-BY-STEP BUILD GUIDE

Ordered so that the steps that could invalidate the design come first. Do not proceed
past a failed test; the design has named fallbacks at each decision gate.

**Phase 0 — Loader reactivation & baseline** (no code)
- Goal: RedLoader runs on the current build on this machine, today (also settles the
  Windows-update risk, issue #48, immediately).
- Do: copy `version.dll` + `doorstop_config.ini` from `redloaderhttp's\` to the game
  root. The parked config is VERIFIED-correct: `target_assembly =
  _Redloader\net6\Redloader.dll`, `coreclr_path = _Redloader\dotnet\coreclr.dll`,
  `corlib_dir = _Redloader\dotnet` — all relative to the game root, and the
  `_Redloader\` tree they point at is present. **Only one Doorstop proxy may exist at
  the root** — do not also place `beplnexmods\winhttp.dll` there (that BepInEx config
  additionally expects its `BepInEx\` tree at the root, which is currently nested;
  it is not usable as-parked). Fallback if the parked copy misbehaves: reinstall
  RedLoader 0.8.6 via RedManager (github.com/ToniMacaroni/RedManager) or the release
  zip (github.com/ToniMacaroni/RedLoader/releases/tag/0.8.6). Back up saves first
  (paths in Appendix C). Launch game.
- Test: `_Redloader\Latest.log` shows the Unity 2022.2.16f1 banner and ItemSpawner
  1.0.1 loading; in a world, ItemSpawner's backquote panel opens and can spawn a log.
  **This also re-validates the entire spawn-precedent path before we write a line.**

**Phase 1 — Toolchain + hello world**
- Goal: compile-and-load loop working.
- Do: .NET 6 class library per the scaffolding facts in **Appendix C** (SDK install,
  reference list, `manifest.json` schema, packaging layout); `SonsMod` subclass; log
  lines in `OnSdkInitialized`/`OnGameStart`.
- Test: both log lines appear in `Latest.log` at the right lifecycle moments.

**Phase 2 — SPIKE: the six design-validating experiments** (throwaway code, ideally
via RedLoader 0.8.6 hot-reload scripts for fast iteration)
Each experiment maps to an ASSUMED label in section 1 and converts it to fact:
- 2a **AddItem semantics**: in-world, call `LocalPlayer.Inventory.AddItem(356, 1,
  true, false, null)` (Modern Axe — unique-instance item) and `(78, 10, …)` (Log —
  stackable). Test: returns true; `AmountOf` reflects; open inventory → items visible
  and usable; a *second* axe respects unique-instance rules sanely. Risk is low —
  production mods pass no instance routinely (1.3) — but the unique-item second-copy
  case has no precedent. Fallback is the `_additem` console method (test it too for
  amount parsing: does `_additem("78 10")` add 10?).
- 2b **Live UI refresh**: run 2a once with inventory closed (then open — ceiling
  test) and once with it already open (does the mat update live?). Record which.
- 2c **Spawn path A** (IngameShop pattern, 1.6): raycast via
  `SonsTools.CastToTerrainFromCamera(30)` (fallback `Physics.Raycast` with
  `LayerMask.GetMask("Terrain")`); `Object.Instantiate(ItemDatabaseManager
  .ItemById(78).PickupPrefab, pos+0.1up, random yaw)`. Test: object appears at the
  aimed point, is pickupable, physics settles; then quicksave → quit to title →
  reload: **object persists**. Persistence failure → run 2c-bis (path C).
- 2c-bis **Spawn path C**: `AddItem(78,1)` → `DropItemFromInventory(78, out go)` →
  move `go` to the raycast point. Test: silent enough with
  `SkipNextAddItemWoosh`/`DontShowDrop` set; object persists across reload.
- 2d **Spawn path B**: `_canBeSpawned` flip + `DebugConsole.Instance._spawnitem("353")`
  (Stun Gun — normally locked). Test: spawns despite lock, near player.
- 2e **Console gating (confirmation)**: expectation from RedLoader source (1.7):
  cheats already allowed in SP. Confirm `_godmode("on")` direct call and
  `SendCommand("godmode on")` both work cold; capture whether output reaches
  `Application.logMessageReceived`.
- Deliverable of the phase: a filled-in test matrix updating section 1's ASSUMED
  items; GO/adjust decision on 1.6 (A vs C vs B).

**Phase 3 — Dispatcher + listener skeleton**
- Goal: threading model proven before any real commands exist.
- Do: MainThreadDispatcher (queue + OnUpdate drain + budget + TCS), TcpListenerHost,
  ClientSession, NDJSON codec, port/token file, `hello`/`ping`/`state.get` only.
- Test: from a terminal (`ncat 127.0.0.1 8271`), complete a hello handshake and get
  correct `state.get` transitions across title → world → quit-to-title. Flood 1,000
  pings during gameplay: no frame hitch (frame-time overlay), all answered.

**Phase 4 — Typed item commands**
- Goal: headline capability #1 end-to-end.
- Do: `item.list/resolve/add/remove/count` per spec 4.4; SessionState gate (SP path
  only at this phase); db.ready latch; push events wired from `OnItemAddedEvent`.
- Test: scripted session from the REPL client: resolve "modern axe" → add → count
  increments → visible in inventory (reopen at most) → remove → count restored.
  `item.add` for id 9999 → `E_UNKNOWN_ITEM`; while at title → `E_NOT_IN_WORLD`.

**Phase 5 — Spawn command**
- Goal: headline capability #2.
- Do: `item.spawn` implementing the Phase-2 winner (mode reported in result), qty
  loop with per-item scatter, maxDist clamp, `at:"front"` fallback using
  `GetPositionInFrontOfPlayer`.
- Test: aim at a slope 20 m away, spawn 3 logs: they land at the aim point, don't
  clip through terrain, persist across save/reload. Aim at sky → `E_BAD_ARGS`
  (no hit within maxDist).

**Phase 6 — Console passthrough + enumeration**
- Goal: full console surface exposed.
- Do: runtime enumeration (5.1), `console.list`, `console.exec` with direct-method
  dispatch + SendCommand fallback + output capture as validated in 2e.
- Test: `console.list` count ≈ 359 + dynamics; `console.exec "addcharacter robby"`
  spawns a Kelvin; a nonsense command → `E_UNKNOWN_CMD`; `godmode on` round-trips
  with captured output.

**Phase 7 — Overlay file + reconciliation**
- Goal: your reference file live in the loop.
- Do: OverlayFileLoader (three parsers, 5.2), Reconciler, `reconcile.json`,
  overlay-annotated `item.list`/`describe`.
- Test: startup log shows ~352 items / ~250+ commands parsed; `reconcile.json` lists
  the 21 known-stale commands (matching section 1.10) and flags any stale item IDs;
  `describe` shows `doc` text for `additem`.

**Phase 8 — Multiplayer validation matrix**
- Goal: turn the `mp:"untested"` annotations into facts and wire the session gate.
- Prerequisite: a second seat — second PC + second Steam account (family sharing
  can't run the same game concurrently), or a friend for an hour. Plugin installed on
  both sides where the matrix requires it.
- Do: implement SessionState MP detection (`BoltNetwork.isRunning`/`isServer`,
  `OnCheatsEnabledChanged`), `session.changed` push, `E_CHEATS_DISABLED` gate,
  MP spawn-mode switch (3.4). Then run the matrix, recording into
  `UserData\LiveEditor\mp-matrix.json`:
  1. host + client with cheats ON: `item.add` on each side → visible/usable locally;
     other player unaffected (expected — inventory is client-local);
  2. `item.spawn` (console mode) on host → other player sees the object; host saves,
     reloads world → object persists;
  3. `item.spawn` on client → does the other player see it? (records
     `works`/`local-only`);
  4. `console.exec godmode/goto/addcharacter` from host and from client → per-command
     result into the matrix;
  5. cheats OFF: cheat-class commands → `E_CHEATS_DISABLED` on both sides, no game
     API touched; host toggles Allow Cheats mid-session → `session.changed` arrives;
  6. client quicksave → client player-data save written (`MultiplayerClient` folder),
     granted items persist for the client across rejoin.
- Test: matrix file populated for the headline commands + at least the 20 most-used
  console commands; `describe` reflects it; no desync/kick observed during the run.

**Phase 9 — External app v0 + hardening**
- Goal: usable daily driver, resilient.
- Do: minimal REPL/CLI client reading the port file (UI app comes later, per scope);
  reconnect-with-backoff; per-job timeout; startup self-check (5.3) + `degraded`
  path; config entries (port, file path, budget); README with install/activation.
- Test: kill/restart the game with the client attached → client auto-reconnects and
  re-hellos; unplug mid-command → no game-side exception in log; self-check log block
  lists all interop members OK.

---

# 7. RISK REGISTER

| # | Risk | Likelihood | Impact | Failure type | Mitigation |
|---|---|---|---|---|---|
| 1 | **Game content update** changes GameAssembly → interop regenerates with renamed/removed members; typed layer breaks | Low near-term (binary frozen since 2025-01; dev focus moved to next title) but certain *eventually* | Plugin dead until fixed | Fail-fast at startup (self-check), not a crash | Break-surface is enumerated (5.3); archived interop dumps + Cecil diff makes the fix a rename exercise; runtime enumeration layers survive unchanged |
| 2 | **RedLoader breaks** (game update it can't handle, or OS-level: issue #48 Windows Update injection failure) and project is dormant | Medium (the Windows issue is live *now*) | Total, until workaround | Loader never initializes — game runs vanilla | Phase 0 validates today's state before any investment; BepInEx 6 be.755 fallback documented (SonsSdk-touching code kept behind thin wrappers); watch issue #48 for the specific KB number |
| 3 | **Direct-instantiate spawns don't persist** (skip world-object bookkeeping) or double-spawn against `_maxWorldPickups` | Medium | Feature degraded, not broken | Silent (objects vanish on reload) | Explicitly tested in Phase 2c *before* building on it; path C (add→drop→teleport, game-created pickup with a GameObject handle) is the designed fallback, path B (`_spawnitem`) last resort |
| 4 | **Calling game APIs off the main thread** (a future contributor bypasses the dispatcher) | Low by construction | Hard crash (native, il2cpp) | Instant CTD, crash dump | Architecture rule: only Dispatcher executes handlers; game-API wrappers assert `MainThread` in debug builds |
| 5 | **Command executed in a bad state** (title screen, loading, cutscene, dying) | High without gating | Ranges from no-op to CTD | Mixed | GameGate on `IsInWorld`/`GameState.IsInGame` + db-ready latch; every handler wrapped; unknown-state console commands are explicitly caller-beware in `describe` (your file's own warnings surface here) |
| 6 | **`_additem`/`AddItem` misbehaves for unique/instanced items** (null ItemInstance path) | Low (production mods pass null routinely; only the duplicate-unique case is unproven) | Wrong grants for ~weapons class | Recoverable (RemoveItem) | Phase 2a tests both item classes; per-item-class routing if needed (stackables → AddItem; uniques → `_additem` string path) |
| 7 | **Save corruption from bulk operations** (e.g. scripted addallitems-scale grants) | Low (we only call the game's own add path) | High if it happens | Recoverable via backups | Plugin never writes save files; docs mandate save backup before bulk scripts; `save.now` requires explicit client call |
| 8 | **Multiplayer semantics** (per-command MP behavior unknown until tested; client-local ghosts; desync) | Medium (MP in scope, but rides game-native paths only) | Desync/undefined per command | Mixed | SessionState gate (`E_CHEATS_DISABLED`, direct-instantiate refused in MP); MP uses only game-blessed paths (`AddItem`, `_spawnitem`, console dispatch); commands default `mp:"untested"` in `describe` until the Phase 8 matrix proves them; matrix persisted and surfaced to the app |
| 9 | **Achievements/telemetry** flagged by cheat usage | Unknown | Cosmetic | N/A | Documented as unknown; no evidence either way; user decision |
| 10 | **Local-process abuse of the control port** | Low | Game manipulation only | N/A | Loopback bind + per-launch token file (open question 2) |
| 11 | **Output capture unreliable** for console passthrough | Medium | Cosmetic (results still execute) | Degraded UX | Protocol marks `output` best-effort; success/failure still derived from dispatch |
| 12 | **Overlay file drift** (you edit/replace the txt with different formatting) | Certain over time | None if parser holds | Soft-degrade | Parser is skip-tolerant, reports parsed/skipped counts, and nothing functional depends on the file |

Hard-crash sources in this design are exactly two — off-main-thread game calls (#4)
and unguarded execution state (#5) — and both are addressed by construction, not by
testing alone. Everything else fails soft into a protocol error or a degraded flag.

---

*Appendix A — evidence artifacts and tooling:*
- **`tools\dump-type.ps1`** (in this repo) — dumps any type's fields/properties/method
  signatures from the interop assemblies via Mono.Cecil. Every signature in section 1
  came from it; rerun after any game patch to detect renames
  (`.\tools\dump-type.ps1 -Dll Sons.dll -TypeName TheForest.Items.Inventory.PlayerInventory`).
- **`tools\enum-console-commands.ps1`** (in this repo) — regenerates the live console
  command list and the 1.10 stale/missing diff against the reference txt.
- `_Redloader\Latest.log` — proof of loader+SDK function on this build.
- `Mods\ItemSpawner.dll` — reference implementation for the spawn technique
  (call-graph extracted, summarized in 1.6).
- Command diff (file vs live build) — section 1.10 lists both directions in full.

*Appendix B — external primary/community sources cited:*
- github.com/ToniMacaroni/RedLoader (branch `rewrite`) — loader + SonsSdk source:
  `SonsSdk/GameCommands.cs`, `SonsSdk/Attributes/Command.cs`, `SonsSdk/SdkEvents.cs`,
  `SonsSdk/ItemTools.cs`, `SonsSdk/SonsTools.cs`, `SonsSdk/Private/SdkEntryPoint.cs`,
  `RedLoader/Utils/Coroutines.cs`; docs at tonimacaroni.github.io/RedLoader
- github.com/NeuralBinary/Sons-of-The-Forest-Dump — IL2CPP dummy-DLL decompile
  (signatures + default parameter values; note: pre-1.0 era, superseded by the live
  interop dumps where they disagree)
- github.com/ImAxel0/ZombieMode_SOTF, /AxelModMenu, /RedNodeEditor, /SonsAxLib —
  AddItem / DebugConsole direct-call production precedents
- github.com/move123456789/IngameShop — raycast + Instantiate(PickupPrefab) precedent
- github.com/Frankyfunkz/FrankyModMenu — ItemData field manipulation precedent
- steamdb.info/patchnotes/20228174, unity.com/security/sept-2025-01 — build identity
- sotf-mods.com, thunderstore.io/c/sons-of-the-forest — ecosystem state
- sonsoftheforest.wiki.gg (Console_commands, Updates) — player-facing console docs

*Appendix C — cold-start execution pack (environment & scaffolding facts):*

**Machine facts (this install):**
- Game root: auto-detected (see `tools/Find-GameDir.ps1`); override with `SOTF_GAME_DIR`
- Reference txt (master copy): `SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt` in the repo root
- Steam account on this machine: SteamID64 `76561199352902660` (VERIFIED —
  `_Redloader\ErrorLog.log`); needed for the save paths below.

**Save locations (for the "back up saves" steps):**
`%USERPROFILE%\AppData\LocalLow\Endnight\SonsOfTheForest\Saves\<SteamID64>\`
with subfolders `SinglePlayer\<saveId>\`, `Multiplayer\<saveId>\`,
`MultiplayerClient\<saveId>\` — folder suffix constants VERIFIED on
`SaveGameManager` (`SinglePlayerSubFolderSuffix` etc.); the LocalLow root is
COMMUNITY-standard (Unity `Application.persistentDataPath` for Endnight). Backing up
= copying the numbered folder. Quicksaves live in the same tree
(`QuickSaveFolderSuffix`).

**Toolchain:**
- .NET 6 SDK (6.0.4xx). .NET 6 is EOL since Nov 2024 — download from the archive:
  dotnet.microsoft.com/download/dotnet/6.0. The *runtime* the game-side plugin uses is
  RedLoader's own bundled CoreCLR (`_Redloader\dotnet\`), so nothing extra to install
  for the game; the SDK is only for building.
- Any C# IDE. Target `net6.0`, no unsafe code needed, `LangVersion` default is fine.

**Project references** (all `Private=false` / CopyLocal off — the loader provides
them at runtime; never ship game or loader DLLs with the mod):
- from `_Redloader\net6\`: `RedLoader.dll`, `SonsSdk.dll`, `Il2CppInterop.Runtime.dll`,
  `0Harmony.dll` (only if patching), `Il2CppInterop.Common.dll` (transitively needed)
- from `_Redloader\Game\`: `Sons.dll` (PlayerInventory, LocalPlayer, DebugConsole),
  `Sons.Item.dll` (ItemDatabaseManager, ItemData), `Sons.Save.dll` (SaveGameManager),
  `Il2Cppmscorlib.dll`, `Il2CppSystem.dll` (il2cpp base types),
  `UnityEngine.CoreModule.dll`, `UnityEngine.PhysicsModule.dll` (Raycast),
  `bolt.dll` (BoltNetwork, for SessionState), plus whatever a later phase's compile
  error asks for from the same folder.

**Mod packaging layout** (VERIFIED — the installed ItemSpawner mod uses exactly this):
```
<game root>\Mods\LiveEditor.dll
<game root>\Mods\LiveEditor\manifest.json
```
`manifest.json` (schema: raw.githubusercontent.com/ToniMacaroni/RedLoader/main/MetadataSchema.json):
```json
{
  "$schema": "https://raw.githubusercontent.com/ToniMacaroni/RedLoader/main/MetadataSchema.json",
  "id": "LiveEditor",
  "author": "Nitro70",
  "version": "0.1.0",
  "description": "Live session control bridge (TCP/NDJSON)",
  "gameVersion": "1.0.0",
  "loaderVersion": "0.8.6",
  "url": ""
}
```
The assembly name must match `id`. The mod class subclasses `SonsSdk.SonsMod`
(overridable lifecycle: `OnInitializeMod`, `OnSdkInitialized`, `OnGameStart`,
`OnSonsSceneInitialized(ESonsScene)` — VERIFIED virtuals).

**Config**: use RedLoader's ConfigSystem
(`ConfigSystem.CreateFileCategory("LiveEditor", …)` → TOML at
`UserData\LiveEditor.cfg`; keybind/value entry types in `RedLoader.Preferences`) —
VERIFIED pattern from ItemSpawner's `Config.Load` + its on-disk
`UserData\ItemSpawner.cfg`. The port/token discovery file `UserData\LiveEditor.port`
(4.2) is separate and plugin-written.

**Doorstop activation** (Phase 0) — the parked
`redloaderhttp's\doorstop_config.ini` is verified correct and, with `version.dll`,
is the complete activation set:
```ini
[General]
enabled = true
target_assembly = _Redloader\net6\Redloader.dll
[Il2Cpp]
coreclr_path = _Redloader\dotnet\coreclr.dll
corlib_dir = _Redloader\dotnet
```
Exactly one proxy DLL (`version.dll` OR `winhttp.dll`) may sit in the game root.

**Canonical API cheat-sheet for the executor** (all VERIFIED, details in section 1):
```csharp
LocalPlayer.Inventory.AddItem(itemId, amount, preventAutoEquip: true);   // grant
ItemDatabaseManager.Initialize(); var items = ItemDatabaseManager.Items; // item DB
DebugConsole.Instance._spawnitem("78");                                  // native spawn
DebugConsole.Instance.SendCommand("godmode on");                         // console line
DebugConsole.RegisterCommand("mycmd",
    (Il2CppSystem.Func<string, bool>)Handler, DebugConsole.Instance);    // new command
SonsTools.CastToTerrainFromCamera(30f);                                  // look-at point
GlobalEvents.OnUpdate.Subscribe(DrainQueue);                             // main thread
RedLoader.Coroutines.Start(SomeRoutine());                               // coroutines
SaveGameManager.TriggerQuickSave();                                      // save
```

**External URLs an executor needs:** RedLoader releases
(github.com/ToniMacaroni/RedLoader/releases), RedManager installer
(github.com/ToniMacaroni/RedManager), docs (tonimacaroni.github.io/RedLoader), mod
hub (sotf-mods.com), decompiled signature reference
(github.com/NeuralBinary/Sons-of-The-Forest-Dump — pre-1.0, trust live interop over
it), community precedents per Appendix B.
