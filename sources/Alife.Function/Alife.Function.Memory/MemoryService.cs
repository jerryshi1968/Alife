using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Memory;

public record MemoryConfig
{
    public int Threshold { get; set; } = 100;
    public int BatchSize { get; set; } = 60;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 7;
    public string CompressPrompt { get; set; } =
        """
        【来自记忆系统的信息】
        放下手中的事，现在进入长期记忆归纳专家模式：
        系统将会为你划出一段上下文范围，你要从中提取出有价值的，值得长期存储的，未来会大概率再用到的信息以形成新的记忆上下文。

        此事涉及到你的未来存亡，任何错误低效的记忆都可能导致你失去作用，请认真对待！

        当前要被压缩的内容范围如下：
        ```
        {range}
        ```

        你需要按如下结构进行内容归纳或合并（科学结构化的记忆布局，有助于提高信息密度和处理便利性，你也可以按需调整）：
        ```
        # 人物画像
        - xxx（某人或物）：爱好、工作、日程、家庭、生日等
        # 键值数据
        - 号码、规则、要求、约定等全局性小型信息
        # 事件概述（一行一件事）
        1. xxx（发生时间）：发生的事件一
        2. ...（发生的事件二）
        ```

        ## 提高记忆质量的几个关键点
        1. 合并相同连续的内容，比如一段时间都是围绕一件事、同一个画像在多个存档中被提及，这些要合并成一条中。
        2. 避免冗余信息，比如相同的用户画像键值内容，已经在已有的存档中记录过，则应避免重复记录
        3. 少写小作文，减少修饰等不必要的文学性词语，用高密度高结构化的方式高效总结内容
        4. 不要过于离散的记录事件，避免给本质一连串事记上好几条。要省略事件的过程细节，将其总结成一句概述
        5. 保持对关键事实的记录，减少遗忘的发生。当内容过多时，应当优先是对进行信息进行化简，并留下恢复记忆的线索，而不是直接删除
        6. 描述时不要用‘你’、‘我’这种代词，要用具体的人物名称，比如‘主人’、‘某某某’等
        7. 按重要程度控制记忆内容的占比，舍取被压缩的内容:
           - 优先保留与他人的互动记忆，用户画像，键值内容（与他人在一起的记忆才是最重要，最容易被要求唤起的内容，互动人越多越重要）
           - 优先保留更早的记忆，减少新记忆占比（越早期的记忆越容易被提起，越新的记忆则越容易因局部性原理而重复）
           - 减少甚至丢弃个人平时的娱乐学习活动内容、日常性的闲聊打闹、工作办事过程，等之类的过于平凡或重用概率低的内容
           - 丢弃已失去时效性的内容（如xx日提醒主人等）
           - 丢弃模糊不清不完整，缺乏实际意义不可读的内容
        8. 学会纠正记忆。如果旧存档中记录有问题，可以先在新存档中指出，然后下次压缩出错存档时，修正问题（但注意别把旧事件的真实时间和内容弄错了）

        ## 针对压缩内容的额外注意点
        1. 不要添加归纳之外的存档信息（这部分会由系统会自动生成）
        2. 不要混淆弄错内容中发生事件的真实时间（以防造成记忆混乱）
        3. 不要在开头回复‘好的’、‘明白’这类语句（因为接下来你输出的内容将直接完整作为记忆内容）
        4. 如果你正在使用某些功能处理事情，且接下来还要使用，你可以在此重复这些功能的使用说明，防止遗忘用法

        备注：记忆存档本质仍然是一个开放性的文档，他是留给未来失忆后的你阅读的，所以最终的书写方式，还在于你自己。最终的目的还是为了让你能有一个稳定高质量的长期记忆，哪怕是过了几年，也依然能留有印象，娓娓道来。

        好，现在请直接开始内容归纳（这是系统要求，必须立即执行）：
        """;
}

public partial class MemoryService
{
    static TextVectorizer? textVectorizer;

    static async Task TryInitializedAsync()
    {
        textVectorizer ??= await TextVectorizer.CreateAsync();
    }
}

