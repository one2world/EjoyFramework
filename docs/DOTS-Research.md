# Unity DOTS 研究报告（2026-08-10）

> 两路并行研究汇总：① Unity 2021 可用 ECS 版本与 1.x 差距；② DOTS 架构与实现深挖。
> 版本数据来自 packages.unity.com 注册表实取；文档引用来自 docs.unity3d.com 官方页面
> （抓取的纯文本存于 %TEMP%\dots13\，标注"未经在线来源验证"的小节以包源码为准）。

---

## 第一部分：Unity 2021 × Entities 0.51 可行性

### 版本硬事实（注册表实取）

| 包 | 2021 可用（最低 2020.3.30f1） | 1.x 线（最低编辑器） |
|---|---|---|
| com.unity.entities | **0.51.1-preview.21（0.x 终点）** | 1.0.14+ 要 2022.3.0f1；1.2.x 要 2022.3.11f1；最新 1.4.8 要 2022.3.20f1 |
| com.unity.rendering.hybrid | 0.51.1-preview.21（包终止） | 改名 entities.graphics，1.0.16 要 2022.3 |
| com.unity.collections | 1.4.0 | 2.1.x 要 2022.2+ |
| com.unity.netcode | 0.51.1-preview.21 | **断档直接跳 1.2.0（2022.3.11f1）** |
| com.unity.burst | 不锁 2021（1.4+ 标注 2018.4） | 非阻塞点 |

2021 与 1.x 之间**没有任何重叠**：升级 DOTS 必然升级编辑器。

### 结论：锁死 2021 时不推荐 0.51 做生产；移动/主机/XR 硬性不可行

**绕不过去的四条**：
1. **Hybrid Renderer 0.51 官方自认未达生产可用**——文档原文"尚未在移动和主机平台验证""尚未在 XR 测试"，不支持 OpenGL/GLES（Android 强制 Vulkan）。移动/主机支持是 1.0 用 BRG 重写为 Entities Graphics 后才有。
2. **无 Enableable Components**——高频状态开关只能在"结构性变更开销"与"丧失 query 过滤"二选一，架构级天花板（官方基准：enableable 0.03ms vs 批量结构变更 3.5ms vs 逐实体 35ms）。
3. **官方零维护**——preview 包 + 0.x 终点，所有 bug 自己扛（含 Burst/IL2CPP 底层问题）；Netcode 锁死 preview 版无升级路径。
4. **Burst 覆盖面差一代**——1.0 的 SystemHandle 改造 / ECB singleton（0.51 的 ECB 录制路径无法 Burst）/ EntityQueryBuilder 都不存在。

**可绕的**：Aspects（手写 Lookup）、SystemAPI/foreach（用 Entities.ForEach+IJobEntity，0.51 已有 IJobEntity）、Baking（用旧 conversion，接受迭代慢；0.51 反而保留了 1.0 已移除的运行时转换）、Content Management（自接 Addressables）。

**务实折中**：只在纯逻辑层用 0.51 的 ECS+Burst+Jobs（模拟/寻路/海量单位），渲染完全走传统 GameObject，不碰 Hybrid Renderer。

其它差距细节：Baking 的增量烘焙/依赖回滚 0.51 全无；1.0 移除 IJobChunk 的 firstEntityIndex（prefix sum 开销）、IJobEntityBatch 系并入 IJobChunk（v128 掩码签名）；`Schedule(query, dependsOn)` 的 dependsOn 在 1.0 不再可选（漏改静默退化单线程）；系统更新条件显式化（RequireForUpdate 系）；Journaling/authoring-runtime 双视图等编辑器工具 0.51 均无。

---

## 第二部分：DOTS 架构与实现深挖（要点）

> 完整论证见研究纪要；此处保留可指导深度使用/自研的核心。

