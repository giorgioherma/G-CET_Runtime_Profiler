using System.Text.Json;
using GCETRuntimeProfiler.Core.Contracts;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length == 0)
        {
            Application.Run(new MainForm());
            return 0;
        }

        try
        {
            return RunCli(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
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
                case "--game" when i + 1 < args.Length: game = args[++i]; break;
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
            "collect" => new { ok = true, destination = service.Collect(game) },
            "reset" => new { ok = true, archived = service.ResetLive(game) },
            "restore" => new { ok = true, archived = service.Restore(game) },
            _ => throw new InvalidOperationException()
        };

        Console.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }
}
