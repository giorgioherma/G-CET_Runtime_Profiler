"""Read-only coarse investigation gate using the existing G-CET Resolver input.

The gate orders source review; it NEVER admits or vetoes a source-proven rewrite.
Owner-zero native marshalling and collector wall time cannot be assigned to mods.
"""
from __future__ import annotations

MIB=1024*1024
CPU_FLOOR_MS_S=0.20
ALLOC_FLOOR_MIB_S=0.01
OPERATIONS_FLOOR_S=30.0
SCHEDULER_FLOOR_MS_S=0.20
SPIKE_FLOOR_MS=5.0

def _num(value, field):
    if not isinstance(value,(int,float)) or isinstance(value,bool) or not 0<=value<float("inf"):
        raise ValueError("invalid profiler numeric field: "+field)
    return float(value)

def _key(value):
    return value.casefold() if isinstance(value,str) else ""

def apply_workload_gate(report:dict, profiler:dict)->dict:
    """Annotate (never remove) source candidates with observed workload evidence."""
    if not isinstance(profiler,dict) or not isinstance(profiler.get("owners"),list):
        raise ValueError("expected CET_Resolver_Input.json with owners array")
    duration=_num(profiler.get("capture",{}).get("durationSeconds"),"capture.durationSeconds")
    if duration<=0:
        raise ValueError("profiler duration must be positive")
    owners={}
    for row in profiler["owners"]:
        if isinstance(row,dict) and _key(row.get("owner")):
            owners[_key(row["owner"])]=row
    pressure=profiler.get("callbackAllocatorPressure") or {}
    allocations={}
    for row in pressure.get("byMod",[]):
        if isinstance(row,dict) and _key(row.get("owner")):
            allocations[_key(row["owner"])]=row
    gc={}
    for row in (profiler.get("gcTracer") or {}).get("rankings",[]):
        if isinstance(row,dict) and _key(row.get("Mod")):
            gc[_key(row["Mod"])]=row
    jobs={}
    for row in (profiler.get("scheduler") or {}).get("jobs",[]):
        if isinstance(row,dict) and _key(row.get("owner")):
            k=_key(row["owner"])
            jobs[k]=jobs.get(k,0.0)+_num(row.get("msPerSecond"),"scheduler.msPerSecond")
    statuses={}
    for candidate in report["owners"]:
        key=_key(candidate["owner"].split(":",1)[-1])
        owner=owners.get(key)
        if not candidate["source_review_candidate"]:
            status="NO_SOURCE_WAKE_CANDIDATE"
        elif owner is None:
            status="UNOBSERVED_IN_CAPTURE"
        else:
            cpu=_num(owner.get("exclusiveMsPerSecond"),"owners.exclusiveMsPerSecond")
            spike=_num(owner.get("maxExclusiveMs"),"owners.maxExclusiveMs")
            a=allocations.get(key)
            g=gc.get(key)
            # Compare alternative attribution views; never SUM overlapping bytes.
            rates=[]
            ops=[]
            if a is not None:
                rates.append(_num(a.get("positiveBytes"),"allocator.positiveBytes")/duration/MIB)
                ops.append(_num(a.get("allocationOperations"),"allocator.allocationOperations")/duration)
            if g is not None:
                rates.append(_num(g.get("AllocatedMiBPerSecond"),"gc.AllocatedMiBPerSecond"))
                ops.append(_num(g.get("AllocationsPerSecond"),"gc.AllocationsPerSecond"))
            allocation_rate=max(rates) if rates else None
            operation_rate=max(ops) if ops else None
            scheduler_ms_s=jobs.get(key,0.0)
            if (cpu>=CPU_FLOOR_MS_S or spike>=SPIKE_FLOOR_MS or
                scheduler_ms_s>=SCHEDULER_FLOOR_MS_S or
                (allocation_rate is not None and allocation_rate>=ALLOC_FLOOR_MIB_S) or
                (operation_rate is not None and operation_rate>=OPERATIONS_FLOOR_S)):
                status="REVIEW_MEASURED_FOOTPRINT"
            elif allocation_rate is None or operation_rate is None:
                status="UNKNOWN_ALLOCATION_COVERAGE"
            else:
                status="DEFER_LOW_CAPTURED_FOOTPRINT"
        metrics={"owner_exclusive_ms_per_second":None,
                 "owner_max_exclusive_ms":None,
                 "scoped_alloc_mib_per_second":None,
                 "scoped_alloc_operations_per_second":None,
                 "scheduler_nested_ms_per_second":None,
                 "shared_native_marshalling_owner":"NOT_ATTRIBUTABLE"}
        if owner is not None:
            metrics.update({
                "owner_exclusive_ms_per_second":cpu,
                "owner_max_exclusive_ms":spike,
                "scoped_alloc_mib_per_second":allocation_rate,
                "scoped_alloc_operations_per_second":operation_rate,
                "scheduler_nested_ms_per_second":scheduler_ms_s,
            })
        candidate["workload_gate"]={
            "status":status,
            "metrics":metrics,
            "capture_title":str((profiler.get("capture") or {}).get("title","")),
            "investigation_only":True,
            "never_veto_existing_source_proven_semantic":True,
            "scheduler_metrics_nested_not_additive":True,
        }
        statuses[status]=statuses.get(status,0)+1
    report["workload_gate_counts"]=statuses
    return report
