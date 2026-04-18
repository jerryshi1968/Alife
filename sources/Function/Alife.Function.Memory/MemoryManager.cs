using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace Alife.Function.Memory;

/// <summary>
/// 记忆核心管理器。协调存储、索引和压缩逻辑。
/// 实现层级化索引关系：每个摘要记录都描述了其涵盖的对话范围和时间跨度。
/// </summary>
public class MemoryManager
{
    public MemoryManager(TextCompressor compressor, TextVectorizer vectorizer, string storagePath,
        int compressionThreshold, int compressionCount)
    {
        this.compressionThreshold = compressionThreshold;
        this.compressionCount = compressionCount;

        this.compressor = compressor;
        historyStoragePath = $"{storagePath}/History.json";
        memoryStorage = new MemoryStorage(storagePath, vectorizer);

        if (!Directory.Exists(storagePath))
            Directory.CreateDirectory(storagePath);
    }

    public async Task<bool> Filter(ChatHistory chatHistory)
    {
        //跳过系统提示词
        int contentIndex = 0;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            if (chatHistory[contentIndex].Role != AuthorRole.System)
                break;
        }

        //遍历每个层级的聊天记录
        int areaLevel = -1;
        int areaStart = -1;
        int areaCount = -1;
        int areaCompressionThreshold = -1;
        int areaCompressionCount = -1;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            ChatMessageContent currentContent = chatHistory[contentIndex];
            MemoryMeta currentMemoryMeta = GetMemoryMetaData(currentContent);

            int currentLevel = currentMemoryMeta.Level;
            if (areaLevel != currentLevel)
            {
                //进入一个区域
                areaLevel = currentLevel;
                areaStart = contentIndex;
                areaCount = 1;
                areaCompressionThreshold = areaLevel == 0 ? compressionThreshold : 4;
                areaCompressionCount = areaLevel == 0 ? compressionCount : 3;
            }
            else
            {
                areaCount++;
            }

            if (areaCount >= areaCompressionThreshold)
            {
                //压缩记忆
                DateTime startTime = GetMemoryMetaData(chatHistory[areaStart]).StartTime;
                DateTime endTime = currentMemoryMeta.EndTime;
                string content = PickContent(chatHistory, areaStart, areaStart + areaCompressionCount);
                string summary = await compressor.Compress(content);

                //提取并保存旧的记录
                string name = await SaveMemory(new MemoryMeta(areaLevel, startTime, endTime), summary, content);
                for (int index = areaStart + areaCompressionCount - 1; index >= areaStart; index--)
                    memoryMetaDatas.Remove(chatHistory[index]);
                chatHistory.RemoveRange(areaStart, areaCompressionCount);

                //增加新的记录
                summary = $"""
                              [记忆存档(L{areaLevel})]
                              完整内容索引：{name}
                              发生时间：{startTime}到{endTime}
                              事件概述：{summary}
                              """;
                ChatMessageContent compressedContent = new(AuthorRole.Assistant, summary);
                chatHistory.Insert(areaStart, compressedContent);
                memoryMetaDatas[compressedContent] = new MemoryMeta(areaLevel + 1, startTime, endTime);

                Console.WriteLine($"压缩记忆：{name}");

                return true;
            }
        }

        return false;
    }
    public void SaveHistory(ChatHistory chatHistory)
    {
        List<HistoryRecord> history = new List<HistoryRecord>();
        foreach (ChatMessageContent chatMessageContent in chatHistory.Where(content => content.Role != AuthorRole.System))
        {
            if (chatMessageContent.Content == null)
                continue;
            history.Add(new HistoryRecord(
                chatMessageContent.Role,
                chatMessageContent.Content,
                GetMemoryMetaData(chatMessageContent)
            ));
        }
        File.WriteAllText(historyStoragePath, JsonConvert.SerializeObject(history, Formatting.Indented));
    }
    public void LoadHistory(ChatHistory chatHistory)
    {
        if (File.Exists(historyStoragePath) == false)
            return;

        string historyJson = File.ReadAllText(historyStoragePath);
        List<HistoryRecord>? history = JsonConvert.DeserializeObject<List<HistoryRecord>>(historyJson);
        if (history == null)
            return;

        foreach (HistoryRecord historyRecord in history)
        {
            ChatMessageContent chatMessageContent = new(historyRecord.Role, historyRecord.Content);
            chatHistory.Add(chatMessageContent);
            memoryMetaDatas.Add(chatMessageContent, historyRecord.MemoryMeta);
        }
    }
    public Task<string?> ReadMemory(string index)
    {
        int level = int.Parse(index[..index.IndexOf('-')]);
        return memoryStorage.LoadAsync(level, index);
    }
    public async Task<List<SearchResult>> SearchMemory(string query, int count, DateTime? startTime, DateTime? endTime)
    {
        return await memoryStorage.SearchAsync(query, count, startTime, endTime);
    }

    record MemoryMeta(int Level, DateTime StartTime, DateTime EndTime);

    record HistoryRecord(AuthorRole Role, string Content, MemoryMeta MemoryMeta);

    readonly int compressionThreshold;
    readonly int compressionCount;
    readonly TextCompressor compressor;
    readonly MemoryStorage memoryStorage;
    readonly string historyStoragePath;
    readonly Dictionary<ChatMessageContent, MemoryMeta> memoryMetaDatas = new Dictionary<ChatMessageContent, MemoryMeta>();

    MemoryMeta GetMemoryMetaData(ChatMessageContent content)
    {
        if (memoryMetaDatas.TryGetValue(content, out MemoryMeta? data) == false)
        {
            data = new MemoryMeta(0, DateTime.Now, DateTime.Now);
            memoryMetaDatas.Add(content, data);
        }

        return data;
    }
    async Task<string> SaveMemory(MemoryMeta memoryMeta, string summary, string content)
    {
        string name = $"{memoryMeta.Level}-{memoryMeta.StartTime:yyyyMMddhhmmss}-{memoryMeta.EndTime:yyyyMMddhhmmss}";
        await memoryStorage.SaveAsync(name, memoryMeta.Level, summary, content, memoryMeta.StartTime, memoryMeta.EndTime);
        return name;
    }
    string PickContent(ChatHistory chatHistory, int start, int end)
    {
        StringBuilder stringBuilder = new();

        for (int index = start; index < end; index++)
        {
            ChatMessageContent content = chatHistory[index];
            stringBuilder.AppendLine($"【{content.Role}】：\n{content.Content}\n");
        }

        return stringBuilder.ToString();
    }
}
