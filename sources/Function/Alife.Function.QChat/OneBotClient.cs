using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Alife.Function.QChat;

/// <summary>
/// 纯净的 OneBot v11 协议客户端，提供多态事件分发与协议握手状态管理
/// </summary>
public class OneBotClient : IAsyncDisposable
{
    public event Action<OneBotBaseEvent>? OnEventReceived;
    public event Action<bool>? OnConnectionStatusChanged;

    public long BotId => botId;

    public OneBotClient(string url)
    {
        this.url = url;
    }

    public async ValueTask DisposeAsync()
    {
        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
        ws.Dispose();
    }

    public async Task ConnectAsync()
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                throw new InvalidOperationException("[OneBotClient] 客户端已建立连接。请勿重复调用 ConnectAsync。");

            ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            lastHeartbeatTime = DateTimeOffset.Now.ToUnixTimeSeconds();

            TaskCompletionSource<long> handshakeTcs = new();
            Action<OneBotBaseEvent> handshakeHandler = e => {
                if (e.SelfId != 0) handshakeTcs.TrySetResult(e.SelfId);
            };


            OnEventReceived += handshakeHandler;
            _ = Task.Run(ReceiveLoop); //开始接收消息

            try
            {
                using CancellationTokenSource cts = new(10000);
                cts.Token.Register(() => handshakeTcs.TrySetCanceled());
                botId = await handshakeTcs.Task;
            }
            finally
            {
                OnEventReceived -= handshakeHandler;
            }

            OnConnectionStatusChanged?.Invoke(true);
            MonitorLoop();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("[OneBotClient] 协议握手超时：已建立网络连接，但在规定时间内没能收到包含 Bot ID 的信息报文。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OneBotClient] 连接失败: {ex.Message}");
            OnConnectionStatusChanged?.Invoke(false);
            throw;
        }
    }

    public async Task SendActionAsync(string action, object? @params = null, string? echo = null)
    {
        OneBotAction payload = new() {
            Action = action,
            Params = @params,
            Echo = echo
        };

        string json = JsonSerializer.Serialize(payload);
        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
            WebSocketMessageType.Text, true, CancellationToken.None);
    }

    readonly string url;
    ClientWebSocket ws = new();
    long botId;
    long lastHeartbeatTime;

    async void MonitorLoop()
    {
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                await Task.Delay(10000);
                long now = DateTimeOffset.Now.ToUnixTimeSeconds();

                if (now - lastHeartbeatTime > 60)
                {
                    Console.WriteLine("[OneBotClient] 链路心跳超时，主动重连中...");
                    await Reconnect();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OneBotClient] MonitorLoop Error: {ex}");
        }
    }

    async Task Reconnect()
    {
        if (ws.State == WebSocketState.Open)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnect", CancellationToken.None);
        }
        OnConnectionStatusChanged?.Invoke(false);
        await Task.Delay(5000);

        botId = 0;
        await ConnectAsync();
    }

    async void ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 64];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            await Reconnect();
        }
    }

    void HandleMessage(string json)
    {
        lastHeartbeatTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        OneBotBaseEvent? ev = JsonSerializer.Deserialize<OneBotBaseEvent>(json);
        if (ev != null) OnEventReceived?.Invoke(ev);
    }
}
