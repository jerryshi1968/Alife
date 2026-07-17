using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Alife.Platform;
using Newtonsoft.Json.Linq;

namespace Alife.Components.Services;

public record UpdateInfo(string Version, string? ReleaseNotes, string DownloadUrl);

public class UpdateService
{
    const string RawGitHubApiUrl = "https://api.github.com/repos/BDFFZI/Alife/releases/latest";
    static readonly bool EnableAppUpdate = false;

    public bool IsUpdateEnabled => EnableAppUpdate;

    public string GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        if (EnableAppUpdate == false)
            return null;

        try
        {
            var response = await AlifePlatform.FetchStringAsync(RawGitHubApiUrl);
            var json = JObject.Parse(response);

            string? tagName = json["tag_name"]?.ToString();
            if (string.IsNullOrEmpty(tagName))
                return null;

            string latestVersion = tagName.TrimStart('v');
            string currentVersion = GetCurrentVersion();

            if (new Version(latestVersion) > new Version(currentVersion))
            {
                string? body = json["body"]?.ToString();
                string? downloadUrl = json["assets"]?[0]?["browser_download_url"]?.ToString();

                if (!string.IsNullOrEmpty(downloadUrl))
                    return new UpdateInfo(latestVersion, body, downloadUrl);
            }
        }
        catch
        {
            // 网络异常静默忽略
        }

        return null;
    }

    public async Task ApplyUpdateAsync(UpdateInfo updateInfo, Action<int>? onProgress = null)
    {
        if (EnableAppUpdate == false)
            throw new InvalidOperationException("Application update is disabled in this fork.");

        string tempDir = Path.Combine(AlifePath.TempFolderPath, "Update");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);

        string zipPath = Path.Combine(tempDir, "Alife.zip");
        await AlifePlatform.DownloadFileAsync(updateInfo.DownloadUrl, zipPath, (read, total) =>
        {
            if (total > 0)
                onProgress?.Invoke((int)(read * 100 / total));
        });

        string currentDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string exeName = Path.GetFileName(exePath);
        string psPath = Path.Combine(tempDir, "update.ps1");
        File.WriteAllText(psPath, $$"""
Write-Host '=== Alife Update ===' -ForegroundColor Cyan
Write-Host ''

$proc = Get-Process -Name '{{exeName.Replace(".exe", "")}}' -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host 'Waiting for old process to exit...' -ForegroundColor Yellow
    $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host 'ZipPath:    {{zipPath}}'
Write-Host 'CurrentDir: {{currentDir}}'
Write-Host ''
if (-not (Test-Path '{{zipPath}}')) {
    Write-Host 'ERROR: ZIP not found!' -ForegroundColor Red
    Read-Host 'Press Enter to exit'
    exit 1
}

$extractTemp = '{{tempDir}}\_extract_tmp'
if (Test-Path $extractTemp) { Remove-Item $extractTemp -Recurse -Force }
New-Item -ItemType Directory -Path $extractTemp -Force | Out-Null

Write-Host 'Extracting to temp...' -ForegroundColor Yellow
try {
    Expand-Archive -Path '{{zipPath}}' -DestinationPath $extractTemp -Force
    Write-Host 'Extraction succeeded.' -ForegroundColor Green
} catch {
    Write-Host "Extraction failed: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host 'Press Enter to exit'
    exit 1
}

Write-Host 'Copying new files (overwrite)...' -ForegroundColor Yellow
Copy-Item -Path "$extractTemp\*" -Destination '{{currentDir}}' -Recurse -Force
Remove-Item $extractTemp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Starting Alife...' -ForegroundColor Cyan
Start-Process -FilePath '{{exePath}}'
Write-Host ''
Write-Host 'Upgrade successful! Press Enter to exit.' -ForegroundColor Green
Read-Host
""");

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{psPath}\"",
            CreateNoWindow = false,
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }
}
