#!/usr/bin/env python3
"""Read-only native 2.15 deep producer/callsite analysis.

Does not instrument a game, authorize cache reuse, or change Resolver behavior.
Usage: python tools/deep_reuse_candidates.py CAPTURE.zip --output candidates.json
"""
import argparse
import csv
import io
import json
import zipfile
from collections import defaultdict


def read_csv(archive, suffix):
    matches = [n for n in archive.namelist() if n.endswith(suffix)]
    if len(matches) != 1:
        raise ValueError(f"Expected one {suffix}, found {len(matches)}")
    with archive.open(matches[0]) as stream:
        return list(csv.DictReader(io.TextIOWrapper(stream, encoding="utf-8-sig", newline="")))


def integer(row, key):
    return int(row.get(key) or 0)


def analyze(archive):
    functions = read_csv(archive, "CET_Runtime_Profile_Deep_Functions.csv")
    edges = read_csv(archive, "CET_Runtime_Profile_Deep_Callsites.csv")
    registrations = read_csv(archive, "CET_Runtime_Profile_Deep_Registrations.csv")
    owners = {}
    for row in registrations:
        rid = integer(row, "RegistrationId")
        name = row.get("Mod") or row.get("ModName") or row.get("Owner") or "UNRESOLVED"
        if rid in owners and owners[rid] != name:
            raise ValueError(f"Ambiguous registration identity {rid}")
        owners[rid] = name

    by_key = {}
    for row in functions:
        key = (integer(row, "RegistrationId"), integer(row, "ProfileEpoch"), row["FunctionKey"])
        if key in by_key:
            raise ValueError(f"Duplicate function key: {key}")
        by_key[key] = row

    groups = defaultdict(lambda: {"calls": 0, "inclusiveMs": 0., "frames": set(), "invocations": set()})
    unresolved = 0
    for row in edges:
        key = (integer(row, "RegistrationId"), integer(row, "ProfileEpoch"), row["ChildFunctionKey"])
        if key not in by_key:
            unresolved += 1
            continue
        caller = (row.get("CallerSourceFile") or "").replace("\\", "/")
        site = (caller, integer(row, "CallerLine"), row.get("ParentFunctionKey") or "")
        bucket = groups[(key, site)]
        bucket["calls"] += integer(row, "Calls")
        bucket["inclusiveMs"] += float(row.get("ChildInclusiveMs") or 0)
        bucket["frames"].add(integer(row, "Frame"))
        bucket["invocations"].add((integer(row, "Frame"), integer(row, "FrameInvocationOrdinal")))

    candidates = []
    for key, function in by_key.items():
        sites = []
        for (fk, (file, line, parent)), stats in groups.items():
            if fk != key:
                continue
            sites.append({
                "file": file, "line": line, "parentFunctionKey": parent,
                "sampledEdgeCalls": stats["calls"],
                "sampledChildInclusiveMs": round(stats["inclusiveMs"], 6),
                "sampledDistinctFrames": len(stats["frames"]),
                "sampledInvocationOrdinals": len(stats["invocations"]),
                "resultEquivalence": "NOT_MEASURED",
                "mutationAndEscape": "NOT_PROVEN",
                "freshnessAndInvalidation": "NOT_PROVEN",
                "cacheReuseAuthorized": False,
                "attributedOwnAllocationBytes": None,
            })
        if not sites:
            continue
        sites.sort(key=lambda s: (-s["sampledEdgeCalls"], s["file"], s["line"], s["parentFunctionKey"]))
        candidates.append({
            "registrationId": key[0], "profileEpoch": key[1], "functionKey": key[2],
            "mod": owners.get(key[0], "UNRESOLVED"),
            "function": function.get("FunctionName") or "",
            "sourceFile": function.get("SourceFile") or "",
            "sampledFunctionCalls": integer(function, "Calls"),
            "sampledOwnAllocatedBytesOnce": integer(function, "GGCTracerSampledOwnAllocatedBytes"),
            "sampledOwnAllocationOpsOnce": integer(function, "GGCTracerSampledOwnAllocations"),
            "callerSites": sites,
            "distinctCallerSites": len(sites),
            "sharedResultContent": "NOT_MEASURED",
            "inputParameters": "NOT_MEASURED",
            "mutationAndAliasing": "NOT_PROVEN",
            "invalidations": "NOT_PROVEN",
            "recommendedReuseClass": "INVESTIGATE_ONLY",
            "potentialSavingsBytes": None,
        })
    candidates.sort(key=lambda c: (-c["sampledOwnAllocatedBytesOnce"],
                                   -c["sampledOwnAllocationOpsOnce"],
                                   -sum(x["sampledEdgeCalls"] for x in c["callerSites"]),
                                   c["mod"], c["functionKey"], c["profileEpoch"]))
    return {
        "schema": "deep-data-reuse-review-v1",
        "nativeCompatibility": "2.15",
        "measurement": "BOUNDED_DEEP_SAMPLE_ONLY",
        "automaticRewriteAuthorized": False,
        "resultValueChangeFrequencyMeasured": False,
        "totalFunctionRows": len(functions),
        "totalCallsiteRows": len(edges),
        "unmatchedCallsiteRows": unresolved,
        "matchedCallsiteRows": len(edges) - unresolved,
        "functionOwnAllocationsCountedOncePerRegistrationEpochKey": True,
        "ownerZeroAttribution": "NOT_MEASURED",
        "candidates": candidates,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", help="Profiler capture ZIP including Deep CSV files")
    parser.add_argument("--output", required=True, help="Output review JSON path")
    args = parser.parse_args()
    with zipfile.ZipFile(args.capture) as capture:
        result = analyze(capture)
    with open(args.output, "w", encoding="utf-8") as out:
        json.dump(result, out, indent=2, ensure_ascii=False)
    print(f"Matched {result['matchedCallsiteRows']}/{result['totalCallsiteRows']} callsite rows; "
          f"{len(result['candidates'])} sampled functions with caller evidence. "
          "No reuse automatically authorized.")


if __name__ == "__main__":
    main()
