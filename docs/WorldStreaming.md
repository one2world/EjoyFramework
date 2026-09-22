# World Streaming — 世界分区流送

> WS3-M2（2026-09-22）。Core：`Runtime/Core/Streaming/`，Unity：`Runtime/Core.Unity/Streaming/WorldStreamingComponent.cs`，
> 编辑器：`Editor/Core.Unity.Editor/Streaming/ChunkVisualizer.cs`（Scene 视图叠加）。

## 模型（一句话）

世界按整数单元 `(cx, cz)` 分区、分**层**（地形 / 建筑 / 植被……各有半径与 LOD 表），
**观察者**位置驱动期望集，期望集与活动集**差分**产生加载/卸载，加载**最近优先**并受预算限制，
业务经 `IWorldStreamingHandler` 做 IO 并异步回报。

## 主路径

```csharp
// 1. 组件配置（或直接 Framework.GetModule<IWorldStreamingManager>()）
var ws = GameEntry.WorldStreaming;                 // WorldStreamingComponent
ws.ConfigureLayer(layer: 0, loadRadius: 200f, unloadRadius: 260f, lodDistances: new[] { 80f, 160f });
ws.ConfigureLayer(layer: 1, loadRadius: 400f, unloadRadius: 480f, priorityBias: 5);   // 远景层，后加载

// 2. 登记单元（通常从 ConfigBlob 的 WorldCells 表批量登记；contentKey = 表行 Id）
foreach (var row in worldCells) ws.Streaming.RegisterCell(row.Layer, row.Cx, row.Cz, row.Id);

// 3. 观察者：玩家 Transform（分屏 / 观战再绑一个 id）
ws.BindObserver(observerId: 1, player.transform);

// 4. IO 处理器：每单元一个附加场景（内置），或自定义 IWorldStreamingHandler
ws.UseSceneHandler(new MySceneResolver());        // contentKey → "World/L0_12_7"

// 5. 玩家已站在未加载的地面上：同步兜底
int cell = ws.Streaming.FindCell(0, cx, cz);
ws.Streaming.RequireLoaded(cell);

// 6. 浮动原点：业务把场景整体平移 -delta 时
ws.ShiftOrigin(delta);
```

## 语义要点

| 主题 | 行为 |
|---|---|
| 期望集 | 任一观察者 `LoadRadius` 内（到单元中心）的单元；多观察者取并集 |
| 滞回 | 已加载单元只有到最近观察者距离 > `UnloadRadius` 才卸载（`UnloadRadius` 必须 > `LoadRadius`） |
| 优先级 | 键 = 最近距离 + `PriorityBias × CellSize`；每次评估排队单元按新距离重排（旧堆项凭入队戳丢弃） |
| 预算 | `MaxLoadStartsPerFrame` / `MaxLoadsInFlight` / `MaxUnloadsPerFrame`；预算不足的动作顺延到后续帧 |
| 评估触发 | 观察者移动超过 `ReevaluateMoveThreshold`（默认 CellSize/4）、配置/登记变化、`ForceReevaluate` |
| 取消 | 加载中离开半径 → `CancelLoad`，状态 `Cancelling`；迟到的成功结果自动转为卸载 |
| 失败 | `NotifyLoaded(id, false)` → 回到 Unloaded 并计数；下次评估自动重试 |
| 注销 | 已加载先卸载、加载中先取消；槽位在最终回报后释放，旧 id 查询得到 `None` |
| 浮动原点 | 内部一律世界坐标；`ShiftOrigin` 累加偏移，观察者本地坐标按新原点换算，单元不动 |
| 复杂度 | 评估只枚举半径覆盖的整数 cell 范围，与世界总单元数无关；稳态零分配（1681 单元巡逻断言） |

## IWorldStreamingHandler 契约

- 全部回调在主线程；`BeginLoad` / `BeginUnload` 可同步完成（回调内直接 `Notify*`）。
- 回调抛异常：加载按失败处理、卸载按已卸载处理，不会卡死状态机。
- `CancelLoad` 后**仍必须**回报 `NotifyLoaded`（成功或失败），管理器据此收尾；不能取消的 IO（场景）直接等结果。
- 一次 `BeginLoad` 只能配对一次 `NotifyLoaded`；重复回报被忽略并告警。

## 内置 SceneStreamingHandler

每单元一个附加场景。`ISceneNameResolver.Resolve(layer, cx, cz, contentKey)` 返回场景资源名（null = 该单元无场景，立即视为加载成功）。
同一场景名映射到两个单元是配置错误：第二个单元按失败处理并报错。场景卸载失败会记录错误并按已卸载处理，避免单元永久卡在 Unloading。

## 未做 / 后续

- 单元级持久化（离开时保存增量、进入时恢复）→ WS3-M3。
- 种群/生成管理与流送联动 → WS3-M3。
- 流式 navmesh 分块 → WS3-M3。
- 四叉树 / 层级分区（超大世界的多级 LOD 单元）：当前统一网格已覆盖常规开放世界规模，按需再加。
