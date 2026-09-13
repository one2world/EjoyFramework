# ConfigBlob — 零解析二进制配置表方案

> 2026-08-14 定稿。来源：DOTS 研究「可抄精华 ①：Blob 式配置表（相对偏移 + memcpy 加载 + 引用计数去重）」，
> 见 `DOTS-Research.md`。与零 GC 基建（TempText / AssetBatch 等）同一路线：加载不解析、读取不分配。

## 一句话

配置表在编辑器侧一次性写成单个二进制文件（`.ejcb`），运行时**拿到一段只读内存即完成加载**
（mmap 按页惰性调入，不进托管堆），之后所有读取都是「编译期常量偏移 + 一次直读」，零解析、零装箱、零 GC。

## 与现有 DataTable 文本链路的关系

**并行新链路，不触碰旧路径。** 旧的 `DataTableManager` + 文本解析仍然可用；业务可按表逐步迁移。
两条链路读同一份源表（`Assets/GameMain/DataTables/**/*.txt`），数据不存在两份权威。

## 全链路工作流

```
改表 (.txt / .csv)
   │
   ├─ 只改数值 ──────────────────────────────► 直接导出
   │
   └─ 改结构（加删列/类型）──► 菜单 EjoyFramework/Core/CodeGen/Generate ConfigBlob
                                （重新生成 Reader/Builder/Schema，等编译完成）
                                          │
                                          ▼
              菜单 EjoyFramework/Core/Config/Export ConfigBlob (StreamingAssets)
              （出包时 IPreprocessBuildWithReport 钩子也会自动导出，失败即中止构建）
                                          │
                                          ▼
                     Assets/StreamingAssets/ConfigTables.ejcb
                                          │
                                          ▼
   运行时：var blob = ConfigBlobStreamingAssets.Open("ConfigTables.ejcb", ConfigBlobSchema.SchemaHash);
           var monsters = new MonstersTable(blob);
           var boss = monsters.GetById(5);        // 二分查主键，O(log n)，零分配
           float speed = boss.Speed;              // 常量偏移直读
           foreach (var row in monsters) { ... }  // struct 枚举器，无装箱
           ...
           blob.Release();                        // 引用计数归零才真正释放
```

顺序错了不会静默出错：`Open` 校验 schemaHash，代码与数据不同源时明确报错并提示重新导表。

## 二进制格式（v1，小端，4 字节对齐）

```
Header(32B)  : magic 'EJCB' | formatVersion | schemaHash(8B) | tableCount | reserved
TableDir[N]  : { nameHash(8B) | tableOffset | tableSize }，按 nameHash 升序（查表 = 二分）
Table        : { rowCount | rowSize | pkIndexOffset | rowsOffset }
               PK 索引区：升序 int 主键[rowCount] + int 行号[rowCount]（二分只扫紧凑 int 数组）
               行数据区：rowSize 定长行连续排列；变长字段存指向字符串池的 int 绝对偏移，0 = 空
StringPool   : 文件尾部，{ utf8Len | utf8 bytes }，构建期全局去重（跨表同串只存一份）
```

## 分层与文件清单

| 层 | 位置 | 内容 |
|---|---|---|
| Core.Blobs（纯 C#） | `Runtime/EjoyFramework.Core/Blobs/` | `ConfigBlob`（容器+读原语+引用计数）、`ConfigTableView`、`BlobString`、`ConfigBlobWriter`（写侧）、`ConfigBlobHash`（FNV1a64）、来源：`NativeAllocBlobSource` / `MmapBlobSource` / `ApkOffsetBlobSource` + `ZipEntryLocator`、门面 `ConfigBlobFile` |
| Core.Unity | `Runtime/EjoyFramework.Core.Unity/ConfigBlob/` | `ConfigBlobStreamingAssets`（平台选源） |
| Editor codegen | `Editor/.../CodeGen/ConfigBlob/` | `ConfigBlobGenerator`（第 7 条 codegen 线）、`ConfigBlobSchemaSource`（TAB/CSV 读入 + 类型推断）、`ConfigBlobLayout`（布局/对齐/schemaHash）、Reader/Builder 两个 Emitter、`ConfigBlobTypeSidecar` |
| 生成产物（运行时） | 对应 asmdef 目录下的 `Generated/ConfigBlob/`；DLL、预定义程序集或无 asmdef 时回退到 `Assets/Generated/ConfigBlob/` | 每表 `XxxRow`（readonly struct）+ `XxxTable`（GetById/TryGetById/索引器/struct 枚举器）+ `ConfigBlobSchema.g.cs`（全部偏移常量） |
| 生成产物（编辑器） | 对应 Editor asmdef 目录下的 `Generated/ConfigBlob/`；无法定位 asmdef 时回退到 `Assets/Editor/Generated/ConfigBlob/` | 每表 `XxxTableBuilder` + 总装 `ConfigBlobBuild`（Build/WriteToFile） |
| 游戏侧导出 | `Assets/Editor/ConfigBlobExport.cs` | 菜单导出 + 出包前置钩子 + 导出后按运行时路径回开自校验 |
| 类型推断边车 | `ProjectSettings/ConfigBlobTypes/*.types` | 推断类型快照，**签入版本库**；推断漂移时生成器告警 |

## 源表格式约定

- TAB 表（现有 `DataTables/**/*.txt`）：`#` 注释行 + TAB 数据行。字段名头 = 第一条「列数一致、
  全为合法标识符、且含 Id 列」的注释行。类型默认由全量数据推断（bool → int → long → float → string 取最窄）。
