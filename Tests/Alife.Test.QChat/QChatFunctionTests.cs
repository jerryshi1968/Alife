using Alife.Function.QChat;
using System.IO;
using NUnit.Framework;
using System.Windows;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatFunctionTests
{
    [OneTimeSetUp]
    public async Task Setup()
    {
        client = new OneBotClient(TestUrl);
        await client.ConnectAsync();
    }

    [Test, Order(1)]
    public void TestConnection()
    {
        Console.WriteLine($"已连接 OneBot。服务器 Bot ID: {client.BotId}");
        Assert.That(client.BotId, Is.Not.Zero);
    }

    [Test, Order(2)]
    public async Task TestSendPrivateMessage()
    {
        string msg = $"[QChat单元测试] 你好，主人喵！当前时间：{DateTime.Now:HH:mm:ss}";
        await client.SendPrivateMessage(TestPrivateId, msg);

        AskUser($"你的 QQ 是否收到了来自 Bot ({client.BotId}) 的私聊测试消息？\n内容: \"{msg}\"");
    }

    [Test, Order(3)]
    public async Task TestReceivePrivateMessage()
    {
        OneBotBaseEvent? receivedEvent = null;
        client.OnEventReceived += e => {
            if (e is OneBotMessageEvent { UserId: TestPrivateId }) receivedEvent = e;
        };

        MessageBox.Show($"请向 Bot ({client.BotId}) 发送一条私聊消息：'验证接收'。\n完成后点击确定。", "人工指令");

        int retry = 0;
        while (receivedEvent == null && retry++ < 20) await Task.Delay(500);

        Assert.That(receivedEvent, Is.Not.Null, "未收到预期的私聊消息。");
    }

    [Test, Order(4)]
    public async Task TestUploadFile()
    {
        string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_file.txt");
        await File.WriteAllTextAsync(tempFile, $"这是一个来自 Alife 单元测试的上传文件，生成于 {DateTime.Now}");

        await client.UploadPrivateFile(TestPrivateId, tempFile, "测试文档.txt");

        AskUser("你的 QQ 是否收到并能正常下载一个名为 '测试文档.txt' 的文件？");
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await client.DisposeAsync();
    }

    OneBotClient client = null!;
    const string TestUrl = "ws://127.0.0.1:3001";
    const long TestPrivateId = 1330958515L;

    void AskUser(string question)
    {
        MessageBoxResult result = MessageBox.Show(question, "单元测试人工验证", MessageBoxButton.YesNo, MessageBoxImage.Question);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), $"人工验证失败: {question}");
    }
}
