# Alife AI Memory

## 0. 强制开发准则 (Mandatory Rules)

- **[IMPORTANT]** 每次对话开始前，必须阅读 **`.gemini/开发规范`** 文件夹下的所有文档，确保代码设计符合项目标准。
- 遵循单一职责、开闭原则及依赖倒转原则，特别是处理跨平台抽象时。

## 1. 项目架构概述 (Architecture Overview)

### 目录结构 (Directory Structure)
- **.gemini/**: AI 规则、规范及长期记忆。
- **Sources/**: 源代码 (五层分层架构)。
- **Outputs/**: 统一编译输出，包含 DLL 及必要资源。
- **Tests/**: 单元测试与集成测试。
- **Demos/**: 演示工程。
- **Launch.cmd**: 引导程序，处理环境初始化。

### 技术栈 (Tech Stack)
- **Runtime**: .NET 9.0 (Stable) - *注：由于 .NET 10 Preview 存在 Razor SDK 编译 Bug，已执行全局降级。*
- **UI**: WPF + Blazor Hybrid (Ant Design Blazor).
- **Architecture**: 插件化分层架构。
- **Alife.Basic (基础设施层)**:
    - 职责：封装底层 OS 能力。
    - 核心：`AlifePlatform` (调度器)、`WindowsPlatform` (Windows 实现)、`WindowsNative` (所有 Win32 P/Invoke 集中地)。
    - 功能：截屏、窗口枚举、进程追踪 (`ProcessTracker`)、路径管理 (`AlifePath`)。
- **Alife.Framework (核心框架层)**:
    - 职责：定义插件协议与交互契约。
    - 核心：`PluginAttribute`、`InteractivePlugin`、`PluginUIBase`。
- **Alife.Function (功能模块层)**:
    - 职责：特定领域的重型能力（AI 模型推理、桌宠行为逻辑）。
    - 模块：`Vision` (InternVL)、`DeskPet` (Live2D 交互、`IpcCommand`)。
- **Alife.Implement (业务实现层)**:
    - 职责：组装能力为 AI 可用的工具服务。
    - 核心：`VisionService`、`ChatService` 等。
- **Alife (展现层)**:
    - 职责：主程序外壳，使用 WPF + WebView2 (Blazor Hybrid) 渲染。

## 2. 最近重构进展 (Recent Refactorings)

- **环境初始化外迁**: 将 Python 环境检测与初始化逻辑从 C# 代码移除，迁移至根目录的 `Launch.cmd`。
- **平台代码隔离**: 
    - 实现了 `AlifePlatform` 抽象层。
    - 成功将所有 Windows 原生调用 (P/Invoke) 集中到 `WindowsNative.cs`。
    - 业务逻辑不再直接耦合 Windows API。
- **依赖优化**: 清理了 `Alife.Implement` 中冗余的 `System.Drawing.Common` 引用，统一由 `Basic` 层处理。

## 3. 未来计划 (Future Plans)

- **[ ] 跨平台准备 (TFM 切换)**:
    - 将 `Alife.Basic` 的 TargetFramework 从 `net10.0-windows` 切换为 `net10.0`，并移除 `UseWPF`。
- **[ ] 跨平台 UI 探索 (Avalonia)**:
    - 计划引入 **Avalonia UI** 作为跨平台外壳。
    - 利用 **Avalonia + Blazor Hybrid** 模式保留现有 Razor 组件。
- **[ ] 移动端适配 (Android)**:
    - 探索将 `DeskPet` 以悬浮窗形式移植到安卓。
    - 需要在 `Alife.Basic` 中实现 `AndroidPlatform`。
- **[ ] 架构持续解耦**:
    - 逐步用 `ImageSharp` 替换 `System.Drawing.Common` 以消除 GDI+ 依赖。
    - 在 `AlifePlatform.Command` 中增加对 Linux/macOS Shell 的支持。

## 4. 项目里程碑 (Project Milestones)

- [2026-04-28] 解决 Razor SDK 编译冲突: 修复了 `Accessibility.dll` 缺少 `FusionName` 导致的编译崩溃。通过降级至 .NET 9 稳定版工具链并在 `Directory.Build.props` 中使用 `ItemDefinitionGroup` 全局补全元数据解决。
- [2026-04-28] 存储路径无感迁移: 实现了 `AlifePath` 在路径变更时自动搬迁数据的功能，增强了用户体验。
- [2026-04-28] 打包与环境自动化升级: 引入了 `Build_Release.cmd` 解决 WPF Blazor 静态资产绝对路径依赖问题，并在 `Launch.cmd` 中加入了 `.NET 9 Desktop Runtime` 的全自动检测与静默安装逻辑，实现了真正的开箱即用。
- [2026-04-27] 平台能力架构重构: 完成了 `Alife.Basic` 层的解耦，所有 Win32 原生调用已集中管理，实现了业务与平台的初步分离。
- [2026-04-27] 环境管理外迁: 引入 `Launch.cmd` 统一管理 Python 环境初始化。
- [2026-05-06] XML 解析器修复与强化: 修复了 `XmlStreamParser` 中 `\"` 在属性内被截断导致标签无法闭合的严重 bug。优化了 `SurfingService.ExecuteScript` 以支持将 JS 代码置于标签内容（Content）中，极大提高了大段 JS 和包含引号/正则的代码执行成功率。

## 5. 经验教训与技巧 (Lessons Learned & Tips)

- **XML 标签生成最佳实践**: AI 过去在生成 `<execute_script script="...">` 时常因属性值内转义双引号 `\"` 导致 XML 流解析器卡死崩溃。尽管解析器已修复，但最佳实践仍是**将复杂 JavaScript 代码放在 `<execute_script>` 的内容块中**，而不是作为 `script` 属性。
- **MIDI 获取与 MusicMaster 进阶策略**:
    - **先验证再搜索**: 对于不确定的曲名，必须先通过 `websearch` 确认标准名、艺术家及别名，避免因译名差异搜不到。
    - **阶梯搜索**: 优先尝试 GitHub 和 MIDIClouds 的精确定位，若失败应**果断切换到 Google (SurfingService.websearch)** 进行全局搜索，不要死扣在专门的 MIDI 站。
    - **及时止损**: 如果尝试多种关键词仍无果，主动告知用户尝试过的关键词并请求协助（提供链接或文件），严禁在无效结果中循环搜索。
