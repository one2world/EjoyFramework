# Unity 回归实测记录（2026-10-25）

本文记录本机实际执行结果；较早文档中的许可证退出 198、尚未安装 Editor 等描述属于历史状态。起点为 main `4626f9069bbb41a55eb56703fc31e8db2e7eb1cd`，修复在 `codex/unity-regression-validation` 分支进行。原始 XML、日志、构建产物保存在 `Tests~/Unity/TestResults/unity-cli/2026-10-25/`，均不进入 UPM 包或版本管理。

## 环境与边界

- macOS arm64，Apple M5；Unity CLI 1.0.0-beta.14；Unity Personal 已激活，实际测试通过许可证检查。
- 主测试宿主 `Tests~/Unity`，本地 UPM 引用本仓库；Unity 6000.4.8f1（f8b72d3d7343）。PlayMode 启用 Metal，日志确认 Apple M5。Editor 测试不等于完整游戏画面或人工手感验收。
- 原先根目录 TestResults 下的安装/许可证探针目录会被 Unity 当作包资源导入，曾触发“Library folder embedded in an asset folder”。已将这些本地产物迁入被 Unity 排除的 `Tests~/Unity/TestResults/Artifacts/`；后续日志和探针均放在 Tests~ 内。
- 测试宿主的默认 ProjectSettings 和 Test Runner 临时场景由 Editor 生成，忽略这些本机状态；保存依赖 lock 以固定本轮测试依赖。

## 失败、修复与复测流水

1. `Focused-01.xml`：111 项，107 通过、4 失败。4 项均为 ThirdPersonCharacterMotor 在 Awake 前传送时的空引用。新增按需、幂等初始化，支持尚未触发 Awake 的对象池实例，并保留 active/enabled 状态；非法坐标在初始化和变更状态前拒绝。
2. `EditMode-02.xml`：3590 项，3580 通过、2 失败、8 跳过。失败分别来自嵌套 Library 目录导入，以及 MainThreadDispatcher 耗时测试使用固定 SpinWait 次数。在该 Mono/CPU 上固定次数不足 1ms，不能证明越过预算；改为按单调时钟等待 2ms，保留“首帧不执行完、后续帧全部执行”的断言。
3. 同时检查原生日志发现，非凸 MeshCollider 初始重叠路径调用 Collider.ClosestPoint，而该 Unity API 不支持非凸网格。修复为构造时缓存可读网格的世界坐标三角形，重叠时计算最近点，不在查询中分配；跳过退化三角形。不可读网格仍使用原生重叠检测，初始接触点以查询起点、法线以反方向表示，避免假设不可读三角形数据。普通 cast 仍使用原生碰撞结果。测试分别覆盖可读网格的表面点/法线及不可读网格的明确回退，并检查无非预期日志。
4. `EditMode-03.xml`：3582 通过、0 失败、8 跳过。`PlayMode-01.xml`：43/43。`AssemblyCSharpSnapshotSmoke.log`：默认程序集路径解析、快照 golden 对比及快照性能入口完成，进程退出 0。
5. 添加不可读网格回归后，`EditMode-Final.xml` 暴露 UDP budget 测试偶发失败（新数据尚未到达内核接收队列即断言）。测试改为有截止时间地先用禁用派发的 Poll 收齐四个事件，再验证回调数、字节限制和重入保护；不改变传输层生产代码。
6. `UnsafeAndFocused-05.xml`：54/54，含物理/传送/UDP、真实 TCP 回环及显式启用 EJOY_UNSAFE_STRING 的 MutableString。`EditMode-Unsafe.xml`：3591 项，3589 通过、0 失败、2 跳过。随后恢复默认编译符号。
7. `EditMode-Default-Final.xml`：3591 项，3583 通过、0 失败、8 跳过。

## Git UPM 与 6000.6 兼容性

运行时代码修复已经发布到 main `8f58ec5826e01738c1566749789940d524bbc45e`。该提交的 GitHub Package Policy CI 已成功：<https://github.com/one2world/EjoyFramework/actions/runs/40177606339>。

