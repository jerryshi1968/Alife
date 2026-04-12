using System.ComponentModel;
using Alife.Framework;
using Alife.Function.DeskPet;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("Live2D桌宠", "将Live2D桌宠接入AI系统，实现表现力同步和互动反馈。")]
[Description("此服务让你获得控制Live2D桌宠以及接收其交互的能力")]
public class DeskPetService : Plugin, IAsyncDisposable
{
    [XmlFunction("say")]
    [Description("发送消息：显示一段文本字幕。示例: <say>你好</say>")]
    public void PetBubble(XmlExecutorContext context, [XmlContent] string content)
    {
        switch (context.CallMode)
        {
            //流式输出模式：在内容更新时显示气泡
            case CallMode.Content:
                if (string.IsNullOrWhiteSpace(content) == false)
                    client.ShowBubble(content);
                break;
            //标签结束模式：关闭气泡
            case CallMode.Closing:
            case CallMode.Reset:
                client.HideBubble();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [XmlFunction("exp")]
    [Description("控制表情：切换当前显示的表情。具体类型见互动指南。")]
    public void PetExpression(XmlExecutorContext context, string exp)
    {
        if (context.CallMode != CallMode.OneShot)
            return;
        exp = exp.Trim();
        if (string.IsNullOrWhiteSpace(exp))
            return;

        client.PlayExpression(exp);
    }
    [XmlFunction("mtn")]
    [Description("执行动作：播放预设动画。具体类型见互动指南。")]
    public void PetMotion(XmlExecutorContext context, string mtn)
    {
        if (context.CallMode != CallMode.OneShot)
            return;
        mtn = mtn.Trim();
        if (string.IsNullOrWhiteSpace(mtn))
            return;
        if (client.SupportedMotions.TryGetValue(mtn, out (string Group, int Index) motion) == false)
            return;

        client.PlayMotion(motion.Group, motion.Index);
    }
    [XmlFunction("move")]
    [Description("移动位置：在屏幕上进行相对位移。示例: <pmove x=\"100\" y=\"50\" duration=\"3000\" /> - 表示向右移100像素，下移50像素")]
    public Task PetMove(XmlExecutorContext context, double x = 0, double y = 0, int duration = 1000)
    {
        if (context.CallMode != CallMode.OneShot)
            return Task.CompletedTask;

        if (x == 0 && y == 0 && string.IsNullOrEmpty(context.FullContent) == false)
        {
            string[] parts = context.FullContent.Split(',');
            if (parts.Length >= 2)
            {
                double.TryParse(parts[0].Trim(), out x);
                double.TryParse(parts[1].Trim(), out y);
            }
        }

        if (duration <= 0) duration = 1000;

        return client.MoveAsync(x, y, duration);
    }
    [XmlFunction("pos")]
    [Description("获取位置：获取当前在屏幕上的绝对坐标。示例: <pos />")]
    public async Task PetPos(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        try
        {
            (double x, double y) = await client.GetPositionAsync();
            chatBot.Poke($"[DeskPetService] 当前坐标: x={x}, y={y}");
        }
        catch (TimeoutException)
        {
            chatBot.Poke("[DeskPetService] 获取坐标超时");
        }
    }

    ChatBot chatBot = null!;
    PetServer client = null!;


    public DeskPetService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        client = new PetServer("Mao/Mao.model3.json");
        string supportedExpressionsDescription = string.Join(", ", client.SupportedExpressions);
        string supportedMotionsDescription = string.Join(", ", client.SupportedMotions.Keys);
        context.contextBuilder.ChatHistory.AddSystemMessage($"""
                                                             # DeskPetService 互动指南

                                                             ## 支持的表情动作
                                                                - 支持的 exp（表情）：{supportedExpressionsDescription}
                                                                - 支持的 mtn（动作）：{supportedMotionsDescription}
                                                             """);
        return Task.CompletedTask;
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;

        await client.WaitReadyAsync();
        client.OnInput += text => chatBot.Chat("[DeskPetService] " + text);
        client.OnInteracted += text => chatBot.Poke("[DeskPetService] (交互: " + text + ")");
    }
    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
