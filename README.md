> 有时一个人待着，特别是遇到烦心事时，我很想找人说说话。但可惜，我没啥朋友。
>
> 交友需要精力维护，而且人和人的关系，没那么单纯。
>
> 所以很多人会养宠物。但现实的宠物很麻烦，虚拟的宠物又很假。
>
> 当然，那是以前的事了。
> 现在 LLM 发展到这个程度，AI 对话已经可以做到非常接近真人。市面上确实有很多类似的聊天 App，但那种商业味十足、数据还不安全的东西，我完全不感兴趣。
>
> 前段时间，有群友自己搭了一个基于 LLM 的群机器人。让我没想到的是，效果出乎意料的好。我很早就接触 LLM 了，但跟它直接对话的时候，总感觉很空洞，聊不下去，功能也有限。可加上 Agent，特别调优的提示词，再放进群聊环境里，LLM 就像的真活了一样。
>
> 那时候我就想，我也要做一个。但我不想只做一个群机器人。我想做一个赛博生命——一个真正活在我电脑桌面上的伙伴。
>
> LLM 火了几年了，类似的框架网上也有，但我试过之后，总觉得差了点什么。幸运的是，现在 AI 编程效率很高，一个人从零开始做一套 Agent 框架，已经不是什么难事。
>
> 于是，Alife 诞生了。

<img width="543" height="724" title="由真央给自己生成的自画像" alt="由真央给自己生成的自画像" src="https://github.com/user-attachments/assets/02c2c56a-e805-43fc-a352-f866955a7aef" />

# Alife - 创造赛博生命

