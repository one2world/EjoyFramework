# 独立包迁移与验证

2026-09-13。本仓库基于 `1131df1f` 初始提交继续整理，不使用之前临时仓库的候选提交或 bundle。当前只做本地提交，推送由用户完成。

## 版本控制边界

- 根目录为 UPM 包：`package.json`、Runtime、Editor、Tests、文档及 `.meta` 进入版本控制。
- 代码生成的 partial、注册表、序列化代码是编译输入，必须提交；不能整体忽略 Generated。
- `Samples~` 保存可导入示例；`Tools~` 保存独立验证工具；`Tests~/Unity` 保存默认程序集集成测试工程、样例、generated partial、预制体及 golden。
- Library、Temp、Logs、UserSettings、bin、obj、TestResults、下载归档及凭据不提交。测试结果在本机保留，不随包分发。
- 不包含游戏场景、游戏 UI、下载 ZIP 或旧 Git bundle。Monsters 测试夹具固定自 Game 提交 `6e4fbdcb4f4e398891dbaaad6eaf974904e7e6eb`，不依赖 Game 工作目录。

## 本次验证

| 检查 | 结果 |
|---|---|
| ECS 独立 .NET Standard 2.1 编译 | 通过，0 错误、0 警告 |
| 独立 .NET ECS 测试 | 32 通过、0 失败、0 跳过 |
| ECS 基准程序 | 1,000 / 10,000 / 100,000 实体运行通过，测量区间托管分配为 0 B |
| Core.Unity.Editor 及 Core EditMode 测试静态编译 | 使用本机 Unity 6000.4.8f1 引用程序集编译通过；存在既有废弃 API 和未使用字段警告 |
| unity-cli 独立工程 EditMode 运行 | 未完成：Editor 在许可证检查阶段退出 198，没有测试报告 |
| Git URL 安装、PlayMode、Game 消费验证 | 尚未完成；不能用以上静态编译替代 |

.NET 验证使用本机 SDK 8.0.401 和已缓存的 NuGet 包。默认 SDK 10 的 restore 在本会话遇到 NuGet SSL 凭据错误，未改变项目依赖版本。

## 复现

从本仓库根目录执行：

```powershell
rtk dotnet test Tools~/Ecs/EjoyFramework.Core.Ecs.Tests.csproj -c Release
rtk dotnet run --project Tools~/Ecs/EjoyFramework.Core.Ecs.Benchmark.csproj -c Release
rtk proxy pwsh -File Tools~/Repository/Test-Package.ps1
rtk proxy "C:/Program Files/Unity/unity-cli.exe" test Tests~/Unity --mode EditMode --editor-version 6000.4.8f1 --output Tools~/Ecs/TestResults/EditMode.xml -- -nographics
rtk proxy "C:/Program Files/Unity/unity-cli.exe" test Tests~/Unity --mode PlayMode --editor-version 6000.4.8f1 --output Tools~/Ecs/TestResults/PlayMode.xml -- -nographics
```

`Tests~/Unity/Packages/manifest.json` 通过相对路径引用本仓库，并设置 `testables: ["com.ejoy.framework"]`。普通消费者的包测试不再要求 Game 的默认程序集样例。完整的默认程序集回归在测试宿主中，批处理入口为 `EjoyFramework.Tests.AssemblyCSharpSnapshotSmoke.RunAssemblyCSharpSnapshotSmoke`。

Git 包的代码生成路径解析到真实包目录，用于读取及检查现有生成物。只有 Local/Embedded 包允许修改；一致的生成文件直接复用，陈旧或缺失的只读输出明确报错。ConfigBlob 在任何副作用前检查全部输出，防止边车及孤儿清理修改不可变包。包来源及路径依据 [Unity PackageInfo API](https://docs.unity3d.com/ScriptReference/PackageManager.PackageInfo.html)。

## 发布及 Game 切换

完成 Unity 验证后发布 `v2.1.0` 标签并先推送 Framework。再让 Game 使用 `https://github.com/one2world/EjoyFramework.git#v2.1.0`，由 UPM 更新 lock，移除原内嵌源码，验证 Game 后提交并推送 Game。当前尚未创建标签或推送。

本机独立仓库可位于 `EjoyGame/EjoyFramework`；Game 的 `.gitignore` 必须包含 `/EjoyFramework/`，避免将其作为文件或 gitlink 提交。该目录仍有自己的 origin、分支和提交历史。
