using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // A bounded laboratory size-selection test; never a production engineering approval.
    //
    // Route actually executed by this phase: the effective section diameter field
    // (ConduitElement.ConduitDiameter). It is the only section lever that both survives
    // IProject.Save and reaches the primary solver, as recorded by
    // docs/evidence/farzanegan/20261029T105909Z-p14-eleven-conduit-diagnosis-and-resize.
    //
    // The alternative-scoped catalog-size selector (CatalogPipeType_CatalogPipeSize) and the
    // user-defined conduit type are NOT used. Eight independent read-only reopens
    // (20261029T064500Z-api-member-surface/*, 20261029T120000Z-userdefined-catalogrefs-cleared)
    // all persisted a CatalogConduit section with the original catalog size after save, so
    // neither route is technically verified and neither may be claimed as a catalog resize.
    // Their setters are retained below as audited, unreachable-by-this-path evidence of that
    // negative result; this Apply never calls them.
    internal static class FarzaneganHydraulicRemediationHost
    {
        internal const string Operation = "FarzaneganHydraulicRemediationTestOnly";
        internal const string LaunchSwitch = "--farzanegan-hydraulic-remediation-test-only";

        // Qualified by docs/evidence/farzanegan/20261029T060035Z-catalog-selector-qualification.
        internal const string CatalogSizeField = "CatalogPipeType_CatalogPipeSize";
        internal const string CatalogPipeIdField = "CatalogPipeType_CatalogPipeID";
        internal const string TenInchCatalogSizeGuid = "54919863-b228-4f75-b7e2-4e5e6a82a7f4";
        internal const string TwelveInchCatalogSizeGuid = "61634a43-f8e2-4469-a1eb-c47fedbf8c6a";
        internal const int TargetConduitId = 150;
        internal const int TargetCatalogPipeId = 125;
        internal const double TwelveInchInsideDiameterFt = 1.0;
        internal const string RouteName = "EFFECTIVE_SECTION_DIAMETER_ONLY";

        // The explicit eleven-conduit allowlist. SourceFt is the unmodified source-copy
        // effective diameter; TargetFt is the shared-catalog inside diameter of the smallest
        // catalog size that holds the unchanged outlet depth at or below 75% of D. Nothing
        // outside this table is writable, and no two entries may share an id.
        internal sealed class Target
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public double SourceFt { get; set; }
            public double TargetFt { get; set; }
            public string SourceSizeGuid { get; set; }
            public string TargetCatalogSize { get; set; }
        }

        internal static readonly Target[] Targets =
        {
            new Target { Id = 52,  Label = "CO-1",                  SourceFt = 0.6666666666666667,  TargetFt = 0.8333333333333335,  SourceSizeGuid = "5791999b-5fb5-45fd-9448-3e53a9d1ab77", TargetCatalogSize = "10 inch" },
            new Target { Id = 56,  Label = "CO-3",                  SourceFt = 1.0,                 TargetFt = 1.1666666666620003,  SourceSizeGuid = "61634a43-f8e2-4469-a1eb-c47fedbf8c6a", TargetCatalogSize = "14 inch" },
            new Target { Id = 63,  Label = "CO-6",                  SourceFt = 0.6666666666666667,  TargetFt = 0.8333333333333335,  SourceSizeGuid = "5791999b-5fb5-45fd-9448-3e53a9d1ab77", TargetCatalogSize = "10 inch" },
            new Target { Id = 66,  Label = "CO-8",                  SourceFt = 0.8333333333333335,  TargetFt = 1.0,                 SourceSizeGuid = "54919863-b228-4f75-b7e2-4e5e6a82a7f4", TargetCatalogSize = "12 inch" },
            new Target { Id = 70,  Label = "CO-10",                 SourceFt = 1.1666666666620003,  TargetFt = 1.333333333328,      SourceSizeGuid = "4495c0c2-91ca-4394-935e-19d55493e9b4", TargetCatalogSize = "16 inch" },
            new Target { Id = 74,  Label = "CO-12",                 SourceFt = 1.5000000000000002,  TargetFt = 1.66666666666,       SourceSizeGuid = "7c47ab83-71fe-446b-bdee-f95b905367d8", TargetCatalogSize = "20 inch" },
            new Target { Id = 99,  Label = "CO-24",                 SourceFt = 0.6666666666666667,  TargetFt = 0.8333333333333335,  SourceSizeGuid = "5791999b-5fb5-45fd-9448-3e53a9d1ab77", TargetCatalogSize = "10 inch" },
            new Target { Id = 150, Label = "CO-40",                 SourceFt = 0.8333333333333335,  TargetFt = 1.1666666666620003,  SourceSizeGuid = "54919863-b228-4f75-b7e2-4e5e6a82a7f4", TargetCatalogSize = "14 inch" },
            new Target { Id = 234, Label = "CO-14(1)",              SourceFt = 1.66666666666,       TargetFt = 1.833333333326,      SourceSizeGuid = "8ce9bc94-e13a-4321-b735-8909f7a48f76", TargetCatalogSize = "22 inch" },
            new Target { Id = 240, Label = "CO-39(2)(2)",           SourceFt = 0.8333333333333335,  TargetFt = 1.0,                 SourceSizeGuid = "54919863-b228-4f75-b7e2-4e5e6a82a7f4", TargetCatalogSize = "12 inch" },
            new Target { Id = 268, Label = "CO-55(2)(2)(2)(2)",     SourceFt = 0.6666666666666667,  TargetFt = 0.8333333333333335,  SourceSizeGuid = "5791999b-5fb5-45fd-9448-3e53a9d1ab77", TargetCatalogSize = "10 inch" }
        };

        // Exact allowlist membership: the element id and the requested diameter must both match
        // one table row. This is the only gate that lets a diameter reach the native setter.
        internal static bool IsAllowlistedDiameter(int id, double diameterFt)
        {
            foreach (var target in Targets)
                if (target.Id == id && Math.Abs(target.TargetFt - diameterFt) <= 1e-12) return true;
            return false;
        }

        internal static Target TargetFor(int id) => Targets.SingleOrDefault(t => t.Id == id);

        // Four P14 invert writes plus eleven allowlisted effective-diameter writes.
        internal static int ExpectedWrites(string op) => op == Operation ? 4 + Targets.Length : op == P14FarzaneganTestOnlyHost.Operation ? 4 : op == P14CatalogPointerPersistenceHost.Operation ? 2 : 0;

        internal sealed class Section
        {
            public int Id { get; set; }
            public double DiameterFt { get; set; }
            public string CatalogId { get; set; }
            public string CatalogSizeGuid { get; set; }
            public string CatalogSizeLabel { get; set; }
            public string CatalogSizeInsideDiameterFt { get; set; }
            public string SelectionField { get; set; }
            public string ConduitType { get; set; }
            public int ConduitTypeValue { get; set; }
            public string Shape { get; set; }
            public string ShapeLabel { get; set; }
            public bool UseLocalShapeLabel { get; set; }
            public string Material { get; set; }
            public double ManningsN { get; set; }
        }

        private static object Get(object instance, string name)
        {
            var property = instance.GetType().GetProperty(name);
            if (property == null || property.GetGetMethod() == null) throw new MissingMemberException(instance.GetType().FullName, name);
            return property.GetValue(instance, null);
        }

        private static int CatalogId(object conduit)
        {
            var method = conduit.GetType().GetMethod("ConduitTypeObject", Type.EmptyTypes);
            object variant = method?.Invoke(conduit, null);
            if (variant?.GetType().FullName != "Haestad.Domain.ModelingObjects.Dynamic.ConduitTypeObject_CatalogConduitType")
                throw new InvalidDataException("Native conduit must use the proven catalog type variant.");
            return (int)Get(variant, "CatalogPipeType_CatalogPipeID");
        }

        // Resolves the qualified alternative-scoped catalog-size field. Read-only until a caller writes.
        internal static object CatalogPipeIdFieldHandle(object dataset, object scenario)
        {
            int elementTypeId = GravityNetworkReadMapper.ResolveElementTypeId(
                GravityNetworkReadMapper.ResolveDomainElementType(), "ConduitElementManager");
            int alternativeTypeId = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            int alternativeId = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(scenario, alternativeTypeId);
            Type alternativeType = Contract("Haestad.Domain", "Haestad.Domain.IAlternative");
            object manager = dataset.GetType().GetMethod("AlternativeManager", new[] { typeof(int) })
                .Invoke(dataset, new object[] { alternativeTypeId });
            object alternative = manager?.GetType().GetMethod("Element", new[] { typeof(int) })
                .Invoke(manager, new object[] { alternativeId });
            if (alternative == null || !alternativeType.IsInstanceOfType(alternative))
                throw new InvalidOperationException("Physical alternative for catalog pipe ID is unavailable.");
            object field = alternativeType.GetMethod("AlternativeField", new[] { typeof(string), typeof(int) })
                .Invoke(alternative, new object[] { CatalogPipeIdField, elementTypeId });
            if (field == null || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != CatalogPipeIdField)
                throw new InvalidOperationException("Qualified catalog pipe-ID field is not resolvable.");
            return field;
        }

        internal static object CatalogSizeFieldHandle(object dataset, object scenario)
        {
            // Use the domain-owned Physical field: the alternative-only wrapper bypasses
            // the domain field's write path and is not a qualified catalog selection.
            object field=P14NativeConduitLabelFormatter.PhysicalField(P14FarzaneganTestOnlyHost.Manager(dataset),CatalogSizeField);
            if(field==null || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field,null))!=CatalogSizeField)
                throw new InvalidDataException("Physical catalog-size field unavailable.");
            return field;
        }

        // Reads the shared catalog size table natively. Read-only.
        internal static Dictionary<string, string[]> CatalogSizes(object dataset)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            Type supportType = Contract("Haestad.Domain", "Haestad.Domain.SupportElementType");
            object catalogType = WaterObjectsLifecycleHost.EnumMember(supportType, "CatalogConduitElementManager");
            Type supportManagerType = Contract("Haestad.Domain", "Haestad.Domain.ISupportElementManager");
            Type fieldType = Contract("Haestad.Support", "Haestad.Support.Support.IField");
            Type collectionType = Contract("Haestad.Domain", "Haestad.Domain.ICollectionFieldListManager");
            object catalogManager = dataset.GetType().GetMethod("SupportElementManager", new[] { typeof(int) })
                .Invoke(dataset, new object[] { Convert.ToInt32(catalogType, CultureInfo.InvariantCulture) });
            if (catalogManager == null) throw new InvalidOperationException("Catalog conduit support manager is unavailable.");
            object collection = supportManagerType.GetMethod("SupportElementField", new[] { typeof(string) })
                .Invoke(catalogManager, new object[] { "ClassSizeCollection" });
            object bound = collection.GetType().GetMethod("GetValue", new[] { typeof(int) })
                .Invoke(collection, new object[] { TargetCatalogPipeId });
            if (bound == null || !collectionType.IsInstanceOfType(bound))
                throw new InvalidOperationException("Catalog size collection is not bound to catalog pipe " + TargetCatalogPipeId + ".");
            var guids = Map(collectionType.GetMethod("Field", new[] { typeof(string) }).Invoke(bound, new object[] { "ClassSizeCollection_Guid" }));
            var labels = Map(collectionType.GetMethod("Field", new[] { typeof(string) }).Invoke(bound, new object[] { "ClassSizeCollection_Label" }));
            var diameters = Map(collectionType.GetMethod("Field", new[] { typeof(string) }).Invoke(bound, new object[] { "ClassSizeCollection_InsideDiameter" }));
            var mannings = Map(collectionType.GetMethod("Field", new[] { typeof(string) }).Invoke(bound, new object[] { "ClassSizeCollection_ManningsN" }));
            foreach (var pair in guids)
                map[pair.Value] = new[]
                {
                    labels.TryGetValue(pair.Key, out string label) ? label : null,
                    diameters.TryGetValue(pair.Key, out string diameter) ? diameter : null,
                    mannings.TryGetValue(pair.Key, out string n) ? n : null
                };
            if (map.Count == 0) throw new InvalidDataException("Shared catalog size table is empty or unreadable.");
            return map;
        }

        private static Dictionary<int, string> Map(object field)
        {
            var map = new Dictionary<int, string>();
            if (field == null) return map;
            var values = field.GetType().GetMethod("GetValues", Type.EmptyTypes)?.Invoke(field, null) as System.Collections.IDictionary;
            if (values == null) return map;
            foreach (System.Collections.DictionaryEntry entry in values)
                map[Convert.ToInt32(entry.Key, CultureInfo.InvariantCulture)] =
                    Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
            return map;
        }

        internal static Section[] Sections(object dataset, object sizeField, Dictionary<string, string[]> sizes)
        {
            object manager = P14FarzaneganTestOnlyHost.Manager(dataset);
            var read = sizeField.GetType().GetMethod("GetValue", new[] { typeof(int) });
            object conduitTypeField = manager.GetType().GetProperty("ConduitTypeField")?.GetValue(manager, null);
            var readType = conduitTypeField?.GetType().GetMethod("GetValue", new[] { typeof(int) });
            if (readType == null) throw new InvalidOperationException("Native ConduitType field readback is unavailable.");
            return P14FarzaneganTestOnlyHost.Snapshot(dataset).Select(row =>
            {
                object conduit = manager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(manager, new object[] { row.Id });
                int typeValue = Convert.ToInt32(readType.Invoke(conduitTypeField, new object[] { row.Id }), CultureInfo.InvariantCulture);
                if (typeValue != 0 && typeValue != 1) throw new InvalidDataException("Unknown ConduitType enumeration value for element " + row.Id + ": " + typeValue);
                bool isCatalog = typeValue == 1;
                string guid = isCatalog ? Convert.ToString(read.Invoke(sizeField, new object[] { row.Id }), CultureInfo.InvariantCulture) : null;
                string[] entry = null;
                if (isCatalog && !string.IsNullOrEmpty(guid)) sizes.TryGetValue(guid, out entry);
                return new Section
                {
                    Id = row.Id,
                    DiameterFt = Convert.ToDouble(Get(conduit, "ConduitDiameter"), CultureInfo.InvariantCulture),
                    CatalogId = isCatalog ? CatalogId(conduit).ToString(CultureInfo.InvariantCulture) : "USER_DEFINED",
                    CatalogSizeGuid = guid,
                    CatalogSizeLabel = isCatalog ? entry?[0] : "USER_DEFINED",
                    CatalogSizeInsideDiameterFt = isCatalog ? entry?[1] : null,
                    SelectionField = isCatalog ? CatalogSizeField : "ConduitType",
                    ConduitType = isCatalog ? "CatalogConduit" : "UserDefinedConduitType",
                    ConduitTypeValue = typeValue,
                    Shape = Convert.ToString(Get(conduit, "ConduitShape"), CultureInfo.InvariantCulture),
                    ShapeLabel = Convert.ToString(Get(conduit, "Physical_ConduitShapeLabel"), CultureInfo.InvariantCulture),
                    UseLocalShapeLabel = Convert.ToBoolean(Get(conduit, "Physical_UseLocalConduitShapeLabel"), CultureInfo.InvariantCulture),
                    Material = Convert.ToString(Get(conduit, "Physical_Material"), CultureInfo.InvariantCulture),
                    ManningsN = Convert.ToDouble(Get(conduit, "Physical_ManningsN"), CultureInfo.InvariantCulture)
                };
            }).ToArray();
        }

        internal static void Apply(object dataset, object scenario, HostRequest request, ModelReadEvidence evidence)
        {
            if (request.Operation != Operation || evidence.Diagnostics["P14NativeWriteReadbackVerified"] != "true" ||
                evidence.Diagnostics["PropertySetCount"] != "4") throw new InvalidDataException("Remediation requires exact verified P14 precondition.");
            object sizeField = CatalogSizeFieldHandle(dataset, scenario);
            var sizes = CatalogSizes(dataset);
            evidence.Diagnostics["CatalogSizeEntryCount"] = sizes.Count.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["RemediationRoute"] = RouteName;
            evidence.Diagnostics["RemediationTargetCount"] = Targets.Length.ToString(CultureInfo.InvariantCulture);

            // The shared catalog is only read here. Every target size must exist as a real
            // catalog entry with the inside diameter this table claims, so no size is invented.
            foreach (var target in Targets)
            {
                string[] entry = null;
                foreach (var pair in sizes)
                    if (pair.Value[0] == target.TargetCatalogSize) entry = pair.Value;
                if (entry == null) throw new InvalidDataException("Catalog size is absent from the shared catalog: " + target.TargetCatalogSize);
                if (Math.Abs(double.Parse(entry[1], CultureInfo.InvariantCulture) - target.TargetFt) > 1e-12)
                    throw new InvalidDataException("Catalog inside diameter identity changed for " + target.TargetCatalogSize);
                if (entry[2] != "0.01") throw new InvalidDataException("Catalog roughness changed for " + target.TargetCatalogSize);
            }

            var before = Sections(dataset, sizeField, sizes);
            evidence.Diagnostics["RemediationSectionsBeforeJson"] = RuntimeHostProtocol.Serializer().Serialize(before);

            // Fail-closed source verification: every allowlisted conduit must still carry its
            // unmodified catalog section, circular shape, GRVE material and n = 0.01 before any write.
            foreach (var target in Targets)
            {
                var row = before.Single(c => c.Id == target.Id);
                if (Math.Abs(row.DiameterFt - target.SourceFt) > 1e-9 || row.CatalogId != TargetCatalogPipeId.ToString(CultureInfo.InvariantCulture) ||
                    !string.Equals(row.CatalogSizeGuid, target.SourceSizeGuid, StringComparison.OrdinalIgnoreCase) ||
                    row.ConduitType != "CatalogConduit" || row.ConduitTypeValue != 1 ||
                    Math.Abs(row.ManningsN - 0.01) > 1e-12 || row.Material != "GRVE" || row.Shape != "CircularPipe")
                    throw new InvalidDataException("Allowlisted conduit " + target.Id + " differs from the qualified source section.");
                if (target.TargetFt <= target.SourceFt)
                    throw new InvalidDataException("Allowlisted conduit " + target.Id + " has no enlargement to apply.");
            }

            object manager = P14FarzaneganTestOnlyHost.Manager(dataset);
            int count = 4;
            foreach (var target in Targets)
            {
                object conduit = manager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(manager, new object[] { target.Id });
                if (conduit == null) throw new InvalidDataException("Allowlisted native conduit element is unavailable: " + target.Id);
                count++;
                evidence.Diagnostics["PropertySetCount"] = count.ToString(CultureInfo.InvariantCulture);
                AdapterWriteBinding.ApplyTestOnlyConduitElementDiameter(conduit, target.Id, target.TargetFt);
            }
            evidence.Diagnostics["RemediationEffectiveDiameterWrites"] = string.Join(";", Targets.Select(
                t => t.Id.ToString(CultureInfo.InvariantCulture) + "=" + t.TargetFt.ToString("R", CultureInfo.InvariantCulture)));

            var after = Sections(dataset, sizeField, sizes);
            evidence.Diagnostics["RemediationSectionsAfterJson"] = RuntimeHostProtocol.Serializer().Serialize(after);
            var failures = new List<string>();
            foreach (var target in Targets)
            {
                var observed = after.Single(c => c.Id == target.Id);
                var prior = before.Single(c => c.Id == target.Id);
                if (Math.Abs(observed.DiameterFt - target.TargetFt) > 1e-10) failures.Add("diameter-not-applied:" + target.Id);
                if (observed.ConduitType != prior.ConduitType || observed.ConduitTypeValue != prior.ConduitTypeValue) failures.Add("conduit-type-changed:" + target.Id);
                if (!string.Equals(observed.CatalogSizeGuid, prior.CatalogSizeGuid, StringComparison.OrdinalIgnoreCase)) failures.Add("catalog-selection-changed:" + target.Id);
                if (observed.Shape != prior.Shape || observed.Material != prior.Material ||
                    Math.Abs(observed.ManningsN - prior.ManningsN) > 1e-15) failures.Add("shape-material-or-roughness-changed:" + target.Id);
            }
            foreach (var row in after)
            {
                if (Targets.Any(t => t.Id == row.Id)) continue;
                if (RuntimeHostProtocol.Serializer().Serialize(row) != RuntimeHostProtocol.Serializer().Serialize(before.Single(c => c.Id == row.Id)))
                    throw new InvalidDataException("Unexpected section change on a non-allowlisted conduit: " + row.Id);
            }
            if (failures.Count != 0)
                throw new InvalidDataException("Allowlisted section readback failed: " + string.Join(",", failures));
            evidence.Diagnostics["RemediationSelectionReadback"] = string.Join("|", Targets.Select(t =>
                t.Id.ToString(CultureInfo.InvariantCulture) + ":" + after.Single(c => c.Id == t.Id).DiameterFt.ToString("R", CultureInfo.InvariantCulture) +
                "/" + after.Single(c => c.Id == t.Id).ConduitType));
            var endpoints = P14FarzaneganTestOnlyHost.Snapshot(dataset);
            if (RuntimeHostProtocol.Serializer().Serialize(endpoints) != evidence.Diagnostics["P14NativeAfterJson"])
                throw new InvalidDataException("Section resize changed endpoint inverts or inheritance.");
            evidence.Diagnostics["RemediationNativeReadbackVerified"] = "true";
        }

        // Read-only persisted selection snapshot for an independent reopen. No setter is reachable here.
        internal static Section[] PersistedSections(object dataset, object scenario) =>
            Sections(dataset, CatalogSizeFieldHandle(dataset, scenario), CatalogSizes(dataset));

        internal static string OutfallConfiguration(object dataset)
        {
            int type = GravityNetworkReadMapper.ResolveElementTypeId(GravityNetworkReadMapper.ResolveDomainElementType(), "OutfallElementManager");
            object manager = dataset.GetType().GetMethod("DomainElementManager", new[] { typeof(int) }).Invoke(dataset, new object[] { type });
            var rows = new List<object>();
            foreach (int id in new[] { 91, 163 })
            {
                object element = manager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(manager, new object[] { id });
                object raw = Get(element, "BoundaryConditionType");
                var method = element.GetType().GetMethod("BoundaryConditionTypeObject", Type.EmptyTypes);
                object variant = method == null ? null : method.Invoke(element, null);
                rows.Add(new
                {
                    Id = id, BoundaryValue = Convert.ToString(raw, CultureInfo.InvariantCulture),
                    BoundaryClrType = raw.GetType().FullName, NativeBoundaryObjectType = variant?.GetType().FullName,
                    PhysicalInvertFt = Get(element, "Physical_OutfallInvertElevation"),
                    Approval = "MODEL_CONFIGURATION_OBSERVATION_ONLY_RECEIVING_CONDITIONS_UNVERIFIED"
                });
            }
            return RuntimeHostProtocol.Serializer().Serialize(rows);
        }

        internal static void ReadExtended(object dataset, object project, ModelReadEvidence evidence)
        {
            var reader = new WaterObjectsResultReader();
            var results = new List<object>();
            foreach (var row in P14FarzaneganTestOnlyHost.Snapshot(dataset))
            {
                var depth = reader.ReadMetric(dataset, project, "DepthOut", new[] { "BoundingDepthResults_DepthOut" },
                    new[] { P16FarzaneganNativePilotHost.Engine }, new[] { "BoundingDepthResults" }, 1,
                    P16FarzaneganNativePilotHost.Engine, row.Id, false, "Depth at native conduit outlet", null, "ft");
                results.Add(new { Id = row.Id, Result = depth });
            }
            evidence.Diagnostics["NativeOutletDepthsJson"] = RuntimeHostProtocol.Serializer().Serialize(results);
            try { evidence.Diagnostics["NativeOutfallConfigurationJson"] = OutfallConfiguration(dataset); }
            catch (Exception error) { evidence.Diagnostics["NativeOutfallConfigurationStatus"] = "NOT_QUALIFIED:" + error.GetType().FullName; }
        }

        private static Type Contract(string assemblyName, string typeName)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assemblyName).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Vendor contract assembly is missing or ambiguous: " + assemblyName);
            return matches[0].GetType(typeName, true, false);
        }
    }
}
