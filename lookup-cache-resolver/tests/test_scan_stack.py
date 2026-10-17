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

    def test_static_vehicle_garage_position_is_not_player_equivalence(self):
        self.put(s.CET/"AnyGarage/init.lua",'''local sites = {
 { key="first", pos = {
    x = -788.028,
    y = 943.054,
    z = 12.199,
 }, radius = 10.0 }
}
registerForEvent("onUpdate", function()
 local vehicle=GetMountedVehicleSafe()
 if vehicle and GetActiveGarageForVehiclePosition(vehicle) then
   ProcessRepairState()
 end
 local p = Game.GetPlayer()
end)
local d=DistanceBetween(vehiclePos, garagePos)
''')
        report=s.scan(self.root)
        mod=report["owners"][0]
        self.assertEqual(mod["classification"],"SOURCE_REVIEW_SAME_FILE")
        self.assertTrue(mod["position_sources_detected"]["vehicle_or_world_transform"])
        self.assertTrue(mod["position_sources_detected"]["player"])
        self.assertFalse(mod["safe_to_patch"])

    def test_external_map_epoch_and_nearby_package_streaming(self):
        self.put(s.CET/"Pack/init.lua",'''local p=Game.GetPlayer():GetWorldPosition()
Cron.Every(1, function()
 if math.abs(p.x-target.x)<=100 and DistanceSquared(p,target)<=100 then end
end)''')
        self.put(s.CET/"Pack/Maps/current.map",
                 "# generated package map\n-123.4 55.2 7.5 1\n1 2 3\n")
        mod=s.scan(self.root)["owners"][0]
        self.assertEqual(mod["classification"],"SOURCE_REVIEW_CROSS_FILE")
        self.assertEqual(mod["evidence_counts"]["literal_xyz"],2)
        self.assertEqual(len(mod["author_data_assets"]),1)
        self.assertTrue(mod["author_data_assets"][0].endswith("current.map"))
        self.assertFalse(mod["safe_to_patch"])

    def test_fixed_start_with_manual_squared_distance(self):
        self.put(s.CET/"Start/init.lua",'''registerForEvent("onUpdate",function(dt) Cron.Update(dt) end)
local function start()
 Cron.Every(1.0,function()
  local playerPos=Game.GetPlayer():GetWorldPosition()
  local distSq84=(playerPos.x + 1496.32)^2 + (playerPos.y - 2201.52)^2
  if distSq84<=1.0 then activate() end
 end)
end
local site={x=-1496.32,y=2201.52,z=18.19}
''')
        mod=s.scan(self.root)["owners"][0]
        self.assertTrue(mod["source_review_candidate"])
        self.assertEqual(mod["classification"],"SOURCE_REVIEW_SAME_FILE")
        self.assertFalse(mod["safe_to_patch"])

    def test_empty_install_refused(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):
                s.scan(Path(root))

if __name__=="__main__":
    unittest.main()
