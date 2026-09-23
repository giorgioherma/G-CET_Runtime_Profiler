using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }

        try
        {
            return RunCli(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                ok = false,
                error = FlattenMessage(ex)
            }, JsonOptions));
            return 1;
        }
    }

    private static int RunCli(string[] args)
    {
        string? command = null;
        string? game = null;
        bool coreOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--status": command = "status"; break;
                case "--install": command = "install"; break;
                case "--collect": command = "collect"; break;
                case "--reset": command = "reset"; break;
                case "--restore": command = "restore"; break;
                case "--core-only": coreOnly = true; break;
                case "--json": break; // JSON is always used in headless mode.
                case "--game" when i + 1 < args.Length: game = args[++i]; break;
                case "--help":
                case "-h":
                case "/?":
                    Console.WriteLine(HelpText);
                    return 0;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Specify --status, --install, --collect, --reset, or --restore.");
        if (string.IsNullOrWhiteSpace(game))
            throw new ArgumentException("Specify --game <Cyberpunk 2077 root>.");

        IProfilerService service = new ProfilerService();

        object result = command switch
        {
            "status" => service.GetStatus(game),
            "install" => service.Install(game, coreOnly),
            "collect" => new
            {
                ok = true,
                destination = service.Collect(game),
                status = service.GetStatus(game)
            },
            "reset" => new
            {
                ok = true,
                archived = service.ResetLive(game),
                status = service.GetStatus(game)
            },
            "restore" => new
            {
                ok = true,
                archived = service.Restore(game),
                status = service.GetStatus(game)
            },
            _ => throw new InvalidOperationException()
        };

        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static string FlattenMessage(Exception ex)
    {
        if (ex is AggregateException aggregate)
            return string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(x => x.Message));

        return ex.Message;
    }

    private const string HelpText =
        "G-CET-Runtime-Profiler headless interface\n" +
        "\n" +
        "  --status  --game <root> --json\n" +
        "  --install --game <root> [--core-only] --json\n" +
        "  --collect --game <root> --json\n" +
        "  --reset   --game <root> --json\n" +
        "  --restore --game <root> --json\n";
}
