// TuiApp.cs
using Spectre.Console;
using Spectre.Console.Rendering;

namespace WinTuiPod;

internal sealed class TuiApp
{
    private readonly DataStore _store;
    private readonly AudioPlayer _player;

    // Now Playing state
    private Episode? _nowPlayingEpisode;
    private string? _nowPlayingPath;

    private volatile bool _isDownloading;
    private volatile int _downloadPercent; // 0..100

    private readonly SemaphoreSlim _playLock = new(1, 1);
    
    // UI theme
    private static class Ui
    {
        private static int _themeIndex = 0;
        private static readonly (Color Accent, string TitleColor)[] Themes =
        {
            (Color.CornflowerBlue, "#87AFFF"), // Blue
            (Color.Green, "#5FFF87"),          // Green
            (Color.Gold1, "#FFD75F"),          // Amber
            (Color.Grey, "#AAAAAA"),           // Mono
        };

        public static Color Accent => Themes[_themeIndex].Accent;
        public static Color Border => Accent;
        public static Color SubtleBorder => Color.Grey37;

        public static string Title =>
            $"[bold {Themes[_themeIndex].TitleColor}]WinTuiPod[/]  [grey](MVP)[/]";

        public const string Hint =
            "Up/Down: move  Enter: select  Esc: back  P: play/pause  S: stop  ←/→: seek  T: theme";

        public static string SelOpen =>
            $"[black on {Themes[_themeIndex].TitleColor}]";
        public const string SelClose = "[/]";

        public static void NextTheme()
        {
            _themeIndex = (_themeIndex + 1) % Themes.Length;
        }
    }

    public TuiApp(DataStore store, AudioPlayer player)
    {
        _store = store;
        _player = player;
    }

    public async Task RunAsync()
    {
        var subs = await _store.LoadSubscriptionsAsync();
        var state = await _store.LoadStateAsync();

        while (true)
        {
            var actions = new List<string>
            {
                "Open subscription",
                "Add subscription",
                "Remove subscription",
                "Quit"
            };

            var choice = LiveSelect(
                title: "Select an action",
                help: "Up/Down: move   Enter: select   Esc: quit   P: play/pause   S: stop   ←/→: seek",
                items: actions,
                line: s => Markup.Escape(s),
                pageSize: 10,
                footer: BuildNowPlayingFooter,
                keyHandler: (key, _) => HandleGlobalPlaybackKeys(key));

            if (choice is null || choice == "Quit")
                break;

            if (choice == "Add subscription")
            {
                await AddSubscriptionAsync(subs);
                await _store.SaveSubscriptionsAsync(subs);
            }
            else if (choice == "Remove subscription")
            {
                await RemoveSubscriptionAsync(subs);
                await _store.SaveSubscriptionsAsync(subs);
            }
            else if (choice == "Open subscription")
            {
                if (subs.Count == 0)
                {
                    AnsiConsole.Clear();
                    RenderHeader();
                    AnsiConsole.MarkupLine("[yellow]No subscriptions yet.[/]");
                    WaitKey();
                    continue;
                }

                var sub = LiveSelect(
                    title: "Select a feed",
                    help: "Up/Down: move   Enter: select   Esc: back   P: play/pause   S: stop   ←/→: seek",
                    items: subs,
                    line: s => Markup.Escape(string.IsNullOrWhiteSpace(s.Title) ? s.FeedUrl : s.Title),
                    pageSize: 15,
                    footer: BuildNowPlayingFooter,
                    keyHandler: (key, _) => HandleGlobalPlaybackKeys(key));

                if (sub is null)
                    continue;

                await BrowseFeedAsync(sub, state);
                await _store.SaveStateAsync(state);
            }
        }

        StopPlayback();
    }

    private static void RenderHeader()
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn(new GridColumn().RightAligned());

        grid.AddRow(
            new Markup("[bold]WinTuiPod[/]  (MVP)"),
            new Markup("[grey]Up/Down: move  Enter: select  Esc: back/quit  P: play/pause  S: stop  ←/→: seek[/]"));

