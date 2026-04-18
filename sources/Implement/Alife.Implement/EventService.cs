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
    [Description("设置下次重新开始自动报点的时间。")]
    public void SetTimer(XmlExecutorContext context, [Description("下次自动报点的时间，格式基于 ISO-8601")] DateTime time)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        SetTimer(time);
    }
    [XmlFunction]
    [Description("设置一个一次性的定时提醒（设置时自动取消上一个定时提醒）。如 <SetReminder delay=\"2026-04-18T09:30:00\" remark=\"提醒主人起床\" />")]
    public void SetReminder(XmlExecutorContext context, [Description("触发的时间，格式基于 ISO-8601")] DateTime time, [Description("备注的消息")] string remark)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        timeTask[1] = (time, () => {
            chatBot.Poke($"[{nameof(EventService)}] 来自Reminder的定时提醒：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => { }); //关闭定时提醒
        });
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
        chatBot.ChatSent += _ => {
            continuousTimerCount = 0;
            SetTimer(null); //重置自动报点
        };

        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]对话活动重新开始。({configuration.AppendStartPrompt})");

        SetTimer(null);
        timeTask[1].Item1 = DateTime.MaxValue;

        updateCancelSource = new CancellationTokenSource();
        Update(updateCancelSource.Token);
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatBot.ChatAsync($"[{nameof(EventService)}]对话活动即将关闭。({configuration.AppendDestroyPrompt})");
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
            timeTask[0].Item1 = DateTime.Now.AddSeconds((configuration.UpdateInterval + offset) * MathF.Pow(3, MathF.Min(continuousTimerCount, 5)));
        }
        else
        {
            timeTask[0].Item1 = time.Value;
        }

        timeTask[1].Item2 = () => {
            chatBot.Poke($"[{nameof(EventService)}] 这是系统自动报点。你可以借此自由活动或保持安静。({configuration.AppendUpdatePrompt})");
            continuousTimerCount++;
            SetTimer(null); //自动进入下一次报点
        };
    }
}
