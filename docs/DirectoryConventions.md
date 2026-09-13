# Framework 目录与命名规范

**新增功能必须先遵守现有目录和命名。** 若认为现有结构不合适，先按当前规范完成接入，再通过单独、完整的重构统一调整；不得由一个新模块先建立另一套规则。

## 目录先按层，再按模块

| 内容 | 位置 | 命名空间与程序集 |
|---|---|---|
| 平台无关基础功能 | `Runtime/EjoyFramework.Core/<Module>/` | 命名空间通常为 `EjoyFramework.Core.<Module>`；默认归 Core |
| Unity 基础适配 | `Runtime/EjoyFramework.Core.Unity/<Module>/` | 命名空间和程序集使用 `EjoyFramework.Core.Unity` |
| 平台无关玩法 | `Runtime/EjoyFramework.GamePlay/<Module>/` | 遵循所在玩法模块的命名空间和 asmdef |
| Unity 玩法适配 | `Runtime/EjoyFramework.GamePlay.Unity/<Module>/` | 遵循现有 GamePlay.Unity 层约定 |
| 编辑器代码 | `Editor/EjoyFramework.Core.Unity.Editor/<Module>/` 或对应 GamePlay 编辑器层 | 跟随所属编辑器层 |
| Core EditMode 测试 | `Tests/Editor/EjoyFramework.Core.Tests/<Module>/` | 加入 `EjoyFramework.Tests`；命名空间可用 `EjoyFramework.Tests.<Module>` |
| Core PlayMode 测试 | `Tests/Runtime/EjoyFramework.Core.Tests.PlayMode/<Module>/` | 加入 `EjoyFramework.Tests.PlayMode`；命名空间可用 `EjoyFramework.Tests.PlayMode.<Module>` |
| 包的可导入示例 | `Samples~/<Demo>/` | 保留既有示例命名空间与 GUID，避免破坏消费者引用 |
| 项目外部验证工具 | `Tools~/<Module>/` | csproj 文件名与输出程序集名一致；单独管理构建和测试产物 |
| 依赖 Unity 默认程序集的集成测试 | `Tests~/Unity/Assets/Tests/Editor/` | 独立测试工程；测试程序集引用包，包不引用测试工程 |

物理目录和程序集不是一一对应关系。确需隔离依赖的模块可以参照 `Core/Blobs/`，在层内模块目录放置独立 asmdef；不要因此在 Runtime 根下增加 `EjoyFramework.Core.<Module>` 目录。新 asmdef 必须有实际的依赖隔离用途。

已有 `Core.Jobs.Unity` 等历史例外在专门重构之前保持兼容，不作为新增模块另起根目录的依据。本文不批量搬迁这些既有模块。

## 类型与文件命名

- 目录、文件、类型使用现有 PascalCase 风格；接口以 `I` 开头，私有字段沿用 `m_` 前缀。
- 普通类型文件以其主要类型命名；Unity MonoBehaviour 文件必须与类型名一致。分部类使用已有的 `Type.Part.cs` 风格。
- Core 内按模块分命名空间；Core.Unity 的模块子目录不额外创造 `<Module>.Unity` 命名空间。
- Unity 适配类型用有业务含义的 `XxxComponent`、`XxxHelper`、`XxxView` 等现有后缀，菜单放在 `EjoyFramework/Core/...` 或对应层。
- 示例中的局部数据类型、内部辅助系统可与示例入口放在同一文件；不要把示例专属类型提升为 Framework 公共 API。
- asmdef 的 `name`、文件名、消费者引用必须一致；`rootNamespace` 采用该程序集现有约定。

## ECS 的落位

```text
EjoyFramework/
  Runtime/
    EjoyFramework.Core/Ecs/                  # 独立 EjoyFramework.Core.Ecs 程序集
    EjoyFramework.Core.Unity/Ecs/            # 并入 EjoyFramework.Core.Unity
  Tests/
    Editor/EjoyFramework.Core.Tests/Ecs/     # 并入 EjoyFramework.Tests
    Runtime/EjoyFramework.Core.Tests.PlayMode/Ecs/
  Samples~/EcsDemo/                         # EjoyGame.Samples.EcsDemo，保留兼容命名
  Tools~/Ecs/                               # 独立 .NET 验证、基准
  Tests~/Unity/                             # 自包含 Unity 测试工程
```

ECS 内核的独立程序集保证其不引用 Unity 或其他框架模块。Unity 适配层依赖内核，反向依赖禁止。目录调整不改变 World 的实例所有权，也不把它改为全局 FrameworkModule。

`~` 是 UPM 忽略导入目录的约定，仅用于示例、独立工具和测试工程边界；Runtime、Editor、Tests 内现有层和模块命名保持不变。Game 自己的示例及工具仍按 Game 的目录规范管理。

## 迁移要求

搬迁文件时保留 `.meta` GUID，同步修改 asmdef、命名空间、示例、构建入口和文档。移除被合并的 asmdef 后，确认源码确实进入目标程序集。验证必须覆盖独立编译、Unity 编译和受影响的测试，不能只依赖文本替换成功。历史测试报告保留原始内容，迁移后的结果另存，避免混淆程序集边界。
