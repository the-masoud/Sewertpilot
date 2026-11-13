using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // No parallel unit/value infrastructure: the vertical criterion reuses the P8
    // StandardValueKind (Number = dimensionless ratio, Quantity = feet length).
    public sealed class GravityVerticalCriterionSelection
    {
        public string ParameterKey { get; }
        public GravityFlowSectionIdentifier BasisSection { get; }
        public string BasisCriterionKey { get; }
        public StandardSourceLayer StandardLayer { get; }
        public string StandardId { get; }
        public string StandardRevision { get; }
        public StandardValueKind Quantity { get; }
        public GravityVerticalCriterionSelection(string parameterKey, GravityFlowSectionIdentifier basisSection, string basisCriterionKey,
            StandardSourceLayer standardLayer, string standardId, string standardRevision, StandardValueKind quantity)
        {
            ParameterKey = parameterKey; BasisSection = basisSection; BasisCriterionKey = basisCriterionKey;
            StandardLayer = standardLayer; StandardId = standardId; StandardRevision = standardRevision; Quantity = quantity;
        }
    }
    // Explicit request only. No inferred keys, implicit standard selection or persisted second decision store.
    public sealed class GravityVerticalPreparationRequest
    {
        public ReadOnlyCollection<StandardDefinition> Standards { get; }
        public StandardsResolutionOptions SelectedStandards { get; }
        public GravityVerticalCriterionSelection MinimumSlope { get; }
        public GravityVerticalCriterionSelection MaximumSlope { get; }
        public GravityVerticalCriterionSelection MinimumCover { get; }
        public GravityAutomaticPreparationContext Current { get; }
        public string DesignBasisId { get; }
        public string DesignBasisVersion { get; }
        public GravityVerticalPreparationRequest(IEnumerable<StandardDefinition> standards, StandardsResolutionOptions selectedStandards,
            GravityVerticalCriterionSelection minimumSlope, GravityVerticalCriterionSelection maximumSlope,
            GravityVerticalCriterionSelection minimumCover, GravityAutomaticPreparationContext current, string designBasisId, string designBasisVersion)
        {
            Standards = Array.AsReadOnly((standards ?? Enumerable.Empty<StandardDefinition>()).Select(Clone).ToArray());
            SelectedStandards = selectedStandards == null ? null : Clone(selectedStandards);
            MinimumSlope = minimumSlope; MaximumSlope = maximumSlope; MinimumCover = minimumCover; Current = current;
            DesignBasisId = designBasisId; DesignBasisVersion = designBasisVersion;
        }
        private static T Clone<T>(T value) => RuntimeHostProtocol.Serializer().Deserialize<T>(GravityExecutionPlanBuilder.Json(value));
        internal GravityVerticalPreparationRequest WithConstraintFingerprint(string fingerprint) => new GravityVerticalPreparationRequest(Standards,
            SelectedStandards, MinimumSlope, MaximumSlope, MinimumCover, Current == null ? null : new GravityAutomaticPreparationContext(
                Current.ModelFingerprint, Current.NetworkFingerprint, Current.Scenario, Current.ActivePolicyId, Current.ActivePolicyVersion, fingerprint),
            DesignBasisId, DesignBasisVersion);
    }
    public sealed class GravityVerticalCriterionAssessment
    {
        public string Role { get; }
        public GravityVerticalCriterionSelection Selection { get; }
        public ResolvedStandardValue Resolution { get; }
        public GravityDesignCriterion BasisCriterion { get; }
        public double? NumericValue { get; }
        public ReadOnlyCollection<string> UnresolvedReasons { get; }
        public bool Eligible => NumericValue.HasValue && UnresolvedReasons.Count == 0;
        internal GravityVerticalCriterionAssessment(string role, GravityVerticalCriterionSelection selection, ResolvedStandardValue resolution,
            GravityDesignCriterion criterion, double? value, IEnumerable<string> reasons)
        {
            Role = role; Selection = selection; Resolution = resolution; BasisCriterion = criterion?.Clone(); NumericValue = value;
            UnresolvedReasons = Array.AsReadOnly(reasons.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray());
        }
    }
    public sealed class GravityVerticalPreparationItem
    {
        public string Kind { get; }
        public int ConduitId { get; }
        public ConduitEndKind? End { get; }
        public string Status { get; }
        public ReadOnlyCollection<string> Reasons { get; }
        internal GravityVerticalPreparationItem(string kind, int id, ConduitEndKind? end, string status, IEnumerable<string> reasons)
        { Kind = kind; ConduitId = id; End = end; Status = status; Reasons = Array.AsReadOnly(reasons.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()); }
    }
    public sealed class GravityOutfallBoundaryAssessment
    {
        public GravityOutfallPreparationEvidence Evidence { get; }
        public bool SourceElevationObserved { get; }
        public bool DesignBoundaryAuthorized { get; }
        public bool ObservationAuthorizesBoundary => false;
        public string MissingAuthority { get; }
        internal GravityOutfallBoundaryAssessment(GravityOutfallPreparationEvidence evidence)
        {
            Evidence = evidence; SourceElevationObserved = GravityAutomaticVerticalPreparationService.Finite(evidence.SourceElevations.InvertElevation);
            DesignBoundaryAuthorized = evidence.Classifications.Contains("EXPLICIT_BOUNDARY_CONFIRMED");
            MissingAuthority = DesignBoundaryAuthorized ? null : "EXPLICIT_PROTECTED_OR_CONNECTED_OUTFALL_DECISION_REQUIRED";
        }
    }
    public sealed class GravityVerticalPreparationResult
    {
        public ReadOnlyCollection<string> ContextFailures { get; }
        public ReadOnlyCollection<GravityVerticalCriterionAssessment> Criteria { get; }
        public ReadOnlyCollection<GravityVerticalPreparationItem> Items { get; }
        public ReadOnlyCollection<VerticalSlopeConstraint> Slopes { get; }
        public ReadOnlyCollection<VerticalCoverConstraint> Covers { get; }
        public ReadOnlyCollection<GravityOutfallBoundaryAssessment> Outfalls { get; }
        public bool HasStaleDerivedDecisions => Items.Any(i => i.Status == "STALE_DERIVED");
        internal GravityVerticalPreparationResult(IEnumerable<string> failures, IEnumerable<GravityVerticalCriterionAssessment> criteria,
            IEnumerable<GravityVerticalPreparationItem> items, IEnumerable<VerticalSlopeConstraint> slopes,
            IEnumerable<VerticalCoverConstraint> covers, IEnumerable<GravityOutfallPreparationEvidence> outfalls)
        {
            ContextFailures = Array.AsReadOnly(failures.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()); Criteria = Array.AsReadOnly(criteria.ToArray());
            Items = Array.AsReadOnly(items.ToArray()); Slopes = Array.AsReadOnly(slopes.ToArray()); Covers = Array.AsReadOnly(covers.ToArray());
            Outfalls = Array.AsReadOnly(outfalls.Select(e => new GravityOutfallBoundaryAssessment(e)).ToArray());
        }
    }

    /// <summary>Pure preparation from explicitly resolved criteria; existing P13 performs every mathematical propagation.</summary>
    public sealed class GravityAutomaticVerticalPreparationService
    {
        private const string Marker = "AUTO_VERTICAL_FROM_RESOLVED_CRITERIA_V1\n";
        public GravityVerticalPreparationResult Prepare(GravityNetworkSnapshot network, GravityFlowDesignBasis basis,
            GravityVerticalDesignConstraintSet constraints, GravityVerticalPreparationRequest request)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (request == null) throw new ArgumentNullException(nameof(request));
            var automatic = new GravityAutomaticPreparationService().Prepare(network, constraints,
                request.Current?.ActivePolicyId, request.Current?.ActivePolicyVersion, request.Current);
            var failures = automatic.ContextFailures.ToList();
            if (basis?.CommonDesignBasis == null || string.IsNullOrWhiteSpace(request.DesignBasisId) || string.IsNullOrWhiteSpace(request.DesignBasisVersion) ||
                request.DesignBasisId != basis.CommonDesignBasis.DesignBasisId || request.DesignBasisVersion != basis.CommonDesignBasis.DesignBasisVersion)
                failures.Add("DESIGN_BASIS_CONTEXT_STALE");
            if (basis != null && !basis.Validate().IsValid) failures.Add("DESIGN_BASIS_INVALID");
            if (constraints != null && (constraints.DesignBasisId != basis?.CommonDesignBasis?.DesignBasisId ||
                constraints.DesignBasisVersion != basis?.CommonDesignBasis?.DesignBasisVersion)) failures.Add("CONSTRAINT_BASIS_STALE");
            if (request.SelectedStandards == null) failures.Add("EXPLICIT_STANDARD_CONTEXT_REQUIRED");
            var minimum = Resolve("MinimumSlope", request.MinimumSlope, StandardValueKind.Number, request, basis);
            var maximum = request.MaximumSlope == null ? null : Resolve("MaximumSlope", request.MaximumSlope, StandardValueKind.Number, request, basis);
            var cover = Resolve("MinimumCover", request.MinimumCover, StandardValueKind.Quantity, request, basis);
            var criteria = new[] { minimum, maximum, cover }.Where(c => c != null).ToArray();
            var items = new List<GravityVerticalPreparationItem>(); var slopes = new List<VerticalSlopeConstraint>(); var covers = new List<VerticalCoverConstraint>();
            foreach (var c in network.Conduits.OrderBy(c => c.SourceElementId))
            {
                var identity = new List<string>(failures);
                if (c.SourceElementId <= 0 || network.Conduits.Count(x => x.SourceElementId == c.SourceElementId) != 1) identity.Add("INVALID_OR_DUPLICATE_CONDUIT");
                var slopeReasons = new List<string>(identity); slopeReasons.AddRange(minimum.UnresolvedReasons);
                if (new[] { c.StartNodeId, c.StopNodeId }.Any(id => id <= 0 || network.Nodes.Count(n => n.SourceElementId == id) != 1))
                    slopeReasons.Add("ACTUAL_CONDUIT_ENDPOINTS_UNRESOLVED");
                if (maximum != null) slopeReasons.AddRange(maximum.UnresolvedReasons);
                if (!Finite(c.ExistingPhysicalState.Length) || c.ExistingPhysicalState.Length <= 0) slopeReasons.Add("FINITE_POSITIVE_LENGTH_REQUIRED");
                if (!Feet(c.ExistingPhysicalState.LengthUnit)) slopeReasons.Add("LENGTH_FEET_UNIT_REQUIRED");
                GravityDesignOrientation orientation = null;
                if (constraints == null || !constraints.TryGetOrientation(c.SourceElementId, out orientation)) slopeReasons.Add("ACCEPTED_ORIENTATION_REQUIRED");
                else if (!ValidProvenance(orientation.Provenance) || (!orientation.IsStartToStop(c.StartNodeId, c.StopNodeId) && !orientation.IsStopToStart(c.StartNodeId, c.StopNodeId)))
                    slopeReasons.Add("ORIENTATION_INVALID");
                else if (GravityAutomaticPreparationService.IsDerived(orientation) && !automatic.Components.Any(x => x.Eligible && x.ConduitIds.Contains(c.SourceElementId)))
                    slopeReasons.Add("AUTOMATIC_ORIENTATION_STALE");
                if (minimum.Eligible && maximum?.Eligible == true && minimum.NumericValue > maximum.NumericValue) slopeReasons.Add("MINIMUM_SLOPE_EXCEEDS_MAXIMUM");
                var slope = slopeReasons.Count == 0 ? new VerticalSlopeConstraint(c.SourceElementId, minimum.NumericValue, maximum?.NumericValue,
                    Provenance(network, basis, request, criteria, minimum.Resolution.EffectiveProvenance, "Slope", c, null, orientation, null)) : null;
                string slopeStatus = Existing("Slope", c.SourceElementId, null, slope, constraints?.SlopeConstraints.Where(s => s.ConduitId == c.SourceElementId).Cast<object>(), slopeReasons, items);
                if (slopeStatus == "PREPARED") slopes.Add(slope);
                foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                {
                    var reasons = new List<string>(identity); reasons.AddRange(cover.UnresolvedReasons);
                    int nodeId = end == ConduitEndKind.Start ? c.StartNodeId : c.StopNodeId;
                    var nodes = network.Nodes.Where(n => n.SourceElementId == nodeId).ToArray();
                    var node = nodes.Length == 1 && nodeId > 0 ? nodes[0] : null;
                    if (node == null) reasons.Add("CONNECTED_NODE_UNRESOLVED:" + N(nodeId));
                    else
                    {
                        if (!Finite(node.ExistingElevationState.GroundElevation)) reasons.Add("GROUND_ELEVATION_FINITE_VALUE_REQUIRED");
                        if (!Feet(node.ExistingElevationState.GroundElevationUnit)) reasons.Add("GROUND_ELEVATION_FEET_UNIT_REQUIRED");
                        if (!Proven(node.PropertyClassifications, "GroundElevation")) reasons.Add("GROUND_ELEVATION_CLASSIFICATION_NOT_PROVEN");
                    }
                    bool hasEffectiveHeight = c.ExistingPhysicalState.EffectiveSectionHeight.HasValue &&
                        Proven(c.PropertyClassifications, "EffectiveSectionHeight");
                    if (hasEffectiveHeight)
                    {
                        if (!Finite(c.ExistingPhysicalState.EffectiveSectionHeight.Value) || c.ExistingPhysicalState.EffectiveSectionHeight.Value <= 0)
                            reasons.Add("RISE_FINITE_POSITIVE_VALUE_REQUIRED");
                        if (!Feet(c.ExistingPhysicalState.EffectiveSectionHeightUnit ?? c.ExistingPhysicalState.DiameterUnit ?? c.ExistingPhysicalState.RiseUnit))
                            reasons.Add("RISE_FEET_UNIT_REQUIRED");
                        if (c.ExistingPhysicalState.Rise.HasValue && c.ExistingPhysicalState.Rise.Value > 0 &&
                            Math.Abs(c.ExistingPhysicalState.Rise.Value - c.ExistingPhysicalState.EffectiveSectionHeight.Value) > 1e-4)
                        {
                            reasons.Add("SECTION_HEIGHT_CONFLICT");
                        }
                    }
                    else
                    {
                        bool isCircular = !string.IsNullOrWhiteSpace(c.ExistingPhysicalState.Shape) &&
                            string.Equals(c.ExistingPhysicalState.Shape.Trim(), "CircularPipe", StringComparison.OrdinalIgnoreCase);
                        if (isCircular)
                        {
                            if (c.ExistingPhysicalState.Rise.HasValue && c.ExistingPhysicalState.Rise.Value > 0 &&
                                c.ExistingPhysicalState.Diameter.HasValue &&
                                Math.Abs(c.ExistingPhysicalState.Rise.Value - c.ExistingPhysicalState.Diameter.Value) > 1e-4)
                            {
                                reasons.Add("SECTION_HEIGHT_CONFLICT");
                            }
                            if (!c.ExistingPhysicalState.Diameter.HasValue || !Finite(c.ExistingPhysicalState.Diameter.Value) || c.ExistingPhysicalState.Diameter.Value <= 0)
                                reasons.Add("DIAMETER_FINITE_POSITIVE_VALUE_REQUIRED");
                            if (c.ExistingPhysicalState.Diameter.HasValue && !Feet(c.ExistingPhysicalState.DiameterUnit))
                                reasons.Add("DIAMETER_FEET_UNIT_REQUIRED");
                            if (c.ExistingPhysicalState.Diameter.HasValue && !Proven(c.PropertyClassifications, "Diameter"))
                                reasons.Add("DIAMETER_CLASSIFICATION_NOT_PROVEN");
                        }
                        else
                        {
                            if (!Finite(c.ExistingPhysicalState.Rise) || c.ExistingPhysicalState.Rise <= 0) reasons.Add("RISE_FINITE_POSITIVE_VALUE_REQUIRED");
                            if (!Feet(c.ExistingPhysicalState.RiseUnit)) reasons.Add("RISE_FEET_UNIT_REQUIRED");
                            if (!Proven(c.PropertyClassifications, "Rise")) reasons.Add("RISE_CLASSIFICATION_NOT_PROVEN");
                        }
                    }
                    var value = reasons.Count == 0 ? new VerticalCoverConstraint(new GravityConduitEnd(c.SourceElementId, end, nodeId),
                        new SourceEngineeringValue(node.ExistingElevationState.GroundElevation.Value, node.ExistingElevationState.GroundElevationUnit),
                        new SourceEngineeringValue(cover.NumericValue.Value, new SourceUnitIdentity("length", "Feet", "feet", "ft")),
                        Provenance(network, basis, request, criteria, cover.Resolution.EffectiveProvenance, "Cover", c, end, null, node)) : null;
                    string status = Existing("Cover", c.SourceElementId, end, value,
                        constraints?.CoverConstraints.Where(v => v.ConduitEnd.ConduitId == c.SourceElementId && v.ConduitEnd.End == end).Cast<object>(), reasons, items);
                    if (status == "PREPARED") covers.Add(value);
                }
            }
            return new GravityVerticalPreparationResult(failures, criteria, items, slopes, covers, automatic.Outfalls);
        }

        private static GravityVerticalCriterionAssessment Resolve(string role, GravityVerticalCriterionSelection selection,
            StandardValueKind expected, GravityVerticalPreparationRequest request, GravityFlowDesignBasis basis)
        {
            var reasons = new List<string>(); ResolvedStandardValue resolved = null; GravityDesignCriterion criterion = null; double? value = null;
            if (selection == null) reasons.Add(role + ":EXPLICIT_PARAMETER_MAPPING_REQUIRED");
            else
            {
                if (string.IsNullOrWhiteSpace(selection.ParameterKey) || string.IsNullOrWhiteSpace(selection.BasisCriterionKey) ||
                    !Enum.IsDefined(typeof(GravityFlowSectionIdentifier), selection.BasisSection) || selection.Quantity != expected) reasons.Add(role + ":PARAMETER_MAPPING_INVALID");
                if (string.IsNullOrWhiteSpace(selection.StandardId) || string.IsNullOrWhiteSpace(selection.StandardRevision) ||
                    !Enum.IsDefined(typeof(StandardSourceLayer), selection.StandardLayer)) reasons.Add(role + ":EXPLICIT_STANDARD_IDENTITY_REQUIRED");
                if (request.SelectedStandards == null) reasons.Add(role + ":EXPLICIT_STANDARD_CONTEXT_REQUIRED");
                if (reasons.Count == 0)
                {
                    try
                    {
                        // Validate explicit IDs BEFORE Resolve, whose normal API can fall back when a selected ID is absent.
                        foreach (var layer in Enum.GetValues(typeof(StandardSourceLayer)).Cast<StandardSourceLayer>())
                        {
                            string selectedId = Selected(request.SelectedStandards, layer);
                            if (selectedId != null && !request.Standards.Any(s => s != null && s.SourceLayer == layer && s.StandardId == selectedId))
                                reasons.Add(role + ":SELECTED_STANDARD_UNAVAILABLE:" + selectedId);
                            if (selectedId != null && request.Standards.Any(s => s != null && s.SourceLayer == layer && HasParameter(s, selection.ParameterKey)) &&
                                !request.Standards.Any(s => s != null && s.SourceLayer == layer && s.StandardId == selectedId && HasParameter(s, selection.ParameterKey)))
                                reasons.Add(role + ":SELECTED_STANDARD_PARAMETER_UNAVAILABLE:" + selectedId);
                        }
                        if (selection.StandardLayer != StandardSourceLayer.ApplicationDefault && Selected(request.SelectedStandards, selection.StandardLayer) != selection.StandardId)
                            reasons.Add(role + ":SELECTED_STANDARD_IDENTITY_MISMATCH");
                        if (request.Standards.Count(s => s != null && s.SourceLayer == selection.StandardLayer && s.StandardId == selection.StandardId) > 1)
                            reasons.Add(role + ":AMBIGUOUS_STANDARD_IDENTITY");
                        if (reasons.Count == 0)
                        {
                            resolved = StandardsResolver.Resolve(selection.ParameterKey, request.Standards, request.SelectedStandards);
                            if (resolved.Status != StandardResolutionStatus.Defined) reasons.Add(role + ":STANDARD_VALUE_NOT_DEFINED");
                            else if (!ValidProvenance(resolved.EffectiveProvenance) || resolved.EffectiveSource != selection.StandardLayer ||
                                resolved.EffectiveStandardId != selection.StandardId || resolved.EffectiveVersion != selection.StandardRevision ||
                                string.IsNullOrWhiteSpace(resolved.EffectiveProvenance.StandardVersion)) reasons.Add(role + ":RESOLVED_STANDARD_CONTEXT_MISMATCH");
                        }
                        var matches = basis?.GetSection(selection.BasisSection)?.Criteria?.Where(c => c != null &&
                            string.Equals(c.Key, selection.BasisCriterionKey, StringComparison.OrdinalIgnoreCase)).ToArray() ?? new GravityDesignCriterion[0];
                        if (matches.Length != 1 || matches[0].Status != GravityCriterionStatus.Defined) reasons.Add(role + ":DEFINED_BASIS_CRITERION_REQUIRED");
                        else
                        {
                            criterion = matches[0];
                            if (resolved?.Status == StandardResolutionStatus.Defined && (!resolved.EffectiveValue.Equals(criterion.Value) ||
                                GravityExecutionPlanBuilder.Json(resolved.EffectiveProvenance) != GravityExecutionPlanBuilder.Json(criterion.Provenance)))
                                reasons.Add(role + ":BASIS_VALUE_OR_PROVENANCE_MISMATCH");
                        }
                        if (resolved?.Status == StandardResolutionStatus.Defined)
                        {
                            var v = resolved.EffectiveValue;
                            if (expected == StandardValueKind.Number)
                            {
                                if (v.Kind != StandardValueKind.Number || !Finite(v.NumberValue)) reasons.Add(role + ":DIMENSIONLESS_NUMBER_RATIO_REQUIRED_NO_PERCENT_CONVERSION");
                                else value = v.NumberValue;
                            }
                            else if (v.Kind != StandardValueKind.Quantity || v.QuantityValue == null || !Finite(v.QuantityValue.Value) ||
                                (v.QuantityValue.Unit != "ft" && v.QuantityValue.Unit != "Feet")) reasons.Add(role + ":FINITE_FEET_LENGTH_QUANTITY_REQUIRED");
                            else value = v.QuantityValue.Value;
                        }
                    }
                    catch (StandardsResolutionException e) { reasons.Add(role + ":" + e.Classification); }
                    catch (StandardsValidationException e) { reasons.Add(role + ":" + e.Classification); }
                    catch (ArgumentException) { reasons.Add(role + ":PARAMETER_MAPPING_INVALID"); }
                }
            }
            return new GravityVerticalCriterionAssessment(role, selection, resolved, criterion, value, reasons);
        }
        private static bool HasParameter(StandardDefinition s, string key) => s.Parameters != null && s.Parameters.Any(p => p != null &&
            new StandardParameterKey(p.Key).CanonicalKey == new StandardParameterKey(key).CanonicalKey);
        private static string Selected(StandardsResolutionOptions o, StandardSourceLayer layer) => layer == StandardSourceLayer.PublishedStandard ? o.SelectedPublishedStandardId :
            layer == StandardSourceLayer.CompanyStandard ? o.SelectedCompanyStandardId : layer == StandardSourceLayer.ProjectOverride ? o.SelectedProjectOverrideId : null;
        internal static bool Finite(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        private static bool Feet(SourceUnitIdentity unit) => new SourceUnitIdentity("length", "Feet", "feet", "ft").Equals(unit);
        private static bool Proven(IReadOnlyDictionary<string, string> classifications, string key) => classifications.TryGetValue(key, out var status) && status == "PROVEN";
        private static bool ValidProvenance(StandardProvenance p) => p != null && Enum.IsDefined(typeof(StandardSourceLayer), p.SourceLayer) &&
            !string.IsNullOrWhiteSpace(p.ParameterKey) && !string.IsNullOrWhiteSpace(p.StandardId) && !string.IsNullOrWhiteSpace(p.StandardVersion);
        private static string N(int id) => id.ToString(CultureInfo.InvariantCulture);
        private static string Key(string kind, int id, ConduitEndKind? end) => "auto.vertical." + kind.ToLowerInvariant() + "." + N(id) + (end.HasValue ? "." + end.Value : "");
        private static StandardProvenance DecisionProvenance(object d) => (StandardProvenance)d.GetType().GetProperty("Provenance").GetValue(d, null);
        internal static bool IsDerivedDecision(object d)
        {
            if (!(d is VerticalSlopeConstraint) && !(d is VerticalCoverConstraint)) return false;
            var p = DecisionProvenance(d); var slope = d as VerticalSlopeConstraint; var cover = d as VerticalCoverConstraint;
            return p?.Notes?.StartsWith(Marker, StringComparison.Ordinal) == true && p.ParameterKey == (slope != null ?
                Key("Slope", slope.ConduitId, null) : Key("Cover", cover.ConduitEnd.ConduitId, cover.ConduitEnd.End));
        }
        private static string Existing(string kind, int id, ConduitEndKind? end, object proposed, IEnumerable<object> existing,
            List<string> reasons, List<GravityVerticalPreparationItem> items)
        {
            var old = existing?.ToArray() ?? new object[0]; string status;
            if (old.Length > 1) { reasons.Add("MULTIPLE_EXISTING_" + kind.ToUpperInvariant() + "_DECISIONS"); status = old.Any(IsDerivedDecision) ? "STALE_DERIVED" : "UNRESOLVED"; }
            else if (old.Length == 1 && !IsDerivedDecision(old[0])) status = "EXPLICIT_PRESERVED";
            else if (old.Length == 1)
            {
                status = proposed != null && GravityExecutionPlanBuilder.Json(old[0]) == GravityExecutionPlanBuilder.Json(proposed) ? "CURRENT_DERIVED" : "STALE_DERIVED";
                if (status == "STALE_DERIVED") reasons.Add("DERIVED_INPUTS_CHANGED_EXPLICIT_DRAFT_REVISION_REQUIRED");
            }
            else status = proposed != null ? "PREPARED" : "UNRESOLVED";
            items.Add(new GravityVerticalPreparationItem(kind, id, end, status, reasons)); return status;
        }
        private static StandardProvenance Provenance(GravityNetworkSnapshot network, GravityFlowDesignBasis basis, GravityVerticalPreparationRequest request,
            IEnumerable<GravityVerticalCriterionAssessment> criteria, StandardProvenance primary, string kind,
            GravityNetworkConduit c, ConduitEndKind? end, GravityDesignOrientation orientation, GravityNetworkNode node)
        {
            var p = RuntimeHostProtocol.Serializer().Deserialize<StandardProvenance>(GravityExecutionPlanBuilder.Json(primary));
            p.ParameterKey = Key(kind, c.SourceElementId, end);
            var selectedContent = request.Standards.Where(s => s != null).OrderBy(s => (int)s.SourceLayer).ThenBy(s => s.StandardId, StringComparer.Ordinal)
                .ThenBy(s => s.Version, StringComparer.Ordinal).Select(s => new { s.StandardId, s.Version, s.Name, s.SourceLayer,
                    Parameters = s.Parameters?.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray(), s.ProvenanceMetadata }).ToArray();
            string standardContext = GravityExecutionPlanBuilder.Json(new { request.SelectedStandards, Standards = selectedContent });
            string fingerprint;
            using (var sha = SHA256.Create()) fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(standardContext))).Replace("-", "").ToLowerInvariant();
            // Existing provenance Notes carries auditable decision evidence, not a second decision serializer/store.
            p.Notes = Marker + GravityExecutionPlanBuilder.Json(new { Method = kind == "Slope" ? "ResolvedDimensionlessRatioBounds" : "ResolvedGroundToCrownMinimumCover",
                network.SourceModelFingerprint, network.NetworkFingerprint, network.ScenarioContext, request.Current.ActivePolicyId, request.Current.ActivePolicyVersion,
                basis.CommonDesignBasis.DesignBasisId, basis.CommonDesignBasis.DesignBasisVersion, StandardContextFingerprint = fingerprint,
                Criteria = criteria.Select(v => new { v.Role, v.Selection, v.BasisCriterion, EffectiveProvenance = v.Resolution?.EffectiveProvenance, EffectiveValue = v.Resolution?.EffectiveValue }).ToArray(),
                c.SourceElementId, c.StartNodeId, c.StopNodeId, End = end, Orientation = orientation,
                Length = kind == "Slope" ? c.ExistingPhysicalState.Length : null,
                LengthUnit = kind == "Slope" ? c.ExistingPhysicalState.LengthUnit : null,
                Ground = node?.ExistingElevationState.GroundElevation, GroundUnit = node?.ExistingElevationState.GroundElevationUnit,
                GroundClassification = node == null ? null : node.PropertyClassifications["GroundElevation"],
                Rise = kind == "Cover" ? c.ExistingPhysicalState.Rise : null, RiseUnit = kind == "Cover" ? c.ExistingPhysicalState.RiseUnit : null,
                RiseClassification = kind == "Cover" ? c.PropertyClassifications["Rise"] : null });
            return p;
        }
    }
}
