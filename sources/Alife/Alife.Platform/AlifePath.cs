using System.IO;

namespace Alife.Platform;

public static class AlifePath
{
    [Obsolete("不应该依赖应用根目录文件，这会有权限问题，且不利于分发更新。")]
    public static string RootFolderPath { get; private set; }
    [Obsolete("不应该依赖应用根目录文件，这会有权限问题，且不利于分发更新。")]
    public static string OutputsFolderPath { get; }
    public static string StorageFolderPath { get; private set; }
    public static string RuntimeFolderPath { get; private set; }
    public static string TempFolderPath { get; }

    public static void SetStorageFolderPath(string path)
    {
        StorageFolderPath = MigrateDirectory(StorageFolderPath, path, "存储");
        AlifeConfig.SetString("storage_path", StorageFolderPath);
    }

    public static void SetRuntimeFolderPath(string path)
    {
        RuntimeFolderPath = MigrateDirectory(RuntimeFolderPath, path, "运行时");
        AlifeConfig.SetString("runtime_path", RuntimeFolderPath);
    }

    static string MigrateDirectory(string oldPath, string newPath, string label)
    {
        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return oldPath;

        try
        {
            if (!Directory.Exists(newPath) && Directory.Exists(oldPath))
            {
                string? parent = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                Directory.Move(oldPath, newPath);
                AlifeTerminal.LogHint($"检测到{label}位置变更，数据已从 {oldPath} 迁移至 {newPath}");
            }
            else if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
            }
        }
        catch (Exception ex)
        {
            AlifeTerminal.LogError($"{label}位置迁移失败: {ex.Message}");
            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);
        }

        return newPath;
    }

    static AlifePath()
    {
        OutputsFolderPath = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        RootFolderPath = Path.GetDirectoryName(OutputsFolderPath) ?? "";

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        StorageFolderPath = Directory.Exists(Path.Combine(RootFolderPath, "Storage"))
            ? Path.Combine(RootFolderPath, "Storage")
            : Path.Combine(documentsPath, "Alife", "Storage");
        RuntimeFolderPath = Directory.Exists(Path.Combine(RootFolderPath, "Runtime"))
            ? Path.Combine(RootFolderPath, "Runtime")
            : Path.Combine(documentsPath, "Alife", "Runtime");
        TempFolderPath = Path.Combine(Path.GetTempPath(), "Alife.Client");

        string configRuntime = AlifeConfig.GetString("runtime_path");
        if (!string.IsNullOrEmpty(configRuntime))
            RuntimeFolderPath = configRuntime;

        string configStorage = AlifeConfig.GetString("storage_path");
        if (!string.IsNullOrEmpty(configStorage))
            StorageFolderPath = configStorage;

        if (Directory.Exists(TempFolderPath))
            Directory.Delete(TempFolderPath, recursive: true);

        Directory.CreateDirectory(StorageFolderPath);
        Directory.CreateDirectory(RuntimeFolderPath);
        Directory.CreateDirectory(TempFolderPath);
    }
}
