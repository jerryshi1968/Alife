using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

/// <summary>
/// OneBot CQ 码与富文本处理工具
/// </summary>
public static class OneBotSegment
{
    public static string GetSourceTag(this OneBotMessageEvent message)
    {
        string groupLabel = $"{message.GroupId}({message.GroupName})";
        string sayerLabel = $"{message.UserId}({message.Sender?.Nickname})";
        return message.MessageType == OneBotMessageType.Group
            ? $"[群聊 {groupLabel}, 发言人 {sayerLabel}]"
            : $"[私聊 {sayerLabel}]";
    }
    public static string GetSpeakerTag(this OneBotMessageEvent message)
    {
        string sayerLabel = $"{message.UserId}({message.Sender?.Nickname})";
        return $"[{sayerLabel}]";
    }
    public static string GetGroupTag(this OneBotMessageEvent message)
    {
        string groupLabel = $"{message.GroupId}({message.GroupName})";
        return $"[{groupLabel}]";
    }

    /// <summary>
    /// 将消息转换为 AI 友好的可读文本（处理回复、@、图片、表情等）。
    /// </summary>
    public static async Task<string> GetReadableMessage(this OneBotMessageEvent messageEvent, OneBotClient oneBotClient)
    {
        string content = messageEvent.RawMessage;
        content = FilterFace(content);
        content = FilterAt(content, oneBotClient.BotId);
        content = await FilterReply(content, oneBotClient);
        content = await FilterFile(content, messageEvent.GroupId, oneBotClient);
        content = FilterImage(content);
        return content;
    }
    public static string FilterFace(string text)
    {
        return Regex.Replace(text, @"\[CQ:face,id=(?<id>\d+).*?\]", "[表情: ${id}]");
    }
    public static string FilterAt(string text, long botId)
    {
        text = text.Replace($"[CQ:at,qq={botId}]", "@我");
        return Regex.Replace(text, @"\[CQ:at,qq=(?<qq>\d+)[^\]]*\]", "@${qq}");
    }
    public static async Task<string> FilterReply(string text, OneBotClient client)
    {
        var matches = Regex.Matches(text, @"\[CQ:reply,id=(?<id>-?\d+)[^\]]*\]");
        foreach (Match match in matches)
        {
            if (long.TryParse(match.Groups["id"].Value, out long replyId))
            {
                OneBotMessageEvent? quoted = await client.GetMessage(replyId);
                string quotedInfo = quoted != null
                    ? $"[回复 {quoted.UserId} 的消息: {quoted.RawMessage.GetPlainText()}]"
                    : "[回复其他消息]";
                text = text.Replace(match.Value, quotedInfo);
            }
        }
        return text;
    }
    public static string FilterImage(string text)
    {
        text = Regex.Replace(text, @"\[CQ:image,.*?url=(?<url>http[s]?://[^,\]]+).*?\]", "[图片: ${url}]");
        text = Regex.Replace(text, @"\[CQ:image[^\]]*\]", "[图片]");
        return text;
    }
    public static async Task<string> FilterFile(string text, long groupId, OneBotClient client)
    {
        var matches = Regex.Matches(text, @"\[CQ:file,.*?file_id=(?<id>[^,\]]+).*?file=(?<name>[^,\]]+).*?\]");
        foreach (Match match in matches)
        {
            string fileId = match.Groups["id"].Value;
            string fileName = match.Groups["name"].Value;

            OneBotFile? fileInfo = groupId != 0
                ? await client.GetGroupFileUrl(groupId, fileId)
                : await client.GetPrivateFile(fileId);

            string info = fileInfo != null
                ? $"[文件: {fileName}, 大小: {fileInfo.Size}b, 下载地址: {fileInfo.Url}]"
                : $"[文件: {fileName}]";

            text = text.Replace(match.Value, info);
        }
        return text;
    }


    /// <summary>
    /// 提取消息中的 @ ID（如果存在）。
    /// </summary>
    public static long? GetAtID(this OneBotMessageEvent message) => GetAtID(message.RawMessage);
    public static long? GetAtID(string rawMessage)
    {
        Match match = Regex.Match(rawMessage, @"\[CQ:at,qq=(?<id>-?\d+)");
        if (match.Success == false) return null;
        if (long.TryParse(match.Groups["id"].Value, out long id))
            return id;
        return null;
    }

    /// <summary>
    /// 提取消息中的回复消息 ID（如果存在）。
    /// </summary>
    public static long? GetReplyId(this OneBotMessageEvent message) => GetReplyId(message.RawMessage);
    public static long? GetReplyId(string rawMessage)
    {
        Match match = Regex.Match(rawMessage, @"\[CQ:reply,id=(?<id>-?\d+)");
        if (match.Success == false) return null;
        if (long.TryParse(match.Groups["id"].Value, out long id))
            return id;
        return null;
    }

    public static bool HasFile(this OneBotMessageEvent message) => message.RawMessage.Contains("[CQ:file");

    public static string? GetFileId(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"\[CQ:file,.*?file_id=(?<id>[^,\]]+)");
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static string? GetFileName(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"file=(?<name>[^,\]]+)");
        return match.Success ? match.Groups["name"].Value : null;
    }

    public static long? GetFileSize(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"file_size=(?<size>\d+)");
        if (match.Success && long.TryParse(match.Groups["size"].Value, out long result))
            return result;
        return null;
    }
    /// <summary>
    /// 移除所有 CQ 码，保留纯文本。
    /// </summary>
    public static string GetPlainText(this string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return Regex.Replace(text, @"\[CQ:.*?\]", "").Trim();
    }
}
