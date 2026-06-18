using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Platform;

/// <summary>
/// 跨平台抽象层：根据当前操作系统调度不同的底层实现。
/// </summary>
public static class AlifePlatform
{
    /// <summary>
    /// 通过 HttpClient 下载文件到本地
    /// </summary>
    /// <param name="url">下载地址</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="progress">进度回调，参数为已下载字节数和总字节数</param>
    /// <param name="timeout">超时时间，默认 100 秒</param>
    public static async Task DownloadFileAsync(string url, string savePath, Action<long, long>? progress = null, TimeSpan? timeout = null)
    {
        //应用镜像替换
        url = MirrorProvider.TransformUrl(url);

        //伪装用户
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        if (url.Contains("multimedia.nt.qq.com.cn") || url.Contains("qpic.cn"))
            request.Headers.Add("Referer", "https://q.qq.com/");

        //设置超时
        using var httpClient = timeout.HasValue ? new HttpClient { Timeout = timeout.Value } : SharedHttpClient;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        //获取总大小
        long totalBytes = response.Content.Headers.ContentLength ?? -1;

        //创建目录
        string? dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        //下载文件并报告进度
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long readSoFar = 0;
        int bytesRead;
        long nextReport = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            readSoFar += bytesRead;

            if (progress != null && readSoFar >= nextReport)
            {
                progress(readSoFar, totalBytes);
                nextReport = readSoFar + 10 * 1024 * 1024; // 每 10MB 报告一次
            }
        }

        //最终进度报告
        progress?.Invoke(readSoFar, totalBytes);
    }

    /// <summary>
    /// 通过 HttpClient 获取远程字符串内容
    /// </summary>
    /// <param name="url">请求地址</param>
    /// <param name="timeout">超时时间，默认 30 秒</param>
    /// <returns>响应内容字符串</returns>
    public static async Task<string> FetchStringAsync(string url, TimeSpan? timeout = null)
    {
        //应用镜像替换
        url = MirrorProvider.TransformUrl(url);

        //伪装用户
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        //设置超时
        using var httpClient = timeout.HasValue ? new HttpClient { Timeout = timeout.Value } : SharedHttpClient;
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
    public static async Task DownloadZipFileAsync(string rootPath, string url)
    {
        if (Directory.Exists(rootPath) == false)
            Directory.CreateDirectory(rootPath);

        string zipPath = Path.Combine(rootPath, "temp.zip");
        await DownloadFileAsync(url, zipPath);

        ZipFile.ExtractToDirectory(zipPath, rootPath, overwriteFiles: true);
        File.Delete(zipPath);
    }
    public static string Command(string fileName, string arguments)
    {
        if (CommandIgnore.Length != 0)
        {
            string fullCommand = $"{fileName} {arguments}";
            if (CommandIgnore.Any(ignore => Regex.IsMatch(fullCommand, ignore)))
                return "";
        }

        ProcessStartInfo psi = new() {
            FileName = "cmd.exe",
            Arguments = $"/c {fileName} {arguments}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process? process = Process.Start(psi);
        StringBuilder stdoutBuilder = new();
        StringBuilder stderrBuilder = new();
        if (process != null)
        {
            process.OutputDataReceived += (s, e) => {
                Console.WriteLine(e.Data);
                stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (s, e) => {
                Console.WriteLine(e.Data);
                stderrBuilder.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }

        string stdout = stdoutBuilder.ToString();
        string stderr = stderrBuilder.ToString();

        return string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";
    }

    public static (int Width, int Height) GetResolution()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.GetResolution();

        throw new PlatformNotSupportedException("当前平台不支持获取分辨率。");
    }

    /// <summary>
    /// 判断当前系统是否处于锁屏/息屏状态。
    /// </summary>
    public static bool IsLocking()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.IsLocking();

        return false;
    }

    public static void Notice(string title, string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsPlatform.Notice(title, message);
            return;
        }

        AlifeTerminal.LogWarning($"[Notification] {title}: {message} (当前平台暂不支持弹出式通知)");
    }

    public static string GetRunningWindowTitles()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.GetRunningWindowTitles();

        return "当前平台不支持窗口枚举";
    }

    public static string? PickFolder(string title = "请选择文件夹")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.PickFolder(title);

        throw new PlatformNotSupportedException("当前平台暂不支持目录选择对话框。");
    }

    public static async Task<string?> PickFolderAsync(string title = "请选择文件夹")
    {
        await Task.Delay(50);
        return PickFolder(title);
    }

    public static async Task<string> OcrAsync(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return await WindowsPlatform.OcrAsync(path);

        return "当前平台不支持 OCR";
    }



    static readonly HttpClient SharedHttpClient = new();
    static readonly string[] CommandIgnore;

    static AlifePlatform()
    {
        string commandIgnoreFile = Path.Combine(AlifePath.RuntimeFolderPath, "CommandIgnore.txt");
        if (File.Exists(commandIgnoreFile) == false)
            File.Create(commandIgnoreFile).Close();
        CommandIgnore = File.ReadAllLines(commandIgnoreFile).Where(s => string.IsNullOrEmpty(s.Trim()) == false).ToArray();
    }
}
