namespace Alife.Components.Services;

public static class NavigationHub
{
    static event Action<string>? TabRequested;
    static string? pendingTab;

    public static void RequestTab(string tab)
    {
        if (TabRequested == null)
        {
            pendingTab = tab;
            return;
        }
        TabRequested.Invoke(tab);
    }

    public static void Subscribe(Action<string> handler)
    {
        TabRequested += handler;
        if (pendingTab != null)
        {
            handler.Invoke(pendingTab);
            pendingTab = null;
        }
    }

    public static void Unsubscribe(Action<string> handler) => TabRequested -= handler;
}
