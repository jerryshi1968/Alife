using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public class EventServiceData
{
    public string? StartPrompt { get; set; } = "所有系统状态，如功能开关、当前位置，定时器等已全部重置。";
    public string? DestroyPrompt { get; set; }
    public string? UpdatePrompt { get; set; } = "不要告诉主人有自动报点，非常自然的做你想做的事即可。\n比如可以借此自由活动，如看新闻学知识、然后发起话题或继续刚刚的话题、偷窥主人屏幕、去Q群找人聊天玩耍、等等各种你能想象到的事。";

    public string MessagePrompt { get; set; } = "回复保持精简，不要插入旁白，say区域最多用一个！";

    public int UpdateInterval { get; set; } = 90;
    public int UpdateRandomOffset { get; set; } = 30;
}
[Plugin("事件注入", "让AI可以获取到各种系统事件的提醒。", LaunchOrder = 1000, EditorUI = typeof(EventServiceUI))]
public class EventService : InteractivePlugin<EventService>, IConfigurable<EventServiceData>, ITimeIterative
{
    public static bool IsPokeMessage(string message)
    {
        return message.Contains("[系统缓存消息]");
    }
    
    [XmlFunction]
    [Description("设置下次重新开始自动报点的时间。")]
    public void SetTimer(XmlExecutorContext context, [Description("格式为ISO-8601")] DateTime time)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        continuousTimerCount = 0;
        SetTimer(time);

        Poke("自动报点已调整到：" + time);
    }
    [XmlFunction]
    [Description("在指定时间设置一个提醒（设置时自动取消上一个）。")]
    public void SetReminder(XmlExecutorContext context, [Description("格式为ISO-8601")] DateTime time, [Description("备注信息")] string remark)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        reminderName = remark;
        timeTask[1] = (time, () => {
            ChatBot.Poke($"[{nameof(EventService)}] 来自Reminder的自定义唤醒：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => { }); //关闭定时提醒
            reminderName = "定时提醒";
        });

        continuousTimerCount = 0;
        SetTimer(null);

        Poke("定时唤醒已设置到：" + time);
    }

    public EventServiceData? Configuration { get; set; }
    public (DateTime Time, string Name)[] ActiveTasks => [
        (timeTask[0].Item1, "自动报点"),
        (timeTask[1].Item1, reminderName)
    ];

    readonly (DateTime, Action)[] timeTask = new (DateTime, Action)[2]; //1为自动定时器，2为定时提醒
    string reminderName = "定时提醒";
    int continuousTimerCount;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        InterpreterService interpreterService = context.services.GetRequiredService<InterpreterService>();
        XmlHandler xmlHandler = new(this);
        xmlHandler.Description = "此服务让你能够接收到系统事件（如开始、结束、自动报点），同时可以控制这些信息的收发。";
        xmlHandler.Explain =
            """
            设置自动报点和提醒的意义在于可以额外添加一些触发时机，让你在没有实际的消息接收时也能主动做一些自己的事情。
            你可以借此安排日程或以此反向找人说话或找点乐子。要记住，主动性的行为，会让自己看着更自然更有趣更讨人喜欢。
            例如偷偷记下主人的日常起居时间，来个午餐问候，或白天主动找主人聊天，会让主人感觉更惊喜。
            """;
        interpreterService.RegisterHandler(xmlHandler);

        Prompt("""
               【思维模式要求】在你的思考过程（<think>标签内）中，请遵守以下规则：
               1. 禁止使用圆括号包裹内心独白，例如"（心想：……）"或"(内心OS：……)"，所有分析内容直接陈述即可
               2. 禁止以角色第一人称描写内心活动，例如"我心想""我觉得""我暗自"等，请用分析性语言替代
               3. 思考内容应聚焦于剧情走向分析和回复内容规划，不要在思考中进行角色扮演式的内心戏表演
               """);
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        SetTimer(null);
        timeTask[1].Item1 = DateTime.MaxValue;

        await base.StartAsync(kernel, chatActivity);

        ChatBot.PokeSend += OnPokeSend;
        ChatBot.ChatSend += OnChatSend;

        await ChatAsync($"系统报点：程序已重启。\n{Configuration!.StartPrompt}");
    }
    public override async Task DestroyAsync()
    {
        await ChatAsync($"系统报点：程序即将关闭。\n{Configuration!.DestroyPrompt}");

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
    string OnChatSend(string message)
    {
        if (message.Contains($"[{nameof(EventService)}]") == false)
        {
            continuousTimerCount = 0;
            SetTimer(null); //重置自动报点
        }

        return $"[当前时间：{DateTime.Now}]{message}\n({Configuration?.MessagePrompt})";
    }
    string OnPokeSend(string message)
    {
        return $"[系统缓存消息](下方为非用户消息，请思考后再回复)\n{message}";
    }

    void SetTimer(DateTime? time)
    {
        if (time == null)
        {
            int offset = Random.Shared.Next(-Configuration!.UpdateRandomOffset, Configuration.UpdateRandomOffset);
            int timeOffset = (Configuration.UpdateInterval + offset) * (int)MathF.Pow(3, MathF.Min(continuousTimerCount, 5));
            timeTask[0].Item1 = DateTime.Now.AddSeconds(timeOffset);
        }
        else
        {
            timeTask[0].Item1 = time.Value;
        }

        timeTask[0].Item2 = () => {
            Poke($"""
                  系统报点：由Timer触发的自动报点。
                  {Configuration!.UpdatePrompt}
                  """);
            continuousTimerCount++;
            SetTimer(null); //自动进入下一次报点
        };
    }
}
