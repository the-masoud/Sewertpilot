using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        public sealed class TestStormGroup { public TestNativeCollection StormEventsCollection { get; set; } }
        public sealed class TestStormGroups
        {
            public TestStormGroup Group { get; set; }
            public TestNativeField CollectionField;
            public List<int> ElementIDs() => new List<int> { 126 };
            public object Element(int id) => id == 126 ? Group : null;
            public object SupportElementField(string name) => name == "StormEventsCollection" ? CollectionField : null;
        }
        public sealed class TestStormDataset { public TestStormGroups StormEventElementManager { get; set; } }
        public sealed class TestScopedCatchmentManager
        {
            public object DomainElementField(string name) => null;
            public string LastScope;
            public readonly Dictionary<string, TestNativeField> Fields = new Dictionary<string, TestNativeField>();
            public object DomainElementField(string name, string scope)
            { LastScope = scope; return scope == "Hydrologic" && Fields.TryGetValue(name, out var f) ? f : null; }
        }
        private static List<NativeSourceParameter> LoadingInterpretationFixture(bool scaled, bool local)
        {
            var s = new TestNativeScenario();
            Func<string, object, Haestad.Support.Units.UnitConversionManager.UnitIndex?, NativeSourceParameter> raw = (name, value, unit) =>
                GravityNetworkReadMapper.ReadNativeParameter("Catchment", name, NativeTestField(name, value, unit), 30, 1, s);
            var rows = new List<NativeSourceParameter> {
                raw("Hydrologic_UseScaledArea",scaled,null),
                raw("Hydrologic_Area",0.0,Haestad.Support.Units.UnitConversionManager.UnitIndex.SquareFeet),
                raw("HMIGeometryScaledArea",100.0,Haestad.Support.Units.UnitConversionManager.UnitIndex.SquareFeet),
                raw("RainfallRunoff_UseLocalRainfall",local,null),raw("RainfallRunoff_StormEventID","local-event",null) };
            rows.Add(new NativeSourceParameter { Category="Rainfall",ParameterIdentity="SystemStormEventID",SourceId=13,ScenarioId=1,
                AlternativeTypeId=12,AlternativeId=13,ScenarioAssignedAlternativeId=13,Value="system-event",Availability="NATIVE_CONFIGURATION_OBSERVED",SourceClassification="PROVEN" });
            return rows;
        }
        private static void CatchmentLoading_ScopedFieldsAndSelectedArea()
        {
            var m = new TestScopedCatchmentManager(); var f = NativeTestField("Hydrologic_UseScaledArea", true);
            m.Fields[f.Name] = f;
            Check(ReferenceEquals(GravityNetworkReadMapper.CatchmentField(m, f.Name), f) && m.LastScope == "Hydrologic", "Exact missing selector resolved through native Hydrologic scope.");
            m.LastScope=null; Check(GravityNetworkReadMapper.CatchmentField(m,"TEST_ONLY_UNKNOWN")==null && m.LastScope==null, "No scope guesses for unrelated fields.");
            foreach (bool scaled in new[] { true,false })
            {
                var rows=LoadingInterpretationFixture(scaled,false); GravityNetworkReadMapper.ResolveNativeCatchmentLoading(rows,1);
                var area=rows.Single(p=>p.ParameterIdentity=="EffectiveArea");
                Check(area.SourceClassification=="PROVEN" && Convert.ToDouble(area.Value)==(scaled?100.0:0.0) && area.Unit.Dimension=="area",
                    "Native selector chooses geometric positive area or genuine zero user area without fallback substitution.");
                Check(area.Availability=="DERIVED_FROM_NATIVE_CONFIGURATION" && area.Authority=="NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY", "Effective area is derived observation, not engineering approval.");
            }
            Check(f.Writes==0,"Scoped field lookup invokes no native setter.");
        }
        private static void CatchmentLoading_EventInheritanceAndUncertainty()
        {
            foreach(bool local in new[]{true,false})
            {
                var rows=LoadingInterpretationFixture(true,local);GravityNetworkReadMapper.ResolveNativeCatchmentLoading(rows,1);
                Check((string)rows.Single(p=>p.ParameterIdentity=="EffectiveStormEventID").Value==(local?"local-event":"system-event"), "Explicit native local flag selects local or scenario-system event.");
            }
            var blank=LoadingInterpretationFixture(true,false);blank.Single(p=>p.ParameterIdentity=="RainfallRunoff_StormEventID").Value="";
            GravityNetworkReadMapper.ResolveNativeCatchmentLoading(blank,1);
            Check((string)blank.Single(p=>p.ParameterIdentity=="EffectiveStormEventID").Value=="system-event", "Blank unused local event does not erase global rainfall.");
            var missing=LoadingInterpretationFixture(true,false);missing.Single(p=>p.ParameterIdentity=="Hydrologic_UseScaledArea").SourceClassification="NOT_PROVEN";
            missing.Single(p=>p.Category=="Rainfall").AlternativeId=99;GravityNetworkReadMapper.ResolveNativeCatchmentLoading(missing,1);
            Check(missing.Single(p=>p.ParameterIdentity=="EffectiveArea").Value==null && missing.Single(p=>p.ParameterIdentity=="EffectiveStormEventID").Value==null,
                "Missing selector and stale system alternative cannot produce trusted effective inputs.");
            var units=LoadingInterpretationFixture(true,false);units.Single(p=>p.ParameterIdentity=="HMIGeometryScaledArea").Unit=new SourceUnitIdentity("length","Feet","feet","ft");
            GravityNetworkReadMapper.ResolveNativeCatchmentLoading(units,1);
            Check(units.Single(p=>p.ParameterIdentity=="EffectiveArea").Value==null,"Length units cannot be used as effective area.");
            var overrideBlank=LoadingInterpretationFixture(true,true);overrideBlank.Single(p=>p.ParameterIdentity=="RainfallRunoff_StormEventID").Value="";
            GravityNetworkReadMapper.ResolveNativeCatchmentLoading(overrideBlank,1);
            Check(overrideBlank.Single(p=>p.ParameterIdentity=="EffectiveStormEventID").Value==null,"Empty explicit local override cannot silently adopt system event.");
        }
        private static void CatchmentLoading_NestedStormEntriesPreserveIdentity()
        {
            var entries=new TestNativeCollection();var curve=new TestNativeCollection();
            var identifier=NativeTestField("StormEventsCollection_StormIdentifier",null);identifier.Values=new System.Collections.Hashtable{{0,"system-event"}};
            var period=NativeTestField("StormEventsCollection_ReturnEvent",null,Haestad.Support.Units.UnitConversionManager.UnitIndex.Years);period.Values=new System.Collections.Hashtable{{0,10.0}};
            var nested=NativeTestField("IDFCurve",curve);
            var time=NativeTestField("IDFCurve_IDFTime",null,Haestad.Support.Units.UnitConversionManager.UnitIndex.Minutes);time.Values=new System.Collections.Hashtable{{0,5.0},{1,10.0}};
            var intensity=NativeTestField("IDFCurve_IDFIntensity",null,Haestad.Support.Units.UnitConversionManager.UnitIndex.Cfs);intensity.Values=new System.Collections.Hashtable{{0,2.0},{1,1.0}};
            entries.Fields[identifier.Name]=identifier;entries.Fields[period.Name]=period;entries.Fields[nested.Name]=nested;
            curve.Fields[time.Name]=time;curve.Fields[intensity.Name]=intensity;
            var rows=new List<NativeSourceParameter>();
            var collectionField=NativeTestField("StormEventsCollection",entries);
            GravityNetworkReadMapper.MapNativeStormDefinitions(rows,new TestStormDataset{StormEventElementManager=new TestStormGroups{CollectionField=collectionField}},new TestNativeScenario(),1);
            Check(rows.Single(p=>p.ParameterIdentity==identifier.Name).SourceId==126 && rows.Single(p=>p.ParameterIdentity==identifier.Name).NativeRowKey=="0", "Storm-group and opaque zero-based event-row identities remain distinct.");
            Check(rows.Count(p=>p.CollectionIdentity=="StormEventsCollection[0].IDFCurve")==4 && rows.Where(p=>p.ParameterIdentity==time.Name).All(p=>p.Unit.Symbol=="min"),
                "Nested native curve rows keep event association, opaque keys and units.");
            Check((double)rows.Single(p=>p.ParameterIdentity==period.Name).Value==10.0 && identifier.Writes+period.Writes+nested.Writes+time.Writes+intensity.Writes+collectionField.Writes==0,
                "Native return period observed without group-ID substitution or setter calls.");
            var missing=new List<NativeSourceParameter>();
            GravityNetworkReadMapper.MapNativeStormDefinitions(missing,new TestStormDataset{StormEventElementManager=new TestStormGroups()},new TestNativeScenario(),1);
            Check(missing.Single().Reason=="STORM_GROUP_COLLECTION_UNAVAILABLE", "Missing native collection produces an explicit gap, not silent absent rainfall.");
        }
    }
}
