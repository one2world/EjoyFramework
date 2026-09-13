# EjoyFramework

独立 Unity Package Manager 包，最低声明版本为 Unity 6000.4，本次验证使用 6000.4.8f1 的程序集。本地拆分已整理，以下 Git URL 在推送并发布对应标签后生效；当前未推送，Git 安装和 Unity 测试仍待验证，详见 [验证记录](docs/PackageMigration.md)。

```json
{
  "dependencies": {
    "com.ejoy.framework": "https://github.com/one2world/EjoyFramework.git#v2.1.0"
  }
}
```

也可以在 Unity Package Manager 中选择 **Add package from git URL**，填入：

`https://github.com/one2world/EjoyFramework.git#v2.1.0`

仓库根目录就是 UPM 包根目录，必须包含本文件和 `package.json`。发布新版本时创建匹配的 `vX.Y.Z` Git tag；Game 项目和 `packages-lock.json` 应同时更新到该 tag。

A modular Unity 6 game framework with explicit engine-agnostic and Unity-specific assembly boundaries, designed for **mid-to-large商业游戏** development.

## Architecture

```
Runtime/
├── Core/            ← Layer 1: Engine-agnostic core (pure C#, no UnityEngine refs)
├── Core/Blobs/      ← Engine-agnostic ConfigBlob assembly (unsafe isolated)
├── Core/Ecs/        ← Self-authored pure C# ECS (separate assembly within Core)
├── Core.Unity/      ← Layer 2: Unity MonoBehaviour adapters + Helpers (core)
├── Core.Unity/Ecs/  ← ECS world lifecycle and view bindings (Core.Unity assembly)
├── Core.Jobs.Unity/ ← Unity Job System, Burst and Collections adapters
├── GamePlay/        ← Cross-genre gameplay layer (pure C#, no UnityEngine refs)
└── GamePlay.Unity/  ← Unity adapters for selected gameplay systems
Editor/
├── Core.Unity.Editor/     ← Layer 3: AssetBundle pipeline + UI editors + Config exporter (core)
└── GamePlay.Unity.Editor/ ← Editor tooling for gameplay systems
Tests/
├── Editor/                        ← EditMode unit tests (EjoyFramework.Tests + EjoyFramework.GamePlay.Tests)
└── Runtime/                       ← PlayMode integration tests (EjoyFramework.Tests.PlayMode)
```

## Core Managers (foundation, 17)

The independently owned ECS worlds are documented in [Self-authored ECS](Runtime/Core/Ecs/README.md). The core has no Unity or third-party ECS dependencies; adapters belong to Core.Unity. Import the ECS Demo from Package Manager's Samples section. New modules must follow the existing [directory and naming conventions](docs/DirectoryConventions.md).

| Module | Purpose |
|---|---|
| Event | Type-safe event pool |
| Fsm | Generic finite-state machine |
| Procedure | Game flow over FSM |
| Coroutine | Coroutine driver (used by Resource async loading) |
| ObjectPool | Multi-spawn / single-spawn pools |
| Resource | AssetBundle pipeline + manifest + ref counting |
| Entity | Entity show/hide with instance pool |
| UI | UIForm lifecycle + groups + instance pool |
| Sound | Sound groups + agents + fade in/out |
| Scene | Async scene load/unload |
| Config | Key-value game configs |
| Localization | Multi-language with PluralRules |
| DataTable | Strongly-typed config tables (with code-gen from CSV) |
| DataNode | Hierarchical data tree |
| Setting | Player settings (PlayerPrefs / encrypted file) |
| Network | TCP channel + heartbeat + Packet/Rpc registry |
| Debugger | F1 toggle IMGUI debug windows + perf collector |

## Expansion Modules (gameplay & world, 7)

| Module | Phase | Capability |
|---|---|---|
| Patch | 12 | Hot-update bundles (manifest diff + serial download + md5 verify) |
| Streaming | 14 | World chunk streaming + LOD + hysteresis policy |
| Save | 18 | Multi-slot saves + AES-256 + CRC32 + version migration |
| Input | 19 | Action remapping + InputContext stack |
| AI | 20 | Behavior Tree (Sequence/Selector/Inverter/Action/Condition) + Blackboard |
| Security | 22 | SecureInt/Float + ReplayDetector + IntegrityCheck CRC32 |
| Navigation | 23 | Grid/path navigation manager (INavigationManager) |

