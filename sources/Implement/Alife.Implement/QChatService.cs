using System.ComponentModel;
using System.Text;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public record QChatConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public long OwnerId { get; set; }
    public bool IsGroupEnabled { get; set; } = true;
    public int AutoCloseMinutes { get; set; } = 30;
}

[Plugin("QQ聊天", "连接 OneBot v11 服务器，实现 QQ 消息收发、群聊自动管理及文件传输。")]
public class QChatService : Plugin, IAsyncDisposable, IConfigurable<QChatConfig>
{
    public QChatService(InterpreterService interpreterService, StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        interpreterService.RegisterHandler(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (oneBotClient != null) await oneBotClient.DisposeAsync();
    }

    public void Configure(QChatConfig configuration)
    {
        this.config = configuration;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        oneBotClient = new OneBotClient(config.Url);
        await oneBotClient.ConnectAsync();

        string prompt = GeneratePrompt();
        if (string.IsNullOrEmpty(prompt) == false)
        {
            context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
            context.contextBuilder.ChatHistory.AddUserMessage($"[QChatService] 当前群聊状态为: {(config.IsGroupEnabled ? "开启" : "关闭")}");
        }
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        this.chatActivity = chatActivity;

        oneBotClient.OnEventReceived += (OneBotBaseEvent e) => _ = HandleMessage(e);
        oneBotClient.OnConnectionStatusChanged += (bool connected) => Console.WriteLine($"[QChatService] OneBot 连接: {(connected ? "在线" : "掉线")}");

        GlobalLoop();
        return Task.CompletedTask;
    }

    [XmlFunction]
    [Description("发送文本消息。支持 [CQ:at,qq=xxx] 回复特定用户。")]
    public async Task QChat(XmlExecutorContext ctx, long target = 0, string type = "")
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;

        string content = ctx.FullContent.Trim();
        if (string.IsNullOrEmpty(content)) return;

        string finalType = string.IsNullOrEmpty(type) == false ? type : lastType;
        long finalTarget = target != 0 ? target : (finalType == "group" ? lastGroupTarget : lastPrivateTarget);

        if (finalTarget == 0) throw new InvalidOperationException("[QChat] 无法确定消息目标：未提供 target 且无上下文历史。");

        if (finalTarget == lastSentTarget && content == lastSentMessage) return;
        lastSentMessage = content;
        lastSentTarget = finalTarget;

        if (finalType == "group")
            await oneBotClient.SendGroupMessage(finalTarget, content);
        else
            await oneBotClient.SendPrivateMessage(finalTarget, content);
    }

    [XmlFunction]
    [Description("向 QQ 发送图片或文件。")]
    public async Task QSendFile(XmlExecutorContext ctx, string file = "", long target = 0, string type = "")
    {
        if (ctx.CallMode != CallMode.OneShot || string.IsNullOrEmpty(file)) return;

        string finalType = string.IsNullOrEmpty(type) == false ? type : lastType;
        long finalTarget = target != 0 ? target : (finalType == "group" ? lastGroupTarget : lastPrivateTarget);
        
        if (finalTarget == 0) throw new InvalidOperationException("[QSendFile] 无法确定文件发送目标。");

        file = file.Replace(Path.DirectorySeparatorChar, '/');
        string fileName = Path.GetFileName(file);

        if (File.Exists(file))
        {
            if (finalType == "group") await oneBotClient.UploadGroupFile(finalTarget, file, fileName);
            else await oneBotClient.UploadPrivateFile(finalTarget, file, fileName);
        }
        else
        {
            string msg = OneBotSegment.Image(file);
            if (finalType == "group") await oneBotClient.SendGroupMessage(finalTarget, msg);
            else await oneBotClient.SendPrivateMessage(finalTarget, msg);
        }
    }

    [XmlFunction]
    [Description("设置群消息监听开关。")]
    public void QToggleGroup(XmlExecutorContext ctx, bool enabled)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;

        config.IsGroupEnabled = enabled;
        if (enabled) lastGroupActivityTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        chatActivity.ChatBot.Poke($"[QChatService] 群消息监听已{(enabled ? "开启" : "关闭")}");
    }

    OneBotClient oneBotClient = null!;
    readonly StorageSystem storageSystem;
    QChatConfig config = null!;
    ChatActivity chatActivity = null!;

    long lastPrivateTarget;
    long lastGroupTarget;
    string lastType = "private";
    string lastSentMessage = "";
    long lastSentTarget;
    long lastGroupActivityTime;

    readonly Dictionary<long, StringBuilder> groupBuffers = new();

    async void GlobalLoop()
    {
        while (true)
        {
            try
            {
                await Task.Delay(10000);
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                Dictionary<long, string> batches = new();
                lock (groupBuffers)
                {
                    if (groupBuffers.Count > 0)
                    {
                        foreach (KeyValuePair<long, StringBuilder> pair in groupBuffers) 
                            batches[pair.Key] = pair.Value.ToString();
                        groupBuffers.Clear();
                    }
                }
                foreach (KeyValuePair<long, string> pair in batches) 
                    chatActivity.ChatBot.Poke(pair.Value);

                if (config.IsGroupEnabled && now - lastGroupActivityTime > config.AutoCloseMinutes * 60000)
                {
                    config.IsGroupEnabled = false;
                    chatActivity.ChatBot.Poke($"[QChatService] 由于长时间无活跃群聊消息，群监听已自动关闭。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QChatService] GlobalLoop Error: {ex}");
            }
        }
    }

    async Task HandleMessage(OneBotBaseEvent e)
    {
        if (e is OneBotMessageEvent msg) await HandleChatMessage(msg);
    }

    async Task HandleChatMessage(OneBotMessageEvent e)
    {
        if (oneBotClient.BotId != 0 && e.UserId == oneBotClient.BotId) return;

        string rawMsg = e.RawMessage ?? "";
        string plainMsg = OneBotSegment.ToPlainText(rawMsg);
        bool isAtMe = OneBotSegment.IsAt(rawMsg, oneBotClient.BotId);

        lastType = e.MessageType == OneBotMessageType.Group ? "group" : "private";
        if (lastType == "group")
        {
            lastGroupTarget = e.GroupId;
            lastGroupActivityTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
        else lastPrivateTarget = e.UserId;

        string tag = lastType == "group" ? $"[群 {e.GroupId}, 人 {e.UserId}]" : $"[私聊 {e.UserId}]";
        string formatted = $"{tag} {plainMsg}";

        if (lastType == "private")
        {
            await chatActivity.ChatBot.ChatAsync(formatted);
        }
        else if (lastType == "group" && (config.IsGroupEnabled || isAtMe))
        {
            if (isAtMe) config.IsGroupEnabled = true; 
            
            lock (groupBuffers)
            {
                if (groupBuffers.TryGetValue(e.GroupId, out StringBuilder? sb) == false) 
                    groupBuffers[e.GroupId] = sb = new StringBuilder();
                sb.AppendLine(formatted);
            }
        }
    }

    string GeneratePrompt() => $"""
        # QChat (QQ 模块) 运行手册
        - 主人 QQ: {config.OwnerId} (优先响应主人的指令)。
        - 你的 QQ: {oneBotClient.BotId} (在此 QQ 被 At 时必须回应)。
        - 交互方式: 使用 <QChat /> 发送消息， <QSendFile /> 发送文件/图片。
        - 自动管理: 群聊长时间不活跃会自动关闭监听以节省性能，如需开启请手动调用 <QToggleGroup enabled="true" />。
        """;
}
