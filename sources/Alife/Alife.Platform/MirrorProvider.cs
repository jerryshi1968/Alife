using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Platform;

/// <summary>
/// 镜像配置提供器，用于管理各种镜像源配置。
/// </summary>
public static class MirrorProvider
{
    /// <summary>
    /// URL 替换映射表。Key 为原始 URL 前缀，Value 为镜像 URL 前缀。
    /// </summary>
    public static Dictionary<string, string> MirrorUrlMap { get; private set; }
    /// <summary>
    /// 环境变量配置表。启动 Python 进程时会自动设置这些环境变量。
    /// </summary>
    public static Dictionary<string, string> MirrorEnvironmentVariables { get; private set; }

    /// <summary>
    /// 根据 MirrorUrlMap 替换 URL 中的匹配字符串。
    /// </summary>
    /// <param name="url">原始 URL</param>
    /// <returns>替换后的 URL</returns>
    public static string TransformUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        foreach (var (old, replacement) in MirrorUrlMap)
        {
            url = url.Replace(old, replacement, StringComparison.OrdinalIgnoreCase);
        }

        return url;
    }
    /// <summary>
    /// 设置环境变量，使 Python 进程能够使用镜像源。
    /// 应在应用程序启动时调用一次。
    /// </summary>
    public static void SetupEnvironment()
    {
        foreach (var (key, value) in MirrorEnvironmentVariables)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// 从配置文件加载镜像配置。
    /// </summary>
    public static void Load()
    {
        // 加载 URL 映射
        string urlMapJson = AlifeConfig.GetString(UrlMapKey);
        if (!string.IsNullOrEmpty(urlMapJson))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(urlMapJson);
                if (loaded is not null)
                    MirrorUrlMap = loaded;
            }
            catch {}
        }

        // 加载环境变量
        string envVarsJson = AlifeConfig.GetString(EnvVarsKey);
        if (!string.IsNullOrEmpty(envVarsJson))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(envVarsJson);
                if (loaded is not null)
                    MirrorEnvironmentVariables = loaded;
            }
            catch {}
        }
    }
    /// <summary>
    /// 保存当前镜像配置到配置文件。
    /// </summary>
    public static void Save()
    {
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };

        AlifeConfig.SetString(UrlMapKey, System.Text.Json.JsonSerializer.Serialize(MirrorUrlMap, options));
        AlifeConfig.SetString(EnvVarsKey, System.Text.Json.JsonSerializer.Serialize(MirrorEnvironmentVariables, options));
    }

    /// <summary>
    /// 更新 URL 映射并保存。
    /// </summary>
    public static void SetMirrorUrlMap(Dictionary<string, string> urlMap)
    {
        MirrorUrlMap = urlMap;
        Save();
    }
    /// <summary>
    /// 更新环境变量配置并保存。
    /// </summary>
    public static void SetMirrorEnvironmentVariables(Dictionary<string, string> envVars)
    {
        MirrorEnvironmentVariables = envVars;
        Save();
    }

    /// <summary>
    /// 重置为默认配置。
    /// </summary>
    public static void ResetToDefaults()
    {
        MirrorUrlMap = DefaultMirrorUrlMap;
        MirrorEnvironmentVariables = DefaultMirrorEnvironmentVariables;

        Save();
    }

    const string UrlMapKey = "mirror_url_map";
    const string EnvVarsKey = "mirror_env_vars";
    static readonly Dictionary<string, string> DefaultMirrorUrlMap = new() {
        { "https://github.com", "https://v4.gh-proxy.org/https://github.com" },
        { "https://api.github.com", "https://v4.gh-proxy.org/https://api.github.com" },
        { "https://raw.githubusercontent.com", "https://v4.gh-proxy.org/https://raw.githubusercontent.com" },
        { "--index-url https://download.pytorch.org/whl/cu128", "--find-links https://mirrors.aliyun.com/pytorch-wheels/cu128" }
    };
    static readonly Dictionary<string, string> DefaultMirrorEnvironmentVariables = new() {
        { "HF_ENDPOINT", "https://hf-mirror.com" },
        { "PIP_INDEX_URL", "https://mirrors.aliyun.com/pypi/simple/" },
        { "PIP_TRUSTED_HOST", "mirrors.aliyun.com" },
    };

    static MirrorProvider()
    {
        MirrorUrlMap = DefaultMirrorUrlMap;
        MirrorEnvironmentVariables = DefaultMirrorEnvironmentVariables;

        Load();
    }
}
