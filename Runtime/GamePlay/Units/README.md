# EjoyFramework 3D 单位系统（Units）

单位 / 角色 / 怪 / 机关 / 物品 / 投射物的统一抽象、继承与管理。**逻辑核引擎无关、可测、可服务器模拟；Unity 接线薄、架在框架 Entity 底盘上。**

---

## 1. 设计原则

- **浅继承表达"是什么 + 共享视图/生命周期";组合表达"能做什么"。** 深继承会因 Health/Movement/AI/Interactable/Loot 正交分布而崩，故能力做成**可选组件**。
- **能力全可选**：health / 属性 / 技能 / 移动 / 目标 / AI / 交互 / 掉落 任意搭配。机关、物品可以零战斗态。基类**不强制**任何战斗状态。
- **逻辑/表现分离**：单位的模拟逻辑（`UnitModel` / `UnitWorld`）纯 C#、引擎无关、可被 netcode 在**服务器无 Unity 模拟**；Unity 层（`UnitLogic` / `UnitManager`）只做表现 + 物理 + 接 Entity 池。
- **复用既有 GamePlay 系统**：单位系统主要是**集成胶水**——属性、技能、目标、寻路、空间、掉落、背包都直接复用。

## 2. 能力矩阵（为什么组合优于继承）

| 能力 | 角色Hero | 怪Monster | 机关Trap | 物品Item | 投射物Projectile |
|---|:--:|:--:|:--:|:--:|:--:|
| 视图/Transform | ✓ | ✓ | ✓ | ✓ | ✓ |
| Health/可伤害 | ✓ | ✓ | △可破坏 | ✗ | ✗ |
| 属性 Attributes | ✓ | ✓ | △ | ✗ | △ |
| 技能 Abilities(GAS) | ✓ | △ | △触发 | ✗ | ✗ |
| 阵营 Faction | ✓ | ✓ | ✓ | 中立 | 继承施法者 |
| 移动 Movement | ✓ | ✓ | ✗ | ✗ | ✓ |
| 目标选择 Targeting | ✓ | ✓ | △ | ✗ | ✓ |
| AI | △ | ✓ | △ | ✗ | ✗ |
| 可交互 | ✗ | ✗ | △ | ✓ | ✗ |
| 掉落 Loot | ✗ | ✓ | △ | ✗ | ✗ |

→ 没有单继承链能囊括；能力按需挂载。

## 3. 分层与类型

```
EjoyFramework.GamePlay（核心，引擎无关，可测）
  Factions/    FactionRelation, FactionRelations（友/敌/中立矩阵，支持 FFA）
  Units/
    ITargetableUnit          目标抽象（解耦：任何可被瞄准者实现它）
    TargetFilter, TargetingQuery   阵营过滤 → TargetInfo → TargetSelector → 单位
    UnitModel : ITargetableUnit    逻辑核：可选 health/属性(AttributeSet)/可推进行为(ITickable Behavior，如技能)，可池化 Reset
    DamageResult
    UnitWorld                管理核：注册表 + 阵营分桶 + AoiGrid 空间查询 + 为单位选敌 + 死亡/增删事件 + Tick
    Movement/                IMover, StraightMover, PathMover, Waypoint, MoveStep
    ProjectileModel          投射物运动/寿命/穿透/命中
    MechanismTrigger         机关触发器（Proximity/Timer/Manual + 冷却 + 次数）
    ItemPickup               物品拾取（范围 + 内容 + 一次性）

EjoyFramework.GamePlay.Unity（Unity 接线，薄）
  Units/
    UnitLogic : EjoyFramework.Runtime.EntityLogic   ★继承骨架（非 sealed）★
        持有 UnitModel，OnShow 按 UnitData 构建/池化重置，Transform↔model 同步，自注册进 UnitWorld
    UnitManager : MonoBehaviour    经 IEntityManager spawn/池化、每帧驱动 UnitWorld、目标查询门面
    UnitData / UnitDefinition / UnitKind
    TransformMover                 把 IMover 应用到 Transform（世界 XZ ↔ 模型 XY）
    ProjectileLogic / MechanismLogic / ItemLogic   : UnitLogic 子类

游戏侧（Assets/GameMain）
    HeroLogic / MonsterLogic / ...  : UnitLogic 子类（游戏专属技能/AI）
    typeId → UnitDefinition 配表
```