- 全新 `GitConsumer` 宿主通过 `https://github.com/one2world/EjoyFramework.git#8f58ec5826e01738c1566749789940d524bbc45e` 安装包，并带入默认程序集样例和测试。首轮全量 3579 通过、4 项分配断言失败、8 跳过。失败项为 UnitRaycastQueryTests.WarmedQueriesDoNotAllocate、AllocProbeTests.Probe_StackPushPopForeach_WarmLambda_DoesNotAllocate、BenchmarkInfraTests.PerfCapture_AddFrame_DoesNotAllocate、BufferPoolTests.ByteBuffer_WriteWithinCapacity_DoesNotAllocate。
- 未修改源码、未放宽分配断言：上述 4 项专项复测通过（命令配置 `-repeat 10`，XML 按 4 个测试报告），再次全量 `GitConsumer-EditMode-02.xml` 为 **3583 通过、0 失败、8 跳过**。首次失败的准确原因尚未证实，不能把后续通过写成已修复首次导入的分配抖动。
- 安装并实际运行 arm64 Unity **6000.6.5f1（3ff58d469c8a）**，使用独立 `Compatibility6000.6` 宿主以保留主宿主 6000.4 配置。`6000.6-EditMode-01.xml`：**3583 通过、0 失败、8 跳过**。这包含该版本实际编译的 EntityData 显式 ID 分支及静态物理查询修复，不再仅依赖旧版本条件编译或他人的消费记录。

## 默认跳过项的处理

- 6 项 MutableString unsafe 路径用例：已在显式启用 EJOY_UNSAFE_STRING 的 Unity Mono Editor 中另行通过；默认关闭该可选路径时仍按设计跳过。这不代表已经验证 IL2CPP 下的 unsafe 字符串。
- 真实 TCP 回环用例为 Explicit：已在专项按完整测试名选中并通过；全量默认套件依然跳过。
- ConfigBlob 的 FileShare.None 双重失败用例依赖 Windows 强制锁语义；macOS/POSIX 不能复现，本轮仍跳过，不能记为通过。

## 复现入口

从仓库根目录执行，确保同一个宿主没有其他 Editor 进程：

```sh
unity --non-interactive --no-banner --json test "$PWD/Tests~/Unity" \
  --mode EditMode --editor-version 6000.4.8f1 \
  --output "$PWD/Tests~/Unity/TestResults/unity-cli/EditMode.xml" \
  -- -nographics -logFile "$PWD/Tests~/Unity/TestResults/unity-cli/EditMode.log"
unity --non-interactive --no-banner --json test "$PWD/Tests~/Unity" \
  --mode PlayMode --editor-version 6000.4.8f1 \
  --output "$PWD/Tests~/Unity/TestResults/unity-cli/PlayMode.xml" \
  -- -logFile "$PWD/Tests~/Unity/TestResults/unity-cli/PlayMode.log"
```

不要在 `-runTests` 命令额外加 `-quit`；测试完成后由 Test Runner 退出。专项 TCP 需通过 `--filter EjoyFramework.GamePlay.Tests.Netcode.Transport.TcpNetTransportTests.LocalhostRoundTrip_ClientAndServerExchangeBytes` 显式选择。可选 unsafe 路径需在测试前将 EJOY_UNSAFE_STRING 加入 Standalone 的 Scripting Define Symbols，让运行时和测试程序集同时重新编译，测后恢复。

## 独立播放器的失败与诊断

- 6000.4 原生 Test Runner 的 StandaloneOSX 构建，Mono 和 IL2CPP 均完成构建、实际启动 Metal/Apple M5，但读取 level0 时报告 `The file .../level0 is corrupted! [Position out of bounds!]`，在任何测试执行前结束。另一次首次导入出现测试启动器脚本丢失并等待连接；重复启动和显式添加 test-framework 直接依赖后仍能复现 level0 失败。
- 空白项目仅含同版本 Test Framework 的 1 项 Player 对照测试通过；加入 Burst/Collections/Input System/UGUI/Newtonsoft 等相同版本依赖后仍 1/1。加入本包但不启用本包 testables，仍可复现 level0 原生失败。因此不能声称仅为旧宿主缓存或安装损坏，也不能未经定位直接归因于 Unity 引擎。扫描本包 1504 个已跟踪 GUID 与宿主依赖 2607 个 .meta，未发现跨依赖 GUID 冲突。
- 为继续检查实际运行时，在独立临时宿主中用无序列化配置的启动组件，在运行时创建同版本 Unity Test Runner 的 PlaymodeTestsController，仍执行原有 EjoyFramework.Tests.PlayMode 测试、NUnit 断言和 coroutine runner；保留 Test Runner 私有类型的 linker metadata。此诊断入口不替换测试断言，且依赖 UTF 1.6 内部接口，不能等同默认 `unity test` Player 入口已恢复。
- 该入口的首轮真实 Mono Player：37 通过、6 失败（共 43），失败全部为 MVVM binder。私有测试 VM 未登记 PropertyAccessor，Editor 的反射兜底掩盖了 Player 中“必须静态登记访问器”的契约。已在夹具 SetUp 为 Text、Flag、DoCommand、Items 登记强类型访问器；不为生产 Player 增加反射兜底。

