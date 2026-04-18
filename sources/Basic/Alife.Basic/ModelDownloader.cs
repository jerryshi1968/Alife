namespace Alife.Basic;

/// <summary>
/// 通用的模型与资源下载引导器。
/// 负责检测文件完整性并调用独立的 WPF 下载器窗口。
/// </summary>
public static class ModelDownloader
{
    public static string ModelScopeCachePath { get; }

    /// <summary>
    /// 确保 ModelScope 模型已存在。如果缺失，启动控制台窗口进行下载。
    /// </summary>
    /// <param name="modelId">ModelScope 模型 ID (如 OpenGVLab/InternVL2_5-1B)</param>
    /// <param name="checkFileRelPath">用于校验模型是否存在的相对路径文件 (默认 config.json)</param>
    public static void EnsureModel(string modelId, string checkFileRelPath = "config.json")
    {
        string localPath = Path.Combine(ModelScopeCachePath, modelId.Replace('/', Path.DirectorySeparatorChar));
        string checkFile = Path.Combine(localPath, checkFileRelPath);
        if (File.Exists(checkFile))
            return;

        AlifeCommand.Command("python", $"-c \"from modelscope import snapshot_download; snapshot_download('{modelId}')\"");
        if (File.Exists(checkFile) == false)
            throw new FileNotFoundException($"模型下载失败或未找到校验文件：{checkFile}");
    }

    static ModelDownloader()
    {
        AlifeCommand.Command("pip", "install modelscope");
        ModelScopeCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "modelscope", "hub", "models").Replace(Path.DirectorySeparatorChar, '/');
    }
}
