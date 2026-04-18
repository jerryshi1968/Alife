using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Alife.Basic;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Implement;

public record MemoryConfig
{
    public int Threshold { get; set; } = 32;
    public int BatchSize { get; set; } = 16;
}
[Plugin("记忆服务", "自动管理和分层压缩对话记忆，提供长期记忆检索能力。", LaunchOrder = -100)]
public class MemoryService : Plugin, IConfigurable<MemoryConfig>
{
    [XmlFunction]
    [Description("读取记忆存档的完整内容。")]
    public async Task Recall(XmlExecutorContext ctx, [Description("内容索引（如：0-20240101120000-20240101130000）")] string index)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;

        string? memory = await memoryManager.ReadMemory(index);
        chatBot.Poke(memory != null
            ? $"[{nameof(MemoryService)}] 读取完整记忆如下：\n{memory}"
            : $"[{nameof(MemoryService)}] 未找到记忆记录");
    }
    [XmlFunction]
    [Description($"在归档的记忆记录中搜索内容（搜索到的结果是存储索引，你需要用 {nameof(Recall)} 打开）。")]
    public async Task Search(XmlExecutorContext ctx,
        [Description("搜索的问题")] string query,
        [Description("可选，格式为yyyy-MM-dd")] string? startTime = null,
        [Description("可选，格式为yyyy-MM-dd")] string? endTime = null,
        [Description("可选，搜索条数")] int count = 5)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;

        query = query.Trim();
        bool hasStartTime = DateTime.TryParse(startTime, out DateTime start);
        bool hasEndTime = DateTime.TryParse(endTime, out DateTime end);
        if (hasEndTime)
            end += TimeSpan.FromDays(1); //包含当前天

        List<SearchResult> results = await memoryManager.SearchMemory(query, count, hasStartTime ? start : null, hasEndTime ? end : null);

        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"[{nameof(MemoryService)}] “{query}”的搜索结果如下：");
        for (int index = 0; index < results.Count; index++)
        {
            SearchResult searchResult = results[index];
            stringBuilder.AppendLine(
                $"""
                 > {index + 1}
                 匹配度：{searchResult.Score}
                 发生时间：{searchResult.StartTime}到{searchResult.EndTime}
                 完整内容索引：{searchResult.Name}
                 事件概述：```{searchResult.Summary}```
                 """);
        }
        chatBot.Poke(stringBuilder.ToString());
    }

    MemoryManager memoryManager = null!;
    ChatBot chatBot = null!;
    ChatHistory chatHistory = null!;
    MemoryConfig config = null!;

    public void Configure(MemoryConfig configuration)
    {
        config = configuration;
    }

    public MemoryService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        context.contextBuilder.ChatHistory.AddSystemMessage(
            $"""
             [{nameof(MemoryService)}] 上下文压缩功能说明

             有时你会收到关于上下文压缩的提示，它会给予你一段过往时间的聊天记录或记忆存档。这些内容是即将移出上下文的内容，所以需要你用第一人称简述一下发生的事情，方便日后回忆。

             注意！描述事情时，你要遵守如下规则：
             1. 按重要程度进行信息舍取，注意简洁。
             2. 多事件时注意按时间段区分。
             3. 保持对一些关键数据的记录。
             4. 不要记录系统信息，直接口语化描述。
             5. 分清事件中的具体人物，不要用‘你’这种代词。
             6. 不要回复无关事件描述的内容，如不要开头回复‘好的’。
             """);

        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatHistory = chatBot.ChatHistory;

        //每次对话后检测压缩
        chatBot.ChatHistoryAdd += OnChatHistoryAdd;

        //初始化向量化器和感知人设的压缩器
        TextVectorizer vectorizer = new(AlifePath.ModelsFolderPath);
        AlifeTextCompressor compressor = new(kernel.GetRequiredService<IChatCompletionService>(), chatHistory);
        string storagePath = Path.Combine(AlifePath.StorageFolderPath, "Memories", chatActivity.Character.ID);
        memoryManager = new MemoryManager(compressor, vectorizer, storagePath, config.Threshold, config.BatchSize);

        //加载历史记忆
        memoryManager.LoadHistory(chatHistory);

        return Task.CompletedTask;
    }

    async void OnChatHistoryAdd(ChatMessageContent content)
    {
        try
        {
            if (content.Role != AuthorRole.Assistant)
                return; //只在ai说话后整理，这样对话更完整

            await chatBot.ChatSemaphore.WaitAsync();
            memoryManager.SaveHistory(chatHistory);
            if (await memoryManager.Filter(chatHistory))
                chatBot.UpdateHistoryEndIndex();
            chatBot.ChatSemaphore.Release();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    /// <summary>
    /// 感知上下文的人设化压缩器
    /// </summary>
    class AlifeTextCompressor(IChatCompletionService chatCompletionService, ChatHistory history) : TextCompressor
    {
        public override async Task<string> Compress(string text)
        {
            history.AddMessage(AuthorRole.User,
                $"""
                 [{nameof(MemoryService)}] 触发上下文压缩了，压缩内容如下：
                 ```
                 {text}
                 ```
                 现在直接开始概述上述内容描述的事情。
                 """);
            ChatMessageContent content = await chatCompletionService.GetChatMessageContentAsync(history);
            history.RemoveAt(history.Count - 1);
            if (content.Content == null)
                throw new Exception("记忆压缩失败！");

            string result = Regex.Replace(content.Content, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            return result;
        }
    }
}
