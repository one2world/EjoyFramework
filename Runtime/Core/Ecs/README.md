# EjoyFramework 自研 ECS

运行时全部由本仓库实现，仅依赖 .NET 基础类库。没有引用、包装或复制 Unity Entities、第三方 ECS 的实现。独立项目以 `netstandard2.1` 编译，Unity 程序集设置 `noEngineReferences: true`。

## 模块与所有权

| 模块 | 职责 |
|---|---|
| `EjoyFramework.Core.Ecs` | Entity、World、Sparse Set 组件存储、Query、CommandBuffer、SystemGroup |
| `EjoyFramework.Core.Unity`（`Ecs/` 子目录） | 场景世界生命周期、Update/FixedUpdate/手动驱动、表现对象绑定 |
| 游戏层 | 业务组件、系统、Transform 同步、物理/渲染/网络接入 |

每个 `World` 独立拥有实体和组件。`SystemGroup` 拥有命令缓冲，不拥有 World 或系统实例。Unity 中 `EcsWorldComponent` 拥有 World 和 SystemGroup；禁用暂停自动推进，销毁释放它们。外部代码不要单独释放该组件持有的 World/SystemGroup。

ECS 不作为全局单例注册到 Framework：一个进程可以同时模拟战斗世界、预览世界、服务端世界。现有 `Core.Entity` 继续管理资源与表现对象，现有 `UnitWorld` 可继续使用；本模块不改变这些 API，也不迁移现有游戏数据。

## 数据与查询

```csharp
using EjoyFramework.Core.Ecs;

public struct Position { public float X; }
public struct Velocity { public float X; }
public struct Sleeping { }

using var world = new World();
var entity = world.CreateEntity();
world.Set(entity, new Position { X = 0 });
world.Set(entity, new Velocity { X = 2 });

// 缓存查询和回调，避免每帧构造描述或闭包。
var moving = world.Query().WithNone<Sleeping>();
ComponentAction<Position, Velocity> step =
    (Entity e, ref Position p, ref Velocity v) => p.X += v.X * 0.02f;
moving.ForEach(step);
```

- `Set<T>` 插入或覆盖组件，`Get<T>` 返回原位引用，`TryGet<T>` 返回值副本。
- 组件约束为 `struct`，无需注册、继承或特性；空结构体可以作 Tag。包含托管引用的结构体也能存储，但复制不深拷贝其引用字段。
- Entity 包含世界标识、索引、版本。销毁/取消预留后旧句柄失效，版本耗尽的槽位永久退役；不允许版本回绕复活旧实体。
- `Has/TryGet/IsAlive` 可用于判断过期实体；`Get/Set/Remove/DestroyEntity` 对无效实体抛异常。Dispose 后除 `IsAlive` 返回 false 外，数据操作抛 `ObjectDisposedException`。
- Query 描述不可变。`WithAll<A>().WithAll<B>()` 要求全部存在；`WithAny<A>().WithAny<B>()` 要求至少一种；`WithNone<C>()` 排除 C。三组条件之间是 AND。
- `ForEach<T1,...,T4>` 自动将泛型组件加入本次遍历的必需条件；Query 本身的 `Count` 只统计描述中显式声明的过滤条件。
- 缓存的 Query 可看到后续新增实体/组件，纯 Any 查询不会重复返回实体。
- 从数量最少的必需组件集合开始遍历；没有必需组件时遍历活跃实体。遍历顺序不构成契约，删除采用末项交换。

**ref 的有效期止于下一次结构变更或世界释放。** 不跨调用保存 ref，不跨线程使用。ForEach 回调和系统执行期间允许修改已有值；直接创建/销毁实体、增删组件、释放世界会抛异常。嵌套只读查询及值修改可用。

## 结构变更与失败规则

```csharp
using var commands = new CommandBuffer(world);
moving.ForEach<Position>((Entity e, ref Position p) =>
{
    if (p.X > 100) commands.DestroyEntity(e);
});

var child = commands.CreateEntity(); // 已预留句柄，暂不可查询或直接 Set
commands.Set(child, new Position());
commands.Playback();                 // 顺序执行，child 此时活跃
```

预留句柄可以写入其他组件作为实体引用，无需回放时重映射。只有创建它的缓冲可以记录针对它的命令；激活之后其他缓冲可正常使用。`Clear/Dispose` 取消未激活预留并使旧句柄失效。

缓冲按记录顺序严格执行。`Destroy(e)` 后再 `Set(e)`，或回放前 e 已失效，都属于错误。错误发生时：

1. 已完成的命令保留结果，不回滚。
2. 当前失败及后续命令丢弃，尚未激活的预留句柄释放。
3. 缓冲清空后可以复用，异常交给调用方处理。

