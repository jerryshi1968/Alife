using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public partial class ChatActivity
{
    public static async Task<ChatActivity> Create(
        Character character,
        ConfigurationSystem configurationSystem,
        PluginSystem pluginSystem,
        IProgress<(string, float)>? progress = null,
        object[]? appendServices = null)
    {
        //创建服务容器
        ContainerBuilder containerBuilder = new();

        //添加基础服务
        ServiceCollection serviceCollection = new();
        serviceCollection.AddLogging(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        containerBuilder.Populate(serviceCollection);
        //额外添加用户勾选插件（提高优先级）
        Type[] pluginTypes = character.Plugins
            .Select(pluginSystem.GetPlugin)
            .Where(t => t != null).Cast<Type>()
            .ToArray();
        foreach (Type pluginType in pluginTypes)
        {
            var registration = containerBuilder.RegisterType(pluginType)
                .AsSelf()
                .AsImplementedInterfaces()
                .SingleInstance()
                .OnActivated(args => {
                    if (args.Instance is IConfigurable configurable)
                    {
                        object? configData = configurationSystem.GetConfiguration(args.Instance.GetType(), character.StorageKey);
                        configurable.Configuration = configData;
                    }
                });
            //同时注册所有非系统抽象基类
            Type? baseType = pluginType.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                registration.As(baseType);
                baseType = baseType.BaseType;
            }
        }
        //添加其他服务
        if (appendServices != null)
        {
            foreach (var appendService in appendServices)
                containerBuilder.RegisterInstance(appendService).As(appendService.GetType());
        }
        IContainer pluginContainer = containerBuilder.Build();

        try
        {
            //创建人工智能构建器
            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            //创建上下文构建器
            ChatHistoryAgentThread contextBuilder = new();
            //进行系统初始化
            List<ISystemEvent> allEventPlugin = new();
            {
                AwakeContext awakeContext = new() {
                    Character = character,
                    Services = (IServiceProvider)pluginContainer,
                    KernelBuilder = kernelBuilder,
                    ContextBuilder = contextBuilder
                };

                //触发系统初始化事件，首先获取支持系统事件的类
                {
                    Type[] allEventPluginTypes = pluginTypes
                        .Where(type => type.IsAssignableTo(typeof(ISystemEvent)))
                        .OrderBy(type => type.GetCustomAttribute<PluginAttribute>()?.LaunchOrder)
                        .ToArray();
                    for (int index = 0; index < allEventPluginTypes.Length; index++)
                    {
                        Type pluginType = allEventPluginTypes[index];
                        progress?.Report(($"创建服务 {pluginType.Name}", (float)index / pluginTypes.Length));
                        allEventPlugin.Add((ISystemEvent)pluginContainer.Resolve(pluginType));
                    }
                }

                for (int index = 0; index < allEventPlugin.Count; index++)
                {
                    ISystemEvent systemEvent = allEventPlugin[index];
                    progress?.Report(($"初始化服务 {systemEvent.GetType().Name}", (float)index / allEventPlugin.Count));
                    await systemEvent.AwakeAsync(awakeContext);
                }
            }

            //创建最核心的对话机器人
            ChatBot chatBot;
            Kernel kernelService;
            {
                if (pluginContainer.TryResolve(out ILanguageModel? languageModel) == false)
                    throw new Exception($"必须确保启用了一个文本模型插件！（系统依赖 {nameof(ILanguageModel)}）");
                languageModel.RegisterChatCompletion(kernelBuilder);
                kernelService = kernelBuilder.Build();
                ChatCompletionAgent chatCompletionAgent = new() {
                    Name = character.Name,
                    Instructions =
                        $"名称：{character.Name}\n生日：{character.Birthday}\n简介：{character.Description}\n设定：\n{character.Prompt}",
                    InstructionsRole = AuthorRole.System,
                    Kernel = kernelService,
                    Arguments = new KernelArguments(languageModel.ProvidePromptExecutionSettings()),
                };
                chatBot = new ChatBot(chatCompletionAgent, contextBuilder);
            }


            return new(character, kernelService, pluginContainer, chatBot, allEventPlugin);
        }
        catch
        {
            await pluginContainer.DisposeAsync();
            throw;
        }
    }
}

public partial class ChatActivity(Character character, Kernel kernelService, IContainer pluginService, ChatBot chatBot, List<ISystemEvent> eventPlugins) : IAsyncDisposable
{
    public Character Character => character;
    public Kernel KernelService => kernelService;
    public IContainer PluginService => pluginService;
    public ChatBot ChatBot => chatBot;
    public IReadOnlyList<ISystemEvent> EventPlugins => eventPlugins;

    public async Task Launch(IProgress<(string, float)>? progress = null)
    {
        for (int index = 0; index < eventPlugins.Count; index++)
        {
            ISystemEvent systemEvent = eventPlugins[index];
            progress?.Report(($"开始服务 {systemEvent.GetType().Name}", (float)index / eventPlugins.Count));
            await systemEvent.StartAsync(kernelService, this);
        }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (ISystemEvent systemEvent in ((IEnumerable<ISystemEvent>)eventPlugins).Reverse())
                await systemEvent.DestroyAsync();
            await chatBot.DisposeAsync();
            await pluginService.DisposeAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public IEnumerable<string> GetImplicitFunctionContext()
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
}
