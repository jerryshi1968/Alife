using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Alife.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Function.Browser;

/// <summary>
/// 在独立的 STA 线程中管理 WebView2，以支持后台执行浏览器任务。
/// 浏览器窗口默认可见，用户可实时观察 AI 的浏览行为并手动介入。
/// </summary>
public class WebViewWorker : IDisposable
{
    public bool IsNavigating => isNavigating;
    public bool IsLoaded => isLoaded;

    public void EnsureVisible()
    {
        if (window == null) return;
        window.Dispatcher.Invoke(() => {
            window.WindowState = WindowState.Normal;
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        });
    }

    public Task<T> AddFormTask<T>(Func<WebView2, Task<T>> action)
    {
        if (window == null)
            throw new ObjectDisposedException(nameof(WebViewWorker));

        TaskCompletionSource<T> tcs = new();

        formTasks.Add(async () => {
            try
            {
                var active = ActiveWebView;
                T result = await action(active);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    Window? window;
    BrowserWindowContent? browserContent;
    WebView2? webView;
    readonly BlockingCollection<Func<Task>> formTasks = new();
    bool isNavigating;
    bool isLoaded;
    CoreWebView2Environment? env;

    readonly object popupLock = new();
    readonly Stack<WebView2> popupStack = new();
    readonly Dictionary<WebView2, Window> popupWindowMap = new();

    WebView2 ActiveWebView
    {
        get
        {
            lock (popupLock)
                return popupStack.Count > 0 ? popupStack.Peek() : webView!;
        }
    }

    public bool HasActivePopup
    {
        get
        {
            lock (popupLock)
                return popupStack.Count > 0;
        }
    }

    public int PopupCount
    {
        get
        {
            lock (popupLock)
                return popupStack.Count;
        }
    }

    void RemovePopupFromStack(WebView2 popup)
    {
        lock (popupLock)
        {
            if (popupStack.Count == 0) return;
            if (popupStack.Peek() == popup)
            {
                popupStack.Pop();
                return;
            }
            var temp = new List<WebView2>();
            while (popupStack.Count > 0)
            {
                var item = popupStack.Pop();
                if (item == popup) break;
                temp.Add(item);
            }
            for (int i = temp.Count - 1; i >= 0; i--)
                popupStack.Push(temp[i]);
        }
    }

    public WebViewWorker()
    {
        var thread = new Thread(() => {
            try
            {
                window = new UnclosableWindow {
                    Title = "Alife Browser",
                    Width = 1024,
                    Height = 768,
                    WindowState = WindowState.Minimized,
                    ShowInTaskbar = true,
                    ResizeMode = ResizeMode.CanResize,
                };
                browserContent = new BrowserWindowContent();
                webView = browserContent.WebView;
                window.Content = browserContent;
                window.Loaded += OnWindowLoaded;
                window.Closing += (_, _) => System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();

                window.Show();
                window.Activate();
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public async Task<bool> CloseTopPopupAsync()
    {
        Window? popupWindow;
        lock (popupLock)
        {
            if (popupStack.Count == 0) return false;
            if (!popupWindowMap.TryGetValue(popupStack.Peek(), out popupWindow)) return false;
        }

        await popupWindow.Dispatcher.InvokeAsync(() => popupWindow.Close());
        return true;
    }

    public void Dispose()
    {
        formTasks.CompleteAdding();
        var popupWindows = new List<Window>();
        lock (popupLock)
        {
            foreach (var w in popupWindowMap.Values)
                popupWindows.Add(w);
            popupStack.Clear();
            popupWindowMap.Clear();
        }
        foreach (var pw in popupWindows)
            pw.Dispatcher.Invoke(() => pw.Close());
        if (window != null)
            window.Dispatcher.Invoke(() => window.Close());
    }

    async void OnWindowLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(AlifePath.RuntimeFolderPath, "WebView2Data");
            if (!Directory.Exists(userDataFolder))
                Directory.CreateDirectory(userDataFolder);
            env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await webView!.Dispatcher.InvokeAsync(async () => {
                await webView!.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
                webView.CoreWebView2.NewWindowRequested += async (_, ev) => {
                    var deferral = ev.GetDeferral();
                    try
                    {
                        var mainSource = webView.Source;
                        var popupWindow = new Window {
                            Title = "Alife.Client Popup",
                            Width = 800,
                            Height = 600,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        };
                        var popupWebView = new WebView2();
                        popupWindow.Content = popupWebView;

                        var loadedTcs = new TaskCompletionSource();
                        popupWindow.Loaded += (_, _) => loadedTcs.SetResult();
                        popupWindow.Show();
                        await loadedTcs.Task;

                        await popupWebView.EnsureCoreWebView2Async(env);
                        popupWebView.CoreWebView2.Settings.UserAgent =
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";

                        var mainHost = mainSource?.Host ?? "";
                        CancellationTokenSource? autoCloseCts = null;
                        void TryAutoClosePopup(CoreWebView2 cwv)
                        {
                            try
                            {
                                if (Uri.TryCreate(cwv.Source, UriKind.Absolute, out var popupUri) &&
                                    mainHost.Length > 0 &&
                                    (popupUri.Host.Equals(mainHost, StringComparison.OrdinalIgnoreCase) ||
                                     popupUri.Host.EndsWith("." + mainHost, StringComparison.OrdinalIgnoreCase) ||
                                     mainHost.EndsWith("." + popupUri.Host, StringComparison.OrdinalIgnoreCase)))
                                {
                                    autoCloseCts?.Cancel();
                                    autoCloseCts = new CancellationTokenSource();
                                    var token = autoCloseCts.Token;
                                    Task.Delay(2000, token).ContinueWith(_ => {
                                        if (!token.IsCancellationRequested)
                                        {
                                            popupWindow.Dispatcher.Invoke(() => {
                                                webView!.CoreWebView2.Navigate(popupUri.ToString());
                                                popupWindow.Close();
                                            });
                                        }
                                    }, token);
                                }
                            }
                            catch { }
                        }

                        popupWebView.CoreWebView2.NavigationCompleted += (_, e) => {
                            TryAutoClosePopup(popupWebView.CoreWebView2);
                        };
                        popupWebView.CoreWebView2.SourceChanged += (_, _) => {
                            TryAutoClosePopup(popupWebView.CoreWebView2);
                        };

                        lock (popupLock)
                        {
                            popupStack.Push(popupWebView);
                            popupWindowMap[popupWebView] = popupWindow;
                        }

                        popupWebView.CoreWebView2.WindowCloseRequested += (_, _) => {
                            popupWindow.Dispatcher.Invoke(() => popupWindow.Close());
                        };
                        popupWindow.Closing += (_, _) => {
                            RemovePopupFromStack(popupWebView);
                            popupWindowMap.Remove(popupWebView);
                        };

                        ev.NewWindow = popupWebView.CoreWebView2;
                        ev.Handled = true;
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                };
                webView.CoreWebView2.NavigationStarting += (_, ev) => {
                    isNavigating = true;
                    browserContent?.OnNavigationStateChanged();
                };
                webView.CoreWebView2.SourceChanged += (_, ev) => browserContent?.OnNavigationStateChanged();
                webView.CoreWebView2.NavigationCompleted += (_, ev) => {
                    isNavigating = false;
                    browserContent?.OnNavigationStateChanged();
                };
                browserContent?.OnBrowserReady();
            });

            isLoaded = true;
            await Task.Run(() => {
                foreach (Func<Task> formTask in formTasks.GetConsumingEnumerable())
                {
                    try
                    {
                        Task task = window!.Dispatcher.Invoke(formTask);
                        task.Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
