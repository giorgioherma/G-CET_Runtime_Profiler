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

    public BindingTransactionState Snapshot(ProfilerPaths paths)
    {
        var fileExisted = File.Exists(paths.Bindings);
        var root = ReadBindingsObject(paths);
        var hadNode = root.TryGetPropertyValue("CETProfilerControls", out var node) && node is not null;

        return new BindingTransactionState
        {
            FileExistedBefore = fileExisted,
            HadNode = hadNode,
            OriginalNodeJson = hadNode ? node!.ToJsonString() : "",
            InstalledToggle = F11BindCode
        };
    }

    public void SetDefaultF11(ProfilerPaths paths)
    {
        var root = ReadBindingsObject(paths);

        var node = root["CETProfilerControls"] as JsonObject ?? new JsonObject();
        node["CETProfiler_Toggle"] = F11BindCode;
        node.Remove("CETProfiler_Dump");
        root["CETProfilerControls"] = node;

        WriteBindings(paths, root);
    }

    public bool IsF11Configured(ProfilerPaths paths)
    {
        if (!File.Exists(paths.Bindings)) return false;

        try
        {
            var root = ReadBindingsObject(paths);
            var node = root["CETProfilerControls"] as JsonObject;
            if (node?["CETProfiler_Toggle"] is not JsonValue value) return false;
            return value.TryGetValue<long>(out var code) && code == F11BindCode;
        }
        catch
        {
            return false;
        }
    }

    public void Restore(ProfilerPaths paths, BindingTransactionState? state)
    {
        state ??= ReadLegacyTotalState(paths);

        var root = ReadBindingsObject(paths);
        root.Remove("CETProfilerControls");

        if (state?.HadNode == true)
        {
            if (string.IsNullOrWhiteSpace(state.OriginalNodeJson))
                throw new InvalidOperationException("Saved CETProfilerControls binding state is empty.");

            root["CETProfilerControls"] = JsonNode.Parse(state.OriginalNodeJson)
                ?? throw new InvalidOperationException("Saved CETProfilerControls binding state is invalid.");
        }

        var fileExistedBefore = state?.FileExistedBefore ?? true;
        if (!fileExistedBefore && root.Count == 0)
            FileSystemService.DeleteFileIfExists(paths.Bindings);
        else
            WriteBindings(paths, root);

        FileSystemService.DeleteFileIfExists(paths.LegacyTotalBindingState);
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

    private static BindingTransactionState? ReadLegacyTotalState(ProfilerPaths paths)
    {
        if (!File.Exists(paths.LegacyTotalBindingState)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(paths.LegacyTotalBindingState));
            var root = doc.RootElement;

            var existed = root.TryGetProperty("bindingsFileExisted", out var existedNode)
                ? existedNode.GetBoolean()
                : true;
            var hadNode = root.TryGetProperty("hadNode", out var hadNodeValue) && hadNodeValue.GetBoolean();
            var originalNode = "";

            if (hadNode && root.TryGetProperty("node", out var node))
                originalNode = node.GetRawText();

            return new BindingTransactionState
            {
                FileExistedBefore = existed,
                HadNode = hadNode,
                OriginalNodeJson = originalNode,
                InstalledToggle = F11BindCode
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("Legacy TOTAL Profiler CET binding state is invalid; binding restore was not attempted.", ex);
        }
    }
}
