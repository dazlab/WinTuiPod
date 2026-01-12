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
            AnsiConsole.Clear();
            RenderHeader();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select an action")
                    .AddChoices([
                        "Open subscription",
                        "Add subscription (A)",
                        "Remove subscription (D)",
                        "Quit (Q)"
                    ]));

            if (choice.StartsWith("Quit")) break;

            if (choice.StartsWith("Add"))
            {
                await AddSubscriptionAsync(subs);
                await _store.SaveSubscriptionsAsync(subs);
            }
            else if (choice.StartsWith("Remove"))
            {
                await RemoveSubscriptionAsync(subs);
                await _store.SaveSubscriptionsAsync(subs);
            }
            else if (choice.StartsWith("Open"))
            {
                if (subs.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No subscriptions yet.[/]");
                    WaitKey();
                    continue;
                }

                var sub = AnsiConsole.Prompt(
                    new SelectionPrompt<Subscription>()
                        .Title("Select a feed")
                        .UseConverter(s => string.IsNullOrWhiteSpace(s.Title) ? s.FeedUrl : s.Title)
                        .AddChoices(subs));

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
            new Markup("[grey]Enter: select  A: add  D: delete  Q: quit  P: play/pause  S: stop  ←/→: seek[/]"));

        AnsiConsole.Write(new Panel(grid).RoundedBorder());
        AnsiConsole.WriteLine();
    }

    private async Task AddSubscriptionAsync(List<Subscription> subs)
    {
        var url = AnsiConsole.Ask<string>("Feed URL:");
        url = url.Trim();

        if (subs.Any(s => s.FeedUrl.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            AnsiConsole.MarkupLine("[yellow]Already subscribed.[/]");
            WaitKey();
            return;
        }

        try
        {
            var (title, _) = await PodcastService.FetchEpisodesAsync(url);
            subs.Add(new Subscription(title, url));
            subs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            AnsiConsole.MarkupLine("[green]Subscribed:[/] " + Markup.Escape(title));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Failed to subscribe:[/] " + Markup.Escape(ex.Message));
        }

        WaitKey();
    }

    private async Task RemoveSubscriptionAsync(List<Subscription> subs)
    {
        if (subs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No subscriptions to remove.[/]");
            WaitKey();
            return;
        }

        var sub = AnsiConsole.Prompt(
            new SelectionPrompt<Subscription>()
                .Title("Remove which feed?")
                .UseConverter(s => s.Title)
                .AddChoices(subs));

        subs.Remove(sub);
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

            var prompt = new SelectionPrompt<Episode>()
                .Title("Select an episode (Esc to go back)")
                .PageSize(15)
                .UseConverter(e =>
                {
                    var played = e.Id is not null && state.PlayedEpisodeIds.Contains(e.Id) ? "[grey](played)[/] " : "";
                    var date = e.Published?.ToString("yyyy-MM-dd") ?? "---- -- --";
                    return $"{played}{date}  {e.Title}";
                })
                .AddChoices(episodes);

            Episode ep;
            try
            {
                ep = AnsiConsole.Prompt(prompt);
            }
            catch (OperationCanceledException)
            {
                return; //fallback
            }

            await EpisodeMenuAsync(ep, state);
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

            var cmd = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Action")
                    .AddChoices([
                        "Play (download if needed)",
                        "Play/Pause (P)",
                        "Stop (S)",
                        "Seek -15s (Left)",
                        "Seek +15s (Right)",
                        "Mark played",
                        "Back"
                    ]));

            if (cmd == "Back") return;

            if (cmd.StartsWith("Play (download"))
            {
                var progress = new Progress<double>(p =>
                {
                    // no-op; we render progress via Spectre below
                });

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
            else if (cmd.StartsWith("Play/Pause"))
            {
                _player.TogglePause();
            }
            else if (cmd.StartsWith("Stop"))
            {
                _player.Stop();
            }
            else if (cmd.StartsWith("Seek -15"))
            {
                _player.SeekBy(TimeSpan.FromSeconds(-15));
            }
            else if (cmd.StartsWith("Seek +15"))
            {
                _player.SeekBy(TimeSpan.FromSeconds(+15));
            }
            else if (cmd.StartsWith("Mark played"))
            {
                if (!string.IsNullOrWhiteSpace(ep.Id))
                    state.PlayedEpisodeIds.Add(ep.Id);

                AnsiConsole.MarkupLine("[green]Marked played.[/]");
                WaitKey();
            }
        }
    }

    private static void WaitKey()
    {
        AnsiConsole.MarkupLine("[grey]Press any key...[/]");
        Console.ReadKey(true);
    }
}
