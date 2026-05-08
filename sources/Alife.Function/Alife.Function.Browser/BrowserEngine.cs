using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Alife.Function.Browser;

public class BrowserEngine : IDisposable
{
    readonly WebViewWorker worker = new();

    public async Task<NavigateResult> NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource<NavigateResult>();

        await worker.EnqueueAsync(async webView =>
        {
            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                tcs.SetResult(new NavigateResult { Success = e.IsSuccess, StatusCode = (int)e.WebErrorStatus });
            }

            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate(url);
            return true;
        });

        var result = await tcs.Task;
        if (result.Success)
        {
            await WaitUntilStableAsync();
        }
        return result;
    }

    /// <summary>
    /// 等待页面加载稳定（内容长度不再剧烈变化）
    /// </summary>
    public async Task WaitUntilStableAsync(string? oldUrl = null)
    {
        await worker.EnqueueAsync(async webView =>
        {
            int lastLen = -1;
            int stableCount = 0;

            for (int i = 0; i < 20; i++)
            {
                if (oldUrl != null)
                {
                    string currentUrl = await webView.CoreWebView2.ExecuteScriptAsync("location.href");
                    if (JsonSerializer.Deserialize<string>(currentUrl) != oldUrl) break;
                }

                int currentLen = (await webView.CoreWebView2.ExecuteScriptAsync("document.body.innerText.length"))?.Length ?? 0;
                
                if (currentLen > 200 || i > 10)
                {
                    if (currentLen == lastLen) stableCount++;
                    else stableCount = 0;

                    if (stableCount >= 3) break; 
                }

                lastLen = currentLen;
                await Task.Delay(300);
            }
            return true;
        });
    }

    /// <summary>
    /// 观察当前页面，返回精简的页面结构信息
    /// </summary>
    public async Task<string> ObserveAsync(int scope = 1)
    {
        if (scope < 1) scope = 1;

        return await ExecuteScriptAsync($@"
        (function() {{
            try {{
                const scope = {scope};
                const TEXT_SIZE = 1500;
                const ELEMENT_SIZE = 40;

                const info = {{
                    title: document.title,
                    url: location.href,
                    text: ''
                }};

                if (document.body) {{
                    const fullText = (document.body.innerText || '').replace(/\s+/g, ' ').trim();
                    const textStart = (scope - 1) * TEXT_SIZE;
                    info.text = fullText.substring(textStart, textStart + TEXT_SIZE);
                }}

                const allInputs = [];
                const allLinks = [];
                let maxId = parseInt(document.body.getAttribute('data-alife-max-id') || '0');

                const scan = (root) => {{
                    if (!root) return;
                    try {{
                        const nodes = root.querySelectorAll('*');
                        for (let i = 0; i < nodes.length; i++) {{
                            const node = nodes[i];
                            try {{
                                const tagName = (node.tagName || '').toLowerCase();
                                if (!tagName || ['script', 'style', 'svg', 'path', 'meta'].includes(tagName)) continue;

                                const isInput = ['input', 'textarea', 'select'].includes(tagName) || node.contentEditable === 'true' || node.getAttribute('contenteditable') === 'true';
                                const isLink = tagName === 'a' && node.href;
                                const isBtn = tagName === 'button' || node.getAttribute('role') === 'button' || node.hasAttribute('onclick');
                                
                                let isPointer = false;
                                let isTextCursor = false;
                                if (!isInput && !isLink && !isBtn && !['html', 'body'].includes(tagName)) {{
                                    const style = window.getComputedStyle(node);
                                    if (style) {{
                                        isPointer = style.cursor === 'pointer';
                                        isTextCursor = style.cursor === 'text';
                                    }}
                                }}

                                if (isInput || isTextCursor || isLink || isBtn || isPointer) {{
                                    if (node.offsetWidth === 0 || node.offsetHeight === 0) continue;

                                    // --- Super Enhanced label extraction ---
                                    let text = (node.innerText || node.value || node.placeholder || node.title || node.alt || node.getAttribute('aria-label') || '').trim();
                                    
                                    if (!text) {{
                                        // 1. Search for associated label
                                        if (node.id) {{
                                            const lbl = document.querySelector(`label[for='${{node.id}}']`);
                                            if (lbl) text = lbl.innerText;
                                        }}
                                        // 2. Search for adjacent text nodes
                                        if (!text && node.previousSibling) text = node.previousSibling.textContent;
                                        if (!text && node.nextSibling) text = node.nextSibling.textContent;

                                        // 3. Search parent text (covers <div>Search: <input/></div>)
                                        if (!text && node.parentElement && node.parentElement.innerText.length < 60) {{
                                            text = node.parentElement.innerText;
                                        }}

                                        // 4. Fallback to name, ID, or class
                                        if (!text) {{
                                            text = node.name || node.id || (typeof node.className === 'string' ? node.className.split(' ')[0] : '');
                                        }}
                                        
                                        // 5. Image filename fallback
                                        if (!text && node.tagName === 'IMG') {{
                                            text = node.src.split('/').pop().split('.')[0];
                                        }}
                                    }}

                                    text = text.substring(0, 40).replace(/\n/g, ' ').trim();
                                    const href = node.href || '';

                                    if (isInput || isTextCursor) {{
                                        let id = node.getAttribute('data-alife-id');
                                        if (!id) {{
                                            id = (++maxId).toString();
                                            node.setAttribute('data-alife-id', id);
                                        }}
                                        allInputs.push({{ text, type: 'input', id }});
                                    }} else if (href || isPointer || isBtn) {{
                                        let linkItem = {{ text: text || (href ? '[Link]' : '[Button]'), href: href.substring(0, 150) }};
                                        if (!href || isBtn || isPointer) {{
                                            let id = node.getAttribute('data-alife-id');
                                            if (!id) {{
                                                id = (++maxId).toString();
                                                node.setAttribute('data-alife-id', id);
                                            }}
                                            linkItem.id = id;
                                            linkItem.tagName = tagName;
                                        }}
                                        allLinks.push(linkItem);
                                    }}
                                }}
                                if (node.shadowRoot) scan(node.shadowRoot);
                            }} catch (e) {{}}
                        }}
                    }} catch (e) {{}}
                }};

                scan(document);
                if (document.body) document.body.setAttribute('data-alife-max-id', maxId.toString());

                const elementStart = (scope - 1) * ELEMENT_SIZE;
                const linksPage = allLinks.slice(elementStart, elementStart + ELEMENT_SIZE);
                
                const linkScopes = Math.ceil(allLinks.length / ELEMENT_SIZE);
                const totalScopes = Math.max(Math.ceil(((document.body ? document.body.innerText.length : 0)) / TEXT_SIZE), linkScopes, 1);

                // --- Build custom layout ---
                let output = `TITLE:${{document.title}}\nURL:${{location.href}}\nSTATUS:${{scope}}/${{totalScopes}}\n`;
                output += `${{info.text}}\n\n`;

                let componentsStr = """";
                allInputs.forEach(i => {{
                    componentsStr += `${{i.text}}:${{i.type}}[${{i.id}}]\n`;
                }});
                linksPage.forEach(l => {{
                    if (l.id) {{
                        componentsStr += `${{l.text}}:${{l.tagName || 'button'}}[${{l.id}}]\n`;
                    }}
                }});
                if (componentsStr) output += `-- COMPONENTS (ID) --\n${{componentsStr}}`;

                let linksStr = """";
                linksPage.forEach(l => {{
                    if (l.href) {{
                        linksStr += `${{l.text}}:${{l.href}}\n`;
                    }}
                }});
                if (linksStr) output += `-- LINKS (HREF) --\n${{linksStr}}`;

                return output.trim();
            }} catch (err) {{
                return ""ERROR: "" + err.toString();
            }}
        }})()");
    }

    /// <summary>
    /// 在当前页面执行 JavaScript 并返回结果
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        string rawRes = await worker.EnqueueAsync(async webView =>
        {
            try
            {
                return await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize("ERROR: JS 引擎异常 - " + ex.Message);
            }
        });

        if (string.IsNullOrEmpty(rawRes) || rawRes == "null") return null;

        try
        {
            using var doc = JsonDocument.Parse(rawRes);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return doc.RootElement.GetString();
            }
            return rawRes;
        }
        catch
        {
            return rawRes;
        }
    }

    /// <summary>
    /// 通过 HttpClient 下载文件到本地
    /// </summary>
    public static async Task DownloadFileAsync(string url, string savePath)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        var bytes = await client.GetByteArrayAsync(url);

        string? dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(savePath, bytes);
    }

    public void Dispose() => worker.Dispose();
}

public class NavigateResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
}
