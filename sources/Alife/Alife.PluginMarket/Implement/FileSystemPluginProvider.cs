using Newtonsoft.Json;

namespace Alife.PluginMarket;

public class FileSystemPluginProvider(string directoryPath) : IPluginProvider
{
    public Task<Plugin[]> GetPluginsAsync()
    {
        if (!Directory.Exists(directoryPath))
            return Task.FromResult(Array.Empty<Plugin>());

        string[] files = Directory.GetFiles(directoryPath, "*.json");
        List<Plugin> plugins = new();

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                Plugin? plugin = JsonConvert.DeserializeObject<Plugin>(json);
                if (plugin != null)
                    plugins.Add(plugin);
            }
            catch
            {
                // 忽略解析失败的文件
            }
        }

        return Task.FromResult(plugins.ToArray());
    }
}
