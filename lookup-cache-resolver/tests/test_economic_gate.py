"""Economic gate: measured work retirement, not source recognition or owner CPU."""
import copy
import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"tools"))
from economic_gate import decide, EconomicRefused, SCHEMA, MIN_NET_MS_PER_SECOND, MIN_RETURN_RATIO

SOURCE="bin/x64/plugins/cyber_engine_tweaks/mods/AnyName/init.lua"
HASH="0123456789abcdef"*4

def evidence(saved=1.4, extra=0.3, shared=0.4):
    def scene(suffix):
        return {"baseline_capture_id":"original-"+suffix,
                "optimized_capture_id":"optimized-"+suffix,
                "baseline_seconds":90.0,
                "optimized_seconds":90.0,
                "retired_far_job_ms_per_second":saved,
                "added_owner_ms_per_second":extra,
                "shared_native_cet_ms_per_second":shared}
    return {"schema":SCHEMA,"source":SOURCE,
            "original_sha256":HASH,
            "attribution":"SOURCE_OWNED_FAR_JOB",
            "shared_cost_scope":"ENTIRE_NATIVE_AND_CET_HOST",
            "scenarios":{"sparse":scene("sparse"),"dense":scene("dense")}}

class EconomicGateTests(unittest.TestCase):
    def check(self,e):
        return decide(e,SOURCE,HASH)
    def reject(self,e,word):
        with self.assertRaisesRegex(EconomicRefused,word):
            self.check(e)
    def test_positive_direct_lane_evidence(self):
        r=self.check(evidence())
        self.assertEqual(r["status"],"ECONOMIC_ADMIT")
        self.assertEqual(r["original_sha256"],HASH)
        self.assertAlmostEqual(r["scenarios"]["dense"]["net_ms_per_second"],0.7)
        self.assertEqual(r["shared_overhead_charged"],"ONCE_IN_FULL_FOR_SINGLE_OWNER")
    def test_no_measurement_and_stale_source(self):
        self.reject(None,"MEASURE_FIRST")
        e=evidence();e["original_sha256"]="x"*64
        self.reject(e,"fingerprint")
        e=evidence();e["source"]="bin/x64/plugins/cyber_engine_tweaks/mods/Other/init.lua"
        self.reject(e,"fingerprint")
    def test_decline_owner_attribution_and_missing_shared_cost(self):
        e=evidence();e["attribution"]="OWNER_EXCLUSIVE_TOTAL"
        self.reject(e,"owner totals")
        e=evidence();e["shared_cost_scope"]="UNKNOWN"
        self.reject(e,"shared native")
        e=evidence();del e["scenarios"]["dense"]
        self.reject(e,"sparse and dense")
    def test_missing_short_and_unpaired_captures(self):
        e=evidence();e["scenarios"]["dense"]["optimized_capture_id"]="original-dense"
        self.reject(e,"paired")
        e=evidence();e["scenarios"]["sparse"]["baseline_seconds"]=20
        self.reject(e,"too short")
        e=evidence();e["scenarios"]["dense"]["shared_native_cet_ms_per_second"]=None
        self.reject(e,"invalid measured")
    def test_invalid_numerics(self):
        for v in (True,-1,float("nan"),float("inf"),"1.0"):
            with self.subTest(value=v):
                e=evidence()
                e["scenarios"]["sparse"]["retired_far_job_ms_per_second"]=v
                self.reject(e,"invalid measured")
    def test_dense_near_work_prevents_admission(self):
        e=evidence()
        e["scenarios"]["dense"]["added_owner_ms_per_second"]=1.0
        self.reject(e,"NO_GAIN: dense")
    def test_minimum_net_absolute_floor(self):
        self.assertGreater(MIN_NET_MS_PER_SECOND,0)
        self.reject(evidence(saved=0.24,extra=0.02,shared=0.10),"net below")
    def test_ratio_margin_rejects_barely_positive(self):
        self.assertGreater(MIN_RETURN_RATIO,1)
        # Net 0.23 ms/s clears floor, but <1.25x replacement cost.
        self.reject(evidence(saved=1.23,extra=0.70,shared=0.30),"1.25x")
    def test_no_shared_cost_amortization_from_unselected_mods(self):
        # The full shared cost must be paid by this one source.
        e=evidence(saved=0.60,extra=0.02,shared=0.42)
        self.reject(e,"NO_GAIN")
        # The same source becomes profitable if the measured total overhead
        # is actually lower, not if invented 'other mod' savings are added.
        e=evidence(saved=0.60,extra=0.02,shared=0.10)
        self.assertEqual(self.check(e)["status"],"ECONOMIC_ADMIT")

if __name__=="__main__":
    unittest.main()
