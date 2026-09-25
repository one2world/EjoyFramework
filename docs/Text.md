# Text — 零分配文本基建

> WS5-M1（2026-09-25）。Core：`Runtime/Core/Base/Text/`（R1 目录重构后随 Base 一起上移），
> Unity：`Runtime/Core.Unity/Text/UnityTextFormatters.cs`，编辑器：`Editor/Core.Unity.Editor/Text/StringHashValidator.cs`。

## 选哪个（一张表）

| 场景 | 用法 | 分配 |
|---|---|---|
| 每帧拼 UI / 调试文本 | `TempText.Rent()` + `Append` / `AppendFormat` → `AsSpan()` 交给 TMP / `BindableText.Set(t)` | 0 |
| MVVM 文本属性 | `BindableText.SetValue(x, "F1")`（任意类型，内容不变不通知） | 0 |
| 接口只收 string 的小整数（uGUI `Text.text`） | `NumberStrings.Get(hp)`（默认缓存 0..1023） | 0（缓存内） |
| 金币 / 战力 / 倒计时 / 进度 | `AppendCompact` / `AppendDuration` / `AppendPercent`（或 `TextFormat.TryFormat*` 写 Span） | 0 |
| 热路径上的字符串键 | `static readonly StringHash k_X = StringHash.Of("X")` + 哈希重载 | 0 |
| 自建字符串键表 | `StringMap<T>`：string / span / StringHash 三种查找 | 0（查找） |
| 解析 TAB 文本（数据表 / 配置 / 本地化） | `TextLineEnumerator` + `TextFieldReader` + `SpanParse` | 仅 string 列 |

## 主路径

```csharp
// 格式化：语法与 string.Format 相同，输出与 string.Format(InvariantCulture, ...) 逐字一致，值类型不装箱
using (var t = TempText.Rent())
{
    t.AppendFormat("{0}/{1}  {2,6:F1}%", hp, maxHp, ratio * 100f);
    t.Append(' ').AppendCompact(gold, CompactNumberFormat.Chinese);          // 1.2万
    t.Append(' ').AppendDuration(remain, DurationStyle.Clock, DurationRounding.Ceiling);
    label.SetText(t.AsSpan());
}

// 本地化：模板非法（翻译稿常见）时原样追加模板并返回 false，不抛异常
loc.AppendFormat(t, "ui.hp", hp, maxHp);
loc.AppendPlural(t, "item.apple", count);   // 复数键在栈缓冲里拼出后按切片查表

// 字符串哈希键
static readonly StringHash k_MaxHp = StringHash.Of("MaxHp");
int maxHp = GameEntry.Config.GetInt(k_MaxHp);

// 自定义类型零分配：实现 ITextFormatter<T> 并在启动期注册一次
TextFormatter.Register<MyStat>(new MyStatFormatter());
```

## 语义要点

