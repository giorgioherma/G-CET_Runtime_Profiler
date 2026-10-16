#!/usr/bin/env python3
"""Contract: native-only zero-Engine attribution cannot silently become Lua execution routing."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
native = (root / "native-profiler-build/v2.11.0/CETRuntimeProfiler.h").read_text(encoding="utf-8")
patch = (root / "native-profiler-build/v2.11.0/Patch-CET-v1.37.1.ps1").read_text(encoding="utf-8")
normal = (root / ".github/workflows/native-profiler-rebuild.yml").read_text(encoding="utf-8")

assert 'counter->Mod == "0-Engine"' in native
assert 'counter->Mod == "CETProfilerControls" || calls < 2' in native
assert 'counter->Mod == "0-Engine" ||' not in native, "0-Engine deep sampling is still excluded"
assert "MaxZeroEngineDeepRegistrations = 1" in native
assert "MaxDeepHotRegistrations = 6" in native, "Normal mod quota must remain unchanged"
assert "ZeroEngineDeepTargetSamples = 20" in native
assert "std::vector<Candidate> zeroEngineCandidates;" in native
assert "zeroEngineCandidates.push_back({" in native
assert "std::min(MaxZeroEngineDeepRegistrations," in native
assert "std::min(MaxDeepHotRegistrations, candidates.size())" in native
assert "DeepSelected.store(" in native
assert "CET_Runtime_Profile_ZeroEngine_SourceAttribution.csv" in native
assert "CET_Runtime_Profile_ZeroEngine_Callsites.csv" in native
assert 'zeroRegistration.count(row.RegistrationId)' in native
assert 'row->SampledExclusiveAllocBytes' in native
assert 'row->SampledExclusiveAllocations' in native
assert "sampled-relative-composition-not-absolute-runtime" in native
assert "sampled-child-inclusive-overlap-not-additive" in native
assert 'lua_getinfo(aLuaState, "S", aDebug)' in native
assert "RecordDeepLine" in native
assert "DeepTraceScope" in native
assert "CETProfilerSchedulerRegisterJob" in patch
assert "counter->Mod == \"0-Engine\"" in native
assert "dev/resolver-profiler-handoff" in normal
assert "Verify 0-Engine source attribution contract" in normal
assert "CET_Runtime_Profile_ZeroEngine_SourceAttribution.csv" in normal
assert "CET_Runtime_Profile_ZeroEngine_Callsites.csv" in normal
assert "native-profiler-build/v2.11.0/Build-Profiler.ps1" in normal
assert "Get-CETPEInfo.ps1" in normal
assert "DLL_SHA256.txt" in normal
assert "cyber_engine_tweaks.PROFILER.pdb" in normal
print("native 0-Engine source attribution original-CI and schema contract passed")
