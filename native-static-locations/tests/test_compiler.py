import sys, unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from compile_locations import compile_data

class ManifestTests(unittest.TestCase):
    def test_valid(self):
        data=compile_data({'schema':1,'anchors':[{'id':1,'owner':4,'x':10,'y':20,'z':30,'radius':25},{'id':2,'owner':7,'x':-10,'y':0,'z':1,'radius':142,'metric':'xy'}]})
        self.assertEqual(len(data),72)
        self.assertEqual(data[:4],b'GCAS')
    def test_reject_duplicates(self):
        with self.assertRaises(ValueError):compile_data({'schema':1,'anchors':[{'id':1,'owner':1,'x':1,'y':1,'z':1,'radius':10}]*2})
    def test_reject_invalid(self):
        for x in [float('nan'),float('inf'),1e10]:
            with self.assertRaises(ValueError):compile_data({'schema':1,'anchors':[{'id':1,'owner':1,'x':x,'y':0,'z':0,'radius':1}]})
    def test_reject_dynamic(self):
        for invalid in ['not a point',{},None]:
            with self.assertRaises(ValueError):compile_data({'schema':1,'anchors':[invalid]})

if __name__=='__main__':unittest.main()
