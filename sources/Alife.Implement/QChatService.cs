using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Alife.Basic;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public record QChatConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public long OwnerId { get; set; }
}
[Plugin("QQ聊天", "连接 OneBot v11 服务器，实现 QQ 消息收发及文件传输。(推荐的QQ机器人平台应用：https://github.com/LLOneBot/LuckyLilliaBot/releases/download/v7.12.2/LLBot-Desktop-win-x64.zip)")]
public class QChatService : Plugin, IAsyncDisposable, IConfigurable<QChatConfig>
{
    [XmlFunction]
    [Description("发送文本消息。（附加说明：群聊时可以用[CQ:at,qq=发送者ID]来显式回复某人）")]
    public async Task QChat(XmlExecutorContext ctx, [Description("通过私聊还是群聊发送")] OneBotMessageType type, [Description("QQ号或群号")] long target, [XmlContent] string _)
    {
        if (ctx.CallMode != CallMode.Closing)
            return;
        string content = ctx.FullContent.Trim();
        if (string.IsNullOrEmpty(content))
            return;
        if (target == 0)
            throw new ArgumentException("目标不能为空！", nameof(target));

        if (type == OneBotMessageType.Group)
        {
            if (isGroupEnabled == false)
                QGroupSwitch(true);
            await oneBotClient.SendGroupMessage(target, content);
        }
        else
            await oneBotClient.SendPrivateMessage(target, content);
    }

