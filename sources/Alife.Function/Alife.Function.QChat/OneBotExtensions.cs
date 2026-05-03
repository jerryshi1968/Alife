namespace Alife.Function.QChat;

/// <summary>
/// 基础 API 扩展，提供最常用的消息发送与文件处理功能。
/// </summary>
public static class OneBotExtensions
{
    public static async Task SendPrivateMessage(this OneBotClient client, long userId, string message)
    {
        await client.SendActionAsync("send_private_msg", new { user_id = userId, message });
    }
    public static async Task SendGroupMessage(this OneBotClient client, long groupId, string message)
    {
        await client.SendActionAsync("send_group_msg", new { group_id = groupId, message });
    }
    public static async Task SendPrivateImage(this OneBotClient client, long userId, string file)
    {
        await client.SendActionAsync("send_private_msg", new { user_id = userId, message = $"[CQ:image,file={file}]" });
    }
    public static async Task SendGroupImage(this OneBotClient client, long groupId, string file)
    {
        await client.SendActionAsync("send_group_msg", new { group_id = groupId, message = $"[CQ:image,file={file}]" });
    }
    public static async Task UploadPrivateFile(this OneBotClient client, long userId, string filePath, string name)
    {
        await client.SendActionAsync("upload_private_file", new UploadFileParams { UserId = userId, File = filePath, Name = name });
    }
    public static async Task UploadGroupFile(this OneBotClient client, long groupId, string filePath, string name)
    {
        await client.SendActionAsync("upload_group_file", new UploadFileParams { GroupId = groupId, File = filePath, Name = name });
    }

    /// <summary>
    /// 下载文件（用于私聊）
    /// </summary>
    public static async Task<OneBotFile?> GetPrivateFile(this OneBotClient client, string fileId)
    {
        return await client.CallActionAsync<OneBotFile>("get_file", new { file = fileId });
    }
    /// <summary>
    /// 获取群文件下载链接。
    /// </summary>
    public static async Task<OneBotFile?> GetGroupFileUrl(this OneBotClient client, long groupId, string fileId)
    {
        return await client.CallActionAsync<OneBotFile>("get_group_file_url", new { group_id = groupId, file_id = fileId });
    }
    /// <summary>
    /// 根据消息 ID 获取消息详情。
    /// </summary>
    public static async Task<OneBotMessageEvent?> GetMessage(this OneBotClient client, long messageId)
    {
        return await client.CallActionAsync<OneBotMessageEvent>("get_msg", new { message_id = messageId });
    }
    /// <summary>
    /// 获取合并转发消息详情。
    /// </summary>
    public static async Task<List<OneBotForwardMessage>?> GetForwardMessage(this OneBotClient client, string forwardId)
    {
        OneBotForwardData? data = await client.CallActionAsync<OneBotForwardData>("get_forward_msg", new { id = forwardId });
        return data?.Messages;
    }

    /// <summary>
    /// 简单的文件异步下载辅助，包含基础请求头以绕过部分防盗链。
    /// </summary>
    public static async Task DownloadFileAsync(this string url, string savePath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        // 针对腾讯多媒体服务器设置 Referer
        if (url.Contains("multimedia.nt.qq.com.cn") || url.Contains("qpic.cn"))
        {
            request.Headers.Add("Referer", "https://q.qq.com/");
        }

        using var response = await SharedHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        byte[] data = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(savePath, data);
    }

    static readonly HttpClient SharedHttpClient = new();
}
