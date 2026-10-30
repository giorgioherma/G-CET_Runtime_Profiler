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
assert "CET_Runtime_Profile_ZeroEngine_ProducerAttribution.csv" in native
assert "CET_Runtime_Profile_ZeroEngine_ConsumerCallsites.csv" in native
assert "PHASE3_ZERO_ENGINE_PRODUCER_V1" in native
assert 'callsite.ChildSourceFile = frame.SourceFile' in native
assert 'callsite.SampledChildOwnPositiveBytes += ownBytes' in native
assert 'callsite.SampledChildOwnPositiveOps += ownOps' in native
assert 'SAMPLED_CHILD_EXCLUSIVE_NOT_ADDITIVE_WITH_PARENT' in native
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
assert "CET_Runtime_Profile_ZeroEngine_ProducerAttribution.csv" in normal
assert "CET_Runtime_Profile_ZeroEngine_ConsumerCallsites.csv" in normal
assert "native-profiler-build/v2.11.0/Build-Profiler.ps1" in normal
assert "Get-CETPEInfo.ps1" in normal
assert "DLL_SHA256.txt" in normal
assert "cyber_engine_tweaks.PROFILER.pdb" in normal
print("native 0-Engine source attribution original-CI and schema contract passed")

# Capture-gated C caller-site and owner-zero native pre-dispatch contracts.
assert 'CET_Runtime_Profile_LuaNative_Callsites.csv' in native
assert 'CET_Runtime_Profile_Native_PreDispatchPressure.csv' in native
assert 'SampledCFunctionOwnPositiveBytes += ownBytes' in native
assert 'frame.What == "C" && frame.SourceFile == "=[C]"' in native
assert 'gcet_gc_unscoped_thread_bytes' in native and 'ResetPreDispatch();' in native
assert 'gcet_unscoped_thread_bytes += newSize - oldSize' in Path('native-profiler-build/v2.11.0/Patch-LuaJIT-GC-Source.ps1').read_text(encoding='utf-8')
patch_text = Path('native-profiler-build/v2.11.0/Patch-CET-v1.37.1.ps1').read_text(encoding='utf-8')
assert patch_text.count('EndPreDispatch(') == 3
assert patch_text.count('BeginPreDispatch()') == 5

# CI #91 regression: the earlier MethodScope patch modifies the same
# Overridden block before this new pre-dispatch patch runs.
# A pristine upstream-only search fails, so match the post-patch marker.
overridden_snapshot = """    -Old @'
        TiltedPhoques::Allocator::Set(pAllocator);

        {
            CETRuntimeProfiler::MethodScope ggcMethodScope(
                7, apFunction->fullName.hash,
'@ \`
"""
assert overridden_snapshot.replace('\\`', '`') in patch_text, "Overridden snapshot must match prior MethodScope insertion"
assert patch_text.index('-Label "OverriddenScript argument marshalling native boundary"') < patch_text.index('-Label "Overridden pre-dispatch allocation snapshot"')

# Exact native method-stage attribution, capture-only and bounded.
assert "NativeStageCount = 5" in native
assert "NativeStageSlots = 256" in native
assert "CET_Runtime_Profile_Native_MethodStages.csv" in native
assert "OWNER_ZERO_POSITIVE_LUAJIT_GROWTH_STAGE_WINDOW_NOT_ADDITIVE" in native
assert "RecordNativeStage(boundary - 6, methodHash, start)" in native
assert native.count("ResetNativeStages();") == 2
assert patch.count('RecordNativeStage(') == 2
assert patch.count('BeginPreDispatch()') == 5
assert patch.count('EndPreDispatch(') == 3
assert 'No RAII across LuaJIT longjmp/Windows SEH' in patch
print("native exact-method stage source contract passed")

# Native PE VERSIONINFO is built into the real DLL, not a renamed artifact.
assert "$gcetNativeVersion = '2.16.1'" in patch
assert "src\\xmake\\Resource.rc.in" in patch
assert "src\\config\\Resource.rc" in patch
assert "G-CET Native Runtime Profiler (CET 1.37.1)" in patch
assert "CET_VERSION C++ macros (1.37.1)" in patch
build_script = Path('native-profiler-build/v2.11.0/Build-Profiler.ps1').read_text(encoding='utf-8')
assert "Compiled G-CET native VERSIONINFO mismatch" in build_script
assert "Compiled native VERSIONINFO mismatch" in normal
assert "NATIVE_VERSION.txt" in normal
print("native resource version 2.16.1 compiler and workflow source contracts passed")