> `Battle` (Skill + Buff + DamageCalculator) used to live here; it is **game logic**, not
> engine-agnostic infrastructure, so it now lives in the GamePlay layer
> (`EjoyFramework.GamePlay.Battle`, still reachable via `Framework.GetModule<IBuffManager>()`).

## Live-Ops & Platform Modules (11)

These modules cover server-time, scheduling, networking, store, telemetry and message-badge
concerns. Each exposes a backend/platform-agnostic interface so business code injects the
concrete provider (HTTP client, analytics backend, store backend, notification platform, …).

| Module | Purpose |
|---|---|
| Time | Server-time manager — server-anchored corrected clock immune to local clock tampering |
| Timer | Delay / repeat / end-of-frame callbacks driven by framework Update (pause/cancel/query) |
| Http | HTTP manager — BaseUrl + default headers, retry, injectable `IHttpClient` |
| RemoteConfig | Typed remote-config values (string/int/bool/float) with local defaults + injectable provider |
| Purchase | IAP manager — purchase flow, product catalog, optional server-side receipt validation |
| Notification | Local + remote push manager — schedule/cancel/authorize over an injectable platform |
| Analytics | Unified event/property funnel over an injectable `IAnalyticsBackend` |
| Diagnostics | Crash reporting + log sinks / remote logging |
| RedDot | Red-dot (message-badge) tree manager with parent propagation |
| Performance | FPS/memory sampling exposed to the Debugger profiler tab |
| Serialization | `ByteBuffer` + binary/deep-copy contracts (impls generated by the Editor codegen) |

Total: **36 functional module directories** under `Runtime/Core/` (17 core managers +
8 expansion modules + 11 live-ops/platform modules), plus the `Base` infrastructure directory —
**37 directories** in all.

## GamePlay Layer (`EjoyFramework.GamePlay`, 31 systems)

A second engine-agnostic layer (`Runtime/GamePlay/`, also `noEngineReferences:
true`) provides cross-genre gameplay building blocks on top of the core. It ships **31 gameplay
systems** plus a shared `Core` math/infrastructure directory (`GameMath`). Seven of these
systems have Unity adapters under `Runtime/GamePlay.Unity/`
(Abilities, Music, Netcode, TouchInput, Tweening, Units, Worldmap).

| Area | Systems |
|---|---|
| Combat & units | Abilities, Attributes, Battle, Targeting, Units, Pathfinding |
| Progression & economy | Quest, Crafting, Production, Items, Loot, Tech, Leaderboard |
| World & content | Worldmap, Atmosphere, Sequencing, Dialogue, Guide, Music, Tweening |
| Social & live-ops | Social, Chat, Mail, Factions, Activities, Experiments, Matchmaking |
| Multiplayer & integrity | Netcode, AntiCheat, CloudSave, TouchInput |

The `Units` system has its own deep-dive in
[`Runtime/GamePlay/Units/README.md`](Runtime/GamePlay/Units/README.md).

