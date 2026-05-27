using Alife.Basic;
using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Vision;
using Microsoft.Extensions.Logging;

namespace Alife.Implement;

public enum VisionAnalyzerType
{
    None,
    Qwen,
    OpenAI
}

public record VisionConfig
{
    //选择视觉模型的推理方式
    public VisionAnalyzerType AnalyzerType { get; set; } = VisionAnalyzerType.None;

    public string OnlineBaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string OnlineApiKey { get; set; } = "";
    public string OnlineModel { get; set; } = "gpt-4o";

    //对图片的附加提示词
    public string AppendPrompt { get; set; } = "（请精简的描述一下图片大体内容，避免输出过多的文本，提高分析速度）";
}

public partial class VisionService
{
    QwenVisionAnalyzer? qwenVisionAnalyzer;
    OpenAIVisionAnalyzer? openAIVisionAnalyzer;
}

[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。", EditorUI = typeof(VisionServiceUI))]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。" +
             "在分析图片时，你需要提供prompt参数。在该参数中，你要清晰描述你的问题，并尽可能提供背景信息，以帮助视觉模型分析，但也注意不要随意揣测其中内容，防止影响识别结果！")]
public partial class VisionService(FunctionService functionService, ILogger<QwenVisionAnalyzer> qwenLogger)
    : InteractivePlugin<VisionService>, IConfigurable<VisionConfig>
{
    /// <summary>
    /// 获取当前可以截取的所有可用窗口的列表，供 AI 选择截屏目标。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description($"获取当前系统所有可用窗口列表，返回结果包含窗口标题和对应的 Handle，可用于传入到 {nameof(LookScreen)} 中进行识图。")]
    public void GetWindows()
    {
        var windows = WindowCaptureHelper.EnumerateWindows()
            .Where(w => !string.IsNullOrWhiteSpace(w.Title))
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【可用窗口列表】");
        foreach (var w in windows)
            sb.AppendLine($"Handle: {w.Handle.ToInt64()} | 标题: {w.Title}");

        Poke(sb.ToString());
    }

    /// <summary>
    /// 截取指定窗口或全屏并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看当前屏幕或特定窗口内容。（使用后需等待结果返回）")]
    public async Task LookScreen(
        [Description($"要查看画面的窗口句柄（可用 {nameof(GetWindows)} 获取），传入 -1 则直接识别当前全屏画面")] long windowHandle,
        string prompt,
        [Description("期望回复字数，过小可能结果不全，过大可能分析过慢")] int maxToken = 64)
    {
        if (AlifePlatform.IsLocking())
        {
            Poke("【屏幕分析结果】当前电脑处于锁屏状态，无法获取屏幕内容，用户应该不在电脑前。");
            return;
        }

        //截取目标画面
        string screenshotPath = Path.Combine(AlifePath.TempFolderPath, $"vision_capture_{DateTime.Now.Ticks}.png");
        {
            using var bmp = windowHandle == -1
                ? await WindowCaptureHelper.CaptureFullscreenAsync()
                : await WindowCaptureHelper.CaptureWindowAsync(new IntPtr(windowHandle));
            bmp.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        //获取深度识别结果
        string deepVisionResult = "未开启";
        if (analyzer != null)
        {
            prompt += configuration!.AppendPrompt;
            if (windowHandle == -1)
                prompt += $"（这是一张屏幕截图，当前焦点窗口为{WindowsPlatform.GetActiveWindowTitle()}）" + configuration!.AppendPrompt;

            CancellationTokenSource cancellationTokenSource = new(30000);
            deepVisionResult = $"{await analyzer.QueryAsync(
            screenshotPath,
            prompt,
            maxToken,
            cancellationToken: cancellationTokenSource.Token)}";
        }

        if (windowHandle == -1)
        {
            Poke($"""
                  【屏幕分析结果】
                  - 窗口列表：{AlifePlatform.GetRunningWindowTitles()}
                  - 焦点窗口：{WindowsPlatform.GetActiveWindowTitle()}
                  - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                  """);
        }
        else
        {
            Poke($"""
                  【窗口分析结果】
                  - 文字识别：{await AlifePlatform.OcrAsync(screenshotPath)}
                  - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                  """);
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("对指定的图片进行视觉分析。（使用后需等待结果返回）")]
    public async Task LookImage(
        [Description("图片地址或网址")] string path,
        string prompt,
        [Description("期望回复字数，过小可能结果不全，过大可能分析过慢")] int maxToken = 64)
    {
        // 处理网络图片
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string downloaded = $"{AlifePath.TempFolderPath}/vision_download.png";
            await AlifePlatform.DownloadFileAsync(path, downloaded);
            path = downloaded;
        }

        string deepVisionResult = "未开启";
        if (analyzer != null)
        {
            prompt += configuration!.AppendPrompt;

            CancellationTokenSource cancellationTokenSource = new(30000);
            deepVisionResult = $"{await analyzer.QueryAsync(
            path,
            prompt,
            maxToken,
            cancellationToken: cancellationTokenSource.Token)}";
        }

        Poke($"""
              【图片分析结果】
              - 文字识别：{await AlifePlatform.OcrAsync(path)}
              - 深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
              """);
    }

    public VisionConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value;
            if (value != null)
            {
                if (openAIVisionAnalyzer != null)
                    openAIVisionAnalyzer.UpdateConfig(value.OnlineBaseUrl, value.OnlineApiKey, value.OnlineModel);
            }
        }
    }

    VisionConfig? configuration;
    VisionAnalyzer? analyzer;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionService.RegisterHandler(this);

        switch (configuration!.AnalyzerType)
        {
            case VisionAnalyzerType.None:
                break;
            case VisionAnalyzerType.Qwen:
                qwenVisionAnalyzer ??= new QwenVisionAnalyzer(qwenLogger);
                analyzer = qwenVisionAnalyzer;
                break;
            case VisionAnalyzerType.OpenAI:
                openAIVisionAnalyzer ??= new OpenAIVisionAnalyzer(configuration.OnlineBaseUrl, configuration.OnlineApiKey, configuration.OnlineModel);
                analyzer = openAIVisionAnalyzer;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