### 1. 存储模型
- **Archetype 只增不减**（World 销毁才释放）；运行期动态制造大量 archetype 会永久抬高所有 query 匹配成本。先 `CreateArchetype` 再建实体、用 `ComponentTypeSet` 批量增删。
- **16KB chunk**：单 archetype、按类型分段 SoA、紧密打包 + swap-back 删除；容量 ≈ (16384−header)/(8+Σ组件大小)，**1.0 起钳制 ≤128 实体**（为 enableable 的 v128 掩码）。热/冷数据拆组件是 SoA 的主要杠杆。
- **Shared component = 分组键而非数据**：值存 World 级数组，chunk 存索引；**改值是结构性变更**；基数上千 = 海量半空 chunk（最常见的自伤）。
- **Chunk component**：per-chunk 数据（如整块包围盒），赋值不是结构性变更——批级保守判定（剔除/激活分层）的正确载体。
- **版本号体系**：32 位回绕（比较必须 `(B-A)>0`）；Entity{index,version} 判存活；Chunk.ChangeVersion[type] 记"上次被**可写访问**"（是"可能改过"不是"确实改过"）；OrderVersion 记结构变动。

### 2. 查询与遍历
- query 在 **archetype 粒度**匹配 + 结果缓存（archetype 集合早期趋稳是前提）。
- WithAll/Any/None 对 enableable 有精确语义（None=不含或已禁用），另有 WithDisabled/WithAbsent/WithPresent。
- **读写声明是双刃剑**：RW 推进 ChangeVersion 并串联依赖——多声明一个 RW，下游 change filter 全部白做。
- ComponentTypeHandle（chunk 内连续数组，主干道）vs ComponentLookup（两次间接寻址+cache miss，慢一个量级）。
- **enableable = chunk 内 v128 位掩码 + ChunkEntityEnumerator 跳位遍历**，把高频开关从 O(搬一行) 降到 O(翻一个 bit)。

### 3. 结构性变更
- 加一个组件的完整代价链：查/建 archetype → 找/分配 chunk → memcpy 整行 → 更新实体映射表 → swap-back 填坑 → **清空所有相关 query 的 chunk 缓存**（隐性放大器）。
- 结构性变更只能主线程做 → 必须先等所有在途 job = **sync point**。
- **ECB**：临时实体占位 + 回放时替换引用；ParallelWriter = per-thread 命令链无锁追加 + 按 sortKey（用 ChunkIndexInQuery，调度无关）归并回放保确定性；每个 job 用独立 ECB。
- **官方基准（百万实体加组件）**：enableable 0.03ms ≪ EM+query / ECB+query(AtPlayback) 3.5ms ≪ ECB+IJobChunk 17ms ≪ 逐实体 35ms ≪ **ECB+IJobEntity 逐实体记录 170ms（最多线程反而最慢）**。批量 API 快在 chunk 粒度整改。

### 4. Job 安全与调度
- SystemState.Dependency 按组件读写声明自动串联（RAW/WAR/WAW）；**官方承认的缺陷：粒度是 system 而非 job**，会制造伪依赖（Bevy 在此更精细）。自研应从 job 粒度记录读写集。
- Dependency **不追踪 NativeArray 传递的依赖**；IJobEntity 显式 Schedule(dep) 的返回值也要手动合并。
- 安全检查（AtomicSafetyHandle/RefRW 失效检测）**仅 Editor 存在**；player build 越界=崩溃/内存损坏。官方点名无防护区：IJobEntity 配外部 query 不校验组件匹配。

### 5. Burst
- IL→LLVM，HPC# 子集（无 GC/托管类型/动态分发；FixedString、FunctionPointer、SharedStatic 是替代件）。
- Unity.Mathematics 是向量化提示层（float4→SIMD 寄存器）；[NoAlias] 常是"循环没向量化"的解药；**Burst Inspector 看汇编是验证向量化的唯一可靠手段**。
- 最阴险陷阱：**代码静默掉出 Burst 时性能退化一个量级且无报错**——CI 应断言关键 job 的 Burst 编译状态。