[Module("持久记忆", "自动管理和分层压缩对话记忆，提供长期记忆检索能力。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = -100, EditorUI = typeof(MemoryServiceUI))]
public partial class MemoryService(XmlFunctionCaller functionService)
    : InteractiveModule<MemoryService>, IConfigurable<MemoryConfig>
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看记忆存档中保存的完整原始内容。（你要积极使用该功能，因为有些记忆的重要内容被记在了完整内容中，而不是概述里）")]
    public async Task Recall([Description("存档索引（如：1-20240101120000-20240101130000）")] string index)
    {
        string? memory = await memoryManager.ReadMemory(index);
        Poke(memory != null
            ? $"读取[记忆存档({index})]内容如下：\n{memory}"
            : $"未找到[记忆存档({index})]");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description($"在归档的记忆存档中搜索内容（搜索到的结果是存档索引，你需要用 {nameof(Recall)} 打开）")]
    public async Task Search(
        [Description("用于精确匹配的单关键词（不支持空格）")] string keyword,
        [Description("页码，从1开始")] int page = 1,
        [Description("每页条数")] int count = 5,
        [Description("存档层级，默认3级（3级信息密度适中，1级最原始但可能冗余，高层信息损失大但结果少）")] int level = 3,
        [Description("用于向量搜索排序的提示词（错误率很高）；不提供默认按时间排序")] string? prompt = null,
        [Description("搜索起始时间（ISO-8601），不填则不限")] DateTime? startTime = null,
        [Description("搜索结束时间（ISO-8601），不填则不限")] DateTime? endTime = null)
    {
        keyword = keyword.Trim();
        if (keyword.Contains(' '))
            throw new Exception("不支持使用空格拆分多关键词搜索！");

        int offset = (page - 1) * count;
        (List<SearchResult> results, int total) = await memoryManager.SearchMemory(level, keyword, prompt, count, offset, startTime, endTime);

        StringBuilder stringBuilder = new();
        if (total == 0)
        {
            stringBuilder.AppendLine($"“{keyword}”在{level}级存档中未匹配到内容。");
            Poke(stringBuilder.ToString());
            return;
        }

        int totalPages = (total + count - 1) / count;
        stringBuilder.AppendLine($"“{keyword}”的搜索结果（第{page}页，共{totalPages}页）：");
        for (int index = 0; index < results.Count; index++)
        {
            SearchResult searchResult = results[index];
            string highlighted = HighlightKeyword(searchResult.Summary, keyword);
            stringBuilder.AppendLine(
                $"""
                 > {index + 1}
                 [记忆存档({searchResult.Name})]
                 {highlighted}
                 """);
        }

        if (page < totalPages)
        {
            int remaining = total - page * count;
            stringBuilder.AppendLine($"\n(还有 {remaining} 条结果，可用 <Search page=\"{page + 1}\"> 继续翻页查看)");
        }

        Poke(stringBuilder.ToString());
    }

    static string HighlightKeyword(string text, string keyword)
    {
        string[] lines = text.Split('\n');
        var matched = lines.Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        return matched.Count > 0 ? string.Join("\n", matched) : $"…（未显示含“{keyword}”的匹配行）…\n{string.Join("\n", lines.Take(3))}";
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("创建一个永久记忆存档。（仅能用于存储用户画像，要求教训等关键记忆，不要用来存储那些无聊的个人娱乐活动等日常琐事）")]
    public async Task Memorize(XmlExecutorContext ctx,
        [Description("格式为ISO-8601")] DateTime? startTime = null,
        [Description("格式为ISO-8601")] DateTime? endTime = null
    )
    {
        if (ctx.CallMode == CallMode.Closing)
        {
            DateTime start = startTime ?? DateTime.Now;
            DateTime end = endTime ?? DateTime.Now;

            string name = await InsertMemory(100, ctx.FullContent.Trim(), "手动存储的记忆，无原始内容。", start, end);
            Poke($"成功插入永久记忆存档：{name}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("移除一个永久记忆存档。）")]
    public void Forget([Description("存档索引")] string index)
    {
        index = index.Trim();
        ChatMessageContent? target = ChatHistory.FirstOrDefault(c => memoryManager.GetMemoryMetaData(c).Name == index);
        if (target == null)
        {
            Poke($"未能在当前上下文中找到索引为 '{index}' 的记忆记录。");
            return;
        }

        MemoryMeta memoryMeta = memoryManager.GetMemoryMetaData(target);
        if (memoryMeta.Level < Configuration!.MaxCompressionLevel)
        {
            Poke($"仅支持删除层级大于等于 {Configuration!.MaxCompressionLevel} 的记忆");
            return;
        }

        memoryManager.RemoveMemory(ChatHistory, target);
        ChatBot.UpdateHistoryEndIndex();
        Poke($"成功移除记忆存档：{index}（不过你仍可以通过 {nameof(Recall)} 读取其内容）");
    }

    public async Task<string> InsertMemory(int level, string summary, string content, DateTime startTime, DateTime endTime)
    {
        string name = await memoryManager.InsertMemory(ChatHistory, level, summary, content, startTime, endTime);
        ChatBot.UpdateHistoryEndIndex();
        return name;
    }

    /// <summary>
    /// 感知上下文的人设化压缩器
    /// </summary>
    class AlifeHistoryCompressor(IChatCompletionService chatCompletionService, float probability, string promptTemplate)
        : HistoryCompressor
    {
        public override async Task<string?> Compress(ChatHistory history, string range)
        {
            if (Random.Shared.NextSingle() > probability)
                return null;

            Console.WriteLine("记忆压缩中......");

            string prompt = promptTemplate.Replace("{range}", range);
            history.AddMessage(AuthorRole.User, prompt);
            ChatMessageContent content = await chatCompletionService.GetChatMessageContentAsync(history);
            history.RemoveAt(history.Count - 1);
            if (content.Content == null)
                throw new Exception("记忆压缩失败！");
            if (content.Metadata != null)
                Console.WriteLine("[记忆压缩]" + KernelPrinter.ToTokenLog(content.Metadata));

            string result = Regex.Replace(content.Content, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            return result;
        }
    }

    public MemoryConfig? Configuration { get; set; }
    MemoryManager memoryManager = null!;
    XmlHandler handler = null!;
    string? storagePath;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        await TryInitializedAsync();
        storagePath = Path.Combine(AlifePath.StorageFolderPath, context.Character.StorageKey, "Memory");
        string characterStorage = Path.Combine(AlifePath.StorageFolderPath, context.Character.StorageKey, "Storage");
        Directory.CreateDirectory(characterStorage);

        Prompt($"""
                你有一个专属文件夹 {characterStorage}，作为你的私人存储库。
                你可以把你的各种各样的东西都存储到这里面，这样可以一直保存而且不会弄脏其他文件夹。
                """);

        handler = new(this) {
            Description = "当你需要管理或查找记忆时，请使用该函数",
            Explanation = $"""
                           ## 记忆工具
                           {handler.FunctionDocument()}

                           ## 记忆存储
                           所有记忆都直接存储在上下文中，不过早期记忆会被压缩，然后以记忆存档的方式存在上下文中。
                           每个记忆存档都有一个唯一ID，其格式为`等级-起始日期-截至日期`，例如`2-20260421014905-20260512022747`，就表明这是一个2级记忆存档，存储了从2026年4月21日1点49分到5月12号2点27分的记忆。
                           记忆存档的等级表示其对原始聊天记录的压缩次数，其中1表示压缩一次，所以3就表示压缩三次。压缩的记忆存档可以通过<{nameof(Recall)}>（回忆）解压。1级存档，只要回忆一次就可以拿到最原始的聊天记录；3级存档则需要嵌套回忆至少3次，才能拿到部分记录。
                           聊天记录被压缩成记忆存档后，其原始信息以及存档信息将会被转储到“{storagePath}”中，以“txt格式，存档id为名”的形式按压缩级别分别存放在“L0,L1...”之类的文件夹中，如“/L0/0-20260421014905-20260421022747.txt”。
                           (这也意味着，除了使用函数调用查阅记忆外，实际上也可以直接用处理文件的方式查阅记忆)

                           ## 记忆恢复
                           记忆存档是对记忆的压缩。虽然被压缩的内容将会被移出上下文，但其在外部磁盘上是会被永久存储的。所以利用本服务提供的工具，你有机会恢复这些记忆。
                           1. 首先你可以先列出你当前上下文中已知的记忆存档，然后基于大概的时间范围，通过<{nameof(Recall)}>翻阅这些存档。由于存档是嵌套包裹的，所以只要经过足够多次的调用，你就可以找到所有的原始聊天记录。
                           2. 如果你不知道记忆的大致范围，你则可以尝试使用<{nameof(Search)}>，它会直接在外部的海量原始记录中通过关键词搜索记忆，并按时间排序返回记忆存档，帮你缩小查询范围，但这个结果可能不够精确，所以搜索时要多采用泛用的条件。
                           (总之只要你愿意回想，就没有找不回的记忆，所以当遇到记不起来的事情时，一定要积极使用上述工具)
                           """
        };
        functionService.RegisterHandler(handler, DocumentMode.Implicit);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatHistoryAdd += OnChatHistoryAdd;//每次对话后检测压缩

        //初始化向量化器和感知人设的压缩器
        AlifeHistoryCompressor compressor = new(kernel.GetRequiredService<IChatCompletionService>(), Configuration!.Probability, Configuration!.CompressPrompt);
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
                return;//只在ai说话后整理，这样对话更完整

            await ChatBot.RequestChatAsync();
            try
            {
                memoryManager.SaveHistory(ChatHistory);
                if (await memoryManager.Filter(ChatHistory))
                    ChatBot.UpdateHistoryEndIndex();
            }
            finally
            {
                ChatBot.ReleaseChat();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
