# LiveOps：遥测、崩溃、画质与性能覆盖层

上线后要回答三个问题：**玩家设备上跑得怎么样**（遥测）、**崩在哪 / 卡在哪**（崩溃与 ANR）、**画质是否匹配设备**（分级与自动画质）。
框架把这三件事做成共用会话、共用上报通道的一套管道，外加一个真机可用的性能覆盖层。

| 模块 | 接口 | Unity 组件 | 入口 |
|---|---|---|---|
| 性能遥测 | `ITelemetryManager` | `TelemetryComponent` | `GameEntry.Telemetry` |
| 崩溃 / ANR / 异常退出 | `ICrashManager` | `DiagnosticsComponent`（`Crash` 属性） | `GameEntry.Diagnostics.Crash` |
| 设备分级 / 画质 / 自动画质 | `IQualityManager` | `QualityComponent` | `GameEntry.Quality` |
| 性能覆盖层 | — | `PerfOverlayComponent` | `GameEntry.PerfOverlay` |

会话 id 与设备摘要由 `AppSession` 统一提供，遥测批次、崩溃报告、分级记录用同一个会话 id，后台可以直接关联。

## 1. 性能遥测

- 记录是定长 struct `TelemetryRecord`（8 float + 8 int），`Record` 零分配；种类见 `TelemetryKind`
  （帧时间窗口 / 内存 / 加载耗时 / 热电 / 崩溃 / 分级 / 画质变化，业务自定义从 1000 起）。
- 会话开始时按 `SampleRate` 掷骰决定本会话是否采样（xorshift，可播种）；`Enabled` 是隐私总开关。
- 攒满 `BatchSize` 或到 `FlushIntervalSeconds` 封成二进制批次（魔数 `EJTM`、版本、会话、设备、版本号、记录）。
  单批在途，后端回调可在任意线程，结算在主线程；失败重排队，离线队列超上限丢最旧。
- 默认后端 `FileTelemetryBackend` 落盘 `persistentDataPath/Telemetry/*.ejtm`；接入 HTTP / 第三方 SDK 时实现 `ITelemetryBackend`。

## 2. 崩溃 / ANR / 异常退出

`DiagnosticsComponent` 默认开启 `ICrashManager`，报告落盘 `persistentDataPath/Crash/*.ejcr`。

- **异常**：来自 Unity 日志流（Exception 级别）与 `AppDomain.UnhandledException`。按 `CrashFingerprint`
  （异常短类型名 + 前 12 帧，去掉行号、IL 偏移、`(at …)`）会话内去重计次，每会话上限 32 份；首次出现即落盘。
  未处理异常先以非致命进来，再以 fatal 升级同一份报告，不重复计数。
- **面包屑**：最近 64 条日志（Info 以上）+ `AddBreadcrumb` 业务事件，随报告附带。
- **阶段与用户键**：`SetPhase("Loading:Region_12")`、`SetUserKey("uid", …)`，随报告与会话标记记录。
- **ANR**：主线程每帧原子写心跳，看门狗线程发现停顿超过阈值（默认 5s）立即落一份 Hang 报告，恢复后补记实际时长。
  已知长阻塞用 `SuspendHangDetection` / `ResumeHangDetection` 包住；后台期间自动免判；编辑器默认关闭（断点会误判）。
- **异常退出**：会话期间盘上保留会话标记（阶段、前后台、低内存、卡死、已报致命 + 面包屑），正常退出删除。
  下次启动发现残留即生成 `AbnormalExit` / `BackgroundKill` / `LowMemoryKill` / `HangKill` 报告——原生崩溃与 OOM 被杀在托管侧唯一可靠的信号。
- **上报**：实现 `ICrashUploader` 并 `SetUploader`；逐个上传，成功删除，本会话连续失败 3 次停止、下次启动重试；盘上最多保留 20 份。
- 每份报告同时写一条 `TelemetryKind.Crash` 遥测（kind / 消息指纹 / 崩溃指纹 / 次数 / 卡顿秒）。

与第三方崩溃 SDK（Crashlytics / Bugly）的桥接仍走原有的 `ICrashReporter`，两者并存。

## 3. 设备分级与画质

### 分级

`QualityComponent` 启动时读 `SystemInfo`，`DeviceTierClassifier` 依次应用：覆盖规则 → 硬件评分 → 上限规则。
硬件评分只是 GPU 性能的粗略代理，所以分级只决定**起点档位**，运行期由自动画质按真实帧耗时纠偏；
线上 `TelemetryKind.Tier` 记录带评分回收，表现偏离的机型写成覆盖规则下发即可校准。

规则文本（`QualityComponent.m_TierRules`，出错时整份忽略并报错，不阻断启动）：

```text
# 机型 / GPU / CPU / 系统子串覆盖，先写先得，不受上限规则约束
override gpu "Adreno (TM) 740" = Ultra
override model "iPhone10," = Low
# 只约束评分结果
cap memory < 3072 = Low
cap vram < 1024 = Minimum
# Low / Medium / High / Ultra 的最低分
thresholds mobile 0.40 0.55 0.70 0.85
thresholds desktop 0.35 0.50 0.65 0.85
```

### 档位与旋钮

五档（0..4）。每个旋钮是"每档一个数"，由应用器落到引擎：