- 可选显式类型行：`#@types	int	string	float ...`（第 0 列被标记占位，要求首列必须是 Id）。
  写了它就不做推断，也没有推断漂移问题，**结构稳定后建议补上**。
- CSV 源表（`Assets/Configs/Source/*.csv`）：第 0 行字段名、第 1 行类型、第 2 行起数据。
- 每表必须有 int 主键列 `Id`；同名表（不同目录）整组拒绝生成。

## 平台矩阵

| 平台 | 路径 | 机制 |
|---|---|---|
| PC / iOS / 主机 | `ConfigBlobFile.OpenFile` | mmap 整文件；映射失败自动回退堆外整读（Warning 一次） |
| 编辑器 | 同上但**禁用 mmap** | 映射会锁住导出目标文件，播放模式泄漏一次即毁掉后续导出/出包；编辑器不需要按页惰性 |
| Android 真机 | `ConfigBlobFile.OpenZipEntry(Application.dataPath, "assets/<file>")` | 定位 APK 内 STORED 条目 → 对 APK 区间 mmap；**映射不可用的设备自动回退按偏移整块读入**（Android 恰是 mmap 最不可靠的平台） |
| Android + Play Asset Delivery / OBB | 不支持直读 | 先落沙盒再 `OpenFile`（打开时报错信息会给出该指引） |
| WebGL | 不支持 | 无本地文件系统；`ConfigBlobStreamingAssets` 直接抛明确错误，需下载进内存后用 `NativeAllocBlobSource` 打开 |

Android 依赖条目 **STORED（未压缩）**：Unity Gradle 模板默认把 StreamingAssets 内出现的扩展名并入
aapt `noCompress`，`.ejcb` 天然满足；自定义模板破坏该行为时 `ZipEntryLocator` 会明确报错并给修复指引，
不会退化成慢路径。

## 硬约束与坑

- **schemaHash 双向校验**：生成代码里的 `ConfigBlobSchema.SchemaHash` 必须与数据文件一致，否则 `Open` 抛错。
  这是「先 codegen 后导出」顺序的兜底。
- **`Slice`/`BlobStringHandle` 返回的 Span 即用即弃**：裸指针视图不持引用，跨 `Release` 使用即 use-after-free；
  需要长期保存用 `BlobStringHandle.ToString()` 拷贝。
- **边界检查常编译**：Release 构建同样生效（内存安全问题，不是逻辑断言）。
- **引用计数**：`Open` 返回计数 1；多方共享用 `Retain`/`Release` 配对；归零后任何读取抛异常。
- **mmap 锁文件**：映射打开的文件在 Release 前拒绝写共享（映射来源刻意无终结器，泄漏在进程退出前不解锁）。
  编辑器侧一律走 `allowMemoryMapping:false`（`ConfigBlobStreamingAssets` 与导出校验已内置）。
- **文件名单一事实源**：`EjoyGame.Constant.ConfigBlob.FileName`（运行时程序集），编辑器导出引用它，勿在别处重复字面量。
- 单文件 ≤ 2GB；zip 不支持 ZIP64；数值列不允许留空格（空串只能落 string 列）。
- 生成产物、`.ejcb`、`.types` 边车全部签入版本库。

## 验证状态（2026-08-14，证据分级）

**已证明（有直接证据）**
- EditMode 85/85 绿（格式/写侧/读侧/zip 定位/门面；含"打开期间可覆盖写源文件"与
  "mmap+回退双失败合并报错"两条回归防线，后者用 Windows 强制文件锁真实触发）；PlayMode 15/15 绿。
- zip 非映射读入路径经公开参数（`OpenZipEntry(..., allowMemoryMapping:false)`）直测，不依赖故障注入。
- 真实 6 张 ChentangGuard 表：codegen → 导出 7420 字节 → StreamingAssets 回读，中文串/浮点/主键二分/枚举比对通过。
- Player 编译证据：Android 与 Win64 目标各产出 53/54 个程序集零错误；二进制字符串扫描确认
  Android DLL 含 `"assets/"` 分支且不含 WebGL 分支、Win64/编辑器 DLL 反之——预处理分支按目标正确裁剪。
- noCompress 机制：本机 Unity 6000.4.8f1 官方 Gradle 模板（launcher/main）均为
  `noCompress = **BUILTIN_NOCOMPRESS** + unityStreamingAssets.tokenize(', ')`，且本工程无自定义模板覆盖。
- 全量 EditMode 套件 2175 例仅 1 失败（`TouchInputDriverTests`，InputSystem 断言）：经隔离实验证明
  与本方案无关——移除本方案全部文件后该失败原样复现；该测试文件本身是未提交的其他会话 WIP。

**本机不可证明（需真机/额外条件，风险已界定）**
- APK 内条目实际为 STORED：机制已在模板层证实，端到端需出一次 Android 包验证；若被破坏，
  运行时是带修复指引的明确报错（`ZipEntryLocator`），不是静默慢化或错值。
- Android 设备上 mmap 是否可用：不可用时自动回退整块读入（回退主体已直测），仅损失按页惰性。
- `Application.dataPath` = base APK 路径：Unity 文档级行为，真机一跑即知；失败表现为明确的"条目不存在"报错。
- WebGL 分支（3 行 throw）：本机未装 WebGL 模块无法编译验证；该分支无外部依赖，行为是显式抛错。
