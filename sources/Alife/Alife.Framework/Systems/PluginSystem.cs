using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Alife.Platform;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace Alife.Framework;

public class PluginLoadContext(string pluginDirectory) : AssemblyLoadContext("PluginContext", isCollectible: true)
{
    public Dictionary<Assembly, string> AssemblyPaths => assemblyPaths;

    public void LoadDll(string dllPath)
    {
        using var assemblyStream = new MemoryStream(File.ReadAllBytes(dllPath));
        string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        Assembly assembly = LoadFromStream(assemblyStream, pdbStream);
        pdbStream?.Dispose();
        assemblyPaths.Add(assembly, dllPath);
    }

    readonly string baseDirectory = Path.Combine(pluginDirectory, "BaseDirectory");
    readonly string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? $"win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? $"linux-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
            : $"osx-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
    readonly string[] searchPaths = new string[4];

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string path = Path.Combine(baseDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            string pdbPath = Path.ChangeExtension(path, ".pdb");
            MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
            Assembly assembly = LoadFromStream(stream, pdbStream);
            pdbStream?.Dispose();
            return assembly;
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (Path.HasExtension(unmanagedDllName) == false)
            unmanagedDllName += ".dll";
        searchPaths[0] = Path.Combine(baseDirectory, "runtimes", rid, "native", unmanagedDllName);
        searchPaths[1] = Path.Combine(baseDirectory, unmanagedDllName);
        searchPaths[2] = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", unmanagedDllName);
        searchPaths[3] = Path.Combine(AppContext.BaseDirectory, unmanagedDllName);

        foreach (string path in searchPaths)
        {
            if (File.Exists(path))
                return LoadUnmanagedDllFromPath(path);
        }

        if (NativeLibrary.TryLoad(unmanagedDllName, out IntPtr handle))
            return handle;

        return IntPtr.Zero;
    }

    readonly Dictionary<Assembly, string> assemblyPaths = new();
}

