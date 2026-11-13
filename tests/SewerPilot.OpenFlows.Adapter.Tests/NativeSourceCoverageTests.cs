using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        // Every native object below is TEST_ONLY. These tests prove mapping, not real-model values.
        public sealed class TestNativeAlternative { public int Id => 12; }
        public sealed class TestNativeScenario
        {
            public int Assigned = 13, Options = 67;
            public int AlternativeID(int typeId) => typeId == 12 ? Assigned : -1;
            public int CalculationOptionsID(string engine) => Options;
        }
        public sealed class TestNativeField
        {
            public string Name { get; set; }
            public object RawValue;
            public int Reads, Writes;
            public bool Throw;
            public int ActiveAlternativeID { get; set; } = 13;
            public TestNativeAlternative AlternativeType => new TestNativeAlternative();
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? WorkingUnitIndex { get; set; }
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? StorageUnitIndex => WorkingUnitIndex;
            public IDictionary Values;
            public object GetValue(int id) { Reads++; if (Throw) throw new InvalidOperationException("TEST_ONLY_SECRET_MUST_NOT_ESCAPE"); return RawValue; }
            public IDictionary GetValues() { Reads++; return Values; }
            public void SetValue(int id, object value) { Writes++; throw new InvalidOperationException("TEST_ONLY_WRITE_FORBIDDEN"); }
        }
        public sealed class TestNativeManager
        {
            public bool ThrowOnMissing = true;
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public readonly Dictionary<string, object> CustomFields = new Dictionary<string, object>();
            public object DomainElementField(string name)
            {
                if (Fields.TryGetValue(name, out var f)) return f;
                if (CustomFields.TryGetValue(name, out var cf)) return cf;
                if (ThrowOnMissing) throw new ArgumentException("Field not found: " + name);
                return null;
            }
        }
        public sealed class TestNativeCollection
        {
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public object Field(string name) => Fields.TryGetValue(name, out var f) ? f : null;
        }
        public sealed class TestNativeElement
        {
            public TestNativeCollection SanitaryLoads { get; set; }
            public object BoundaryConditionTypeObject() => new Haestad.Domain.ModelingObjects.Dynamic.BoundaryConditionTypeObject_UserDefinedTwType();
        }
        public enum TestBoundaryKind { FreeOutfallType = 0, Other = 1 }
        public sealed class TestTypedBoundaryElement
        {
            public TestBoundaryKind BoundaryConditionType { get; set; }
        }
        public sealed class TestSystemFlowManager
        {
            public TestNativeField Field;
            public string Scope;
            public object DomainElementField(string name) => null;
            public object DomainElementField(string name, string scope)
            { Scope=scope; return name=="SystemFlows_KnownFlow" && scope=="SystemFlows" ? Field : null; }
        }
        public sealed class TestNativeDataSetType
        {
            public string Engine = "TEST_ONLY_ENGINE";
            public int Reads;
            public bool Throw;
            public string GetMainNumericalEngineTypeName()
            {
                Reads++;
                if (Throw) throw new InvalidOperationException("TEST_ONLY_ENGINE_READ_FAILED");
                return Engine;
            }
        }
        public sealed class TestNativeDataset
        {
            public TestNativeDataSetType DataSetType = new TestNativeDataSetType();
            public int DataSetTypeReads;
            public int WrongDatasetLevelReads;
            public string Engine { get => DataSetType?.Engine; set { if (DataSetType != null) DataSetType.Engine = value; } }
            public object DomainDataSetType() { DataSetTypeReads++; return DataSetType; }
            public string GetMainNumericalEngineTypeName() { WrongDatasetLevelReads++; return "TEST_ONLY_WRONG_DATASET_LEVEL"; }
        }
        public sealed class TestNativeDatasetWithoutOwner
        {
            public int WrongDatasetLevelReads;
            public string GetMainNumericalEngineTypeName() { WrongDatasetLevelReads++; return "TEST_ONLY_ENGINE"; }
        }
        public sealed class TestNativeDataSetTypeBadSignature
        {
            public int GetMainNumericalEngineTypeName() => 42;
        }
        public sealed class TestNativeDatasetBadSignature
        {
            public object DomainDataSetType() => new TestNativeDataSetTypeBadSignature();
        }
        public sealed class TestNativeDatasetMissingMethod
        {
            public object DomainDataSetType() => new object();
        }
        public sealed class TestNativeEngineItem
        {
            public string Name { get; }
            public int Id { get; }
            public TestNativeEngineItem(string name, int id) { Name = name; Id = id; }
        }
        public sealed class TestNativeDataSetTypeMultiEngine
        {
            public string MainEngine = "DelawareProductEngine";
            public List<TestNativeEngineItem> Engines = new List<TestNativeEngineItem>
            {
                new TestNativeEngineItem("CivilStormDynamicEngine", -719657520),
                new TestNativeEngineItem("DelawareProductEngine", 1633152845)
            };
            public string GetMainNumericalEngineTypeName() => MainEngine;
            public object NumericalEngineTypes() => Engines;
            public object NumericalEngineType(string name) => Engines.FirstOrDefault(e => e.Name == name);
        }
        public sealed class TestNativeDatasetMultiEngine
        {
            public TestNativeDataSetTypeMultiEngine DataSetType = new TestNativeDataSetTypeMultiEngine();
            public object DomainDataSetType() => DataSetType;
        }
        public sealed class TestNativeOptions
        {
            public string NumericalEngineTypeName { get; set; } = "TEST_ONLY_ENGINE";
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public object CalculationOptionsField(string name) => Fields.TryGetValue(name, out var f) ? f : null;
        }
        public sealed class TestNativeOptionsManager
        {
            public int Reads; public int ManagerFieldReads; public TestNativeOptions Options = new TestNativeOptions();
            public object Element(int id) { Reads++; if (id != 67) throw new InvalidOperationException("TEST_ONLY_WRONG_ID"); return Options; }
            public object CalculationOptionsField(string name) { ManagerFieldReads++; return Options?.CalculationOptionsField(name); }
            public object ModelingElementField(string name) => null;
            public object SupportedFields() => Options?.Fields.Values.ToList();
        }
        public sealed class TestNativeFieldDomainBase
        {
            public string Name { get; set; }
            public object RawValue;
            public int Reads;
            public int AlternativeIdValue = 13;
            public TestNativeAlternative AlternativeType => new TestNativeAlternative();
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? WorkingUnitIndex { get; set; }
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? StorageUnitIndex => WorkingUnitIndex;
            internal int AlternativeID() => AlternativeIdValue;
            public object GetValue(int id) { Reads++; return RawValue; }
        }
        public sealed class TestNativeFieldScenarioAlt
        {
            public string Name { get; set; }
            public object RawValue;
            public int Reads;
            public int AlternativeIdValue = 13;
            public TestNativeAlternative AlternativeType => new TestNativeAlternative();
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? WorkingUnitIndex { get; set; }
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? StorageUnitIndex => WorkingUnitIndex;
            internal int AlternativeID(int scenarioId) => scenarioId == 1 ? AlternativeIdValue : -1;
            public object GetValue(int id) { Reads++; return RawValue; }
        }
        public sealed class TestNativeFieldNoAlt
        {
            public string Name { get; set; }
            public object RawValue;
            public TestNativeAlternative AlternativeType => null;
            public object GetValue(int id) => RawValue;
        }
        public sealed class TestNativeOptionsThrowingNre
        {
            public string NumericalEngineTypeName { get; set; } = "CivilStormDynamicEngine";
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_NULL_MANAGER_BACKLINK");
        }
        public sealed class TestNativeOptionsManagerWithNreOptions
        {
            public int Reads; public int ManagerFieldReads;
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) { ManagerFieldReads++; return Fields.TryGetValue(name, out var f) ? f : null; }
            public object ModelingElementField(string name) => null;
        }
        public sealed class TestNativeOptionsManagerThrowingAll
        {
            public int Reads;
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
        }
        public sealed class TestNativeOptionsManagerModelingElementFieldFallback
        {
            public int Reads;
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => Fields.TryGetValue(name, out var f) ? f : null;
        }
        public sealed class TestNativeOptionsManagerWithSupportedFields
        {
            public int Reads;
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => -719657520;
            public readonly List<TestNativeField> Supported = new List<TestNativeField>();
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
            public object SupportedFields() => Supported;
        }
        public sealed class TestNativeOptionsManagerWithEmptySupportedFields
        {
            public int Reads;
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => -719657520;
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
            public object SupportedFields() => new List<TestNativeField>();
        }
        public sealed class TestNativeOptionsManagerWithNullSupportedFields
        {
            public int Reads;
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => -719657520;
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
            public object SupportedFields() => null;
        }
        public sealed class TestNativeOptionsManagerThrowingSupportedFields
        {
            public int Reads;
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => -719657520;
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
            public object SupportedFields() => throw new InvalidOperationException("TEST_ONLY_SIMULATED_ENUM_FAILED_SECRET");
        }
        public sealed class TestNativeOptionsManagerWithEngineMismatch
        {
            public int Reads;
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => 1633152845; // DelawareProductEngine ID != -719657520
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public object Element(int id) { Reads++; return Options; }
            public object CalculationOptionsField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_LOOKUP");
            public object ModelingElementField(string name) => throw new NullReferenceException("TEST_ONLY_SIMULATED_BENTLEY_NRE_MODELING_FIELD");
            public object SupportedFields() => new List<TestNativeField>();
        }
        public sealed class TestNativeEngineTypeWithLookup
        {
            public string Name { get; set; } = "CivilStormDynamicEngine";
            public int Id { get; set; } = -719657520;
            public Func<string, bool> ExistsFunc { get; set; }
            public bool CalculationOptionsFieldTypeExists(string name) => ExistsFunc != null && ExistsFunc(name);
        }
        public sealed class TestNativeDataSetTypeWithEngineLookup
        {
            public string MainEngine = "DelawareProductEngine";
            public TestNativeEngineTypeWithLookup Engine = new TestNativeEngineTypeWithLookup();
            public string GetMainNumericalEngineTypeName() => MainEngine;
            public object NumericalEngineTypes() => new List<object> { Engine };
            public object NumericalEngineType(string name) => name == Engine.Name ? Engine : null;
        }
        public sealed class TestNativeDatasetWithEngineLookup
        {
            public TestNativeDataSetTypeWithEngineLookup DataSetType = new TestNativeDataSetTypeWithEngineLookup();
            public object DomainDataSetType() => DataSetType;
        }
        public sealed class TestNativeOptionsManagerWithFieldDouble
        {
            public object TargetField;
            public TestNativeOptionsThrowingNre Options = new TestNativeOptionsThrowingNre();
            public TestNativeOptionsManagerWithFieldDouble(object field) { TargetField = field; }
            public object Element(int id) => Options;
            public object CalculationOptionsField(string name) => name == "CivilStormDynamicEngine_CalcTimeStep" ? TargetField : null;
            public object ModelingElementField(string name) => null;
        }
        public sealed class TestNativeEngineTypeItem
        {
            public string Name { get; set; } = "CivilStormDynamicEngine";
        }
        public sealed class TestNativeFieldTypeItem
        {
            public TestNativeEngineTypeItem NumericalEngineType { get; set; } = new TestNativeEngineTypeItem();
        }
        public sealed class TestMapScenarioElement
        {
            public int Id { get; set; } = 1;
            public string Label { get; set; } = "Base Scenario";
            public string EngineNameRequested { get; set; }
            public int OptionsIdToReturn { get; set; } = 14;
            public int AlternativeID(int typeId) => 0;
            public int CalculationOptionsID(string engineName)
            {
                EngineNameRequested = engineName;
                return OptionsIdToReturn;
            }
        }
        public sealed class TestMapCalculationOptionElement
        {
            public int Id { get; set; } = 14;
            public string Label { get; set; } = "Base Calculation Options";
            public string NumericalEngineTypeName { get; set; } = "DelawareProductEngine";
            public object CalculationOptionsField(string name) => null;
        }
        public sealed class TestMapCalculationOptionsManager
        {
            public string NumericalEngineTypeName { get; set; } = "DelawareProductEngine";
            public string ModelingElementType => "CalculationOptions";
            public int ElementTypeID => 1633152845;
            public TestMapCalculationOptionElement ElementObj = new TestMapCalculationOptionElement();
            public object ElementIDs() => new List<int> { 14 };
            public object Element(int id) => ElementObj;
            public object CalculationOptionsField(string name) => null;
            public object ModelingElementField(string name) => null;
            public object SupportedFields() => new List<TestNativeField>();
        }
        public sealed class TestMapScenarioManager
        {
            public int ActiveScenarioID { get; set; } = 1;
            public TestMapScenarioElement ScenarioElem = new TestMapScenarioElement();
            public TestMapCalculationOptionsManager CalcOptionsMgr = new TestMapCalculationOptionsManager();
            public string EngineNameRequested { get; set; }
            public object ElementIDs() => new List<int> { 1 };
            public object Element(int id) => ScenarioElem;
            public object BaseElements() => new List<object> { ScenarioElem };
            public object ChildrenOfElement(int id) => new List<object>();
            public object CalculationOptionsManager(string engineName)
            {
                EngineNameRequested = engineName;
                return CalcOptionsMgr;
            }
        }
        public sealed class TestMapDataSetType
        {
            public string MainEngine = "DelawareProductEngine";
            public bool ThrowOnGetMain;
            public List<TestNativeEngineItem> Engines = new List<TestNativeEngineItem>
            {
                new TestNativeEngineItem("CivilStormDynamicEngine", -719657520),
                new TestNativeEngineItem("DelawareProductEngine", 1633152845)
            };
            public string GetMainNumericalEngineTypeName()
            {
                if (ThrowOnGetMain) throw new InvalidOperationException("TEST_ONLY_GET_MAIN_FAILED");
                return MainEngine;
            }
            public object NumericalEngineTypes() => Engines;
            public object NumericalEngineType(string name) => Engines.FirstOrDefault(e => e.Name == name);
            public object AlternativeTypes() => new List<object>();
        }
        public sealed class TestMapDataset
        {
            public TestMapScenarioManager ScenarioManager { get; set; } = new TestMapScenarioManager();
            public TestMapDataSetType DataSetType { get; set; } = new TestMapDataSetType();
            public object DomainDataSetType() => DataSetType;
            public object AlternativeManager(int typeId) => null;
        }
        public sealed class TestMapDatasetWithObjectDst
        {
            public object DstObj;
            public TestMapScenarioManager ScenarioManager { get; set; } = new TestMapScenarioManager();
            public TestMapDatasetWithObjectDst(object dst) { DstObj = dst; }
            public object DomainDataSetType() => DstObj;
            public object AlternativeManager(int typeId) => null;
        }
        public sealed class TestNativeFieldWithMetadata
        {
            public string Name { get; set; }
            public object RawValue;
            public TestNativeFieldTypeItem FieldType { get; set; } = new TestNativeFieldTypeItem();
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? WorkingUnitIndex { get; set; }
            public Haestad.Support.Units.UnitConversionManager.UnitIndex? StorageUnitIndex => WorkingUnitIndex;
            public object GetValue(int id) => RawValue;
        }
        public sealed class TestNativeFieldThrowingFieldType
        {
            public string Name { get; set; } = "CivilStormDynamicEngine_CalcTimeStep";
            public object FieldType => throw new NullReferenceException("TEST_ONLY_SIMULATED_FIELD_TYPE_NRE");
            public object GetValue(int id) => 0.5;
        }
        public sealed class TestNativeFieldTypeThrowingEngineType
        {
            public object NumericalEngineType => throw new NullReferenceException("TEST_ONLY_SIMULATED_ENGINE_TYPE_NRE");
        }
        public sealed class TestNativeFieldThrowingEngineType
        {
            public string Name { get; set; } = "CivilStormDynamicEngine_CalcTimeStep";
            public object FieldType => new TestNativeFieldTypeThrowingEngineType();
            public object GetValue(int id) => 0.5;
        }
        private static TestNativeField NativeTestField(string name, object value,
            Haestad.Support.Units.UnitConversionManager.UnitIndex? unit = null) => new TestNativeField { Name = name, RawValue = value, WorkingUnitIndex = unit };
        private static NativeSourceParameter NativeTestRead(TestNativeField field, string name = "InfiltrationAndInflow_InfiltrationFlow", TestNativeScenario scenario = null) =>
            GravityNetworkReadMapper.ReadNativeParameter("Loading", name, field, 101, 1, scenario ?? new TestNativeScenario());

        private static void P17S2_ExactMappingAndZeroVersusNull()
        {
            var field = NativeTestField("InfiltrationAndInflow_InfiltrationFlow", 0.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs);
            var zero = NativeTestRead(field);
            Check(zero.Availability == "NATIVE_CONFIGURATION_OBSERVED" && zero.SourceClassification == "PROVEN" && (double)zero.Value == 0,
                "TEST_ONLY exact field maps a real numeric zero, never an absent value.");
            Check(zero.NativeValueType == "System.Double" && zero.ParameterIdentity == field.Name && zero.SourceId == 101 && field.Reads == 1 && field.Writes == 0,
                "Native scalar type, exact field/owner identity and read-only invocation retained.");
            field.RawValue = null; var missing = NativeTestRead(field);
            Check(missing.Value == null && missing.Availability == "NOT_AVAILABLE" && missing.Reason == "NULL_NATIVE_VALUE",
                "Native null remains unavailable, without a zero fallback.");
        }
        private static void P17S2_UnitsProvenanceAndAlternative()
        {
            var p = NativeTestRead(NativeTestField("InfiltrationAndInflow_InfiltrationFlow", 1.25, Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs));
            Check(p.Unit.Dimension == "flow" && p.Unit.UnitId == "Cfs" && p.Unit.Symbol == "ft3/s" && p.Unit.Provenance.Source == "WaterObjectsFieldMetadata",
                "Exact native quantity/unit identity and metadata provenance retained without conversion.");
            Check(p.ScenarioId == 1 && p.AlternativeTypeId == 12 && p.AlternativeId == 13 && p.ScenarioAssignedAlternativeId == 13 &&
                p.SourceProvenance.Contains("IScenario.AlternativeID(Int32)"), "Current scenario and native owning alternative agree explicitly.");
            var copy = p.Copy(); copy.Unit.Symbol = "TEST_ONLY_CHANGED";
            Check(p.Unit.Symbol == "ft3/s", "Unit and provenance are safely copied.");

            // DomainElementFieldBase without ActiveAlternativeID property associates via AlternativeID()
            var domainBaseField = new TestNativeFieldDomainBase
            {
                Name = "InfiltrationLoadType",
                RawValue = 1,
                AlternativeIdValue = 13
            };
            var pDomainBase = GravityNetworkReadMapper.ReadNativeParameter("Loading", domainBaseField.Name, domainBaseField, 101, 1, new TestNativeScenario());
            Check(pDomainBase.Availability == "NATIVE_CONFIGURATION_OBSERVED" && pDomainBase.SourceClassification == "PROVEN" &&
                pDomainBase.AlternativeId == 13 && pDomainBase.ScenarioAssignedAlternativeId == 13 &&
                pDomainBase.SourceProvenance.Contains("DomainElementFieldBase.AlternativeID()"),
                "DomainElementFieldBase resolves alternative association via parameterless AlternativeID() method and preserves provenance.");

            // DomainElementFieldBase resolves via AlternativeID(Int32 scenarioId) when parameterless method is absent
            var scenarioAltField = new TestNativeFieldScenarioAlt
            {
                Name = "BoundaryConditionType",
                RawValue = 2,
                AlternativeIdValue = 13
            };
            var pScenarioAlt = GravityNetworkReadMapper.ReadNativeParameter("Outfall", scenarioAltField.Name, scenarioAltField, 25, 1, new TestNativeScenario());
            Check(pScenarioAlt.Availability == "NATIVE_CONFIGURATION_OBSERVED" && pScenarioAlt.SourceClassification == "PROVEN" &&
                pScenarioAlt.AlternativeId == 13 && pScenarioAlt.ScenarioAssignedAlternativeId == 13 &&
                pScenarioAlt.SourceProvenance.Contains("DomainElementFieldBase.AlternativeID(Int32)"),
                "DomainElementFieldBase resolves alternative association via AlternativeID(Int32) method and preserves provenance.");

            // Field with null or invalid AlternativeType fails closed
            var noAltField = new TestNativeFieldNoAlt { Name = "InfiltrationLoadType", RawValue = 1 };
            var pNoAlt = GravityNetworkReadMapper.ReadNativeParameter("Loading", noAltField.Name, noAltField, 101, 1, new TestNativeScenario());
            Check(pNoAlt.Availability == "SEMANTICS_UNRESOLVED" && pNoAlt.Reason == "ALTERNATIVE_ASSOCIATION_UNPROVEN",
                "Field with unproven AlternativeType fails closed without reading value.");

            // TEST_ONLY source-model design intake: raw settings must never imply reviewed criteria.
            var designSlope = GravityNetworkReadMapper.ReadNativeParameter("DesignModelSettings", "Design_LocalMinSlope",
                NativeTestField("Design_LocalMinSlope", 0.003), 101, 1, new TestNativeScenario());
            Check(designSlope.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)designSlope.Value == 0.003 &&
                designSlope.AlternativeId == 13 && designSlope.Category == "DesignModelSettings",
                "Native design slope is an alternative-bound raw observation, not a reviewed Design Basis.");
            var designCover = GravityNetworkReadMapper.ReadNativeParameter("DesignModelSettings", "Design_LocalMinCover",
                NativeTestField("Design_LocalMinCover", 2.624671916, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet),
                101, 1, new TestNativeScenario());
            Check(designCover.Availability == "NATIVE_CONFIGURATION_OBSERVED" && designCover.Unit?.Symbol == "ft" &&
                designCover.Unit.Provenance.Source == "WaterObjectsFieldMetadata",
                "Native design cover requires observed length units and retains exact vendor metadata.");
            var noUnit = GravityNetworkReadMapper.ReadNativeParameter("DesignModelSettings", "Design_LocalMinCover",
                NativeTestField("Design_LocalMinCover", 2.6), 101, 1, new TestNativeScenario());
            Check(noUnit.Availability == "SEMANTICS_UNRESOLVED" && noUnit.Reason == "QUANTITY_TYPE_OR_UNIT_UNPROVEN",
                "Native design cover without a proven unit fails closed.");
            Check(GravityNetworkReadMapper.TargetFieldKeys("DesignModelSettings").Count == 12 &&
                !GravityNetworkReadMapper.RecognizedNativeSourceParameter("DesignModelSettings", "ApprovedDesignBasis"),
                "Design intake admits only 12 exact raw field identities, never fabricated approval.");
        }
        private static void P17S2_MissingUnknownAndSanitizedFailure()
        {
            Check(NativeTestRead(null).Availability == "NOT_AVAILABLE", "Missing native field is unavailable.");
            var field = NativeTestField("TEST_ONLY_UNKNOWN", 12.0);
            Check(NativeTestRead(field, "TEST_ONLY_UNKNOWN").Reason == "UNRECOGNIZED_PARAMETER" && field.Reads == 0,
                "Unknown field names cannot become proven observations or trigger value reads.");
            Check(NativeTestRead(field).Reason == "FIELD_IDENTITY_MISMATCH" && field.Reads == 0, "Returned field identity must match the exact supported member.");
            field.Name = "InfiltrationAndInflow_InfiltrationFlow"; field.Throw = true;
            var failed = NativeTestRead(field);
            Check(failed.Availability == "SEMANTICS_UNRESOLVED" && failed.Reason.Contains("System.InvalidOperationException") &&
                !GravityExecutionPlanBuilder.Json(failed).Contains("TEST_ONLY_SECRET_MUST_NOT_ESCAPE"), "Native errors retain type/classification without sensitive exception text.");
        }
        private static void P17S2_StaleAlternativeAndInvalidIdentity()
        {
            var field = NativeTestField("InfiltrationAndInflow_InfiltrationFlow", 0.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs);
            var p = NativeTestRead(field, scenario: new TestNativeScenario { Assigned = 99 });
            Check(p.Availability == "SEMANTICS_UNRESOLVED" && p.Reason == "SCENARIO_ALTERNATIVE_MISMATCH" && p.AlternativeId == 13 &&
                p.ScenarioAssignedAlternativeId == 99 && field.Reads == 0, "Mismatched alternative association fails closed before reading values.");
            p = GravityNetworkReadMapper.ReadNativeParameter("Loading", field.Name, field, 0, 1, new TestNativeScenario());
            Check(p.Reason == "SOURCE_IDENTITY_UNPROVEN" && field.Reads == 0, "Invalid element identity is not accepted.");
        }
        private static void P17S2_CalculationOptionsAndEngineAssociation()
        {
            var manager = new TestNativeOptionsManager();
            manager.Options.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.5,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var method = typeof(TestNativeOptionsManager).GetMethod("Element", new[] { typeof(int) });
            var dataset = new TestNativeDataset();
            var result = GravityNetworkReadMapper.ReadNativeCalculationParameters(dataset, new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(dataset.DataSetType.Reads == 1 && dataset.DataSetTypeReads == 1 && dataset.WrongDatasetLevelReads == 0,
                "GetMainNumericalEngineTypeName is invoked on dataset.DomainDataSetType(), never directly on dataset.");
            var step = result.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(step.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)step.Value == .5 && step.Unit.Symbol == "min" && step.Unit.Dimension == "time" &&
                step.EngineTypeName == "TEST_ONLY_ENGINE" && step.CalculationOptionsId == 67 && step.SourceId == 67 && step.ScenarioId == 1,
                "TEST_ONLY options retain engine/scenario/options ownership, exact value and time unit without conversion.");
            Check(result.Single(p => p.ParameterIdentity == "EpsType_Duration").Availability == "NOT_AVAILABLE", "Missing duration does not become an invented default.");
            var engineParam = result.Single(p => p.ParameterIdentity == "NumericalEngineTypeName");
            Check(engineParam.Availability == "NATIVE_CONFIGURATION_OBSERVED" && engineParam.SourceClassification == "PROVEN" &&
                (string)engineParam.Value == "TEST_ONLY_ENGINE" && engineParam.EngineTypeName == "TEST_ONLY_ENGINE" &&
                engineParam.SourceProvenance.Contains("IDomainDataSet.DomainDataSetType().GetMainNumericalEngineTypeName()"),
                "Provenance identifies the actual owner IDomainDataSetType method traversed via DomainDataSetType().");
            var blocked = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDataset { Engine = "TEST_ONLY_OTHER" }, new TestNativeScenario(),
                1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(blocked.Single().Availability == "SEMANTICS_UNRESOLVED" && blocked.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN" && manager.Reads == 1,
                "No first-engine guess or options read after a main-engine mismatch.");
            var wrongDouble = new TestNativeDatasetWithoutOwner();
            var resWrong = GravityNetworkReadMapper.ReadNativeCalculationParameters(wrongDouble, new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resWrong.Single().Availability == "SEMANTICS_UNRESOLVED" && resWrong.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN" && wrongDouble.WrongDatasetLevelReads == 0,
                "Dataset without DomainDataSetType cannot establish evidence from dataset-level method.");
            var missingOwner = new TestNativeDataset { DataSetType = null };
            var resMissingOwner = GravityNetworkReadMapper.ReadNativeCalculationParameters(missingOwner, new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resMissingOwner.Single().Availability == "SEMANTICS_UNRESOLVED" && resMissingOwner.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Missing DomainDataSetType owner fails closed.");
            var resNullDataset = GravityNetworkReadMapper.ReadNativeCalculationParameters(null, new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resNullDataset.Single().Availability == "SEMANTICS_UNRESOLVED" && resNullDataset.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Null dataset fails closed.");
            var resMissingMethod = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMissingMethod(), new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resMissingMethod.Single().Availability == "SEMANTICS_UNRESOLVED" && resMissingMethod.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Owner missing GetMainNumericalEngineTypeName fails closed.");
            var resBadSig = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetBadSignature(), new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resBadSig.Single().Availability == "SEMANTICS_UNRESOLVED" && resBadSig.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Unsupported owner method signature fails closed.");
            var throwing = new TestNativeDataset();
            throwing.DataSetType.Throw = true;
            var resThrow = GravityNetworkReadMapper.ReadNativeCalculationParameters(throwing, new TestNativeScenario(), 1, "TEST_ONLY_ENGINE", 67, method, manager);
            Check(resThrow.Single().Availability == "SEMANTICS_UNRESOLVED" && resThrow.Single().SourceClassification != "PROVEN" &&
                resThrow.Single().Reason.Contains("NATIVE_READ_FAILED:System.InvalidOperationException"),
                "Invocation failure cannot produce a PROVEN value and fails closed with sanitized error.");
            Check(manager.Options.Fields.Values.All(f => f.Writes == 0),
                "Zero native field writes or mutations occurred during calculation parameter read.");

            // P17-S3 Multi-engine dataset: main engine differs from target engine, but target engine is in NumericalEngineTypes
            var multiMgr = new TestNativeOptionsManager();
            multiMgr.Options.NumericalEngineTypeName = "CivilStormDynamicEngine";
            multiMgr.Options.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 1.0,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var multiResult = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67, method, multiMgr);
            var multiStep = multiResult.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(multiStep.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)multiStep.Value == 1.0 &&
                multiStep.EngineTypeName == "CivilStormDynamicEngine" && multiStep.CalculationOptionsId == 67,
                "Multi-engine dataset correctly associates non-main target engine when present in NumericalEngineTypes.");
            var multiEngineParam = multiResult.Single(p => p.ParameterIdentity == "NumericalEngineTypeName");
            Check(multiEngineParam.Availability == "NATIVE_CONFIGURATION_OBSERVED" && multiEngineParam.SourceClassification == "PROVEN" &&
                (string)multiEngineParam.Value == "CivilStormDynamicEngine" &&
                multiEngineParam.SourceProvenance.Contains("DatasetMainEngine=DelawareProductEngine") &&
                multiEngineParam.SourceProvenance.Contains("OptionsEngine=CivilStormDynamicEngine"),
                "Provenance preserves both exact engine strings (DelawareProductEngine and CivilStormDynamicEngine) and source identities.");

            // Mismatched options engine: options object declares a different engine than target
            var mismatchMgr = new TestNativeOptionsManager();
            mismatchMgr.Options.NumericalEngineTypeName = "WRONG_OPTIONS_ENGINE";
            var mismatchRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67, method, mismatchMgr);
            Check(mismatchRes.Single().Availability == "SEMANTICS_UNRESOLVED" &&
                mismatchRes.Single().Reason == "OPTIONS_ENGINE_MISMATCH" &&
                mismatchRes.Single().SourceProvenance.Contains("OptionsEngine=WRONG_OPTIONS_ENGINE") &&
                mismatchRes.Single().SourceProvenance.Contains("TargetEngine=CivilStormDynamicEngine"),
                "Options engine mismatch fails closed and preserves both engine identities in diagnostic evidence.");

            // Mismatched scenario options ID: scenario assigns 99 instead of 67
            var badScenarioRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario { Options = 99 }, 1, "CivilStormDynamicEngine", 67, method, multiMgr);
            Check(badScenarioRes.Single().Availability == "SEMANTICS_UNRESOLVED" &&
                badScenarioRes.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Scenario options ID mismatch fails closed.");

            // Missing options element: manager.Element returns null
            var nullOptionsMgr = new TestNativeOptionsManager { Options = null };
            var nullOptRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67, method, nullOptionsMgr);
            Check(nullOptRes.Single().Availability == "SEMANTICS_UNRESOLVED" &&
                nullOptRes.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Missing options element fails closed.");

            // Target engine not in NumericalEngineTypes and not main engine
            var unsuppRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "UNSUPPORTED_ENGINE", 67, method, multiMgr);
            Check(unsuppRes.Single().Availability == "SEMANTICS_UNRESOLVED" &&
                unsuppRes.Single().Reason == "ACTIVE_ENGINE_OR_OPTIONS_ASSOCIATION_UNPROVEN",
                "Unsupported engine identity not in NumericalEngineTypes fails closed.");

            // Calculation options: options element throws NullReferenceException (simulating null element manager backlink),
            // but manager supplies CalculationOptionsField directly (ICalculationOptionsManager contract)
            var nreMgr = new TestNativeOptionsManagerWithNreOptions();
            nreMgr.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.25,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var nreResult = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithNreOptions).GetMethod("Element", new[] { typeof(int) }), nreMgr);
            var nreStep = nreResult.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(nreStep.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)nreStep.Value == 0.25 &&
                nreMgr.ManagerFieldReads > 0,
                "ICalculationOptionsManager.CalculationOptionsField resolves fields directly without throwing NullReferenceException on element options.");
            var nreMissing = nreResult.Single(p => p.ParameterIdentity == "EpsType_Duration");
            Check(nreMissing.Availability == "SEMANTICS_UNRESOLVED" && nreMissing.Reason.StartsWith("CALC_OPTIONS_FIELD_LOOKUP_FAILED"),
                "Null calculation option field from manager without absence proof evaluates cleanly to SEMANTICS_UNRESOLVED.");

            // Per-field failure isolation on calculation options read
            var isoMgr = new TestNativeOptionsManager();
            isoMgr.Options.NumericalEngineTypeName = "CivilStormDynamicEngine";
            isoMgr.Options.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.5,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var throwingField = NativeTestField("CivilStormDynamicEngine_OutputIncrement", 1.0,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            throwingField.Throw = true;
            isoMgr.Options.Fields["CivilStormDynamicEngine_OutputIncrement"] = throwingField;
            var isoResult = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67, method, isoMgr);
            Check(isoResult.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep").Availability == "NATIVE_CONFIGURATION_OBSERVED",
                "Valid option field succeeds even when another field throws.");
            Check(isoResult.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_OutputIncrement").Availability == "SEMANTICS_UNRESOLVED" &&
                isoResult.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_OutputIncrement").Reason.Contains("System.InvalidOperationException"),
                "Throwing option field is isolated per-field to SEMANTICS_UNRESOLVED.");

            // Fallback to manager.ModelingElementField when CalculationOptionsField throws NRE
            var fallbackMgr = new TestNativeOptionsManagerModelingElementFieldFallback();
            fallbackMgr.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.75,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var fallbackRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerModelingElementFieldFallback).GetMethod("Element", new[] { typeof(int) }), fallbackMgr);
            var fallbackStep = fallbackRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(fallbackStep.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)fallbackStep.Value == 0.75 &&
                fallbackStep.SourceProvenance.Contains(".ModelingElementField(String)"),
                "Fallback to ModelingElementField succeeds when CalculationOptionsField throws NullReferenceException.");

            // Fallback to manager.SupportedFields() when both CalculationOptionsField and ModelingElementField throw NRE
            var sfMgr = new TestNativeOptionsManagerWithSupportedFields();
            sfMgr.Supported.Add(NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.5,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes));
            var sfRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithSupportedFields).GetMethod("Element", new[] { typeof(int) }), sfMgr);
            var sfStep = sfRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(sfStep.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)sfStep.Value == 0.5 &&
                sfStep.SourceProvenance.Contains(".SupportedFields()[Name=CivilStormDynamicEngine_CalcTimeStep]"),
                "SupportedFields enumeration successfully discovers option field when direct accessors throw NRE.");
            Check(sfStep.SupportedFieldsCount == 1 && sfStep.SupportedFieldsSample == "CivilStormDynamicEngine_CalcTimeStep" &&
                sfStep.ManagerRuntimeType == typeof(TestNativeOptionsManagerWithSupportedFields).FullName &&
                sfStep.ModelingElementType == "CalculationOptions" && sfStep.ElementTypeId == -719657520 &&
                sfStep.EngineTypeId == -719657520 && sfStep.EngineLookupResult == "NOT_CHECKED",
                "Bounded diagnostic properties correctly captured on exact SupportedFields match.");
            var sfMissing = sfRes.Single(p => p.ParameterIdentity == "EpsType_Duration");
            Check(sfMissing.Availability == "NOT_AVAILABLE" && sfMissing.Reason == "FIELD_NOT_AVAILABLE" &&
                sfMissing.SourceProvenance.Contains("FIELD_NOT_IN_COLLECTION") && sfMissing.SupportedFieldsCount == 1,
                "Field absent from SupportedFields collection cleanly evaluates to NOT_AVAILABLE with provenance.");

            // All accessors throwing without SupportedFields classifies as lookup failure with full provenance
            var allThrowMgr = new TestNativeOptionsManagerThrowingAll();
            var allThrowRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerThrowingAll).GetMethod("Element", new[] { typeof(int) }), allThrowMgr);
            var allThrowParam = allThrowRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(allThrowParam.Availability == "SEMANTICS_UNRESOLVED" &&
                allThrowParam.Reason.StartsWith("CALC_OPTIONS_FIELD_LOOKUP_FAILED") &&
                allThrowParam.SourceProvenance.Contains("ICalculationOptionsManager.CalculationOptionsField:System.NullReferenceException") &&
                allThrowParam.SourceProvenance.Contains("IModelingElementManager.ModelingElementField:System.NullReferenceException") &&
                allThrowParam.SourceProvenance.Contains("ICalculationOptions.CalculationOptionsField:System.NullReferenceException"),
                "When all accessors throw without SupportedFields, failure is classified as lookup failure and preserves exact exception provenance.");

            // Metadata getter failure on FieldType
            var ftRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithFieldDouble).GetMethod("Element", new[] { typeof(int) }),
                new TestNativeOptionsManagerWithFieldDouble(new TestNativeFieldThrowingFieldType()));
            var ftParam = ftRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(ftParam.Availability == "SEMANTICS_UNRESOLVED" &&
                ftParam.Reason.Contains("CALC_OPTIONS_METADATA_UNAVAILABLE") &&
                ftParam.SourceProvenance.Contains("IField.FieldType:System.NullReferenceException"),
                "Exception in FieldType getter is isolated per-field to SEMANTICS_UNRESOLVED with metadata provenance.");

            // Metadata getter failure on NumericalEngineType
            var netRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithFieldDouble).GetMethod("Element", new[] { typeof(int) }),
                new TestNativeOptionsManagerWithFieldDouble(new TestNativeFieldThrowingEngineType()));
            var netParam = netRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(netParam.Availability == "SEMANTICS_UNRESOLVED" &&
                netParam.Reason.Contains("CALC_OPTIONS_METADATA_UNAVAILABLE") &&
                netParam.SourceProvenance.Contains("IFieldType.NumericalEngineType:System.NullReferenceException"),
                "Exception in NumericalEngineType getter is isolated per-field to SEMANTICS_UNRESOLVED with metadata provenance.");

            // Metadata engine mismatch
            var mismatchField = new TestNativeFieldWithMetadata
            {
                Name = "CivilStormDynamicEngine_CalcTimeStep",
                RawValue = 0.5,
                WorkingUnitIndex = Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes,
                FieldType = new TestNativeFieldTypeItem { NumericalEngineType = new TestNativeEngineTypeItem { Name = "DelawareProductEngine" } }
            };
            var mmRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithFieldDouble).GetMethod("Element", new[] { typeof(int) }),
                new TestNativeOptionsManagerWithFieldDouble(mismatchField));
            var mmParam = mmRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(mmParam.Availability == "NOT_AVAILABLE" &&
                mmParam.Reason == "PARAMETER_ENGINE_NOT_APPLICABLE" &&
                mmParam.SourceProvenance.Contains("FieldEngine=DelawareProductEngine") &&
                mmParam.SourceProvenance.Contains("TargetEngine=CivilStormDynamicEngine"),
                "Metadata engine mismatch cleanly marks PARAMETER_ENGINE_NOT_APPLICABLE and preserves competing engine names.");

            // Genuine numeric zero vs missing
            var zeroMgr = new TestNativeOptionsManagerWithNreOptions();
            zeroMgr.Fields["CivilStormDynamicEngine_CalcTimeStep"] = NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.0,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);
            var zeroRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithNreOptions).GetMethod("Element", new[] { typeof(int) }), zeroMgr);
            var zeroParam = zeroRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(zeroParam.Availability == "NATIVE_CONFIGURATION_OBSERVED" && (double)zeroParam.Value == 0.0,
                "Genuine numeric zero on calculation options field is preserved as 0.0, distinct from missing data.");

            // P17-S3R-F2: Empty SupportedFields collection
            var emptyMgr = new TestNativeOptionsManagerWithEmptySupportedFields();
            var emptyRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithEmptySupportedFields).GetMethod("Element", new[] { typeof(int) }), emptyMgr);
            var emptyStep = emptyRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(emptyStep.Availability == "SEMANTICS_UNRESOLVED" && emptyStep.Reason.StartsWith("CALC_OPTIONS_FIELD_LOOKUP_FAILED") &&
                emptyStep.SupportedFieldsCount == 0 && emptyStep.SourceProvenance.Contains("COLLECTION_EMPTY"),
                "Empty SupportedFields collection without authoritative absence proof evaluates cleanly to SEMANTICS_UNRESOLVED with COLLECTION_EMPTY provenance.");

            // P17-S3R-F2: Null SupportedFields collection
            var nullSfMgr = new TestNativeOptionsManagerWithNullSupportedFields();
            var nullSfRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithNullSupportedFields).GetMethod("Element", new[] { typeof(int) }), nullSfMgr);
            var nullSfStep = nullSfRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(nullSfStep.Availability == "SEMANTICS_UNRESOLVED" &&
                nullSfStep.Reason == "CALC_OPTIONS_ENUMERATION_UNAVAILABLE:COLLECTION_NULL" &&
                nullSfStep.SupportedFieldsCount == null && nullSfStep.SourceProvenance.Contains("COLLECTION_NULL"),
                "Null SupportedFields collection evaluates to SEMANTICS_UNRESOLVED with CALC_OPTIONS_ENUMERATION_UNAVAILABLE:COLLECTION_NULL.");

            // P17-S3R-F2: Throwing SupportedFields collection
            var throwSfMgr = new TestNativeOptionsManagerThrowingSupportedFields();
            var throwSfRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerThrowingSupportedFields).GetMethod("Element", new[] { typeof(int) }), throwSfMgr);
            var throwSfStep = throwSfRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(throwSfStep.Availability == "SEMANTICS_UNRESOLVED" &&
                throwSfStep.Reason.StartsWith("CALC_OPTIONS_ENUMERATION_UNAVAILABLE:EXCEPTION:System.InvalidOperationException") &&
                !throwSfStep.Reason.Contains("SECRET") && !throwSfStep.SourceProvenance.Contains("SECRET"),
                "Throwing SupportedFields collection isolates exception to CALC_OPTIONS_ENUMERATION_UNAVAILABLE without leaking secret text.");

            // P17-S3R-F2: Manager engine/type ID mismatch
            var mismatchIdMgr = new TestNativeOptionsManagerWithEngineMismatch();
            var mismatchIdRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithEngineMismatch).GetMethod("Element", new[] { typeof(int) }), mismatchIdMgr);
            var mismatchIdStep = mismatchIdRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(mismatchIdStep.Availability == "SEMANTICS_UNRESOLVED" &&
                mismatchIdStep.Reason == "MANAGER_ENGINE_TYPE_ID_MISMATCH:ManagerTypeID=1633152845,EngineTypeID=-719657520" &&
                mismatchIdStep.ElementTypeId == 1633152845 && mismatchIdStep.EngineTypeId == -719657520 &&
                mismatchIdStep.ModelingElementType == "CalculationOptions",
                "Manager engine type ID mismatch fails closed to MANAGER_ENGINE_TYPE_ID_MISMATCH with exact IDs recorded.");

            // P17-S3R-F2: Engine-specific field-type lookup success and failure via INumericalEngineType
            var engineLookupDs = new TestNativeDatasetWithEngineLookup();
            engineLookupDs.DataSetType.Engine.ExistsFunc = name => name == "CivilStormDynamicEngine_CalcTimeStep";
            var engineLookupMgr = new TestNativeOptionsManagerWithSupportedFields();
            engineLookupMgr.Supported.Add(NativeTestField("CivilStormDynamicEngine_CalcTimeStep", 0.5,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes));
            var elRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(engineLookupDs,
                new TestNativeScenario(), 1, "CivilStormDynamicEngine", 67,
                typeof(TestNativeOptionsManagerWithSupportedFields).GetMethod("Element", new[] { typeof(int) }), engineLookupMgr);
            var elStep = elRes.Single(p => p.ParameterIdentity == "CivilStormDynamicEngine_CalcTimeStep");
            Check(elStep.Availability == "NATIVE_CONFIGURATION_OBSERVED" && elStep.EngineLookupResult == "EXISTS" &&
                elStep.SourceProvenance.Contains("EngineFieldExists=True"),
                "CalculationOptionsFieldTypeExists returning true records EXISTS diagnostic.");
            var elAbsentStep = elRes.Single(p => p.ParameterIdentity == "EpsType_Duration");
            Check(elAbsentStep.Availability == "NOT_AVAILABLE" && elAbsentStep.Reason == "FIELD_NOT_AVAILABLE" &&
                elAbsentStep.EngineLookupResult == "DOES_NOT_EXIST" &&
                elAbsentStep.SourceProvenance.Contains("INumericalEngineType.CalculationOptionsFieldTypeExists:FALSE"),
                "CalculationOptionsFieldTypeExists returning false authoritatively proves field absent with DOES_NOT_EXIST diagnostic.");

            // P17-S3R-F3: Authoritative engine selection when CivilStormDynamicEngine is first in discovered engines
            var f3Ds = new TestMapDataset();
            var f3Map = ScenarioAlternativeCapabilityMapper.Map(f3Ds, null, null, null);
            Check(f3Map.CalculationOptionsRead.EngineTypeName == "DelawareProductEngine" &&
                f3Map.CalculationOptionsRead.Classification == "MAPPED_AND_READ",
                "Authoritative GetMainNumericalEngineTypeName selects DelawareProductEngine even when CivilStormDynamicEngine appears first in discovered engines.");
            Check(f3Ds.ScenarioManager.EngineNameRequested == "DelawareProductEngine" &&
                f3Ds.ScenarioManager.ScenarioElem.EngineNameRequested == "DelawareProductEngine",
                "Manager and scenario options ID are requested using authoritative DelawareProductEngine name.");
            Check(f3Map.CalculationOptionsRead.NumericalEngineTypes.Length == 2 &&
                f3Map.CalculationOptionsRead.NumericalEngineTypes[0].Name == "CivilStormDynamicEngine" &&
                f3Map.CalculationOptionsRead.NumericalEngineTypes[1].Name == "DelawareProductEngine",
                "Discovered engine inventory preserves both engines without conflating main-engine identity.");

            // Contradictory engine identity fails closed without fallback to FirstOrDefault()
            var f3ContraDs = new TestMapDataset();
            f3ContraDs.DataSetType.MainEngine = "ContradictoryEngine";
            var f3ContraMap = ScenarioAlternativeCapabilityMapper.Map(f3ContraDs, null, null, null);
            Check(f3ContraMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE" &&
                f3ContraMap.CalculationOptionsRead.EngineTypeName == null &&
                f3ContraDs.ScenarioManager.EngineNameRequested == null,
                "Contradictory engine identity fails closed with zero fallback to FirstOrDefault().");

            // Blank engine name fails closed
            var f3BlankDs = new TestMapDataset();
            f3BlankDs.DataSetType.MainEngine = "   ";
            var f3BlankMap = ScenarioAlternativeCapabilityMapper.Map(f3BlankDs, null, null, null);
            Check(f3BlankMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE" &&
                f3BlankMap.CalculationOptionsRead.EngineTypeName == null &&
                f3BlankDs.ScenarioManager.EngineNameRequested == null,
                "Blank engine name fails closed with zero fallback.");

            // Missing DomainDataSetType owner fails closed
            var f3NullOwnerDs = new TestMapDataset { DataSetType = null };
            var f3NullOwnerMap = ScenarioAlternativeCapabilityMapper.Map(f3NullOwnerDs, null, null, null);
            Check(f3NullOwnerMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE" &&
                f3NullOwnerMap.CalculationOptionsRead.EngineTypeName == null,
                "Null DomainDataSetType owner fails closed.");

            // Missing GetMainNumericalEngineTypeName method fails closed
            var f3MissingMethodMap = ScenarioAlternativeCapabilityMapper.Map(new TestMapDatasetWithObjectDst(new object()), null, null, null);
            Check(f3MissingMethodMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE",
                "Missing GetMainNumericalEngineTypeName method fails closed.");

            // Wrong method signature (returns int) fails closed
            var f3BadSigMap = ScenarioAlternativeCapabilityMapper.Map(new TestMapDatasetWithObjectDst(new TestNativeDataSetTypeBadSignature()), null, null, null);
            Check(f3BadSigMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE",
                "Unsupported GetMainNumericalEngineTypeName signature fails closed.");

            // Invocation exception fails closed
            var f3ThrowDs = new TestMapDataset();
            f3ThrowDs.DataSetType.ThrowOnGetMain = true;
            var f3ThrowMap = ScenarioAlternativeCapabilityMapper.Map(f3ThrowDs, null, null, null);
            Check(f3ThrowMap.CalculationOptionsRead.Classification == "NOT_AVAILABLE" &&
                f3ThrowMap.CalculationOptionsRead.EngineTypeName == null,
                "Invocation exception in GetMainNumericalEngineTypeName fails closed with zero fallback.");

            // Manager engine mismatch fails closed to NOT_PROVEN in Map
            var f3MgrMismatchDs = new TestMapDataset();
            f3MgrMismatchDs.ScenarioManager.CalcOptionsMgr.NumericalEngineTypeName = "MismatchEngine";
            var f3MgrMismatchMap = ScenarioAlternativeCapabilityMapper.Map(f3MgrMismatchDs, null, null, null);
            Check(f3MgrMismatchMap.CalculationOptionsRead.Classification == "NOT_PROVEN",
                "Manager NumericalEngineTypeName mismatch fails closed to NOT_PROVEN.");

            // Options element engine mismatch fails closed to NOT_PROVEN in Map
            var f3OptMismatchDs = new TestMapDataset();
            f3OptMismatchDs.ScenarioManager.CalcOptionsMgr.ElementObj.NumericalEngineTypeName = "MismatchEngine";
            var f3OptMismatchMap = ScenarioAlternativeCapabilityMapper.Map(f3OptMismatchDs, null, null, null);
            Check(f3OptMismatchMap.CalculationOptionsRead.Classification == "NOT_PROVEN",
                "Options element NumericalEngineTypeName mismatch fails closed to NOT_PROVEN.");

            // Manager engine mismatch in ReadNativeCalculationParameters
            var f3ReaderMismatchRes = GravityNetworkReadMapper.ReadNativeCalculationParameters(new TestNativeDatasetMultiEngine(),
                new TestNativeScenario(), 1, "DelawareProductEngine", 67, method,
                new TestMapCalculationOptionsManager { NumericalEngineTypeName = "CivilStormDynamicEngine" });
            Check(f3ReaderMismatchRes.Single().Availability == "SEMANTICS_UNRESOLVED" &&
                f3ReaderMismatchRes.Single().Reason == "MANAGER_ENGINE_MISMATCH" &&
                f3ReaderMismatchRes.Single().SourceProvenance.Contains("ManagerEngine=CivilStormDynamicEngine; TargetEngine=DelawareProductEngine"),
                "Manager engine mismatch fails closed to SEMANTICS_UNRESOLVED with MANAGER_ENGINE_MISMATCH.");

            // Reconciliation of exact 20 approved native calculation option fields
            var expected20Catalog = new[]
            {
                "CivilStormDynamicEngine_OutputIncrement",
                "CivilStormDynamicEngine_CalcTimeStep",
                "CivilStormDynamicEngine_PatternSetupID",
                "TimeAnalysisType",
                "DurationType",
                "StartType",
                "BaseDate",
                "AnalysisStartTime",
                "EpsType_Duration",
                "EpsType_HydraulicTimeStep",
                "EpsType_ReportingTimeStep",
                "EpsType_HydrologicTimeStep",
                "SwmmRoutingMethod",
                "SwmmCalcOptionsType_RoutingTimeStep",
                "SwmmCalcOptionsType_DryStep",
                "SwmmCalcOptionsType_SwmmOutputIncrement",
                "SwmmCalcOptionsType_AnalysisEndDate",
                "SwmmCalcOptionsType_AnalysisEndTime",
                "SwmmCalcOptionsType_ReportStartDate",
                "SwmmCalcOptionsType_ReportStartTime"
            };
            Check(GravityNetworkReadMapper.NativeOptionFields.Count == 20 &&
                GravityNetworkReadMapper.NativeOptionFields.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(expected20Catalog.OrderBy(k => k, StringComparer.Ordinal)),
                "Exact approved 20-target calculation options catalog is preserved without missing or invented entries.");

            // DateTime calculation option fields resolve to NOT_APPLICABLE units and PROVEN classification
            var dateOptionNames = new[]
            {
                "BaseDate",
                "AnalysisStartTime",
                "SwmmCalcOptionsType_AnalysisEndDate",
                "SwmmCalcOptionsType_AnalysisEndTime",
                "SwmmCalcOptionsType_ReportStartDate",
                "SwmmCalcOptionsType_ReportStartTime"
            };
            foreach (var dateField in dateOptionNames)
            {
                Check(GravityNetworkReadMapper.NativeOptionFields[dateField] == false,
                    dateField + " is classified as not requiring engineering units (NOT_APPLICABLE).");
                var dtVal = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var testField = NativeTestField(dateField, dtVal);
                var pDate = GravityNetworkReadMapper.ReadNativeParameter("CalculationOptions", dateField, testField, 14, 1, new TestNativeScenario(), alternativeRequired: false);
                Check(pDate.Availability == "NATIVE_CONFIGURATION_OBSERVED" && pDate.SourceClassification == "PROVEN" &&
                    pDate.UnitAvailability == "NOT_APPLICABLE" && pDate.Unit == null && pDate.Reason == null &&
                    pDate.NativeValueType == "System.DateTime" && pDate.Value != null,
                    dateField + " returns PROVEN with NOT_APPLICABLE units for DateTime value.");
            }

            var quantFieldNoUnit = NativeTestField("EpsType_Duration", 86400.0, null);
            var pQuantNoUnit = GravityNetworkReadMapper.ReadNativeParameter("CalculationOptions", "EpsType_Duration", quantFieldNoUnit, 14, 1, new TestNativeScenario(), alternativeRequired: false);
            Check(pQuantNoUnit.Availability == "SEMANTICS_UNRESOLVED" && pQuantNoUnit.SourceClassification != "PROVEN" &&
                pQuantNoUnit.Reason == "QUANTITY_TYPE_OR_UNIT_UNPROVEN" && pQuantNoUnit.UnitAvailability == "NOT_AVAILABLE",
                "Quantity calculation option without unit metadata fails closed as QUANTITY_TYPE_OR_UNIT_UNPROVEN.");
        }
        private static void P17S2_OutfallObservationsAreNotDesignPolicy()
        {
            var flowField=NativeTestField("SystemFlows_KnownFlow",2.0,Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs);
            var scoped=new TestSystemFlowManager{Field=flowField};var flowRows=new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeKnownFlow(flowRows,scoped,new TestNativeScenario(),25,1);
            Check(flowRows.Single().SourceClassification=="PROVEN" && flowRows.Single().Unit.Dimension=="flow" && scoped.Scope=="SystemFlows",
                "Known flow uses exact SystemFlows scope with native units and scenario alternative.");
            flowField.RawValue=0.0;flowRows.Clear();GravityNetworkReadMapper.MapNativeKnownFlow(flowRows,scoped,new TestNativeScenario(),25,1);
            Check((double)flowRows.Single().Value==0 && flowRows.Single().Authority=="NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY" && flowField.Writes==0,
                "True zero known flow remains separate from approved service intent; no setters.");
            flowRows.Clear();GravityNetworkReadMapper.MapNativeKnownFlow(flowRows,scoped,new TestNativeScenario{Assigned=99},25,1);
            Check(flowRows.Single().SourceClassification!="PROVEN","Stale known-flow alternative fails closed.");
            flowField.WorkingUnitIndex=null;flowRows.Clear();GravityNetworkReadMapper.MapNativeKnownFlow(flowRows,scoped,new TestNativeScenario(),25,1);
            Check(flowRows.Single().SourceClassification!="PROVEN","Known flow without native units is not qualified.");
            flowField.Name="WRONG_FIELD";flowRows.Clear();GravityNetworkReadMapper.MapNativeKnownFlow(flowRows,scoped,new TestNativeScenario(),25,1);
            Check(flowRows.Single().Reason=="FIELD_IDENTITY_MISMATCH","Scoped lookup cannot substitute an unrelated field.");
            var typedManager=new TestNativeManager();typedManager.Fields["BoundaryConditionType"]=NativeTestField("BoundaryConditionType",0);
            var boundaryRows=new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeElement(boundaryRows,typedManager,new TestTypedBoundaryElement(),new TestNativeScenario(),25,1,true);
            Check(boundaryRows.Single(p=>p.ParameterIdentity=="BoundaryConditionType").NativeEnumName=="FreeOutfallType",
                "Matching native typed boundary enum qualifies raw code meaning, not real receiver approval.");
            boundaryRows.Clear();GravityNetworkReadMapper.MapNativeElement(boundaryRows,typedManager,new TestTypedBoundaryElement{BoundaryConditionType=TestBoundaryKind.Other},new TestNativeScenario(),25,1,true);
            Check(boundaryRows.Single(p=>p.ParameterIdentity=="BoundaryConditionType").NativeEnumName==null,
                "Mismatched typed enum cannot relabel observed raw boundary code.");
            var manager = new TestNativeManager();
            manager.Fields["Physical_OutfallInvertElevation"] = NativeTestField("Physical_OutfallInvertElevation", 91.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet);
            manager.Fields["UserDefinedTwType_UserDefinedTailwater"] = NativeTestField("UserDefinedTwType_UserDefinedTailwater", 92.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet);
            manager.Fields["BoundaryCondition_HasTidalGate"] = NativeTestField("BoundaryCondition_HasTidalGate", true);
            var result = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeElement(result, manager, new TestNativeElement(), new TestNativeScenario(), 25, 1, true);
            Check((double)result.Single(p => p.ParameterIdentity == "Physical_OutfallInvertElevation").Value == 91 &&
                (double)result.Single(p => p.ParameterIdentity == "UserDefinedTwType_UserDefinedTailwater").Value == 92,
                "TEST_ONLY invert and active user-defined native tailwater remain separate observations.");
            Check(result.Single(p => p.ParameterIdentity == "BoundaryCondition_HasTidalGate").Value is bool gate && gate,
                "Native boolean configuration type is preserved.");
            Check(result.All(p => p.Authority == "NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY") &&
                result.Single(p => p.ParameterIdentity == "NativeProtectedDesignBoundary").Availability == "NOT_EXPORTED" && manager.Fields.Values.All(f => f.Writes == 0),
                "No invert, tailwater or tidal gate becomes an engineer-approved protected boundary; zero native writes.");

            // P17-S3: ArgumentException isolation on element manager
            var throwMgr = new TestNativeManager { ThrowOnMissing = true };
            throwMgr.Fields["Physical_OutfallInvertElevation"] = NativeTestField("Physical_OutfallInvertElevation", 88.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet);
            var throwResult = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeElement(throwResult, throwMgr, new TestNativeElement(), new TestNativeScenario(), 25, 1, true);
            var observedInvert = throwResult.Single(p => p.ParameterIdentity == "Physical_OutfallInvertElevation");
            Check(observedInvert.Availability == "NATIVE_CONFIGURATION_OBSERVED" && observedInvert.SourceClassification == "PROVEN" && (double)observedInvert.Value == 88.0,
                "Present field succeeds even when all other missing fields throw ArgumentException.");
            var missingField = throwResult.First(p => p.ParameterIdentity == "InfiltrationLoadType");
            Check(missingField.Availability == "NOT_AVAILABLE" && missingField.Reason == "FIELD_NOT_AVAILABLE" && missingField.Value == null && missingField.SourceClassification == "NOT_PROVEN",
                "ArgumentException is isolated per field, does not create a successful observation or fabricated zero, and marks missing state cleanly.");
            Check(throwResult.All(p => p.Reason == null || !p.Reason.Contains("System.ArgumentException")),
                "No unhandled ArgumentException leaks into parameter availability or error classification.");

            // Verified native field identity mapping for Physical_OutfallInvertElevation -> Physical_InvertElevation
            var nativeInvertField = NativeTestField("Physical_InvertElevation", 91.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet);
            var mappedInvertResult = GravityNetworkReadMapper.ReadNativeParameter("Outfall", "Physical_OutfallInvertElevation",
                nativeInvertField, 25, 1, new TestNativeScenario());
            Check(mappedInvertResult.Availability == "NATIVE_CONFIGURATION_OBSERVED" && mappedInvertResult.SourceClassification == "PROVEN" &&
                (double)mappedInvertResult.Value == 91.0 && mappedInvertResult.ParameterIdentity == "Physical_OutfallInvertElevation" &&
                mappedInvertResult.SourceProvenance.Contains("IField.Name=Physical_InvertElevation") &&
                mappedInvertResult.SourceProvenance.Contains("TargetParameter=Physical_OutfallInvertElevation"),
                "Native Physical_InvertElevation correctly maps to Physical_OutfallInvertElevation target identity with complete provenance.");

            // Genuine outfall field identity mismatch remains fail-closed
            var mismatchedInvertField = NativeTestField("Physical_UnknownInvert", 91.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.Feet);
            var badInvertResult = GravityNetworkReadMapper.ReadNativeParameter("Outfall", "Physical_OutfallInvertElevation",
                mismatchedInvertField, 25, 1, new TestNativeScenario());
            Check(badInvertResult.Availability == "SEMANTICS_UNRESOLVED" && badInvertResult.Reason == "FIELD_IDENTITY_MISMATCH",
                "Unrecognized field name remains FIELD_IDENTITY_MISMATCH and does not produce a valid observation.");
        }
        private static void P17S2_SanitaryRowsPreserveNativeAssociation()
        {
            var manager = new TestNativeManager(); manager.Fields["SanitaryLoads"] = NativeTestField("SanitaryLoads", null);
            var collection = new TestNativeCollection(); var field = NativeTestField("SanitaryPatternLoadType_SanitaryBaseFlow", null,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs);
            field.Values = new Hashtable { { 7, 0.0 }, { 9, 1.5 } }; collection.Fields[field.Name] = field;
            var result = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.ReadNativeSanitaryCollection(result, manager, new TestNativeElement { SanitaryLoads = collection }, new TestNativeScenario(), 101, 1);
            var rows = result.Where(p => p.ParameterIdentity == field.Name).OrderBy(p => p.NativeRowKey).ToArray();
            Check(rows.Length == 2 && rows[0].NativeRowKey == "7" && (double)rows[0].Value == 0 && rows[1].NativeRowKey == "9" &&
                rows.All(p => p.SourceId == 101 && p.CollectionIdentity == "SanitaryLoads" && p.AlternativeId == 13 && p.Unit.Symbol == "ft3/s"),
                "TEST_ONLY native row keys, owning element/alternative and raw sanitary flow units preserved; no aggregation.");
            Check(rows.All(p => p.Interpretation.Contains("NOT_CALCULATED_DESIGN_LOADING")) && field.Writes == 0, "Native collection values never become invented design loading.");
            field.Values = new Hashtable(); for (int i = 0; i <= 1000; i++) field.Values[i] = 0.0;
            result.Clear(); GravityNetworkReadMapper.ReadNativeSanitaryCollection(result, manager, new TestNativeElement { SanitaryLoads = collection }, new TestNativeScenario(), 101, 1);
            Check(result.Any(p => p.Reason == "COLLECTION_LIMIT_EXCEEDED") && !result.Any(p => p.NativeRowKey != null), "Oversized collection remains unresolved, without truncation masquerading as complete data.");

            // P17-S3: Missing parent collection throws ArgumentException on manager, cleanly resolves to COLLECTION_NOT_AVAILABLE
            var missingParentMgr = new TestNativeManager { ThrowOnMissing = true };
            var resMissingParent = new List<NativeSourceParameter>();
            GravityNetworkReadMapper.ReadNativeSanitaryCollection(resMissingParent, missingParentMgr, new TestNativeElement { SanitaryLoads = collection }, new TestNativeScenario(), 101, 1);
            Check(resMissingParent.Single().Availability == "NOT_AVAILABLE" && resMissingParent.Single().Reason == "COLLECTION_NOT_AVAILABLE",
                "Missing collection parent on manager resolves to COLLECTION_NOT_AVAILABLE without unhandled ArgumentException.");
        }
        private static void P17S2_LegacyCompatibilityAndFingerprint()
        {
            var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(System.IO.File.ReadAllText(System.IO.Path.Combine(Repository(), "docs/evidence/p12/p12a_network_read.json")));
            Check(raw.SourceData == null, "Historical DTO has explicitly unexported supplemental data, not zero observations.");
            var service = new GravityNetworkIngestionService(); var before = service.Ingest(raw, new string('A', 64), schemaVersion: 2);
            raw.SourceData = new NativeSourceData { Parameters = new List<NativeSourceParameter> { NativeTestRead(NativeTestField("InfiltrationAndInflow_InfiltrationFlow", 0.0,
                Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs)) } };
            var after = service.Ingest(raw, new string('A', 64), schemaVersion: 2);
            Check(before.NetworkFingerprint == after.NetworkFingerprint && before.SchemaVersion == after.SchemaVersion, "Supplemental source data does not change accepted network fingerprint/schema.");
            raw.SourceData.Parameters[0].Unit.Symbol = "TEST_ONLY_MUTATED"; var detached = after.SourceData; detached.Parameters.Clear();
            Check(after.SourceData.Parameters.Count == 1 && after.SourceData.Parameters[0].Unit.Symbol == "ft3/s", "Ingestion snapshot is detached from DTO and caller mutations.");
            var reopened = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(raw));
            Check(reopened.SourceData.Parameters[0].Value != null && Convert.ToDouble(reopened.SourceData.Parameters[0].Value) == 0,
                "New DTO round trip preserves native zero separately from missing supplemental data.");
        }
        private static void P17S2_DeterministicCopiesAndUnsafeValues()
        {
            var data = new NativeSourceData { Parameters = new List<NativeSourceParameter> {
                new NativeSourceParameter { Category = "Loading", SourceId = 202, ParameterIdentity = "B" },
                new NativeSourceParameter { Category = "Loading", SourceId = 101, ParameterIdentity = "A" } } };
            var first = GravityExecutionPlanBuilder.Json(data.Copy()); data.Parameters.Reverse();
            Check(first == GravityExecutionPlanBuilder.Json(data.Copy()), "Supplemental observation ordering is deterministic.");
            var unsafeValue = new NativeSourceParameter { Value = new Dictionary<string, object> { { "TEST_ONLY", new object() } }, SourceClassification = "PROVEN" }.Copy();
            Check(unsafeValue.Value == null && unsafeValue.Availability == "SEMANTICS_UNRESOLVED" && unsafeValue.SourceClassification != "PROVEN",
                "Copies cannot leak mutable or live vendor objects from serialized source DTOs.");
            var unknown = new NativeSourceParameter { Category = "Loading", ParameterIdentity = "TEST_ONLY_UNKNOWN", Value = 0.0,
                Availability = "NATIVE_CONFIGURATION_OBSERVED", SourceClassification = "PROVEN" }.Copy();
            Check(unknown.SourceClassification == "NOT_PROVEN" && unknown.Availability == "SEMANTICS_UNRESOLVED" &&
                unknown.Reason == "UNRECOGNIZED_STORED_NATIVE_PARAMETER",
                "Stored DTO input cannot promote an unknown parameter to a proven source observation at the copy/ingestion boundary.");

            // Verify exactly 48 unique declared targets (18 loading, 20 calculation options, 10 outfall).
            var loadingKeys = GravityNetworkReadMapper.TargetFieldKeys("Loading");
            var optionKeys = GravityNetworkReadMapper.TargetFieldKeys("CalculationOptions");
            var outfallKeys = GravityNetworkReadMapper.TargetFieldKeys("Outfall");
            Check(loadingKeys.Count == 18 && loadingKeys.Distinct().Count() == 18 && loadingKeys.Contains("SystemFlows_KnownFlow"), "Exactly 18 unique native loading targets including observed known flow.");
            Check(optionKeys.Count == 20 && optionKeys.Distinct().Count() == 20, "Exactly 20 unique native calculation options target identities.");
            Check(outfallKeys.Count == 10 && outfallKeys.Distinct().Count() == 10, "Exactly 10 unique native outfall target identities.");
            var all48 = loadingKeys.Concat(optionKeys).Concat(outfallKeys).ToList();
            Check(all48.Count == 48 && all48.Distinct().Count() == 48, "Exactly 48 unique target identities across all 3 categories.");
            Check(GravityNetworkReadMapper.RecognizedNativeSourceParameter("CalculationOptions", "NumericalEngineTypeName"),
                "NumericalEngineTypeName is recognized as diagnostic engine-association identity, tracked separately from the 48 target fields.");
        }
        private static void P17S2_UnprovenUnitAndNonFiniteFailClosed()
        {
            var field = NativeTestField("InfiltrationAndInflow_InfiltrationFlow", 0.0, Haestad.Support.Units.UnitConversionManager.UnitIndex.UnknownDimension);
            var p = NativeTestRead(field);
            Check(p.Value is double zero && zero == 0 && p.Unit == null && p.Availability == "SEMANTICS_UNRESOLVED" && p.SourceClassification != "PROVEN",
                "Unresolved dimension never silently defaults to length or a trusted flow unit.");
            field.WorkingUnitIndex = Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs; field.RawValue = double.NaN;
            p = NativeTestRead(field); Check(p.Value == null && p.Reason == "NON_FINITE_NATIVE_VALUE", "Nonfinite native input cannot become a valid observation.");
        }
    }
}

