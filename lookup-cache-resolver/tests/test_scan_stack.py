import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

script=Path(__file__).resolve().parents[1]/"tools"/"scan_stack.py"
spec=importlib.util.spec_from_file_location("lookup_scanner",script)
s=importlib.util.module_from_spec(spec)
spec.loader.exec_module(s)

class LookupEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)/"Cyberpunk 2077"
        (self.root/s.CET).mkdir(parents=True)
        (self.root/s.REDS).mkdir(parents=True)

    def put(self,path,text):
        f=self.root/path
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text(text,encoding="utf-8")
        return f

    def test_cet_phone_is_independent_and_candidate(self):
        self.put(s.CET/"Example/init.lua",'''local site={x=10,y=20,z=30}
Cron.Every(1,function()
  local d=Game.GetPlayer():GetWorldPosition():Distance(site)
end)
Observe("Phone","OnAction",function() incomingPhoneCall() end)''')
        out=s.scan(self.root)
        self.assertEqual(out["source_review_candidates"],1)
        mod=out["owners"][0]
        self.assertEqual(mod["classification"],"SOURCE_REVIEW_SAME_FILE")
        self.assertTrue(mod["independent_wake_present"])
        self.assertFalse(mod["safe_to_patch"])
        self.assertEqual(mod["lane_policy"]["remote_phone_quest_ui"],
                         "PRESERVE_INDEPENDENT_LISTENERS")
        self.assertEqual(mod["lane_policy"]["far_waiting_for_proximity_wake"],
                         "KEEP_NATIVE_ANCHOR_ARMED")
        self.assertEqual(out["generated_anchors"],0)
        self.assertEqual(len(mod["evidence_files"][0]["sha256"]),64)

    def test_redscript_detected(self):
        self.put(s.REDS/"QuestPack/location.reds",'''public func OnUpdate() -> Void {
let p=GameInstance.GetPlayer();
let site=new Vector4(10.0,20.0,30.0,1.0);
let d=Vector4.Distance(p.GetWorldPosition(),site);
}''')
        m=s.scan(self.root)["owners"][0]
        self.assertEqual(m["runtime"],"REDSCRIPT")
        self.assertEqual(m["classification"],"SOURCE_REVIEW_SAME_FILE")

    def test_cross_file_not_automatically_admitted(self):
        self.put(s.CET/"Multi/init.lua","Cron.Every(1,function() Game.GetPlayer() end)")
        self.put(s.CET/"Multi/sites.json",'{"x":10,"y":20,"z":30}')
        self.put(s.CET/"Multi/distance.lua","return Distance(player,site)")
        m=s.scan(self.root)["owners"][0]
        self.assertEqual(m["classification"],"SOURCE_REVIEW_CROSS_FILE")
        self.assertFalse(m["safe_to_patch"])

    def test_dynamic_hint_requires_review(self):
        self.put(s.CET/"Custom/init.lua",'''local a={x=1,y=2,z=3}
Cron.Every(1,function() local d=Game.GetPlayer():GetWorldPosition():Distance(a) end)
function Editor() SetWorldPosition(a) end''')
        m=s.scan(self.root)["owners"][0]
        self.assertTrue(m["mutable_source_hint"])
        self.assertFalse(m["safe_to_patch"])

    def test_deterministic_readonly_and_native_lifecycle(self):
        f=self.put(s.CET/"A/init.lua","Cron.Every(1,function() end)")
        before=f.read_bytes()
        a=s.scan(self.root)
        b=s.scan(self.root)
        self.assertEqual(json.dumps(a,sort_keys=True),json.dumps(b,sort_keys=True))
        self.assertEqual(f.read_bytes(),before)
        self.assertEqual(a["native_lifecycle_contract"]["inactive_location_stage"],
                         "ANCHOR_DISARMED")

    def test_empty_install_refused(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):
                s.scan(Path(root))

if __name__=="__main__":
    unittest.main()
