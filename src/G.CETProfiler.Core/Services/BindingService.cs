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
        string? originalFileHash = null;

        if (fileExisted)
        {
            originalFileHash = FileSystemService.Sha256(paths.Bindings);
            FileSystemService.CopyFileVerified(paths.Bindings, paths.BackupBindings, originalFileHash);
        }

        var root = ReadBindingsObject(paths);
        var hadNode = root.TryGetPropertyValue("CETProfilerControls", out var node) && node is not null;

        return new BindingTransactionState
        {
            FileExistedBefore = fileExisted,
            HadNode = hadNode,
            OriginalNodeJson = hadNode ? node!.ToJsonString() : ""
        };
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

    public void ValidateRestore(ProfilerPaths paths, BindingTransactionState? state)
    {
        state ??= ReadLegacyTotalState(paths);
        if (state is null) return;

        if (state.FileExistedBefore && !File.Exists(paths.Bindings))
            throw new InvalidOperationException(
                "CET bindings.json existed before profiling but is now missing. " +
                "Automatic restore will not recreate only part of the file. The full original backup is preserved for manual recovery.");

        _ = ReadBindingsObject(paths);

        if (state.HadNode)
        {
            if (string.IsNullOrWhiteSpace(state.OriginalNodeJson))
                throw new InvalidOperationException("Saved CETProfilerControls binding state is empty.");

            try
            {
                _ = JsonNode.Parse(state.OriginalNodeJson)
                    ?? throw new InvalidOperationException("Saved CETProfilerControls binding state is invalid.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Saved CETProfilerControls binding state is invalid.", ex);
            }
        }
    }

    public void Restore(ProfilerPaths paths, BindingTransactionState? state)
    {
        state ??= ReadLegacyTotalState(paths);
        if (state is null) return;

        ValidateRestore(paths, state);

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
                OriginalNodeJson = originalNode
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("Legacy TOTAL Profiler CET binding state is invalid; binding restore was not attempted.", ex);
        }
    }
}
