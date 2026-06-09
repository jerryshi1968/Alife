using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Developer;

[Module("开发者模式", "向 AI 暴露项目和系统信息，并提供工具使其可以自制插件和活动管理。",
    defaultCategory: "Alife 官方/生活环境"
)]
public class DeveloperService(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    ModuleSystem moduleSystem,
    XmlFunctionCaller functionCaller) :
    InteractiveModule<DeveloperService>
{
    [XmlFunction(FunctionMode.OneShot)]
    public void GetDevelopGuide()
    {
        Poke($$$""""
                你所身处的框架叫做`Alife`，你可以通过其源码仓库`https://github.com/BDFFZI/Alife`，了解更多信息。

                ## 框架组成
                - 角色：ai的人设功能配置
                - 活动：将角色激活后，与llm和实际功直接连接的对话实例
                - 插件：一个文件夹或项目，包含了若干cs或dll
                - 模块：角色功能单位，通常写在插件的cs中，一个插件可以提供若干模块

                ## 提供工具
                {{{xmlHandler.FunctionDocument()}}}}

                ## 插件开发指南
                插件文件夹中的每个文件夹代表一个插件，一个插件可以由多个功能模块构成

                ### 开发环境
                1. 框架支持热编译热重载C#代码，来编写插件的功能。因此在插件文件夹直接编写cs代码，即可创建插件，没有任何其他文件名等要求
                2. 插件文件夹在`{{{pluginCopyRoot}}}`，所有已有插件都在里面。同时你新增的插件，也要放到该目录
                3. 角色配置文件是`{{{characterSystem.GetCharacterConfigFile(Character)}}}`，插件带来的模块功能，都需要在其中配置开关

                ### 开发步骤
                1. 翻阅插件文件夹，确定已有插件，并参考其中插件的实现。
                2. 在插件文件夹新增cs脚本，实现插件模块。然后通过 {{{nameof(ReloadModules)}}} 加载。
                3. 重载成功后，检查并修改角色配置文件(json)，将要启用的模块完整类名放到`Modules`数组中（具体参考文件中的其他模块放法）。
                4. 通过 {{{nameof(RestartActivity)}}} 重启对话活动，模块将在重启后生效。

                ### 模块示例代码
                ```csharp
                using System.ComponentModel;
                using Alife.Demo.Module;
                using Alife.Framework;
                using Alife.Function.FunctionCaller;
                using Alife.Function.Interpreter;
                using Microsoft.Extensions.Logging;

                public class MyModuleData
                {
                    public int DefaultMax { get; set; } = 120;
                }

                [Module("我的功能模块", "一个示例功能模块")]//只要被打上Module标签的类就会被认为是功能模块，可以让用户勾选，或者也可以通过`角色文件夹/index.json`中的`Modules`属性来编辑启用的模块。
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

                ### 使用提示
                1. 如果重载模块成功，那说明代码肯定是没问题的，只要模块在模块文件夹中，就一定是能加载到程序中。
                2. 如果重载成功但依然没法使用，通常都是角色配置的问题，你需要确保配置填写无误，确定启用了模块。
                3. 框架本身的功能也基本都是用模块实现的，即存放在插件文件夹中，翻阅学习他们的写法，来实现最佳开发实践。
                """");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void GetProjectPath()
    {
        Poke($$$"""
                存储目录：{{{AlifePath.StorageFolderPath}}}
                应用目录：{{{AppContext.BaseDirectory}}}
                插件目录：{{{pluginCopyRoot}}}
                运行时资源目录：{{{AlifePath.RuntimeFolderPath}}}
                （你可以按需编辑这些路径的文件，来实现特别的需求）
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void GetCharacterConfig([Description("为空表示自己")] string? name = null)
    {
        Character? character;
        if (name == null)
            character = Character;
        else
            character = characterSystem.GetAllCharacters().Find(ch => ch.Name == name);

        if (character == null)
        {
            Poke("角色不存在");
            return;
        }

        string configPath = characterSystem.GetCharacterConfigFile(character);
        Poke($$$"""
                配置文件地址：{{{configPath}}}
                具体内容：
                ```
                {{{File.ReadAllText(configPath)}}}
                ```
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ListCharacters()
    {
        var chars = characterSystem.GetAllCharacters();
        string info = string.Join("\n", chars.Select(c => $"- {c.Name} (存储: {c.StorageKey})"));
        Poke($"所有角色：\n{info}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ListAllModules()
    {
        StringFolder folder = moduleSystem.GetModuleFolder();
        Poke(FormatFolder(folder, 0));

        static string FormatFolder(StringFolder f, int depth)
        {
            string indent = new string(' ', depth * 2);
            string result = "";

            if (depth > 0)
                result += $"{indent}📁 {f.Name}\n";

            foreach (string s in f)
                result += $"{indent}  📦 {s}\n";

            foreach (var sub in f.Folders)
                result += FormatFolder(sub, depth + 1);

            return result;
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ReloadModules()
    {
        ModuleLoadContext context = moduleSystem.CompileModule(pluginCopyRoot);
        context.Unload();
        SyncModulesFromCopy();
        moduleSystem.ReloadModules();
        Poke("模块重载成功！接下来请确认角色配置文件中是否正确添加了模块，然后重启角色活动，以使模块生效。");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void RestartActivity([Description("为空表示自己")] string? charactorName = null)
    {
        Character? character;
        if (charactorName == null)
            character = Character;
        else
        {
            character = characterSystem.GetAllCharacters().Find(ch => ch.Name == charactorName);
            if (character == null)
                throw new Exception("角色不存在，请检查名称是否正确！");
        }

        chatActivitySystem.Deactivate(character).ContinueWith(async _ => {
            chatActivitySystem.ActivationFailed += OnActivationFailed;
            await chatActivitySystem.Activate(character);
            chatActivitySystem.ActivationFailed -= OnActivationFailed;

            Exception? ex = null;

            void OnActivationFailed(Character arg1, Exception arg2)
            {
                ex = arg2;
            }

            if (character != Character)
                ChatBot.Poke($"{charactorName} 激活 {(ex == null ? "成功" : "失败\n" + ex)}");
        });
    }


    string pluginRoot = null!;
    string pluginCopyRoot = null!;
    XmlHandler xmlHandler = null!;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        pluginRoot = moduleSystem.GetModuleFolderRoot();
        pluginCopyRoot = Path.Combine(AlifePath.TempFolderPath, "PluginsRuntime");
        CopyModuleFolder(pluginRoot, pluginCopyRoot);

        xmlHandler = new XmlHandler(this);
        functionCaller.RegisterHandlerWithoutDocument(xmlHandler);

        Prompt($"""
                此服务让你拥有对整个框架本身的控制能力。你可以借此查询各种系统信息，管理角色活动，甚至创建编辑插件模块，从而实现自我升级（很危险，建议与用户配合）。
                当你被用户要求进行功能开发调整，或需要了解这个框架的信息时，请使用该服务，通过调用<{nameof(GetDevelopGuide)}/>，获取完整框架环境信息。
                """);
    }

    void CopyModuleFolder(string source, string destination)
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

    void SyncModulesFromCopy()
    {
        foreach (string dir in Directory.GetDirectories(pluginRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(dir) == "BaseDirectory") continue;
            Directory.Delete(dir, true);
        }

        foreach (string file in Directory.GetFiles(pluginRoot, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        foreach (string dir in Directory.GetDirectories(pluginCopyRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(dir) == "BaseDirectory") continue;
            CopyModuleFolder(dir, Path.Combine(pluginRoot, Path.GetFileName(dir)));
        }

        foreach (string file in Directory.GetFiles(pluginCopyRoot, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(pluginRoot, Path.GetFileName(file)), true);
        }
    }
}