| 主题 | 行为 |
|---|---|
| 文化 | 全部不变文化（与 TempText 既有数值 Append 一致）；本地化数字格式请用翻译模板 |
| 非法格式串 | `AppendFormat` 抛 `FrameworkException`（位置 + 原因 + 修法），`TryAppendFormat` 返回 false；两者都回滚已写内容。参数格式串非法（如 int 的 `{0:Q}`）同样处理，BCL 异常作为 InnerException |
| 类型分派 | 基础数值 / bool / char / string / DateTime / TimeSpan / Guid / 所有枚举 / 已注册类型零分配；其余退回 `IFormattable.ToString` / `ToString()`（开发版首次使用打一条 Debug 提示去注册） |
| 枚举 | 名字表缓存；未定义值、Flags 组合与带格式串（"D"/"X"）退回 BCL（分配） |
| Unity 类型 | Vector2/3/4、Vector2Int/3Int、Quaternion、Color、Color32、Rect 自动注册，输出与各自 ToString 逐字一致 |
| 紧凑数字 | **截断**不进位（1999 → "1.9K"，显示值永不大于实际）；门槛 / 小数位 / 有效数字 / 分隔符可配，预设不可变 |
| 时长 | Clock "m:ss"→"h:mm:ss"；MinutesSeconds；HoursMinutesSeconds；Compact "1d 2h"（单位文字可本地化，最多 N 个相邻单位）；倒计时用 Ceiling |
| 百分比 | 四舍五入，但 ratio < 1 **绝不显示 100%**；整数运算写出，无 "-0%" |
| StringHash | 标准 FNV-1a-32 over UTF-8（与服务端 fnv32a / GamePlay StableHash 同值；孤立代理项按 U+FFFD 编码，同 `Encoding.UTF8`）；0 保留为 None；64 位版本即 ConfigBlob 表名哈希 |
| 碰撞 | 三道检查：开发版 `StringHash.Of` 登记表同哈希不同名立即报错；`StringMap(uniqueHashes)` / ConfigManager 入库拒绝碰撞键；菜单 **EjoyFramework/Core/Text/Validate StringHash Collisions** 扫描源码里全部 `StringHash.Of("字面量")` |
| `Of` vs `Compute` | `Of` 用于标识符（会登记）；玩家输入等动态数据用 `Compute`（纯函数，不登记，登记表有上限防无限增长） |
| StringMap | 默认模式允许不同键同哈希（按字符区分，本地化表用），此时禁止按 StringHash 查找；uniqueHashes 模式按哈希查找唯一确定（配置用）。枚举顺序同 Dictionary |
| 切行 / 切列 | `TextLineEnumerator` 与 `Split("\r\n","\n","\r")` 同语义、同行号；`TextFieldReader` 与 `Split(sep)` 逐列一致 |
| SpanParse | 整数与 `int/long.TryParse(Integer, Invariant)` 对拍一致（手写，零分配）；浮点交给 BCL；布尔宽松 1/0、true/false、yes/no |
| 数据行 | 配置工具链生成的行实现 `ISpanDataRow`，直接从切片解析，先解析到局部变量、全部成功再赋值；失败返回 false 由解析器带行号告警跳过（旧实现抛异常中断整表）。手写旧式 `IDataRow` 仍可用（每行退回生成一个 string） |
| 线程 | TempText / CharBufferPool 线程本地；TextFormatter / TextFormat / SpanParse / StringHash 计算任意线程；StringHashRegistry 加锁；NumberStrings 惰性填充为良性竞态；StringMap 同 Dictionary（无写者时可并发读） |

## 消费方改造（本里程碑一并完成）

- **Config**：存储换成 `StringMap(uniqueHashes)`，新增全套 `StringHash` 重载与已解析值的 `AddConfig`；文本解析零中间字符串。
- **Localization**：存储换成 `StringMap`，新增 `TryGetRawString(span)`、`AppendString / AppendFormat / AppendPlural`；俄语复数对 `int.MinValue` 不再溢出。
- **DataTable**：`AddDataRow(span)` + `ISpanDataRow`；生成器改为切片解析（黄金文件测试锁定输出）。
- **DataNode**：路径按切片逐段查找，已存在结点零分配；空段被忽略（旧实现会建出名为空串的结点）。
- **RedDot**：结点间直接引用，订阅者写时复制数组，路径在栈缓冲里规整；GetCount / SetLeafCount / 传播 / 触发零分配。
- **BindableText**：数值 `SetValue` 统一经 TextFormatter，新增泛型 `SetValue<T>`。
- **哈希去重**：`ConfigBlobHash.Compute`、GamePlay `StableHash` 改为委托 `StringHash`（结果不变，非 ASCII 不再分配）。

## 基准

`FrameworkBenchmarks`（`--filter Benchmark`）新增 `Text.TempText.AppendFormat4`、`Text.StringHash.Compute16`、
`Text.StringMap.Lookup3`、`Text.SpanParse.Row7`，全部硬断言 0 分配。

## FPSSample 对照

借鉴 `Utils/StringFormatter`：单例同时实现多个 `IConverter<T>`、按泛型接口分派而不装箱。框架版改为
"静态泛型缓存 + 可注册 + BCL TryFormat"：数值输出与 ToString 逐字一致，未知类型退回而不是抛 InvalidCast，
非法格式串回滚而不是写出半截。