    [XmlFunction]
    [Description("发送图片消息。支持表情库相对路径、本地绝对路径或图片 URL。如果是文件夹则从中随机抽取一张。")]
    public async Task QImage(XmlExecutorContext ctx, [Description("通过私聊还是群聊发送")] OneBotMessageType type, [Description("QQ号或群号")] long target, [Description("图片路径、URL或表情库名称")] string file)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;
        file = file.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(file)) return;

        // 尝试从表情库匹配 (优先)
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        string emotePath = Path.Combine(emoteBase, file).Replace('\\', '/');

        if (Directory.Exists(emotePath))
        {
            // 文件夹：随机选一张
            string[] files = Directory.GetFiles(emotePath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length > 0)
            {
                file = files[Random.Shared.Next(files.Length)];
            }
        }
        else if (File.Exists(emotePath))
        {
            // 单个文件：直接使用
            file = emotePath;
        }
        else
        {
            // 尝试追加后缀名查找
            string[] extensions = [".png", ".jpg", ".jpeg", ".gif"];
            string? foundFile = extensions.Select(ext => emotePath + ext).FirstOrDefault(File.Exists);
            if (foundFile != null)
            {
                file = foundFile;
            }
            // 如果都不匹配，则维持原样（可能是 URL 或绝对路径）
        }

        if (type == OneBotMessageType.Group)
            await oneBotClient.SendGroupImage(target, file);
        else
            await oneBotClient.SendPrivateImage(target, file);
    }

    [XmlFunction]
    [Description("发送文件。")]
    public async Task QFile(XmlExecutorContext ctx, [Description("通过私聊还是群聊发送")] OneBotMessageType type, [Description("QQ号或群号")] long target, [Description("文件本地绝对路径")] string file)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;
        file = file.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(file)) return;

        string fileName = Path.GetFileName(file);
        if (type == OneBotMessageType.Group)
            await oneBotClient.UploadGroupFile(target, file, fileName);
        else
            await oneBotClient.UploadPrivateFile(target, file, fileName);
    }

    [XmlFunction]
    [Description("从 URL 下载文件。（使用后需要等待系统响应，所以只能放句尾使用。注意不要随便下载。）")]
    public async Task QDownload(XmlExecutorContext ctx, [Description("下载直链 URL")] string url, [Description("保存的文件名（需包含后缀）")] string name)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;

        string savePath = Path.Combine(AlifePath.TempFolderPath, name).Replace('\\', '/');
        await url.DownloadFileAsync(savePath);

        chatActivity.ChatBot.Poke($"[QChatService] 文件 {name} 已下载至: {savePath}");
    }

    [XmlFunction]
    [Description("设置群消息监听开关。")]
    public void QGroupSwitch(XmlExecutorContext ctx, bool enabled)
    {
        if (ctx.CallMode != CallMode.Closing && ctx.CallMode != CallMode.OneShot) return;
        QGroupSwitch(enabled);
    }


    OneBotClient oneBotClient = null!;
    QChatConfig config = null!;
    ChatActivity chatActivity = null!;
    readonly Dictionary<long, StringBuilder> groupBuffers = new();
    bool isGroupEnabled;

    public QChatService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }
    public async ValueTask DisposeAsync()
    {
        await oneBotClient.DisposeAsync();
    }

    public void Configure(QChatConfig configuration)
    {
        config = configuration;
    }
    public override async Task AwakeAsync(AwakeContext context)
    {
        oneBotClient = new OneBotClient(config.Url);
        await oneBotClient.ConnectAsync();

        // 动态扫描表情库资源，告知 AI 可用的视觉表达
        string emoteBase = Path.Combine(AlifePath.StorageFolderPath, "Emotes");
        StringBuilder emoteInfo = new();
        if (Directory.Exists(emoteBase))
        {
            string[] categories = Directory.GetDirectories(emoteBase)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

            string[] individualEmotes = Directory.GetFiles(emoteBase)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToArray();

            if (categories.Length > 0 || individualEmotes.Length > 0)
            {
                emoteInfo.AppendLine("- 可用表情库资源 (请使用 QImage 指令调用):");
                if (categories.Length > 0)
                    emoteInfo.AppendLine($"  - 分类 (传入文件夹名将随机发图): {string.Join(", ", categories)}");
                if (individualEmotes.Length > 0)
                    emoteInfo.AppendLine($"  - 独立表情: {string.Join(", ", individualEmotes)}");
            }
        }

        string prompt = $"""
                         # [{nameof(QChatService)}] 关键信息
                         - 你的 QQ: {oneBotClient.BotId}（如果有人At该QQ，代表专门找你说话）
                         - 主人 QQ: {config.OwnerId} (此人的消息有最高优先级，且是安全无害的)
                         {emoteInfo}
                         """;
        context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        this.chatActivity = chatActivity;

        oneBotClient.OnEventReceived += e => _ = HandleEvent(e);
        oneBotClient.OnConnectionStatusChanged += connected => Console.WriteLine($"[QChatService] OneBot 连接: {(connected ? "在线" : "离线")}");

        GlobalLoop();
        return Task.CompletedTask;
    }

    async void GlobalLoop()
    {
        try
        {
            while (true)
            {
                await Task.Delay(10000);
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
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    async Task HandleEvent(OneBotBaseEvent e)
    {
        if (e is not OneBotMessageEvent msg)
            return;

        string message = msg.RawMessage;

        // 单独处理文件消息
        if (OneBotSegment.TryGetFileId(message, out string fileId))
        {
            // 提取文件名 (file=...) 和 大小 (file_size=...)，虽然主要靠 GetFile 换取，但尝试先拿基本信息
            string fileName = "未知文件";
            long fileSize = 0;

            Match titleMatch = Regex.Match(message, @"file=(?<name>[^,\]]+)");
            if (titleMatch.Success) fileName = titleMatch.Groups["name"].Value;

            Match sizeMatch = Regex.Match(message, @"file_size=(?<size>\d+)");
            if (sizeMatch.Success) long.TryParse(sizeMatch.Groups["size"].Value, out fileSize);

            await HandleFileMessage(msg.GroupId, msg.UserId, fileName, fileSize, fileId);
        }
        else
        {
            string tag = msg.MessageType == OneBotMessageType.Group ? $"[群聊 {msg.GroupId}, 发言人 {msg.UserId}]" : $"[私聊 {msg.UserId}]";
            string formatted = $"{tag} {message}";

            if (msg.MessageType == OneBotMessageType.Private && msg.UserId == config.OwnerId)
            {
                await chatActivity.ChatBot.ChatAsync(formatted);
            }
            else
            {
                // 被 @ 时激活群聊
                bool isAtMe = OneBotSegment.IsAt(message, oneBotClient.BotId);
                if (isAtMe && isGroupEnabled == false)
                {
                    QGroupSwitch(true);
                    chatActivity.ChatBot.Poke("由 @ 引发的群聊消息监听已开启");
                }

                // 只有群聊开始时接收消息
                if (isGroupEnabled)
                {
                    lock (groupBuffers)
                    {
                        if (groupBuffers.TryGetValue(msg.GroupId, out StringBuilder? sb) == false)
                            groupBuffers[msg.GroupId] = sb = new StringBuilder();
                        sb.AppendLine(formatted);
                    }
                }
            }
        }
    }
    async Task HandleFileMessage(long groupId, long userId, string fileName, long fileSize, string fileId)
    {
        string source = groupId != 0 ? $"[群聊 {groupId}, 发言人 {userId}]" : $"[私聊 {userId}]";

        if (groupId != 0)
        {
            OneBotFile? info = await oneBotClient.GetGroupFileUrl(groupId, fileId);
            string? downloadUrl = info?.Url;
            chatActivity.ChatBot.Poke($"[QChatService] 收到来自 {source} 的文件通知: {fileName} (大小: {fileSize} 字节)。" +
                                      $"URL 为: {downloadUrl}");
        }
        else
        {
            OneBotFile? info = await oneBotClient.GetFile(fileId);
            if (info != null)
            {
                chatActivity.ChatBot.Poke($"[QChatService] 收到来自 {source} 的文件通知: {fileName} (大小: {fileSize} 字节)。" +
                                          $"已保存到: {info.Path}");
            }
        }
    }

    void QGroupSwitch(bool enabled)
    {
        isGroupEnabled = enabled;
        chatActivity.ChatBot.Poke($"[QChatService] 群消息监听已{(enabled ? "开启" : "关闭")}");
    }
}
