using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public class MockFieldProbe
    {
        public string Name { get; set; }
    }

    public class MockResultFieldProbe : MockFieldProbe
    {
        public bool HasResultsReturnValue { get; set; } = true;
        public object WorkingUnitIndex { get; set; }
        public object ValueReturnValue { get; set; }
        public string WorkingUnitSymbol { get; set; }

        public bool HasResults(int scenarioId) => HasResultsReturnValue;
        public object GetValue(int elementId, int scenarioId, int timestepIndex) => ValueReturnValue;
    }

    public class MockResultManagerProbe
    {
        public bool IsActive { get; set; } = true;
        public bool HasResults { get; set; } = true;
        public double[] TimeStepsReturnValue { get; set; } = new[] { 0.0, 180.0 };
        public bool OpenInvoked { get; set; }
        public bool CloseInvoked { get; set; }

        public void Open() => OpenInvoked = true;
        public void Close() => CloseInvoked = true;
        public double[] TimeSteps(object unit) => TimeStepsReturnValue;
    }

    public class MockScenarioProbe
    {
        public MockResultManagerProbe ResultManagerReturnValue { get; set; }
        public MockResultManagerProbe ResultManager(string engine) => ResultManagerReturnValue;
    }

    public class MockScenarioManagerProbe
    {
        public MockScenarioProbe ScenarioReturnValue { get; set; }
        public MockScenarioProbe Element(int id) => ScenarioReturnValue;
    }

    public class MockConduitManagerProbe
    {
        public bool ExistsReturnValue { get; set; } = true;
        public List<MockFieldProbe> SupportedFields { get; set; } = new List<MockFieldProbe>();
        public MockResultFieldProbe ResultFieldReturnValue { get; set; }

        public bool Exists(int id) => ExistsReturnValue;
        public IEnumerable SupportedResultFields(string engine) => SupportedFields;
        public MockResultFieldProbe ResultField(string fieldName, string engine, string recordName) => ResultFieldReturnValue;
    }

    public class MockDataSetProbe
    {
        public MockScenarioManagerProbe ScenarioManager { get; set; }
        public MockConduitManagerProbe ConduitManager { get; set; }
        public MockConduitManagerProbe DomainElementManager(int type) => ConduitManager;
    }

    internal static partial class Program
    {
        private static (MockDataSetProbe ds, MockResultManagerProbe rm, MockConduitManagerProbe cm, MockResultFieldProbe rf) CreateMockSetup()
        {
            var rf = new MockResultFieldProbe
            {
                Name = "BasicFlowResults_Flow",
                HasResultsReturnValue = true,
                ValueReturnValue = 1.25,
                WorkingUnitSymbol = "ft3/s"
            };
            var cm = new MockConduitManagerProbe
            {
                ExistsReturnValue = true,
                SupportedFields = new List<MockFieldProbe> { new MockFieldProbe { Name = "BasicFlowResults_Flow" } },
                ResultFieldReturnValue = rf
            };
            var rm = new MockResultManagerProbe
            {
                IsActive = true,
                HasResults = true,
                TimeStepsReturnValue = new[] { 0.0, 180.0 }
            };
            var sc = new MockScenarioProbe { ResultManagerReturnValue = rm };
            var sm = new MockScenarioManagerProbe { ScenarioReturnValue = sc };
            var ds = new MockDataSetProbe { ScenarioManager = sm, ConduitManager = cm };
            return (ds, rm, cm, rf);
        }

        // Test 1: ResultReader rejects missing result manager
        private static void P16Proof_ResultReaderRejectsMissingResultManager()
        {
            var reader = new WaterObjectsResultReader();
            var nullRes = reader.ReadFlow(null, null);
            Check(!nullRes.ResultManagerAvailable, "Missing dataset yields ResultManagerAvailable = false.");
            Check(nullRes.Status == "RESULT_MANAGER_UNAVAILABLE", "Status is RESULT_MANAGER_UNAVAILABLE on null dataset.");

            var (ds, rm, _, _) = CreateMockSetup();
            ((MockScenarioProbe)ds.ScenarioManager.ScenarioReturnValue).ResultManagerReturnValue = null;
            var res = reader.ReadFlow(ds, null);
            Check(!res.ResultManagerAvailable, "Scenario returning null ResultManager yields ResultManagerAvailable = false.");
            Check(res.Status == "RESULT_MANAGER_UNAVAILABLE", "Status is RESULT_MANAGER_UNAVAILABLE on missing manager.");
        }

        // Test 2: ResultReader reports HasResults correctly
        private static void P16Proof_ResultReaderReportsHasResultsCorrectly()
        {
            var reader = new WaterObjectsResultReader();
            var (dsTrue, rmTrue, _, _) = CreateMockSetup();
            rmTrue.HasResults = true;
            var resTrue = reader.ReadFlow(dsTrue, null);
            Check(resTrue.HasResults, "HasResults = true reported correctly.");

            var (dsFalse, rmFalse, _, _) = CreateMockSetup();
            rmFalse.HasResults = false;
            var resFalse = reader.ReadFlow(dsFalse, null);
            Check(!resFalse.HasResults, "HasResults = false reported correctly.");
            Check(resFalse.Status == "HAS_RESULTS_FALSE", "Status reflects HAS_RESULTS_FALSE.");
        }

        // Test 3: Flow field detection
        private static void P16Proof_FlowFieldDetection()
        {
            var reader = new WaterObjectsResultReader();
            var (dsSupported, _, _, _) = CreateMockSetup();
            var resSupported = reader.ReadFlow(dsSupported, null);
            Check(resSupported.FlowFieldSupported, "FlowFieldSupported = true when BasicFlowResults_Flow is present.");

            var (dsUnsupported, _, cmUnsupported, _) = CreateMockSetup();
            cmUnsupported.SupportedFields.Clear();
            var resUnsupported = reader.ReadFlow(dsUnsupported, null);
            Check(!resUnsupported.FlowFieldSupported, "FlowFieldSupported = false when BasicFlowResults_Flow is missing.");
            Check(resUnsupported.Status == "FLOW_FIELD_UNSUPPORTED", "Status reflects FLOW_FIELD_UNSUPPORTED.");
        }

        // Test 4: Flow value extraction
        private static void P16Proof_FlowValueExtraction()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateMockSetup();
            rf.ValueReturnValue = 3.14159;
            var res = reader.ReadFlow(ds, null);
            Check(res.Value.HasValue && Math.Abs(res.Value.Value - 3.14159) < 1e-6, "Flow value extracted accurately.");
            Check(res.Classification == "OBSERVED_FINITE", "Flow classified as OBSERVED_FINITE.");
            Check(res.Status == "SUCCESS", "Status is SUCCESS.");
        }

        // Test 5: Flow timestep extraction
        private static void P16Proof_FlowTimestepExtraction()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, rm, _, _) = CreateMockSetup();
            rm.TimeStepsReturnValue = new[] { 0.0, 180.0, 360.0 };
            var res = reader.ReadFlow(ds, null);
            Check(res.TimeStepIndex == 0, "TimeStepIndex extracted as 0.");
            Check(res.TimeSeconds == 0.0, "TimeSeconds extracted as 0.0.");
        }

        // Test 6: Flow unit extraction
        private static void P16Proof_FlowUnitExtraction()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, _) = CreateMockSetup();
            var res = reader.ReadFlow(ds, null);
            Check(res.WorkingUnitSymbol == "ft3/s", "Default flow working unit symbol ft3/s extracted.");
        }

        // Test 7: Zero Flow is preserved as zero
        private static void P16Proof_ZeroFlowPreservedAsZero()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateMockSetup();
            rf.ValueReturnValue = 0.0;
            var res = reader.ReadFlow(ds, null);
            Check(res.Value.HasValue && res.Value.Value == 0.0, "Flow value 0.0 preserved as zero.");
            Check(res.Classification == "OBSERVED_ZERO", "Zero flow classified strictly as OBSERVED_ZERO.");
            Check(res.Status == "SUCCESS", "Zero flow is valid SUCCESS.");
            Check(WaterObjectsResultReader.ClassifyFlowValue(0.0) == "OBSERVED_ZERO", "Helper classifies 0.0 as OBSERVED_ZERO.");
        }

        // Test 8: Invalid/missing result is not converted to zero
        private static void P16Proof_InvalidMissingNotConvertedToZero()
        {
            var reader = new WaterObjectsResultReader();
            var (dsNan, _, _, rfNan) = CreateMockSetup();
            rfNan.ValueReturnValue = double.NaN;
            var resNan = reader.ReadFlow(dsNan, null);
            Check(resNan.Classification == "INVALID_OR_MISSING", "NaN classified as INVALID_OR_MISSING.");
            Check(resNan.Status == "VALUE_NOT_FINITE", "NaN status is VALUE_NOT_FINITE.");

            var (dsNull, _, _, rfNull) = CreateMockSetup();
            rfNull.ValueReturnValue = null;
            var resNull = reader.ReadFlow(dsNull, null);
            Check(!resNull.Value.HasValue, "Null raw value preserves null double.");
            Check(resNull.Classification == "INVALID_OR_MISSING", "Null raw value classified as INVALID_OR_MISSING.");

            Check(WaterObjectsResultReader.ClassifyFlowValue(null) == "INVALID_OR_MISSING", "Helper classifies null as INVALID_OR_MISSING.");
            Check(WaterObjectsResultReader.ClassifyFlowValue(double.NaN) == "INVALID_OR_MISSING", "Helper classifies NaN as INVALID_OR_MISSING.");
            Check(WaterObjectsResultReader.ClassifyFlowValue(double.PositiveInfinity) == "INVALID_OR_MISSING", "Helper classifies PositiveInfinity as INVALID_OR_MISSING.");
        }

        // Test 9: Result lineage contains model/scenario/alternative identity
        private static void P16Proof_LineageContainsRequiredIdentities()
        {
            var ctx = P16HeadlessResultProofHost.Context();
            Check(ctx["P16ScenarioId"] == "81", "ScenarioId is 81.");
            Check(ctx["P16PhysicalAlternativeId"] == "82", "PhysicalAlternativeId is 82.");
            Check(ctx["P16CalculationOptionsId"] == "67", "CalculationOptionsId is 67.");
            Check(ctx["P16Engine"] == "CivilStormDynamicEngine", "Engine is CivilStormDynamicEngine.");
            Check(ctx["P16ElementId"] == "27", "ElementId is 27.");
        }

        // Test 10: Runtime operation rejects unsafe model path
        private static void P16Proof_RejectsUnsafeModelPath()
        {
            var req = P16HeadlessResultProofHost.CreateRequest();
            req.WorkingModelPath = @"C:\Temp\..\Windows\System32\model.stsw";
            Throws<InvalidDataException>(() => P16HeadlessResultProofHost.ValidateRequest(req));

            var reqBadCopy = P16HeadlessResultProofHost.CreateRequest();
            reqBadCopy.WorkingCopyId = "invalid_copy_id_12345";
            Throws<InvalidDataException>(() => P16HeadlessResultProofHost.ValidateRequest(reqBadCopy));

            var reqWithPlan = P16HeadlessResultProofHost.CreateRequest();
            reqWithPlan.EngineeringPlan = new GravityExecutionPlan();
            Throws<InvalidDataException>(() => P16HeadlessResultProofHost.ValidateRequest(reqWithPlan));
        }

        // Test 11: Runtime operation cannot target the protected source model
        private static void P16Proof_CannotTargetProtectedSourceModel()
        {
            var reqSample = P16HeadlessResultProofHost.CreateRequest();
            reqSample.WorkingModelPath = @"C:\Program Files\Bentley\OpenFlows Sewer\Samples\Lesson_1.stsw";
            Throws<InvalidDataException>(() => P16HeadlessResultProofHost.ValidateRequest(reqSample));

            var reqReadOnly = P16HeadlessResultProofHost.CreateRequest();
            reqReadOnly.WorkingModelPath = @"C:\Users\Public\AppData\Local\Temp\SewerPilot\OpenFlows\ReadOnly\ec8c3d18e0c4429b8ce2973019786ca8\Lesson_1.stsw";
            Throws<InvalidDataException>(() => P16HeadlessResultProofHost.ValidateRequest(reqReadOnly));

            Check(P16HeadlessResultProofHost.AuthorizedCopyId == "1b7cf72024f7425799f73738aa6d14e0", "AuthorizedCopyId is locked to disposable instance.");
        }

        // Test 12: No GUI dependency exists in the operation
        private static void P16Proof_NoGuiDependencyExists()
        {
            var hostType = typeof(P16HeadlessResultProofHost);
            var readerType = typeof(WaterObjectsResultReader);
            foreach (var m in hostType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                Check(!m.ToString().Contains("Form") && !m.ToString().Contains("Window") && !m.ToString().Contains("Mouse") && !m.ToString().Contains("Keyboard"),
                    "P16HeadlessResultProofHost member " + m.Name + " contains no GUI dependency.");
            }
            foreach (var m in readerType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                Check(!m.ToString().Contains("Form") && !m.ToString().Contains("Window") && !m.ToString().Contains("Mouse") && !m.ToString().Contains("Keyboard"),
                    "WaterObjectsResultReader member " + m.Name + " contains no GUI dependency.");
            }
            Check(!hostType.IsPublic, "P16HeadlessResultProofHost is internal/test only.");
        }
    }
}
