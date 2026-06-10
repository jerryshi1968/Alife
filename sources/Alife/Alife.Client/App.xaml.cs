using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Alife.Framework;
using Alife.Components.Services;
using Microsoft.Extensions.Logging;

namespace Alife;

public partial class App
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

#if DEBUG
        Console.WriteLine(typeof(Function.Memory.MemoryService).Assembly.FullName);
        Console.WriteLine(typeof(Function.MessageFilter.MessageFilterService).Assembly);
        Console.WriteLine(typeof(Function.SystemEvent.SystemEventService).Assembly);
        Console.WriteLine(typeof(Function.VirtualWorld.VirtualWorldService).Assembly);
        Console.WriteLine(typeof(Function.Developer.DeveloperService).Assembly);

        Console.WriteLine(typeof(Function.FunctionCaller.XmlFunctionCaller).Assembly);
        Console.WriteLine(typeof(Function.Mcp.McpService).Assembly);
        Console.WriteLine(typeof(Function.Skill.SkillService).Assembly);

        Console.WriteLine(typeof(Function.Browser.BrowserService).Assembly);
        Console.WriteLine(typeof(Function.Python.PythonService).Assembly);
        Console.WriteLine(typeof(Function.Vision.VisionService).Assembly);

        Console.WriteLine(typeof(Function.Speech.AuditoryService).Assembly);
        Console.WriteLine(typeof(Function.DeskPet.DeskPetService).Assembly);
        Console.WriteLine(typeof(Function.QChat.QChatService).Assembly);
        Console.WriteLine(typeof(Function.Speech.SpeechService).Assembly);

        Console.WriteLine(typeof(Function.Speech.IAuditoryModel).Assembly);
        Console.WriteLine(typeof(Function.Speech.ISpeechModel).Assembly);
        Console.WriteLine(typeof(Function.Vision.IVisionModel).Assembly);
#endif

        ServiceCollection services = new();
        // 基础 Blazor Desktop 支持
        services.AddWpfBlazorWebView();
        services.AddBlazorWebViewDeveloperTools();// 允许 F12
        // UI 库
        services.AddAntDesign();
        // logger 库
        services.AddLogging(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        // Alife.Client 核心业务系统
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<ModuleSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
        // 添加主窗口本身到容器，以便以后注入
        services.AddSingleton<ActivityNotifyService>();
        services.AddSingleton<ChatMessageService>();
        services.AddSingleton<PluginMarketService>();
        services.AddSingleton<MainWindow>();

        ServiceProvider = services.BuildServiceProvider();
        ServiceProvider.GetRequiredService<ChatMessageService>();
        ServiceProvider.GetRequiredService<PluginMarketService>();
        ServiceProvider.GetRequiredService<MainWindow>().Show();
    }
}
