using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Browser;

[Module("浏览器工具", "让AI可以像人一样操控真实的浏览器，从而能够执行各种网页任务的同时，避免反爬。",
    defaultCategory: "Alife 官方/实用工具")]
public class BrowserService(XmlFunctionCaller functionService)
    : InteractiveModule<BrowserService>, IDisposable
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("同时只能打开一个页面，打开自动顶掉前一个，使用后需等待结果返回")]
    public async Task OpenWebsite(string url)
    {
        if (browser.HasActivePopup)
        {
            Poke("当前处于弹出窗口中，无法直接导航。请先关闭弹出窗口");
            return;
        }
        browser.EnsureVisible();
        await browser.NavigateAsync(url);
        Poke("已打开网站");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public async Task ClosePopupWindow()
    {
        bool closed = await browser.CloseTopPopupAsync();
        Poke(closed ? "已关闭弹出窗口" : "当前没有弹出窗口");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("会自动探测可交互元素并打上data-alife-id，然后以[ID:文本]返回")]
    public async Task ViewWebsite([Description("观察的页码，从1开始")] int page)
    {
        string result = await browser.ObserveAsync(page);
        Poke($"当前页码内容如下：\n{result}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看View结果中被标记的可交互元素信息，如他们的href,type等")]
    public async Task GetWebsiteElementInfo([Description("元素的data-alife-id")] int id)
    {
        string result = await browser.GetElementInfoAsync(id);
        Poke($"元素详情：\n{result}");
    }

    [XmlFunction(FunctionMode.Content)]
    public async Task RunWebsiteJs(XmlExecutorContext context, [XmlContent] string script)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string code = context.FullContent.Trim();
            string result = await browser.ExecuteScriptAsync(code);
            Poke($"JS执行结果：\n{result}");
        }
    }
    
    readonly BrowserEngine browser = new();

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        await browser.WaitToLoadedAsync(TimeSpan.FromSeconds(3));

        XmlHandler xmlHandler = new(this) {
            Description = "你的专属浏览器，当你需要上网查资料时使用",
            Explanation = """
                          1. 遇到验证或登录，可请求用户，绕过反爬
                          2. 优先使用高质量搜索引擎，开源、个人网站，并跳过收费网站（如爱给网、百度文库等）
                          3. 此浏览器叫`Alife Browser`，注意区分

                          交互示例
                          获取元素：let e=document.querySelector('[data-alife-id="1"]');
                          点击元素：e.click()
                          输入文本：e.focus();e.value='';document.execCommand('insertText',false,'内容');

                          注意事项
                          1. 示例方法不一定有效，要学会变通
                          2. 每次交互后一定要检查结果，比如输入文本后文本是否变化
                          3. 很多时候不是你的思路有问题，而是元素操作失误，不要过于依赖data-alife-id
                          """
        };
        functionService.RegisterHandler(xmlHandler,DocumentMode.Implicit);
        functionService.AddPlainAreas(nameof(RunWebsiteJs));
    }

    public void Dispose() => browser.Dispose();
}
