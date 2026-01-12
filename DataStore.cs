using System.Text.Json;

namespace WinTuiPod;

internal sealed class DataStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public DataStore(AppPaths paths) => _paths = paths;

    public async Task<List<Subscription>> LoadSubscriptionsAsync()
    {
        if (!File.Exists(_paths.SubscriptionsPath))
            return [];

        var json = await File.ReadAllTextAsync(_paths.SubscriptionsPath);
        return JsonSerializer.Deserialize<List<Subscription>>(json) ?? [];
    }

    public async Task SaveSubscriptionsAsync(List<Subscription> subs)
    {
        var json = JsonSerializer.Serialize(subs, _json);
        await File.WriteAllTextAsync(_paths.SubscriptionsPath, json);
    }

    public async Task<AppState> LoadStateAsync()
    {
        if (!File.Exists(_paths.StatePath))
            return new AppState();

        var json = await File.ReadAllTextAsync(_paths.StatePath);
        return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
    }

    public async Task SaveStateAsync(AppState state)
    {
        var json = JsonSerializer.Serialize(state, _json);
        await File.WriteAllTextAsync(_paths.StatePath, json);
    }

    public string GetCachePathForEpisode(Episode ep)
    {
        // stable filename based on audio url hash
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(ep.AudioUrl)));

        // keep extension if possible
        var ext = ".mp3";
        try
        {
            var uri = new Uri(ep.AudioUrl);
            var e = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(e) && e.Length <= 6) ext = e;
        }
        catch { /* ignore */ }

        var safeFeed = MakeSafeFileName(ep.FeedTitle);
        var dir = Path.Combine(_paths.CacheDir, safeFeed);
        Directory.CreateDirectory(dir);

        return Path.Combine(dir, $"{hash}{ext}");
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 50 ? name[..50] : name;
    }
}