**Access (parity with core).** Every singleton-manager GamePlay system is a `FrameworkModule`
exposing an `IXxx` interface, so it is reached exactly like a core module —
`Framework.GetModule<ITweenManager>()`, `Framework.GetModule<IQuestTracker>()`, etc. Systems that
need an external backend (Chat, CloudSave, Matchmaking) are created lazily and configured via
`SetXxx(...)` (a missing backend warns in-editor via the helper-injection contract). Value/algorithm
and per-owner types (`AStarPathfinder`, `LootTable`, `AttributeSet`, **`AbilitySystem`** — one per
unit, driven by its owner's `Tick`, the Netcode AOI/Sync/Transport helpers, …) stay plain POCOs you
construct directly. The `Battle` folder hosts both the module-driven `SkillSystem`/`BuffSystem`
(`ISkillManager`/`IBuffManager`) and the POCO `BattleEngine` — see
[`docs/adr/0001`](docs/adr/0001-battle-vs-gameplay-battle.md). GamePlay references the core
`EjoyFramework` assembly, never the reverse.

## Quick Start (2 steps — single drop-in prefab)

Framework provides **one drop-in prefab** that carries the full Framework + UI stack.

```
1. Editor menu: EjoyFramework/Framework/Export Framework Prefab
   → Generates Assets/GameMain/Framework/Prefabs/EjoyFramework.prefab

2. Drag EjoyFramework.prefab into your startup scene. Done — no manual wiring.
```

That's it. Business code accesses any manager — core OR GamePlay — via
`Framework.GetModule<IXxxManager>()`, or core Unity components via the typed
`GameEntry.UI / .Event / .Resource / ...` accessors.

### EjoyFramework.prefab structure

Single prefab brings up all 19 module components (on the `BaseComponent` root) AND the complete UI stack:

```
[EjoyFramework]                  BaseComponent (drives Framework.Update, DontDestroyOnLoad=true)
├─ Event                         EventComponent
├─ Fsm                           FsmComponent
├─ Procedure                     ProcedureComponent
├─ Coroutine                     CoroutineComponent
├─ ObjectPool                    ObjectPoolComponent
├─ Resource                      ResourceComponent
├─ Entity                        EntityComponent
├─ UI                            UIComponent + UIRootHelper + UIResolutionAdapter
│  ├─ [UICamera]                 Camera + UICameraHelper (UI-layer only, orthographic, depth=10)
│  ├─ [EventSystem]              EventSystem + StandaloneInputModule
│  ├─ Canvas:Background          Canvas + Scaler + Raycaster + UIGroupCanvasHelper (order=0)
│  ├─ Canvas:Scene               (order=100)
│  ├─ Canvas:HUD                 (order=200)
│  ├─ Canvas:Window              (order=300)
│  ├─ Canvas:Modal               (order=400)
│  ├─ Canvas:Tip                 (order=500)
│  ├─ Canvas:System              (order=600)
│  └─ Canvas:Top                 (order=700)
├─ Scene                         SceneComponent
├─ Sound                         SoundComponent
├─ Config                        ConfigComponent
├─ Localization                  LocalizationComponent
├─ DataTable                     DataTableComponent
├─ DataNode                      DataNodeComponent
├─ Setting                       SettingComponent
├─ Save                          SaveComponent
├─ Input                         InputComponent
├─ Network                       NetworkComponent
└─ Debugger                      DebuggerComponent  (F1 toggles IMGUI debug windows)
```

Authoring a UIForm and calling `GameEntry.UI.OpenUIForm("UIMainMenu", "Window", 0, false, null)`
is all the business code needs — UIComponent routes the form to `Canvas:Window` automatically.

### MVVM (data binding + commands + auto-wiring)

Framework provides a full MVVM stack so business writes ViewModel logic + names prefab children,
no manual wiring code.

**Workflow:**
1. Author UI prefab. Name widgets with the convention `B_<Property>_<Widget>`:
   - `B_Title_Text`, `B_Avatar_Image`, `B_Play_Button`, `B_Volume_Slider`, `B_Music_Toggle`,
     `B_NameInput_Input`, `B_Loading_Visibility`, `B_FriendsList_List` etc.
2. Menu `EjoyFramework/UI/Scaffold Form from Selected Prefab` → generates:
   - `<Form>.cs` (view, `MvvmView<TVM>` subclass)
   - `<Form>ViewModel.cs` (VM template with one example `BindableProperty`)
   - Auto-wires every `B_*_<Widget>` child with the matching binder MonoBehaviour pointing to the
     correct VM property — zero manual wiring.
3. Add `BindableObject` properties / `RelayCommand` properties to the VM. The view re-renders
   automatically on property change.

**Core (Layer 1, pure C#):**
- `BindableObject` + `SetField` helper — INPC-style change notification
- `BindableProperty<T>` — standalone observable
- `ObservableList<T>` — observable collection with granular events (Inserted/Removed/Replaced/Reset/Move)
  + `BeginBatch()` for bulk updates
- `RelayCommand` / `RelayCommand<T>` / `AsyncRelayCommand` — ICommand impls; AsyncRelayCommand
  auto-blocks reentry while running
- `PropertyAccessor` — Expression-compiled getter/setter cache (10× faster than reflection)
- `BindingPath` — nested path resolution (e.g. `"User.Profile.Name"`)
- `IValueConverter` — pluggable value transformation

**Binders (Layer 2, Unity):** 13 MonoBehaviour adapters, each `[AddComponentMenu("EjoyFramework/UI/Binders/...")]`:
- `TextBinder` / `TMPTextBinder` (TMP optional, reflection-guarded)
- `ImageBinder` / `RawImageBinder` / `ColorBinder`
- `ButtonBinder` (ICommand → click + interactable)
- `ToggleBinder` / `SliderBinder` / `DropdownBinder` (two-way)
- `InputFieldBinder` / `TMPInputFieldBinder` (two-way, configurable onEndEdit vs onValueChanged)
- `VisibilityBinder` (gameObject.SetActive ± inverted)
- `InteractableBinder` (Selectable.interactable)
- `ListBinder` (`ObservableList<T>` → child prefab instances with granular updates)

Every binder supports 4 `BindingMode`: OneWay / TwoWay / OneTime / OneWayToSource.

**📖 Full UI guide** — workflow, lifecycle, all binders, converters, performance notes, FAQ: [`docs/UI.md`](docs/UI.md).

**Value converters (Layer 2, drop-in MonoBehaviours):**
- `BoolNegateConverter` — `!bool`
- `NumberFormatConverter` — `IFormattable.ToString(format, culture)` with invariant-culture default
- `NullToBoolConverter` — null/empty-string → false (supports invert)
- `EnumToStringConverter` — enum ↔ name with optional `KeyPrefix` for L10n keys

**Nested data contexts (`MvvmContext`):**
Place an `MvvmContext` MonoBehaviour on a subtree root to give it its own `DataContext`,
independent of the form's root VM. The owning `MvvmView` automatically skips binders inside
that subtree, and `MvvmContext` re-binds them when its `DataContext` is reassigned.
Useful for master/detail panels, reusable sub-prefabs, and selection-driven views.

### Editor menus

| Menu | Function |
|---|---|
| `EjoyFramework/Framework/Create Framework in Scene` | Builds Framework hierarchy in current scene (no prefab) |
| `EjoyFramework/Framework/Export Framework Prefab` | Saves EjoyFramework.prefab — **the default workflow** |
| `EjoyFramework/Framework/Export Framework + Standalone UIRoot` | Advanced: also saves a standalone UIRoot.prefab for UI-only scenes (do **not** drop both into the same scene) |
| `EjoyFramework/UI/Create UI Root in Scene` | Builds UI-only hierarchy in current scene (no prefab) |
| `EjoyFramework/UI/Export UI Root Prefab` | Saves standalone UIRoot.prefab — UI-only authoring |
| `EjoyFramework/UI/Auto-Wire Bindings on Selected Prefab` | Auto-generates B_* GameObject binding code for UIForm prefabs |
| `EjoyFramework/UI/Add SafeAreaFitter to Selection` | Attaches notch/round-corner safe-area handler to a RectTransform |

## Editor Tools

| Menu | Function |
|---|---|
| `EjoyFramework/Resource/AssetBundle Builder` | Build bundles per rules (PerDirectory/PerFile/SizeLimit/SingleBundle) |
| `EjoyFramework/Config/Export from CSV` | Excel/CSV → DataRow .cs + .txt data table |
| `EjoyFramework/UI/Generate UIFormId` | Auto-generate UIFormId enum from prefabs |

## Testing

| Assembly | Location | Count | Attributes |
|---|---|---|---|
| `EjoyFramework.Tests` (core EditMode) | `Tests/Editor/Core.Tests/` | 549 | 536 `[Test]` + 13 `[TestCase]` |
| `EjoyFramework.GamePlay.Tests` (EditMode) | `Tests/Editor/GamePlay.Tests/` | 1253 | 1230 `[Test]` + 23 `[TestCase]` |
| `EjoyFramework.Tests.PlayMode` (PlayMode) | `Tests/Runtime/Core.Tests.PlayMode/` | 15 | 15 `[UnityTest]` |

- **Total**: 1817 test methods / cases
- Run via Unity Test Runner window or `[MenuItem] Tests/Run Framework Tests`

## License

See [LICENSE.md](LICENSE.md).

## Roadmap

Active phase plan and module state: see [`docs/EXPANSION_PLAN.md`](docs/EXPANSION_PLAN.md).
