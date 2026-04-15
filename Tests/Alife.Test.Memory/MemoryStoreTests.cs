using Alife.Basic;
using Alife.Function.Memory;


namespace Alife.Test.Memory;

[TestFixture]
public class MemoryStoreTests
{
    string tempDbDir = string.Empty;
    MemoryStore? store;

    [OneTimeSetUp]
    public void Setup()
    {
        tempDbDir = Path.Combine(Path.GetTempPath(), "AlifeTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDbDir);
        
        // 直接使用真实的向量化器（冷启动）
        var realVectorizer = new TextVectorizer(AlifePath.ModelsFolderPath);
        store = new MemoryStore(tempDbDir, realVectorizer);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        // DuckDB 文件占用解除需要垃圾回收等资源完全释放，稍微延时确保清空文件
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, true); } catch { }
    }

    [Test]
    public async Task Search_ShouldReturnSemanticMatches()
    {
        // Arrange - 插入两条语义完全不同的话
        var now = DateTimeOffset.Now;
        await store!.SaveAsync(1, "Memory_Food", "小明今天中午去食堂吃了一大碗牛肉面", now, now);
        await store!.SaveAsync(1, "Memory_Tech", "微软今天发布了最新的 AI 操作系统", now, now);

        // Act - 模糊搜索想吃饭的内容
        var results = await store!.SearchAsync("我饿了想找点宵夜或者吃的", topK: 2);

        // Assert - 确认拿到了结果，且"食物"那条的相似度必须排第一
        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Name, Is.EqualTo("Memory_Food"), "语义搜索失败，第一条不是食物记忆");
        
        // 分别输出打分，方便在控制台看到真实的 Cosine Similarity
        Console.WriteLine($"食物匹配得分: {results[0].Score}");
        Console.WriteLine($"科技匹配得分: {results[1].Score}");
        
        Assert.That(results[0].Score, Is.GreaterThan(results[1].Score), "食物的相关度得分应该远高于科技新闻");
    }

    [Test]
    public async Task Search_ShouldFilterByTime_AndOrderCorrectly()
    {
        // Arrange
        // 生成多个样本
        var now = DateTimeOffset.Now;
        
        // 老数据，Level 0
        await store!.SaveAsync(0, "Old_L0", "相同的文本", now.AddDays(-10), now.AddDays(-9));
        // 中等数据，Level 1 
        await store!.SaveAsync(1, "Mid_L1", "相同的文本", now.AddDays(-5), now.AddDays(-4));
        // 最新数据，Level 1
        await store!.SaveAsync(1, "New_L1", "相同的文本", now.AddDays(-1), now.AddDays(0));
        // 最新数据，Level 2 
        await store!.SaveAsync(2, "New_L2", "相同的文本", now.AddDays(-1), now.AddDays(0));

        // Act 1: 不限时间搜索，使用一样的搜索词触发完全相同的分数
        var results = await store!.SearchAsync("相同的文本", topK: 10);

        // Assert 1:
        Assert.That(results.Count, Is.EqualTo(4));
        // 如果相似度都极小（接近0），后续排序按照 Level >= Timestamp
        Assert.That(results[0].Name, Is.EqualTo("New_L2")); 
        Assert.That(results[1].Name, Is.EqualTo("New_L1")); 
        Assert.That(results[2].Name, Is.EqualTo("Mid_L1")); 
        Assert.That(results[3].Name, Is.EqualTo("Old_L0")); 

        // Act 2: 按时间限制进行搜索（排除最新的，只搜老和中等）
        var resultsFiltered = await store!.SearchAsync(
            "相同的文本", 
            topK: 10, 
            minTime: now.AddDays(-11), 
            maxTime: now.AddDays(-3)
        );

        // Assert 2:
        Assert.That(resultsFiltered.Count, Is.EqualTo(2)); // 应该只有 Mid 和 Old
        Assert.That(resultsFiltered[0].Name, Is.EqualTo("Mid_L1")); 
        Assert.That(resultsFiltered[1].Name, Is.EqualTo("Old_L0"));
    }
}
