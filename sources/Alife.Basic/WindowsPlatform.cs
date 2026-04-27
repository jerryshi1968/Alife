using System.Diagnostics;
using System.IO;
using System.Text;

namespace Alife.Basic;

internal static class WindowsPlatform
{
    public static (int Width, int Height) GetResolution()
    {
        IntPtr hdc = WindowsNative.GetDC(IntPtr.Zero);
        try
        {
            int width = WindowsNative.GetDeviceCaps(hdc, WindowsNative.Desktophorzres);
            int height = WindowsNative.GetDeviceCaps(hdc, WindowsNative.Desktopvertres);
            return (width, height);
        }
        finally
        {
            WindowsNative.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static void Notice(string title, string message)
    {
        string script = $"$Title='{title}'; $Message='{message}'; " +
                        "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                        "$Template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                        "$TextNodes = $Template.GetElementsByTagName('text'); " +
                        "$TextNodes.Item(0).AppendChild($Template.CreateTextNode($Title)) | Out-Null; " +
                        "$TextNodes.Item(1).AppendChild($Template.CreateTextNode($Message)) | Out-Null; " +
                        "$Toast = [Windows.UI.Notifications.ToastNotification]::new($Template); " +
                        "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('AlifeSpeechAssist').Show($Toast);";

        Process.Start(new ProcessStartInfo {
            FileName = "powershell",
            Arguments = $"-Command \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    public static void Command(string fileName, string arguments)
    {
        ProcessStartInfo psi = new() {
            FileName = "cmd.exe",
            Arguments = $"/c {fileName} {arguments}",
            CreateNoWindow = false,
            UseShellExecute = true,
        };
        using Process? process = Process.Start(psi);
        process?.WaitForExit();
    }

    public static string Screenshot()
    {
        int left = WindowsNative.GetSystemMetrics(WindowsNative.SmXvirtualscreen);
        int top = WindowsNative.GetSystemMetrics(WindowsNative.SmYvirtualscreen);
        int width = WindowsNative.GetSystemMetrics(WindowsNative.SmCxvirtualscreen);
        int height = WindowsNative.GetSystemMetrics(WindowsNative.SmCyvirtualscreen);

        if (width <= 0 || height <= 0)
        {
            left = 0;
            top = 0;
            width = WindowsNative.GetSystemMetrics(WindowsNative.SmCxscreen);
            height = WindowsNative.GetSystemMetrics(WindowsNative.SmCyscreen);
        }

        using System.Drawing.Bitmap bitmap = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);

        const int MaxSide = 512;
        string path = $"{AlifePath.TempFolderPath}/vision_screen.png";

        if (width > MaxSide || height > MaxSide)
        {
            float scale = Math.Min((float)MaxSide / width, (float)MaxSide / height);
            int newWidth = (int)(width * scale);
            int newHeight = (int)(height * scale);

            using System.Drawing.Bitmap resized = new(newWidth, newHeight);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
            }
            resized.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        else
        {
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        return path;
    }

    public static string GetRunningWindowTitles()
    {
        List<string> titles = new();
        IntPtr foregroundWnd = WindowsNative.GetForegroundWindow();

        WindowsNative.EnumWindows((hWnd, _) => {
            if (WindowsNative.IsWindowVisible(hWnd))
            {
                int length = WindowsNative.GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    StringBuilder sb = new(length + 1);
                    WindowsNative.GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title) == false && title != "Program Manager")
                    {
                        if (hWnd == foregroundWnd)
                        {
                            title = $"{title}(当前聚焦)";
                        }
                        titles.Add(title);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return titles.Count > 0 ? string.Join(", ", titles) : "无可见窗口";
    }

    public static string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title
        };

        // 确保初始目录路径格式正确且存在，否则会触发 Shell API 异常
        string initialDir = AlifePath.StorageFolderPath;
        if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
        {
            dialog.InitialDirectory = Path.GetFullPath(initialDir);
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
