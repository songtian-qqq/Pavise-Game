# 源码与验证入口

Pavise 使用 .NET Framework 和 Windows Forms，`build.cmd -b prod` 从 `src/` 递归编译源码，并核验程序集版本、必要实现和嵌入资源。构建不会运行应用入口。

## 模块入口

| 位置 | 职责 |
| --- | --- |
| `Program.cs` | 单实例、启动、退出与运行时编排 |
| `GameMode.cs` | 会话状态、属性与构造；具体方法按职责放在同名 partial 文件中 |
| `GameMode.Session.cs`、`GameMode.Detection.cs` | 扫描循环、会话切换与游戏识别 |
| `GameMode.Background.cs`、`GameMode.Protection.cs` | 后台压制编排、白名单与依赖保护 |
| `GameMode.CpuScheduling.cs`、`GameMode.Recovery.cs` | 线程／核心调度编排及恢复调用方 |
| `GameMode.Automation.cs`、`GameMode.Policies.cs` | 自动动作状态与功能策略编排 |
| `SuppressionCore.cs`、`Tamer.cs` | 后台进程的应用、核验、扫描与恢复 |
| `CriticalThread*`、`GameAffinityJob*` | 关键线程、核心范围及 Job 生命周期 |
| `PowerPlan*`、`ManagedPowerPreset*`、`Timer*` | 对局电源、模板、计时器请求与恢复 |
| `PanelForm.cs` 及 `PanelForm.*.cs` | 窗口状态、页面组合、布局、操作与状态刷新 |
| `PaviseCore.cs` 及 `PaviseCore.Motion.cs` | 核心图形、缓存图层与动画 |
| `AutomaticRecoveryBudget.cs`、`ActionFailureStore.cs` | 失败预算、阶段身份与恢复记录 |
| `tests/`、`tools/Run-PublicTests.ps1` | 公开回归夹具与统一运行入口 |

除 `tests/` 和 `tools/` 外，上表中的源码路径均相对于 `src/PaviseApp/`。同名 partial 文件属于同一个类型；这次整理按职责拆分方法，字段初始化与构造顺序保留在原类文件中。

## 基底来源

源码基底从正式发布的 3.1.0.6 程序通过 ILSpy 恢复，之后作为源码继续维护；详见 [来源记录](../src/RECOVERY.md)。恢复过程未保留全部历史注释、局部变量命名及文件组织，当前已整理自动状态分支、核心动画命名和主要会话／界面文件。源码整理与构建成功均不能代替运行行为验证。

当前公开且可运行的测试见 [测试说明](../tests/README.md)。发布程序及原始版本源码以相应发布页和版本标签为准；维护分支可以包含后续源码整理及测试。
