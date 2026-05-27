using Alife.Basic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Vision;

/// <summary>
/// 使用 InternVL2.5-1B 进行图像理解。
/// 内部维护一个长驻 Python 子进程，模型只加载一次。
/// </summary>
public class QwenVisionAnalyzer : VisionAnalyzer
{
    public QwenVisionAnalyzer(ILogger<QwenVisionAnalyzer> logger)
    {
        //安装依赖
        AlifePlatform.Command("python", "-m pip install torch==2.5.1+cu121 torchvision==0.20.1+cu121 --find-links https://mirrors.aliyun.com/pytorch-wheels/cu121/");
        AlifePlatform.Command("python", "-m pip install Pillow transformers qwen-vl-utils bitsandbytes accelerate sentencepiece tiktoken -i https://mirrors.aliyun.com/pypi/simple/");

        //下载模型
        const string ModelId = "Qwen/Qwen2.5-VL-3B-Instruct";
        string modelPath = AlifeModel.EnsureModelExisting(ModelId);

        //准备python桥
        string script = Path.Combine(Path.GetDirectoryName(typeof(QwenVisionAnalyzer).Assembly.Location)!, "vision_bridge.py");
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
            StandardErrorEncoding = new UTF8Encoding(false),
            Environment = {
                ["PYTHONIOENCODING"] = "utf-8",
                ["PYTHONUTF8"] = "1"
            }
        };

        try
        {
            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("无法启动python桥进程");
            stdin = process.StandardInput;
            stdout = process.StandardOutput;

            //监听管道异常信息
            _ = Task.Run(async () => {
                try
                {
                    StreamReader stderr = process.StandardError;
                    while (process.HasExited == false)
                        logger.LogError(await stderr.ReadLineAsync());
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "管道异常监听停止");
                }
            });

            //等待模型加载完毕
            while (true)
            {
                string? line = Task.Run(async () => {
                    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(300));
                    return await stdout.ReadLineAsync(cts.Token);
                }).Result;
                if (line == null)
                    throw new InvalidOperationException("无法获取到管道输入");
                if (line.StartsWith("{"))// 可能是错误信息
                {
                    JsonNode? err = JsonNode.Parse(line);
                    throw new InvalidOperationException($"python桥启动异常: {err?["message"]}");
                }
                if (line == "READY")
                    return;

                logger.LogInformation(line);
            }
        }
        catch (Exception ex)
        {
            isFallback = true;
            logger.LogError(ex, "深度视觉初始化失败");
        }
    }
    public override void Dispose()
    {
        process?.Kill();
        process?.Dispose();
        syncLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 视觉问答：用中文提问，获得中文回答。
    /// </summary>
    public override async Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens,
        CancellationToken cancellationToken = default)
    {
        if (isFallback)
            return "深度视觉初始化失败。";

        try
        {
            var aiResult = await SendRequestAsync(
            new { action = "query", image_path = imagePath, question, max_new_tokens = maxResponseTokens }, cancellationToken);

            return aiResult;
        }
        catch (Exception ex)
        {
            return $"调用失败：{ex}";
        }
    }

    readonly Process? process;
    readonly StreamWriter? stdin;
    readonly StreamReader? stdout;
    readonly SemaphoreSlim syncLock = new(1, 1);
    readonly bool isFallback;//深度模型初始化失败

    async Task<string> SendRequestAsync(object request, CancellationToken ct)
    {
        await syncLock.WaitAsync(ct);
        try
        {
            //不要取消写入读取，尤其是写入后必须读取，否则会导致上传消息的残留
            stdin!.WriteLine(JsonSerializer.Serialize(request));
            string? response = stdout!.ReadLine();

            if (response == null)
                throw new InvalidOperationException("获取python桥结果为空");

            JsonNode? node = JsonNode.Parse(response);
            string? status = node?["status"]?.GetValue<string>();

            if (status == "ok")
                return node!["result"]?.GetValue<string>() ?? string.Empty;

            string message = node?["message"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"python桥异常: {message}");
        }
        finally
        {
            syncLock.Release();
        }
    }
}
