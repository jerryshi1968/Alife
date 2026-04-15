namespace Alife.Function.QChat;

/// <summary>
/// OneBot v11 API 扩展方法，将业务指令与核心链路层分离
/// </summary>
public static class OneBotExtensions
{
    public static async Task SendPrivateMessage(this OneBotClient client, long userId, string message)
    {
        await client.SendActionAsync("send_private_msg", new SendMessageParams { UserId = userId, Message = message });
    }

    public static async Task SendGroupMessage(this OneBotClient client, long groupId, string message)
    {
        await client.SendActionAsync("send_group_msg", new SendMessageParams { GroupId = groupId, Message = message });
    }

    public static async Task UploadPrivateFile(this OneBotClient client, long userId, string filePath, string name)
    {
        await client.SendActionAsync("upload_private_file", new UploadFileParams { UserId = userId, File = filePath, Name = name });
    }

    public static async Task UploadGroupFile(this OneBotClient client, long groupId, string filePath, string name)
    {
        await client.SendActionAsync("upload_group_file", new UploadFileParams { GroupId = groupId, File = filePath, Name = name });
    }
}
