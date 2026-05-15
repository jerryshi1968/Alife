using Alife.Basic;
using System.ComponentModel;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Vision;

namespace Alife.Implement;

public partial class VisionService
{
    static VisionAnalyzer? analyzer;

    /// <summary>
    /// 确保视觉分析器已初始化（供其他服务调用）
    /// </summary>
    static void TryInitAnalyzer()
    {
        analyzer ??= new VisionAnalyzer();
    }
}

[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。")]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。")]
public partial class VisionService(FunctionService functionService) : InteractivePlugin<VisionService>
{
    /// <summary>
    /// 截取屏幕并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看当前屏幕内容。（使用后需等待结果返回）")]
    public async Task LookScreen(string query)
    {
        string screenshotPath = AlifePlatform.Screenshot();
        Poke($"""
              【屏幕分析结果】（注意！本结果不能完全作为判断用户行为的依据，因为电脑可能处于挂机状态）
              - 窗口列表：{AlifePlatform.GetRunningWindowTitles()}
              - 焦点窗口：{WindowsPlatform.GetActiveWindowTitle()}
              - 深度视觉：{await analyzer!.QueryAsync(screenshotPath, query)}（注意！深度视觉误识别率非常高）
              """);
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("对指定的图片进行视觉分析。（使用后需等待结果返回）")]
    public async Task LookImage([Description("图片地址或网址")] string path, string query)
    {
        try
        {
            // 处理网络图片
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string downloaded = $"{AlifePath.TempFolderPath}/vision_download.png";
                await AlifePlatform.DownloadFileAsync(path, downloaded);
                path = downloaded;
            }

            string result = await analyzer!.QueryAsync(path, query);
            Poke($"""
                  【图片分析结果】
                  - 文字识别：{await AlifePlatform.OcrAsync(path)}
                  - 深度视觉：{result}（注意！深度视觉误识别率非常高）
                  """);
        }
        catch (Exception ex)
        {
            Poke($"图片分析失败：{ex.Message}");
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        TryInitAnalyzer();
        functionService.RegisterHandler(this);
    }
}
