import importlib.util
from pathlib import Path
import unittest
script=Path(__file__).resolve().parents[1]/"tools"/"reviewed_streetgames_pilot.py"
import sys
sys.path.insert(0,str(script.parent))
spec=importlib.util.spec_from_file_location("reviewed_streetgames_pilot",script)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class ReviewedPilotTests(unittest.TestCase):
 def test_complete_roster_and_static_epoch(self):
  rows=[]
  for i in range(40):
   rows.append("""{
 id='site_%d',
 center={ x=%d, y=%d },
 floorZ=%d,
 },"""%(i,i,i+1,i+2))
  text="StreetTables.locations = {\n"+"\n".join(rows)+"\n}\n"
  found=m.get_sites(text)
  self.assertEqual(len(found),40)
  self.assertEqual(len({x["id"] for x in found}),40)
  self.assertEqual(found[0]["metric"],"xyz")
  with self.assertRaises(Exception):
   m.get_sites(text+"table.insert(StreetTables.locations, site)\n")
  with self.assertRaises(Exception):
   m.get_sites(text.replace("site_20","site_1"))
 def test_bundle_contains_one_shared_host_and_refuses_collision(self):
  import io,zipfile,tempfile
  from pathlib import Path
  with tempfile.TemporaryDirectory() as td:
   root=Path(td)
   base=io.BytesIO()
   with zipfile.ZipFile(base,"w") as z:
    z.writestr("G-LookupCache/REVIEWED-STAGE.json","{}")
   blob=m.bundle_runtime(root,base.getvalue())
   with zipfile.ZipFile(io.BytesIO(blob)) as z:
    names=set(z.namelist())
    self.assertIn("bin/x64/plugins/cyber_engine_tweaks/mods/G-LookupCache/init.lua",names)
    self.assertIn("bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/GCETLookupWakeBinding.lua",names)
    self.assertIn("bin/x64/plugins/cyber_engine_tweaks/mods/G-LookupCache/modules/CETNativeWakeHost.lua",names)
    self.assertIn("G-LookupCache/EXPERIMENTAL-NATIVE-DEPENDENCY.txt",names)
    self.assertEqual(len(names),10)
   target=root/"bin/x64/plugins/cyber_engine_tweaks/mods/G-LookupCache/init.lua"
   target.parent.mkdir(parents=True)
   target.write_text("foreign")
   with self.assertRaises(Exception):m.bundle_runtime(root,base.getvalue())
 def test_template_is_source_pinned_and_has_cleanup_lane(self):
  from stage_source_pass import REQUIRED_PROOFS
  self.assertIn("original_wake_side_effects_reviewed",REQUIRED_PROOFS)
  self.assertEqual(len(m.SOURCE_HASH),64)
  self.assertEqual(len(m.DATA_HASH),64)
  self.assertLess(m.FIRST_SITE_ID+40,2_100_000_000)

if __name__=="__main__":unittest.main()
