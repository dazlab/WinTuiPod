// TuiApp.cs
using Spectre.Console;

namespace WinTuiPod;

internal sealed class TuiApp
{
    private readonly DataStore _store;
    private readonly AudioPlayer _player;

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

            var choice = SelectFromList(
                title: "Select an action",
                help: "Up/Down: move   Enter: select   Esc: quit",
                items: actions,
                render: s => Markup.Escape(s),
                pageSize: 10);

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

                var sub = SelectFromList(
                    title: "Select a feed",
                    help: "Up/Down: move   Enter: select   Esc: back",
                    items: subs,
                    render: s => Markup.Escape(string.IsNullOrWhiteSpace(s.Title) ? s.FeedUrl : s.Title),
                    pageSize: 15);

                if (sub is null)
                    continue; // Esc -> back to main menu

                await BrowseFeedAsync(sub, state);
                await _store.SaveStateAsync(state);
            }
        }

        _player.Stop();
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

    private async Task AddSubscriptionAsync(List<Subscription> subs)
    {
        var url = PromptTextOrEsc(
            title: "Add subscription",
            help: "Paste the RSS/Atom feed URL. Enter: confirm   Esc: back");

        if (url is null)
            return; // Esc -> back

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

        var sub = SelectFromList(
            title: "Remove which feed?",
            help: "Up/Down: move   Enter: remove   Esc: back",
            items: subs,
            render: s => Markup.Escape(s.Title),
            pageSize: 15);

        if (sub is null)
            return; // Esc -> back

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

            var selected = SelectEpisode(episodes, state);
            if (selected is null)
                return; // Esc -> back to subscriptions

            await EpisodeMenuAsync(selected, state);
        }
    }

    private async Task EpisodeMenuAsync(Episode ep, AppState state)
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();

            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(ep.Title)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ep.AudioUrl)}[/]");
            AnsiConsole.WriteLine();

            var status = _player.IsPlaying ? "Playing" : "Stopped/Paused";
            var pos = $"{_player.Position:mm\\:ss} / {_player.Duration:mm\\:ss}";
            AnsiConsole.Write(new Panel($"[bold]{status}[/]  {pos}").RoundedBorder());

            AnsiConsole.WriteLine();

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

            var cmd = SelectFromList(
                title: "Episode actions",
                help: "Up/Down: move   Enter: select   Esc: back",
                items: actions,
                render: s => Markup.Escape(s),
                pageSize: 10);

            if (cmd is null || cmd == "Back")
                return;

            if (cmd.StartsWith("Play (download", StringComparison.Ordinal))
            {
                string filePath = "";

                await AnsiConsole.Progress()
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("Downloading", autoStart: true);
                        var prog = new Progress<double>(p => task.Value = p * 100);

                        filePath = await EpisodeDownloader.EnsureDownloadedAsync(ep, _store, prog);
                        task.Value = 100;
                    });

                _player.PlayFile(filePath);
            }
            else if (cmd.StartsWith("Play/Pause", StringComparison.Ordinal))
            {
                _player.TogglePause();
            }
            else if (cmd.StartsWith("Stop", StringComparison.Ordinal))
            {
                _player.Stop();
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

                AnsiConsole.MarkupLine("[green]Marked played.[/]");
                WaitKey();
            }
        }
    }

    private static Episode? SelectEpisode(IReadOnlyList<Episode> episodes, AppState state)
    {
        var index = 0;
        var top = 0;
        const int pageSize = 15;

        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine("[bold]Episodes[/]");
            AnsiConsole.MarkupLine("[grey]Up/Down: move   Enter: select   Esc: back[/]");
            AnsiConsole.WriteLine();

            if (index < 0) index = 0;
            if (index >= episodes.Count) index = episodes.Count - 1;
            if (index < top) top = index;
            if (index >= top + pageSize) top = index - pageSize + 1;
            if (top < 0) top = 0;

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Date").NoWrap())
                .AddColumn(new TableColumn("Title"));

            var end = Math.Min(top + pageSize, episodes.Count);
            for (var i = top; i < end; i++)
            {
                var ep = episodes[i];
                var date = ep.Published?.ToString("yyyy-MM-dd") ?? "---- -- --";

                var played = ep.Id is not null && state.PlayedEpisodeIds.Contains(ep.Id);
                var title = Markup.Escape(ep.Title);
                if (played) title = "[grey](played)[/] " + title;

                if (i == index)
                    table.AddRow($"[bold]{date}[/]", $"[reverse]{title}[/]");
                else
                    table.AddRow(date, title);
            }

            AnsiConsole.Write(table);

            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index--;
                    break;

                case ConsoleKey.DownArrow:
                    index++;
                    break;

                case ConsoleKey.PageUp:
                    index -= pageSize;
                    break;

                case ConsoleKey.PageDown:
                    index += pageSize;
                    break;

                case ConsoleKey.Enter:
                    return episodes[index];

                case ConsoleKey.Escape:
                    return null;
            }
        }
    }

    private static T? SelectFromList<T>(
        string title,
        string help,
        IReadOnlyList<T> items,
        Func<T, string> render,
        int pageSize = 15)
    {
        if (items.Count == 0) return default;

        var index = 0;
        var top = 0;

        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
            if (!string.IsNullOrWhiteSpace(help))
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(help)}[/]");
            AnsiConsole.WriteLine();

            if (index < 0) index = 0;
            if (index >= items.Count) index = items.Count - 1;
            if (index < top) top = index;
            if (index >= top + pageSize) top = index - pageSize + 1;
            if (top < 0) top = 0;

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Item"));

            var end = Math.Min(top + pageSize, items.Count);
            for (var i = top; i < end; i++)
            {
                var text = render(items[i]);
                if (i == index)
                    table.AddRow($"[reverse]{text}[/]");
                else
                    table.AddRow(text);
            }

            AnsiConsole.Write(table);

            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index--;
                    break;

                case ConsoleKey.DownArrow:
                    index++;
                    break;

                case ConsoleKey.PageUp:
                    index -= pageSize;
                    break;

                case ConsoleKey.PageDown:
                    index += pageSize;
                    break;

                case ConsoleKey.Enter:
                    return items[index];

                case ConsoleKey.Escape:
                    return default;
            }
        }
    }

    private static void WaitKey()
    {
        AnsiConsole.MarkupLine("[grey]Press any key...[/]");
        Console.ReadKey(true);
    }
    
    private static bool? PromptYesNoOrEsc(string title, string question, bool defaultYes = true)
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(Markup.Escape(question));
            AnsiConsole.MarkupLine("[grey]Y/N: choose   Enter: accept default   Esc: back[/]");
            AnsiConsole.MarkupLine($"[grey]Default: {(defaultYes ? "Yes" : "No")}[/]");

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape) return null;
            if (key.Key == ConsoleKey.Enter) return defaultYes;

            if (key.Key == ConsoleKey.Y) return true;
            if (key.Key == ConsoleKey.N) return false;
        }
    }

    
    private static string? PromptTextOrEsc(string title, string help)
    {
        var input = new System.Text.StringBuilder();

        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
            if (!string.IsNullOrWhiteSpace(help))
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(help)}[/]");
            AnsiConsole.WriteLine();

            var shown = Markup.Escape(input.ToString());
            AnsiConsole.Write(new Panel(shown.Length == 0 ? "[grey](empty)[/]" : shown)
                .RoundedBorder()
                .Header("Input", Justify.Left));

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape)
                return null;

            if (key.Key == ConsoleKey.Enter)
                return input.ToString();

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
    }
   
}
