using System.Runtime.InteropServices;

namespace Alife.Basic;

/// <summary>
/// 跨平台抽象层：根据当前操作系统调度不同的底层实现。
/// </summary>
public static class AlifePlatform
{
    public static (int Width, int Height) GetResolution()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.GetResolution();
        
        throw new PlatformNotSupportedException("当前平台不支持获取分辨率。");
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

    public static void Command(string fileName, string arguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsPlatform.Command(fileName, arguments);
            return;
        }

        // 未来可在此添加 Linux/macOS 的 Shell 调用实现 (如使用 /bin/sh)
        throw new PlatformNotSupportedException("当前平台暂不支持执行命令行。");
    }

    public static string Screenshot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.Screenshot();

        throw new PlatformNotSupportedException("当前平台暂不支持截屏。");
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
}
