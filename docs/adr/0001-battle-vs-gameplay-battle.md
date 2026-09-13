# ADR 0001 — Two combat abstractions in GamePlay: SkillSystem/BuffSystem vs BattleEngine/AbilitySystem

Status: Accepted (revised after the Battle module was moved into GamePlay)

## Context

The framework ships **two parallel combat abstractions** that overlap in scope. Originally one lived
in the core `EjoyFramework` assembly and the other in `EjoyFramework.GamePlay`; both now live in the
**`EjoyFramework.GamePlay`** assembly (combat is game logic, not engine-agnostic infrastructure):

1. **`EjoyFramework.GamePlay.Battle` — `SkillSystem`/`BuffSystem`** (moved here from core). `SkillDef`/
   `BuffDef` are authored through DataTable, skills register by id, and casting is driven imperatively
   (`Cast(caster, skillId, target, buffManager, damageCalculator)`) with an injected damage calculator.
   These are framework modules: `public sealed class … : FrameworkModule`, reached via
   `Framework.GetModule<ISkillManager>()` / `Framework.GetModule<IBuffManager>()`. This is the
   "module-driven", low-ceremony combat path.

2. **`EjoyFramework.GamePlay.Battle.BattleEngine` + `EjoyFramework.GamePlay.Abilities.AbilitySystem`**
   — `BattleEngine` (FIFO event-chain combat over `BattleState`, with `BattleTrigger` chains and a
   `MaxEventsPerCall` safety bound) and `AbilitySystem` (GAS-style: `AttributeSet` + `GameplayTag` gates
   + `GameplayEffect` Instant/Duration/Infinite/Period semantics). `AbilitySystem` is a **per-owner
   POCO** (one instance per unit over its `AttributeSet`, driven by the owner's `Tick` — not a
   `FrameworkModule`); `BattleEngine`/`BattleState` are likewise per-encounter POCOs you construct
   directly. All are deterministic and allocation-aware.

Dependency direction: `EjoyFramework.GamePlay` references the core `EjoyFramework` assembly (for
`FrameworkModule`/`Framework.GetModule`), **never the reverse** — core and its Test/Unity/Editor
satellites depend on nothing in GamePlay. GamePlay is still engine-agnostic (`noEngineReferences:true`).

Having two abstractions is intentional, not accidental — but contributors need explicit guidance on
which to pick, or the codebase will drift toward inconsistent, duplicated combat logic per feature.

## Decision

Keep both. Treat them as distinct tools for distinct scenarios rather than competing implementations:

| Use case | Canonical choice | Why |
|---|---|---|
| Simple, module-driven combat (skill on cooldown, apply buff, deal damage), authored via DataTable | **`SkillSystem`/`BuffSystem`** (`ISkillManager`/`IBuffManager`) | Lowest ceremony; reuses DataTable authoring, injected damage calculator, `GetModule` access. Best when combat is one feature among many. |
| Data-driven, cross-genre combat needing composable attributes, tag-gated abilities, stacking/duration/periodic effects (GAS) | **`AbilitySystem` + `AttributeSet`** | Designed for systemic depth: modifiers, tags, effect lifetimes, DOT/HOT. Use when designers compose behavior from data. |
| Turn-based / reactive combat where one action triggers chains of further actions (on-hit, on-death, retaliation) | **`BattleEngine` + `BattleTrigger`** | The FIFO event queue with a hard `MaxEventsPerCall` bound is purpose-built for trigger chains and guarantees termination. |
| Headless simulation, server-authoritative logic, deterministic replay/tests | **`BattleEngine`/`AbilitySystem`** | Deterministic, allocation-aware, engine-agnostic — trivially unit-testable and portable. |

Guidance for contributors:

- Default to **SkillSystem/BuffSystem** when combat is shallow and you want module-driven, DataTable-authored skills.
- Reach for **Abilities/BattleEngine** when combat is the systemic core of the game, must run
  headless/deterministically, or needs GAS-style data composition.
- Do **not** bridge the two inside a single combat feature. Pick one per feature and stay within it;
  mixing produces two sources of truth for the same fight.

## Consequences

- Contributors have a clear, scenario-based default and will not reinvent combat per feature.
- Both abstractions now live in one assembly (`EjoyFramework.GamePlay`) and share one access idiom
  (`Framework.GetModule<IXxx>()` for the manager-style entry points), removing the earlier
  cross-layer split. The core framework no longer carries combat code.
- Some conceptual duplication (damage, buffs/effects) persists by design. If the two ever need to
  converge, prefer composing `AbilitySystem` behind `SkillSystem` rather than merging both abstractions.
