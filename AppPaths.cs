namespace WinTuiPod;

internal sealed class AppPaths
{
    public string Root { get; }
    public string CacheDir { get; }
    public string SubscriptionsPath { get; }
    public string StatePath { get; }

    private AppPaths(string root)
    {
        Root = root;
        CacheDir = Path.Combine(root, "cache");
        SubscriptionsPath = Path.Combine(root, "subscriptions.json");
        StatePath = Path.Combine(root, "state.json");
    }

    public static AppPaths Create()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinTuiPod");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "cache"));

        return new AppPaths(root);
    }
}
