using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum GravityNodeKind
    {
        Unknown = 0,
        Manhole = 1,
        CatchBasin = 2,
        Outfall = 3
    }

    public enum GravityUnitClassification
    {
        SourceNativeUnresolved = 1,
        Proven = 2
    }

    public sealed class GravityElevationState
    {
        public double? GroundElevation { get; }
        public double? RimElevation { get; }
        public double? InvertElevation { get; }
        public string UnitContext { get; }
        public SourceUnitIdentity ElevationUnit { get; }
        public SourceUnitIdentity GroundElevationUnit { get; }
        public SourceUnitIdentity RimElevationUnit { get; }
        public SourceUnitIdentity InvertElevationUnit { get; }

        public GravityElevationState(
            double? groundElevation = null,
            double? rimElevation = null,
            double? invertElevation = null,
            string unitContext = "SOURCE_NATIVE_UNRESOLVED",
            SourceUnitIdentity elevationUnit = null,
            SourceUnitIdentity groundElevationUnit = null,
            SourceUnitIdentity rimElevationUnit = null,
            SourceUnitIdentity invertElevationUnit = null)
        {
            GroundElevation = groundElevation;
            RimElevation = rimElevation;
            InvertElevation = invertElevation;
            UnitContext = unitContext ?? "SOURCE_NATIVE_UNRESOLVED";
            ElevationUnit = elevationUnit?.Clone();
            GroundElevationUnit = groundElevationUnit?.Clone() ?? ElevationUnit;
            RimElevationUnit = rimElevationUnit?.Clone() ?? ElevationUnit;
            InvertElevationUnit = invertElevationUnit?.Clone() ?? ElevationUnit;
        }

        public GravityElevationState Clone()
        {
            return new GravityElevationState(
                GroundElevation, RimElevation, InvertElevation, UnitContext,
                ElevationUnit, GroundElevationUnit, RimElevationUnit, InvertElevationUnit);
        }
    }

    public sealed class GravityConduitPhysicalState
    {
        public double? Length { get; }
        public double? Diameter { get; }
        public double? Rise { get; }
        public double? Span { get; }
        public string Shape { get; }
        public string Material { get; }
        public double? ManningsN { get; }
        public double? StartInvert { get; }
        public double? StopInvert { get; }
        public SourceUnitIdentity StartInvertUnit { get; }
        public SourceUnitIdentity StopInvertUnit { get; }
        public bool? SetInvertToStart { get; }
        public bool? SetInvertToStop { get; }
        public string StartInvertSourceField { get; }
        public string StopInvertSourceField { get; }

        public string UnitContext { get; }
        public SourceUnitIdentity LengthUnit { get; }
        public string LengthSourceField { get; }
        public SourceUnitIdentity DiameterUnit { get; }
        public SourceUnitIdentity RiseUnit { get; }
        public SourceUnitIdentity SpanUnit { get; }

        // Diagnostic / backward compatibility accessors
        public double? UpstreamInvert => StartInvert;
        public double? DownstreamInvert => StopInvert;
        public SourceUnitIdentity UpstreamInvertUnit => StartInvertUnit;
        public SourceUnitIdentity DownstreamInvertUnit => StopInvertUnit;

        // Effective section height
        public double? EffectiveSectionHeight { get; }
        public SourceUnitIdentity EffectiveSectionHeightUnit { get; }
        public string SectionHeightDerivation { get; }
        public double? EffectiveHeight => EffectiveSectionHeight;
        public string EffectiveSectionHeightDerivation => SectionHeightDerivation;

        public GravityConduitPhysicalState(
            double? length = null,
            double? diameter = null,
            double? rise = null,
            double? span = null,
            string shape = null,
            string material = null,
            double? manningsN = null,
            double? startInvert = null,
            double? stopInvert = null,
            string unitContext = "SOURCE_NATIVE_UNRESOLVED",
            SourceUnitIdentity lengthUnit = null,
            string lengthSourceField = null,
            SourceUnitIdentity diameterUnit = null,
            SourceUnitIdentity riseUnit = null,
            SourceUnitIdentity spanUnit = null,
            SourceUnitIdentity startInvertUnit = null,
            SourceUnitIdentity stopInvertUnit = null,
            bool? setInvertToStart = null,
            bool? setInvertToStop = null,
            string startInvertSourceField = null,
            string stopInvertSourceField = null,
            double? upstreamInvert = null,
            double? downstreamInvert = null,
            SourceUnitIdentity upstreamInvertUnit = null,
            SourceUnitIdentity downstreamInvertUnit = null,
            double? effectiveSectionHeight = null,
            SourceUnitIdentity effectiveSectionHeightUnit = null,
            string sectionHeightDerivation = null)
        {
            Length = length;
            Diameter = diameter;
            Rise = rise;
            Span = span;
            Shape = shape;
            Material = material;
            ManningsN = manningsN;
            StartInvert = startInvert ?? upstreamInvert;
            StopInvert = stopInvert ?? downstreamInvert;
            UnitContext = unitContext ?? "SOURCE_NATIVE_UNRESOLVED";
            LengthUnit = lengthUnit?.Clone();
            LengthSourceField = lengthSourceField;
            DiameterUnit = diameterUnit?.Clone();
            RiseUnit = riseUnit?.Clone();
            SpanUnit = spanUnit?.Clone();
            StartInvertUnit = (startInvertUnit ?? upstreamInvertUnit)?.Clone();
            StopInvertUnit = (stopInvertUnit ?? downstreamInvertUnit)?.Clone();
            SetInvertToStart = setInvertToStart;
            SetInvertToStop = setInvertToStop;
            StartInvertSourceField = startInvertSourceField ?? "Physical_UpstreamInvert";
            StopInvertSourceField = stopInvertSourceField ?? "Physical_DownstreamInvert";
            EffectiveSectionHeight = effectiveSectionHeight;
            EffectiveSectionHeightUnit = effectiveSectionHeightUnit?.Clone();
            SectionHeightDerivation = sectionHeightDerivation;
        }

        public GravityConduitPhysicalState Clone()
        {
            return new GravityConduitPhysicalState(
                length: Length,
                diameter: Diameter,
                rise: Rise,
                span: Span,
                shape: Shape,
                material: Material,
                manningsN: ManningsN,
                startInvert: StartInvert,
                stopInvert: StopInvert,
                unitContext: UnitContext,
                lengthUnit: LengthUnit,
                lengthSourceField: LengthSourceField,
                diameterUnit: DiameterUnit,
                riseUnit: RiseUnit,
                spanUnit: SpanUnit,
                startInvertUnit: StartInvertUnit,
                stopInvertUnit: StopInvertUnit,
                setInvertToStart: SetInvertToStart,
                setInvertToStop: SetInvertToStop,
                startInvertSourceField: StartInvertSourceField,
                stopInvertSourceField: StopInvertSourceField,
                effectiveSectionHeight: EffectiveSectionHeight,
                effectiveSectionHeightUnit: EffectiveSectionHeightUnit,
                sectionHeightDerivation: SectionHeightDerivation);
        }
    }

    public sealed class GravityScenarioContext
    {
        public int ScenarioId { get; }
        public string ScenarioLabel { get; }
        public int TopologyAlternativeId { get; }
        public int PhysicalAlternativeId { get; }
        public int ActiveTopologyAlternativeId { get; }

        public GravityScenarioContext(
            int scenarioId,
            string scenarioLabel,
            int topologyAlternativeId,
            int physicalAlternativeId,
            int activeTopologyAlternativeId)
        {
            ScenarioId = scenarioId;
            ScenarioLabel = scenarioLabel ?? string.Empty;
            TopologyAlternativeId = topologyAlternativeId;
            PhysicalAlternativeId = physicalAlternativeId;
            ActiveTopologyAlternativeId = activeTopologyAlternativeId;
        }

        public GravityScenarioContext Clone()
        {
            return new GravityScenarioContext(
                ScenarioId, ScenarioLabel, TopologyAlternativeId, PhysicalAlternativeId, ActiveTopologyAlternativeId);
        }
    }

    public sealed class GravityNetworkNode
    {
        public int SourceElementId { get; }
        public GravityNodeKind Kind { get; }
        public string Label { get; }
        public double? X { get; }
        public double? Y { get; }
        public GravityElevationState ExistingElevationState { get; }
        public string CoordinateUnitContext { get; }
        public SourceUnitIdentity CoordinateUnit { get; }
        public IReadOnlyDictionary<string, string> PropertyClassifications { get; }

        public GravityNetworkNode(
            int sourceElementId,
            GravityNodeKind kind,
            string label,
            double? x,
            double? y,
            GravityElevationState existingElevationState,
            string coordinateUnitContext = "SOURCE_NATIVE_UNRESOLVED",
            IDictionary<string, string> propertyClassifications = null,
            SourceUnitIdentity coordinateUnit = null)
        {
            SourceElementId = sourceElementId;
            Kind = kind;
            Label = label ?? string.Empty;
            X = x;
            Y = y;
            ExistingElevationState = existingElevationState?.Clone() ?? new GravityElevationState();
            CoordinateUnitContext = coordinateUnitContext ?? "SOURCE_NATIVE_UNRESOLVED";
            CoordinateUnit = coordinateUnit?.Clone();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (propertyClassifications != null)
            {
                foreach (var kvp in propertyClassifications)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }
            PropertyClassifications = new ReadOnlyDictionary<string, string>(dict);
        }
    }

    public sealed class GravityNetworkConduit
    {
        public int SourceElementId { get; }
        public string Label { get; }
        public int StartNodeId { get; }
        public int StopNodeId { get; }
        public GravityConduitPhysicalState ExistingPhysicalState { get; }
        public IReadOnlyDictionary<string, string> PropertyClassifications { get; }

        public GravityNetworkConduit(
            int sourceElementId,
            string label,
            int startNodeId,
            int stopNodeId,
            GravityConduitPhysicalState existingPhysicalState,
            IDictionary<string, string> propertyClassifications = null)
        {
            SourceElementId = sourceElementId;
            Label = label ?? string.Empty;
            StartNodeId = startNodeId;
            StopNodeId = stopNodeId;
            ExistingPhysicalState = existingPhysicalState?.Clone() ?? new GravityConduitPhysicalState();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (propertyClassifications != null)
            {
                foreach (var kvp in propertyClassifications)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }
            PropertyClassifications = new ReadOnlyDictionary<string, string>(dict);
        }
    }

    public sealed class GravityNetworkAdjacency
    {
        public IReadOnlyDictionary<string, IReadOnlyList<int>> IncidentConduitsByNode { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<int>> StartConduitsByNode { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<int>> StopConduitsByNode { get; }

        public GravityNetworkAdjacency(
            IDictionary<string, List<int>> incident,
            IDictionary<string, List<int>> startSide,
            IDictionary<string, List<int>> stopSide)
        {
            var inc = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            if (incident != null)
            {
                foreach (var kvp in incident)
                {
                    inc[kvp.Key] = new ReadOnlyCollection<int>(new List<int>(kvp.Value));
                }
            }
            IncidentConduitsByNode = new ReadOnlyDictionary<string, IReadOnlyList<int>>(inc);

            var st = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            if (startSide != null)
            {
                foreach (var kvp in startSide)
                {
                    st[kvp.Key] = new ReadOnlyCollection<int>(new List<int>(kvp.Value));
                }
            }
            StartConduitsByNode = new ReadOnlyDictionary<string, IReadOnlyList<int>>(st);

            var sp = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            if (stopSide != null)
            {
                foreach (var kvp in stopSide)
                {
                    sp[kvp.Key] = new ReadOnlyCollection<int>(new List<int>(kvp.Value));
                }
            }
            StopConduitsByNode = new ReadOnlyDictionary<string, IReadOnlyList<int>>(sp);
        }

        public IReadOnlyList<int> GetIncidentConduits(int nodeId)
        {
            string key = nodeId.ToString(CultureInfo.InvariantCulture);
            return IncidentConduitsByNode.TryGetValue(key, out var list) ? list : (IReadOnlyList<int>)new int[0];
        }

        public IReadOnlyList<int> GetStartConduits(int nodeId)
        {
            string key = nodeId.ToString(CultureInfo.InvariantCulture);
            return StartConduitsByNode.TryGetValue(key, out var list) ? list : (IReadOnlyList<int>)new int[0];
        }

        public IReadOnlyList<int> GetStopConduits(int nodeId)
        {
            string key = nodeId.ToString(CultureInfo.InvariantCulture);
            return StopConduitsByNode.TryGetValue(key, out var list) ? list : (IReadOnlyList<int>)new int[0];
        }
    }

    public sealed class GravityNetworkSnapshot
    {
        public int SchemaVersion { get; } = 1;
        public string SourceModelFingerprint { get; }
        public string WorkingCopyId { get; }
        public GravityScenarioContext ScenarioContext { get; }
        public ReadOnlyCollection<GravityNetworkNode> Nodes { get; }
        public ReadOnlyCollection<GravityNetworkConduit> Conduits { get; }
        public GravityNetworkAdjacency Adjacency { get; }
        public string NetworkFingerprint { get; }
        private readonly NativeSourceData sourceData;
        public NativeSourceData SourceData => sourceData?.Copy();

        public GravityNetworkSnapshot(
            int schemaVersion,
            string sourceModelFingerprint,
            GravityScenarioContext scenarioContext,
            IEnumerable<GravityNetworkNode> nodes,
            IEnumerable<GravityNetworkConduit> conduits,
            GravityNetworkAdjacency adjacency,
            string networkFingerprint,
            string workingCopyId = null,
            NativeSourceData sourceData = null)
        {
            SchemaVersion = schemaVersion;
            this.sourceData = sourceData?.Copy();
            SourceModelFingerprint = sourceModelFingerprint ?? string.Empty;
            WorkingCopyId = workingCopyId;
            ScenarioContext = scenarioContext?.Clone() ?? new GravityScenarioContext(0, string.Empty, 0, 0, 0);

            var nodeList = nodes != null
                ? nodes.Select(n => new GravityNetworkNode(
                    n.SourceElementId,
                    n.Kind,
                    n.Label,
                    n.X,
                    n.Y,
                    n.ExistingElevationState,
                    n.CoordinateUnitContext,
                    n.PropertyClassifications.ToDictionary(k => k.Key, k => k.Value),
                    n.CoordinateUnit
                )).ToList()
                : new List<GravityNetworkNode>();
            Nodes = new ReadOnlyCollection<GravityNetworkNode>(nodeList);

            var conduitList = conduits != null
                ? conduits.Select(c => new GravityNetworkConduit(
                    c.SourceElementId,
                    c.Label,
                    c.StartNodeId,
                    c.StopNodeId,
                    c.ExistingPhysicalState,
                    c.PropertyClassifications.ToDictionary(k => k.Key, k => k.Value)
                )).ToList()
                : new List<GravityNetworkConduit>();
            Conduits = new ReadOnlyCollection<GravityNetworkConduit>(conduitList);

            Adjacency = adjacency ?? new GravityNetworkAdjacency(null, null, null);
            NetworkFingerprint = networkFingerprint ?? string.Empty;
        }

        public static string ComputeFingerprint(
            int schemaVersion,
            string sourceModelFingerprint,
            GravityScenarioContext scenarioContext,
            IEnumerable<GravityNetworkNode> nodes,
            IEnumerable<GravityNetworkConduit> conduits)
        {
            var sb = new StringBuilder();
            sb.Append("SCHEMA:").Append(schemaVersion.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("SOURCE_MODEL:").Append(sourceModelFingerprint ?? string.Empty).Append("\n");
            if (scenarioContext != null)
            {
                sb.Append("SCENARIO:")
                  .Append(scenarioContext.ScenarioId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(scenarioContext.ScenarioLabel ?? string.Empty).Append("|")
                  .Append(scenarioContext.TopologyAlternativeId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(scenarioContext.PhysicalAlternativeId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(scenarioContext.ActiveTopologyAlternativeId.ToString(CultureInfo.InvariantCulture)).Append("\n");
            }

            sb.Append("NODES:\n");
            if (nodes != null)
            {
                var sortedNodes = nodes
                    .OrderBy(n => (int)n.Kind)
                    .ThenBy(n => n.SourceElementId);

                foreach (var n in sortedNodes)
                {
                    sb.Append("  NODE|")
                      .Append(((int)n.Kind).ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(n.SourceElementId.ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(n.Label ?? string.Empty).Append("|")
                      .Append(FormatDouble(n.X)).Append("|")
                      .Append(FormatDouble(n.Y)).Append("|");

                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(n.CoordinateUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(n.ExistingElevationState?.GroundElevation)).Append("|");
                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(n.ExistingElevationState?.GroundElevationUnit ?? n.ExistingElevationState?.ElevationUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(n.ExistingElevationState?.RimElevation)).Append("|");
                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(n.ExistingElevationState?.RimElevationUnit ?? n.ExistingElevationState?.ElevationUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(n.ExistingElevationState?.InvertElevation));
                    if (schemaVersion >= 2)
                    {
                        sb.Append("|").Append(FormatUnit(n.ExistingElevationState?.InvertElevationUnit ?? n.ExistingElevationState?.ElevationUnit));
                    }
                    sb.Append("\n");
                }
            }

            sb.Append("CONDUITS:\n");
            if (conduits != null)
            {
                var sortedConduits = conduits
                    .OrderBy(c => c.SourceElementId);

                foreach (var c in sortedConduits)
                {
                    sb.Append("  CONDUIT|")
                      .Append(c.SourceElementId.ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(c.Label ?? string.Empty).Append("|")
                      .Append(c.StartNodeId.ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(c.StopNodeId.ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(FormatDouble(c.ExistingPhysicalState?.Length)).Append("|");

                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(c.ExistingPhysicalState?.LengthUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(c.ExistingPhysicalState?.Diameter)).Append("|");
                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(c.ExistingPhysicalState?.DiameterUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(c.ExistingPhysicalState?.Rise)).Append("|");
                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(c.ExistingPhysicalState?.RiseUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(c.ExistingPhysicalState?.Span)).Append("|");
                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(c.ExistingPhysicalState?.SpanUnit)).Append("|");
                    }

                    sb.Append(c.ExistingPhysicalState?.Shape ?? string.Empty).Append("|")
                      .Append(c.ExistingPhysicalState?.Material ?? string.Empty).Append("|")
                      .Append(FormatDouble(c.ExistingPhysicalState?.ManningsN)).Append("|")
                      .Append(FormatDouble(c.ExistingPhysicalState?.StartInvert)).Append("|");

                    if (schemaVersion >= 2)
                    {
                        sb.Append(FormatUnit(c.ExistingPhysicalState?.StartInvertUnit)).Append("|");
                    }

                    sb.Append(FormatDouble(c.ExistingPhysicalState?.StopInvert));
                    if (schemaVersion >= 2)
                    {
                        sb.Append("|").Append(FormatUnit(c.ExistingPhysicalState?.StopInvertUnit));
                    }
                    sb.Append("\n");
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string FormatDouble(double? val)
        {
            if (!val.HasValue) return "NULL";
            return val.Value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatUnit(SourceUnitIdentity u)
        {
            if (u == null) return "UNRESOLVED";
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}",
                u.Dimension ?? "UNRESOLVED",
                u.UnitId ?? "UNRESOLVED",
                u.UnitName ?? "UNRESOLVED",
                u.Symbol ?? "UNRESOLVED");
        }
    }
}
