using System.Text.Json;
using System.Text.Json.Nodes;
using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed class BindingService
{
    public const long F11BindCode = 34339947158700032L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void EnsureDefaultF11IfMissing(ProfilerPaths paths)
    {
        var root = ReadBindingsObject(paths);

        var node = root["CETProfilerControls"] as JsonObject;
        if (node is null)
        {
            node = new JsonObject();
            root["CETProfilerControls"] = node;
        }

        // CET owns the user's binding choice. Seed F11 only for a brand-new
        // profiler input; never overwrite a binding the user already selected.
        if (node["CETProfiler_Toggle"] is not null)
            return;

        node["CETProfiler_Toggle"] = F11BindCode;
        node.Remove("CETProfiler_Dump");

        WriteBindings(paths, root);
    }

    private static JsonObject ReadBindingsObject(ProfilerPaths paths)
    {
        if (!File.Exists(paths.Bindings)) return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(paths.Bindings)) as JsonObject
                ?? throw new InvalidOperationException("CET bindings.json root is not an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("CET bindings.json is invalid JSON. No binding changes were made.", ex);
        }
    }

    private static void WriteBindings(ProfilerPaths paths, JsonObject root)
    {
        Directory.CreateDirectory(paths.CetRoot);
        File.WriteAllText(paths.Bindings, root.ToJsonString(JsonOptions) + Environment.NewLine);
    }
}
