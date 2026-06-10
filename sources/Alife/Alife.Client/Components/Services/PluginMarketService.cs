using System.IO;
using Alife.Platform;
using Alife.PluginMarket;

namespace Alife.Components.Services;

public class PluginMarketService
{
    readonly Alife.PluginMarket.PluginMarket pluginMarket;
    readonly FileSystemPluginManager localManager;

    public PluginMarketService()
    {
        string pluginDir = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
        string installedDir = Path.Combine(AlifePath.StorageFolderPath, "Plugins_Installed");
        string packageListFile = Path.Combine(installedDir, "NUGET_PACKAGES.txt");

        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(installedDir);

        var onlineProvider = new GithubPluginProvider("BDFFZI", "Alife.PluginMarket");
        localManager = new FileSystemPluginManager(installedDir);

        Dictionary<string, IEnvironmentInstaller> environmentInstallers = new()
        {
            { "nuget", new NuGetEnvironmentInstaller(packageListFile) },
            { "pip", new PipEnvironmentInstaller() }
        };

        pluginMarket = new Alife.PluginMarket.PluginMarket(onlineProvider, localManager, localManager, environmentInstallers);
    }

    public async Task InitializeAsync()
    {
        await pluginMarket.InitializeAsync();
    }

    public Plugin[] GetAllPlugins() => pluginMarket.GetAllPlugins().ToArray();

    public void RefreshLocalPlugins() => pluginMarket.RefreshLocalPlugins();

    public async Task FetchOnlinePluginsAsync() => await pluginMarket.FetchOnlinePluginsAsync();

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
            .OrderByDescending(v => v)
            .FirstOrDefault();

        return latestVersion != null && latestVersion != installedVersion;
    }

    public string? GetLatestVersion(Plugin plugin)
    {
        return plugin.Releases?.Keys
            .OrderByDescending(v => v)
            .FirstOrDefault();
    }

    public async Task InstallPlugin(Plugin plugin, string version)
    {
        await pluginMarket.InstallPlugin(plugin, version);
        OnInstalled?.Invoke();
    }

    public async Task UninstallPlugin(Plugin plugin)
    {
        await pluginMarket.UninstallPlugin(plugin);
        OnInstalled?.Invoke();
    }

    public event Action? OnInstalled;
}
