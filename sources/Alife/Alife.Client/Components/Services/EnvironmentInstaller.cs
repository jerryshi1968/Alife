using System;
using System.IO;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.Components.Services;

public enum EnvStatus { Ready, NotInstalled, Error, Checking }

public class EnvCheckResult
{
    public EnvStatus Status { get; set; } = EnvStatus.Checking;
    public string Message { get; set; } = "";
    public bool IsOptional { get; set; }
}

public class EnvironmentInstaller
{
    public EnvCheckResult VCRedist { get; } = new();
    public EnvCheckResult Python { get; } = new();
    public EnvCheckResult DotNetSdk { get; } = new();
    public EnvCheckResult Cuda { get; } = new() { IsOptional = true };

    public string? PythonDir { get; private set; }

    public async Task CheckAllAsync()
    {
        await Task.WhenAll(
            CheckVCppRedistAsync(),
            Task.Run(CheckPython),
            CheckDotNetSdkAsync(),
            CheckCudaAsync()
        );
    }

    // ────────────────────────────────────────────
    //  Step 1a: VC++ Redist
    // ────────────────────────────────────────────
    public Task CheckVCppRedistAsync()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "vcruntime140.dll");
        if (File.Exists(path))
        {
            VCRedist.Status = EnvStatus.Ready;
            VCRedist.Message = "已就绪";
        }
        else
        {
            VCRedist.Status = EnvStatus.NotInstalled;
            VCRedist.Message = "未安装";
        }
        return Task.CompletedTask;
    }

    public async Task InstallVCppRedistAsync(IProgress<string>? progress = null)
    {
        VCRedist.Status = EnvStatus.Checking;
        VCRedist.Message = "正在安装...";

        try
        {
            progress?.Report("正在下载 Visual C++ Redistributable...");
            string tempExe = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
            await AlifePlatform.DownloadFileAsync("https://aka.ms/vs/17/release/vc_redist.x64.exe", tempExe);

            progress?.Report("正在静默安装 Visual C++ Redistributable...");
            AlifePlatform.Command(tempExe, "/install /quiet /norestart");

            try { File.Delete(tempExe); } catch { }

            await CheckVCppRedistAsync();
            progress?.Report(VCRedist.Status == EnvStatus.Ready ? "Visual C++ 安装完成" : "Visual C++ 安装失败，请重试");
        }
        catch (Exception ex)
        {
            VCRedist.Status = EnvStatus.Error;
            VCRedist.Message = $"安装出错: {ex.Message}";
            progress?.Report($"VC++ 安装失败: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────
    //  Step 1b: Python 3.12 + pip
    // ────────────────────────────────────────────
    public void CheckPython()
    {
        PythonDir = FindPythonDir();
        if (PythonDir != null)
        {
            Python.Status = EnvStatus.Ready;
            Python.Message = $"已就绪 ({PythonDir})";
        }
        else
        {
            Python.Status = EnvStatus.NotInstalled;
            Python.Message = "未安装";
        }
    }

    static string? FindPythonDir()
    {
        // 1. Check %LOCALAPPDATA%\Programs\Python\Python312
        string systemPy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312");
        if (File.Exists(Path.Combine(systemPy, "python.exe")))
            return systemPy;

        // 2. Check Runtime dir
        string runtimePy = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
        if (File.Exists(Path.Combine(runtimePy, "python.exe")))
            return runtimePy;

        return null;
    }

    public async Task InstallPythonAsync(IProgress<string>? progress = null)
    {
        Python.Status = EnvStatus.Checking;
        Python.Message = "正在安装...";

        try
        {
            string pyDir = Path.Combine(AlifePath.RuntimeFolderPath, "Python312");
            Directory.CreateDirectory(pyDir);

            progress?.Report("正在下载 Python 3.12 嵌入版...");
            await AlifePlatform.DownloadFileAsync(
                "https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-embed-amd64.zip",
                Path.Combine(Path.GetTempPath(), "py.zip"));

            progress?.Report("正在解压 Python 3.12...");
            System.IO.Compression.ZipFile.ExtractToDirectory(
                Path.Combine(Path.GetTempPath(), "py.zip"), pyDir, overwriteFiles: true);
            try { File.Delete(Path.Combine(Path.GetTempPath(), "py.zip")); } catch { }

            progress?.Report("配置 site-packages...");
            string pthFile = Path.Combine(pyDir, "python312._pth");
            if (File.Exists(pthFile))
            {
                string content = await File.ReadAllTextAsync(pthFile);
                content = content.Replace("#import site", "import site");
                await File.WriteAllTextAsync(pthFile, content);
            }

            progress?.Report("正在安装 pip...");
            string getPyUrl = "https://bootstrap.pypa.io/get-pip.py";
            string getPyPath = Path.Combine(Path.GetTempPath(), "get-pip.py");
            await AlifePlatform.DownloadFileAsync(getPyUrl, getPyPath);

            AlifePlatform.Command(Path.Combine(pyDir, "python.exe"), $"\"{getPyPath}\" --no-warn-script-location");
            try { File.Delete(getPyPath); } catch { }

            progress?.Report("正在安装 setuptools / wheel...");
            string pyExe = Path.Combine(pyDir, "python.exe");
            AlifePlatform.Command(pyExe, "-m pip install --upgrade pip setuptools wheel --quiet --no-warn-script-location");

            PythonDir = pyDir;
            CheckPython();
            progress?.Report(Python.Status == EnvStatus.Ready ? "Python 3.12 安装完成" : "Python 3.12 安装失败，请重试");
        }
        catch (Exception ex)
        {
            Python.Status = EnvStatus.Error;
            Python.Message = $"安装出错: {ex.Message}";
            progress?.Report($"Python 安装失败: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────
    //  Step 1c: .NET SDK 10
    // ────────────────────────────────────────────
    public async Task CheckDotNetSdkAsync()
    {
        // 先尝试系统 PATH 中的 dotnet
        string output = AlifePlatform.Command("dotnet", "--list-sdks");
        if (output.Contains("10."))
        {
            DotNetSdk.Status = EnvStatus.Ready;
            DotNetSdk.Message = "已就绪";
            return;
        }

        // 再尝试 Program Files 路径
        string programFilesDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(programFilesDotnet))
        {
            output = AlifePlatform.Command($"\"{programFilesDotnet}\"", "--list-sdks");
            if (output.Contains("10."))
            {
                DotNetSdk.Status = EnvStatus.Ready;
                DotNetSdk.Message = "已就绪";
                return;
            }
        }

        // 最后尝试 LocalAppData 路径
        string localDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet", "dotnet.exe");
        if (File.Exists(localDotnet))
        {
            output = AlifePlatform.Command($"\"{localDotnet}\"", "--list-sdks");
            if (output.Contains("10."))
            {
                DotNetSdk.Status = EnvStatus.Ready;
                DotNetSdk.Message = "已就绪";
                return;
            }
        }

        DotNetSdk.Status = EnvStatus.NotInstalled;
        DotNetSdk.Message = "未安装 .NET SDK 10";
    }

    public async Task InstallDotNetSdkAsync(IProgress<string>? progress = null)
    {
        DotNetSdk.Status = EnvStatus.Checking;
        DotNetSdk.Message = "正在安装...";

        try
        {
            progress?.Report("正在下载 .NET SDK 10 安装包...");
            string tempExe = Path.Combine(Path.GetTempPath(), "dotnet-sdk-10.exe");
            await AlifePlatform.DownloadFileAsync("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.301/dotnet-sdk-10.0.301-win-x64.exe", tempExe);

            progress?.Report("正在安装 .NET SDK 10...");
            AlifePlatform.Command(tempExe, "/install /quiet /norestart");

            try { File.Delete(tempExe); } catch { }

            await CheckDotNetSdkAsync();
            progress?.Report(DotNetSdk.Status == EnvStatus.Ready ? ".NET SDK 10 安装完成" : ".NET SDK 10 安装失败，请重试");
        }
        catch (Exception ex)
        {
            DotNetSdk.Status = EnvStatus.Error;
            DotNetSdk.Message = $"安装出错: {ex.Message}";
            progress?.Report($".NET SDK 10 安装失败: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────
    //  Step 1d: CUDA (可选)
    // ────────────────────────────────────────────
    public async Task CheckCudaAsync()
    {
        if (PythonDir == null)
        {
            Cuda.Status = EnvStatus.NotInstalled;
            Cuda.Message = "需要先安装 Python";
            return;
        }

        try
        {
            string pyExe = Path.Combine(PythonDir, "python.exe");
            string output = AlifePlatform.Command(pyExe, "-c \"import torch; print(torch.version.cuda or 'none')\"");
            string trimmed = output.Trim();

            if (trimmed.Contains("12."))
            {
                Cuda.Status = EnvStatus.Ready;
                Cuda.Message = $"已就绪 (CUDA {trimmed})";
            }
            else if (trimmed == "none")
            {
                Cuda.Status = EnvStatus.NotInstalled;
                Cuda.Message = "PyTorch 已安装，但未启用 CUDA";
            }
            else
            {
                Cuda.Status = EnvStatus.NotInstalled;
                Cuda.Message = "未检测到 PyTorch CUDA";
            }
        }
        catch
        {
            Cuda.Status = EnvStatus.NotInstalled;
            Cuda.Message = "未配置 CUDA";
        }
    }

    public async Task InstallCudaAsync(IProgress<string>? progress = null)
    {
        if (PythonDir == null)
        {
            Cuda.Status = EnvStatus.Error;
            Cuda.Message = "请先安装 Python";
            return;
        }

        Cuda.Status = EnvStatus.Checking;
        Cuda.Message = "正在安装...";

        try
        {
            string pyExe = Path.Combine(PythonDir, "python.exe");

            progress?.Report("正在卸载已有 torch...");
            AlifePlatform.Command(pyExe, "-m pip uninstall torch torchvision -y");

            progress?.Report("正在安装 PyTorch 2.10.0 + CUDA 12.8（可能需要较长时间）...");
            string pytorchIndex = MirrorProvider.TransformUrl("https://download.pytorch.org/whl/cu128");
            string pipInstall = $"install torch==2.10.0+cu128 torchvision==0.25.0+cu128 --index-url {pytorchIndex}";
            AlifePlatform.Command(pyExe, $"-m pip {pipInstall}");

            await CheckCudaAsync();
            progress?.Report(Cuda.Status == EnvStatus.Ready ? "CUDA 安装完成" : "CUDA 安装失败，请检查网络");
        }
        catch (Exception ex)
        {
            Cuda.Status = EnvStatus.Error;
            Cuda.Message = $"安装出错: {ex.Message}";
            progress?.Report($"CUDA 安装失败: {ex.Message}");
        }
    }

    // ────────────────────────────────────────────
    //  工具方法
    // ────────────────────────────────────────────
    public static void SetupEnvironmentPaths()
    {
        List<string> paths = new();

        string? pythonDir = FindPythonDir();
        if (pythonDir != null) paths.Add(pythonDir);

        string dotnetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        if (Directory.Exists(dotnetDir)) paths.Add(dotnetDir);

        string dotnetLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
        if (Directory.Exists(dotnetLocal)) paths.Add(dotnetLocal);

        if (paths.Count > 0)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string extra = string.Join(Path.PathSeparator, paths.Where(p => !path.Contains(p)));
            if (extra.Length > 0)
                Environment.SetEnvironmentVariable("PATH", $"{extra}{Path.PathSeparator}{path}");
        }
    }
}
