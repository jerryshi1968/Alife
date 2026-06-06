using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public Task<T> AddFormTask<T>(Func<WebView2, Task<T>> action, bool showWindow = false)
    {
        if (window == null)
            throw new ObjectDisposedException(nameof(WebViewWorker));

        TaskCompletionSource<T> tcs = new();

        formTasks.Add(async () => {
            try
            {
                if (webView == null)
                    throw new ArgumentNullException(nameof(webView));
                if (showWindow)
                    ShowBrowserWindow();
                T result = await action(webView);
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
    WebView2? webView;
    Button? backButton;
    Button? forwardButton;
    Button? refreshButton;
    Button? goButton;
    TextBox? addressBar;
    readonly BlockingCollection<Func<Task>> formTasks = new();
    bool isNavigating;
    bool isLoaded;
    bool isDisposing;

    public WebViewWorker()
    {
        var thread = new Thread(() => {
            try
            {
                window = new UnclosableWindow {
                    Title = "Alife.Client Browser",
                    Width = 1024,
                    Height = 768,
                    WindowState = WindowState.Minimized,
                    ShowInTaskbar = true,
                    ResizeMode = ResizeMode.CanResize,
                };
                webView = new WebView2();
                window.Content = CreateBrowserContent(webView);
                window.Loaded += OnWindowLoaded;
                window.Closing += OnWindowClosing;

                window.Show();
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

    public void Dispose()
    {
        isDisposing = true;
        formTasks.CompleteAdding();
        if (window != null)
        {
            window.Dispatcher.Invoke(() => {
                window.Close();
                window.Dispatcher.InvokeShutdown();
            });
        }
    }

    Grid CreateBrowserContent(WebView2 webViewControl)
    {
        backButton = CreateToolbarButton("Back");
        forwardButton = CreateToolbarButton("Forward");
        refreshButton = CreateToolbarButton("Refresh");
        goButton = CreateToolbarButton("Go");
        addressBar = new TextBox {
            Margin = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 240,
        };

        backButton.Click += (_, _) => {
            if (webView?.CoreWebView2?.CanGoBack == true)
                webView.CoreWebView2.GoBack();
        };
        forwardButton.Click += (_, _) => {
            if (webView?.CoreWebView2?.CanGoForward == true)
                webView.CoreWebView2.GoForward();
        };
        refreshButton.Click += (_, _) => webView?.CoreWebView2?.Reload();
        goButton.Click += (_, _) => NavigateFromAddressBar();
        addressBar.KeyDown += (_, e) => {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                NavigateFromAddressBar();
            }
        };

        Grid toolbar = new() { Margin = new Thickness(6) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddToolbarChild(toolbar, backButton, 0);
        AddToolbarChild(toolbar, forwardButton, 1);
        AddToolbarChild(toolbar, refreshButton, 2);
        AddToolbarChild(toolbar, addressBar, 3);
        AddToolbarChild(toolbar, goButton, 4);

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(webViewControl, 1);
        root.Children.Add(toolbar);
        root.Children.Add(webViewControl);

        UpdateToolbarState();
        return root;
    }

    static Button CreateToolbarButton(string text)
    {
        return new Button {
            Content = text,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };
    }

    static void AddToolbarChild(Grid toolbar, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        toolbar.Children.Add(element);
    }

    async void OnWindowLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            string userDataFolder = Path.Combine(AlifePath.RuntimeFolderPath, "WebView2Data");
            if (!Directory.Exists(userDataFolder))
                Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await webView!.Dispatcher.InvokeAsync(async () => {
                await webView!.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
                webView.CoreWebView2.NewWindowRequested += (_, ev) => {
                    ev.Handled = true;
                    webView.CoreWebView2.Navigate(ev.Uri);
                };
                webView.CoreWebView2.NavigationStarting += (_, _) => {
                    isNavigating = true;
                    UpdateToolbarState();
                };
                webView.CoreWebView2.SourceChanged += (_, _) => UpdateAddressBar();
                webView.CoreWebView2.NavigationCompleted += (_, _) => {
                    isNavigating = false;
                    UpdateAddressBar();
                    UpdateToolbarState();
                };
                UpdateAddressBar();
                UpdateToolbarState();
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

    void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (isDisposing)
            return;

        e.Cancel = true;
        UnloadCurrentPage();
        window?.Hide();
    }

    void NavigateFromAddressBar()
    {
        if (webView?.CoreWebView2 == null || addressBar == null)
            return;

        string? url = NormalizeUserUrl(addressBar.Text);
        if (url == null)
        {
            Console.WriteLine($"[Browser] Rejected user URL: {addressBar.Text}");
            UpdateAddressBar();
            return;
        }

        webView.CoreWebView2.Navigate(url);
    }

    static string? NormalizeUserUrl(string text)
    {
        string url = text.Trim();
        if (url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            return "about:blank";

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;
        }

        return uri.Scheme switch {
            "http" or "https" => uri.AbsoluteUri,
            _ => null
        };
    }

    void ShowBrowserWindow()
    {
        if (window == null)
            return;

        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    void UnloadCurrentPage()
    {
        try
        {
            webView?.CoreWebView2?.Stop();
            webView?.CoreWebView2?.Navigate("about:blank");
            if (addressBar != null)
                addressBar.Text = "about:blank";
            UpdateToolbarState();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    void UpdateAddressBar()
    {
        if (addressBar == null)
            return;

        addressBar.Text = webView?.Source?.ToString() ?? "about:blank";
    }

    void UpdateToolbarState()
    {
        bool ready = webView?.CoreWebView2 != null;
        if (backButton != null)
            backButton.IsEnabled = ready && webView!.CoreWebView2.CanGoBack;
        if (forwardButton != null)
            forwardButton.IsEnabled = ready && webView!.CoreWebView2.CanGoForward;
        if (refreshButton != null)
            refreshButton.IsEnabled = ready;
        if (goButton != null)
            goButton.IsEnabled = ready;
        if (addressBar != null)
            addressBar.IsEnabled = ready;
    }
}