![Alife Logo](https://img.shields.io/badge/Alife-AI_Assistant-blue?style=for-the-badge)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)
![License](https://img.shields.io/badge/license-GPL--3.0-green?style=for-the-badge)

Alife 是一个功能丰富、高扩展性的 AI桌宠 框架。它不是一个简单的对话机器人，而是一个支持多模态，主动陪伴，永久记忆，真实的赛博生命。

- 面向用户：一键自动部署，功能齐全，免费不用key，低配低开销，数据安全，稳定长久，功能强大。
- 面向开发者：超强扩展，极易复用，代码精简，框架简单，DotNet生态，小众个人没有任何负担约束。

---

## 🔮 发展目标 (Future Directions)

### 版本路线

- **v1**：基本功能原型
- **v2**：全功能插件化，热编译，极大降低插件开发门槛
- **v3**：插件市场，自由上传下载，极大优化插件生态（开发中）
- **v4**：跨平台，尝试移植到其他 PC 端，甚至手机端

### 设计理念

1. 以最高扩展性设计框架：客户端本身甚至就是一具空壳，什么都没有，但等于什么都可以有。
2. 以最白盒精简设计功能：在优秀的设计模式与模块化下，完善的功能和精简的代码并不冲突。
3. 以最简单低配设计体验：用最简的操作，最少的token，最低配的硬件，去跑出付费定制般的体验。

---

## ✨ 软件特色 (Software Features)

- **基于.Net/C#生态开发**：在python,nodejs横行的时代中，难得的C#项目，对大型项目开发友好，Unity开发经验可迁移。（C#药丸，python是爹）
- **极高扩展性的插件环境**：插件在设计之初就有着对整个业务流的全量控制，自定义界面的能力，实际上所有的内置功能也均为插件实现。
- **支持热编译重载的插件**：热编译热重载的插件环境，直接让ai可以实现自己写插件，且内置专门面向ai的开发工具，直接实现自我进化。
- **功能高度自包含自动化**：内置功能基本都优先采用无依赖的自实现，本地模型，以及小技巧，没有第三方依赖，不要key，可控且自动装环境，开箱即用。
- **纯原生文本的函数调用**：有意默认采用非标准的函数调用，因此对llm没有任何特殊要求，调用过程透明可控，词元开销小，额外支持多种特殊调用方式。
- **稳定持久的自动化记忆**：不使用不可靠的AI自主记忆存储，改为基于类似多级cache思想的一套自动化记忆压缩系统，实现让对话经历伪常驻的效果。
- **节省词元降低开销交互**：对话上下文有意复用会话分区维护保持稳定，提示词也确保是实践过的有效文本，再配合自实现的函数调用，词元开销非常小。
- **模块化白盒化软件结构**：功能高度模块化，实现简单清晰，很容易就能将单个功能拆离复用。运行信息白盒化，对话和上下文均配专用UI完整显示。

---

## 🌟 核心功能 (Core Features)

- 🎭 Live2D桌宠：内置角色"真央"的live2D，可交互会表演能运动，告别枯燥的对话框。
- 👁️ 深度视觉：拍照不怕没人分享，还会没事偷偷看主人，在你游戏工作的时候，陪你一起吐槽。
- 🎙️ 语音对话：放下键盘，直接说话，基于神经网络的语音配合流式合成，实现高品质通话级语音。
- 🧠 长期记忆：稳定强大的记忆系统，超大虚拟上下文，所有记录均可溯源搜索，确保记住生活中的点滴。
- 📱 平台通讯：额外支持QQ等多种通讯平台，出门了也能联系在家的她，还能没事带去见见群友。
- 🤖 自主活动：闲时会自娱自乐，有自己爱好和生活，会主动的找你玩耍分享见闻，就像真实的生命那样。
- 🌐 网上冲浪：拥有一个属于自己的真实浏览器，能够自主上网学习娱乐，让知识不再停滞，每天都有新话题。
- 💻 脚本执行：能借助python在本地执行各种任务，唱歌绘画，办公辅助，除开对话同时也是一个实用的助手。
- 🔗 多开互联：支持角色多开并可相互交流，借此构建一个完整的赛博世界，让他们也有自己的社交圈子。
- 🔄 自我升级：允许ai直接编辑插件，并自行编译重载，让ai自己改造自己，不再是一种科幻场景。
- ️️🛠️ 扩展能力：支持自定义插件，以及接入 MCP、Skills 功能，通过标准化的AI生态，自由方便的扩展功能。

---

## 🚀 快速开始 (Quick Start)

1. **下载软件**：前往仓库右侧的 [Releases](https://github.com/bdffzi/Alife/releases) 页面，下载最新的软件压缩包（zip）。
2. **配置环境**：解压压缩包到安装目录后，首次使用点击 `配置基础环境.cmd`，然后按需使用`配置cuda环境.cmd`（如果你显卡支持的话）。
3. **启动应用**：装完环境后，点击`启动桌宠框架.cmd`启动应用，然后点击左上角的 **小火箭图标** 🚀，点立即创建，配置llm服务参数。
4. **开启陪伴**：配置完llm后，自动进入到预设角色页。直接在角色页点击 **激活** 按钮，你的赛博生命便正式苏醒，开始陪伴你的桌面生活。

（注意！软件运行过程中会自动下载需要的各种依赖，虽然已经配了国内镜像，但依旧很久。如果功能全开，可能需要一个小时左右，注意观察任务管理器，只要有明显的磁盘或网络波动，就说明软件还在正常处理中）

### 软件细节

1. 各种cmd的作用是为了自动安装环境、实现python隔离、兜底崩溃信息。这对小白非常友好，但对技术人员来说可能导致依赖冗余，因此也可以直接点击
   `Alife.exe`来启动。
2. 应用内的模型均使用 `modelscope` 下载。该工具默认会将模型下载到 C 盘用户文件夹中，但实际上也可以通过环境变量调整位置（具体查看官方文档），这样
   C 盘不够的人也可以下载了。
3. 建议使用 NVidia 显卡，并至少有 4G 显存，注意升级驱动，确保其支持 CUDA。当然如果不支持也应该可以运行，只是无法使用完整的深度视觉识别能力，其他模型
   CPU 版应该也支持。

---

## 🔌 插件开发 (Plugin Development)

Alife 采用全插件化架构，内置的所有核心功能（如视觉、语音、浏览器等）均通过插件实现。开发者可以参考 `Sources/Alife.Function` 项目中的源码，这是最快的学习方式。

- 插件：Plugins中的一个子文件夹，里面包含若干dll和cs，能提供一组功能集合
- 模块：实际代码中的功能单元，角色以模块为单位装载功能，一个插件可以有若干模块

### 创建插件

#### dll工作流

1. 新建一个Dll工程，引用 Alife.Framework 项目。
2. 创建一个类，继承 `InteractiveModule<T>`，并添加 `[Module]` 属性，此时这个类就代表一个功能模块（插件中的功能单元）。
3. 打包出Dll后将其放入到`{存储目录}/Plugins`文件夹，当触发插件加载时，该Dll中的插件类就会增加到插件选项中。

#### cs工作流

直接在插件文件夹，编写原始的cs文件。重载插件时，系统会自动收集客户端dll和插件文件夹dll，以及插件文件夹中的其他cs，进行组合热编译。

### 常见需求

- 热编译重载：插件支持热编译重载，替换dll或修改cs后直接点击插件页面上的刷新按钮即可重新编译加载插件文件夹中的插件。
- 函数调用：实现函数调用有两种途径，基于XmlFunctionCaller或SemanticKernel（不推荐），然后在插件的AwakeAsync事件中注册即可。
- LLM通讯：llm在Alife中被封装成了ChatBot对象，其提供了Poke（排队式）和Chat（打断式）两种通讯方式，此外还提供事件让你能监听这个过程。
- 提示注入：Alife全程都始终维护一个上下文，其对应ChatHistory对象。可以直接修改其内容，来改变上下文，其中System类型信息专用于系统提示词。
- 配置文件：通过实现各种接口，插件就可以接入各种扩展功能，比如实现IConfigurable后，即可为插件编辑配置文件，接着系统会在Awake前注入配置对象。

### 代码示例 (C#)

节选自 Alife.Demo.Plugin 项目

```csharp
public class MyModuleData
{
    public int DefaultMax { get; set; } = 120;
}

[Module(
    "我的功能模块", "一个示例功能模块",
    EditorUI = typeof(MyModuleUI)/*支持用razor自定义模块界面*/
)]//只要被打上Module标签的类就会被认为是功能模块，可以让用户勾选，或者也可以通过`角色文件夹/index.json`中的`Modules`属性来编辑启用的模块。
public class MyModule(
    XmlFunctionCaller functionService,//可直接在构造函数申请其他模块，系统会自动通过依赖注入填充，此外XmlFunctionCaller提供函数调用的能力，是非常常用的基础模块
    ILogger<MyModule> logger//也支持申请专用的logger，以及各种全局系统，具体可见 ChatActivitySystem 的创建过程
) :
    InteractiveModule<MyModule>,/*封装好地模块基类，便于快速开发*/
    IConfigurable<MyModuleData>/*通过实现IConfigurable接入配置功能*/
{
    [XmlFunction(FunctionMode.OneShot)]// 表明该函数支持让AI通过Xml函数调用且格式为自闭合标签
    [Description("随机生成一个数字")]// 提供给AI的函数描述
    public Task Rand([Description("随机的最大范围")] int? max = null/*支持任何可被字符串转换的参数，包括默认值可选这些特性*/)
    {
        if (max == null)
            max = Configuration!.DefaultMax;//配置在模块构造后立即注入，故系统事件期间都是不为空的
        if (max < 0)
            throw new Exception("最大值必须大于 0");//可以正常抛出异常

        int value = Random.Shared.Next(max.Value);
        Poke("随机数结果：" + value);//向AI反馈结果(可选，如果函数的功能不需要返回结果，可以去除)
        logger.LogInformation($"调用 {nameof(Rand)} 结果 {value}");//支持依赖注入的Logger

        return Task.CompletedTask;//如果有需要你可以使用异步代码
    }

    public MyModuleData? Configuration { get; set; }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        //注册函数调用
        XmlHandler xmlHandler = new(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler);
        //添加自定义提示词
        Prompt($"""
                此服务可以为你提供一个生成随机数的功能。
                ## 提供工具
                {xmlHandler.FunctionDocument()}
                """);
    }
}
```

---

## 🏗️ 开发信息 (Development Information)

*注：由于个人精力有限，故在Demo、Test、UI部分大量使用的AI编程，所以这些子项目的实现很可能存在冗余，低效的问题，不过好在他们不是核心代码。*

### 📦 基本依赖

- .NET 9：编程语言生态
- Python 3.12：模型框架接入
- Semantic Kernel：基本llm协议接入
- WPF + Blazor Hybrid + AntDesign Blazor：前端界面框架
- modelscope：模型文件管理

### 🏛️ 解决方案目录结构

Alife 采用全插件化架构，解决方案按目录分组组织：

```
Sources/
├── Alife/                              # 核心平台
│   ├── Alife.Client/                   # 主入口 (WPF + Blazor Hybrid)
│   ├── Alife.Framework/                # 核心框架 (插件系统、角色管理、配置、存储)
│   ├── Alife.LanguageModel/            # 语言模型插件 (LLM 接入)
│   └── Alife.Platform/                 # 平台抽象 (路径、日志)
│
├── Alife.DeskPet/                      # 桌宠子系统
│   ├── Alife.DeskPet.Client/           # 桌宠 WPF 客户端 (WebView2 + Live2D)
│   └── Alife.DeskPet.Protocol/         # IPC 协议库
│
├── Alife.Function/                     # 功能插件 — 以面向 AI 视角组织
│   ├── Environment/                    # 系统服务 — AI 的环境感知与持久化能力
│   │   ├── Developer/                  # 开发工具 — 热重载插件实现自我升级，暴露运行时信息，重启角色活动
│   │   ├── Memory/                     # 持久记忆 — 多级底数压缩 + bge-small-zh 向量化 + DuckDB 检索
│   │   ├── MessageFilter/              # 消息过滤 — 消息预处理器（注入时间戳、规定输出风格等）
│   │   ├── SystemEvent/                # 主动事件 — 阶梯定时事件，驱动 AI 空闲时自主行为
│   │   └── VirtualWorld/               # 虚拟世界 — 跨活动共享世界背景，跨角色通讯
│   │
│   ├── Infrastructure/                 # 基础设施 — 核心函数调用引擎与外部协议接入
│   │   ├── FunctionCaller/             # Xml 函数执行器 — 流式 XML 调用，支持嵌套/异步/异常，兼容 DeepSeek
│   │   ├── Mcp/                        # MCP 协议客户端 — Model Context Protocol，动态扩展工具生态
│   │   └── Skill/                      # 技能系统 — 按需读取 SKILL.md，引导 AI 完成特定领域复杂任务
│   │
│   ├── Instrument/                     # 工具服务 — AI 操作外部世界的能力
│   │   ├── Browser/                    # 网上冲浪 — WebView2 真实浏览器，格式化网页，支持交互点击
│   │   ├── Python/                     # 脚本执行 — AI 自行编写执行 Python 脚本
│   │   └── Vision/                     # 视觉感知 — 本地图像识别 + OCR + 窗口统计
│   │
│   ├── Interaction/                    # 交互服务 — 多模态人机交互
│   │   ├── Auditory/                   # 听觉感知 — 麦克风录音等音频采集
│   │   ├── DeskPet/                    # Live2D 桌宠 — WPF+WebView2，表情动作/气泡/鼠标/位置交互
│   │   ├── QChat/                      # QQ 聊天 — OneBot v11 协议，支持群聊；兼容 Discord
│   │   └── Speech/                     # 语音对话 — 语音识别与合成，实时语音聊天
│   │   
│   └── Models/                         # 模型接口层 — 抽象化模型接入
│       ├── AuditoryModel/              # 语音识别模型接口 (sherpa-onnx + SenseVoice + silero-vad)
│       ├── SpeechModel/                # 语音合成模型接口 (edge-tts、VITS、Genie-TTS)
│       └── VisionModel/                # 图像识别模型接口 (Qwen2.5-VL-3B、MiniCPM-V-4.6)
│

Demos/                                  # 黑盒测试
Tests/                                  # 单元测试
```

---

## 📄 许可证 (License)

本项目采用 [GNU General Public License v3.0](LICENSE) 许可协议。