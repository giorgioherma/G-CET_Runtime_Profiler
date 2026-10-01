using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler.Resolver;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new ResolverForm());
            return 0;
        }

        try
        {
            string? capture = null;
            string? mods = null;
            string? game = null;
            string? semanticLibrary = null;
            string? passOutput = null;
            var generatePass = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--capture" when i + 1 < args.Length: capture = args[++i]; break;
                    case "--mods" when i + 1 < args.Length: mods = args[++i]; break;
                    case "--game" when i + 1 < args.Length: game = args[++i]; break;
                    case "--semantic-library" when i + 1 < args.Length: semanticLibrary = args[++i]; break;
                    case "--generate-pass": generatePass = true; break;
                    case "--pass-output" when i + 1 < args.Length: passOutput = args[++i]; break;
                    case "--json": break;
                    case "--help":
                    case "-h":
                    case "/?":
                        Console.WriteLine(
                            "G-CET Resolver\n\n" +
                            "  --capture <collected result folder> --mods <live CET mods folder> --json\n" +
                            "  --capture <collected result folder> --game <Cyberpunk 2077 root> --json\n" +
                            "  [--generate-pass] [--pass-output <zip path>]\n" +
                            "  [--semantic-library <semantic rule library json>]");
                        return 0;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }

            if (string.IsNullOrWhiteSpace(capture))
                throw new ArgumentException("Specify --capture <collected CET result folder>.");

            if (string.IsNullOrWhiteSpace(mods))
            {
                if (string.IsNullOrWhiteSpace(game))
                    throw new ArgumentException("Specify --mods <live CET mods folder> or --game <Cyberpunk 2077 root>.");
                mods = Path.Combine(Path.GetFullPath(game), "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
            }

            var result = ResolverService.Resolve(capture, mods, semanticLibrary);
            PassBuildResult? pass = null;
            if (generatePass)
                pass = ResolverService.GeneratePass(capture, mods, passOutput);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                result.ResolverPath,
                result.CadenceRuntimePath,
                result.CadenceFinalPath,
                result.FamilyCount,
                result.RankedCallbackCount,
                result.GenericResolvedCount,
                result.NonFrameOnlyAutoCount,
                result.FrameOnlyAutoCount,
                result.SemanticReadyRuleCount,
                result.AlreadySatisfiedCount,
                result.MaterialRemainingCount,
                result.BelowThresholdCount,
                result.UnresolvedCount,
                pass
            }, JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions));
            return 1;
        }
    }
}
