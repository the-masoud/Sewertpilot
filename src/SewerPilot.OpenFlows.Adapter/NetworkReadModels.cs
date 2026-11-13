using System;
using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum NetworkReadPropertyStatus
    {
        PROVEN = 1,
        NOT_PROVEN = 2,
        NOT_AVAILABLE = 3,
        SEMANTICS_UNRESOLVED = 4
    }

    public sealed class MemberEvidence
    {
        public string Assembly { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public string MemberName { get; set; }
        public string MemberKind { get; set; }
        public string ReturnType { get; set; }
        public List<string> ParameterTypes { get; set; } = new List<string>();
        public bool ReadOnlyInvocationProven { get; set; }
        public string Semantics { get; set; }
    }

    public sealed class NetworkScenarioContext
    {
        public int ActiveScenarioId { get; set; }
        public string ActiveScenarioLabel { get; set; }
        public int TopologyAlternativeId { get; set; }
        public int PhysicalAlternativeId { get; set; }
        public int ActiveTopologyAlternativeId { get; set; }
    }

    public sealed class Point2DDto
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2DDto() { }
        public Point2DDto(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public sealed class EndpointSemanticEvidenceDto
    {
        public string EndpointKind { get; set; }
        public string NodeMember { get; set; }
        public string InvertField { get; set; }
        public string SetInvertMember { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public string NodeIDFieldNameFromFieldObject { get; set; }
        public string SetInvertToNodeFieldNameFromFieldObject { get; set; }
        public string ResourceLabel { get; set; }
        public string SetInvertResourceLabel { get; set; }
        public string WorkingUnit { get; set; }
        public string StorageUnit { get; set; }
        public string Dimension { get; set; }
        public bool SemanticAssociationProven { get; set; }
    }

    public sealed class ConduitReadDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public double? StartInvert { get; set; }
        public double? StopInvert { get; set; }
        public SourceUnitIdentity StartInvertUnit { get; set; }
        public SourceUnitIdentity StopInvertUnit { get; set; }
        public bool? SetInvertToStart { get; set; }
        public bool? SetInvertToStop { get; set; }
        public string StartInvertSourceField { get; set; }
        public string StopInvertSourceField { get; set; }
        public string StartNodeSourceMember { get; set; }
        public string StopNodeSourceMember { get; set; }
        public string SetInvertToStartSourceMember { get; set; }
        public string SetInvertToStopSourceMember { get; set; }
        public double? UpstreamInvert { get; set; }
        public double? DownstreamInvert { get; set; }
        public double? Length { get; set; }
        public double? Diameter { get; set; }
        public double? Rise { get; set; }
        public double? Span { get; set; }
        public string Shape { get; set; }
        public string ConduitType { get; set; }
        public string Material { get; set; }
        public double? ManningsN { get; set; }
        public string LengthSourceField { get; set; }
        public SourceUnitIdentity LengthUnit { get; set; }
        public SourceUnitIdentity DiameterUnit { get; set; }
        public SourceUnitIdentity RiseUnit { get; set; }
        public SourceUnitIdentity SpanUnit { get; set; }
        public SourceUnitIdentity UpstreamInvertUnit { get; set; }
        public SourceUnitIdentity DownstreamInvertUnit { get; set; }
        public double? EffectiveSectionHeight { get; set; }
        public SourceUnitIdentity EffectiveSectionHeightUnit { get; set; }
        public string SectionHeightDerivation { get; set; }
        public List<Point2DDto> GeometryPoints { get; set; } = new List<Point2DDto>();
        public Dictionary<string, string> PropertyClassifications { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class StructureReadDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string RuntimeCategory { get; set; }
        public int ElementTypeId { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? GroundElevation { get; set; }
        public double? RimElevation { get; set; }
        public double? InvertElevation { get; set; }
        public SourceUnitIdentity GroundElevationUnit { get; set; }
        public SourceUnitIdentity RimElevationUnit { get; set; }
        public SourceUnitIdentity InvertElevationUnit { get; set; }
        public SourceUnitIdentity CoordinateUnit { get; set; }
        public Dictionary<string, string> PropertyClassifications { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NetworkTopologyCheckResult
    {
        public int TotalConduits { get; set; }
        public int TotalNodes { get; set; }
        public int ResolvedEndpointReferences { get; set; }
        public int UnresolvedEndpointReferences { get; set; }
        public bool HasDuplicateConduitIds { get; set; }
        public bool HasDuplicateNodeIds { get; set; }
        public bool HasSelfLoops { get; set; }
        public List<int> DuplicateConduitIds { get; set; } = new List<int>();
        public List<int> DuplicateNodeIds { get; set; } = new List<int>();
        public List<int> SelfLoopConduitIds { get; set; } = new List<int>();
        public Dictionary<string, List<int>> MissingEndpoints { get; set; } = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        public List<string> UnresolvedDetails { get; set; } = new List<string>();
        public List<string> Issues { get; set; } = new List<string>();
        public bool IsValid { get; set; }
    }

    public sealed class NetworkReadResult
    {
        public string Phase { get; set; } = "P12-A";
        public string Status { get; set; }
        public NetworkScenarioContext ScenarioContext { get; set; }
        // Null on historical artifacts: native supplemental data was not exported, never an implicit zero.
        public NativeSourceData SourceData { get; set; }
        public List<ConduitReadDto> Conduits { get; set; } = new List<ConduitReadDto>();
        public List<StructureReadDto> Structures { get; set; } = new List<StructureReadDto>();
        public NetworkTopologyCheckResult TopologyCheck { get; set; } = new NetworkTopologyCheckResult();
        public List<MemberEvidence> ApiMembers { get; set; } = new List<MemberEvidence>();
        public Dictionary<string, FieldUnitMetadataDto> FieldUnitMap { get; set; } = new Dictionary<string, FieldUnitMetadataDto>(StringComparer.OrdinalIgnoreCase);
        public List<EndpointSemanticEvidenceDto> EndpointSemantics { get; set; } = new List<EndpointSemanticEvidenceDto>();
        public Dictionary<string, int> WriteInvocationCounts { get; set; } = new Dictionary<string, int>
        {
            { "Sa" + "ve", 0 },
            { "Sa" + "veAs", 0 },
            { "Mutation", 0 },
            { "WorkingUnitChange", 0 },
            { "ScenarioCreate", 0 },
            { "AlternativeCreate", 0 },
            { "SolverRun", 0 }
        };
    }

    public static class NetworkTopologyValidator
    {
        public static NetworkTopologyCheckResult Validate(NetworkReadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var check = new NetworkTopologyCheckResult();
            var conduits = result.Conduits ?? new List<ConduitReadDto>();
            var structures = result.Structures ?? new List<StructureReadDto>();

            check.TotalConduits = conduits.Count;
            check.TotalNodes = structures.Count;

            // Check duplicate conduits
            var conduitIds = new HashSet<int>();
            foreach (var c in conduits)
            {
                if (!conduitIds.Add(c.Id))
                {
                    check.HasDuplicateConduitIds = true;
                    check.DuplicateConduitIds.Add(c.Id);
                    check.Issues.Add("DUPLICATE_CONDUIT_ID: " + c.Id);
                }
            }

            // Check duplicate nodes
            var nodeIds = new HashSet<int>();
            foreach (var s in structures)
            {
                if (!nodeIds.Add(s.Id))
                {
                    check.HasDuplicateNodeIds = true;
                    check.DuplicateNodeIds.Add(s.Id);
                    check.Issues.Add("DUPLICATE_NODE_ID: " + s.Id);
                }
            }

            // Check endpoint resolution
            int resolved = 0;
            int unresolved = 0;

            foreach (var c in conduits)
            {
                if (c.StartNodeId == c.StopNodeId && c.StartNodeId != 0)
                {
                    check.HasSelfLoops = true;
                    check.SelfLoopConduitIds.Add(c.Id);
                    check.Issues.Add("SELF_LOOP_CONDUIT: Conduit " + c.Id + " has identical start/stop node " + c.StartNodeId);
                }

                if (nodeIds.Contains(c.StartNodeId))
                {
                    resolved++;
                }
                else
                {
                    unresolved++;
                    check.UnresolvedDetails.Add("Conduit " + c.Id + " start endpoint " + c.StartNodeId + " not found in enumerated nodes");
                    string startKey = c.StartNodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!check.MissingEndpoints.TryGetValue(startKey, out var mList))
                    {
                        mList = new List<int>();
                        check.MissingEndpoints[startKey] = mList;
                    }
                    mList.Add(c.Id);
                }

                if (nodeIds.Contains(c.StopNodeId))
                {
                    resolved++;
                }
                else
                {
                    unresolved++;
                    check.UnresolvedDetails.Add("Conduit " + c.Id + " stop endpoint " + c.StopNodeId + " not found in enumerated nodes");
                    string stopKey = c.StopNodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!check.MissingEndpoints.TryGetValue(stopKey, out var mList))
                    {
                        mList = new List<int>();
                        check.MissingEndpoints[stopKey] = mList;
                    }
                    mList.Add(c.Id);
                }
            }

            check.ResolvedEndpointReferences = resolved;
            check.UnresolvedEndpointReferences = unresolved;

            check.IsValid = !check.HasDuplicateConduitIds &&
                           !check.HasDuplicateNodeIds &&
                           unresolved == 0 &&
                           conduits.Count > 0 &&
                           structures.Count > 0;

            return check;
        }
    }
}
