using Alife.Basic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Alife.Function.Vision;

/// <summary>
/// 使用 InternVL2.5-1B 进行图像理解。
/// 内部维护一个长驻 Python 子进程，模型只加载一次。
/// </summary>
public class VisionAnalyzer : IDisposable
{
    /// <summary>
    /// 模型生成的最大字长限制（Token 数量）。
    /// </summary>
    public int MaxResponseTokens { get; set; } = 100;

    public VisionAnalyzer(int timeoutSeconds = 120, Action<string>? onLog = null)
    {
        AlifeCommand.Command("pip", "install torch torchvision Pillow transformers timm einops --index-url https://download.pytorch.org/whl/cu121 --extra-index-url https://pypi.org/simple");

        const string ModelId = "OpenGVLab/InternVL2_5-1B";
        string modelPath = ModelDownloader.EnsureModel(ModelId);
        string script = Path.Combine(AppContext.BaseDirectory, "vision_bridge.py");
        string arguments = $"\"{script}\" --model_path \"{modelPath}\"";

        ProcessStartInfo psi = new() {
            FileName = "python",
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        process = Process.Start(psi)
                  ?? throw new InvalidOperationException("Failed to start Python vision bridge.");

        stdin = process.StandardInput;
        stdout = process.StandardOutput;

        if (onLog != null)
        {
            _ = Task.Run(async () => {
                StreamReader stderr = process.StandardError;
                char[] buffer = new char[256];
                try
                {
                    int read;
                    while ((read = await stderr.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        onLog(new string(buffer, 0, read));
                    }
                }
                catch
                {
                    // ignored
                }
            });
        }

        // 等待 "READY" 信号
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                string? line = stdout.ReadLine();
                if (line == null) throw new InvalidOperationException("Python bridge process exited unexpectedly during startup.");
                if (line == "READY") return;
                if (line.StartsWith("{")) // 可能是错误信息
                {
                    JsonNode? err = JsonNode.Parse(line);
                    throw new InvalidOperationException($"Vision bridge startup error: {err?["message"]}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Vision bridge did not become ready within {timeoutSeconds}s.");
        }
    }


    /// <summary>
    /// 视觉问答：用中文提问，获得中文回答。
    /// </summary>
    public Task<string> QueryAsync(string imagePath, string question, int? maxResponseTokens = null, CancellationToken ct = default)
    {
        return SendRequestAsync(new { action = "query", image_path = imagePath, question, max_new_tokens = maxResponseTokens }, ct);
    }

    async Task<string> SendRequestAsync(object request, CancellationToken ct)
    {
        await syncLock.WaitAsync(ct);
        try
        {
            // 如果请求对象中没有指定 max_new_tokens，则使用默认配置
            string requestJson = JsonSerializer.Serialize(request);
            JsonNode? requestNode = JsonNode.Parse(requestJson);
            if (requestNode != null && requestNode["max_new_tokens"] == null)
            {
                requestNode["max_new_tokens"] = MaxResponseTokens;
            }

            string json = requestNode?.ToJsonString() ?? requestJson;
            await stdin!.WriteLineAsync(json.AsMemory(), ct);
            await stdin.FlushAsync(ct);

            string? response = await stdout!.ReadLineAsync(ct);
            if (response == null)
                throw new InvalidOperationException("Python bridge closed unexpectedly.");

            JsonNode? node = JsonNode.Parse(response);
            string? status = node?["status"]?.GetValue<string>();

            if (status == "ok")
                return node!["result"]?.GetValue<string>() ?? string.Empty;

            string message = node?["message"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"Vision bridge error: {message}");
        }
        finally
        {
            syncLock.Release();
        }
    }

    public void Dispose()
    {
        process?.Kill();
        process?.Dispose();
        syncLock.Dispose();
        GC.SuppressFinalize(this);
    }

    readonly Process? process;
    readonly StreamWriter? stdin;
    readonly StreamReader? stdout;
    readonly SemaphoreSlim syncLock = new(1, 1);
}
