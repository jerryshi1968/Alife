using System;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.SemanticKernel;

namespace Alife.Function.MessageFilter;

public class MessageFilterData
{
    public bool EnableTimestamp { get; set; } = true;
    public string MessageAppend { get; set; } = "（请简洁回复，禁用旁白、emoji）";
    public string PokeAppend { get; set; } = "";
    public int MaxMessageLength { get; set; } = 5000;
}

[Plugin("消息过滤", "统一管理消息的提示词注入和格式化。负责添加时间戳、通用提示词以及系统消息头。",
defaultCategory: "Alife 官方/生活环境",
LaunchOrder = -100, EditorUI = typeof(MessageFilterServiceUI))]
public class MessageFilterService : InteractivePlugin<MessageFilterService>, IConfigurable<MessageFilterData>
{
    public MessageFilterData? Configuration { get; set; }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);
        ChatBot.ChatSend += OnChatSend;
        ChatBot.PokeSend += OnPokeSend;
    }

    string OnChatSend(string message)
    {
        string result = $"{message}{Configuration?.MessageAppend}";
        if (Configuration?.EnableTimestamp == true)
            result = $"[当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{result}";

        if (result.Length > Configuration!.MaxMessageLength)
        {
            result = result.Substring(0, Configuration!.MaxMessageLength);
            result += $"(文本过长，超过 {Configuration?.MaxMessageLength} 的部分已截断)";
        }

        return result;
    }

    string OnPokeSend(string message)
    {
        return $"{message}{Configuration?.PokeAppend}";
    }
}
