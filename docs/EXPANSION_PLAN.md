# EjoyFramework Expansion Plan

This document tracks the framework's module roadmap and the honest current state of
each module. It complements the high-level summary in [`../README.md`](../README.md)
and the per-release log in [`../CHANGELOG.md`](../CHANGELOG.md).

## Module Inventory

The core lives under `Runtime/EjoyFramework.Core/`. There are **37 directories**: one `Base`
infrastructure directory plus **36 functional modules** (17 core managers + 8 expansion
modules + 11 live-ops/platform modules). Each module exposes an `IXxxManager`-style interface
in the engine-agnostic layer and (where it needs runtime/scene integration) a matching
`XxxComponent` MonoBehaviour adapter under `Runtime/EjoyFramework.Core.Unity/`.

A second engine-agnostic layer, `Runtime/EjoyFramework.GamePlay/` (`noEngineReferences: true`),
adds **31 cross-genre gameplay systems** plus a shared `Core` (GameMath) directory; seven of
those systems have Unity adapters under `Runtime/EjoyFramework.GamePlay.Unity/`.

### Core managers (Phase 0–11) — shipped

| Module | State |
|---|---|
| Event | Shipped — type-safe event pool |
| Fsm | Shipped — generic finite-state machine |
| Procedure | Shipped — game flow over FSM |
| Coroutine | Shipped — coroutine driver for async loading |
| ObjectPool | Shipped — multi-spawn / single-spawn pools |
| Resource | Shipped — AssetBundle pipeline + manifest + ref counting |
| Entity | Shipped — show/hide with instance pool |
| UI | Shipped — UIForm lifecycle + groups + MVVM stack |
| Sound | Shipped — groups + agents + fade |
| Scene | Shipped — async load/unload |
| Config | Shipped — key-value game configs |
| Localization | Shipped — multi-language + PluralRules |
| DataTable | Shipped — typed tables + CSV code-gen |
| DataNode | Shipped — hierarchical data tree |
| Setting | Shipped — PlayerPrefs / encrypted file |
| Network | Shipped — TCP channel + heartbeat + Packet/Rpc registry |
| Debugger | Shipped — F1 IMGUI windows + perf collector |

### Expansion modules (Phase 12–23) — shipped

| Module | Phase | State |
|---|---|---|
| Patch | 12 | Shipped — manifest diff + serial download + md5 verify |
| Streaming | 14 | Shipped — world chunk loader + LOD + load/unload hysteresis |
| Save | 18 | Shipped — multi-slot + AES-256-CBC + CRC32 + version migration |
| Input | 19 | Shipped — action binding abstraction + InputContext stack |
| AI | 20 | Shipped — Behavior Tree + typed Blackboard |
| Battle | 21 | Shipped — Skill + Buff (DOT/HOT/stacking) + DamageCalculator |
| Security | 22 | Shipped — SecureInt/Float + ReplayDetector + IntegrityCheck |
| Navigation | 23 | Shipped — grid/path navigation manager (INavigationManager) |

### Live-Ops & Platform modules — shipped

Each exposes a backend/platform-agnostic interface so business code injects the concrete provider.

| Module | State |
|---|---|
| Time | Shipped — server-anchored corrected clock (tamper-resistant) |
| Timer | Shipped — delay / repeat / end-of-frame scheduling via framework Update |
| Http | Shipped — HTTP manager (BaseUrl + headers + retry) over injectable `IHttpClient` |
| RemoteConfig | Shipped — typed remote-config values + local defaults + injectable provider |
| Purchase | Shipped — IAP flow + product catalog + optional server-side receipt validation |
| Notification | Shipped — local + remote push over an injectable platform |
| Analytics | Shipped — unified event/property funnel over injectable `IAnalyticsBackend` |
| Diagnostics | Shipped — crash reporting + log sinks / remote logging |
| RedDot | Shipped — red-dot (message-badge) tree with parent propagation |
| Performance | Shipped — FPS/memory sampling surfaced in the Debugger profiler tab |
| Serialization | Shipped — `ByteBuffer` + binary/deep-copy contracts (Editor-generated impls) |

### GamePlay layer (`EjoyFramework.GamePlay`) — shipped

31 cross-genre gameplay systems plus a shared `Core` (GameMath) directory. Seven have Unity
adapters (Abilities, Music, Netcode, TouchInput, Tweening, Units, Worldmap).

| Area | Systems |
|---|---|
| Combat & units | Abilities, Attributes, Battle, Targeting, Units, Pathfinding |
| Progression & economy | Quest, Crafting, Production, Items, Loot, Tech, Leaderboard |
| World & content | Worldmap, Atmosphere, Sequencing, Dialogue, Guide, Music, Tweening |
| Social & live-ops | Social, Chat, Mail, Factions, Activities, Experiments, Matchmaking |
| Multiplayer & integrity | Netcode, AntiCheat, CloudSave, TouchInput |

## Planned / Future Work

These are candidate directions, not yet implemented. They are listed so the roadmap stays
honest about what exists versus what is aspirational.

- **Navigation depth**: dynamic obstacle re-baking and off-mesh links beyond the current
  grid/path manager.
- **Battle**: targeting/aggro helpers and projectile lifecycle on top of the existing
  Skill/Buff/Damage layer.
- **Save**: cloud-sync adapter behind the existing `ISaveSerializer` (the default
  serializer uses Unity `JsonUtility`; richer serializers such as Newtonsoft.Json can be
  injected by business code but are not a framework runtime dependency).
- **Network**: reliable-UDP channel alongside the current TCP channel.
- **Tooling**: addressables-style asset addressing layer over the AssetBundle pipeline.

## Verification

Module and test counts in this plan, the README, and the CHANGELOG are kept in sync with
the source tree. As of the latest recount:

- Core module directories: `Runtime/EjoyFramework/` — 37 dirs (`Base` + 36 functional modules).
- GamePlay layer: `Runtime/EjoyFramework.GamePlay/` — 32 dirs (`Core` + 31 gameplay systems);
  `Runtime/EjoyFramework.GamePlay.Unity/` — 7 adapter directories.
- Test methods / cases (recomputed from the tree):
  - `EjoyFramework.Tests` (EditMode): 549 (536 `[Test]` + 13 `[TestCase]`)
  - `EjoyFramework.GamePlay.Tests` (EditMode): 1253 (1230 `[Test]` + 23 `[TestCase]`)
  - `EjoyFramework.Tests.PlayMode` (PlayMode): 15 `[UnityTest]`
  - 1817 total.

Recompute with: `[Test]` / `[TestCase]` / `[UnityTest]` attribute counts under each `Tests/`
assembly directory, and `find -maxdepth 1 -type d` under each `Runtime/EjoyFramework*` layer.
