# ADR 0002 — Formalize the helper/loader injection contract

Status: Accepted and implemented (2026-08-22)

## Context

Several core modules depend on a helper or loader that the Unity layer (or bootstrap code) must inject
before the module is usable. Injection is done through `SetXxx` methods — for example
`ResourceManager.SetLoader(IResourceLoader)`, `SetReadOnlyPath`, `SetReadWritePath`, and the analogous
`Set*Helper` methods on `SoundManager`, `EntityManager`, `LocalizationManager`, `ConfigManager`,
`DataTableManager`, `SettingManager`, `UIManager`, `SceneManager`, `NavigationManager`, `InputManager`,
`SaveManager`, `PatchManager`, and `CoroutineManager`.

Before this decision, injection was **convention, not contract**. Nothing forced the bootstrap to call it.
A forgotten injection surfaced later and inconsistently:

- **Late NRE** — a method dereferences the null helper on first real use, often deep in gameplay.
- **Silent no-op / soft error** — e.g. `ResourceManager.InitializeAsync` checks `m_Loader == null` and
  routes to its `onFailure` callback with *"Resource loader is not set. Call SetLoader before
  InitializeAsync."*. Correct, but only discovered when something happens to call `InitializeAsync`, and
  only as a callback string rather than a hard boot failure.

In both cases the failure is detached in time and place from the real root cause (a missing wire-up in
the bootstrap), which makes it disproportionately expensive to diagnose for a configuration mistake.

## Decision

Formalize the injection contract so a missing helper fails loudly and early:

1. **Per-module configuration contract.** `FrameworkModule` exposes `RequiresConfiguration`,
   `IsModuleConfigured`, and `ConfigurationHint`. Each module owns the definition of "fully configured".

2. **Explicit validation in pure C# Core.** `Framework.ValidateModuleConfigurations()` walks the currently
   created modules and throws one aggregate `FrameworkException` naming every missing dependency.

3. **One Unity composition root.** `BaseComponent.Start` invokes validation after every scene `Awake` has
   completed and before gameplay `Start` methods run. The same hard validation executes in Editor and Player.

4. **Late-created modules.** A host that creates configurable modules after startup must call
   `ValidateModuleConfigurations()` again after wiring them. The Editor first-update warning remains a
   diagnostic fallback, not the primary contract.

Deliberately out of scope: changing the `Set*` API surface, making injection mandatory at construction,
or introducing a DI container.

## Consequences

- A forgotten injection fails at boot in every build with a precise aggregate message.
- Each affected module defines its own configuration state and remediation hint.
- Core remains Unity-independent; only the `.Unity` composition root knows about Unity lifecycle ordering.
- Dynamic composition must end with an explicit validation call.
