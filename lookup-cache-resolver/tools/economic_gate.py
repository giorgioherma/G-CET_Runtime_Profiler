"""Fail-closed economic admission for one generated G-LookupCache location lane.

This is NOT the coarse workload-prioritization gate. It requires direct,
source-pinned before/after lane measurements and charges the shared native+CET
service to this lane in full. Other candidates cannot subsidize independent
staging. A future atomic multi-owner composer may charge that overhead once
for the *actually selected and installed* group, never once per owner.
"""
from __future__ import annotations

import math

SCHEMA="g-lookupcache-economic-evidence-v1"
MIN_NET_MS_PER_SECOND=0.20
MIN_RETURN_RATIO=1.25
MIN_CAPTURE_SECONDS=30.0
SCENARIOS=("sparse","dense")


class EconomicRefused(ValueError):
    pass


def nonnegative(row, key):
    value=row.get(key)
    if type(value) not in (int,float) or not math.isfinite(value) or value<0:
        raise EconomicRefused("missing or invalid measured "+key)
    return float(value)


def decide(evidence:dict|None, source:str, source_sha256:str)->dict:
    """Return a positive verdict or refuse, without staging an overlay.

    The evidence is a separate instrumentation product, not arbitrary
    owner-exclusive CPU or inferred saved Lua allocation. Its provenance and
    the real-game measurements must be verified upstream; this function
    validates shape/fingerprints/cost math but cannot authenticate captures.
    """
    if not isinstance(evidence,dict):
        raise EconomicRefused("MEASURE_FIRST: no lane-level economic evidence")
    if evidence.get("schema")!=SCHEMA:
        raise EconomicRefused("MEASURE_FIRST: unknown economic evidence schema")
    if (evidence.get("source")!=source or
        evidence.get("original_sha256")!=source_sha256):
        raise EconomicRefused("MEASURE_FIRST: source fingerprint mismatch")
    if evidence.get("attribution")!="SOURCE_OWNED_FAR_JOB":
        raise EconomicRefused("MEASURE_FIRST: owner totals are not retired-lane costs")
    if evidence.get("shared_cost_scope")!="ENTIRE_NATIVE_AND_CET_HOST":
        raise EconomicRefused("MEASURE_FIRST: shared native/CET cost not covered")
    scenarios=evidence.get("scenarios")
    if not isinstance(scenarios,dict) or set(scenarios)!=set(SCENARIOS):
        raise EconomicRefused("MEASURE_FIRST: sparse and dense evidence required")
    results={}
    for name in SCENARIOS:
        row=scenarios[name]
        if not isinstance(row,dict):
            raise EconomicRefused("MEASURE_FIRST: invalid "+name+" measurement")
        a,b=row.get("baseline_capture_id"),row.get("optimized_capture_id")
        if (not isinstance(a,str) or not a.strip() or
            not isinstance(b,str) or not b.strip() or a==b):
            raise EconomicRefused("MEASURE_FIRST: paired capture IDs required")
        duration_a=nonnegative(row,"baseline_seconds")
        duration_b=nonnegative(row,"optimized_seconds")
        if min(duration_a,duration_b)<MIN_CAPTURE_SECONDS:
            raise EconomicRefused("MEASURE_FIRST: captures too short")
        # Direct, non-overlapping work: cost removed from original FAR job,
        # marginal optimized owner work (including extra near/cleanup), and
        # the full fixed shared service with player sampling and CET hosting.
        saved=nonnegative(row,"retired_far_job_ms_per_second")
        extra=nonnegative(row,"added_owner_ms_per_second")
        shared=nonnegative(row,"shared_native_cet_ms_per_second")
        cost=extra+shared
        net=saved-cost
        results[name]={
            "retired_ms_per_second":saved,
            "replacement_ms_per_second":cost,
            "net_ms_per_second":net,
            "return_ratio":(saved/cost if cost>0 else None),
            "baseline_capture_id":a,
            "optimized_capture_id":b,
        }
        if net<MIN_NET_MS_PER_SECOND:
            raise EconomicRefused(
                "NO_GAIN: "+name+" net below %.2f ms/s minimum" %
                MIN_NET_MS_PER_SECOND)
        if saved < MIN_RETURN_RATIO*cost:
            raise EconomicRefused(
                "NO_GAIN: "+name+" saved work below %.2fx replacement cost" %
                MIN_RETURN_RATIO)
    return {
        "status":"ECONOMIC_ADMIT",
        "source":source,
        "original_sha256":source_sha256,
        "scenarios":results,
        "min_net_ms_per_second":MIN_NET_MS_PER_SECOND,
        "min_return_ratio":MIN_RETURN_RATIO,
        "shared_overhead_charged":"ONCE_IN_FULL_FOR_SINGLE_OWNER",
        "source":"SOURCE_PINNED",
        "not_gameplay_verified":True,
    }