最终 arm64 Mono 诊断 Player `RuntimeRunner-Mono-Complete.xml`（包含下方场景隔离修复）同样为 **43/43**，通过 `arch -arm64` 实际执行。

## 域重载与测试场景隔离补测

- 默认域重载下，后续源码重新编译后的 6000.4/6000.6 PlayMode 也观察到进入播放后没有测试执行/结果的停滞。6.6 关闭图形仍复现，采样显示 Editor 事件循环/PlayerLoop 在运行，不据此假定是死锁或 GPU 故障。中止的是本轮启动的测试进程，保留日志。使用独立宿主关闭 Domain Reload 后可以执行用例；默认入口问题仍保留为 [#22](https://github.com/one2world/EjoyFramework/issues/22)。
- 6.6 关闭 Domain Reload 的首轮为 39 通过、4 失败，均为 EventComponent 重复注册。PlayModeTestBase 的 TearDown 原先仅发起 UnloadSceneAsync，较快的无图形执行可在组件 OnDestroy 前开始下一用例。已同步销毁本用例创建的 root，使组件确定性注销，再卸载空场景；不清空生产注册表或放宽重复注册规则。
- 修复后 `6000.6-PlayMode-Final.xml`：**43/43**（6000.6.5f1、关闭 Domain Reload、-nographics）。`PlayMode-NoDomainReload-Final.xml`：**43/43**（6000.4.8f1、关闭 Domain Reload、启用 Metal）。这补齐该模式下的用例执行，不代表默认域重载入口恢复。
- 最终 arm64 IL2CPP 诊断 Player `RuntimeRunner-IL2CPP-Final.xml`：**43/43**；实际执行 `arch -arm64`，可执行文件和 GameAssembly.dylib 均为 arm64。记录的是实际启动后完成的测试，不仅是 IL2CPP 转换、链接或安装成功。

## 剩余问题与明确未验证范围

1. [#22：默认 Test Runner 启动/恢复](https://github.com/one2world/EjoyFramework/issues/22)。默认 Player 入口及正常域重载下的稳定运行尚未修复；诊断入口/关闭域重载通过不关闭此问题。诊断工具源码保存在 [Tools~/Unity/RuntimeRunner](../Tools~/Unity/RuntimeRunner/README.md)，其 UTF 内部接口和显式链接保留条件必须随报告一起说明。
2. [#23：Git 新宿主首轮分配抖动](https://github.com/one2world/EjoyFramework/issues/23)。保留首次 4 项失败、专项通过和后续全量通过的记录，原因未定位。
3. Windows 强制文件锁用例仍未在 Windows 运行；MutableString 可选 unsafe 路径已测 Unity Mono Editor，但不声称完成 IL2CPP 的该可选路径验证。
4. 本轮独立 Player 为 Unity 6000.4.8f1/macOS；6000.6.5f1 的独立 Player/AOT 尚未另测；不代表 iOS/Android 真机、移动 GPU 性能或完整游戏流程/美术/动画/手感验收。旧消费项目/外部 FirstRep 报告属于其对应版本的历史证据；本轮 GitConsumer 是本包独立宿主，不能替代外部游戏项目验收。

已修复 issue 的本轮明确覆盖：ResourceComponentModeTests **3/3**（#16）、EntityManagerTests **10/10**（#17）、DefaultEntityHelperTests **5/5**、StaticPhysicsHistoricalQueryTests **12/12**、DropInPrefabWorkflowTests **7/7**；均已在真实 Unity EditMode 执行。全量、专项、跨版本存在重叠，不能相加为独立覆盖数。原始 XML 的计数、SHA256 和被测源码摘要保存在同目录的[机器可读结果摘要](UnityRegression-2026-10-25.results.json)。

关闭域重载的可复现配置入口在 [LocalPlayModeConfiguration.cs](../Tools~/Unity/LocalPlayModeConfiguration.cs)。仅在独立回归宿主中，将它复制进 `Assets/Editor/`（或本仓库宿主的 `Assets/Tests/Editor/`），先用 `-batchmode -quit -projectPath <宿主> -executeMethod EjoyFramework.Tests.LocalPlayModeConfiguration.DisableDomainReload` 配置并退出，再执行上述 PlayMode 命令。该配置是本轮针对 #22 的测试绕行条件；不要据此修改外部游戏的验收配置。生成的本机辅助脚本已从主宿主源码目录移除，原始副本保存在本机证据目录。
