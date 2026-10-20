using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-verified 0-Engine batch of three allocation/redundant-work reductions.
/// No persistent native-entity or mutable-position cache; no scheduling changes.
/// Stages apply separately and never compose over another staged file.
/// </summary>
internal static class ZeroEngineAllocationBatchSource
{
    internal const string SpatialRelative = "0-Engine/modules/SpatialHash.lua";
    internal const string LifecycleRelative = "0-Engine/modules/Lifecycle.lua";
    internal const string RadiusMarker = "-- G-CET SPATIAL_POLL_RADIUS_SNAPSHOT_STAGE_A_V1";
    internal const string RowMarker = "-- G-CET SPATIAL_GRID_X_ROW_REUSE_STAGE_B_V1";
    internal const string LifecycleMarker = "-- G-CET LIFECYCLE_SINGLE_ENTITY_ID_STAGE_A_V1";

    private static readonly Regex SpatialGridLoops = new(
        @"(?m)^(?<outer>[ \t]*)for dx = -cellRadius, cellRadius do\r?\n(?<inner>[ \t]*)for dy = -cellRadius, cellRadius do\r?\n(?<key>[ \t]*)local key = getGridKey\(px \+ dx \* gridSize, py \+ dy \* gridSize, gridSize\)",
        RegexOptions.CultureInvariant);

