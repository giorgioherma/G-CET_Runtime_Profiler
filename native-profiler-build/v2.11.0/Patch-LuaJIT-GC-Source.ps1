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
/* GCET_COLLECTOR_PROBE_V2: phase + allocation evidence, capture-only. */
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


/* Numeric states from lj_gc.h: pause, propagate, atomic, string sweep, sweep, finalize. */
typedef struct GCETGCSlowEvent {
  unsigned long long startNs, endNs, threadId, registrationId;
  unsigned long long heapBefore, heapAfter, threshold, debt;
  unsigned int phaseBefore, phaseAfter, pause, stepmul;
} GCETGCSlowEvent;
typedef struct GCETGCPhaseStats {
  unsigned long long calls[6], ticks[6];
} GCETGCPhaseStats;
#define GCET_SLOW_CAPACITY 2048
static GCETGCSlowEvent gcet_slow_events[GCET_SLOW_CAPACITY];
static volatile LONG gcet_slow_seen = 0;
static volatile LONG64 gcet_phase_calls[6], gcet_phase_ticks[6];
static volatile LONG64 gcet_segment_ticks = 0, gcet_segment_offset_ns = 0, gcet_frequency = 0;
static __declspec(thread) unsigned long long gcet_current_registration = 0;
static __declspec(thread) unsigned long long gcet_gross_alloc_bytes = 0;

unsigned long long gcet_gc_scope_set(unsigned long long registration)
{
  unsigned long long old = gcet_current_registration;
  gcet_current_registration = registration;
  return old;
}
unsigned long long gcet_gc_alloc_bytes(void) { return gcet_gross_alloc_bytes; }
static void gcet_gc_alloc_observe(unsigned long long oldSize, unsigned long long newSize)
{
  if (newSize > oldSize && InterlockedCompareExchange(&gcet_gc_on, 0, 0) != 0)
    gcet_gross_alloc_bytes += newSize - oldSize;
}
void gcet_gc_trace_segment(unsigned long long captureOffsetNs)
{
  LARGE_INTEGER tick, freq;
  QueryPerformanceCounter(&tick);
  QueryPerformanceFrequency(&freq);
  InterlockedExchange64(&gcet_segment_ticks, tick.QuadPart);
  InterlockedExchange64(&gcet_frequency, freq.QuadPart);
  InterlockedExchange64(&gcet_segment_offset_ns, (LONG64)captureOffsetNs);
}
static unsigned long long gcet_gc_capture_ns(LONG64 ticks)
{
  LONG64 base = InterlockedCompareExchange64(&gcet_segment_ticks, 0, 0);
  LONG64 freq = InterlockedCompareExchange64(&gcet_frequency, 0, 0);
  unsigned long long offset = (unsigned long long)InterlockedCompareExchange64(&gcet_segment_offset_ns, 0, 0);
  if (freq <= 0 || ticks < base) return offset;
  return offset + (unsigned long long)((double)(ticks - base) * (1000000000.0 / (double)freq));
}
int gcet_gc_slow_read(GCETGCSlowEvent *out, int capacity, int *dropped)
{
  int i, seen = (int)InterlockedCompareExchange(&gcet_slow_seen, 0, 0);
  int available = seen > GCET_SLOW_CAPACITY ? GCET_SLOW_CAPACITY : seen;
  if (dropped) *dropped = seen > GCET_SLOW_CAPACITY ? seen - GCET_SLOW_CAPACITY : 0;
  if (!out || capacity <= 0) return 0;
  if (capacity < available) available = capacity;
  for (i = 0; i < available; i++) out[i] = gcet_slow_events[i];
  return available;
}
void gcet_gc_phase_read(GCETGCPhaseStats *out)
{
  int i;
  if (!out) return;
  for (i = 0; i < 6; i++) {
    out->calls[i] = (unsigned long long)InterlockedCompareExchange64(&gcet_phase_calls[i], 0, 0);
    out->ticks[i] = (unsigned long long)InterlockedCompareExchange64(&gcet_phase_ticks[i], 0, 0);
  }
}

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
    int i;
    InterlockedExchange(&gcet_slow_seen, 0);
    for (i = 0; i < 6; i++) {
      InterlockedExchange64(&gcet_phase_calls[i], 0);
      InterlockedExchange64(&gcet_phase_ticks[i], 0);
    }
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
$src = Change-Once $src 'static size_t gc_onestep(lua_State *L)' 'static size_t gcet_original_onestep(lua_State *L)' 'GC state operation'
$phase = @'

/* Time each indivisible internal GC operation. Values are wall-clock
 * intervals, potentially including OS thread descheduling. No GC calls added. */
static size_t gc_onestep(lua_State *L)
{
  global_State *g = G(L);
  unsigned int phase;
  unsigned long long before, threshold, debt;
  LARGE_INTEGER start, end;
  LONG64 ticks, freq;
  size_t cost;
  if (InterlockedCompareExchange(&gcet_gc_on, 0, 0) == 0)
    return gcet_original_onestep(L);
  phase = (unsigned int)g->gc.state;
  before = (unsigned long long)g->gc.total;
  threshold = (unsigned long long)g->gc.threshold;
  debt = (unsigned long long)g->gc.debt;
  QueryPerformanceCounter(&start);
  cost = gcet_original_onestep(L);
  QueryPerformanceCounter(&end);
  ticks = end.QuadPart - start.QuadPart;
  if (ticks < 0) ticks = 0;
  if (phase < 6) {
    InterlockedIncrement64(&gcet_phase_calls[phase]);
    InterlockedAdd64(&gcet_phase_ticks[phase], ticks);
  }
  freq = InterlockedCompareExchange64(&gcet_frequency, 0, 0);
  if (freq > 0 && ticks >= freq / 500) {
    LONG index = InterlockedIncrement(&gcet_slow_seen) - 1;
    if (index >= 0 && index < GCET_SLOW_CAPACITY) {
      GCETGCSlowEvent *e = &gcet_slow_events[index];
      e->startNs = gcet_gc_capture_ns(start.QuadPart);
      e->endNs = gcet_gc_capture_ns(end.QuadPart);
      e->threadId = (unsigned long long)GetCurrentThreadId();
      e->registrationId = gcet_current_registration;
      e->heapBefore = before;
      e->heapAfter = (unsigned long long)g->gc.total;
      e->threshold = threshold;
      e->debt = debt;
      e->phaseBefore = phase;
      e->phaseAfter = (unsigned int)g->gc.state;
      e->pause = (unsigned int)g->gc.pause;
      e->stepmul = (unsigned int)g->gc.stepmul;
    }
  }
  return cost;
}

'@
$src = Change-Once $src '/* Perform a limited amount of incremental GC steps. */' ($phase + '/* Perform a limited amount of incremental GC steps. */') 'internal collector phase wrapper'
$src = Change-Once $src '  g->gc.total = (g->gc.total - osz) + nsz;' ('  gcet_gc_alloc_observe(osz, nsz);' + [Environment]::NewLine + '  g->gc.total = (g->gc.total - osz) + nsz;') 'realloc accounting'
$src = Change-Once $src '  g->gc.total += size;' ('  gcet_gc_alloc_observe(0, size);' + [Environment]::NewLine + '  g->gc.total += size;') 'new GC object accounting'
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
