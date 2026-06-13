using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Browser;

[Module("网上冲浪", "让AI可以像人一样操控真实的浏览器，从而能够执行各种网页任务的同时，避免反爬。",
    defaultCategory: "Alife 官方/实用工具")]
public class BrowserService(XmlFunctionCaller functionService)
    : InteractiveModule<BrowserService>, IDisposable
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("打开网页。")]
    public async Task Navigate(string url)
    {
        await browser.NavigateAsync(url);
        Poke($"[Navigate] 已打开: {url}（接下来可以使用 observe 来查看页面内容）");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看页面内容（注意！内容过多时会被分页，所以当你没看到想要的内容时，可以尝试用 page 翻页。可交互元素会以[ID:文本]格式标记，需要获取元素详情（如href）时使用 GetElementInfo）")]
    public async Task Observe([Description("观察的页面区域，从1开始")] int page)
    {
        string result = await browser.ObserveAsync(page);
        Poke($"页面结果如下（注意！网站页面大多不能一次全显示，必须通过 page 翻页来查看完整内容。此外若遇到人机验证或登录，可请求主人协助）：\n{result}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查询页面中指定ID元素的详细信息（href、type、placeholder等），ID来自Observe中标记的[ID:文本]")]
    public async Task GetElementInfo([Description("元素的data-alife-id")] int id)
    {
        string result = await browser.GetElementInfoAsync(id);
        Poke($"[GetElementInfo] 元素详情：\n{result}");
    }

    [XmlFunction(FunctionMode.Content)]
    [Description("执行JS表达式（这只能在浏览器沙盒中使用，不能执行全局性脚本操作）")]
    public async Task RunJs(XmlExecutorContext context, [XmlContent] string script)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string code = context.FullContent.Trim();
            string result = await browser.ExecuteScriptAsync(code);
            Poke($"[RunJS] 执行结果：\n{result}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("下载文件。")]
    public async Task Download([Description("下载链接")] string url, [Description("本地绝对路径")] string path)
    {
        await AlifePlatform.DownloadFileAsync(url, path);
        Poke($"[Download] 文件已下载至：{path}");
    }

    readonly BrowserEngine browser = new();

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        await browser.WaitToLoadedAsync(TimeSpan.FromSeconds(3));

        XmlHandler xmlHandler = new(this) {
            Description = "当你需要使用浏览器上网时调用",
            Explanation = """
                          你拥有一个独属于自己的真实浏览器，可借此进行网上冲浪，每天学点新知识，找点新话题。

                          ## 使用提示

                          1. 若遇到验证或登录，可以请求主人协助，从而避免被反爬。
                          2. 办事前先明确需求，再行动。
                          3. 优先使用搜索引擎`谷歌 > 必应 > 百度`
                          """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Implicit);
        functionService.AddPlainAreas(nameof(RunJs));
    }

    public void Dispose() => browser.Dispose();
}
