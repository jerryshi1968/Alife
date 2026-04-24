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

public partial class MemoryService
{
    static readonly TextVectorizer TextVectorizer;
    static MemoryService()
    {
        TextVectorizer = new TextVectorizer();
    }
}
public record MemoryConfig
{
    public int Threshold { get; init; } = 40;
    public int BatchSize { get; init; } = 20;
}
[Plugin("记忆服务", "自动管理和分层压缩对话记忆，提供长期记忆检索能力。", LaunchOrder = -100)]
public partial class MemoryService : InteractivePlugin<MemoryService>, IConfigurable<MemoryConfig>
{
    [XmlFunction]
    [Description("读取记忆存档的完整内容。")]
    public async Task Recall(XmlExecutorContext ctx, [Description("内容索引（如：0-20240101120000-20240101130000）")] string index)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;

        string? memory = await memoryManager.ReadMemory(index);
        Poke(memory != null
            ? $"读取完整记忆如下：\n{memory}"
            : "未找到记忆记录");
    }
    [XmlFunction]
    [Description($"在归档的记忆记录中搜索内容（搜索到的结果是存储索引，你需要用 {nameof(Recall)} 打开）。")]
    public async Task Search(XmlExecutorContext ctx,
        [Description("搜索的问题")] string query,
        [Description("格式为ISO-8601")] DateTime? startTime = null,
        [Description("格式为ISO-8601")] DateTime? endTime = null,
        [Description("可选，搜索条数")] int count = 5)
    {
        if (ctx.CallMode != CallMode.OneShot)
            return;

        query = query.Trim();
        if (endTime != null)
            endTime += TimeSpan.FromDays(1); //包含当前天

        List<SearchResult> results = await memoryManager.SearchMemory(query, count, startTime, endTime);

        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine($"{query}”的搜索结果如下：");
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
        Poke(stringBuilder.ToString());
    }

    public MemoryConfig? Configuration { get; set; }
    MemoryManager memoryManager = null!;

    public MemoryService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }
    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        Prompt($"""
                上下文压缩说明：
                有时你会收到关于上下文压缩的提示，它会给予你一段过往时间的聊天记录或记忆存档。这些内容是即将移出上下文的内容，所以需要你用第一人称简述一下发生的事情，方便日后回忆。

                注意！描述事情时，你要遵守如下规则：
                1. 按重要程度进行信息舍取，注意简洁。
                2. 多事件时注意按时间段区分。
                3. 保持对一些关键数据的记录。
                4. 系统会自动生成存档信息，所以你不用负责添加系统信息，直接像讲故事一样描述概述内容即可。
                5. 分清事件中的具体人物，不要用‘你’这种代词。
                6. 不要回复无关事件描述的内容，如不要开头回复‘好的’。
                """);
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatHistoryAdd += OnChatHistoryAdd; //每次对话后检测压缩

        //初始化向量化器和感知人设的压缩器
        string storagePath = Path.Combine(AlifePath.StorageFolderPath, chatActivity.Character.StorageKey, "Memory");
        AlifeTextCompressor compressor = new(kernel.GetRequiredService<IChatCompletionService>(), ChatHistory);
        memoryManager = new MemoryManager(compressor, TextVectorizer, storagePath, Configuration!.Threshold, Configuration!.BatchSize);

        //加载历史记忆
        memoryManager.LoadHistory(ChatHistory);
    }

    async void OnChatHistoryAdd(ChatMessageContent content)
    {
        try
        {
            if (content.Role != AuthorRole.Assistant)
                return; //只在ai说话后整理，这样对话更完整

            await ChatBot.ChatSemaphore.WaitAsync();
            memoryManager.SaveHistory(ChatHistory);
            if (await memoryManager.Filter(ChatHistory))
                ChatBot.UpdateHistoryEndIndex();
            ChatBot.ChatSemaphore.Release();
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

                 压缩要点：
                 1. 无需记录存档信息，不要混淆其他聊天，仅直接描述上述内容中的事件即可。
                 2. 注意学会反复回忆重要经历，以及反复记忆关键性的内容，以形成核心记忆。
                 现在请直接开始概述上述内容描述的事情。
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
