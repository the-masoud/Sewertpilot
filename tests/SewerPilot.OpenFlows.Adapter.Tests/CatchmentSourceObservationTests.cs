using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        public sealed class TestCatchmentManager
        {
            public object Ids = new List<int> { 30, 31 };
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public object ElementIDs() => Ids;
            public object DomainElementField(string name) => Fields.TryGetValue(name, out var field) ? field : null;
        }

        private static void CatchmentSource_MembershipAndZeroVersusMissing()
        {
            var manager = new TestCatchmentManager();
            manager.Fields["Hydrologic_OutflowNode"] = NativeTestField("Hydrologic_OutflowNode", 0);
            manager.Fields["Hydrologic_ApplyGroundWater"] = NativeTestField("Hydrologic_ApplyGroundWater", false);
            manager.Fields["Hydrologic_Area"] = NativeTestField("Hydrologic_Area", 0.0); // No area unit: never trusted.
            var values = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeCatchmentElements(values, manager, new TestNativeScenario(), 1);
            Check((int)values.Single(p => p.ParameterIdentity == "ElementCount").Value == 2, "TEST_ONLY native catchment membership exported.");
            Check(values.Where(p => p.ParameterIdentity == "Hydrologic_OutflowNode").All(p => p.Value is int n && n == 0 && p.SourceClassification == "PROVEN"),
                "Observed zero receiver ID is preserved; no catchment assignment is inferred.");
            Check(values.Where(p => p.ParameterIdentity == "Hydrologic_Area").All(p => p.SourceClassification != "PROVEN" && p.Reason == "QUANTITY_TYPE_OR_UNIT_UNPROVEN"),
                "Zero area without vendor units remains unresolved.");
            Check(values.Where(p => p.ParameterIdentity == "RainfallRunoff_StormEventID").All(p => p.Value == null && p.Availability == "NOT_AVAILABLE"),
                "Missing rain event is unavailable, not an empty or adopted event.");
            Check(values.All(p => p.Authority == "NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY") && manager.Fields.Values.All(f => f.Writes == 0),
                "Read-only catchment observations never become engineering approval.");
            var copied = new NativeSourceData { Parameters = values }.Copy();
            Check(copied.Parameters.Any(p => p.Category == "Catchment" && p.SourceClassification == "PROVEN"), "Catchment identities survive source DTO copy.");
            manager.Fields["Hydrologic_OutflowNode"].ActiveAlternativeID = 99;
            values.Clear(); GravityNetworkReadMapper.MapNativeCatchmentElements(values, manager, new TestNativeScenario(), 1);
            Check(values.Where(p => p.ParameterIdentity == "Hydrologic_OutflowNode").All(p => p.SourceClassification != "PROVEN" && p.Reason == "SCENARIO_ALTERNATIVE_MISMATCH"),
                "Catchment routing from a different alternative fails closed.");
        }

        private static void CatchmentSource_EnumerationFailureIsNotZero()
        {
            var values = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeCatchmentElements(values, null, new TestNativeScenario(), 1);
            Check(values.Count == 1 && values[0].Value == null && values[0].SourceClassification != "PROVEN", "Missing manager is not zero catchments.");
            foreach (object ids in new object[] { new object(), new List<int> { 30, 30 }, new List<int> { 0 } })
            {
                values.Clear(); GravityNetworkReadMapper.MapNativeCatchmentElements(values, new TestCatchmentManager { Ids = ids }, new TestNativeScenario(), 1);
                Check(values.Count == 1 && values[0].SourceClassification != "PROVEN" && values[0].Value == null, "Malformed/duplicate enumeration cannot report valid membership.");
            }
            values.Clear(); GravityNetworkReadMapper.MapNativeCatchmentElements(values, new TestCatchmentManager { Ids = new List<int>() }, new TestNativeScenario(), 1);
            Check(values.Count == 1 && values[0].Value is int n && n == 0 && values[0].SourceClassification == "PROVEN", "Genuinely empty native enumeration is distinct from missing capability.");
        }

        private static void CatchmentSource_ControlledReadOpenRouting()
        {
            Check(WaterObjectsLifecycleHost.UsesControlledProjectOpen(new HostRequest { Operation = RuntimeHostProtocol.Operation }),
                "Ordinary read-only intake uses bounded project-manager open, not graphical prompts.");
            Check(!WaterObjectsLifecycleHost.UsesControlledProjectOpen(null) &&
                !WaterObjectsLifecycleHost.UsesControlledProjectOpen(new HostRequest { Operation = "TEST_ONLY_UNKNOWN" }),
                "Null or unknown operation is not admitted to the controlled native workflow.");
            Check(WaterObjectsLifecycleHost.UsesControlledProjectOpen(new HostRequest { Operation = P16FarzaneganNativeReadHost.Operation }),
                "Existing qualified native-read opening remains selected.");
        }
    }
}
