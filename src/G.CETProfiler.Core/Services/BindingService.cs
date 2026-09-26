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

    public CaptureBindingInfo InspectCaptureBinding(ProfilerPaths paths)
    {
        if (!File.Exists(paths.Bindings))
            return new CaptureBindingInfo("MISSING", false, null);

        try
        {
            var root = ReadBindingsObject(paths);
            var node = root["CETProfilerControls"] as JsonObject;
            var value = node?["CETProfiler_Toggle"];
            if (value is null)
                return new CaptureBindingInfo("MISSING", false, null);

            long? code = null;
            if (value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<long>(out var numeric))
                    code = numeric;
                else if (jsonValue.TryGetValue<string>(out var text) && long.TryParse(text, out var parsed))
                    code = parsed;
            }

            if (code == F11BindCode)
                return new CaptureBindingInfo("F11", true, code);

            return code.HasValue
                ? new CaptureBindingInfo("CUSTOM", false, code)
                : new CaptureBindingInfo("UNKNOWN", false, null);
        }
        catch
        {
            // Status inspection must never make the whole profiler UI unavailable
            // because CET rewrote or temporarily locked its bindings file.
            return new CaptureBindingInfo("UNKNOWN", false, null);
        }
    }

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

internal sealed record CaptureBindingInfo(string Key, bool IsDefaultF11, long? Code);
