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

[Plugin("开发者服务", "全面暴露项目结构和系统服务信息，让 AI 能完全了解和掌控整个软件。",
defaultCategory: "Alife 官方/系统服务"
)]
[Description(@"你身处一个名 Alife 的代理框架中（源码仓库：https://github.com/BDFFZI/Alife）。现在你拥有对其运行环境的完全控制。
你可以通过各项查询函数了解其运行时信息，甚至编辑并热重载插件，实现自我升级（很危险，建议与用户配合）")]
public class DeveloperService(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    PluginSystem pluginSystem,
    ConfigurationSystem configurationSystem,
    XmlFunctionCaller functionCaller) :
    InteractivePlugin<DeveloperService>
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取插件开发指南")]
    public void GetPluginGuide()
    {
        Poke($$$"""
                # 插件开发指南

                ## 示例代码
                ```csharp
                using System.ComponentModel;
                using Alife.Framework;
                using Alife.Function.FunctionCaller;

                [Plugin("插件名", "插件描述")]
                public class MyPlugin(XmlFunctionCaller functionService) : InteractivePlugin<MyPlugin>
                {
                    [XmlFunction(FunctionMode.OneShot)]
                    [Description("函数描述")]
                    public Task DoSomething([Description("参数描述")] string input)
                    {
                        // 你的逻辑
                        Poke("结果：" + input);
                        return Task.CompletedTask;
                    }

                    public override async Task AwakeAsync(AwakeContext context)
                    {
                        await base.AwakeAsync(context);
                        functionService.RegisterHandler(this);
                        Prompt("此插件的功能说明...");
                    }
                }
                ```

                - `[Plugin]` 标记插件类
                - 继承 `InteractivePlugin<T>` 获得 `Poke()`、`Prompt()` 等方法
                - 构造函数参数自动依赖注入（其他插件、Logger、系统服务等）
                - `[XmlFunction(FunctionMode.OneShot)]` 标记可调用函数
                - `Poke()` 向 AI 返回结果
                - `AwakeAsync` 中 `RegisterHandler(this)` 注册函数

                ## 开发环境：
                1. 本框架支持热编译热重载C#代码，来编写插件的功能。因此只需在插件文件夹直接编写cs代码，即可创建插件，没有任何其他文件名等要求。
                2. 插件文件夹是`{{{pluginCopyRoot}}}`，所有已有插件以及你新增的插件，都要放到该目录下。
                3. 角色配置文件是`{{{characterSystem.GetCharacterConfigFile(Character)}}}`，插件的开关需要在中设置。

                ## 插件开发步骤
                1. 翻阅插件文件夹，确定已有插件，以及参考其中插件的实现。
                2. 在插件文件夹新增或修改插件的.cs后，通过 ReloadPlugin 重载插件。
                3. 重载成功后，检查并修改角色配置文件，确保其中正确包含了要启用的插件。
                4. 通过 RestartActivity 重启对话活动，插件将在重启后生效。

                ## 使用提示
                1. 如果重载插件成功，那说明代码肯定是没问题的，只要插件在插件文件夹中，就一定是能加载到程序中。
                2. 如果重载成功但依然没法使用，通常都是角色配置的问题，你需要确保配置填写无误，确定启用了插件。

                ## 更多信息
                插件的参考实现在插件根目录中，你可以翻阅其中的现有插件代码来学习。
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取所有已安装插件的列表和分类")]
    public void ListAllPlugins()
    {
        StringFolder folder = pluginSystem.GetPluginFolder();
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
    [Description("尝试编译重载插件")]
    public void ReloadPlugin()
    {
        PluginLoadContext context = pluginSystem.CompilePlugin(pluginCopyRoot);
        context.Unload();
        SyncPluginsFromCopy();
        pluginSystem.ReloadPlugins();
        Poke("插件重载成功！接下来请确认角色配置文件中是否正确添加了插件，然后重启角色活动，以使插件生效。");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("重启角色活动")]
    public void RestartActivity([Description("为空时表示重启自己")] string? charactorName = null)
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

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取项目各种路径地址")]
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
    [Description("获取指定角色的配置信息")]
    public void GetCharacterInfo([Description("角色名，为空时返回当前角色")] string? name = null)
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
        string plugins = string.Join(", ", character.Plugins.Select(pluginSystem.GetPlugin)
            .Where(type => type != null)
            .Cast<Type>()
            .Select(type => type.FullName!));
        Poke($$$"""
                名称: {{{character.Name}}}
                描述: {{{character.Description}}}
                已启用插件（只列出确实已被系统识别到的插件）: {{{plugins}}}
                配置文件地址（你可以修改该文件然后重启活动，来实现角色配置调整，比如借此调整其启用的插件）: {{{configPath}}}
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取指定插件的配置信息")]
    public void GetPluginConfig([Description("插件完整类名")] string pluginTypeName)
    {
        Type? type = pluginSystem.GetPlugin(pluginTypeName);
        if (type == null)
        {
            Poke($"插件 '{pluginTypeName}' 不存在");
            return;
        }

        if (configurationSystem.CanConfiguration(type) == false)
        {
            Poke($"插件 '{pluginTypeName}' 不支持配置");
            return;
        }

        object? config = configurationSystem.GetConfiguration(type, Character.StorageKey);
        string json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        string charConfigPath = Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey, "Configuration", $"{pluginTypeName}.json");
        string globalConfigPath = Path.Combine(AlifePath.StorageFolderPath, "Configuration", $"{pluginTypeName}.json");
        Poke($$$"""
                插件 {{{pluginTypeName}}} 的配置（{{{Character.Name}}} 角色）：

                {{{json}}}

                配置查找顺序：
                1. 角色级配置: {{{charConfigPath}}}
                2. 全局配置: {{{globalConfigPath}}}
                未找到则使用代码中的默认值。
                """);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取项目中的所有角色列表")]
    public void ListCharacters()
    {
        var chars = characterSystem.GetAllCharacters();
        string info = string.Join("\n", chars.Select(c => $"- {c.Name} (存储: {c.StorageKey})"));
        Poke($"所有角色：\n{info}");
    }

    string pluginRoot = null!;
    string pluginCopyRoot = null!;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        pluginRoot = pluginSystem.GetPluginFolderRoot();
        pluginCopyRoot = Path.Combine(AlifePath.TempFolderPath, "PluginsRuntime");
        CopyPluginFolder(pluginRoot, pluginCopyRoot);

        functionCaller.RegisterHandler(this);
    }

    void CopyPluginFolder(string source, string destination)
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

    void SyncPluginsFromCopy()
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
            CopyPluginFolder(dir, Path.Combine(pluginRoot, Path.GetFileName(dir)));
        }

        foreach (string file in Directory.GetFiles(pluginCopyRoot, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(pluginRoot, Path.GetFileName(file)), true);
        }
    }
}
