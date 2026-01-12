using Spectre.Console;

namespace WinTuiPod;

internal static class Program
{
    public static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var paths = AppPaths.Create();
        var store = new DataStore(paths);
        var player = new AudioPlayer();

        var app = new TuiApp(store, player);
        await app.RunAsync();
    }
}
