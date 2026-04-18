using Alife.Basic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Alife.Framework;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("Python工具", "借助Python，让AI几乎可以执行任何任务！")]
[Description(@"此服务能让你获得执行python的能力，可用于文件管理、设备控制、网页爬取等各自复杂的自定义需求。
如果缺少环境你还可以利用`subprocess.check_call([sys.executable, ""-m"", ""pip"", ""install"", package_name])`来安装环境。")]
public class PythonService : Plugin
{
    [XmlFunction]
    [Description(@"执行python脚本（使用后需要等待系统响应，所以只能放句尾使用）。
注意事项：
1. 用户看不到结果也无法交互，所以不要编写需要用户操作的代码，否则会导致进程卡死（如果需要异步，你可以尝试自己单独创建一个进程）。
2. 要极简代码量，只写必要代码，不要写注释、异常判断等非必要代码，能一行解决就不要两行。（因为这个非常烧token，烧完你就宕机了！）")]
    public async Task Python(XmlExecutorContext context, [XmlContent] string _)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string filePath = storageSystem.GetTempPath("pythonScript.py");
        await File.WriteAllTextAsync(filePath, context.FullContent);
        ProcessStartInfo startInfo = new() {
            FileName = "python",
            Arguments = filePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        string output;
        try
        {
            CancellationTokenSource cts = new(3000);
            await process.WaitForExitAsync(cts.Token);
            output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (process.HasExited == false)
                process.Kill();
            throw new TimeoutException();
        }

        if (process.ExitCode != 0)
            output = await process.StandardError.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(output) == false)
            chatBot.Poke("[PythonService] Python执行结果如下：" + output);
    }

    readonly StorageSystem storageSystem;
    ChatBot chatBot = null!;

    public PythonService(StorageSystem storageSystem, InterpreterService interpreterService)
    {
        AlifeCommand.EnsureInitialized();
        this.storageSystem = storageSystem;
        interpreterService.RegisterHandler(this);
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        return Task.CompletedTask;
    }
}
