using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

/// <summary>
/// OneBot CQ 码与富文本处理工具
/// </summary>
public static class OneBotSegment
{
    /// <summary>
    /// 将包含 [CQ:...] 的字符串转换为纯文本（或解析出特定信息）
    /// </summary>
    public static string ToPlainText(string content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        // 简单提取所有非 CQ 码部分
        return Regex.Replace(content, @"\[CQ:[^\]]+\]", "").Trim();
    }

    /// <summary>
    /// 构造 At 消息片段
    /// </summary>
    public static string At(long userId) => $"[CQ:at,qq={userId}]";

    /// <summary>
    /// 构造表情片段
    /// </summary>
    public static string Face(int id) => $"[CQ:face,id={id}]";

    /// <summary>
    /// 构造图片片段
    /// </summary>
    public static string Image(string file) => $"[CQ:image,file={file}]";

    /// <summary>
    /// 检查消息是否提到特定的 QQ 号
    /// </summary>
    public static bool IsAt(string content, long selfId)
    {
        if (string.IsNullOrEmpty(content)) return false;
        return content.Contains($"[CQ:at,qq={selfId}]") || content.Contains($"[CQ:at,qq={selfId},");
    }
}
