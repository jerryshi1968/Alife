当前项目正在积极开发中，目前已完成大部分功能上的开发，已进入到调优检错阶段，期待未来正式发布。

---

# Alife - 创造赛博生命

![Alife Logo](https://img.shields.io/badge/Alife-AI_Assistant-blue?style=for-the-badge)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)
![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)

Alife 是一个功能丰富、高扩展性的 AI桌宠 框架。它不是一个简单的对话机器人，而是一个支持多模态，主动陪伴，永久记忆，真实的赛博生命。

---

## ✨ 软件特色 (Software Features)

- **基于.Net/C#生态开发**：在python,nodejs横向的时代中，难得的C#项目，对大型项目开发友好，Unity开发经验可迁移。
- **极高扩展性的插件环境**：插件在设计之初就有着对整个业务流的全量控制，自定义界面的能力，实际上所有的内置功能也均为插件实现。
- **功能高度自包含自动化**：内置功能基本都优先采用无依赖的自实现，本地模型，以及小技巧，没有第三方依赖，不要key，可控且开箱即用。
- **纯原生文本的函数调用**：有意默认采用非标准的函数调用，因此对llm没有任何特殊要求，调用过程透明可控，词元开销小，额外支持流式多级调用。
- **稳定持久的自动化记忆**：不使用不可靠的AI自主记忆存储，改为基于类似虚拟页表的一套自动化记忆压缩系统，实现让对话经历永驻上下文的效果。
- **节省词元降低开销交互**：对话上下文有意复用会话分区维护保持稳定，提示词也确保是实践过的有效文本，再配合自实现的函数调用，词元开销非常小。
- **模块化白盒化软件结构**：功能高度模块化，实现简单清晰，很容易就能将单个功能拆离复用。运行信息白盒化，对话和上下文均配专用UI完整显示。

---

## 🌟 核心功能 (Core Features)

- 🎭 Live2D桌宠：内置角色“真央”的live2D，可交互会表演能运动，告别枯燥的对话框。
- 👁️ 深度视觉：拍照不怕没人分享，还会没事偷偷看主人，陪你游戏陪你吐槽。
- 🎙️ 语音对话：放下键盘，直接说话，白嫖的edge加流式合成，实现高品质实时语音。
- 🧠 长期记忆：稳定强大的记忆系统，超大虚拟上下文，所有记录均可溯源搜索，确保记住生活中的点滴。
- 📱 平台通讯：额外支持QQ等多种通讯平台，出门了也能联系在家的她，还能没事带去见见群友。
- 🤖 自主活动：闲时会自娱自乐，有自己爱好和生活，会主动的找你玩耍分享见闻，就像真实的生命那样。
- 🌐 网上冲浪：拥有一个属于自己的真实浏览器，能够自主上网学习娱乐，让知识不再停滞，每天都有新话题。
- 💻 脚本执行：能借助python在本地执行各种任务，唱歌绘画，办公辅助，除开对话同时也是一个实用的助手。
- 🔗 多开互联：允许同时运行多个角色并可相互交流，借此构建一个完整的赛博世界，让他们也有自己的社交圈子。
- ️️🛠️ 扩展能力：支持自定义插件，以及接入 MCP、Skills 功能，通过标准化的AI生态，自由方便的扩展功能。

---

## 🚀 快速开始 (Quick Start)

1. **下载软件**：前往仓库右侧的 [Releases](https://github.com/bdffzi/Alife/releases) 页面，下载最新的软件压缩包（zip）。
2. **环境启动**：解压压缩包后，双击运行 `点我启动.cmd`。该引导程序会自动全自动检测并配置所需的运行环境，真正实现开箱即用。
3. **创建角色**：进入主界面后，点击左上角的 **小火箭图标** 🚀，选择 **立即创建**。在配置完 LLM（如 DeepSeek、OpenAI
   等）相关参数后，即可完成角色的创建。
4. **开启陪伴**：在角色管理页面点击 **激活** 按钮，你的赛博生命便正式苏醒，开始陪伴你的桌面生活。

---

## 🏗️ 开发信息 (Development Information)

*注：由于个人精力有限，故在Demo、Test、UI部分大量使用的AI编程，所以这些子项目的实现很可能存在冗余，低效的问题，不过好在他们不是核心代码。*

### 📦 基本依赖

- 语言后端：.NET 9 + Python 3.12
- 前端界面：WPF + Blazor Hybrid + AntDesign Blazor
- 业务底座：Semantic Kernel

### 🏛️ 层次架构

1. **基础设施层 (`Alife.Basic`)**：封装底层OS能力，统一软件环境变量。
2. **功能模块层 (`Alife.Function`)**：针对各种特种功能的，无依赖模块化实现。
3. **核心框架层 (`Alife.Framework`)**：制定标准业务处理单元，实现基本功能系统，确定插件框架结构。
4. **业务实现层 (`Alife.Implement`)**：实现或接入功能模块层的功能，以插件的方式将其组装为内置业务功能。
5. **用户交互层 (`Alife`)**：接入UI界面，让用户得以通过图形化的界面使用 Alife 框架能力。

### 🧩 功能实现

本项目的所有内置功能均以插件形式实现，逻辑清晰且易于扩展。以下是目前已实现的主要插件及其原理简述：

#### 核心底座 (Core Services)

- **函数调用 (`FunctionService`)**：基于 XML 的流式解析与执行引擎。通过 `XmlStreamExecutor` 实现高实时的工具调用，支持多级流式嵌套，让
  AI 能够“边想边做”。
- **持久记忆 (`MemoryService`)**：分层记忆管理系统。利用向量检索与 LLM 自动摘要技术，将对话历史自动压缩并归档至 L0-LMax
  不同层级，确保长期记忆的可溯源与永久保留。
- **对话能力 (`ChatService`)**：OpenAI 协议适配层。专门针对 DeepSeek 等模型优化了思考过程（Thinking）的字段捕获，并强制 HTTP
  1.1 以确保长流式输出的稳定性。
- **消息加工 (`MessageProcessService`)**：统一的消息前处理过滤器。负责向 AI 动态注入当前时间戳、全局提示词及响应格式约束，确保输出风格的一致性。

#### 交互插件 (Interactive Plugins)

- **网上冲浪 (`SurfingService`)**：利用 WebView2 封装的浏览器引擎。赋予 AI 真实的上网能力，支持通过观察 DOM 结构（Observe）和执行
  JS 脚本（RunJS）来像真人一样操控网页。
- **视觉感知 (`VisionService`)**：集成多模态视觉模型（VLM）。支持屏幕截图分析与本地/网络图片理解，让 AI 能够“看见”主人的桌面和分享的瞬间。
- **语音对话 (`SpeechService`)**：本地离线 STT + 在线 Edge-TTS 方案。具备智能噪声抑制与双工切换逻辑，支持 AI
  在说话时自动暂停识别，实现低延迟的语音交互。
- **桌宠交互 (`DeskPetService`)**：Live2D 角色驱动系统。将 AI 的意图转化为表情、动作和气泡字幕，并支持根据分辨率自动适配屏幕位置。
- **脚本执行 (`PythonService`)**：原生 Python 运行环境。允许 AI 动态生成并执行 Python 代码，支持 `pip` 自动安装依赖，是 AI
  处理复杂任务、绘图及办公自动化的“瑞士军刀”。
- **主动事件 (`EventService`)**：基于时间轮询的自我意识激发系统。定时向 AI 投喂“系统报点”，驱动其在空闲时产生好奇心、发起话题或进行自主学习。
- **世界背景 (`VirtualWorldService`)**：多角色社交协议。模拟了一个具有物理法则和物价体系的虚拟世界，支持不同 AI
  角色间跨进程的消息通讯（Call）与物资交换（Give）。

#### 扩展增强 (Extensions)

- **Skill 工具 (`SkillService`)**：渐进式技能加载系统。通过按需读取 `SKILL.md` 手册，在不污染主 Prompt 的前提下，引导 AI
  完成特定领域的复杂任务。
- **MCP 服务 (`McpService`)**：标准模型上下文协议（Model Context Protocol）客户端。支持动态接入业界标准的外部工具生态，实现能力的无限扩展。
- **QQ 聊天 (`QChatService`)**：接入 OneBot v11 协议。实现 QQ 私聊与群聊的收发，具备群消息去抖缓冲机制与表情包库集成，让 AI
  轻松融入社交圈。

---

## 🧩 插件开发 (Plugin Development)

Alife 采用全插件化架构，内置的所有核心功能（如视觉、语音、浏览器等）均通过插件实现。开发者可以参考 `Sources/Alife.Implement` 项目中的源码，这是最快的学习方式。

### 如何创建一个新插件？

1. 新建一个Dll工程，引用 Alife.Framework 项目。
2. 创建一个类，继承 `InteractivePlugin<T>`，并添加 `[Plugin]` 属性。
3. 打包成Dll


### 开发三步走
1. **创建插件**：。
2. **定义功能 (可选)**：若需 AI 调用，为方法标注 `[XmlFunction]`。若只需注入 AI 背景知识，可在 `AwakeAsync` 中调用 `Prompt("指令")`。
3. **注册能力**：在 `AwakeAsync` 生命周期中调用 `functionService.RegisterHandler(this)`，框架将自动识别标记的方法。

### 代码示例 (C#)
```csharp
[Plugin("我的助手", "示例插件")]
// 推荐：通过构造函数直接注入所需服务（如 FunctionService）
public class MyPlugin(FunctionService functionService) : InteractivePlugin<MyPlugin> 
{
    public override async Task AwakeAsync(AwakeContext context)
    {
        // 注册插件能力（如果定义了 XmlFunction）
        functionService.RegisterHandler(this);

        // 可选：向 AI 注入关于此插件的使用“说明书”
        Prompt("当你需要...时，可以调用 HelloAlife...");
    }

    [XmlFunction]
    [Description("向用户打招呼")]
    public async Task<string> HelloAlife(string name) => $"你好 {name}！";
}
```

### 部署与调试
编译生成的 DLL 放入 `Plugins` 目录后，系统支持 **热重载**（无需重启程序）。你可以直接查阅内置功能的源码来了解如何访问核心 API 或拦截消息流。

---

## 📄 许可证 (License)

本项目采用 [MIT License](LICENSE) 许可协议。