        AnsiConsole.Write(new Panel(grid).RoundedBorder());
        AnsiConsole.WriteLine();
    }

    private static IRenderable BuildHeaderRenderable(string rightText)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn(new GridColumn().RightAligned());

        grid.AddRow(
            new Markup(Ui.Title),
            new Markup($"[grey]{Markup.Escape(rightText)}[/]"));

        return new Panel(grid)
            .RoundedBorder()
            .BorderColor(Ui.SubtleBorder);
    }


    private IRenderable BuildNowPlayingFooter()
    {
        var left = "[grey]Idle[/]";
        if (_nowPlayingEpisode is not null)
            left = $"[bold]{Markup.Escape(_nowPlayingEpisode.Title)}[/]";

        string right;
        if (_isDownloading)
        {
            right = $"[yellow]Downloading {_downloadPercent}%[/]";
        }
        else if (_player.IsPlaying)
        {
            right = $"[green]Playing[/] {_player.Position:mm\\:ss}/{_player.Duration:mm\\:ss}";
        }
        else if (_player.Duration > TimeSpan.Zero)
        {
            right = $"[yellow]Paused[/] {_player.Position:mm\\:ss}/{_player.Duration:mm\\:ss}";
        }
        else
        {
            right = "[grey]Stopped[/]";
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn(new GridColumn().RightAligned());
        grid.AddRow(new Markup(left), new Markup(right));

        return new Panel(grid)
            .RoundedBorder()
            .BorderColor(Ui.SubtleBorder)
            .Header("Now Playing", Justify.Left);
    }

    private bool HandleGlobalPlaybackKeys(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.T)
        {
            Ui.NextTheme();
            return true; // force redraw
        }

        if (key.Key == ConsoleKey.P)
        {
            _player.TogglePause();
            return true;
        }

        if (key.Key == ConsoleKey.S)
        {
            StopPlayback();
            return true;
        }

        if (key.Key == ConsoleKey.LeftArrow)
        {
            _player.SeekBy(TimeSpan.FromSeconds(-15));
            return true;
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            _player.SeekBy(TimeSpan.FromSeconds(+15));
            return true;
        }

        return false;
    }

    private void StopPlayback()
    {
        _player.Stop();
        _nowPlayingEpisode = null;
        _nowPlayingPath = null;
        _isDownloading = false;
        _downloadPercent = 0;
    }

    private async Task StartPlayAsync(Episode ep)
    {
        await _playLock.WaitAsync();
        try
        {
            _player.Stop();

            _nowPlayingEpisode = ep;
            _nowPlayingPath = null;

            _isDownloading = true;
            _downloadPercent = 0;

            var prog = new Progress<double>(p =>
            {
                if (p < 0) p = 0;
                if (p > 1) p = 1;
                _downloadPercent = (int)Math.Round(p * 100);
            });

            var path = await EpisodeDownloader.EnsureDownloadedAsync(ep, _store, prog);

            _isDownloading = false;
            _downloadPercent = 0;

            _nowPlayingPath = path;
            _player.PlayFile(path);
        }
        catch
        {
            StopPlayback();
        }
        finally
        {
            _playLock.Release();
        }
    }

    private async Task AddSubscriptionAsync(List<Subscription> subs)
    {
        var url = PromptTextOrEsc(
            title: "Add subscription",
            help: "Paste the RSS/Atom feed URL. Enter: confirm   Esc: back");

        if (url is null)
            return;

        url = url.Trim();
        if (url.Length == 0)
            return;

        if (subs.Any(s => s.FeedUrl.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine("[yellow]Already subscribed.[/]");
            WaitKey();
            return;
        }

        try
        {
            var (title, _) = await PodcastService.FetchEpisodesAsync(url);
            subs.Add(new Subscription(title, url));
            subs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine("[green]Subscribed:[/] " + Markup.Escape(title));
        }
        catch (Exception ex)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine("[red]Failed to subscribe:[/] " + Markup.Escape(ex.Message));
        }

        WaitKey();
    }

    private async Task RemoveSubscriptionAsync(List<Subscription> subs)
    {
        if (subs.Count == 0)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine("[yellow]No subscriptions to remove.[/]");
            WaitKey();
            return;
        }

        var sub = LiveSelect(
            title: "Remove which feed?",
            help: "Up/Down: move   Enter: remove   Esc: back   P: play/pause   S: stop   ←/→: seek",
            items: subs,
            line: s => Markup.Escape(s.Title),
            pageSize: 15,
            footer: BuildNowPlayingFooter,
            keyHandler: (key, _) => HandleGlobalPlaybackKeys(key));

        if (sub is null)
            return;

        subs.Remove(sub);

        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[green]Removed.[/]");
        WaitKey();

        await Task.CompletedTask;
    }

    private async Task BrowseFeedAsync(Subscription sub, AppState state)
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(sub.Title)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(sub.FeedUrl)}[/]");
            AnsiConsole.WriteLine();

            List<Episode> episodes;
            try
            {
                var (_, eps) = await PodcastService.FetchEpisodesAsync(sub.FeedUrl);
                episodes = eps;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Feed refresh failed:[/] " + Markup.Escape(ex.Message));
                WaitKey();
                return;
            }

            if (episodes.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No playable episodes found (no enclosures).[/]");
                WaitKey();
                return;
            }

            var selected = LiveSelect(
                title: "Episodes",
                help: "Up/Down: move   Enter: actions   Space: play   P: play/pause   S: stop   ←/→: seek   M: mark played   Esc: back",
                items: episodes,
                line: ep =>
                {
                    var date = ep.Published?.ToString("yyyy-MM-dd") ?? "---- -- --";
                    var played = ep.Id is not null && state.PlayedEpisodeIds.Contains(ep.Id);
                    var t = Markup.Escape(ep.Title);
                    if (played) t = "[grey](played)[/] " + t;
                    return $"[bold]{Markup.Escape(date)}[/]  {t}";
                },
                pageSize: 15,
                footer: BuildNowPlayingFooter,
                keyHandler: (key, currentEp) =>
                {
                    if (HandleGlobalPlaybackKeys(key))
                        return true;

                    if (key.Key == ConsoleKey.Spacebar)
                    {
                        _ = StartPlayAsync(currentEp);
                        return true;
                    }

                    if (key.Key == ConsoleKey.M)
                    {
                        if (!string.IsNullOrWhiteSpace(currentEp.Id))
                            state.PlayedEpisodeIds.Add(currentEp.Id);
                        return true;
                    }

                    return false;
                });

            if (selected is null)
                return;

            await EpisodeMenuAsync(selected, state);
        }
    }

    private async Task EpisodeMenuAsync(Episode ep, AppState state)
    {
        while (true)
        {
            var actions = new List<string>
            {
                "Play (download if needed)",
                "Play/Pause (P)",
                "Stop (S)",
                "Seek -15s (Left)",
                "Seek +15s (Right)",
                "Mark played",
                "Back"
            };

            var cmd = LiveSelect(
                title: ep.Title,
                help: "Enter: run action   Esc: back   P: play/pause   S: stop   ←/→: seek",
                items: actions,
                line: s => Markup.Escape(s),
                pageSize: 10,
                footer: BuildNowPlayingFooter,
                keyHandler: (key, _) => HandleGlobalPlaybackKeys(key));

            if (cmd is null || cmd == "Back")
                return;

            if (cmd.StartsWith("Play (download", StringComparison.Ordinal))
            {
                _ = StartPlayAsync(ep);
            }
            else if (cmd.StartsWith("Play/Pause", StringComparison.Ordinal))
            {
                _player.TogglePause();
            }
            else if (cmd.StartsWith("Stop", StringComparison.Ordinal))
            {
                StopPlayback();
            }
            else if (cmd.StartsWith("Seek -15", StringComparison.Ordinal))
            {
                _player.SeekBy(TimeSpan.FromSeconds(-15));
            }
            else if (cmd.StartsWith("Seek +15", StringComparison.Ordinal))
            {
                _player.SeekBy(TimeSpan.FromSeconds(+15));
            }
            else if (cmd.StartsWith("Mark played", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(ep.Id))
                    state.PlayedEpisodeIds.Add(ep.Id);

                AnsiConsole.Clear();
                RenderHeader();
                AnsiConsole.MarkupLine("[green]Marked played.[/]");
                WaitKey();
            }

            await Task.CompletedTask;
        }
    }

    private T? LiveSelect<T>(
        string title,
        string help,
        IReadOnlyList<T> items,
        Func<T, string> line,
        int pageSize = 15,
        Func<IRenderable>? footer = null,
        Func<ConsoleKeyInfo, T, bool>? keyHandler = null)
    {
        if (items.Count == 0)
            return default;

        var index = 0;
        var top = 0;
        var animFrame = 0;
        T? selected = default;

        AnsiConsole.Clear();

        AnsiConsole.Live(BuildLayout())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx =>
            {
                while (true)
                {
                    ClampAndPage(items.Count, pageSize, ref index, ref top);

                    ctx.UpdateTarget(BuildLayout());
                    ctx.Refresh();
                    animFrame++;

                    if (!Console.KeyAvailable)
                    {
                        Thread.Sleep(120);
                        continue;
                    }

                    var key = Console.ReadKey(true);
                    var current = items[index];

                    if (keyHandler is not null && keyHandler(key, current))
                        continue;

                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow: index--; break;
                        case ConsoleKey.DownArrow: index++; break;
                        case ConsoleKey.PageUp: index -= pageSize; break;
                        case ConsoleKey.PageDown: index += pageSize; break;

                        case ConsoleKey.Enter:
                            selected = items[index];
                            return;

                        case ConsoleKey.Escape:
                            selected = default;
                            return;
                    }
                }
            });

        return selected;

        IRenderable BuildLayout()
        {
            var header = BuildHeaderRenderable("Up/Down: move  Enter: select  Esc: back");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Ui.Border)
                .AddColumn(new TableColumn("Item"));


            var end = Math.Min(top + pageSize, items.Count);
            for (var i = top; i < end; i++)
            {
                var text = line(items[i]);
                if (i == index)
                {
                    var glyph = (animFrame % 2 == 0) ? "▸" : "▹";
                    table.AddRow($"{Ui.SelOpen} {glyph} {text}{Ui.SelClose}");
                }
                else
                {
                    table.AddRow($"   {text}");
                }
            }

            var body = new Grid();
            body.AddColumn();
            body.AddRow(header);
            body.AddEmptyRow();
            body.AddRow(new Markup($"[bold]{Markup.Escape(title)}[/]"));
            body.AddRow(new Markup($"[grey italic]{Markup.Escape(help)}[/]"));
            body.AddEmptyRow();
            body.AddRow(table);

            if (footer is not null)
            {
                body.AddEmptyRow();
                body.AddRow(footer());
            }

            return body;
        }

        static void ClampAndPage(int count, int pageSize, ref int index, ref int top)
        {
            if (index < 0) index = 0;
            if (index >= count) index = count - 1;
            if (index < top) top = index;
            if (index >= top + pageSize) top = index - pageSize + 1;
            if (top < 0) top = 0;
        }
    }

    private string? PromptTextOrEsc(string title, string help)
    {
        var input = new System.Text.StringBuilder();
        string? result = null;

        AnsiConsole.Clear();

        AnsiConsole.Live(BuildLayout())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx =>
            {
                while (true)
                {
                    ctx.UpdateTarget(BuildLayout());
                    ctx.Refresh();

                    if (!Console.KeyAvailable)
                    {
                        Thread.Sleep(120);
                        continue;
                    }

                    var key = Console.ReadKey(true);

                    if (HandleGlobalPlaybackKeys(key))
                        continue;

                    if (key.Key == ConsoleKey.Escape)
                    {
                        result = null;
                        return;
                    }

                    if (key.Key == ConsoleKey.Enter)
                    {
                        result = input.ToString();
                        return;
                    }

                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (input.Length > 0)
                            input.Length--;
                        continue;
                    }

                    if (char.IsControl(key.KeyChar))
                        continue;

                    input.Append(key.KeyChar);
                }
            });

        return result;

        IRenderable BuildLayout()
        {
            var header = BuildHeaderRenderable("Type URL  Enter: confirm  Esc: back  P: play/pause  S: stop  ←/→: seek");

            var shown = Markup.Escape(input.ToString());
            var panel = new Panel(shown.Length == 0 ? "[grey](empty)[/]" : shown)
                .RoundedBorder()
                .Header("Input", Justify.Left);

            var body = new Grid();
            body.AddColumn();
            body.AddRow(header);
            body.AddEmptyRow();
            body.AddRow(new Markup($"[bold]{Markup.Escape(title)}[/]"));
            body.AddRow(new Markup($"[grey]{Markup.Escape(help)}[/]"));
            body.AddEmptyRow();
            body.AddRow(panel);
            body.AddEmptyRow();
            body.AddRow(BuildNowPlayingFooter());

            return body;
        }
    }

    private static void WaitKey()
    {
        AnsiConsole.MarkupLine("[grey]Press any key...[/]");
        Console.ReadKey(true);
    }
}
