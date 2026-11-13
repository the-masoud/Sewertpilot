using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static (MockDataSetProbe ds, MockResultManagerProbe rm, MockConduitManagerProbe cm, MockResultFieldProbe rf) CreateSecondaryMockSetup(string fieldName = "BasicVelocityResults_Velocity", object val = null)
        {
            var rf = new MockResultFieldProbe
            {
                Name = fieldName,
                HasResultsReturnValue = true,
                ValueReturnValue = val ?? 2.5
            };
            var cm = new MockConduitManagerProbe
            {
                ExistsReturnValue = true,
                SupportedFields = new List<MockFieldProbe> { new MockFieldProbe { Name = fieldName } },
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

        // Test 1: Velocity reading verifies field, unit, and non-zero/zero value
        private static void P16Secondary_VelocityReaderReadsCorrectFieldAndSemantics()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("BasicVelocityResults_Velocity", 0.0);
            var res = reader.ReadVelocity(ds, null);
            Check(res.FieldSupported, "Velocity field is supported.");
            Check(res.FieldHasResults, "Velocity field has results.");
            Check(res.Classification == "OBSERVED_ZERO", "Zero velocity correctly classified as OBSERVED_ZERO.");
            Check(res.WorkingUnitSymbol == "ft/s", "Default unit for velocity is ft/s.");
            Check(res.Status == "SUCCESS", "Velocity read status is SUCCESS.");
            Check(res.Semantics.Contains("Velocity"), "Velocity semantics established.");
        }

        // Test 2: Depth reading verifies middle/bounding depth and unit
        private static void P16Secondary_DepthReaderReadsCorrectFieldAndSemantics()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("MiddleDepthResults_DepthMiddle", 1.5);
            var res = reader.ReadDepth(ds, null);
            Check(res.FieldSupported, "Depth field is supported.");
            Check(res.FieldHasResults, "Depth field has results.");
            Check(res.Classification == "OBSERVED_FINITE", "Depth value correctly classified as OBSERVED_FINITE.");
            Check(res.WorkingUnitSymbol == "ft", "Default unit for depth is ft.");
            Check(res.Status == "SUCCESS", "Depth read status is SUCCESS.");
            Check(res.Semantics.Contains("depth"), "Depth semantics established.");
        }

        // Test 3: Capacity reading verifies full flow capacity and volumetric unit
        private static void P16Secondary_CapacityReaderSelectsFullFlowCapacityAndVolumetricUnit()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("GravityDerivedCapacityResults_FullFlowCapacity", 12.34);
            var res = reader.ReadCapacity(ds, null);
            Check(res.FieldSupported, "Full flow capacity field is supported.");
            Check(res.FieldHasResults, "Full flow capacity has results.");
            Check(res.Classification == "OBSERVED_FINITE", "Capacity classified as OBSERVED_FINITE.");
            Check(res.WorkingUnitSymbol == "ft3/s", "Capacity unit is volumetric flow rate (ft3/s).");
            Check(res.Status == "SUCCESS", "Capacity read status is SUCCESS.");
            Check(res.Semantics.Contains("capacity"), "Capacity semantics established.");
        }

        // Test 4: HGL reading verifies elevation and datum semantics
        private static void P16Secondary_HglReaderRecordsElevationAndDatumSemantics()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("MiddleHydraulicGradeResults_HydraulicGradeMiddle", 105.2);
            var res = reader.ReadHgl(ds, null);
            Check(res.FieldSupported, "HGL field is supported.");
            Check(res.FieldHasResults, "HGL field has results.");
            Check(res.Classification == "OBSERVED_FINITE", "HGL classified as OBSERVED_FINITE.");
            Check(res.WorkingUnitSymbol == "ft", "HGL unit is ft.");
            Check(res.Status == "SUCCESS", "HGL read status is SUCCESS.");
            Check(!string.IsNullOrEmpty(res.DatumSemantics), "HGL datum semantics established.");
        }

        // Test 5: Surcharge reading preserves boolean true without numeric conversion
        private static void P16Secondary_SurchargeReaderPreservesBooleanWithoutNumericConversion()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("BasicSurchargedResults_IsSurcharged", true);
            var res = reader.ReadSurcharge(ds, null);
            Check(res.FieldSupported, "Surcharge field is supported.");
            Check(res.FieldHasResults, "Surcharge field has results.");
            Check(res.Classification == "BOOLEAN_TRUE", "Surcharge true classified as BOOLEAN_TRUE.");
            Check(!res.NumericValue.HasValue, "Boolean true does not produce numeric depth.");
            Check(res.ValueClrType == "System.Boolean", "Value CLR type is System.Boolean.");
            Check(res.Status == "SUCCESS", "Surcharge read status is SUCCESS.");
        }

        // Test 6: Surcharge reading preserves boolean false without numeric conversion
        private static void P16Secondary_SurchargeReaderPreservesBooleanFalse()
        {
            var reader = new WaterObjectsResultReader();
            var (ds, _, _, rf) = CreateSecondaryMockSetup("BasicSurchargedResults_IsSurcharged", false);
            var res = reader.ReadSurcharge(ds, null);
            Check(res.Classification == "BOOLEAN_FALSE", "Surcharge false classified as BOOLEAN_FALSE.");
            Check(!res.NumericValue.HasValue, "Boolean false does not produce numeric depth.");
            Check(res.ValueClrType == "System.Boolean", "Value CLR type is System.Boolean.");
            Check(res.Status == "SUCCESS", "Surcharge read status is SUCCESS.");
        }

        // Test 7: Request validation rejects unsafe path, wrong copy ID, and mutations
        private static void P16Secondary_RequestValidationRejectsUnsafePathAndMutations()
        {
            var req = P16SecondaryMetricsHost.CreateRequest();
            Check(req.Operation == P16SecondaryMetricsHost.Operation, "Request operation matches.");
            Check(req.WorkingCopyId == "1b7cf72024f7425799f73738aa6d14e0", "Request copy id matches authorized copy.");

            var reqBadPath = P16SecondaryMetricsHost.CreateRequest();
            reqBadPath.WorkingModelPath = @"C:\Windows\System32\forbidden.stsw";
            Throws<InvalidDataException>(() => P16SecondaryMetricsHost.ValidateRequest(reqBadPath));

            var reqSample = P16SecondaryMetricsHost.CreateRequest();
            reqSample.WorkingModelPath = @"C:\Program Files\Bentley\OpenFlows Sewer\Samples\Lesson_1.stsw";
            Throws<InvalidDataException>(() => P16SecondaryMetricsHost.ValidateRequest(reqSample));

            var reqWithPlan = P16SecondaryMetricsHost.CreateRequest();
            reqWithPlan.EngineeringPlan = new GravityExecutionPlan();
            Throws<InvalidDataException>(() => P16SecondaryMetricsHost.ValidateRequest(reqWithPlan));
        }

        // Test 8: No GUI dependencies exist in secondary metrics host or reader
        private static void P16Secondary_NoGuiDependencyExists()
        {
            var hostType = typeof(P16SecondaryMetricsHost);
            var readerType = typeof(WaterObjectsResultReader);
            foreach (var m in hostType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                Check(!m.ToString().Contains("Form") && !m.ToString().Contains("Window") && !m.ToString().Contains("Mouse") && !m.ToString().Contains("Keyboard"),
                    "P16SecondaryMetricsHost member " + m.Name + " contains no GUI dependency.");
            }
            foreach (var m in readerType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                Check(!m.ToString().Contains("Form") && !m.ToString().Contains("Window") && !m.ToString().Contains("Mouse") && !m.ToString().Contains("Keyboard"),
                    "WaterObjectsResultReader member " + m.Name + " contains no GUI dependency.");
            }
            Check(!hostType.IsPublic, "P16SecondaryMetricsHost is internal only.");
        }
    }
}
