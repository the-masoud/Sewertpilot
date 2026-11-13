using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class GravityNetworkIngestionException : Exception
    {
        public string Classification { get; }

        public GravityNetworkIngestionException(string classification, string message)
            : base(message)
        {
            Classification = classification ?? "P12_B_BLOCKED_DOMAIN_MAPPING";
        }
    }

    public sealed class GravityNetworkIngestionService
    {
        public GravityNetworkSnapshot Ingest(
            NetworkReadResult readResult,
            string sourceModelFingerprint,
            string workingCopyId = null,
            int schemaVersion = 1)
        {
            if (readResult == null)
            {
                throw new ArgumentNullException(nameof(readResult));
            }

            if (string.IsNullOrWhiteSpace(sourceModelFingerprint))
            {
                throw new GravityNetworkIngestionException(
                    "P12_B_BLOCKED_DOMAIN_MAPPING",
                    "SourceModelFingerprint is required for network snapshot.");
            }

            if (readResult.ScenarioContext == null)
            {
                throw new GravityNetworkIngestionException(
                    "P12_B_BLOCKED_DOMAIN_MAPPING",
                    "ScenarioContext is required in NetworkReadResult.");
            }

            var rawStructures = readResult.Structures ?? new List<StructureReadDto>();
            var rawConduits = readResult.Conduits ?? new List<ConduitReadDto>();

            if (rawStructures.Count == 0)
            {
                throw new GravityNetworkIngestionException(
                    "P12_B_BLOCKED_DOMAIN_MAPPING",
                    "NetworkReadResult contains zero structures/nodes.");
            }

            if (rawConduits.Count == 0)
            {
                throw new GravityNetworkIngestionException(
                    "P12_B_BLOCKED_DOMAIN_MAPPING",
                    "NetworkReadResult contains zero conduits.");
            }

            // Duplicate node validation and kind mapping
            var nodeIds = new HashSet<int>();
            var nodes = new List<GravityNetworkNode>(rawStructures.Count);

            foreach (var s in rawStructures)
            {
                if (s == null)
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_DOMAIN_MAPPING",
                        "Null StructureReadDto in read result.");
                }

                if (!nodeIds.Add(s.Id))
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_DUPLICATE_ELEMENT_ID",
                        string.Format(CultureInfo.InvariantCulture, "Duplicate node SourceElementId: {0}.", s.Id));
                }

                var kind = ResolveNodeKind(s);

                ValidateFinite(s.X, "X", s.Id);
                ValidateFinite(s.Y, "Y", s.Id);
                ValidateFinite(s.GroundElevation, "GroundElevation", s.Id);
                ValidateFinite(s.RimElevation, "RimElevation", s.Id);
                ValidateFinite(s.InvertElevation, "InvertElevation", s.Id);

                ValidateDimension(s.GroundElevationUnit, "length", "GroundElevation", s.Id);
                ValidateDimension(s.RimElevationUnit, "length", "RimElevation", s.Id);
                ValidateDimension(s.InvertElevationUnit, "length", "InvertElevation", s.Id);

                string unitContext = s.GroundElevationUnit != null ? s.GroundElevationUnit.UnitName : "SOURCE_NATIVE_UNRESOLVED";

                var elevationState = new GravityElevationState(
                    s.GroundElevation,
                    s.RimElevation,
                    s.InvertElevation,
                    unitContext,
                    s.GroundElevationUnit,
                    s.GroundElevationUnit,
                    s.RimElevationUnit,
                    s.InvertElevationUnit);

                string coordUnitCtx = s.CoordinateUnit != null ? s.CoordinateUnit.UnitName : "SOURCE_NATIVE_UNRESOLVED";

                var node = new GravityNetworkNode(
                    s.Id,
                    kind,
                    s.Label,
                    s.X,
                    s.Y,
                    elevationState,
                    coordUnitCtx,
                    s.PropertyClassifications,
                    s.CoordinateUnit);

                nodes.Add(node);
            }

            // Duplicate conduit & endpoint validation
            var conduitIds = new HashSet<int>();
            var conduits = new List<GravityNetworkConduit>(rawConduits.Count);

            foreach (var c in rawConduits)
            {
                if (c == null)
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_DOMAIN_MAPPING",
                        "Null ConduitReadDto in read result.");
                }

                if (!conduitIds.Add(c.Id))
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_DUPLICATE_ELEMENT_ID",
                        string.Format(CultureInfo.InvariantCulture, "Duplicate conduit SourceElementId: {0}.", c.Id));
                }

                if (c.StartNodeId == 0 || !nodeIds.Contains(c.StartNodeId))
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_UNRESOLVED_ENDPOINT",
                        string.Format(CultureInfo.InvariantCulture, "Conduit {0} has unresolved start node ID {1}.", c.Id, c.StartNodeId));
                }

                if (c.StopNodeId == 0 || !nodeIds.Contains(c.StopNodeId))
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_UNRESOLVED_ENDPOINT",
                        string.Format(CultureInfo.InvariantCulture, "Conduit {0} has unresolved stop node ID {1}.", c.Id, c.StopNodeId));
                }

                double? startInvert = c.StartInvert ?? c.UpstreamInvert;
                double? stopInvert = c.StopInvert ?? c.DownstreamInvert;
                var startInvertUnit = c.StartInvertUnit ?? c.UpstreamInvertUnit;
                var stopInvertUnit = c.StopInvertUnit ?? c.DownstreamInvertUnit;

                ValidateFinite(c.Length, "Length", c.Id);
                ValidateFinite(c.Diameter, "Diameter", c.Id);
                ValidateFinite(c.Rise, "Rise", c.Id);
                ValidateFinite(c.Span, "Span", c.Id);
                ValidateFinite(c.ManningsN, "ManningsN", c.Id);
                ValidateFinite(startInvert, "StartInvert", c.Id);
                ValidateFinite(stopInvert, "StopInvert", c.Id);

                var diamUnit = c.DiameterUnit;
                if (diamUnit == null && readResult.FieldUnitMap != null &&
                    readResult.FieldUnitMap.TryGetValue("ConduitDiameter", out var diamMeta) && diamMeta != null)
                {
                    diamUnit = new SourceUnitIdentity(diamMeta.Dimension, diamMeta.WorkingUnitId, diamMeta.WorkingUnitName, diamMeta.WorkingUnitSymbol);
                    c.DiameterUnit = diamUnit;
                }

                ValidateDimension(c.LengthUnit, "length", "Length", c.Id);
                ValidateDimension(diamUnit, "length", "Diameter", c.Id);
                ValidateDimension(c.RiseUnit, "length", "Rise", c.Id);
                ValidateDimension(c.SpanUnit, "length", "Span", c.Id);
                ValidateDimension(startInvertUnit, "length", "StartInvert", c.Id);
                ValidateDimension(stopInvertUnit, "length", "StopInvert", c.Id);

                string dClass = c.PropertyClassifications != null && c.PropertyClassifications.TryGetValue("Diameter", out var dc) ? dc : null;
                string rClass = c.PropertyClassifications != null && c.PropertyClassifications.TryGetValue("Rise", out var rc) ? rc : null;
                var res = EffectiveSectionHeightResolver.Resolve(c.Id, c.Shape, c.Diameter, diamUnit, dClass, c.Rise, c.RiseUnit, rClass);

                double? effectiveHeight = res.IsProven ? res.Value : c.EffectiveSectionHeight;
                SourceUnitIdentity effectiveUnit = res.IsProven ? res.Unit : c.EffectiveSectionHeightUnit;
                string effectiveDeriv = res.IsProven ? res.Derivation : c.SectionHeightDerivation;

                if (res.IsProven && c.PropertyClassifications != null)
                {
                    c.PropertyClassifications["EffectiveSectionHeight"] = NetworkReadPropertyStatus.PROVEN.ToString();
                    c.PropertyClassifications["EffectiveHeight"] = NetworkReadPropertyStatus.PROVEN.ToString();
                    c.EffectiveSectionHeight = res.Value;
                    c.EffectiveSectionHeightUnit = res.Unit;
                    c.SectionHeightDerivation = res.Derivation;
                }
                else if (!string.IsNullOrEmpty(res.InconsistencyReason) && c.PropertyClassifications != null)
                {
                    c.PropertyClassifications["EffectiveSectionHeight"] = res.Status.ToString();
                    c.PropertyClassifications["GeometryConsistency"] = res.InconsistencyReason;
                    c.SectionHeightDerivation = res.InconsistencyReason;
                }

                string cUnitCtx = c.LengthUnit != null ? c.LengthUnit.UnitName : "SOURCE_NATIVE_UNRESOLVED";

                var physicalState = new GravityConduitPhysicalState(
                    length: c.Length,
                    diameter: c.Diameter,
                    rise: c.Rise,
                    span: c.Span,
                    shape: c.Shape,
                    material: c.Material,
                    manningsN: c.ManningsN,
                    startInvert: startInvert,
                    stopInvert: stopInvert,
                    unitContext: cUnitCtx,
                    lengthUnit: c.LengthUnit,
                    lengthSourceField: c.LengthSourceField,
                    diameterUnit: diamUnit,
                    riseUnit: c.RiseUnit,
                    spanUnit: c.SpanUnit,
                    startInvertUnit: startInvertUnit,
                    stopInvertUnit: stopInvertUnit,
                    setInvertToStart: c.SetInvertToStart,
                    setInvertToStop: c.SetInvertToStop,
                    startInvertSourceField: c.StartInvertSourceField,
                    stopInvertSourceField: c.StopInvertSourceField,
                    effectiveSectionHeight: effectiveHeight,
                    effectiveSectionHeightUnit: effectiveUnit,
                    sectionHeightDerivation: effectiveDeriv);

                var conduit = new GravityNetworkConduit(
                    c.Id,
                    c.Label,
                    c.StartNodeId,
                    c.StopNodeId,
                    physicalState,
                    c.PropertyClassifications);

                conduits.Add(conduit);
            }

            // Build deterministic adjacency
            var incidentMap = new Dictionary<string, List<int>>();
            var startSideMap = new Dictionary<string, List<int>>();
            var stopSideMap = new Dictionary<string, List<int>>();

            foreach (var n in nodes)
            {
                string key = n.SourceElementId.ToString(CultureInfo.InvariantCulture);
                incidentMap[key] = new List<int>();
                startSideMap[key] = new List<int>();
                stopSideMap[key] = new List<int>();
            }

            foreach (var c in conduits)
            {
                string startKey = c.StartNodeId.ToString(CultureInfo.InvariantCulture);
                string stopKey = c.StopNodeId.ToString(CultureInfo.InvariantCulture);

                incidentMap[startKey].Add(c.SourceElementId);
                startSideMap[startKey].Add(c.SourceElementId);

                incidentMap[stopKey].Add(c.SourceElementId);
                stopSideMap[stopKey].Add(c.SourceElementId);
            }

            foreach (var list in incidentMap.Values) list.Sort();
            foreach (var list in startSideMap.Values) list.Sort();
            foreach (var list in stopSideMap.Values) list.Sort();

            var adjacency = new GravityNetworkAdjacency(incidentMap, startSideMap, stopSideMap);

            var scenarioContext = new GravityScenarioContext(
                readResult.ScenarioContext.ActiveScenarioId,
                readResult.ScenarioContext.ActiveScenarioLabel,
                readResult.ScenarioContext.TopologyAlternativeId,
                readResult.ScenarioContext.PhysicalAlternativeId,
                readResult.ScenarioContext.ActiveTopologyAlternativeId);

            // Canonical sorting
            var canonicalNodes = nodes
                .OrderBy(n => (int)n.Kind)
                .ThenBy(n => n.SourceElementId)
                .ToList();

            var canonicalConduits = conduits
                .OrderBy(c => c.SourceElementId)
                .ToList();

            string networkFingerprint = GravityNetworkSnapshot.ComputeFingerprint(
                schemaVersion,
                sourceModelFingerprint,
                scenarioContext,
                canonicalNodes,
                canonicalConduits);

            return new GravityNetworkSnapshot(
                schemaVersion,
                sourceModelFingerprint,
                scenarioContext,
                canonicalNodes,
                canonicalConduits,
                adjacency,
                networkFingerprint,
                workingCopyId,
                readResult.SourceData);
        }

        private static void ValidateDimension(SourceUnitIdentity unit, string expectedDimension, string fieldDescription, int id)
        {
            if (unit == null) return;
            if (string.Equals(unit.Dimension, "SOURCE_NATIVE_UNRESOLVED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unit.Dimension, "UNRESOLVED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!string.Equals(unit.Dimension, expectedDimension, StringComparison.OrdinalIgnoreCase))
            {
                throw new GravityNetworkIngestionException(
                    "P12_U_BLOCKED_DIMENSION_MISMATCH",
                    string.Format(CultureInfo.InvariantCulture,
                        "Dimension mismatch in {0} for element {1}: expected '{2}', got '{3}'.",
                        fieldDescription, id, expectedDimension, unit.Dimension));
            }
        }

        public static GravityNodeKind ResolveNodeKind(StructureReadDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            string cat = dto.RuntimeCategory ?? string.Empty;
            if (cat.IndexOf("Manhole", StringComparison.OrdinalIgnoreCase) >= 0)
                return GravityNodeKind.Manhole;
            if (cat.IndexOf("CatchBasin", StringComparison.OrdinalIgnoreCase) >= 0)
                return GravityNodeKind.CatchBasin;
            if (cat.IndexOf("Outfall", StringComparison.OrdinalIgnoreCase) >= 0)
                return GravityNodeKind.Outfall;

            throw new GravityNetworkIngestionException(
                "P12_B_BLOCKED_UNSUPPORTED_NODE_TYPE",
                string.Format(CultureInfo.InvariantCulture, "Unsupported structure category '{0}' on element {1}.", cat, dto.Id));
        }

        private static void ValidateFinite(double? val, string field, int id)
        {
            if (val.HasValue)
            {
                if (double.IsNaN(val.Value) || double.IsInfinity(val.Value))
                {
                    throw new GravityNetworkIngestionException(
                        "P12_B_BLOCKED_DOMAIN_MAPPING",
                        string.Format(CultureInfo.InvariantCulture, "Non-finite numeric value in {0} for element {1}.", field, id));
                }
            }
        }
    }
}
