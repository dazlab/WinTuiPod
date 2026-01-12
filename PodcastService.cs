using CodeHollow.FeedReader;

namespace WinTuiPod;

internal static class PodcastService
{
    public static async Task<(string Title, List<Episode> Episodes)> FetchEpisodesAsync(string feedUrl)
    {
        var feed = await FeedReader.ReadAsync(feedUrl);

        var feedTitle = string.IsNullOrWhiteSpace(feed.Title) ? feedUrl : feed.Title.Trim();

        // Podcast episodes typically: enclosure link in item.SpecificItem.Element
        var episodes = new List<Episode>();

        foreach (var item in feed.Items)
        {
            var audioUrl = TryGetEnclosureUrl(item);
            if (string.IsNullOrWhiteSpace(audioUrl))
                continue;

            var title = string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title.Trim();
            var published = item.PublishingDate;

            var id = !string.IsNullOrWhiteSpace(item.Id)
                ? item.Id
                : audioUrl; // fallback stable-ish

            episodes.Add(new Episode(feedTitle, title, published, audioUrl, id));
        }

        // newest first if we have dates
        episodes.Sort((a, b) => Nullable.Compare(b.Published, a.Published));

        return (feedTitle, episodes);
    }

    private static string? TryGetEnclosureUrl(FeedItem item)
    {
        // Best-effort: find <enclosure url="...">
        // CodeHollow doesn't expose enclosures strongly, so use element inspection.
        try
        {
            var x = item.SpecificItem?.Element;
            if (x is null) return null;

            var enclosure = x.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase));
            if (enclosure is null) return null;

            var urlAttr = enclosure.Attribute("url")?.Value;
            if (!string.IsNullOrWhiteSpace(urlAttr)) return urlAttr;

            return null;
        }
        catch
        {
            return null;
        }
    }
}
