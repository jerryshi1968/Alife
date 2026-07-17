using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Alife.Platform;
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
        ModuleSystem moduleSystem,
        IProgress<(string, float)>? progress = null,
        object[]? appendServices = null)
    {
        //创建服务容器
        ContainerBuilder containerBuilder = new();

        //添加基础服务
        ServiceCollection serviceCollection = new();
        serviceCollection.AddLogging(builder => {
            builder.AddConsole();
            builder.AddFile(Path.Combine(Alife.Platform.AlifePath.RuntimeFolderPath, "Logs"), $"activity_{character.Name}");
            builder.SetMinimumLevel(LogLevel.Information);
        });
        containerBuilder.Populate(serviceCollection);
        //额外添加用户勾选模块（提高优先级）
        Type[] moduleTypes = character.Modules
            .Select(moduleSystem.GetModule)
            .Where(t => t != null).Cast<Type>()
            .ToArray();
        foreach (Type moduleType in moduleTypes)
        {
            var registration = containerBuilder.RegisterType(moduleType)
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
            Type? baseType = moduleType.BaseType;
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
        IContainer moduleContainer = containerBuilder.Build();

        try
        {
            //创建人工智能构建器
            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            //创建上下文构建器
            ChatHistoryAgentThread contextBuilder = new();
            //进行系统初始化
            List<ISystemEvent> allEventModules = new();
            {
                AwakeContext awakeContext = new() {
                    Character = character,
                    Services = (IServiceProvider)moduleContainer,
                    KernelBuilder = kernelBuilder,
                    ContextBuilder = contextBuilder
                };

                //触发系统初始化事件，首先获取支持系统事件的类
                {
                    Type[] allEventModuleTypes = moduleTypes
                        .Where(type => type.IsAssignableTo(typeof(ISystemEvent)))
                        .OrderBy(type => type.GetCustomAttribute<ModuleAttribute>()?.LaunchOrder)
                        .ToArray();
                    for (int index = 0; index < allEventModuleTypes.Length; index++)
                    {
                        Type moduleType = allEventModuleTypes[index];
                        ModuleAttribute moduleAttribute = moduleType.GetCustomAttribute<ModuleAttribute>()!;
                        progress?.Report(($"实例化模块 {moduleAttribute.Name}", (float)index / moduleTypes.Length));
                        try
                        {
                            allEventModules.Add((ISystemEvent)moduleContainer.Resolve(moduleType));
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"实例化模块 {moduleAttribute.Name} 失败", ex);
                        }
                    }
                }

                for (int index = 0; index < allEventModules.Count; index++)
                {
                    ISystemEvent systemEvent = allEventModules[index];
                    ModuleAttribute moduleAttribute = systemEvent.GetType().GetCustomAttribute<ModuleAttribute>()!;
                    progress?.Report(($"初始化模块 {moduleAttribute.Name}", (float)index / allEventModules.Count));

                    try
                    {
                        await systemEvent.AwakeAsync(awakeContext);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"初始化模块 {moduleAttribute.Name} 失败", ex);
                    }
                }
            }

            //创建最核心的对话机器人
            ChatBot chatBot;
            Kernel kernelService;
            {
                if (moduleContainer.TryResolve(out ILanguageModel? languageModel) == false)
                    throw new Exception($"必须确保启用了一个文本模型模块！（系统依赖 {nameof(ILanguageModel)}）");
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
                chatBot = new ChatBot(chatCompletionAgent, contextBuilder, moduleContainer.Resolve<ILogger<ChatBot>>());
            }

            return new(character, kernelService, moduleContainer, chatBot, allEventModules);
        }
        catch
        {
            await moduleContainer.DisposeAsync();
            throw;
        }
    }
}

public partial class ChatActivity(Character character, Kernel kernelService, IContainer moduleService, ChatBot chatBot, List<ISystemEvent> eventModules) : IAsyncDisposable
{
    public Character Character => character;
    public Kernel KernelService => kernelService;
    public IContainer ModuleService => moduleService;
    public ChatBot ChatBot => chatBot;
    public IReadOnlyList<ISystemEvent> EventModules => eventModules;

    public async Task Launch(IProgress<(string, float)>? progress = null)
    {
        for (int index = 0; index < eventModules.Count; index++)
        {
            ISystemEvent systemEvent = eventModules[index];
            progress?.Report(($"启动模块 {systemEvent.GetType().Name}", (float)index / eventModules.Count));
            ModuleAttribute moduleAttribute = systemEvent.GetType().GetCustomAttribute<ModuleAttribute>()!;
            try
            {
                await systemEvent.StartAsync(kernelService, this);
            }
            catch (Exception ex)
            {
                throw new Exception($"启动模块 {moduleAttribute.Name} 失败", ex);
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (ISystemEvent systemEvent in ((IEnumerable<ISystemEvent>)eventModules).Reverse())
                await systemEvent.DestroyAsync();
            await chatBot.DisposeAsync();
            await moduleService.DisposeAsync();
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
