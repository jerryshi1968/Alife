using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Developer;

[Module("开发者模式", "向 AI 暴露项目和系统信息，并提供工具使其可以自制插件和活动管理。",
    defaultCategory: "Alife 官方/生活环境"
)]
public class DeveloperService(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    ModuleSystem moduleSystem,
    XmlFunctionCaller functionCaller,
    ILogger<DeveloperService> logger) :
    InteractiveModule<DeveloperService>
{
    [XmlFunction(FunctionMode.OneShot)]
    public void ListAllCharacters()
    {
        var chars = characterSystem.GetAllCharacters();
        string result = string.Join("\n", chars.Select(character => $"{character.Name}:{(chatActivitySystem.IsActivated(character) ? "活跃中" : "未激活")}"));
        Poke(result);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void ListAllModules()
    {
        StringFolder folder = moduleSystem.GetModuleFolder();
        Poke(FormatFolder(folder, 0));

        static string FormatFolder(StringFolder folder, int depth)
        {
            string indent = new string(' ', depth * 2);
            string result = "";

            if (depth > 0)
                result += $"{indent}+ {folder.Name}\n";

            foreach (string s in folder.Strings)
                result += $"{indent}  - {s}\n";

            foreach (var sub in folder.Folders)
                result += FormatFolder(sub, depth + 1);

            return result;
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ReloadCharacters()
    {
        foreach (Character character in characterSystem.GetAllCharacters())
            characterSystem.LoadCharacter(character);
        Poke("角色配置重载成功");
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void ReloadModules()
    {
        ModuleLoadContext context = moduleSystem.CompileModule(pluginCopyRoot);
        context.Unload();
        SyncModulesFromCopy();
        moduleSystem.ReloadModules();
        Poke("模块重载成功");

        void SyncModulesFromCopy()
        {
            foreach (string directory in Directory.GetDirectories(pluginCopyRoot))
            {
                if (Path.GetFileName(directory) == "BaseDirectory") continue;
                string pluginDirectory = Path.Combine(pluginRoot, Path.GetFileName(directory));
                ReplaceFolder(directory, pluginDirectory);//如果沙盒插件环境有的插件，以沙盒为准
            }

            foreach (string file in Directory.GetFiles(pluginCopyRoot))
                File.Copy(file, Path.Combine(pluginRoot, Path.GetFileName(file)), true);
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void GetCharacterEnabledModule([Description("为空表示自己")] string? name = null)
    {
        Character? character = FindCharacter(name);
        if (character == null)
        {
            Poke("角色不存在");
            return;
        }

        string result = string.Join("\n", character.Modules
            .Select(module => $"{module}:{(moduleSystem
                .GetModule(module) != null ? "有效" : "功能不存在")}"));
        Poke(result);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void RestartActivity([Description("为空表示自己")] string? charactorName = null)
    {
        Character? character = FindCharacter(charactorName);
        if (character == null)
        {
            Poke("角色不存在");
            return;
        }
        if (ChatHistory
            .Where(content => content.Role == AuthorRole.Assistant)
            .TakeLast(4).SkipLast(1).Any(content => content.Content != null && content.Content.Contains(nameof(RestartActivity), StringComparison.OrdinalIgnoreCase)))
        {
            Poke("不允许短时间多次重启，请等待几次后再试（你是否误解了重启含义和重启通知？）");
            return;
        }

        async void ActivateCharacter()
        {
            try
            {
                chatActivitySystem.ActivationFailed += OnActivationFailed;
                await chatActivitySystem.Activate(character);
                chatActivitySystem.ActivationFailed -= OnActivationFailed;

                Exception? ex = null;

                void OnActivationFailed(Character arg1, Exception arg2)
                {
                    ex = arg2;
                }

                //将结果传递给自己
                ChatActivity? chatActivity = chatActivitySystem.GetChatActivity(Character);
                if (chatActivity != null)
                    chatActivity.ChatBot.Poke($"{character.Name}激活{(ex == null ? "成功" : "失败\n" + ex)}");
            }
            catch (Exception e)
            {
                logger.LogError(e, "激活角色失败");
            }
        }

        if (chatActivitySystem.IsActivated(character))
            chatActivitySystem.Deactivate(character).ContinueWith(_ => ActivateCharacter());
        else
            ActivateCharacter();
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ReadDemoModuleCode()
    {
        Poke("""
             using System;
             using System.ComponentModel;
             using System.Threading.Tasks;
             using Alife.Framework;
             using Alife.Function.FunctionCaller;
             using Alife.Function.Interpreter;
             using Microsoft.Extensions.Logging;

             public class MyModuleData
             {
                 [Description("随机的最大范围")]//默认的配置UI可以识别并向用户显示Description内容
                 public int DefaultMax { get; set; } = 120;
             }

             [Module(
                 "我的功能模块", "一个示例功能模块"
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
                     //备注：Poke最终是通过ChatBot来与AI交互的，这是一个非常重要的类，如果要从根源上处理交互和上下文，就去获取ChatBot对象

                     logger.LogInformation($"调用 {nameof(Rand)} 结果 {value}");//支持依赖注入的Logger

                     return Task.CompletedTask;//如果有需要你可以使用异步代码
                 }

                 public MyModuleData? Configuration { get; set; }

                 public override async Task AwakeAsync(AwakeContext context)
                 {
                     await base.AwakeAsync(context);

                     //将模块注册为xml处理器，以支持文档化和xml调用
                     XmlHandler xmlHandler = new(this) {
                         Description = "此服务可以为你提供一个生成随机数的功能。",
                     };
                     functionService.RegisterHandler(xmlHandler);
                     //备注：xml函数调用还支持多次注册方式和额外功能，需要复杂的函数调用和注册机制，请查阅Alife.Function.FunctionCaller插件
                 }
             }
             """);
    }

    string pluginRoot = null!;
    string pluginCopyRoot = null!;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        pluginRoot = moduleSystem.GetModuleFolderRoot();
        pluginCopyRoot = Path.Combine(AlifePath.TempFolderPath, "PluginsRuntime");
        ReplaceFolder(pluginRoot, pluginCopyRoot);

        XmlHandler xmlHandler = new(this);
        functionCaller.RegisterHandlerWithoutDocument(xmlHandler);
        Prompt($$"""
                 Alife是一款AIAgent，源码在`https://github.com/BDFFZI/Alife`，需要深入研究请下源码

                 框架结构
                 Character：存储ai人设功能配置
                 ChatBot：与llm实际通讯
                 ChatActivity：创建会话活动，激活插件，连接llm
                 Module：功能单位，可被热更加载

                 插件市场
                 在`https://github.com/BDFFZI/Alife.PluginMarket`获取第三方插件或分享插件。插件是独立功能，可以让你引用nuget、pip包，具体看仓库说明

                 关键插件
                 FunctionCaller：实现函数调用
                 MCPService：实现接入MCP协议
                 SkillService：实现接入Skill协议

                 示例插件，建议参考他们写法
                 Speech.VITS：其通过通用接口扩展模型选项，利用管道通讯调用python运行本地模型，并实现自动下载依赖环境
                 Memory：其通过ChatBot事件和llm申请机制实现交互拦截，并通过直接读写对话上下文实现记忆压缩，同时实现关键字检测发送额外提示词功能。

                 环境目录
                 应用目录：{{AppContext.BaseDirectory}}
                 环境目录：{{AlifePath.RuntimeFolderPath}}
                 存储目录：{{AlifePath.StorageFolderPath}}
                 插件目录：{{pluginCopyRoot}}
                 角色目录：{存储目录}/Character/{{Character.Name}}
                 模块配置：{存储目录}/Configuration
                 角色模块配置（优先级更高）：{角色目录}/Configuration

                 开发工具
                 {{xmlHandler.FunctionDocument()}}

                 插件开发方法
                 1. 在插件目录新增cs脚本，实现模块。然后通过{{nameof(ReloadModules)}}重载
                 2. 成功后，编辑`{角色目录}/index.json`，将新增模块类名放`Modules`数组中
                 3. 编辑后用{{nameof(ReloadCharacters)}}重载，并用{{nameof(GetCharacterEnabledModule)}}验证模块启用
                 4. 如果模块用到配置功能，可编辑`{模块配置}/{模块类名}.json`修改配置
                 5. 最后用{{nameof(RestartActivity)}}重启自己，模块要在重启后才会生效

                 模块代码示例
                 调用<{{nameof(ReadDemoModuleCode)}}>获取

                 注意事项
                 1. 若开发遇问题，优先尝试参考同目录其他文件写法
                 2. 开发时不要互动，以最快速度专心执行开发任务
                 3. 不要听无关通知，专心开发直到需求完成
                 """);
    }

    Character? FindCharacter(string? name)
    {
        Character? character = string.IsNullOrEmpty(name)
            ? Character
            : characterSystem.GetAllCharacters().Find(ch => ch.Name == name);
        return character;
    }

    void ReplaceFolder(string source, string destination)
    {
        if (Directory.Exists(destination))
            Directory.Delete(destination, true);
        Directory.CreateDirectory(destination);

        foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relativePath = dirPath[(source.Length + 1)..];
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        foreach (string filePath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            string relativePath = filePath[(source.Length + 1)..];
            File.Copy(filePath, Path.Combine(destination, relativePath), true);
        }
    }
}
