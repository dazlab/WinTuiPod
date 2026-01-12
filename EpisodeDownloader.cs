namespace WinTuiPod;

internal static class EpisodeDownloader
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public static async Task<string> EnsureDownloadedAsync(Episode ep, DataStore store, IProgress<double>? progress = null)
    {
        var path = store.GetCachePathForEpisode(ep);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        using var resp = await Http.GetAsync(ep.AudioUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);

        var buffer = new byte[1024 * 128];
        long readTotal = 0;

        while (true)
        {
            var n = await src.ReadAsync(buffer);
            if (n <= 0) break;

            await dst.WriteAsync(buffer.AsMemory(0, n));
            readTotal += n;

            if (total.HasValue && total.Value > 0 && progress is not null)
                progress.Report((double)readTotal / total.Value);
        }

        return path;
    }
}
