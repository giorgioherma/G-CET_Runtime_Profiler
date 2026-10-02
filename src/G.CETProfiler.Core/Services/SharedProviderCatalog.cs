namespace GCETRuntimeProfiler.Core.Services;

internal sealed record SharedProviderAuthorization(
    string Provider,
    string Getter,
    string Category);

internal static class SharedProviderCatalog
{
    internal static readonly SharedProviderAuthorization[] Authorized =
    [
        new("PLAYER", "GetPlayer", "ENTITY_REFERENCE"),
        new("QUESTS_SYSTEM", "GetQuestsSystem", "SYSTEM_HANDLE"),
        new("STATS_SYSTEM", "GetStatsSystem", "SYSTEM_HANDLE"),
        new("TRANSACTION_SYSTEM", "GetTransactionSystem", "SYSTEM_HANDLE"),
        new("BLACKBOARD_SYSTEM", "GetBlackboardSystem", "SYSTEM_HANDLE"),
        new("TARGETING_SYSTEM", "GetTargetingSystem", "SYSTEM_HANDLE"),
        new("CAMERA_SYSTEM", "GetCameraSystem", "SYSTEM_HANDLE"),
        new("TIME_SYSTEM", "GetTimeSystem", "SYSTEM_HANDLE"),
        new("PREVENTION_SYSTEM", "GetPreventionSystem", "SYSTEM_HANDLE"),
        new("SCRIPTABLE_SYSTEMS_CONTAINER", "GetScriptableSystemsContainer", "SYSTEM_HANDLE"),

        // Additional stable providers proven by the broad discovery pass.
        new("ALL_BLACKBOARD_DEFS", "GetAllBlackboardDefs", "SHARED_LOOKUP"),
        new("WORKSPOT_SYSTEM", "GetWorkspotSystem", "SYSTEM_HANDLE"),
        new("GAME_EFFECT_SYSTEM", "GetGameEffectSystem", "SYSTEM_HANDLE"),
        new("NAVIGATION_SYSTEM", "GetNavigationSystem", "SYSTEM_HANDLE"),
        new("TELEPORTATION_FACILITY", "GetTeleportationFacility", "SYSTEM_HANDLE"),
        new("SYSTEM_REQUESTS_HANDLER", "GetSystemRequestsHandler", "SYSTEM_HANDLE"),
        new("MAPPIN_SYSTEM", "GetMappinSystem", "SYSTEM_HANDLE"),
        new("VEHICLE_SYSTEM", "GetVehicleSystem", "SYSTEM_HANDLE"),
        new("AUDIO_SYSTEM", "GetAudioSystem", "SYSTEM_HANDLE"),
        new("UI_SYSTEM", "GetUISystem", "SYSTEM_HANDLE"),
        new("FADE_SYSTEM", "GetFadeSystem", "SYSTEM_HANDLE"),
        new("DYNAMIC_ENTITY_SYSTEM", "GetDynamicEntitySystem", "SYSTEM_HANDLE"),
        new("STAT_POOLS_SYSTEM", "GetStatPoolsSystem", "SYSTEM_HANDLE"),
        new("STATUS_EFFECT_SYSTEM", "GetStatusEffectSystem", "SYSTEM_HANDLE"),
        new("AI_NAVIGATION_SYSTEM", "GetAINavigationSystem", "SYSTEM_HANDLE"),
        new("JOURNAL_MANAGER", "GetJournalManager", "SYSTEM_HANDLE")
    ];

    internal static readonly IReadOnlyDictionary<string, SharedProviderAuthorization> ByProvider =
        Authorized.ToDictionary(x => x.Provider, StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlyDictionary<string, SharedProviderAuthorization> ByGetter =
        Authorized.ToDictionary(x => x.Getter, StringComparer.OrdinalIgnoreCase);

    internal static readonly HashSet<string> GenerationFamilies = new(
        Authorized.Select(x => x.Provider),
        StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<SharedProviderAuthorization> RuntimeProviders =>
        Authorized.Where(x => !x.Provider.Equals("PLAYER", StringComparison.OrdinalIgnoreCase));
}
