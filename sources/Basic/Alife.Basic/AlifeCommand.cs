using System.Diagnostics;

namespace Alife.Basic;

/// <summary>
/// 管理系统级 Python 运行环境及 ModelScope 依赖。
/// </summary>
public static class AlifeCommand
{
    public static void EnsureInitialized()
    {
        if (HasPython())
            return;
        InstallPython();
        if (HasPython() == false)
            throw new Exception("Python 安装失败或未被识别，请手动安装 Python 3.11+ 并添加到环境变量。");

        Command("pip", "config set global.index-url https://mirrors.aliyun.com/pypi/simple/");
    }
    public static void Command(string fileName, string arguments)
    {
        ProcessStartInfo psi = new() {
            FileName = "cmd.exe",
            Arguments = $"/c {fileName} {arguments} & pause",
            CreateNoWindow = false,
            UseShellExecute = true,
        };
        using Process? process = Process.Start(psi);
        process?.WaitForExit();
    }

    static AlifeCommand()
    {
        EnsureInitialized();
    }
    static bool HasPython()
    {
        ProcessStartInfo psi = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = "/c python --version",
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using Process? p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode == 0;
    }
    static void InstallPython()
    {
        const string Version = "3.11.9";
        const string InstallerUrl = $"https://www.python.org/ftp/python/{Version}/python-{Version}-amd64.exe";
        string installerPath = Path.Combine(AlifePath.TempFolderPath, "python_installer.exe");

        Terminal.LogInfo($"正在准备安装 Python {Version}...");

        // 下载并执行 PowerShell 安装脚本
        string psScript = $@"
$url = '{InstallerUrl}'
$path = '{installerPath}'
Write-Host '正在下载 Python 安装程序...'
Invoke-WebRequest -Uri $url -OutFile $path
Write-Host '正在启动静默安装 (可能需要管理员权限)...'
Start-Process -FilePath $path -ArgumentList '/quiet PrependPath=1' -Wait
Remove-Item -Path $path -Force
";
        string scriptPath = Path.Combine(AlifePath.TempFolderPath, "install_python.ps1");
        File.WriteAllText(scriptPath, psScript);

        ProcessStartInfo psi = new ProcessStartInfo {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true, // 给予用户看到 UAC 和进度的机会
            Verb = "runas" // 申请管理员权限进行系统级安装
        };

        try
        {
            using Process? p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            throw new Exception($"用户取消或安装失败: {ex.Message}");
        }
    }
}
