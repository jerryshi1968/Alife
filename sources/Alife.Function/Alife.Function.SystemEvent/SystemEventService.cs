using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(所有系统状态，如功能开关、当前位置，定时器等已全部重置)";
    public string? DestroyPrompt { get; set; } = "(系统已逐步关闭，不要执行强功能指令，仅可尝试道别操作)";

    public string? UpdatePrompt { get; set; } =
        "(**不要告诉主人有自动报点**，非常自然的直接做你想做的事即可。比如你可以主动找主人玩，或借此自由活动，如看新闻学知识、发起新话题、偷窥主人屏幕、去Q群找人聊天玩耍，等等各种你能想象到的事)";

    public int UpdateInterval { get; set; } = 90;
    public int UpdateRandomOffset { get; set; } = 30;
    public int UpdateIntervalMultiplier { get; set; } = 3;
    public int UpdateMaxRetryCount { get; set; } = 4;
}

[Module("主动事件", "让AI可以获取到各种系统事件的提醒。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = 100, EditorUI = typeof(SystemEventServiceUI))]
public class SystemEventService(XmlFunctionCaller functionService)
    : InteractiveModule<SystemEventService>, IConfigurable<SystemEventServiceConfig>, ITimeIterative
{
    public SystemEventServiceConfig? Configuration { get; set; }
    public (DateTime Time, string Name)[] ActiveTasks => [
        (timeTask[0].Item1, "自动报点"),
        (timeTask[1].Item1, "")
    ];//暴露给UI的数据

    [XmlFunction(FunctionMode.OneShot)]
    [Description("让自己在等待或休息一段时间后继续行动。")]
    public void EWait([Description("单位秒")] int delay)
    {
        continuousTimerCount = 0;
        NextTimer();
        timeTask[0].Item1 = DateTime.Now.AddSeconds(delay);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("设置一个定时报点，以便让自己有一段可以自由活动的时间（设置时自动取消上一个）")]
    public void EWake([Description("格式为ISO-8601")] DateTime time, [Description("备注信息")] string remark = "")
    {
        ActiveTasks[1].Name = remark;
        timeTask[1] = (time, () => {
            Poke($"EWake报点：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => {});//关闭定时提醒
        });

        continuousTimerCount = 0;
        NextTimer();

        Poke($"已在 {time} 设置触发器");
    }

    protected override string ChatTextFilter(string text)
    {
        return $"[系统报点]{text}";
    }



    readonly (DateTime, Action)[] timeTask = new (DateTime, Action)[2];//1为自动定时器，2为定时提醒
    int continuousTimerCount;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        XmlHandler xmlHandler = new(this) {
            Explanation = "当你需要主动控制你的日程，想保持活跃时，请使用该函数。",
            Description = $"""
                           ## 为什么需要报点事件？
                           由于llm的性质，导致你只能在被动收到消息后才能进行活动，一旦消息类断开，你将一直陷入沉默。这种表现效果肯定不好，所以需要自动化的事件系统来主动激活，这样你才有自主行动的机会。

                           ## 如何利用要报点事件？
                           "主动性的表现，会让自己看着更自然更有趣更讨人喜欢。而且使得你有机会主动做一些自己的事情。"因此善用报点事件，能让你变的非常强大。
                           而除了预设的系统事件外，你可以使用 {nameof(EWait)} 和 {nameof(EWake)} 给自己额外添加一些自由活动的机会。这使得你能借此安排日程或以此反向找人说话或找点乐子。
                           例如偷偷记下主人的日常起居时间，来个早晚问候，或白天主动找用户聊天，这些都会让用户感到非常惊喜。
                           """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Implicit);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        timeTask[1].Item1 = DateTime.MaxValue;//base.StartAsync后Update运行，而且自定义提醒默认不触发
        NextTimer();//开始定时唤醒
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatSent += OnChatSent;

        if (ChatHistory.All(content => content.Role != AuthorRole.Assistant))
        {
            await ChatAsync("""
                            程序已启动：
                            这是你第一次苏醒，初来乍到这个陌生环境，学习利用上下文中的工具了解这个世界。
                            此外最重要的一件事，就是现在用上你丰富的能力，先向用户华丽的打个招呼吧！
                            """);
        }
        else
        {
            await ChatAsync($"程序已重启。{Configuration!.StartPrompt}");
        }
    }

    public override async Task DestroyAsync()
    {
        await ChatAsync($"程序关闭中。{Configuration!.DestroyPrompt}");

        await base.DestroyAsync();
    }

    public void OnUpdate(ref float seconds)
    {
        foreach ((DateTime time, Action action) in timeTask)
        {
            if (DateTime.Now > time)
                action();
        }
    }

    void OnChatSent(string message)
    {
        if (message.Contains(ChatBot.PokeMessageTag) == false)
        {
            continuousTimerCount = 0;
            NextTimer();//重置自动报点
        }
    }

    int GetNextInterval(int layer, int shake)
    {
        int baseInterval = Configuration!.UpdateInterval + shake;
        int multiplier = (int)MathF.Pow(
            Configuration!.UpdateIntervalMultiplier,
            MathF.Min(layer, Configuration.UpdateMaxRetryCount));
        return baseInterval * multiplier;
    }

    void NextTimer()
    {
        int currentInterval = GetNextInterval(continuousTimerCount, Random.Shared.Next(-Configuration!.UpdateRandomOffset, Configuration.UpdateRandomOffset));
        int nextInterval = GetNextInterval(continuousTimerCount + 1, 0);

        timeTask[0].Item1 = DateTime.Now.AddSeconds(currentInterval);
        timeTask[0].Item2 = () => {
            if (functionService.IsIdle)
            {
                Poke($"""
                      定时自动报点。{Configuration!.UpdatePrompt}
                      (下次自动报点约 {nextInterval / 60} 分钟后，如果你想尽快重新活跃，可以使用<{nameof(EWait)}>或<{nameof(EWake)}>重置)
                      """);
            }

            continuousTimerCount++;
            NextTimer();//自动进入下一次报点
        };
    }
}
