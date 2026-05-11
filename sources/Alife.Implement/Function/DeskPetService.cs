using System.ComponentModel;
using Alife.Basic;
using Alife.Framework;
using Alife.Function.DeskPet;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("桌宠交互", "将Live2D桌宠接入AI系统，实现表现力同步和互动反馈。")]
public class DeskPetService : InteractivePlugin<DeskPetService>, IAsyncDisposable
{
    [XmlFunction]
    [Description("显示一段气泡文本")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content)
    {
        if (context.CallMode == CallMode.Reset)
        {
            client!.HideBubble();
            return;
        }

        if (context.CallMode == CallMode.Closing)
        {
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                await Task.Delay(
                    TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()));
            client!.HideBubble();
        }

        if (context.CallMode != CallMode.Content)
            return;

        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content) == false)
        {
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                await Task.Delay(
                    TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()));
            client!.ShowBubble(content);
            lastBubbleEndTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() + content.Length * 150;
        }
    }

    [XmlFunction]
    [Description("表演一个表情（具体选项见附加说明）")]
    public void Expression(XmlExecutorContext context, string option)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client!.SupportedExpressions.Contains(option) == false)
            throw new Exception("选项不存在");

        client!.PlayExpression(option);
    }

    [XmlFunction]
    [Description("表演一个动作（具体选项见附加说明）")]
    public void Motion(XmlExecutorContext context, string option)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client!.SupportedMotions.TryGetValue(option, out (string Group, int Index) motion) == false)
            throw new Exception("选项不存在");

        client.PlayMotion(motion.Group, motion.Index);
    }

    [XmlFunction]
    [Description("获取当前屏幕位置（使用后需等待结果返回）")]
    public async Task Position(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        try
        {
            (double x, double y) = await client!.GetPositionAsync();
            Poke($"当前位置: x={x}, y={y}");
        }
        catch (TimeoutException)
        {
            Poke("获取坐标超时");
        }
    }

    [XmlFunction]
    [Description("在屏幕上进行相对移动（注意！该移动方式为相对位置移动，使用前最好先确认当前位置）")]
    public async Task Move(XmlExecutorContext context, double x = 0, double y = 0, int duration = 1000)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        await client!.MoveAsync(x, y, duration);
        (x, y) = await client!.GetPositionAsync();
        Poke($"移动成功，当前位置: x={x}, y={y}");
    }

    PetServer? client;
    long lastBubbleEndTime;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        client = new PetServer("Mao/Mao.model3.json");
        string supportedExpressionsDescription = string.Join(", ", client.SupportedExpressions);
        string supportedMotionsDescription = string.Join(", ", client.SupportedMotions.Keys);


        FunctionService functionService = context.Services.GetRequiredService<FunctionService>();
        XmlHandler xmlHandler = new(this)
        {
            Description = "此服务让你获得一副可控可交互的Live2D身体。（这是你主要输出表情动作的工具，一定要积极使用）",
            Explain = $"""
                       - 支持的 {nameof(Expression)}：{supportedExpressionsDescription}
                       - 支持的 {nameof(Motion)}：{supportedMotionsDescription}
                       - 当前屏幕分辨率：{AlifePlatform.GetResolution()}
                       """
        };
        functionService.RegisterHandler(xmlHandler);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        await client!.WaitReadyAsync();
        client.OnInput += Chat;
        client.OnInteracted += text => Poke("交互：" + text);

        // 启动状态轮询
        _ = UpdateStatusLoop(chatActivity.ChatBot);
    }

    async Task UpdateStatusLoop(ChatBot chatBot)
    {
        bool lastStatus = false;
        while (!isDisposed)
        {
            try
            {
                bool currentStatus = chatBot.IsChatting;
                if (currentStatus != lastStatus)
                {
                    lastStatus = currentStatus;
                    client?.SendStatus(currentStatus);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            await Task.Delay(250);
        }
    }

    public async ValueTask DisposeAsync()
    {
        isDisposed = true;
        if (client != null)
            await client.DisposeAsync();
    }

    bool isDisposed;
}