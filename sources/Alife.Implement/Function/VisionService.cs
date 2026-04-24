using Alife.Basic;
using System.ComponentModel;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Vision;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

public partial class VisionService
{
    static readonly VisionAnalyzer Analyzer;
    static VisionService()
    {
        Analyzer = new VisionAnalyzer();
    }
}
[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。")]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。（注意！分析系统并不准确，所以你需要配合结果，自己洞察出真正的实际情况）")]
public partial class VisionService : InteractivePlugin<VisionService>
{
    /// <summary>
    /// 截取屏幕并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction("look_screen")]
    [Description("截取当前屏幕并根据需求进行视觉内容分析。")]
    public async Task LookScreen(XmlExecutorContext context, string query)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        string screenshotPath = CaptureScreen();
        Task<string> visionTask = Analyzer.QueryAsync(screenshotPath, query);
        string windowInfo = GetRunningWindowTitles();
        string visionInfo = await visionTask;

        Poke($"窗口信息：{windowInfo}\n视觉分析结果：{visionInfo}");

        string GetRunningWindowTitles()
        {
            List<string> titles = new();
            IntPtr foregroundWnd = GetForegroundWindow();

            EnumWindows((hWnd, _) => {
                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        StringBuilder sb = new(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();
                        if (string.IsNullOrWhiteSpace(title) == false && title != "Program Manager")
                        {
                            if (hWnd == foregroundWnd)
                            {
                                title = $"(聚焦) {title}";
                            }
                            titles.Add(title);
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return titles.Count > 0 ? string.Join(", ", titles) : "无可见窗口";
        }
        string CaptureScreen()
        {
            // 获取虚拟屏幕总尺寸（多显示器支持）
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
            {
                // 回退到主屏幕
                left = 0;
                top = 0;
                width = GetSystemMetrics(SM_CXSCREEN);
                height = GetSystemMetrics(SM_CYSCREEN);
            }

            using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            // --- 压缩图片分辨率 (优化：限制最大边长为 512) ---
            const int MaxSide = 512;
            string path = $"{AlifePath.TempFolderPath}/vision_screen.png";

            if (width > MaxSide || height > MaxSide)
            {
                float scale = Math.Min((float)MaxSide / width, (float)MaxSide / height);
                int newWidth = (int)(width * scale);
                int newHeight = (int)(height * scale);

                using Bitmap resized = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
                }
                resized.Save(path, ImageFormat.Png);
            }
            else
            {
                bitmap.Save(path, ImageFormat.Png);
            }

            return path;
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction("look_image")]
    [Description("根据需求对指定的图片进行视觉内容分析。")]
    public async Task LookImage(XmlExecutorContext context, [Description("图片地址或网址")] string path, string query)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        try
        {
            // 处理网络图片
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                path = await DownloadImageAsync(path);

            string result = await Analyzer.QueryAsync(path, query);
            Poke($"图片分析结果：{result}");
        }
        catch (Exception ex)
        {
            Poke($"图片处理失败：{ex.Message}");
        }

        async Task<string> DownloadImageAsync(string url)
        {
            const string Filename = "vision_download.png";
            string tempPath = $"{AlifePath.TempFolderPath}/{Filename}";
            byte[] data = await httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(tempPath, data);
            return tempPath;
        }
    }

    readonly HttpClient httpClient = new();

    public VisionService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    // ─────────────────────── Win32 PInvoke ───────────────────────

    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
}
