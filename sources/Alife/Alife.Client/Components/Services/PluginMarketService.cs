using System.IO;
using Alife.Framework;
using Alife.Platform;
using Alife.PluginMarket;
using Microsoft.Extensions.Logging;

namespace Alife.Components.Services;

public class PluginMarketConfig
{
    public string SourceUrl { get; set; } = "https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip";
}

public class PluginMarketService
{
    readonly StorageSystem storageSystem;
    readonly FileSystemPluginManager localManager;
    readonly NuGetEnvironmentInstaller nugetInstaller;
    readonly ModuleSystem moduleSystem;
    readonly SemaphoreSlim installLock = new(1, 1);
    Alife.PluginMarket.PluginMarket? pluginMarket;

    const string ConfigKey = "PluginMarketConfig";
    static readonly PluginMarketConfig defaultConfig = new();

    public PluginMarketService(ModuleSystem moduleSystem, StorageSystem storageSystem, ILogger<PluginMarketService> logger)
    {
        this.moduleSystem = moduleSystem;
        this.storageSystem = storageSystem;

        string installedDir = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        string packageListFile = Path.Combine(AlifePath.RuntimeFolderPath, "NUGET_PACKAGES.txt");

        Directory.CreateDirectory(installedDir);

        localManager = new FileSystemPluginManager(installedDir);
        nugetInstaller = new NuGetEnvironmentInstaller(packageListFile);

        Dictionary<string, IEnvironmentInstaller> environmentInstallers = new() {
            { "nuget", nugetInstaller },
            { "pip", new PipEnvironmentInstaller() }
        };

        var config = GetConfig();
        var onlineProvider = new ZipPluginProvider(config.SourceUrl);
        pluginMarket = new Alife.PluginMarket.PluginMarket(onlineProvider, localManager, localManager, environmentInstallers, installedDir);

        pluginMarket.RefreshLocalPlugins();
        UpdateModuleDirectories();

        try
        {
            moduleSystem.ReloadModules();
        }
        catch (Exception e)
        {
            logger.LogError(e, "模块加载失败");
        }
    }

    public PluginMarketConfig GetConfig()
    {
        return storageSystem.GetObject(ConfigKey, defaultConfig) ?? defaultConfig;
    }

    public void SaveConfig(PluginMarketConfig config)
    {
        storageSystem.SetObject(ConfigKey, config);
    }

    public string SourceUrl
    {
        get => GetConfig().SourceUrl;
    }

    public void SetSource(string sourceUrl)
    {
        var config = GetConfig();
        config.SourceUrl = sourceUrl;
        SaveConfig(config);

        var onlineProvider = new ZipPluginProvider(sourceUrl);
        string installedDir = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        pluginMarket = new Alife.PluginMarket.PluginMarket(onlineProvider, localManager, localManager, new Dictionary<string, IEnvironmentInstaller> {
            { "nuget", nugetInstaller },
            { "pip", new PipEnvironmentInstaller() }
        }, installedDir);

        pluginMarket.RefreshLocalPlugins();
        UpdateModuleDirectories();
    }

    public async Task FetchOnlinePluginsAsync()
    {
        if (pluginMarket == null) return;
        await pluginMarket.FetchOnlinePluginsAsync();
        pluginMarket.RefreshLocalPlugins();
    }

    void UpdateModuleDirectories()
    {
        var (managed, native) = nugetInstaller.ReadPackageList();
        if (managed.Length == 0 && native.Length == 0)
        {
            RegeneratePackageList();
            (managed, native) = nugetInstaller.ReadPackageList();
        }
        moduleSystem.SetExtraContext(managed, native);
    }

    void RegeneratePackageList()
    {
        if (pluginMarket == null) return;
        var installed = pluginMarket.GetInstalledPlugins();
        if (installed.Count == 0) return;

        List<KeyValuePair<string, string>> manifest = new();
        foreach (var (pluginId, version) in installed)
        {
            Plugin? plugin = pluginMarket.GetAllPlugins().FirstOrDefault(p => p.Id == pluginId);
            if (plugin == null) continue;
            var envs = plugin.GetEnvironments(version);
            if (envs != null && envs.TryGetValue("nuget", out var nuget))
                manifest.AddRange(nuget);
        }

        if (manifest.Count > 0)
            nugetInstaller.InstallEnvironment(manifest);
    }

    public Plugin[] GetAllPlugins() => pluginMarket?.GetAllPlugins().ToArray() ?? [];

    public void RefreshLocalPlugins() => pluginMarket?.RefreshLocalPlugins();

    public Dictionary<string, string> GetInstalledPlugins() => localManager.GetPlugins();

    public Plugin? GetPlugin(string pluginId)
    {
        return GetAllPlugins().FirstOrDefault(p => p.Id == pluginId);
    }

    public bool IsInstalled(string pluginId)
    {
        return GetInstalledPlugins().ContainsKey(pluginId);
    }

    public string? GetInstalledVersion(string pluginId)
    {
        return GetInstalledPlugins().GetValueOrDefault(pluginId);
    }

    public bool HasUpdate(Plugin plugin)
    {
        string? installedVersion = GetInstalledVersion(plugin.Id);
        if (installedVersion == null || plugin.Releases == null)
            return false;

        string? latestVersion = plugin.Releases.Keys
            .OrderByDescending(v => v, Comparer<string>.Create(VersionResolver.CompareVersions))
            .FirstOrDefault();

        return latestVersion != null && latestVersion != installedVersion;
    }

    public string? GetLatestVersion(Plugin plugin)
    {
        return plugin.Releases?.Keys
            .OrderByDescending(v => v, Comparer<string>.Create(VersionResolver.CompareVersions))
            .FirstOrDefault();
    }

    public async Task InstallPlugin(Plugin plugin, string version)
    {
        if (pluginMarket == null) return;
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.InstallPlugin(plugin, version);
                UpdateModuleDirectories();
                try
                {
                    moduleSystem.ReloadModules();
                }
                catch
                {
                    await pluginMarket.UninstallPlugin(plugin);
                    throw;
                }
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }

    public async Task InstallPlugins(IEnumerable<(Plugin plugin, string version)> plugins)
    {
        if (pluginMarket == null) return;
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.InstallPlugins(plugins);
                UpdateModuleDirectories();
                moduleSystem.ReloadModules();
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }

    public async Task UninstallPlugin(Plugin plugin)
    {
        if (pluginMarket == null) return;
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.UninstallPlugin(plugin);
                UpdateModuleDirectories();
                moduleSystem.ReloadModules();
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }

    public event Action? OnInstalled;
}
