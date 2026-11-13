using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum AssumptionProvenanceKind
    {
        ProjectInput = 1,
        EngineerAssumption = 2,
        ResolvedStandard = 3,
        ImportedReference = 4
    }

    public enum DesignBasisReviewState
    {
        Draft = 1,
        ReadyForReview = 2,
        Reviewed = 3,
        NeedsRevision = 4
    }

    public enum DesignBasisValidationSeverity
    {
        Error = 1,
        Warning = 2
    }

    public sealed class DesignBasisValidationIssue
    {
        public string Code { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
        public DesignBasisValidationSeverity Severity { get; set; } = DesignBasisValidationSeverity.Error;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}: {2} ({3})", Severity, Field, Message, Code);
        }
    }

    public sealed class DesignBasisValidationResult
    {
        public List<DesignBasisValidationIssue> Issues { get; set; } = new List<DesignBasisValidationIssue>();

        public bool IsValid
        {
            get
            {
                if (Issues == null || Issues.Count == 0) return true;
                foreach (var issue in Issues)
                {
                    if (issue != null && issue.Severity == DesignBasisValidationSeverity.Error)
                        return false;
                }
                return true;
            }
        }

        public void AddError(string code, string field, string message)
        {
            Issues.Add(new DesignBasisValidationIssue
            {
                Code = code,
                Field = field,
                Message = message,
                Severity = DesignBasisValidationSeverity.Error
            });
        }

        public void AddWarning(string code, string field, string message)
        {
            Issues.Add(new DesignBasisValidationIssue
            {
                Code = code,
                Field = field,
                Message = message,
                Severity = DesignBasisValidationSeverity.Warning
            });
        }
    }

    public sealed class DesignBasisValidationException : Exception
    {
        public string Classification { get; }
        public DesignBasisValidationResult ValidationResult { get; }

        public DesignBasisValidationException(string message, string classification = "DESIGN_BASIS_VALIDATION_ERROR", DesignBasisValidationResult validationResult = null)
            : base(message)
        {
            Classification = classification ?? "DESIGN_BASIS_VALIDATION_ERROR";
            ValidationResult = validationResult;
        }
    }

    public sealed class DesignBasisAssumptionProvenance
    {
        public AssumptionProvenanceKind Kind { get; set; }
        public string StandardId { get; set; }
        public string StandardVersion { get; set; }
        public string ParameterKey { get; set; }
        public StandardSourceLayer? SourceLayer { get; set; }
        public string SourceDescription { get; set; }
        public string DocumentReference { get; set; }
        public string SectionReference { get; set; }

        public DesignBasisAssumptionProvenance Clone()
        {
            return new DesignBasisAssumptionProvenance
            {
                Kind = Kind,
                StandardId = StandardId,
                StandardVersion = StandardVersion,
                ParameterKey = ParameterKey,
                SourceLayer = SourceLayer,
                SourceDescription = SourceDescription,
                DocumentReference = DocumentReference,
                SectionReference = SectionReference
            };
        }
    }

    public sealed class DesignBasisAssumption
    {
        public string AssumptionId { get; set; }
        public string Key { get; set; }
        public StandardValue Value { get; set; }
        public DesignBasisAssumptionProvenance Provenance { get; set; }
        public string Rationale { get; set; }

        public DesignBasisAssumption Clone()
        {
            return new DesignBasisAssumption
            {
                AssumptionId = AssumptionId,
                Key = Key,
                Value = Value != null ? new StandardValue
                {
                    Kind = Value.Kind,
                    NumberValue = Value.NumberValue,
                    IntegerValue = Value.IntegerValue,
                    BooleanValue = Value.BooleanValue,
                    TextValue = Value.TextValue,
                    QuantityValue = Value.QuantityValue != null ? new StandardQuantity(Value.QuantityValue.Value, Value.QuantityValue.Unit) : null
                } : null,
                Provenance = Provenance?.Clone(),
                Rationale = Rationale
            };
        }

        public static DesignBasisAssumption FromResolvedStandard(
            string assumptionId,
            ResolvedStandardValue resolved,
            string rationale = null)
        {
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));
            if (resolved.Status != StandardResolutionStatus.Defined)
                throw new InvalidOperationException("Cannot create assumption from undefined standard resolution.");

            return new DesignBasisAssumption
            {
                AssumptionId = assumptionId,
                Key = resolved.ParameterKey,
                Value = resolved.EffectiveValue,
                Rationale = rationale,
                Provenance = new DesignBasisAssumptionProvenance
                {
                    Kind = AssumptionProvenanceKind.ResolvedStandard,
                    StandardId = resolved.EffectiveStandardId,
                    StandardVersion = resolved.EffectiveVersion,
                    ParameterKey = resolved.ParameterKey,
                    SourceLayer = resolved.EffectiveSource
                }
            };
        }
    }

    public sealed class DesignBasisOverride
    {
        public string OverrideId { get; set; }
        public string ParameterKey { get; set; }
        public StandardValue OriginalValue { get; set; }
        public StandardValue OverrideValue { get; set; }
        public StandardProvenance OriginalProvenance { get; set; }
        public string Rationale { get; set; }

        public DesignBasisOverride Clone()
        {
            return new DesignBasisOverride
            {
                OverrideId = OverrideId,
                ParameterKey = ParameterKey,
                OriginalValue = OriginalValue != null ? new StandardValue
                {
                    Kind = OriginalValue.Kind,
                    NumberValue = OriginalValue.NumberValue,
                    IntegerValue = OriginalValue.IntegerValue,
                    BooleanValue = OriginalValue.BooleanValue,
                    TextValue = OriginalValue.TextValue,
                    QuantityValue = OriginalValue.QuantityValue != null ? new StandardQuantity(OriginalValue.QuantityValue.Value, OriginalValue.QuantityValue.Unit) : null
                } : null,
                OverrideValue = OverrideValue != null ? new StandardValue
                {
                    Kind = OverrideValue.Kind,
                    NumberValue = OverrideValue.NumberValue,
                    IntegerValue = OverrideValue.IntegerValue,
                    BooleanValue = OverrideValue.BooleanValue,
                    TextValue = OverrideValue.TextValue,
                    QuantityValue = OverrideValue.QuantityValue != null ? new StandardQuantity(OverrideValue.QuantityValue.Value, OverrideValue.QuantityValue.Unit) : null
                } : null,
                OriginalProvenance = OriginalProvenance != null ? new StandardProvenance
                {
                    SourceLayer = OriginalProvenance.SourceLayer,
                    StandardId = OriginalProvenance.StandardId,
                    StandardName = OriginalProvenance.StandardName,
                    StandardVersion = OriginalProvenance.StandardVersion,
                    ParameterKey = OriginalProvenance.ParameterKey,
                    DocumentReference = OriginalProvenance.DocumentReference,
                    SectionReference = OriginalProvenance.SectionReference,
                    Notes = OriginalProvenance.Notes
                } : null,
                Rationale = Rationale
            };
        }
    }

    public sealed class DesignBasisStandardReference
    {
        public StandardSourceLayer SourceLayer { get; set; }
        public string StandardId { get; set; }
        public string StandardVersion { get; set; }

        public DesignBasisStandardReference Clone()
        {
            return new DesignBasisStandardReference
            {
                SourceLayer = SourceLayer,
                StandardId = StandardId,
                StandardVersion = StandardVersion
            };
        }
    }

    public sealed class DesignBasisReviewMetadata
    {
        public string ReviewStateChangedUtc { get; set; }
        public string ActorId { get; set; }
        public string ReviewNote { get; set; }

        public DesignBasisReviewMetadata Clone()
        {
            return new DesignBasisReviewMetadata
            {
                ReviewStateChangedUtc = ReviewStateChangedUtc,
                ActorId = ActorId,
                ReviewNote = ReviewNote
            };
        }
    }

    public sealed class DesignBasis
    {
        public int SchemaVersion { get; set; } = 1;
        public string DesignBasisId { get; set; }
        public string DesignBasisVersion { get; set; }
        public string ProjectId { get; set; }
        public List<DesignBasisStandardReference> StandardReferences { get; set; } = new List<DesignBasisStandardReference>();
        public List<DesignBasisAssumption> Assumptions { get; set; } = new List<DesignBasisAssumption>();
        public List<DesignBasisOverride> Overrides { get; set; } = new List<DesignBasisOverride>();
        public DesignBasisReviewState ReviewState { get; set; } = DesignBasisReviewState.Draft;
        public DesignBasisReviewMetadata ReviewMetadata { get; set; } = new DesignBasisReviewMetadata();

        public DesignBasisValidationResult Validate()
        {
            var result = new DesignBasisValidationResult();

            if (SchemaVersion != 1)
                result.AddError("UNSUPPORTED_SCHEMA", "SchemaVersion", "SchemaVersion must be 1.");

            if (string.IsNullOrWhiteSpace(DesignBasisId))
                result.AddError("MISSING_DESIGN_BASIS_ID", "DesignBasisId", "DesignBasisId is required.");

            bool hasRevisionSpecified = !string.IsNullOrWhiteSpace(DesignBasisVersion);
            if (!hasRevisionSpecified)
                result.AddError("MISSING_VERSION", "DesignBasisVersion", "DesignBasisVersion is required.");

            if (!Enum.IsDefined(typeof(DesignBasisReviewState), ReviewState))
                result.AddError("INVALID_REVIEW_STATE", "ReviewState", "ReviewState has an unrecognized value.");

            // Standard References validation
            if (StandardReferences != null)
            {
                for (int i = 0; i < StandardReferences.Count; i++)
                {
                    var r = StandardReferences[i];
                    if (r == null)
                    {
                        result.AddError("NULL_STANDARD_REFERENCE", "StandardReferences[" + i + "]", "Standard reference entry cannot be null.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(r.StandardId))
                        result.AddError("MISSING_STANDARD_ID", "StandardReferences[" + i + "].StandardId", "StandardId is required in standard reference.");
                    bool hasStandardRevision = !string.IsNullOrWhiteSpace(r.StandardVersion);
                    if (!hasStandardRevision)
                        result.AddError("MISSING_STANDARD_VERSION", "StandardReferences[" + i + "].StandardVersion", "StandardVersion is required in standard reference.");
                    if (!Enum.IsDefined(typeof(StandardSourceLayer), r.SourceLayer))
                        result.AddError("INVALID_SOURCE_LAYER", "StandardReferences[" + i + "].SourceLayer", "StandardSourceLayer is invalid.");
                }
            }

            // Assumptions validation
            var seenAssumptionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAssumptionKeys = new HashSet<string>(StringComparer.Ordinal);

            if (Assumptions != null)
            {
                for (int i = 0; i < Assumptions.Count; i++)
                {
                    var a = Assumptions[i];
                    if (a == null)
                    {
                        result.AddError("NULL_ASSUMPTION", "Assumptions[" + i + "]", "Assumption entry cannot be null.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(a.AssumptionId))
                        result.AddError("MISSING_ASSUMPTION_ID", "Assumptions[" + i + "].AssumptionId", "AssumptionId is required.");
                    else if (!seenAssumptionIds.Add(a.AssumptionId.Trim()))
                        result.AddError("DUPLICATE_ASSUMPTION_ID", "Assumptions[" + i + "].AssumptionId", "Duplicate AssumptionId: " + a.AssumptionId);

                    if (string.IsNullOrWhiteSpace(a.Key))
                    {
                        result.AddError("MISSING_ASSUMPTION_KEY", "Assumptions[" + i + "].Key", "Assumption key is required.");
                    }
                    else
                    {
                        try
                        {
                            var paramKey = new StandardParameterKey(a.Key);
                            if (!seenAssumptionKeys.Add(paramKey.CanonicalKey))
                                result.AddError("DUPLICATE_ASSUMPTION_KEY", "Assumptions[" + i + "].Key", "Duplicate assumption key: " + a.Key);
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_ASSUMPTION_KEY", "Assumptions[" + i + "].Key", ex.Message);
                        }
                    }

                    if (a.Value == null)
                    {
                        result.AddError("MISSING_ASSUMPTION_VALUE", "Assumptions[" + i + "].Value", "Assumption value is required.");
                    }
                    else
                    {
                        try
                        {
                            a.Value.Validate();
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_ASSUMPTION_VALUE", "Assumptions[" + i + "].Value", ex.Message);
                        }
                    }

                    if (a.Provenance == null)
                    {
                        result.AddError("MISSING_ASSUMPTION_PROVENANCE", "Assumptions[" + i + "].Provenance", "Assumption provenance is required.");
                    }
                    else
                    {
                        if (!Enum.IsDefined(typeof(AssumptionProvenanceKind), a.Provenance.Kind))
                            result.AddError("INVALID_PROVENANCE_KIND", "Assumptions[" + i + "].Provenance.Kind", "Provenance Kind is invalid.");

                        if (a.Provenance.Kind == AssumptionProvenanceKind.ResolvedStandard)
                        {
                            if (string.IsNullOrWhiteSpace(a.Provenance.StandardId))
                                result.AddError("MISSING_PROVENANCE_STANDARD_ID", "Assumptions[" + i + "].Provenance.StandardId", "StandardId is required for ResolvedStandard provenance.");
                            bool hasProvVersion = !string.IsNullOrWhiteSpace(a.Provenance.StandardVersion);
                            if (!hasProvVersion)
                                result.AddError("MISSING_PROVENANCE_STANDARD_VERSION", "Assumptions[" + i + "].Provenance.StandardVersion", "StandardVersion is required for ResolvedStandard provenance.");
                            if (string.IsNullOrWhiteSpace(a.Provenance.ParameterKey))
                                result.AddError("MISSING_PROVENANCE_PARAMETER_KEY", "Assumptions[" + i + "].Provenance.ParameterKey", "ParameterKey is required for ResolvedStandard provenance.");
                            if (!a.Provenance.SourceLayer.HasValue || !Enum.IsDefined(typeof(StandardSourceLayer), a.Provenance.SourceLayer.Value))
                                result.AddError("MISSING_PROVENANCE_SOURCE_LAYER", "Assumptions[" + i + "].Provenance.SourceLayer", "SourceLayer is required for ResolvedStandard provenance.");
                        }
                    }
                }
            }

            // Overrides validation
            var seenOverrideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenOverrideKeys = new HashSet<string>(StringComparer.Ordinal);

            if (Overrides != null)
            {
                for (int i = 0; i < Overrides.Count; i++)
                {
                    var o = Overrides[i];
                    if (o == null)
                    {
                        result.AddError("NULL_OVERRIDE", "Overrides[" + i + "]", "Override entry cannot be null.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(o.OverrideId))
                        result.AddError("MISSING_OVERRIDE_ID", "Overrides[" + i + "].OverrideId", "OverrideId is required.");
                    else if (!seenOverrideIds.Add(o.OverrideId.Trim()))
                        result.AddError("DUPLICATE_OVERRIDE_ID", "Overrides[" + i + "].OverrideId", "Duplicate OverrideId: " + o.OverrideId);

                    if (string.IsNullOrWhiteSpace(o.ParameterKey))
                    {
                        result.AddError("MISSING_OVERRIDE_KEY", "Overrides[" + i + "].ParameterKey", "Override ParameterKey is required.");
                    }
                    else
                    {
                        try
                        {
                            var paramKey = new StandardParameterKey(o.ParameterKey);
                            if (!seenOverrideKeys.Add(paramKey.CanonicalKey))
                                result.AddError("DUPLICATE_OVERRIDE_KEY", "Overrides[" + i + "].ParameterKey", "Duplicate active override key: " + o.ParameterKey);
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_OVERRIDE_KEY", "Overrides[" + i + "].ParameterKey", ex.Message);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(o.Rationale))
                        result.AddError("MISSING_OVERRIDE_RATIONALE", "Overrides[" + i + "].Rationale", "Rationale is strictly required for an override.");

                    if (o.OverrideValue == null)
                    {
                        result.AddError("MISSING_OVERRIDE_VALUE", "Overrides[" + i + "].OverrideValue", "OverrideValue is required.");
                    }
                    else
                    {
                        try
                        {
                            o.OverrideValue.Validate();
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_OVERRIDE_VALUE", "Overrides[" + i + "].OverrideValue", ex.Message);
                        }
                    }

                    if (o.OriginalValue != null)
                    {
                        try
                        {
                            o.OriginalValue.Validate();
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_ORIGINAL_VALUE", "Overrides[" + i + "].OriginalValue", ex.Message);
                        }
                    }
                }
            }

            return result;
        }

        public void TransitionReviewState(DesignBasisReviewState newState, string actorId = null, string note = null, DateTime? utcTimestamp = null)
        {
            if (ReviewState == newState)
                return;

            bool allowed = false;
            switch (ReviewState)
            {
                case DesignBasisReviewState.Draft:
                    allowed = (newState == DesignBasisReviewState.ReadyForReview);
                    break;
                case DesignBasisReviewState.ReadyForReview:
                    allowed = (newState == DesignBasisReviewState.Reviewed || newState == DesignBasisReviewState.NeedsRevision);
                    break;
                case DesignBasisReviewState.NeedsRevision:
                    allowed = (newState == DesignBasisReviewState.Draft);
                    break;
                case DesignBasisReviewState.Reviewed:
                    allowed = false;
                    break;
            }

            if (!allowed)
            {
                throw new DesignBasisValidationException(
                    string.Format(CultureInfo.InvariantCulture, "Invalid review state transition from {0} to {1}.", ReviewState, newState),
                    "INVALID_REVIEW_STATE_TRANSITION");
            }

            if (newState == DesignBasisReviewState.ReadyForReview)
            {
                var validation = Validate();
                if (!validation.IsValid)
                {
                    string firstError = validation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                    throw new DesignBasisValidationException(
                        "Cannot transition Draft to ReadyForReview because validation failed: " + firstError,
                        "REVIEW_VALIDATION_GATE_FAILED",
                        validation);
                }
            }

            ReviewState = newState;
            ReviewMetadata = new DesignBasisReviewMetadata
            {
                ReviewStateChangedUtc = (utcTimestamp ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture),
                ActorId = actorId,
                ReviewNote = note
            };
        }

        public DesignBasisSnapshot CreateSnapshot(DateTime? utcCreated = null)
        {
            var validation = Validate();
            if (!validation.IsValid)
            {
                string firstError = validation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                throw new DesignBasisValidationException("Cannot create snapshot: " + firstError, "SNAPSHOT_VALIDATION_FAILED", validation);
            }

            var snapshotRefs = StandardReferences != null
                ? StandardReferences.Select(r => r?.Clone()).Where(r => r != null).ToList()
                : new List<DesignBasisStandardReference>();

            var snapshotAssumptions = Assumptions != null
                ? Assumptions.Select(a => a?.Clone()).Where(a => a != null).ToList()
                : new List<DesignBasisAssumption>();

            var snapshotOverrides = Overrides != null
                ? Overrides.Select(o => o?.Clone()).Where(o => o != null).ToList()
                : new List<DesignBasisOverride>();

            string fingerprint = ComputeFingerprint(SchemaVersion, DesignBasisVersion, snapshotRefs, snapshotAssumptions, snapshotOverrides);

            return new DesignBasisSnapshot(
                SchemaVersion,
                DesignBasisId,
                DesignBasisVersion,
                ProjectId,
                fingerprint,
                utcCreated ?? DateTime.UtcNow,
                snapshotRefs,
                snapshotAssumptions,
                snapshotOverrides);
        }

        public static string ComputeFingerprint(
            int schemaVersion,
            string designBasisVersion,
            IEnumerable<DesignBasisStandardReference> standardRefs,
            IEnumerable<DesignBasisAssumption> assumptions,
            IEnumerable<DesignBasisOverride> overrides)
        {
            var sb = new StringBuilder();
            sb.Append("SCHEMA:").Append(schemaVersion.ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("VERSION:").Append(designBasisVersion ?? "").Append("\n");

            sb.Append("STANDARDS:\n");
            if (standardRefs != null)
            {
                var sortedRefs = standardRefs
                    .OrderBy(r => (int)r.SourceLayer)
                    .ThenBy(r => r.StandardId, StringComparer.Ordinal)
                    .ThenBy(r => r.StandardVersion, StringComparer.Ordinal);

                foreach (var r in sortedRefs)
                {
                    sb.Append("  REF|")
                      .Append(((int)r.SourceLayer).ToString(CultureInfo.InvariantCulture)).Append("|")
                      .Append(r.StandardId).Append("|")
                      .Append(r.StandardVersion).Append("\n");
                }
            }

            sb.Append("ASSUMPTIONS:\n");
            if (assumptions != null)
            {
                var sortedAssumptions = assumptions
                    .OrderBy(a => new StandardParameterKey(a.Key).CanonicalKey, StringComparer.Ordinal);

                foreach (var a in sortedAssumptions)
                {
                    var canonicalKey = new StandardParameterKey(a.Key).CanonicalKey;
                    sb.Append("  ASSUMPTION|")
                      .Append(a.AssumptionId).Append("|")
                      .Append(canonicalKey).Append("|");
                    AppendCanonicalValue(sb, a.Value);
                    sb.Append("|");
                    AppendCanonicalProvenance(sb, a.Provenance);
                    sb.Append("\n");
                }
            }

            sb.Append("OVERRIDES:\n");
            if (overrides != null)
            {
                var sortedOverrides = overrides
                    .OrderBy(o => new StandardParameterKey(o.ParameterKey).CanonicalKey, StringComparer.Ordinal);

                foreach (var o in sortedOverrides)
                {
                    var canonicalKey = new StandardParameterKey(o.ParameterKey).CanonicalKey;
                    sb.Append("  OVERRIDE|")
                      .Append(o.OverrideId).Append("|")
                      .Append(canonicalKey).Append("|");
                    AppendCanonicalValue(sb, o.OverrideValue);
                    sb.Append("|");
                    sb.Append(o.Rationale ?? "").Append("\n");
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void AppendCanonicalValue(StringBuilder sb, StandardValue value)
        {
            if (value == null)
            {
                sb.Append("NULL");
                return;
            }

            sb.Append((int)value.Kind).Append(":");
            switch (value.Kind)
            {
                case StandardValueKind.Number:
                    sb.Append(value.NumberValue.GetValueOrDefault().ToString("R", CultureInfo.InvariantCulture));
                    break;
                case StandardValueKind.Integer:
                    sb.Append(value.IntegerValue.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
                    break;
                case StandardValueKind.Boolean:
                    sb.Append(value.BooleanValue.GetValueOrDefault() ? "true" : "false");
                    break;
                case StandardValueKind.Text:
                    sb.Append(value.TextValue ?? "");
                    break;
                case StandardValueKind.Quantity:
                    if (value.QuantityValue != null)
                    {
                        sb.Append(value.QuantityValue.Value.ToString("R", CultureInfo.InvariantCulture))
                          .Append(" ")
                          .Append(value.QuantityValue.Unit ?? "");
                    }
                    break;
            }
        }

        private static void AppendCanonicalProvenance(StringBuilder sb, DesignBasisAssumptionProvenance prov)
        {
            if (prov == null)
            {
                sb.Append("NONE");
                return;
            }

            sb.Append((int)prov.Kind).Append(":")
              .Append(prov.StandardId ?? "").Append(":")
              .Append(prov.StandardVersion ?? "").Append(":")
              .Append(prov.ParameterKey ?? "").Append(":")
              .Append(prov.SourceLayer.HasValue ? ((int)prov.SourceLayer.Value).ToString(CultureInfo.InvariantCulture) : "");
        }

        public DesignBasisReference ToProjectReference()
        {
            return new DesignBasisReference
            {
                ReferenceIdentifier = DesignBasisId,
                DesignBasisVersion = DesignBasisVersion
            };
        }
    }

    public sealed class DesignBasisSnapshot
    {
        public int SchemaVersion { get; }
        public string DesignBasisId { get; }
        public string DesignBasisVersion { get; }
        public string ProjectId { get; }
        public string ContentFingerprint { get; }
        public DateTime CreatedUtc { get; }

        public ReadOnlyCollection<DesignBasisStandardReference> StandardReferences { get; }
        public ReadOnlyCollection<DesignBasisAssumption> Assumptions { get; }
        public ReadOnlyCollection<DesignBasisOverride> Overrides { get; }

        public DesignBasisSnapshot(
            int schemaVersion,
            string designBasisId,
            string designBasisVersion,
            string projectId,
            string contentFingerprint,
            DateTime createdUtc,
            IList<DesignBasisStandardReference> standardReferences,
            IList<DesignBasisAssumption> assumptions,
            IList<DesignBasisOverride> overrides)
        {
            SchemaVersion = schemaVersion;
            DesignBasisId = designBasisId;
            DesignBasisVersion = designBasisVersion;
            ProjectId = projectId;
            ContentFingerprint = contentFingerprint;
            CreatedUtc = createdUtc;

            StandardReferences = new ReadOnlyCollection<DesignBasisStandardReference>(
                standardReferences != null ? standardReferences.Select(r => r?.Clone()).Where(r => r != null).ToList() : new List<DesignBasisStandardReference>());

            Assumptions = new ReadOnlyCollection<DesignBasisAssumption>(
                assumptions != null ? assumptions.Select(a => a?.Clone()).Where(a => a != null).ToList() : new List<DesignBasisAssumption>());

            Overrides = new ReadOnlyCollection<DesignBasisOverride>(
                overrides != null ? overrides.Select(o => o?.Clone()).Where(o => o != null).ToList() : new List<DesignBasisOverride>());
        }

        public DesignBasisReference ToProjectReference()
        {
            return new DesignBasisReference
            {
                ReferenceIdentifier = DesignBasisId,
                DesignBasisVersion = DesignBasisVersion
            };
        }
    }
}