**继承骨架**：`EntityLogic`（框架）→ `UnitLogic`（单位骨架）→ `ProjectileLogic` / `MechanismLogic` / `ItemLogic` / 游戏的 `MonsterLogic` / `HeroLogic`。
**坐标约定**：Unity 世界水平面 (X, Z) ↔ `UnitModel` 的 2D 战斗平面 (X, Y)。

## 4. 快速上手

### A. 生产路径（经 Entity 系统生成，自带异步加载 + 对象池）
```csharp
// 1) 场景里放一个挂 UnitManager 的物体（自动建 UnitWorld）；配置阵营关系：
UnitManager.Active.World; // FactionRelations 默认不同阵营即敌对

// 2) 用 UnitData 生成（预制体由 assetName 经 Resource 异步加载、按 group 池化）：
var data = new UnitData { UnitId = id, FactionId = 2, Kind = UnitKind.Monster,
                          Position = spawnPos, MaxHealth = 200f };
UnitManager.Active.Spawn(data, "Assets/.../Monster.prefab", "Monster");
// 预制体上挂一个 UnitLogic 子类（如 MonsterLogic）——OnShow 自动建 Model 并自注册进 World。

// 3) 选敌：
if (UnitManager.Active.TryAcquireTarget(myModel, TargetFilter.EnemiesInRange(8f),
        TargetingStrategy.Nearest, out var enemy)) { /* 攻击 enemy */ }
```
游戏子类继承 `UnitLogic`，覆写 `OnShow`/`OnUpdate`（用 `protected override`、先调 `base`）。

### B. 直接用公开 API（无需预制体/Resource，见样例）
见 `Assets/GameMain/Scripts/Samples/UnitSystemDemo/UnitDemoController.cs`：直接 `new UnitWorld(relations)` + `UnitModel` + `StraightMover`/`ProjectileModel`，运行时图元做视图，端到端跑通"怪寻路 → 选最近敌 → 发射 → 命中扣血 → 死亡移除"。挂 `UnitDemoController` 到任意空物体按 Play 即可。

## 5. 与其它 GamePlay 系统的接线

| 单位能力 | 复用系统 |
|---|---|
| 属性/Buff | `Attributes.AttributeSet` + `Abilities` 的 GameplayEffect |
| 技能/冷却 | `Abilities.AbilitySystem`（GAS，经 `UnitModel.AttachBehavior(ITickable)` 挂接；Units 不直接依赖 Combat）|
| 目标选择 | `Targeting.TargetSelector` + `ThreatTable`（经 `TargetingQuery`）|
| 移动/寻路 | `Units.Movement` 或 `Pathfinding.AStarPathfinder` / NavMesh |
| 空间查询 | `Spatial.AoiGrid`（`UnitWorld` 内置，替代 O(N×M) 扫描；位于 GamePlay.Core，Netcode/Units 共用）|
| 掉落 | `Loot.LootTable`（怪死 → 掷落 → 生成 ItemLogic）|
| 携带物/拾取 | `Items.Inventory` / `Wallet` |
| 任务联动 | `Quest` 订阅单位死亡事件 |

## 6. 多人 / 服务器模拟

逻辑核（`UnitModel` / `UnitWorld` / 移动 / 投射物 / 目标 / 阵营）纯 C#、无 Unity 依赖，可在**无头服务器**权威模拟，对接 `Netcode`（`NetcodeServer` 快照下发 + `NetcodeClient` 预测/和解）。单机时 `UnitLogic` 直接持 `UnitModel`，零额外成本。

## 7. 测试

逻辑核 150+ NUnit EditMode 用例（Faction / 目标获取 / UnitModel / UnitWorld / 移动 / 投射物 / 机关 / 物品）；Unity 接线编译验证 + 样例 Play 模式冒烟实证。全量套件全绿。
