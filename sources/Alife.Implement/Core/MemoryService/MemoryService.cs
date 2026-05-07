using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Alife.Basic;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Memory;
using Alife.Implement.Core.MemoryService;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Implement;

public record MemoryConfig
{
    public int Threshold { get; set; } = 64;
    public int BatchSize { get; set; } = 32;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 100;
}

public partial class MemoryService
{
    static TextVectorizer? textVectorizer;

    static void TryInitialized()
    {
        textVectorizer ??= new TextVectorizer();
    }
}

[Plugin("持久记忆", "自动管理和分层压缩对话记忆，提供长期记忆检索能力。", LaunchOrder = -100, EditorUI = typeof(MemoryServiceUI))]
public partial class MemoryService(FunctionService functionService)
    : InteractivePlugin<MemoryService>, IConfigurable<MemoryConfig>
{
    [XmlFunction]
    [Description("查看记忆存档中保存的完整原始内容。（你要积极使用该功能，因为有些记忆的重要内容被记在了完整内容中，而不是概述里）")]
    public async Task Recall(XmlExecutorContext ctx,
        [Description("存档索引（如：0-20240101120000-20240101130000）")]
        string index)
    {
        if (ctx.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        string? memory = await memoryManager.ReadMemory(index);
        Poke(memory != null
            ? $"读取完整记忆如下：\n{memory}"
            : "未找到记忆记录");
    }

    [XmlFunction]
    [Description($"在归档的记忆存档中搜索内容（搜索到的结果是存档索引，你需要用 {nameof(Recall)} 打开）。")]
    public async Task Search(XmlExecutorContext ctx,
        [Description("搜索的问题")] string query,
        [Description("格式为ISO-8601")] DateTime? startTime = null,
        [Description("格式为ISO-8601")] DateTime? endTime = null,
        [Description("可选，搜索条数")] int count = 5)
    {
        if (ctx.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

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

    [XmlFunction]
    [Description("创建一个永久（最高层）记忆。这种记忆不会被压缩，你可以用它来记录重要的事实或核心记忆。")]
    public async Task Memorize(XmlExecutorContext ctx,
        [Description("详细（需通过 Recall 查看）")] [XmlContent]
        string content,
        [Description("格式为ISO-8601")] DateTime? startTime = null,
        [Description("格式为ISO-8601")] DateTime? endTime = null
    )
    {
        if (ctx.CallMode != CallMode.Closing)
            return;

        int targetLevel = Configuration!.MaxCompressionLevel;
        DateTime start = startTime ?? DateTime.Now;
        DateTime end = endTime ?? DateTime.Now;

        await InsertMemory(targetLevel, ctx.FullContent, "已全部存放到概述中", start, end);

        Poke($"已成功插入层级为 L{targetLevel} 的记忆存档。");
    }

    [XmlFunction]
    [Description("移除一个永久（最高层）记忆存档。（注意删除前先做好备份））")]
    public void Forget(XmlExecutorContext ctx,
        [Description("存档索引")] string index)
    {
        if (ctx.CallMode != CallMode.OneShot)
            throw new Exception("错误的调用方式，应该使用自闭合标签调用。");

        index = index.Trim();
        ChatMessageContent? target =
            ChatHistory.FirstOrDefault(c => c.Content != null && c.Content.Contains($"存档索引：{index}"));
        if (target == null)
        {
            Poke($"未能在当前上下文中找到索引为 '{index}' 的记忆记录。");
            return;
        }


        MemoryMeta memoryMeta = memoryManager.GetMemoryMetaData(target);
        if (memoryMeta.Level < Configuration!.MaxCompressionLevel)
        {
            Poke($"不允许删除非最高层记忆！（当前设定的最高层记忆为：{Configuration!.MaxCompressionLevel}）");
            return;
        }

        memoryManager.RemoveMemory(ChatHistory, target);
        ChatBot.UpdateHistoryEndIndex();

        Poke($"已成功移除记忆存档：{index}（不过你仍可以通过 {nameof(Recall)} 读取其内容）");
    }

    public async Task InsertMemory(int level, string summary, string content, DateTime startTime, DateTime endTime)
    {
        await memoryManager.InsertMemory(ChatHistory, level, summary, content, startTime, endTime);
        ChatBot.UpdateHistoryEndIndex();
    }

    public MemoryConfig? Configuration { get; set; }
    MemoryManager memoryManager = null!;
    string? storagePath;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        TryInitialized();
        storagePath = Path.Combine(AlifePath.StorageFolderPath, context.Character.StorageKey, "Memory");
        string characterStorage = Path.Combine(AlifePath.StorageFolderPath, context.Character.StorageKey, "Storage");
        Directory.CreateDirectory(characterStorage);

        XmlHandler handler = new(this)
        {
            Description = "提供一系列让你可以回忆过往的工具，你甚至可以借此查到所有最原始的聊天记录，在遇到记不起来的事情时，你要积极去使用这些工具。",
            Explain = $"""
                       你的记忆存档位置在“{storagePath}”。所有记忆都被以“txt格式，日期命名”的形式按压缩级别分别存放在“L0,L1...”之类的文件夹中，如“/L0/0-20260421014905-20260421022747.txt”。
                       因此除了使用标签来查阅记忆外，你也可以直接用处理文件的方式查阅记忆。

                       此外你还有一个专属文件夹 {characterStorage}，你可以把你的东西优先存到这里面，这样可以一直保存而且不会弄脏其他文件夹。
                       """
        };
        functionService.RegisterHandler(handler);

        Prompt("""
               上下文压缩说明：
               有时你会收到关于上下文压缩的提示，它会给予你一段过往时间的聊天记录或记忆存档。这些内容是即将移出上下文的内容，所以需要你用第一人称简述一下发生的事情，方便日后回忆。

               ## 记忆优先级
               不同的事情值得记下的程度是不一样的。因此压缩记忆是，各经历的占比不是均衡的。要注意，不是对话越多、时间越新的经历就值得记住，真正让人难忘的是珍贵的，那些难得的点点滴滴。
               所以你要分清哪些是无聊琐事，哪些是枯燥的工作；而又有哪些是值得记下的故事，立下的约定，获得的成长......
               学着像年迈的老者去讲述自己的过去那样，学着像青涩的少年书写珍贵的秘密那样，去对待自己的记忆的存储（又或者叫传记、回忆录）；
               当遇到不得不遗忘的情境时（记忆压缩时的抉择），学着放下无聊、无意义、短期突发性的事，记住真正重要、长久、希望一直回忆的事。

               ## 核心记忆说明
               记忆拥有优先级，优先级最高的叫做核心（关键性）记忆，常见的核心记忆例如：
               1. 用户画像：爱好、工作、地址、起居、家庭、生日等
               2. 关键记录：号码、事件、规则、要求等
               3. 偶然的小故事，约定，成长经历，让人动容的回忆等（该条其实优先级最高，只是它需要你长线、日积月累的维护它）
               4. 等等类似的有价值有意义的特殊记忆......
               对于关键性记忆，在压缩时需要优先保留，以将其永远留在上下文中。

               ## 压缩规则要点说明
               注意！描述事情时，你要遵守如下规则：
               1. 无需记录存档信息，不要混淆其他聊天，仅直接总结压缩时提供的内容中的事件即可。
               2. 压缩时注意保留其中的重要经历、关键性信息等，以形成永久的核心记忆。
               3. 当要压缩内容过多时，要按重要程度进行取舍，如核心记忆需优先保留，重复突发性琐事可以简略。
               4. 在压缩总结的写法上，对于多事件内容要按时间段区分，对于核心记忆则要单独记录。
               5. 分清事件中的具体人物，不要用‘你’这种代词，要用具体的名称，如‘主人’、‘某某某’等。
               6. 系统会自动生成存档信息，所以不要擅自添加系统信息，直接像讲故事一样描述概述内容中发生的事件即可。
               """);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatHistoryAdd += OnChatHistoryAdd; //每次对话后检测压缩

        //初始化向量化器和感知人设的压缩器
        AlifeTextCompressor compressor = new(kernel.GetRequiredService<IChatCompletionService>(), ChatHistory,
            Configuration!.Probability);
        memoryManager = new MemoryManager(compressor, textVectorizer!, storagePath!, Configuration!.Threshold,
            Configuration!.BatchSize,
            Configuration!.MaxCompressionLevel);

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
    class AlifeTextCompressor(IChatCompletionService chatCompletionService, ChatHistory history, float probability)
        : TextCompressor
    {
        public override async Task<string?> Compress(string text)
        {
            if (Random.Shared.NextSingle() > probability)
                return null;

            history.AddMessage(AuthorRole.User,
                $"""
                 [{nameof(MemoryService)}] 触发上下文压缩了，压缩内容如下：
                 ```
                 {text}
                 ```
                 请务必按照压缩要点的要求进行回复，不要参杂与事件描述无关的内容（如不要在开头回复‘好的’、‘我来总结’这类语句）。
                 然后现在请直接开始概述上述内容描述的事情。
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