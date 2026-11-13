using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter.HydraulicRules;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class ResultCompositionContext
    {
        public string ProjectId { get; set; }
        public string ModelFingerprint { get; set; }
        public string NetworkFingerprint { get; set; }
        public string CandidateFingerprint { get; set; }
        public int ScenarioId { get; set; }
        public int PhysicalAlternativeId { get; set; }
        public string CalculationContext { get; set; }
        public string CalculationRunId { get; set; }
        public double? ResultTimeSeconds { get; set; }
        public bool IsTestOnly { get; set; } = true;
        public HydraulicResultProvenance Provenance { get; set; }
    }

    public sealed class HydraulicMetricObservation
    {
        public int ElementId { get; set; }
        public string ElementKind { get; set; } // "Conduit", "Manhole", "Outfall"
        public HydraulicMetricKind Metric { get; set; }
        public double? NumericValue { get; set; }
        public bool? BooleanValue { get; set; }
        public string RawValue { get; set; }
        public string FieldName { get; set; }
        public string EngineName { get; set; }
        public string RecordName { get; set; }
        public string Unit { get; set; }
        public int? TimeStepIndex { get; set; }
        public double? TimeSeconds { get; set; }
        public int? ScenarioId { get; set; }
        public int? PhysicalAlternativeId { get; set; }
        public string CalculationContext { get; set; }
        public HydraulicResultAvailability Availability { get; set; } = HydraulicResultAvailability.Available;
        public string Status { get; set; }
        public string ErrorReason { get; set; }
        public HydraulicResultProvenance Provenance { get; set; }

        public static HydraulicMetricObservation FromMetricResult(WaterObjectsMetricResult r, HydraulicMetricKind metric, string elementKind = "Conduit")
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var obs = new HydraulicMetricObservation
            {
                ElementId = r.ElementId ?? 0,
                ElementKind = elementKind,
                Metric = metric,
                FieldName = r.FieldName,
                EngineName = r.EngineName,
                RecordName = r.RecordName,
                Unit = r.WorkingUnitSymbol,
                TimeStepIndex = r.TimeStepIndex,
                TimeSeconds = r.TimeSeconds,
                Status = r.Status,
                Availability = (r.Status == "SUCCESS" || r.Status == "OBSERVED_ZERO" || r.Status == "OBSERVED_FINITE" || r.Status == "BOOLEAN_FALSE" || r.Status == "BOOLEAN_TRUE")
                    ? HydraulicResultAvailability.Available
                    : HydraulicResultAvailability.NotAvailable,
                ErrorReason = r.ErrorMessage
            };

            if (metric == HydraulicMetricKind.Surcharge)
            {
                if (r.Classification == "BOOLEAN_TRUE" || r.RawValue == "True")
                    obs.BooleanValue = true;
                else if (r.Classification == "BOOLEAN_FALSE" || r.RawValue == "False")
                    obs.BooleanValue = false;
                else if (bool.TryParse(r.RawValue, out bool b))
                    obs.BooleanValue = b;
                obs.NumericValue = null;
            }
            else
            {
                double? numVal = (r.NumericValue.HasValue && !double.IsNaN(r.NumericValue.Value) && !double.IsInfinity(r.NumericValue.Value)) ? r.NumericValue : null;
                obs.NumericValue = numVal;
                obs.BooleanValue = null;
                if (!numVal.HasValue && obs.Availability == HydraulicResultAvailability.Available)
                    obs.Availability = HydraulicResultAvailability.NotAvailable;
            }

            return obs;
        }

        public static HydraulicMetricObservation FromFlowResult(WaterObjectsFlowResult r, int elementId, string engine = "CivilStormDynamicEngine")
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            double? numVal = (r.Value.HasValue && !double.IsNaN(r.Value.Value) && !double.IsInfinity(r.Value.Value)) ? r.Value : null;
            return new HydraulicMetricObservation
            {
                ElementId = r.ElementId ?? elementId,
                ElementKind = "Conduit",
                Metric = HydraulicMetricKind.Flow,
                FieldName = WaterObjectsResultReader.FlowFieldName,
                EngineName = engine,
                RecordName = WaterObjectsResultReader.FlowRecordName,
                Unit = r.WorkingUnitSymbol,
                TimeStepIndex = r.TimeStepIndex,
                TimeSeconds = r.TimeSeconds,
                NumericValue = numVal,
                Status = r.Status,
                Availability = (r.Status == "SUCCESS" || r.Status == "OBSERVED_ZERO" || r.Status == "OBSERVED_FINITE") && numVal.HasValue
                    ? HydraulicResultAvailability.Available
                    : HydraulicResultAvailability.NotAvailable,
                ErrorReason = r.ErrorMessage
            };
        }
    }

    public sealed class ConduitGeometryInfo
    {
        public int ConduitId { get; set; }
        public string Label { get; set; }
        public int? StartNodeId { get; set; }
        public int? StopNodeId { get; set; }
        public double? Length { get; set; }
        public string LengthUnit { get; set; }
        public double? Diameter { get; set; }
        public string DiameterUnit { get; set; }
        public double? StartInvert { get; set; }
        public string StartInvertUnit { get; set; }
        public double? StopInvert { get; set; }
        public string StopInvertUnit { get; set; }
        public double? UpstreamGroundElevation { get; set; }
        public double? DownstreamGroundElevation { get; set; }
        public string ElevationUnit { get; set; }
        public double? ManningN { get; set; }
    }

    public sealed class NodeGeometryInfo
    {
        public int NodeId { get; set; }
        public string Label { get; set; }
        public string NodeKind { get; set; } // "Manhole", "Outfall", etc.
        public double? GroundElevation { get; set; }
        public double? RimElevation { get; set; }
        public double? InvertElevation { get; set; }
        public string ElevationUnit { get; set; }
    }

    public sealed class NetworkGeometryInfo
    {
        public List<ConduitGeometryInfo> Conduits { get; set; } = new List<ConduitGeometryInfo>();
        public List<NodeGeometryInfo> Nodes { get; set; } = new List<NodeGeometryInfo>();

        public static NetworkGeometryInfo FromSnapshot(GravityNetworkSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var info = new NetworkGeometryInfo();
            var nodeMap = snapshot.Nodes?.ToDictionary(n => n.SourceElementId, n => n) ?? new Dictionary<int, GravityNetworkNode>();

            if (snapshot.Nodes != null)
            {
                foreach (var node in snapshot.Nodes)
                {
                    info.Nodes.Add(new NodeGeometryInfo
                    {
                        NodeId = node.SourceElementId,
                        Label = node.Label,
                        NodeKind = node.Kind.ToString(),
                        GroundElevation = node.ExistingElevationState?.GroundElevation,
                        RimElevation = node.ExistingElevationState?.RimElevation,
                        InvertElevation = node.ExistingElevationState?.InvertElevation,
                        ElevationUnit = node.ExistingElevationState?.GroundElevationUnit?.Symbol ?? node.ExistingElevationState?.ElevationUnit?.Symbol
                    });
                }
            }

            if (snapshot.Conduits != null)
            {
                foreach (var c in snapshot.Conduits)
                {
                    double? usGround = null;
                    double? dsGround = null;
                    string elevUnit = null;

                    if (nodeMap.TryGetValue(c.StartNodeId, out var usNode))
                    {
                        usGround = usNode.ExistingElevationState?.GroundElevation;
                        elevUnit = usNode.ExistingElevationState?.GroundElevationUnit?.Symbol;
                    }
                    if (nodeMap.TryGetValue(c.StopNodeId, out var dsNode))
                    {
                        dsGround = dsNode.ExistingElevationState?.GroundElevation;
                        if (string.IsNullOrEmpty(elevUnit))
                            elevUnit = dsNode.ExistingElevationState?.GroundElevationUnit?.Symbol;
                    }

                    info.Conduits.Add(new ConduitGeometryInfo
                    {
                        ConduitId = c.SourceElementId,
                        Label = c.Label,
                        StartNodeId = c.StartNodeId,
                        StopNodeId = c.StopNodeId,
                        Length = c.ExistingPhysicalState?.Length,
                        LengthUnit = c.ExistingPhysicalState?.LengthUnit?.Symbol,
                        Diameter = c.ExistingPhysicalState?.Diameter ?? c.ExistingPhysicalState?.EffectiveSectionHeight,
                        DiameterUnit = c.ExistingPhysicalState?.DiameterUnit?.Symbol ?? c.ExistingPhysicalState?.EffectiveSectionHeightUnit?.Symbol,
                        StartInvert = c.ExistingPhysicalState?.StartInvert,
                        StartInvertUnit = c.ExistingPhysicalState?.StartInvertUnit?.Symbol,
                        StopInvert = c.ExistingPhysicalState?.StopInvert,
                        StopInvertUnit = c.ExistingPhysicalState?.StopInvertUnit?.Symbol,
                        UpstreamGroundElevation = usGround,
                        DownstreamGroundElevation = dsGround,
                        ElevationUnit = elevUnit,
                        ManningN = c.ExistingPhysicalState?.ManningsN
                    });
                }
            }

            return info;
        }

        public static NetworkGeometryInfo FromNetworkRead(NetworkReadResult readResult)
        {
            if (readResult == null) throw new ArgumentNullException(nameof(readResult));
            var info = new NetworkGeometryInfo();
            var nodeMap = readResult.Structures?.ToDictionary(n => n.Id, n => n) ?? new Dictionary<int, StructureReadDto>();

            if (readResult.Structures != null)
            {
                foreach (var node in readResult.Structures)
                {
                    info.Nodes.Add(new NodeGeometryInfo
                    {
                        NodeId = node.Id,
                        Label = node.Label,
                        NodeKind = node.RuntimeCategory ?? "Manhole",
                        GroundElevation = node.GroundElevation,
                        RimElevation = node.RimElevation,
                        InvertElevation = node.InvertElevation,
                        ElevationUnit = node.GroundElevationUnit?.Symbol
                    });
                }
            }

            if (readResult.Conduits != null)
            {
                foreach (var c in readResult.Conduits)
                {
                    double? usGround = null;
                    double? dsGround = null;
                    string elevUnit = null;

                    if (nodeMap.TryGetValue(c.StartNodeId, out var usNode))
                    {
                        usGround = usNode.GroundElevation;
                        elevUnit = usNode.GroundElevationUnit?.Symbol;
                    }
                    if (nodeMap.TryGetValue(c.StopNodeId, out var dsNode))
                    {
                        dsGround = dsNode.GroundElevation;
                        if (string.IsNullOrEmpty(elevUnit))
                            elevUnit = dsNode.GroundElevationUnit?.Symbol;
                    }

                    info.Conduits.Add(new ConduitGeometryInfo
                    {
                        ConduitId = c.Id,
                        Label = c.Label,
                        StartNodeId = c.StartNodeId,
                        StopNodeId = c.StopNodeId,
                        Length = c.Length,
                        LengthUnit = c.LengthUnit?.Symbol,
                        Diameter = c.Diameter ?? c.Rise,
                        DiameterUnit = c.DiameterUnit?.Symbol ?? c.RiseUnit?.Symbol,
                        StartInvert = c.StartInvert ?? c.UpstreamInvert,
                        StartInvertUnit = c.StartInvertUnit?.Symbol,
                        StopInvert = c.StopInvert ?? c.DownstreamInvert,
                        StopInvertUnit = c.StopInvertUnit?.Symbol,
                        UpstreamGroundElevation = usGround,
                        DownstreamGroundElevation = dsGround,
                        ElevationUnit = elevUnit,
                        ManningN = c.ManningsN
                    });
                }
            }

            return info;
        }
    }

    public static class NetworkResultComposer
    {
        public static HydraulicResultSet Compose(
            ResultCompositionContext context,
            GravityNetworkSnapshot snapshot,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            return Compose(context, NetworkGeometryInfo.FromSnapshot(snapshot), observations);
        }

        public static HydraulicResultSet Compose(
            ResultCompositionContext context,
            NetworkReadResult readResult,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            return Compose(context, NetworkGeometryInfo.FromNetworkRead(readResult), observations);
        }

        public static HydraulicResultSet Compose(
            ResultCompositionContext context,
            NetworkGeometryInfo network,
            IEnumerable<HydraulicMetricObservation> observations)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (network == null) network = new NetworkGeometryInfo();
            if (observations == null) observations = Enumerable.Empty<HydraulicMetricObservation>();

            if (string.IsNullOrWhiteSpace(context.ProjectId))
                throw new ArgumentException("ProjectId cannot be null or whitespace.", nameof(context));
            if (string.IsNullOrWhiteSpace(context.ModelFingerprint))
                throw new ArgumentException("ModelFingerprint cannot be null or whitespace.", nameof(context));

            // Validate Observations: No duplicates, no non-finite values, no context mismatch
            var obsList = observations.ToList();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var obs in obsList)
            {
                if (obs == null) continue;
                if (obs.ElementId <= 0)
                    throw new InvalidDataException("Observation element ID must be positive: " + obs.ElementId);

                string key = obs.ElementId.ToString(CultureInfo.InvariantCulture) + "|" + ((int)obs.Metric).ToString(CultureInfo.InvariantCulture);
                if (!seenKeys.Add(key))
                    throw new InvalidOperationException("DUPLICATE_OBSERVATION: Duplicate observation for element " + obs.ElementId + ", metric " + obs.Metric);

                if (obs.NumericValue.HasValue)
                {
                    double val = obs.NumericValue.Value;
                    if (double.IsNaN(val) || double.IsInfinity(val))
                        throw new InvalidDataException("NON_FINITE_OBSERVATION: Element " + obs.ElementId + " " + obs.Metric + " has non-finite value.");
                }

                // Context checks
                if (obs.ScenarioId.HasValue && obs.ScenarioId.Value != 0 && obs.ScenarioId.Value != context.ScenarioId)
                    throw new InvalidOperationException("SCENARIO_MISMATCH: Observation scenario " + obs.ScenarioId.Value + " does not match context scenario " + context.ScenarioId);

                if (obs.PhysicalAlternativeId.HasValue && obs.PhysicalAlternativeId.Value != 0 && obs.PhysicalAlternativeId.Value != context.PhysicalAlternativeId)
                    throw new InvalidOperationException("ALTERNATIVE_MISMATCH: Observation physical alternative " + obs.PhysicalAlternativeId.Value + " does not match context alternative " + context.PhysicalAlternativeId);

                if (!string.IsNullOrEmpty(obs.CalculationContext) && !string.IsNullOrEmpty(context.CalculationContext) &&
                    !string.Equals(obs.CalculationContext, context.CalculationContext, StringComparison.Ordinal))
                    throw new InvalidOperationException("CALCULATION_CONTEXT_MISMATCH: Observation context does not match composition context.");

                if (obs.TimeSeconds.HasValue && context.ResultTimeSeconds.HasValue &&
                    Math.Abs(obs.TimeSeconds.Value - context.ResultTimeSeconds.Value) > 1e-6)
                    throw new InvalidOperationException("TIMESTEP_MISMATCH: Observation time " + obs.TimeSeconds.Value + " does not match context time " + context.ResultTimeSeconds.Value);
            }

            // Map Elements: Gather all element IDs from network and observations
            var conduitMap = network.Conduits?.ToDictionary(c => c.ConduitId, c => c) ?? new Dictionary<int, ConduitGeometryInfo>();
            var nodeMap = network.Nodes?.ToDictionary(n => n.NodeId, n => n) ?? new Dictionary<int, NodeGeometryInfo>();
            var obsByElement = obsList.GroupBy(o => o.ElementId).ToDictionary(g => g.Key, g => g.ToList());

            var allElementIds = new SortedSet<int>();
            foreach (var id in conduitMap.Keys) allElementIds.Add(id);
            foreach (var id in nodeMap.Keys) allElementIds.Add(id);
            foreach (var id in obsByElement.Keys) allElementIds.Add(id);

            var elementResults = new List<HydraulicElementResult>();

            foreach (int elemId in allElementIds)
            {
                bool isConduit = conduitMap.ContainsKey(elemId);
                bool isNode = nodeMap.ContainsKey(elemId);
                string elemKind = isConduit ? "Conduit" : (isNode ? (nodeMap[elemId].NodeKind ?? "Manhole") : "Conduit");

                var elemObs = obsByElement.TryGetValue(elemId, out var list) ? list : new List<HydraulicMetricObservation>();
                if (!isConduit && !isNode && elemObs.Count > 0 && !string.IsNullOrEmpty(elemObs[0].ElementKind))
                {
                    elemKind = elemObs[0].ElementKind;
                    isConduit = string.Equals(elemKind, "Conduit", StringComparison.OrdinalIgnoreCase);
                }

                var elemResult = new HydraulicElementResult
                {
                    ElementId = elemId,
                    ElementKind = elemKind,
                    ScenarioId = context.ScenarioId,
                    PhysicalAlternativeId = context.PhysicalAlternativeId,
                    CalculationContext = context.CalculationContext,
                    ResultTimeSeconds = context.ResultTimeSeconds ?? 0.0,
                    Provenance = context.Provenance != null ? context.Provenance.Clone() : new HydraulicResultProvenance
                    {
                        SourceDescription = "Offline network result composition",
                        CalculationContext = context.CalculationContext,
                        EvidenceReference = context.CalculationRunId
                    }
                };

                var obsByMetric = elemObs.ToDictionary(o => o.Metric, o => o);

                // Native metrics: conduits declare Flow, Capacity, Velocity, Depth, HGL, Surcharge; structures declare HGL
                var nativeKinds = isConduit
                    ? new[]
                    {
                        HydraulicMetricKind.Flow,
                        HydraulicMetricKind.Capacity,
                        HydraulicMetricKind.Velocity,
                        HydraulicMetricKind.Depth,
                        HydraulicMetricKind.Hgl,
                        HydraulicMetricKind.Surcharge
                    }
                    : new[]
                    {
                        HydraulicMetricKind.Hgl
                    };

                foreach (var kind in nativeKinds)
                {
                    if (obsByMetric.TryGetValue(kind, out var o))
                    {
                        var prov = o.Provenance?.Clone() ?? elemResult.Provenance?.Clone() ?? new HydraulicResultProvenance();
                        if (!string.IsNullOrEmpty(o.EngineName) || !string.IsNullOrEmpty(o.FieldName))
                        {
                            string note = "Engine=" + (o.EngineName ?? "") + ";Field=" + (o.FieldName ?? "") + ";ReadStatus=" + (o.Status ?? "UNKNOWN");
                            prov.Notes = string.IsNullOrEmpty(prov.Notes) ? note : prov.Notes + "; " + note;
                        }

                        string unit = o.Unit;
                        if (string.Equals(unit, "VelocityFeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(unit, "FeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(unit, "FPS", StringComparison.OrdinalIgnoreCase))
                            unit = "ft/s";
                        else if (string.Equals(unit, "Feet", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(unit, "Foot", StringComparison.OrdinalIgnoreCase))
                            unit = "ft";
                        else if (string.Equals(unit, "CubicFeetPerSecond", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(unit, "CFS", StringComparison.OrdinalIgnoreCase))
                            unit = "ft3/s";

                        var metricResult = new HydraulicMetricResult
                        {
                            Metric = kind,
                            Unit = unit,
                            Availability = o.Availability,
                            Origin = HydraulicValueOrigin.BentleyCalculatedResult,
                            Provenance = prov
                        };

                        if (kind == HydraulicMetricKind.Surcharge)
                        {
                            metricResult.BooleanValue = o.BooleanValue;
                            metricResult.NumericValue = null;
                            if (string.IsNullOrEmpty(metricResult.Unit)) metricResult.Unit = "dimensionless";
                        }
                        else
                        {
                            metricResult.NumericValue = o.NumericValue;
                            metricResult.BooleanValue = null;
                        }

                        elemResult.Metrics.Add(metricResult);
                    }
                    else
                    {
                        // Explicitly represent unavailable native metric when omitted
                        string defaultUnit = kind == HydraulicMetricKind.Flow || kind == HydraulicMetricKind.Capacity ? "ft3/s" :
                            (kind == HydraulicMetricKind.Velocity ? "ft/s" :
                            (kind == HydraulicMetricKind.Depth || kind == HydraulicMetricKind.Hgl ? "ft" : "dimensionless"));
                        elemResult.Metrics.Add(HydraulicMetricResult.Missing(kind, defaultUnit, "Observation not supplied for " + kind));
                    }
                }

                // Derived metrics for conduits: DepthRatio, Slope, Cover
                if (isConduit)
                {
                    conduitMap.TryGetValue(elemId, out var conduit);

                    // 1. DepthRatio (d/D)
                    var depthMetric = elemResult.FindMetric(HydraulicMetricKind.Depth);
                    if (depthMetric != null && depthMetric.Availability == HydraulicResultAvailability.Available &&
                        depthMetric.NumericValue.HasValue && conduit != null && conduit.Diameter.HasValue)
                    {
                        var derivedRatio = HydraulicDerivedMetrics.DepthRatio(
                            depthMetric.NumericValue.Value,
                            depthMetric.Unit,
                            conduit.Diameter.Value,
                            conduit.DiameterUnit,
                            elemResult.Provenance);
                        elemResult.Metrics.Add(derivedRatio);
                    }
                    else
                    {
                        string reason = depthMetric == null || depthMetric.Availability != HydraulicResultAvailability.Available ? "Observed Depth is missing or unavailable." :
                            (conduit == null || !conduit.Diameter.HasValue ? "Conduit diameter is missing." : "Depth is not available.");
                        elemResult.Metrics.Add(HydraulicMetricResult.Missing(HydraulicMetricKind.DepthRatio, "dimensionless", reason));
                    }

                    // 2. Slope
                    if (conduit != null && conduit.StartInvert.HasValue && conduit.StopInvert.HasValue &&
                        conduit.Length.HasValue && conduit.Length.Value > 0)
                    {
                        string invUnit = conduit.StartInvertUnit ?? conduit.StopInvertUnit;
                        string lenUnit = conduit.LengthUnit;
                        if (!string.IsNullOrEmpty(invUnit) && !string.IsNullOrEmpty(lenUnit) &&
                            !string.Equals(invUnit, lenUnit, StringComparison.OrdinalIgnoreCase))
                        {
                            elemResult.Metrics.Add(HydraulicMetricResult.Missing(HydraulicMetricKind.Slope, "dimensionless",
                                "Invert unit (" + invUnit + ") and length unit (" + lenUnit + ") differ; no conversion is applied."));
                        }
                        else
                        {
                            var derivedSlope = HydraulicDerivedMetrics.Slope(
                                conduit.StartInvert.Value,
                                conduit.StopInvert.Value,
                                conduit.Length.Value,
                                lenUnit ?? invUnit ?? "ft",
                                elemResult.Provenance);
                            elemResult.Metrics.Add(derivedSlope);
                        }
                    }
                    else
                    {
                        string reason = conduit == null ? "Conduit geometry is missing." :
                            (!conduit.StartInvert.HasValue || !conduit.StopInvert.HasValue ? "Start/stop inverts are missing." : "Conduit length is missing or zero.");
                        elemResult.Metrics.Add(HydraulicMetricResult.Missing(HydraulicMetricKind.Slope, "dimensionless", reason));
                    }

                    // 3. Cover
                    if (conduit != null && conduit.UpstreamGroundElevation.HasValue && conduit.StartInvert.HasValue && conduit.Diameter.HasValue)
                    {
                        double ground = conduit.UpstreamGroundElevation.Value;
                        double invert = conduit.StartInvert.Value;
                        double diameter = conduit.Diameter.Value;
                        string eUnit = conduit.ElevationUnit ?? conduit.StartInvertUnit ?? conduit.DiameterUnit ?? "ft";

                        string invU = conduit.StartInvertUnit ?? eUnit;
                        string diaU = conduit.DiameterUnit ?? eUnit;
                        if (!string.Equals(eUnit, invU, StringComparison.OrdinalIgnoreCase) || !string.Equals(eUnit, diaU, StringComparison.OrdinalIgnoreCase))
                        {
                            elemResult.Metrics.Add(HydraulicMetricResult.Missing(HydraulicMetricKind.Cover, eUnit,
                                "Ground elevation, invert, and diameter units differ; no conversion is applied."));
                        }
                        else
                        {
                            double crown = invert + diameter;
                            double cover = ground - crown;
                            elemResult.Metrics.Add(new HydraulicMetricResult
                            {
                                Metric = HydraulicMetricKind.Cover,
                                NumericValue = cover,
                                Unit = eUnit,
                                Availability = HydraulicResultAvailability.Available,
                                Origin = HydraulicValueOrigin.VerifiedDerivedQuantity,
                                Derivation = "cover = groundElevation - (startInvert + diameter)",
                                DerivationInputs = "ground=" + ground.ToString("R", CultureInfo.InvariantCulture) + " " + eUnit +
                                    "; invert=" + invert.ToString("R", CultureInfo.InvariantCulture) + " " + invU +
                                    "; diameter=" + diameter.ToString("R", CultureInfo.InvariantCulture) + " " + diaU,
                                Provenance = elemResult.Provenance?.Clone()
                            });
                        }
                    }
                    else
                    {
                        elemResult.Metrics.Add(HydraulicMetricResult.Missing(HydraulicMetricKind.Cover, conduit?.ElevationUnit ?? "ft",
                            "Verified ground elevation, invert, and diameter required."));
                    }
                }

                // Deterministic metric ordering by enum int
                elemResult.Metrics = elemResult.Metrics.OrderBy(m => (int)m.Metric).ToList();
                elementResults.Add(elemResult);
            }

            // Deterministic element ordering by ElementId, then ElementKind
            elementResults = elementResults.OrderBy(e => e.ElementId).ThenBy(e => e.ElementKind, StringComparer.Ordinal).ToList();

            var resultSet = new HydraulicResultSet
            {
                SchemaVersion = 1,
                ProjectId = context.ProjectId,
                ModelFingerprint = context.ModelFingerprint,
                NetworkFingerprint = context.NetworkFingerprint,
                CandidateFingerprint = context.CandidateFingerprint,
                ScenarioId = context.ScenarioId,
                PhysicalAlternativeId = context.PhysicalAlternativeId,
                CalculationContext = context.CalculationContext,
                CalculationRunId = context.CalculationRunId,
                ResultTimeSeconds = context.ResultTimeSeconds ?? 0.0,
                IsTestOnly = context.IsTestOnly,
                Elements = elementResults,
                Provenance = context.Provenance != null ? context.Provenance.Clone() : new HydraulicResultProvenance
                {
                    SourceDescription = "Offline network result composition",
                    CalculationContext = context.CalculationContext,
                    EvidenceReference = context.CalculationRunId
                }
            };

            return resultSet;
        }

        public static MunicipalRuleEvaluationReport EvaluateMunicipalRules(
            HydraulicResultSet results,
            NetworkGeometryInfo geometry,
            MunicipalHydraulicRuleEngine engine = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            var eng = engine ?? new MunicipalHydraulicRuleEngine();
            return eng.EvaluateNetwork(results, geometry);
        }
    }
}