### 6. 托管边界
- Managed component 存 World 级数组、chunk 存索引——cache 局部性完全丧失，仅作引擎对象桥接逃生口。
- **Blob asset：相对偏移（BlobArray/BlobPtr/BlobString）+ BlobBuilder 构造 + 加载≈memcpy + BlobAssetStore 去重/引用计数**。配置表最优解，且不依赖 ECS。
- Baking 三条不变量：**baker 无状态、产出可撤销、访问全部走可追踪 API**（DependsOn 须在 early-out 之前）；增量与全量烘焙输出的 chunk 布局不同 → 任何依赖 chunk 内实体顺序的逻辑都是错的。

### 7. 渲染（Entities Graphics / BRG）
- BRG 三层：draw command（同 mesh+material 实例组）/ filter settings / draw range（整段跳过）。**BRG 不做任何剔除，OnPerformCulling 自己写**（chunk 级 ChunkWorldRenderBounds 先淘汰整块，再逐实体）。
- DOTS Instancing：属性常驻 GPU 大 buffer + metadata 偏移表；SparseUploader **按 chunk ChangeVersion 只传脏块**——change filter 机制最大的内部客户。
- **官方承认**：低实例数时可能比 GameObject+SRP Batcher 慢（尤其 Android）；部分 Android 硬件不适应 persistent GPU data；GLES 无性能收益保证。**Android 为主、可见对象几千以下 → Entities Graphics 大概率不划算（官方文档原文）**。

### 8. 与 flecs/EnTT/Bevy 的取舍（未经在线来源验证）
- 16KB 定长 chunk：批处理统一货币（调度/剔除/版本/GPU 上传同一粒度）——**只在实体量足够大时成立**，小规模纯付内存税。
- Shared component 是最弱设计（分组与数据耦合、改值即结构变更）；flecs 的 relationship 语义更清晰。
- Burst 绑定：C# 生态先天负债的工程解，代价是 HPC# 子集+调试降级+静默掉出陷阱。C++/Rust 阵营无此负担。
- **Enableable 是全场最佳设计，无论用不用 ECS 都值得抄**。
- System 粒度依赖分析是官方承认的架构债。

### 给 EjoyFramework 的分层建议（按收益/成本比，前三条不需要 ECS）
1. **Blob 式静态数据**：配置表改"相对偏移+memcpy 加载+引用计数去重"。
2. **批内位掩码 + 跳位遍历**：实体行为开关（buff/AI 激活/播动画）用 bitmask 替代增删。
3. **批级元数据 + 两级剔除**：逐对象判定前先做批级保守判定。
4. 版本号驱动脏标记（批粒度 change version + 回绕安全比较）。
5. 命令缓冲：per-thread chain + 稳定 sortKey 归并回放。
6. SoA 批式存储：**API 必须强迫调用方按批思考**，否则 SoA 反而更慢（3.5ms vs 170ms 的教训）。
7. Burst 绑定：仅隔离的重计算内核 + CI 编译状态检查。

---

## 来源
- packages.unity.com 注册表 metadata（entities/burst/collections/entities.graphics/rendering.hybrid/netcode）
- docs.unity3d.com/Packages/com.unity.entities@1.0/manual/{whats-new,upgrade-guide}.html（本地 %TEMP%\dots_*.txt）
- com.unity.entities@1.3/manual/{concepts-archetypes,concepts-structural-changes,optimize-structural-changes,components-enableable-*,components-shared-introducing,components-chunk-introducing,components-managed,systems-entityquery*,systems-version-numbers,scheduling-jobs-dependencies,systems-entity-command-buffer*,concepts-safety,blob-assets-*,baking-overview,baking-baker-overview}.html（本地 %TEMP%\dots13\）
- com.unity.rendering.hybrid@0.51 与 com.unity.entities.graphics@1.0 的 requirements-and-compatibility.html
- docs.unity3d.com/Manual/{pack-exp,batch-renderer-group-how}.html、com.unity.burst@1.8/manual
- 未验证项：chunk capacity 公式、entity 元数据表、v128 存储、ECB per-thread chain、DOTS Instancing GPU 映射细节 → 以 Packages/com.unity.entities 源码（Chunk.cs/EntityComponentStore.cs/EntityCommandBuffer.cs）为准
