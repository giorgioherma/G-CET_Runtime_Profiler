"""Fail-closed generic CET job admission and reversible source-staging tests."""
import hashlib
import io
import shutil
import subprocess
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

TOOLS=Path(__file__).resolve().parents[1]/"tools"
sys.path.insert(0,str(TOOLS))
from generic_cancelable_lane import admit,stage_generic
from economic_gate import EconomicRefused, SCHEMA as ECONOMIC_SCHEMA
from stage_source_pass import Refused,stage

PATH="bin/x64/plugins/cyber_engine_tweaks/mods/UnnamedQuest/init.lua"
BASE='''-- unrelated event listeners must survive untouched
registerForEvent("onInit", function()
    remoteQuestFactListener()
end)
local farJob, nearJob
farJob = client.Schedule.Every(1.0, {id="quest.far", active=true}, function()
    local pos = Game.GetPlayer():GetWorldPosition()
    local dx = pos.x - 100
    local dy = pos.y - 200
    local dz = pos.z - 15
    if dx*dx + dy*dy + dz*dz <= 625 then
        nearJob.Resume()
    else
        nearJob.Pause()
    end
end)
nearJob = client.Schedule.Every(0.2, {id="quest.near", active=false}, function()
    originalAuthorExactInteraction()
end)
registerForEvent("onQuestUpdate", function()
    refreshRemoteQuestFact()
end)
'''

class GenericCancelableLaneTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
        target=self.root/PATH
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(BASE,encoding="utf-8")
    def economics(self,source=BASE):
        # Synthetic paired-capture figures for the transformer regression,
        # never claimed to be actual Profiler/game measurements.
        return {"schema":ECONOMIC_SCHEMA,"source":PATH,
                "original_sha256":hashlib.sha256(source.encode()).hexdigest(),
                "attribution":"SOURCE_OWNED_FAR_JOB",
                "shared_cost_scope":"ENTIRE_NATIVE_AND_CET_HOST",
                "scenarios":{
                  "sparse":{"baseline_capture_id":"mock-sparse-original",
                            "optimized_capture_id":"mock-sparse-optimized",
                            "baseline_seconds":60,"optimized_seconds":60,
                            "retired_far_job_ms_per_second":2.0,
                            "added_owner_ms_per_second":0.2,
                            "shared_native_cet_ms_per_second":0.4},
                  "dense":{"baseline_capture_id":"mock-dense-original",
                           "optimized_capture_id":"mock-dense-optimized",
                           "baseline_seconds":60,"optimized_seconds":60,
                           "retired_far_job_ms_per_second":1.4,
                           "added_owner_ms_per_second":0.3,
                           "shared_native_cet_ms_per_second":0.4}}}
    def contract(self,text=BASE):
        return admit(text,PATH,1400123,1400124)
    def source(self,blob):
        with zipfile.ZipFile(io.BytesIO(blob)) as z:
            return z.read(PATH).decode("utf-8")
    def test_proven_job_family_stages_original_partial_optimized_and_restore(self):
        contract=self.contract()
        self.assertTrue(contract["auto_admission"])
        self.assertEqual(contract["admission"],"STRICT_PURE_FAR_JOB_XYZ")
        self.assertEqual(contract["sites"][0]["radius"],25.0)
        status,archive=stage(self.root,contract)
        self.assertEqual(status,"OPTIMIZATION_STAGED")
        optimized=self.source(archive)
        self.assertIn("gcetLocationBinding:attempt()",optimized)
        self.assertIn("NativeBinding.new",optimized)
        self.assertIn("gcetLocationBinding:shutdown()",optimized)
        self.assertIn('registerForEvent("onQuestUpdate"',optimized)
        self.assertIn("originalAuthorExactInteraction()",optimized)
        self.assertIn("if dx*dx + dy*dy + dz*dz <= 625",optimized)
        self.assertEqual((self.root/PATH).read_text(),BASE)
        (self.root/PATH).write_text(optimized)
        status,same=stage(self.root,contract)
        self.assertEqual(status,"ALREADY_OPTIMIZED")
        self.assertEqual(self.source(same),optimized)
        partial=optimized.replace("-- G-LOOKUPCACHE:BEGIN:retry\n"+
            "        if gcetLocationBinding then gcetLocationBinding:attempt() end\n"+
            "-- G-LOOKUPCACHE:END:retry\n","")
        self.assertNotEqual(partial,optimized)
        (self.root/PATH).write_text(partial)
        self.assertEqual(self.source(stage(self.root,contract)[1]),optimized)
        (self.root/PATH).write_text(optimized)
        self.assertEqual(self.source(stage(self.root,contract,True)[1]),BASE)
    def test_no_name_whitelist_and_source_drift_refusal(self):
        another=PATH.replace("UnnamedQuest","IndependentQuest")
        con=admit(BASE,another,1900012,1900013)
        self.assertEqual(con["sites"],[{"id":1900013,"x":100.0,"y":200.0,
                                         "z":15.0,"radius":25.0,"metric":"xyz"}])
        (self.root/PATH).write_text(BASE+"-- modified after admission\n")
        with self.assertRaises(Refused):stage(self.root,self.contract())
    def test_cli_refuses_writes_under_game_root(self):
        cmd=[sys.executable,str(TOOLS/"generic_cancelable_lane.py"),
             "--game",str(self.root),"--source",PATH,
             "--owner-id","1400123","--anchor-id","1400124"]
        output=self.root/"live-overwrite.zip"
        run=subprocess.run(cmd+["--output",str(output)],
                           text=True,capture_output=True)
        self.assertEqual(run.returncode,2)
        self.assertIn("outside selected game root",run.stderr)
        self.assertFalse(output.exists())

    def test_generated_lua_preserves_remote_author_fallback(self):
        lua=shutil.which("lua5.4")
        if not lua:
            return
        compiled=self.source(stage(self.root,self.contract())[1])
        source=self.root/"optimized.lua"
        source.write_text(compiled)
        runner=self.root/"run.lua"
        runner.write_text("""
local events,jobs={},{}
local pos={x=0,y=0,z=0}
local remote,interactions=0,0
Game={GetPlayer=function() return {GetWorldPosition=function() return pos end} end}
client={Schedule={Every=function(_,opts,cb)
    local h={active=opts.active~=false,callback=cb}
    h.Pause=function() h.active=false end
    h.Resume=function() h.active=true end
    jobs[#jobs+1]=h
    return h
end}}
function registerForEvent(n,f)
    events[n]=events[n] or {}
    table.insert(events[n],f)
end
function remoteQuestFactListener() remote=remote+1 end
function refreshRemoteQuestFact() remote=remote+1 end
function originalAuthorExactInteraction() interactions=interactions+1 end
function GetMod() return nil end
dofile(arg[1])
assert(#jobs==2 and jobs[1].active and not jobs[2].active)
events.onInit[1]()
events.onQuestUpdate[1]()
assert(remote==2)
jobs[1].callback()
assert(not jobs[2].active)
pos.x,pos.y,pos.z=100,200,15
jobs[1].callback()
assert(jobs[2].active)
jobs[2].callback()
assert(interactions==1)
pos.x=0
jobs[1].callback()
assert(not jobs[2].active)
events.onQuestUpdate[1]()
assert(remote==3)
events.onShutdown[1]()
print("PASS source runtime remote listener and fallback")
""")
        result=subprocess.run([lua,str(runner),str(source)],
                              text=True,capture_output=True)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)

    def test_same_pattern_different_coordinates_and_radius(self):
        moved=(BASE.replace("pos.x - 100","pos.x - -325.5")
                   .replace("pos.y - 200","pos.y - 27")
                   .replace("pos.z - 15","pos.z - 101")
                   .replace("<= 625","<= 1600"))
        proof=self.contract(moved)
        self.assertEqual(proof["sites"][0]["x"],-325.5)
        self.assertEqual(proof["sites"][0]["y"],27.0)
        self.assertEqual(proof["sites"][0]["radius"],40.0)
        self.assertNotEqual(proof["original_sha256"],self.contract()["original_sha256"])

    def test_never_suspend_mixed_callback_or_non_cancelable_cron(self):
        for modified in (
            BASE.replace("nearJob.Resume()","nearJob.Resume()\n        advanceQuest()"),
            BASE.replace("nearJob.Pause()","nearJob.Pause()\n        resetOtherSystems()"),
            BASE.replace("client.Schedule.Every","Cron.Every",1),
            BASE.replace("local farJob, nearJob", "local farJob, otherJob"),
            BASE.replace("local farJob, nearJob", ""),
            BASE.replace("local dx = pos.x - 100","local dx = pos.x - movingTarget.x"),
            BASE.replace("625","distanceThreshold"),
            BASE.replace("625","625 or remoteQuestFact()"),
            BASE.replace('originalAuthorExactInteraction()',
                         'nearJob.Resume()\n    originalAuthorExactInteraction()'),
            BASE.replace('registerForEvent("onQuestUpdate", function()\n    refreshRemoteQuestFact()',
                         'registerForEvent("onQuestUpdate", function()\n    nearJob.Resume()\n    refreshRemoteQuestFact()'),
        ):
            with self.subTest(modified=modified[50:120]):
                with self.assertRaises(Refused):self.contract(modified)
    def test_reject_second_source_location_or_ambiguous_duplicate_job(self):
        extra=BASE.replace("local dy = pos.y - 200",
                           "local dy = pos.y - 200\n    local secondSite = {x=900,y=800,z=700}")
        with self.assertRaises(Refused):self.contract(extra)
        with self.assertRaises(Refused):self.contract(BASE+BASE)
        with self.assertRaises(Refused):self.contract(
            BASE.replace("active=false","active=true"))
    def test_economic_gate_refuses_unmeasured_generic_staging(self):
        with self.assertRaisesRegex(EconomicRefused,"MEASURE_FIRST"):
            stage_generic(self.root,PATH,1400123,1400124)
        self.assertEqual((self.root/PATH).read_text(),BASE)
        self.assertFalse((self.root/"not-generated.zip").exists())

    def test_economic_gate_catches_source_and_dense_cost(self):
        stale=self.economics()
        stale["original_sha256"]="0"*64
        with self.assertRaisesRegex(EconomicRefused,"fingerprint"):
            stage_generic(self.root,PATH,1400123,1400124,stale)
        expensive=self.economics()
        expensive["scenarios"]["dense"]["added_owner_ms_per_second"]=1.25
        with self.assertRaisesRegex(EconomicRefused,"NO_GAIN"):
            stage_generic(self.root,PATH,1400123,1400124,expensive)
        self.assertEqual((self.root/PATH).read_text(),BASE)

    def test_full_generic_payload_and_managed_source(self):
        (status,blob),contract=stage_generic(self.root,PATH,1400123,1400124,self.economics())
        self.assertEqual(status,"OPTIMIZATION_STAGED")
        self.assertEqual(contract["sites"][0]["radius"],25.0)
        with zipfile.ZipFile(io.BytesIO(blob)) as z:
            names=set(z.namelist())
            prefix="bin/x64/plugins/cyber_engine_tweaks/mods/"
            self.assertTrue(all(n.startswith(prefix) for n in names))
            self.assertIn(prefix+"UnnamedQuest/GCETLookupWakeBinding.lua",names)
            self.assertIn(prefix+"G-LookupCache/modules/CETNativeWakeHost.lua",names)
            self.assertIn(prefix+"G-LookupCache/ECONOMIC-DECISION.json",names)
            patched=z.read(PATH).decode()
        luac=shutil.which("luac5.4")
        if luac:
            generated=self.root/"generated.lua"
            generated.write_text(patched)
            proc=subprocess.run([luac,"-p",str(generated)],
                                capture_output=True,text=True)
            self.assertEqual(proc.returncode,0,proc.stderr)
        (self.root/PATH).write_text(patched)
        (status,blob2),_=stage_generic(self.root,PATH,1400123,1400124,self.economics())
        self.assertEqual(status,"ALREADY_OPTIMIZED")
        with zipfile.ZipFile(io.BytesIO(blob2)) as z:
            self.assertEqual(z.read(PATH).decode(),patched)
        (self.root/PATH).write_text(patched.replace(
            "G-LOOKUPCACHE:BEGIN:retry",
            "G-LOOKUPCACHE:BEGIN:tampered",1))
        with self.assertRaises(Refused):
            stage_generic(self.root,PATH,1400123,1400124,self.economics())
        (self.root/PATH).write_text(patched.replace("NativeBinding.new", "ForeignBinding.new",1))
        with self.assertRaises(Refused):
            stage_generic(self.root,PATH,1400123,1400124,self.economics())
        (self.root/PATH).write_text(BASE)
        target=self.root/"bin/x64/plugins/cyber_engine_tweaks/mods/G-LookupCache/init.lua"
        target.parent.mkdir(parents=True)
        target.write_text("-- foreign runtime")
        with self.assertRaises(Refused):
            stage_generic(self.root,PATH,1400123,1400124,self.economics())

    def test_independent_remote_listener_remains_even_when_far(self):
        contract=self.contract()
        self.assertEqual(contract["proof"]["independent_listeners_preserved"],True)
        produced=self.source(stage(self.root,contract)[1])
        before=BASE.split('registerForEvent("onQuestUpdate"',1)[1]
        after=produced.split('registerForEvent("onQuestUpdate"',1)[1]
        self.assertIn("refreshRemoteQuestFact()",after)
        self.assertEqual(after,before)

if __name__=="__main__":
    unittest.main()
