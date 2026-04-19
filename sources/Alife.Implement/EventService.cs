using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public class EventServiceData
{
    public string? AppendStartPrompt { get; set; }
    public string? AppendDestroyPrompt { get; set; }
    public string? AppendUpdatePrompt { get; set; }
    public int UpdateInterval { get; set; } = 90;
    public int UpdateRandomOffset { get; set; } = 30;
}
[Plugin("系统事件", "让AI可以获取到各种系统事件的提醒。", LaunchOrder = 100)]
[Description("你能够接收到系统事件（如开始、结束、周期报点），并可选的控制这些信息的收发。")]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    [XmlFunction]
    [Description("设置下次重新开始自动唤醒的时间。（你可以借此唤醒自己，从而在短暂间隔后再次做一些想做的事）")]
    public void SetTimer(XmlExecutorContext context, [Description("下次自动唤醒的时间，格式为ISO-8601")] DateTime time)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        continuousTimerCount = 0;
        SetTimer(time);
    }
    [XmlFunction]
    [Description("在指定时间设置一个唤醒（设置时自动取消上一个）。（你可以借此安排自己的日程或以此反向主动找主人对话。要记住，主动性的行为会更让主人喜欢！）")]
    public void SetReminder(XmlExecutorContext context, [Description("触发的时间，格式为ISO-8601")] DateTime time, [Description("备注的消息")] string remark)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        timeTask[1] = (time, () => {
            chatBot.Poke($"[{nameof(EventService)}] 来自Reminder的自定义唤醒：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => { }); //关闭定时提醒
        });

        continuousTimerCount = 0;
        SetTimer(null);
    }

    ChatBot chatBot = null!;
    EventServiceData configuration = null!;
    CancellationTokenSource updateCancelSource = null!;
    readonly (DateTime, Action)[] timeTask; //1为自动定时器，2为定时提醒
    int continuousTimerCount;

    public EventService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
        timeTask = new (DateTime, Action)[2];
    }
    public void Configure(EventServiceData configuration)
    {
        this.configuration = configuration;
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatBot.ChatSent += message => {
            if (chatBot.IsPokeMessage(message))
                return;
            continuousTimerCount = 0;
            SetTimer(null); //重置自动报点
        };

        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]系统报点：程序已重启（所有系统状态，如功能开关、桌宠位置全部已重置）。\n({configuration.AppendStartPrompt})");

        SetTimer(null);
        timeTask[1].Item1 = DateTime.MaxValue;

        updateCancelSource = new CancellationTokenSource();
        Update(updateCancelSource.Token);
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatBot.ChatAsync($"[{nameof(EventService)}]系统报点：程序即将关闭。\n({configuration.AppendDestroyPrompt})");
    }
    async void Update(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                foreach ((DateTime time, Action action) in timeTask)
                {
                    if (DateTime.Now > time)
                        action();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    void SetTimer(DateTime? time)
    {
        if (time == null)
        {
            int offset = Random.Shared.Next(-configuration.UpdateRandomOffset, configuration.UpdateRandomOffset);
            int timeOffset = (configuration.UpdateInterval + offset) * (int)MathF.Pow(3, MathF.Min(continuousTimerCount, 5));
            timeTask[0].Item1 = DateTime.Now.AddSeconds(timeOffset);
        }
        else
        {
            timeTask[0].Item1 = time.Value;
        }

        timeTask[0].Item2 = () => {
            chatBot.Poke($"""
                          [{nameof(EventService)}]系统报点：由Timer触发的自动唤醒（不要告诉主人是自动唤醒的，非常自然的做你想做的事即可）。
                          你可以借此自由活动，比如看新闻学知识、发起话题或继续刚刚的话题、偷窥主人屏幕、去Q群找人聊天玩耍、等等各种你想做的事.
                          ({configuration.AppendUpdatePrompt})
                          """);
            continuousTimerCount++;
            SetTimer(null); //自动进入下一次报点
        };
    }
}
