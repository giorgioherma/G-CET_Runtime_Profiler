import csv
import io
import unittest
import zipfile

from tools.deep_reuse_candidates import analyze


class DeepReuseContract(unittest.TestCase):
    def capture(self, functions, callsites, registrations):
        memory = io.BytesIO()
        with zipfile.ZipFile(memory, "w") as z:
            for suffix, rows in [
                ("Deep_Functions.csv", functions),
                ("Deep_Callsites.csv", callsites),
                ("Deep_Registrations.csv", registrations),
            ]:
                out = io.StringIO()
                writer = csv.DictWriter(out, fieldnames=list(rows[0]))
                writer.writeheader()
                writer.writerows(rows)
                z.writestr("CET_Runtime_Profile_" + suffix, out.getvalue())
        memory.seek(0)
        return zipfile.ZipFile(memory)

    def test_multi_caller_cost_is_not_duplicated(self):
        f = [{
            "RegistrationId": "4", "ProfileEpoch": "2", "FunctionKey": "fn",
            "FunctionName": "makeTable", "SourceFile": "mods/x/init.lua",
            "Calls": "3", "GGCTracerSampledOwnAllocatedBytes": "900",
            "GGCTracerSampledOwnAllocations": "6",
        }]
        edge = [{
            "RegistrationId": "4", "ProfileEpoch": "2",
            "ChildFunctionKey": "fn", "ParentFunctionKey": "p",
            "CallerSourceFile": "mods/x/other.lua", "CallerLine": str(line),
            "Calls": "1", "ChildInclusiveMs": "0.1",
            "Frame": str(line), "FrameInvocationOrdinal": "1",
        } for line in (10, 20, 30)]
        reg = [{"RegistrationId": "4", "Mod": "Example", "ProfileEpoch": "9"}]
        result = analyze(self.capture(f, edge, reg))
        self.assertEqual(result["matchedCallsiteRows"], 3)
        c = result["candidates"][0]
        self.assertEqual(c["mod"], "Example")  # metadata epoch intentionally differs
        self.assertEqual(c["sampledOwnAllocatedBytesOnce"], 900)
        self.assertEqual(len(c["callerSites"]), 3)
        self.assertTrue(all(x["attributedOwnAllocationBytes"] is None for x in c["callerSites"]))
        self.assertFalse(result["automaticRewriteAuthorized"])
        self.assertIsNone(c["potentialSavingsBytes"])

    def test_epoch_scoped_join_and_unmatched_rows(self):
        f = [{
            "RegistrationId": "4", "ProfileEpoch": "2", "FunctionKey": "fn",
            "FunctionName": "producer", "SourceFile": "init.lua",
            "Calls": "1", "GGCTracerSampledOwnAllocatedBytes": "10",
            "GGCTracerSampledOwnAllocations": "1",
        }]
        edge = [{
            "RegistrationId": "4", "ProfileEpoch": "3",
            "ChildFunctionKey": "fn", "ParentFunctionKey": "p",
            "CallerSourceFile": "init.lua", "CallerLine": "3",
            "Calls": "1", "ChildInclusiveMs": "1",
            "Frame": "1", "FrameInvocationOrdinal": "1",
        }]
        reg = [{"RegistrationId": "4", "Mod": "Example", "ProfileEpoch": "3"}]
        result = analyze(self.capture(f, edge, reg))
        self.assertEqual(result["unmatchedCallsiteRows"], 1)
        self.assertEqual(result["candidates"], [])

    def test_ambiguous_registration_fails_closed(self):
        f = [{"RegistrationId": "4", "ProfileEpoch": "2", "FunctionKey": "a"}]
        edge = [{"RegistrationId": "4", "ProfileEpoch": "2", "ChildFunctionKey": "a"}]
        reg = [{"RegistrationId": "4", "Mod": "One"},
               {"RegistrationId": "4", "Mod": "Different"}]
        with self.assertRaises(ValueError):
            analyze(self.capture(f, edge, reg))


if __name__ == "__main__":
    unittest.main()