| 旋钮 | 默认表（0→4） | 应用 |
|---|---|---|
| RenderScale | .75 .85 .9 1 1 | `ScalableBufferManager`；HDRP / URP 注册管线自己的应用器 |
| ShadowDistance | 30 50 80 120 180 | `QualitySettings.shadowDistance` |
| LodBias | .6 .8 1 1.5 2 | `QualitySettings.lodBias` |
| TextureMipLimit | 2 1 1 0 0 | `QualitySettings.globalTextureMipmapLimit` |
| StreamingRadiusScale | .6 .75 .9 1 1.2 | `IWorldStreamingManager.RadiusScale` |
| PopulationScale / ParticleBudgetScale | — | 业务读取 |
| TargetFrameRate | 30 30 60 60 60 | `Application.targetFrameRate`，也是自动画质的帧预算 |

业务旋钮用 `DefineKnob(QualityKnobs.Custom + n, …)` 注册，实现 `IQualityApplier` 应用。

上限 = min(玩家选择的档位（未选 = 设备档位）, 热 / 电量上限)。玩家选档由 `QualityComponent.SetUserLevel` 持久化。

### 自动画质

`AutoQualityController`：先调动态分辨率（每 0.5s 一步），分辨率到底仍超预算持续 2s 才降一档；
长期有富余先把分辨率升满，再持续 8s 且过升档冷却（15s）才升一档；升档后 10s 内又被迫降档视为失败，冷却翻倍（上限 120s）。
超过 250ms 的单帧（加载卡顿）不计入；加载与过场期间调用 `SuspendAuto` / `ResumeAuto`。

喂给控制器的是 **FrameTimingManager 的工作耗时**：max(主线程耗时 − Present 等待, 渲染线程耗时, GPU 耗时)。
需要在 Player Settings 勾选 **Frame Timing Stats**；平台不支持时退化为墙钟帧间隔，此时帧率被上限封住，
只会降档、不会越过帧率档位升档。

`PerformanceComponent` 的"自适应等级 + 切 Unity 质量档"是旧方案，与自动画质同时开启会互相覆盖，`QualityComponent` 启动时会警告。

## 4. 性能覆盖层

`PerfOverlayComponent` 自建 UGUI 画布，不依赖美术资源（字体用引擎内置 `LegacyRuntime.ttf`），真机可用。

- 内容：FPS 与帧时间分位 / spike；画质档位、设备分级、渲染缩放、工作耗时；内存（mono / 分配 / 保留 / 显存 / 常驻 bundle）；
  流送单元与半径；崩溃与遥测状态；帧时间柱状图（预算线，绿 / 黄 / 红）；业务自定义行 `SetCustomLine`。
- 稳态零分配：数值经 `TempText` 写入 `OverlayTextBuffer`，内容不变不重建网格；默认 0.25s 刷新；隐藏时不采集。
- 开关：`Visible` / `Toggle()`；装了 Input System 时 F3 或三指同时按下切换。
- 只预取可打印 ASCII，非 ASCII 字符留空（保证零分配）。

## 5. 基准与真机采集

### 微基准（编辑器 / CI）

- `BenchmarkRunner.Run(name, n => { 循环 n 次 }, options, probe)`：预热 → 每样本操作数自动倍增到不低于 `MinSampleMillis`
  （避开计时器分辨率）→ 多样本 → 最小 / 中位 / 均值 / P95 / 标准差；分配在计时之外单独测一轮。
- 分配探针 `UnityAllocationProbe` 用 Profiler 的 `GC.Alloc` 事件计**分配次数**（Mono/Boehm 下
  `GC.GetAllocatedBytesForCurrentThread` 恒为 0，不能用）；`BenchmarkInfraTests` 用 10 次已知分配自校准，探针失效会直接报错。
  没有探针时结果标记为"未测"，不会写成 0。
- `BenchmarkReport`：带环境头的制表符文本（人可读、可 diff、无需 JSON 库）；`BenchmarkComparison` 抗噪判定
  （变慢要求中位比超阈值**且**当前最快样本仍慢于基线中位；分配变多直接判回归）。
- `FrameworkBenchmarks` 覆盖池、堆、空间查询、流送重评估、TempText、遥测、崩溃指纹、自动画质，零分配路径硬断言 0 次。
- 运行：`pwsh EjoyFramework/Tools~/Benchmarks/RunBenchmarks.ps1`（`-SaveBaseline` 保存基线；有代理时设 `EJOY_PROXY`）。
  结果写工程内 `TestResults/Benchmarks/editmode-<UTC>.tsv` 与 `editmode-latest.tsv`，存在 `editmode-baseline.tsv` 时输出对比。
  时间只在同机同配置下可比，分配结论跨机器可比。

### 真机采集

`PerfCaptureComponent`（`GameEntry.PerfCapture`）：`StartCapture(label, seconds)` 后逐帧记录帧耗时与工作耗时、每 0.5s 记录内存峰值，
`Mark("enter town")` 打标记，停止时写 `persistentDataPath/PerfCaptures/<label>-<UTC>.tsv`（`ReportDirectory` 可改），
并写一条 `TelemetryKind.PerfCapture` 遥测。分位用 0.1ms 桶直方图（固定内存、零分配、与帧数无关）；超过 50 / 100ms 的帧单独计数。
采集期间默认暂停自动画质、在崩溃采集里标记阶段 `Capture:<label>`，保证可复现、崩了也知道在跑哪段。
基准场景 / 跑图脚本 / 自动化回归按固定镜头路径 Start → 走完 → Stop，同机对比前后版本的报告。