// TEST_ONLY unit/variant doubles with the exact public reflection contract; no Bentley assembly is loaded.
namespace Haestad.Support.Units
{
    public sealed class UnitConversionManager
    {
        public enum UnitIndex { Feet, Cfs, Minutes, SquareFeet, Years, UnknownDimension }
        public static UnitConversionManager Current { get; } = new UnitConversionManager();
        public TestUnit UnitAt(UnitIndex id) => id == UnitIndex.Feet ? new TestUnit("length", "feet", "ft") :
            id == UnitIndex.Cfs ? new TestUnit("flow", "cubic feet per second", "ft3/s") :
            id == UnitIndex.Minutes ? new TestUnit("time", "minutes", "min") :
            id == UnitIndex.SquareFeet ? new TestUnit("area", "square feet", "ft2") :
            id == UnitIndex.Years ? new TestUnit("time", "years", "yr") : new TestUnit(null, "TEST_ONLY_UNKNOWN", "?");
    }
    public sealed class TestUnit
    {
        public string Name { get; } public string ShortLabel { get; } public TestDimension Dimension { get; }
        public TestUnit(string dimension, string name, string symbol) { Name = name; ShortLabel = symbol; Dimension = new TestDimension(dimension); }
    }
    public sealed class TestDimension { public string Name { get; } public TestDimension(string name) { Name = name; } }
}
namespace Haestad.Domain.ModelingObjects.Dynamic
{
    public sealed class BoundaryConditionTypeObject_UserDefinedTwType { }
}
