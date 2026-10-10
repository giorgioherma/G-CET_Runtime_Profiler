param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'

$path = Join-Path $SourceRoot 'src\lj_gc.c'
if (!(Test-Path -LiteralPath $path)) { throw "LuaJIT collector missing: $path" }
$src = [IO.File]::ReadAllText($path)
if ($src.Contains('GCET_COLLECTOR_PROBE_V1')) { return }

function Change-Once([string]$src, [string]$anchor, [string]$replacement, [string]$label) {
    $n = ([regex]::Matches($src, [regex]::Escape($anchor))).Count
    if ($n -ne 1) { throw "LuaJIT collector source changed: $label matched $n times" }
    return $src.Replace($anchor, $replacement)
}

$probe = @'
/* GCET_COLLECTOR_PROBE_V1: private LuaJIT static-library readout.
 * Collect and aggregate native collector execution durations, without forcing,
 * changing or advancing garbage collection or invoking the host profiler.
 * Disabled outside explicit capture. No heap allocations or logger calls. */
#include <windows.h>

typedef struct GCETGCStats {
  unsigned long long stepCalls, stepTicks, maxStepTicks, completedCycles;
  unsigned long long fullCalls, fullTicks, maxFullTicks, frequency;
} GCETGCStats;

static volatile LONG gcet_gc_on = 0;
static volatile LONG64 gcet_gc_step_count = 0, gcet_gc_step_ticks = 0;
static volatile LONG64 gcet_gc_step_max = 0, gcet_gc_completed = 0;
static volatile LONG64 gcet_gc_full_count = 0, gcet_gc_full_ticks = 0;
static volatile LONG64 gcet_gc_full_max = 0;

static void gcet_gc_atomic_max(volatile LONG64 *target, LONG64 duration)
{
  LONG64 old = InterlockedCompareExchange64(target, 0, 0);
  while (old < duration) {
    LONG64 observed = InterlockedCompareExchange64(target, duration, old);
    if (observed == old) return;
    old = observed;
  }
}

void gcet_gc_stats_set(int enable, int reset)
{
  InterlockedExchange(&gcet_gc_on, 0);
  if (reset) {
    InterlockedExchange64(&gcet_gc_step_count, 0);
    InterlockedExchange64(&gcet_gc_step_ticks, 0);
    InterlockedExchange64(&gcet_gc_step_max, 0);
    InterlockedExchange64(&gcet_gc_completed, 0);
    InterlockedExchange64(&gcet_gc_full_count, 0);
    InterlockedExchange64(&gcet_gc_full_ticks, 0);
    InterlockedExchange64(&gcet_gc_full_max, 0);
  }
  if (enable) InterlockedExchange(&gcet_gc_on, 1);
}

void gcet_gc_stats_read(GCETGCStats *out)
{
  LARGE_INTEGER freq;
  if (!out) return;
  out->stepCalls = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_step_count, 0, 0);
  out->stepTicks = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_step_ticks, 0, 0);
  out->maxStepTicks = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_step_max, 0, 0);
  out->completedCycles = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_completed, 0, 0);
  out->fullCalls = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_full_count, 0, 0);
  out->fullTicks = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_full_ticks, 0, 0);
  out->maxFullTicks = (unsigned long long)InterlockedCompareExchange64(&gcet_gc_full_max, 0, 0);
  out->frequency = QueryPerformanceFrequency(&freq) && freq.QuadPart > 0
      ? (unsigned long long)freq.QuadPart : 0;
}

'@
$src = Change-Once $src '#include "lj_obj.h"' ('#include "lj_obj.h"' + [Environment]::NewLine + $probe) 'GC definitions'
$src = Change-Once $src 'int LJ_FASTCALL lj_gc_step(lua_State *L)' 'static int LJ_FASTCALL gcet_original_gc_step(lua_State *L)' 'GC step function'

$step = @'
int LJ_FASTCALL lj_gc_step(lua_State *L)
{
  LARGE_INTEGER begin, end;
  LONG64 ticks;
  int result;
  if (InterlockedCompareExchange(&gcet_gc_on, 0, 0) == 0)
    return gcet_original_gc_step(L);
  QueryPerformanceCounter(&begin);
  result = gcet_original_gc_step(L);
  QueryPerformanceCounter(&end);
  ticks = end.QuadPart - begin.QuadPart;
  if (ticks < 0) ticks = 0;
  InterlockedIncrement64(&gcet_gc_step_count);
  InterlockedAdd64(&gcet_gc_step_ticks, ticks);
  gcet_gc_atomic_max(&gcet_gc_step_max, ticks);
  if (result == 1) InterlockedIncrement64(&gcet_gc_completed);
  return result;
}

'@
$src = Change-Once $src '/* Ditto, but fix the stack top first. */' ($step + '/* Ditto, but fix the stack top first. */') 'incremental wrapper'
$src = Change-Once $src 'void lj_gc_fullgc(lua_State *L)' 'static void gcet_original_gc_full(lua_State *L)' 'full GC function'

$full = @'
void lj_gc_fullgc(lua_State *L)
{
  LARGE_INTEGER begin, end;
  LONG64 ticks;
  if (InterlockedCompareExchange(&gcet_gc_on, 0, 0) == 0) {
    gcet_original_gc_full(L);
    return;
  }
  QueryPerformanceCounter(&begin);
  gcet_original_gc_full(L);
  QueryPerformanceCounter(&end);
  ticks = end.QuadPart - begin.QuadPart;
  if (ticks < 0) ticks = 0;
  InterlockedIncrement64(&gcet_gc_full_count);
  InterlockedAdd64(&gcet_gc_full_ticks, ticks);
  gcet_gc_atomic_max(&gcet_gc_full_max, ticks);
}

'@
$src = Change-Once $src '/* -- Write barriers ------------------------------------------------------ */' ($full + '/* -- Write barriers ------------------------------------------------------ */') 'full GC wrapper'
[IO.File]::WriteAllText($path, $src, [Text.UTF8Encoding]::new($false))
Write-Host 'GCET collector probe: instrumented pinned LuaJIT source (capture-gated).'
