using Alife.Framework;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public class MessageFilterData
{
    public bool EnableTimestamp { get; set; } = true;
    public string MessageAppend { get; set; } = "（注意！回复内容请保持简洁，严禁加入旁白表情等非正文内容，同时根据消息类型，需正确使用标签。）";
    public string PokeAppend { get; set; } = "";
}

[Plugin("消息加工", "统一管理消息的提示词注入和格式化。负责添加时间戳、通用提示词以及系统消息头。", LaunchOrder = -100,
    EditorUI = typeof(MessageProcessServiceUI))]
public class MessageProcessService : InteractivePlugin<MessageProcessService>, IConfigurable<MessageFilterData>
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
        string result = message;
        if (Configuration?.EnableTimestamp == true)
            result = $"[当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{result}";

        return $"{result}{Configuration?.MessageAppend}";
    }

    string OnPokeSend(string message)
    {
        return $"{message}{Configuration?.PokeAppend}";
    }
}