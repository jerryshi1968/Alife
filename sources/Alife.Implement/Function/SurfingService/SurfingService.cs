using System.ComponentModel;
using System.Text.Json;
using Alife.Framework;
using Alife.Function.Browser;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Implement.Function;

[Plugin("网上冲浪", "让 AI 像人一样操控浏览器：打开网页、观察页面、点击、打字、滚动、执行脚本。")]
[Description(@"你拥有一个真实的浏览器窗口。你可以通过 observe 或 navigate 获取由系统动态分配了 [ID] 的组件。
操作提示：在使用 runjs 时，请务必使用属性选择器 `[data-alife-id='ID']` 来精准定位并操控这些组件。
注意：
1. 若遇到验证或登录，可请求主人协助。
2. 优先使用搜索引擎（谷歌 > 必应 > 百度）明确需求。")]
public class SurfingService(FunctionService functionService)
    : InteractivePlugin<SurfingService>, IDisposable
{
    readonly BrowserEngine browser = new();

    [XmlFunction("navigate")]
    [Description("打开指定网址。成功后会自动为页面组件分配 [ID] 并返回观察结果。")]
    public async Task Navigate(XmlExecutorContext context,
        [Description("要打开的网址")] string url)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        var result = await browser.NavigateAsync(url);
        if (result.Success)
        {
            string observation = await browser.ObserveAsync();
            Poke($"[Navigate] 已打开: {url}\n[Auto-Observe] 页面内容：\n{observation}");
        }
        else
        {
            Poke($"[Navigate] 加载失败 (HTTP {result.StatusCode})");
        }
    }


    [XmlFunction("observe")]
    [Description("观察当前页面：系统会自动为交互组件分配 [ID] 并返回其描述。")]
    public async Task Observe(XmlExecutorContext context,
        [Description("观察区域索引（用于翻页），从 1 开始，默认 1")] int scope = 1)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        string result = await browser.ObserveAsync(scope);
        Poke($"[Observe] 页面状态：\n{result}");
    }

    [XmlFunction("runjs")]
    [Description("执行 JavaScript。提示：使用 `document.querySelector(\"[data-alife-id='ID']\")` 定位 observe 返回的组件。建议将代码写在标签内容中。")]
    public async Task ExecuteScript(XmlExecutorContext context, [XmlContent] string script = "")
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string code = context.FullContent.Trim();
        // 使用自执行函数包裹，确保能捕获返回值
        string wrappedCode = JsonSerializer.Serialize(code);
        string safeScript = $@"
        (function() {{
            try {{
                let r = eval({wrappedCode});
                if (r instanceof Promise) return r.then(v => JSON.stringify(v));
                return JSON.stringify(r === undefined ? '(null/undefined)' : r);
            }} catch (e) {{
                return 'ERROR: ' + e.toString();
            }}
        }})()";

        string result = await browser.ExecuteScriptAsync(safeScript);
        Poke($"[RunJS] 执行结果：\n{result}");
    }



    [XmlFunction("download")]
    [Description("下载文件到本地。")]
    public async Task Download(XmlExecutorContext context,
        [Description("下载链接")] string url,
        [Description("本地绝对路径")] string path)
    {
        if (context.CallMode != CallMode.OneShot)
            throw new Exception("请使用自闭合标签调用。");

        await BrowserEngine.DownloadFileAsync(url, path);
        Poke($"[Download] 文件已下载至：{path}");
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionService.RegisterHandler(this, "runjs");
    }

    public void Dispose() => browser.Dispose();
}