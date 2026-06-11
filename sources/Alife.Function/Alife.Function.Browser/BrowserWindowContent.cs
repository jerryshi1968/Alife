using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Function.Browser;

public class BrowserWindowContent : Grid
{
    public WebView2 WebView { get; } = new();

    public BrowserWindowContent()
    {
        CreateLayout();
        UpdateToolbarState();
    }

    public void OnBrowserReady()
    {
        UpdateAddressBar();
        UpdateToolbarState();
    }

    public void OnNavigationStateChanged()
    {
        UpdateAddressBar();
        UpdateToolbarState();
    }

    readonly Button backButton = CreateToolbarButton("后退");
    readonly Button forwardButton = CreateToolbarButton("前进");
    readonly Button refreshButton = CreateToolbarButton("刷新");
    readonly Button goButton = CreateToolbarButton("前往");
    readonly TextBox addressBar = new() {
        Text = AddressPrompt,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        MinWidth = 240,
    };

    const string AddressPrompt = "输入网址";

    void CreateLayout()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid toolbar = CreateToolbar();
        SetRow(toolbar, 0);
        SetRow(WebView, 1);
        Children.Add(toolbar);
        Children.Add(WebView);

        backButton.Click += (_, _) => {
            if (WebView.CoreWebView2?.CanGoBack == true)
                WebView.CoreWebView2.GoBack();
        };
        forwardButton.Click += (_, _) => {
            if (WebView.CoreWebView2?.CanGoForward == true)
                WebView.CoreWebView2.GoForward();
        };
        refreshButton.Click += (_, _) => WebView.CoreWebView2?.Reload();
        goButton.Click += (_, _) => NavigateFromAddressBar();
        addressBar.KeyDown += (_, e) => {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            NavigateFromAddressBar();
        };
        addressBar.GotKeyboardFocus += (_, _) => {
            if (addressBar.Text == AddressPrompt)
                addressBar.Text = string.Empty;
        };
        addressBar.LostKeyboardFocus += (_, _) => {
            if (string.IsNullOrWhiteSpace(addressBar.Text) && WebView.Source == null)
                addressBar.Text = AddressPrompt;
        };
    }

    Grid CreateToolbar()
    {
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
        return toolbar;
    }

    static Button CreateToolbarButton(string text)
    {
        return new Button {
            Content = text,
            MinWidth = 56,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };
    }

    static void AddToolbarChild(Grid toolbar, UIElement element, int column)
    {
        SetColumn(element, column);
        toolbar.Children.Add(element);
    }

    void NavigateFromAddressBar()
    {
        if (WebView.CoreWebView2 == null)
            return;

        string? url = NormalizeUserUrl(addressBar.Text);
        if (url == null)
        {
            Console.WriteLine($"[Browser] 已拒绝用户输入的网址：{addressBar.Text}");
            UpdateAddressBar();
            return;
        }

        WebView.CoreWebView2.Navigate(url);
    }

    static string? NormalizeUserUrl(string text)
    {
        string url = text.Trim();
        if (string.IsNullOrWhiteSpace(url) || url == AddressPrompt)
            return null;

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

    void UpdateAddressBar()
    {
        addressBar.Text = WebView.Source?.ToString() ?? AddressPrompt;
    }

    void UpdateToolbarState()
    {
        Microsoft.Web.WebView2.Core.CoreWebView2? coreWebView = WebView.CoreWebView2;
        if (coreWebView == null)
        {
            backButton.IsEnabled = false;
            forwardButton.IsEnabled = false;
            refreshButton.IsEnabled = false;
            goButton.IsEnabled = false;
            addressBar.IsEnabled = false;
            return;
        }

        backButton.IsEnabled = coreWebView.CanGoBack;
        forwardButton.IsEnabled = coreWebView.CanGoForward;
        refreshButton.IsEnabled = true;
        goButton.IsEnabled = true;
        addressBar.IsEnabled = true;
    }
}
