using Alife.Framework;
using Alife.Basic;

namespace TestPlugin;

[Plugin("测试插件", "用于测试插件热重载机制是否正常工作。")]
public class MyTestPlugin : Plugin
{
    public override Task StartAsync(Microsoft.SemanticKernel.Kernel kernel, ChatActivity chatActivity)
    {
        Console.WriteLine("[测试插件] StartAsync 被调用，插件已加载！版本 2");
        return Task.CompletedTask;
    }

    public override Task DestroyAsync()
    {
        Console.WriteLine("[测试插件] DestroyAsync 被调用！");
        return Task.CompletedTask;
    }
}
