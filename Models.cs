namespace WinTuiPod;

internal sealed record Subscription(string Title, string FeedUrl);

internal sealed record Episode(
    string FeedTitle,
    string Title,
    DateTimeOffset? Published,
    string AudioUrl,
    string? Id // guid or enclosure url hash
);

internal sealed class AppState
{
    public HashSet<string> PlayedEpisodeIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
