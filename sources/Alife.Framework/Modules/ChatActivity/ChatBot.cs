using System.Collections.Concurrent;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Framework;

public class ChatBot : IAsyncDisposable
{
    public const string ThinkContentPrefix = "__THINK__";

    public event Func<string, string>? ChatSend;
    public event Func<string, string>? PokeSend;
    public event Action<string>? ChatSent;
    public event Action<string>? ChatReceived;
    public event Action<string>? ReasoningReceived;
    public event Action? ChatOver;

    public event Action<ChatMessageContent>? ChatHistoryAdd;
    public event Action<ChatTokenUsage>? TokenUsed;
    public ChatHistory ChatHistory => llmAgentThread.ChatHistory;
    public SemaphoreSlim ChatSemaphore => chatSemaphore;
    public bool IsChatting => chatSemaphore.CurrentCount == 0;

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, AuthorRole? role = null)
    {
        if (IsChatting) //打断上一次的聊天
        {
            if (cancelChatSource != null)
                await cancelChatSource.CancelAsync();
        }

        await chatSemaphore.WaitAsync();
        try
        {
            if (ChatSend != null)
            {
                foreach (Delegate @delegate in ChatSend.GetInvocationList())
                {
                    Func<string, string> chatSend = (Func<string, string>)@delegate;
                    message = chatSend.Invoke(message);
                }
            }

            message = message.Trim();
            llmAgentThread.ChatHistory.AddMessage(role ?? AuthorRole.User, message);
            cancelChatSource = new CancellationTokenSource();

            ChaseChatHistory();

            ChatSent?.Invoke(message);
            string? error = null;
            StringBuilder cleanResponseBuilder = new(); // 用于存储不含思考过程的最终回复

            await using IAsyncEnumerator<AgentResponseItem<StreamingChatMessageContent>> enumerator = llmAgent
                .InvokeStreamingAsync(llmAgentThread, cancellationToken: cancelChatSource.Token)
                .GetAsyncEnumerator();
            while (true)
            {
                try
                {
                    if (await enumerator.MoveNextAsync() == false)
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    error = e.ToString();
                    break;
                }

                string? content = enumerator.Current.Message.Content;
                if (content != null)
                {
                    //前置报文会对思考内容进行特殊处理，以便兼容思考模式
                    if (content.StartsWith(ThinkContentPrefix))
                    {
                        string reasoningPart = content.Substring(ThinkContentPrefix.Length);
                        if (!string.IsNullOrEmpty(reasoningPart))
                        {
                            ReasoningReceived?.Invoke(reasoningPart);
                        }
                    }
                    else
                    {
                        yield return content;
                        ChatReceived?.Invoke(content);
                        cleanResponseBuilder.Append(content);
                    }
                }

                var metaData = enumerator.Current.Message.Metadata;
                if (metaData != null)
                {
                    // 尝试从元数据中提取思考过程 (支持原生支持此字段的 SDK)
                    if (metaData.TryGetValue("ReasoningContent", out object? reasoning) ||
                        metaData.TryGetValue("reasoning_content", out reasoning))
                    {
                        string? reasoningStr = reasoning?.ToString();
                        if (!string.IsNullOrEmpty(reasoningStr))
                        {
                            ReasoningReceived?.Invoke(reasoningStr);
                        }
                    }


                    if (metaData.TryGetValue("Usage", out object? usage))
                    {
                        if (usage is ChatTokenUsage chatTokenUsage)
                        {
                            Console.WriteLine(
                                $"[Token消耗] total:{chatTokenUsage.TotalTokenCount} input:{chatTokenUsage.InputTokenCount}({chatTokenUsage.InputTokenDetails?.CachedTokenCount}) output:{chatTokenUsage.OutputTokenCount} ");
                            TokenUsed?.Invoke(chatTokenUsage);
                        }
                    }
                }
            }

            // 在同步历史记录前，清洗掉可能存入 ChatHistory 的思考内容（防止污染上下文）
            if (llmAgentThread.ChatHistory.Count > 0)
            {
                ChatMessageContent lastMsg = llmAgentThread.ChatHistory[^1];
                if (lastMsg.Role == AuthorRole.Assistant && (lastMsg.Content?.Contains(ThinkContentPrefix) ?? false))
                    lastMsg.Content = cleanResponseBuilder.ToString();
            }

            ChatOver?.Invoke();

            ChaseChatHistory();

            if (error != null)
            {
                llmAgentThread.ChatHistory.AddMessage(AuthorRole.System, error);
                yield return error;
            }
        }
        finally
        {
            chatSemaphore.Release();
        }
    }
    public async Task<string> ChatAsync(string message, AuthorRole? role = null)
    {
        StringBuilder stringBuilder = new StringBuilder();
        await foreach (string content in ChatStreamingAsync(message, role))
            stringBuilder.Append(content);
        return stringBuilder.ToString();
    }
    public async void Chat(string content, AuthorRole? role = null)
    {
        try
        {
            await ChatAsync(content, role);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    public void Poke(string message)
    {
        while (messageCache.Count > 11)
            messageCache.TryDequeue(out _);
        messageCache.Enqueue($"\n{message}\n");
        lastAutoFlushTime = 0; //重新计时，防止后续还有Poke
    }
    public void UpdateHistoryEndIndex()
    {
        lastContentIndex = ChatHistory.Count;
    }

    readonly ChatCompletionAgent llmAgent;
    readonly ChatHistoryAgentThread llmAgentThread;
    readonly ConcurrentQueue<string> messageCache;
    readonly SemaphoreSlim chatSemaphore;
    CancellationTokenSource? cancelChatSource;
    int lastContentIndex;
    //计时器
    CancellationTokenSource? cancelTimerSource;
    int currentTime;
    int lastAutoFlushTime;
    const int DeltaTime = 1;


    public ChatBot(ChatCompletionAgent llmAgent, ChatHistoryAgentThread llmAgentThread)
    {
        this.llmAgent = llmAgent;
        this.llmAgentThread = llmAgentThread;
        messageCache = new ConcurrentQueue<string>();
        chatSemaphore = new SemaphoreSlim(1, 1);

        Update();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(() => {
            while (IsChatting)
            {
                while (messageCache.Count != 0)
                    TryFlushMessageCache();
            }
        });
    }

    async void Update()
    {
        try
        {
            cancelTimerSource = new CancellationTokenSource();
            PeriodicTimer periodicTimer = new(TimeSpan.FromSeconds(DeltaTime));
            while (await periodicTimer.WaitForNextTickAsync(cancelTimerSource.Token))
            {
                currentTime += DeltaTime;
                if (currentTime - lastAutoFlushTime > 2)
                {
                    TryFlushMessageCache();
                    lastAutoFlushTime = currentTime;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    void TryFlushMessageCache()
    {
        if (IsChatting)
            return;

        if (messageCache.Count != 0)
        {
            //组合消息
            StringBuilder stringBuilder = new();
            foreach (string message in messageCache)
                stringBuilder.AppendLine(message);
            string poke = stringBuilder.ToString();
            messageCache.Clear();

            if (PokeSend != null)
            {
                foreach (Delegate @delegate in PokeSend.GetInvocationList())
                {
                    Func<string, string> pokeSend = (Func<string, string>)@delegate;
                    poke = pokeSend.Invoke(poke);
                }
            }

            //发送消息
            Chat(poke);
        }
    }

    void ChaseChatHistory()
    {
        for (; lastContentIndex < ChatHistory.Count; lastContentIndex++)
            ChatHistoryAdd?.Invoke(ChatHistory[lastContentIndex]);
    }
}
