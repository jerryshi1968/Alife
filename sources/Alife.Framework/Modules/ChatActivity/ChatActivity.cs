using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Alife.Framework;

public class ChatActivity : IAsyncDisposable
{
    public static async Task<ChatActivity> Create(
        Character character,
        ConfigurationSystem configurationSystem,
        IProgress<(string, float)>? progress = null,
        object[]? appendServices = null)
    {

        //创建服务容器
        ServiceCollection extensionServiceBuilder = new();
        //添加系统服务
        if (appendServices != null)
        {
            foreach (var appendService in appendServices)
                extensionServiceBuilder.AddSingleton(appendService.GetType(), appendService);
        }
        //添加插件服务
        foreach (Type pluginType in character.Plugins.OrderBy(type => type.GetCustomAttribute<PluginAttribute>()?.LaunchOrder))
            extensionServiceBuilder.AddSingleton(pluginType);
        ServiceProvider extensionService = extensionServiceBuilder.BuildServiceProvider();

        try
        {
            //实例化所有插件
            List<Plugin> allPlugins = new(extensionServiceBuilder.Count);
            for (int index = 0; index < extensionServiceBuilder.Count; index++)
            {
                ServiceDescriptor serviceDescriptor = extensionServiceBuilder[index];
                progress?.Report(($"创建服务 {serviceDescriptor.ServiceType.Name}", (float)index / extensionServiceBuilder.Count));

                await Task.Delay(100);

                object service = extensionService.GetRequiredService(serviceDescriptor.ServiceType);
                if (service is Plugin plugin)
                    allPlugins.Add(plugin);
            }

            //赋值插件配置数据
            foreach (Plugin pluginInstance in allPlugins)
            {
                if (pluginInstance is IConfigurable configurable)
                {
                    Type pluginType = pluginInstance.GetType();
                    object? configData = configurationSystem.GetConfiguration(pluginType, character.StorageKey);
                    configurable.Configuration = configData;
                }
            }

            //创建人工智能构建器
            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            //创建上下文构建器
            ChatHistoryAgentThread contentBuilder = new();
            //构建环境
            Plugin.AwakeContext awakeContext = new() {
                character = character,
                kernelBuilder = kernelBuilder,
                contextBuilder = contentBuilder
            };
            for (int index = 0; index < allPlugins.Count; index++)
            {
                Plugin pluginInstance = allPlugins[index];
                progress?.Report(($"初始化服务 {pluginInstance.GetType().Name}", (float)index / allPlugins.Count));

                await pluginInstance.AwakeAsync(awakeContext);
            }

            //正式开始 AI 代理
            Kernel kernelService = kernelBuilder.Build();
            ChatActivity chatActivity = new(character, contentBuilder, kernelService, extensionService, allPlugins);

            return chatActivity;
        }
        catch
        {
            await extensionService.DisposeAsync();
            throw;
        }
    }

    public ServiceProvider PluginService => pluginService;
    public Kernel KernelService => kernelService;
    public Character Character => character;
    public ChatBot ChatBot => chatBot;
    public IReadOnlyList<Plugin> Plugins => plugins;

    ChatActivity(Character character, ChatHistoryAgentThread context,
        Kernel kernelService, ServiceProvider pluginService, List<Plugin> plugins)
    {
        this.pluginService = pluginService;
        this.kernelService = kernelService;
        this.plugins = plugins;

        //保存原始设定
        this.character = (Character)character.Clone();

        //创建最核心的大语言服务功能
        if (kernelService.Services.GetService<IChatCompletionService>() == null)
            throw new NotSupportedException("必须至少提供一个支持对话能力的模型！");
        ChatCompletionAgent llmAgent = new() {
            Name = character.Name,
            Instructions = character.Prompt,
            InstructionsRole = AuthorRole.System,
            Kernel = kernelService,
            Arguments = new KernelArguments(
                new PromptExecutionSettings()
            ),
        };
        chatBot = new(llmAgent, context);
    }

    public async Task Start(IProgress<(string, float)>? progress = null)
    {
        for (int index = 0; index < plugins.Count; index++)
        {
            Plugin pluginInstance = plugins[index];
            progress?.Report(($"开始服务 {pluginInstance.GetType().Name}", (float)index / plugins.Count));

            await pluginInstance.StartAsync(kernelService, this);
        }
    }
    
    public IEnumerable<string> GetImplicitContext()
    {
        return kernelService.Plugins.GetFunctionsMetadata()
            .Select(metadata => metadata.ToOpenAIFunction().ToFunctionDefinition(true))
            .Select(chatTool => new JObject() {
                ["kind"] = chatTool.Kind.GetHashCode(),
                ["FunctionName"] = chatTool.FunctionName,
                ["FunctionDescription"] = chatTool.FunctionDescription,
                ["FunctionParameters"] = JToken.Parse(Encoding.UTF8.GetString(chatTool.FunctionParameters)),
                ["FunctionSchemaIsStrict"] = chatTool.FunctionSchemaIsStrict
            }).Select(jObject => jObject.ToString());
    }

    readonly Character character;
    readonly ChatBot chatBot;
    readonly Kernel kernelService;
    readonly ServiceProvider pluginService;
    readonly List<Plugin> plugins;

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (Plugin plugin in Plugins.Reverse())
                await plugin.DestroyAsync();
            await chatBot.DisposeAsync();
            await pluginService.DisposeAsync();
            await Task.Delay(1000); //等待一秒让用户反应
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
