"""Fail-closed generic CET job admission and reversible source-staging tests."""
import hashlib
import io
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

TOOLS=Path(__file__).resolve().parents[1]/"tools"
sys.path.insert(0,str(TOOLS))
from generic_cancelable_lane import admit
from stage_source_pass import Refused,stage

PATH="bin/x64/plugins/cyber_engine_tweaks/mods/UnnamedQuest/init.lua"
BASE='''-- unrelated event listeners must survive untouched
registerForEvent("onInit", function()
    remoteQuestFactListener()
end)
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
    def test_never_suspend_mixed_callback_or_non_cancelable_cron(self):
        for modified in (
            BASE.replace("nearJob.Resume()","nearJob.Resume()\n        advanceQuest()"),
            BASE.replace("nearJob.Pause()","nearJob.Pause()\n        resetOtherSystems()"),
            BASE.replace("client.Schedule.Every","Cron.Every",1),
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
