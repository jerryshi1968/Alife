using DuckDB.NET.Data;

namespace Alife.Function.Memory;

public record SearchResult(string Name, int Level, string Summary, string Content, DateTimeOffset StartTime, DateTimeOffset EndTime, float Score);

/// <summary>
/// 向量记忆存储容器（带物理分离设计）。
/// 文本内容作为真实文件存储在硬盘树中，便于直接管理/浏览。
/// 文本向量、检索标引等元数据则存放到 DuckDB 中。
/// 利用 DuckDB 原生强大的单文件分析性能及 array_cosine_similarity()，无需插件即可执行数百万级的极速相似度搜索并与标量过滤联动。
/// </summary>
public class MemoryStorage
{
    public MemoryStorage(string rootPath, TextVectorizer vectorizer)
    {
        this.rootPath = rootPath;
        this.vectorizer = vectorizer;
        dbPath = Path.Combine(rootPath, "memory_index.duckdb");
        InitializeDatabase();

        void InitializeDatabase()
        {
            if (!Directory.Exists(rootPath))
                Directory.CreateDirectory(rootPath);

            using DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}");
            connection.Open();
            using DuckDBCommand command = connection.CreateCommand();

            // 加载全文搜索扩展
            command.CommandText = "INSTALL fts; LOAD fts;";
            command.ExecuteNonQuery();

            // 1. 尝试以 Name 为唯一主键创建表
            // 2. 动态增加可能缺失的字段（用于旧库升级）
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS MemoryStorage (
                Name VARCHAR PRIMARY KEY, 
                Level INTEGER,
                Summary VARCHAR,
                Content VARCHAR,
                StartTime BIGINT,
                EndTime BIGINT,
                Vector FLOAT[512]
            );
            CREATE INDEX IF NOT EXISTS idx_level_time ON MemoryStorage(Level DESC, EndTime DESC);
        ";
            command.ExecuteNonQuery();
        }
    }


    /// <summary>
    /// 功能1：将文本独立存为文件，并解析向量放入数据库（附带时间范围）
    /// </summary>
    public async Task SaveAsync(string name, int level, string summary, string content, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        await using DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}");
        connection.Open();

        //向量化概述，便于实现语义搜索
        float[] vector = await vectorizer.VectorizeAsync(summary);
        string vectorLiteral = "[" + string.Join(",", vector.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";

        await using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = $@"
            INSERT INTO MemoryStorage (Name, Level, Summary, Content, StartTime, EndTime, Vector)
            VALUES ($1, $2, $3, $4, $5, $6, {vectorLiteral})
        ";
        command.Parameters.Add(new DuckDBParameter(name));
        command.Parameters.Add(new DuckDBParameter(level));
        command.Parameters.Add(new DuckDBParameter(summary));
        command.Parameters.Add(new DuckDBParameter(content));
        command.Parameters.Add(new DuckDBParameter(startTime.ToUnixTimeMilliseconds()));
        command.Parameters.Add(new DuckDBParameter(endTime.ToUnixTimeMilliseconds()));
        command.ExecuteNonQuery();

        //额外保存一份文本文件
        {
            string dir = Path.Combine(rootPath, $"L{level}");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string filePath = Path.Combine(dir, $"{name}.txt");
            await File.WriteAllTextAsync(filePath,
                $"""
                 级别：{level}
                 时间：{startTime}到{endTime}
                 概述：{summary}
                 完整内容：
                 ```
                 {content}
                 ```
                 """);
        }
    }

    /// <summary>
    /// 功能2：根据层级与名称读取出硬盘上的源文本
    /// </summary>
    public async Task<string?> LoadAsync(int level, string name)
    {
        string filePath = Path.Combine(rootPath, $"L{level}", $"{name}.txt");
        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllTextAsync(filePath);
    }

    /// <summary>
    /// 功能3：原生的 DuckDB 侧全库综合高能搜索。直接下推余弦计算并依靠索引剪枝！
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5, DateTimeOffset? minTime = null, DateTimeOffset? maxTime = null)
    {
        await using DuckDBConnection connection = new DuckDBConnection($"Data Source={dbPath}");
        connection.Open();

        // 1. 每次搜索前重建全文索引（针对 Summary 列）
        await using (DuckDBCommand ftsCommand = connection.CreateCommand())
        {
            try
            {
                ftsCommand.CommandText = "PRAGMA drop_fts_index('MemoryStorage');";
                ftsCommand.ExecuteNonQuery();
            }
            catch
            {
                /* 忽略索引不存在的情况 */
            }

            ftsCommand.CommandText = "PRAGMA create_fts_index('MemoryStorage', 'Name', 'Summary');";
            ftsCommand.ExecuteNonQuery();
        }

        // 2. 执行混合搜索：向量余弦相似度 + 全文检索 BM25 评分
        float[] queryVector = await vectorizer.VectorizeAsync(query);
        string vectorLiteral = "[" + string.Join(",", queryVector.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        await using DuckDBCommand command = connection.CreateCommand();
        command.CommandText = $@"
                SELECT Name, Level, Summary, Content, StartTime, EndTime, 
                       (array_cosine_similarity(Vector, {vectorLiteral}::FLOAT[512]) + COALESCE(fts_main_MemoryStorage.match_bm25(Name, $3), 0.0)) as Score
                FROM MemoryStorage 
                WHERE ($1 IS NULL OR EndTime >= $1) 
                  AND ($2 IS NULL OR StartTime <= $2)
                ORDER BY Score DESC
                LIMIT {topK}
            ";

        object minVal = minTime.HasValue ? minTime.Value.ToUnixTimeMilliseconds() : DBNull.Value;
        object maxVal = maxTime.HasValue ? maxTime.Value.ToUnixTimeMilliseconds() : DBNull.Value;
        command.Parameters.Add(new DuckDBParameter(minVal));
        command.Parameters.Add(new DuckDBParameter(maxVal));
        command.Parameters.Add(new DuckDBParameter(query));

        await using DuckDBDataReader reader = command.ExecuteReader();

        List<SearchResult> results = new();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            int level = reader.GetInt32(1);
            string summary = reader.GetString(2);
            string content = reader.GetString(3);
            long startMs = reader.GetInt64(4);
            long endMs = reader.GetInt64(5);
            float score = reader.GetFloat(6);
            results.Add(new SearchResult(name, level, summary, content,
                DateTimeOffset.FromUnixTimeMilliseconds(startMs),
                DateTimeOffset.FromUnixTimeMilliseconds(endMs), score));
        }
        return results;
    }

    readonly string rootPath;
    readonly string dbPath;
    readonly TextVectorizer vectorizer;
}