    internal static bool HasCallsiteEvidence(string captureRoot, string module, int line, string child)
    {
        var path = Path.Combine(captureRoot, "Data", "Runtime", "CET_Runtime_Profile_LuaNative_Callsites.csv");
        if (!File.Exists(path)) return false;
        using var reader = new StreamReader(path);
        if (reader.ReadLine() !=
            "RegistrationId,ProfileEpoch,SampleSequence,Frame,CallerSourceFile,CallerLine,ParentFunctionKey,ChildFunctionKey,SampledCCalls,SampledCOwnPositiveBytes,SampledCOwnPositiveOps,Attribution,Interpretation")
            return false;
        string? row;
        while ((row = reader.ReadLine()) is not null)
        {
            if (!row.Contains(module, StringComparison.OrdinalIgnoreCase)) continue;
            var fields = row.Split(',');
            if (fields.Length != 13) continue;
            var pathPart = fields[4].Replace('\\', '/').Trim('"');
            if (!pathPart.EndsWith("/mods/0-Engine/modules/" + module, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var foundLine) ||
                foundLine <= 0) continue;
            if (!fields[7].Contains("|" + child + "|C", StringComparison.Ordinal)) continue;
            if (!long.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out var positive) ||
                positive <= 0) continue;
            return true;
        }
        return false;
    }

    internal static bool TryTransformSpatial(byte[] source, out byte[] result, out string reason) =>
        TryTransformSpatialCore(source, out result, out reason);

    // Production and tests both use current-source structural proofs.
    internal static bool TryTransformSpatialCore(byte[] source,
        out byte[] result, out string reason)
    {
        result = source;
        var original = Encoding.UTF8.GetString(source);
        if (original.Contains(RowMarker, StringComparison.Ordinal))
        {
            reason = "ALREADY_SATISFIED";
            return false;
        }
        var nl = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var updated = original;
        var alreadyRadius = original.Contains(RadiusMarker, StringComparison.Ordinal);
        if (!alreadyRadius)
        {
            var anchor = "    config = config or {}" + nl + "    entries = entries or {}";
            if (Count(updated, anchor) != 1)
            {
                reason = "Ambiguous spatial registration anchor.";
                return false;
            }
            updated = updated.Replace(anchor, string.Join(nl, new[]
            {
                anchor, "",
                "    -- Immutable per-set polling geometry (no entity/position caching). " + RadiusMarker,
                "    local configuredGridSize = config.gridSize or defaultGridSize",
                "    local pollCellRadius = nil",
                "    if type(config.pollRadius) == \"number\" and type(configuredGridSize) == \"number\"",
                "        and configuredGridSize > 0 then",
                "        pollCellRadius = math.ceil(config.pollRadius / configuredGridSize)",
                "        if pollCellRadius < 1 then pollCellRadius = 1 end",
                "    end"
            }), StringComparison.Ordinal);

            var radiusSq = "            pollRadiusSq = config.pollRadius and (config.pollRadius * config.pollRadius) or nil,";
            if (Count(updated, radiusSq) != 1)
            {
                reason = "Ambiguous pollRadiusSq registration.";
                return false;
            }
            updated = updated.Replace(radiusSq, radiusSq + nl +
                "            pollCellRadius = pollCellRadius,", StringComparison.Ordinal);
            var oldPoll = "            local cellRadius = math.ceil(cfg.pollRadius / gridSize)" + nl +
                "            if cellRadius < 1 then cellRadius = 1 end";
            if (Count(updated, oldPoll) != 1)
            {
                reason = "Ambiguous original poll radius.";
                return false;
            }
            var newPoll = string.Join(nl, new[]
            {
                "            local cellRadius = cfg.pollCellRadius",
                "            if cellRadius == nil then",
                "                cellRadius = math.ceil(cfg.pollRadius / gridSize)",
                "                if cellRadius < 1 then cellRadius = 1 end",
                "            end"
            });
            updated = updated.Replace(oldPoll, newPoll, StringComparison.Ordinal);
        }

        if (SpatialGridLoops.Matches(updated).Count != 3)
        {
            reason = "Expected exactly three unchanged grid loops (nearest, within and polling).";
            return false;
        }
        updated = SpatialGridLoops.Replace(updated, m =>
        {
            var outer = m.Groups["outer"].Value;
            var inner = m.Groups["inner"].Value;
            var key = m.Groups["key"].Value;
            return outer + "for dx = -cellRadius, cellRadius do" + nl +
                inner + "local gx = math.floor((px + dx * gridSize) / gridSize)  " + RowMarker + nl +
                inner + "for dy = -cellRadius, cellRadius do" + nl +
                key + "local gy = math.floor((py + dy * gridSize) / gridSize)" + nl +
                key + "local key = (gx + 10000) * 20000 + (gy + 10000)";
        });

        if (Count(updated, RadiusMarker) != 1 ||
            Count(updated, RowMarker) != 3 ||
            Count(updated, "local gy = math.floor((py + dy * gridSize) / gridSize)") != 3 ||
            Count(updated, "local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)") != 0 ||
            Count(updated, "function SpatialHash.QueryNearest(") != 1 ||
            Count(updated, "function SpatialHash.QueryWithin(") != 1 ||
            Count(updated, "function SpatialHash.Update(frame)") != 1 ||
            Count(updated, "cfg.pollCellRadius") != 1 ||
            Count(updated, "cfg.pollRadius / gridSize") != 1)
        {
            reason = "SpatialHash postconditions failed.";
            return false;
        }

        result = Encoding.UTF8.GetBytes(updated);
        reason = "APPLIED";
        return true;
    }

    internal static bool TryTransformLifecycle(byte[] source, out byte[] result, out string reason) =>
        TryTransformLifecycleCore(source, out result, out reason);

    internal static bool TryTransformLifecycleCore(byte[] source,
        out byte[] result, out string reason)
    {
        result = source;
        var original = Encoding.UTF8.GetString(source);
        if (original.Contains(LifecycleMarker, StringComparison.Ordinal))
        {
            reason = "ALREADY_SATISFIED";
            return false;
        }
        var nl = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var oldHelper = string.Join(nl, new[]
        {
            "local function GetPlayerHash(player)",
            "    if not player then return nil end",
            "",
            "    local ok, id = pcall(player.GetEntityID, player)",
            "    if not ok or not id then return nil end",
            "",
            "    return id.hash",
            "end"
        });
        var newHelper = string.Join(nl, new[]
        {
            "-- The validated ID from Update is valid for this same synchronous check. " + LifecycleMarker,
            "local function GetPlayerHash(id)",
            "    if not id then return nil end",
            "    return id.hash",
            "end"
        });
        var oldUpdate = string.Join(nl, new[]
        {
            "    -- Defensive engine transition protection",
            "    if player then",
            "        local ok2, id = pcall(player.GetEntityID, player)",
            "        if not ok2 or not id then",
            "            player = nil",
            "        end",
            "    end",
            "",
            "    local hash = GetPlayerHash(player)"
        });
        var newUpdate = string.Join(nl, new[]
        {
            "    -- Defensive engine transition protection; reuse this same-update ID.",
            "    local checkedId = nil",
            "    if player then",
            "        local ok2, id = pcall(player.GetEntityID, player)",
            "        if not ok2 or not id then",
            "            player = nil",
            "        else",
            "            checkedId = id",
            "        end",
            "    end",
            "",
            "    local hash = GetPlayerHash(checkedId)"
        });

        if (Count(original, oldHelper) != 1 || Count(original, oldUpdate) != 1 ||
            Count(original, "function Lifecycle.Update()") != 1 ||
            Count(original, "function Lifecycle.ForceInvalidate()") != 1 ||
            Count(original, "Events.PlayerReady:trigger(player)") != 1 ||
            Count(original, "Events.PlayerRecreated:trigger(player)") != 1 ||
            // Author defines two distinct invalidation paths:
            // transient 30-frame loss and ForceInvalidate/session teardown.
            Count(original, "Events.PlayerInvalidated:trigger()") != 2)
        {
            reason = "Lifecycle exact anchors or event paths drifted.";
            return false;
        }
        var updated = original.Replace(oldHelper, newHelper, StringComparison.Ordinal)
            .Replace(oldUpdate, newUpdate, StringComparison.Ordinal);
        if (Count(updated, LifecycleMarker) != 1 ||
            Count(updated, "pcall(player.GetEntityID, player)") != 1 ||
            Count(updated, "return id.hash") != 1 ||
            Count(updated, "local hash = GetPlayerHash(checkedId)") != 1 ||
            Count(updated, "Events.PlayerReady:trigger(player)") != 1 ||
            Count(updated, "Events.PlayerRecreated:trigger(player)") != 1 ||
            Count(updated, "Events.PlayerInvalidated:trigger()") != 2)
        {
            reason = "Lifecycle postconditions failed.";
            return false;
        }
        result = Encoding.UTF8.GetBytes(updated);
        reason = "APPLIED";
        return true;
    }

    internal static int TryStage(string captureRoot, string modsRoot,
        Dictionary<string, byte[]> staged, Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile, List<object> transformManifest,
        List<object> skipped)
    {
        var applied = 0;
        foreach (var descriptor in new[]
        {
            (Relative: SpatialRelative, Module: "SpatialHash.lua", Line: 272, Child: "ceil",
             Rule: "0engine-spatial-fixed-poll-radius-and-row-key-batch",
             Handler: "ZeroEngineAllocationBatchSource.Spatial", Policy: "SOURCE_VERIFIED_PER_SET_SCALAR_AND_ROW_REUSE"),
            (Relative: LifecycleRelative, Module: "Lifecycle.lua", Line: 46, Child: "__index",
             Rule: "0engine-lifecycle-validated-id-reuse",
             Handler: "ZeroEngineAllocationBatchSource.Lifecycle", Policy: "SOURCE_VERIFIED_SAME_UPDATE_ENTITY_ID_REUSE")
        })
        {
            if (!HasCallsiteEvidence(captureRoot, descriptor.Module, descriptor.Line, descriptor.Child))
                continue;
            var livePath = Path.Combine(modsRoot, "0-Engine", "modules", descriptor.Module);
            if (!File.Exists(livePath)) continue;
            if (staged.ContainsKey(descriptor.Relative))
            {
                skipped.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                    RuleId = descriptor.Rule, reason = "File already staged by a prior transform; no composition." });
                continue;
            }
            var source = File.ReadAllBytes(livePath);
            byte[] next;
            string reason;
            var ok = descriptor.Module == "SpatialHash.lua"
                ? TryTransformSpatial(source, out next, out reason)
                : TryTransformLifecycle(source, out next, out reason);
            if (!ok)
            {
                if (reason != "ALREADY_SATISFIED")
                    skipped.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                        RuleId = descriptor.Rule, reason });
                continue;
            }
            staged[descriptor.Relative] = next;
            liveSourceHashes[descriptor.Relative] =
                Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            appliedTransformsByFile[descriptor.Relative] = 1;
            transformManifest.Add(new
            {
                type = "SEMANTIC_RULE",
                RuleId = descriptor.Rule,
                Owner = "0-Engine",
                Handler = descriptor.Handler,
                PolicyClass = descriptor.Policy,
                sourceProof = true,
                sourceSha256 = liveSourceHashes[descriptor.Relative],
                evidence = "Bounded positive Lua/C source-specific allocation callsite",
                patchStyle = descriptor.Module == "SpatialHash.lua"
                    ? "REGISTER_ONCE_RADIUS_AND_WITHIN_SEARCH_ROW_X"
                    : "WITHIN_UPDATE_PLAYER_ID_SCALAR_REUSE",
                callbackOrCadenceChanged = false,
                persistentEntityCache = false,
                gameplayVerification = "PENDING_COMBINED_BATCH"
            });
            applied++;
        }
        return applied;
    }

    private static int Count(string source, string fragment) =>
        (source.Length - source.Replace(fragment, "", StringComparison.Ordinal).Length) / fragment.Length;
}