public class PluginSystem
{
    public Assembly? GetPluginAssembly()
    {
        return pluginAssemblies?.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Plugins");
    }
    public string GetPluginFolder<T>()
    {
        return Path.Combine(pluginRoot, GetType().Namespace!);
    }
    public string GetPluginFolderRoot()
    {
        return pluginRoot;
    }
    public StringFolder GetPluginFolder()
    {
        return pluginFolder;
    }
    public IEnumerable<Type> GetAllPlugins()
    {
        return pluginTypes.Values;
    }
    public Type? GetPlugin(string pluginID)
    {
        return pluginTypes.GetValueOrDefault(pluginID);
    }
    public string GetPluginID(Type pluginType)
    {
        return pluginType.FullName!;
    }
    public void ReloadPlugins()
    {
        //确保插件文件夹存在，防止报错]
        if (Directory.Exists(pluginRoot) == false)
            Directory.CreateDirectory(pluginRoot);

        ReloadContext(CompilePlugin(pluginRoot));
    }
    public PluginLoadContext CompilePlugin(string source)
    {
        PluginLoadContext compilingContext = new(source);
        try
        {
            //加载dll
            foreach (string file in Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    string assemblyName = AssemblyName.GetAssemblyName(file).FullName;
                    if (defaultAssemblies.Contains(assemblyName) == false)
                        compilingContext.LoadDll(file);
                }
                catch
                {
                    // ignored
                }
            }

            //编译cs
            {
                string dllPath = Path.Combine(AlifePath.TempFolderPath, "Plugins.dll");
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

                //解析语法树
                var syntaxTrees = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
                    .Select(file => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(file),
                    new CSharpParseOptions(LanguageVersion.Latest)))
                    .ToList();

                //收集元数据引用（去重）
                var references = new List<MetadataReference>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                        continue;
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
                }
                foreach (var path in compilingContext.AssemblyPaths.Values)
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }

                //编译
                var compilation = CSharpCompilation.Create(
                "Plugins",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithAllowUnsafe(true)
                    .WithOptimizationLevel(OptimizationLevel.Release));

                var emitResult = compilation.Emit(dllPath, pdbPath);

                if (!emitResult.Success)
                {
                    var errors = string.Join("\n", emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                    throw new Exception($"插件编译失败:\n{errors}");
                }

                compilingContext.LoadDll(dllPath);
            }

            return compilingContext;
        }
        catch
        {
            compilingContext.Unload();
            throw;
        }
    }
    public void SaveData()
    {
        storageSystem.SetObject(pluginSystemConfig, pluginFolder);
    }

    readonly string pluginRoot = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
    readonly string pluginSystemConfig = "PluginCategory";
    readonly StorageSystem storageSystem;
    readonly Dictionary<string, Type> pluginTypes;
    readonly StringFolder pluginFolder;
    readonly HashSet<string> defaultAssemblies;
    readonly Assembly[] thisAssemblies;
    AssemblyLoadContext? pluginAssemblies;

    public PluginSystem(StorageSystem storageSystem, ILogger<PluginSystem> logger)
    {
        this.storageSystem = storageSystem;

        pluginTypes = new Dictionary<string, Type>();
        pluginFolder = storageSystem.GetObject(pluginSystemConfig, new StringFolder("全部插件"))!;

        //预热程序集，因为插件可能依赖Alife自身的程序集，结果Alife本身目前未用到，导致未加载
        PreloadAllAssemblies();
        defaultAssemblies = AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.FullName).ToHashSet()!;
        thisAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Alife") ?? false).ToArray();

        try
        {
            ReloadPlugins();
        }
        catch (Exception e)
        {
            logger.LogError(e, "加载插件失败");
        }
    }

    void ReloadContext(AssemblyLoadContext context)
    {
        //替换上下文
        pluginTypes.Clear();
        if (pluginAssemblies != null)
            pluginAssemblies.Unload();
        pluginAssemblies = context;

        //统计Plugin
        foreach (Assembly assembly in pluginAssemblies.Assemblies.Union(thisAssemblies))
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<PluginAttribute>() == null)
                    continue;
                if (type.IsAbstract)
                    continue;
                if (type.IsInterface)
                    continue;

                pluginTypes.Add(GetPluginID(type), type);
            }
        }

        SyncFolder();
    }
    void SyncFolder()
    {
        HashSet<string> currentPlugins = pluginTypes.Keys.ToHashSet();

        //移除无效插件，同时如果有效则剔除
        pluginFolder.RemoveAll(name => currentPlugins.Remove(name) == false);
        //剩下的就是还没有的插件，添加到根目录
        foreach (var typeName in currentPlugins)
        {
            PluginAttribute? pluginAttribute = pluginTypes[typeName].GetCustomAttribute<PluginAttribute>();
            if (pluginAttribute == null)
                continue;

            StringFolder folder = pluginFolder;
            string[] path = pluginAttribute.DefaultCategory.Split("/", StringSplitOptions.RemoveEmptyEntries);
            foreach (string subFolderName in path)
            {
                string name = subFolderName;
                StringFolder? subFolder = folder.Folders.FirstOrDefault(subFolder => subFolder.Name == name);
                if (subFolder == null)
                {
                    subFolder = new StringFolder(subFolderName);
                    folder.Folders.Add(subFolder);
                }

                folder = subFolder;
            }

            folder.Strings.Add(typeName);
        }
    }

    void PreloadAllAssemblies()
    {
        var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(Assembly.GetEntryAssembly()!);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                // 如果这个程序集还没被加载过
                if (!loadedAssemblies.Contains(reference.FullName))
                {
                    try
                    {
                        // 强制加载它
                        var loaded = Assembly.Load(reference);
                        queue.Enqueue(loaded);
                        loadedAssemblies.Add(reference.FullName);
                    }
                    catch
                    {
                        // 忽略加载失败的程序集（有些可能是环境相关的）
                    }
                }
            }
        }
    }
}
