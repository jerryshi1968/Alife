using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Memory;

public record SearchResult(int Level, string Name, string Text, DateTimeOffset StartTime, DateTimeOffset EndTime, float Score);

/// <summary>
/// 向量记忆存储容器（带物理分离设计）。
/// 文本内容作为真实文件存储在硬盘树中，便于直接管理/浏览。
/// 文本向量、检索标引等元数据则存放到 DuckDB 中。
/// 利用 DuckDB 原生强大的单文件分析性能及 array_cosine_similarity()，无需插件即可执行数百万级的极速相似度搜索并与标量过滤联动。
/// </summary>
public class MemoryStore
{
    public MemoryStore(string rootPath, ITextVectorizer vectorizer)
    {
        this.rootPath = rootPath;
        this.vectorizer = vectorizer;
        dbPath = Path.Combine(rootPath, "vector_index.duckdb");
        InitializeDatabase();

        void InitializeDatabase()
        {
            if (!Directory.Exists(rootPath))
                Directory.CreateDirectory(rootPath);

            // 使用极速的本地分析型数据库 DuckDB
            using DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}");
            connection.Open();
            using DuckDBCommand command = connection.CreateCommand();
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS MemoryVectors (
                Level INTEGER,
                Name  VARCHAR,
                StartTime BIGINT,
                EndTime BIGINT,
                Vector FLOAT[512],
                PRIMARY KEY(Level, Name)
            );
            CREATE INDEX IF NOT EXISTS idx_level_time ON MemoryVectors(Level DESC, EndTime DESC);
        ";
            command.ExecuteNonQuery();
        }
    }


    /// <summary>
    /// 功能1：将文本独立存为文件，并解析向量放入数据库（附带时间范围）
    /// </summary>
    public async Task SaveAsync(int level, string name, string text, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        // 1. 文本内容实际落盘为文件：L{Level}/{Name}.json
        string dir = Path.Combine(rootPath, $"L{level}");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, $"{name}.json");
        await File.WriteAllTextAsync(filePath, text);

        // 2. 解析为向量
        float[] vector = await vectorizer.VectorizeAsync(text);

        // 3. 将标引数据更新到数据库
        await using DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}");
        connection.Open();

        // 由于需要使用数组直接合并到 SQL 中以最稳妥执行插入：
        string vectorLiteral = "[" + string.Join(",", vector.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";

        await using DuckDBCommand command = connection.CreateCommand();
        // DuckDB 官方原生支持直接通过 ON CONFLICT DO UPDATE
        command.CommandText = $@"
            INSERT INTO MemoryVectors (Level, Name, StartTime, EndTime, Vector)
            VALUES ($Level, $Name, $StartTime, $EndTime, {vectorLiteral})
            ON CONFLICT (Level, Name) DO UPDATE SET 
            StartTime = excluded.StartTime, 
            EndTime = excluded.EndTime, 
            Vector = excluded.Vector;
        ";
        command.Parameters.Add(new DuckDBParameter("Level", level));
        command.Parameters.Add(new DuckDBParameter("Name", name));
        command.Parameters.Add(new DuckDBParameter("StartTime", startTime.ToUnixTimeMilliseconds()));
        command.Parameters.Add(new DuckDBParameter("EndTime", endTime.ToUnixTimeMilliseconds()));

        // 因为 DuckDB.NET 对数组类型的 Parameter Binding 偶尔需要严格驱动匹配，
        // 将向量序列化直接写入 SQL 语句能兼顾极致轻量和防错，对插入性能影响在这种场景下忽略不计
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 功能2：根据层级与名称读取出硬盘上的源文本
    /// </summary>
    public async Task<string?> LoadAsync(int level, string name)
    {
        string filePath = Path.Combine(rootPath, $"L{level}", $"{name}.json");
        if (!File.Exists(filePath))
            return null;

        string json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<string>(json);
    }

    /// <summary>
    /// 功能3：原生的 DuckDB 侧全库综合高能搜索。直接下推余弦计算并依靠索引剪枝！
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5, DateTimeOffset? minTime = null, DateTimeOffset? maxTime = null)
    {
        float[] queryVector = await vectorizer.VectorizeAsync(query);
        string vectorLiteral = "[" + string.Join(",", queryVector.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";

        List<(int Level, string Name, DateTimeOffset Start, DateTimeOffset End, float Score)> matches = new();

        await using (DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            await using DuckDBCommand command = connection.CreateCommand();

            // 下方是纯正的霸王级分析型 SQL。由于 DuckDB C++ 引擎对这种单机查询优化极猛
            // 把 array_cosine_similarity 在 SQL 里算，连内存都不用来回倒了，比 C# 本地迭代要快几十倍。
            command.CommandText = $@"
                SELECT Level, Name, StartTime, EndTime, array_cosine_similarity(Vector, {vectorLiteral}::FLOAT[512]) as Score
                FROM MemoryVectors 
                WHERE ($Min IS NULL OR EndTime >= $Min) 
                  AND ($Max IS NULL OR StartTime <= $Max)
                ORDER BY Score DESC, Level DESC, EndTime DESC
                LIMIT {topK}
            ";

            command.Parameters.Add(new DuckDBParameter("Min", minTime.HasValue ? minTime.Value.ToUnixTimeMilliseconds() : DBNull.Value));
            command.Parameters.Add(new DuckDBParameter("Max", maxTime.HasValue ? maxTime.Value.ToUnixTimeMilliseconds() : DBNull.Value));

            await using DuckDBDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int level = reader.GetInt32(0);
                string name = reader.GetString(1);
                long startMs = reader.GetInt64(2);
                long endMs = reader.GetInt64(3);
                float score = reader.GetFloat(4);

                matches.Add((level, name, DateTimeOffset.FromUnixTimeMilliseconds(startMs), DateTimeOffset.FromUnixTimeMilliseconds(endMs), score));
            }
        }

        // 以 DuckDB 查出的排序为主：SQL内部已使用了 ORDER BY Score DESC, Level DESC, EndTime DESC。不再做 C# 强行重排导致高分结果被抹杀。
        List<(int Level, string Name, DateTimeOffset Start, DateTimeOffset End, float Score)> topMatches = matches;

        List<SearchResult> results = new List<SearchResult>();
        foreach ((int Level, string Name, DateTimeOffset Start, DateTimeOffset End, float Score) match in topMatches)
        {
            string? text = await LoadAsync(match.Level, match.Name);
            if (text != null)
            {
                results.Add(new SearchResult(match.Level, match.Name, text, match.Start, match.End, match.Score));
            }
        }

        return results;
    }

    readonly string rootPath;
    readonly string dbPath;
    readonly ITextVectorizer vectorizer;
}