在遍历中尝试 Playback 会被拒绝，待执行命令保留，以便在合法同步点重试。组件值分类型存放在复用列表中，记录 Set 不对每条命令装箱；首次使用或扩容仍会分配。

## 系统执行

```csharp
public sealed class MovementSystem : ISystem
{
    private readonly Query m_Query;
    private readonly ComponentAction<Position, Velocity> m_Move;
    private float m_DeltaTime;

    public MovementSystem(World world)
    {
        m_Query = world.Query();
        m_Move = Move;
    }

    public void Update(World world, CommandBuffer commands, float deltaTime)
    {
        m_DeltaTime = deltaTime;
        m_Query.ForEach(m_Move);
    }

    private void Move(Entity entity, ref Position p, ref Velocity v)
        => p.X += v.X * m_DeltaTime;
}

using var systems = new SystemGroup(world);
systems.Add(new MovementSystem(world), order: 0);
systems.Update(0.02f);
```

order 升序执行，同 order 按注册顺序；每个系统之后回放命令，因此下一个系统能查询到变更。支持启停与移除，同一实例不允许重复加入一个组。缓存了 World/Query 的系统实例只用于对应世界。

系统异常停止本次后续系统，丢弃该系统尚未回放的命令；已经发生的值修改和前面系统的结果保留。不允许重入、执行中修改系统组或释放它。时间由调用方传入，必须为有限非负值。

Update 收到的命令缓冲由 SystemGroup 拥有，系统可以记录或 Clear，不能 Dispose；系统组负责释放。世界释放时，即使外部仍缓存 Query，也会释放组件池的存储数组。

## Unity 接入与示例

`EcsWorldComponent` 默认在 FixedUpdate 推进；可切换 Update 或 Manual，手动调用 `Step(deltaTime)`。Step 抛异常时禁用自动推进，保留世界以供检查。系统实例的业务资源由创建它的游戏代码负责释放。

`EcsEntityView.Bind(world, entity)` 只绑定有效实体。`IsBound` 会识别销毁、版本变化和世界释放；`Unbind` 或销毁视图不会销毁模拟实体。接入原 Entity 对象池时在 OnShow 中 Bind，在 OnHide 中 Unbind。业务层明确选择模拟驱动 Transform 或 Transform 驱动模拟，避免双向覆盖。

示例位于 `Assets/GameMain/Scripts/Samples/EcsDemo/`，程序集和命名空间为 `EjoyGame.Samples.EcsDemo`，遵守现有游戏示例目录规范。将 `EcsDemoBehaviour` 挂到空物体，会创建方块目标与球形投射物；球到达目标后扣血，两者消失。需要场景自己的 Camera/光照。`ProjectileScenario.cs` 是可在独立 .NET 运行的同一套逻辑，例子只演示一维命中，不提供物理碰撞算法。

## 验证

在项目根目录运行（测试依赖 NUnit 等测试工具；运行时不依赖它们）：

```powershell
rtk dotnet test Tools~/Ecs/EjoyFramework.Core.Ecs.Tests.csproj --configuration Release
rtk dotnet run --project Tools~/Ecs/EjoyFramework.Core.Ecs.Benchmark.csproj --configuration Release
```

Unity EditMode 测试位于 `Tests/Editor/Core.Tests/Ecs/`，并入 `EjoyFramework.Tests`，按 `EjoyFramework.Tests.Ecs` 过滤。
Unity PlayMode 测试位于 `Tests/Runtime/Core.Tests.PlayMode/Ecs/`，并入 `EjoyFramework.Tests.PlayMode`，按 `EjoyFramework.Tests.PlayMode.Ecs` 过滤。
独立 .NET 测试还会直接编译 `Samples~/EcsDemo/ProjectileScenario.cs`，验证移动、命中、伤害、死亡和预热后的分配。

基准测试使用 1千/1万/10万个实体，仅测串行标量移动，输出 200 次更新的中位数、P95 和当前线程托管分配；不包含初始化、渲染、物理或结构变更。该结果不能代替 Unity Mono/IL2CPP、移动设备或实际游戏性能验证。

## 实现边界

这是单线程 Sparse Set ECS，不提供并行读写调度、Burst 编译、Archetype/Chunk、自动序列化、回滚快照或网络同步。组件引用与结构变更规则为当前实现设计，不声称可直接交给 Unity Jobs。

Sparse Set 的组件查找与末项交换删除为 O(1)，扩容摊销；销毁实体需检查已注册的每种组件池。稀疏数组的内存随该组件曾使用的最大实体索引增长。因此大量稀有组件配合极高实体索引时，应测量内存后再选择分段索引或其他存储布局。
