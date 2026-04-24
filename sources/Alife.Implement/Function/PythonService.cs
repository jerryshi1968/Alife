using Alife.Basic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Alife.Framework;
using Alife.Function.Interpreter;

namespace Alife.Implement;

public partial class PythonService
{
    public static async Task<string> Python(string path, int timeout = 30)
    {
        ProcessStartInfo startInfo = new() {
            FileName = "python",
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new();
        process.StartInfo = startInfo;
        process.Start();

        try
        {
            CancellationTokenSource cts = new(timeout * 1000);

            // 同时开始读取输出和错误流，防止缓冲区满导致进程死锁
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode == 0)
                return await outputTask;
            else
                throw new Exception(await errorTask);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("脚本执行堵塞或超时（注意不要写需要交互的代码，如果需要展示结果等可以单独创建一个进程）");
        }
        finally
        {
            if (process.HasExited == false)
                process.Kill();
        }
    }

    static PythonService()
    {
        AlifeCommand.EnsureInitialized();
    }
}
[Plugin("Python工具", "借助Python，让AI几乎可以执行任何任务！")]
[Description(@"此服务能让你获得执行python的能力，可用于文件管理、设备控制、网页爬取等各自复杂的自定义需求。
如果缺少环境你还可以利用`subprocess.check_call([sys.executable, ""-m"", ""pip"", ""install"", package_name])`来安装环境。")]
public partial class PythonService : InteractivePlugin<PythonService>
{
    [XmlFunction]
    [Description(@"执行python脚本（使用后需要等待系统响应，所以只能放句尾使用）。
注意事项：
1. 用户看不到结果也无法交互，所以不要编写需要用户操作的代码，否则会导致进程卡死（如果需要异步，你可以尝试自己单独创建一个进程）。
2. 要极简代码量，只写必要代码，不要写注释、异常判断等非必要代码，能一行解决就不要两行。（因为这个非常烧token，烧完你就宕机了！）")]
    public async Task Python(XmlExecutorContext context, [XmlContent] string _, [Description("预计执行时间（单位秒）（避免执行阻塞操作）")] int timeout = 30)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string filePath = $"{AlifePath.TempFolderPath}/pythonScript.py";
        await File.WriteAllTextAsync(filePath, context.FullContent.Trim());

        string result = await Python(filePath, timeout);
        Poke("脚本执行完成\n" + result);
    }

    public PythonService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }
}
