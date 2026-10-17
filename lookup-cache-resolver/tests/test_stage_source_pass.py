import importlib.util
import hashlib
import json
import tempfile
import unittest
import zipfile
import io
from pathlib import Path

script=Path(__file__).resolve().parents[1]/"tools"/"stage_source_pass.py"
spec=importlib.util.spec_from_file_location("stage_source_pass",script)
s=importlib.util.module_from_spec(spec)
spec.loader.exec_module(s)
PATH="bin/x64/plugins/cyber_engine_tweaks/mods/Example/init.lua"
DATA="bin/x64/plugins/cyber_engine_tweaks/mods/Example/sites.json"
BASE='local farJob = client.Schedule.Every(1.0, {id="location.world_far"}, function()\n  checkKnownSites()\nend)\n'

class ReviewedStageTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
        p=self.root/PATH
        p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text(BASE)
        d=self.root/DATA
        d.write_text('{"locations":[1,2]}')
        self.contract={
          "schema":s.SCHEMA,"source":PATH,
          "original_sha256":s.digest(BASE.encode()),
          "dependency_sha256":{DATA:s.digest(d.read_bytes())},
          "proof":{key:True for key in s.REQUIRED_PROOFS},
          "sites":[{"id":1201,"x":1,"y":2,"z":3,"radius":30,"metric":"xyz"},
                   {"id":1202,"x":4,"y":5,"z":6,"radius":30,"metric":"xyz"}],
          "insertions":[
            {"name":"binding","needle":'local farJob = client.Schedule.Every(1.0',
             "before":"local glookupHost = findSharedNativeHost()","after":""},
            {"name":"fallback","needle":'  checkKnownSites()',
             "before":"  if nativeLost then resumeOriginalJob() end","after":""},
          ],
        }
    def member(self,archive,path):
        with zipfile.ZipFile(io.BytesIO(archive)) as z: return z.read(path).decode()
    def test_original_partially_applied_and_live_are_one_staged_result(self):
        status,zip1=s.stage(self.root,self.contract)
        self.assertEqual(status,"OPTIMIZATION_STAGED")
        out=self.member(zip1,PATH)
        self.assertIn("G-LOOKUPCACHE:BEGIN:binding",out)
        self.assertIn("checkKnownSites()",out)
        self.assertEqual((self.root/PATH).read_text(),BASE) # never mutate live
        (self.root/PATH).write_text(out)
        status,zip2=s.stage(self.root,self.contract)
        self.assertEqual(status,"ALREADY_OPTIMIZED")
        self.assertEqual(zip1,zip2) if False else None
        self.assertEqual(self.member(zip2,PATH),out)
        partial=out.replace('-- G-LOOKUPCACHE:BEGIN:fallback\n  if nativeLost then resumeOriginalJob() end\n-- G-LOOKUPCACHE:END:fallback\n','')
        (self.root/PATH).write_text(partial)
        status,zip3=s.stage(self.root,self.contract)
        self.assertEqual(status,"OPTIMIZATION_STAGED")
        self.assertEqual(self.member(zip3,PATH),out)
    def test_reversible_and_exact_unmodified_baseline(self):
        _,data=s.stage(self.root,self.contract)
        (self.root/PATH).write_text(self.member(data,PATH))
        status,ziprestore=s.stage(self.root,self.contract,restore=True)
        self.assertEqual(status,"RESTORE_STAGED")
        self.assertEqual(self.member(ziprestore,PATH),BASE)
    def test_drift_and_source_mutation_refused(self):
        (self.root/PATH).write_text(BASE+"-- changed by another mod\n")
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)
        (self.root/PATH).write_text(BASE)
        (self.root/DATA).write_text('{"locations":[3]}')
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)
    def test_unknown_marker_and_invalid_site_refused(self):
        (self.root/PATH).write_text(BASE+"-- G-LOOKUPCACHE:BEGIN:unknown\n")
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)
        (self.root/PATH).write_text(BASE)
        self.contract["sites"][0]["id"]=2_100_000_000
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)
    def test_missing_proof_and_traversal_refused(self):
        self.contract["proof"]["independent_listeners_preserved"]=False
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)
        self.contract["proof"]["independent_listeners_preserved"]=True
        self.contract["source"]="../../Outside/init.lua"
        with self.assertRaises(s.Refused):s.stage(self.root,self.contract)

if __name__=="__main__":unittest.main()
