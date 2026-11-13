using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // P18 StormFlow Design Basis foundation. Independent of GravityFlow while reusing the shared
    // P8 standards, P9 common Design Basis, typed value/unit and review infrastructure.
    // No rainfall intensity, return period, runoff coefficient, time of concentration, threshold or
    // regulatory criterion is invented here; unresolved project inputs stay explicitly unresolved.
    public enum StormFlowSectionIdentifier
    {
        Project = 1,
        Rainfall = 2,
        Hydrology = 3,
        NetworkCriteria = 4,
        HydraulicCriteria = 5
    }

    public enum StormCriterionRequirement
    {
        Optional = 1,
        Required = 2
    }

    public enum StormCriterionStatus
    {
        NotDefined = 1,
        Defined = 2
    }

    public sealed class StormSectionCompletenessReport
    {
        public StormFlowSectionIdentifier SectionId { get; set; }
        public int DefinedCriterionCount { get; set; }
        public int UndefinedCriterionCount { get; set; }
        public int ValidationIssueCount { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: Defined={1}, Undefined={2}, Issues={3}",
                SectionId, DefinedCriterionCount, UndefinedCriterionCount, ValidationIssueCount);
        }
    }

    public sealed class StormDesignCriterion
    {
        public string Key { get; set; }
        public StormFlowSectionIdentifier Section { get; set; }
        public StormCriterionRequirement Requirement { get; set; } = StormCriterionRequirement.Optional;
        public StormCriterionStatus Status { get; set; } = StormCriterionStatus.NotDefined;
        public StandardValue Value { get; set; }
        public StandardProvenance Provenance { get; set; }
        public string Rationale { get; set; }

        public StormDesignCriterion Clone()
        {
            return new StormDesignCriterion
            {
                Key = Key,
                Section = Section,
                Requirement = Requirement,
                Status = Status,
                Value = Value != null ? new StandardValue
                {
                    Kind = Value.Kind,
                    NumberValue = Value.NumberValue,
                    IntegerValue = Value.IntegerValue,
                    BooleanValue = Value.BooleanValue,
                    TextValue = Value.TextValue,
                    QuantityValue = Value.QuantityValue != null ? new StandardQuantity(Value.QuantityValue.Value, Value.QuantityValue.Unit) : null
                } : null,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null,
                Rationale = Rationale
            };
        }
    }

    public abstract class StormSectionBase
    {
        public abstract StormFlowSectionIdentifier SectionId { get; }
        public List<StormDesignCriterion> Criteria { get; set; } = new List<StormDesignCriterion>();

        public StormSectionCompletenessReport GetCompleteness(int issueCount = 0)
        {
            int defined = 0;
            int undefined = 0;
            if (Criteria != null)
            {
                foreach (var c in Criteria)
                {
                    if (c == null) continue;
                    if (c.Status == StormCriterionStatus.Defined && c.Value != null) defined++;
                    else undefined++;
                }
            }
            return new StormSectionCompletenessReport
            {
                SectionId = SectionId,
                DefinedCriterionCount = defined,
                UndefinedCriterionCount = undefined,
                ValidationIssueCount = issueCount
            };
        }
    }

    public sealed class StormProjectSection : StormSectionBase
    {
        public override StormFlowSectionIdentifier SectionId => StormFlowSectionIdentifier.Project;
    }

    public sealed class StormRainfallSection : StormSectionBase
    {
        public override StormFlowSectionIdentifier SectionId => StormFlowSectionIdentifier.Rainfall;
    }

    public sealed class StormHydrologySection : StormSectionBase
    {
        public override StormFlowSectionIdentifier SectionId => StormFlowSectionIdentifier.Hydrology;
    }

    public sealed class StormNetworkCriteriaSection : StormSectionBase
    {
        public override StormFlowSectionIdentifier SectionId => StormFlowSectionIdentifier.NetworkCriteria;
    }

    public sealed class StormHydraulicCriteriaSection : StormSectionBase
    {
        public override StormFlowSectionIdentifier SectionId => StormFlowSectionIdentifier.HydraulicCriteria;
    }

    public sealed class StormFlowDesignBasis
    {
        public const string ModuleName = "StormFlow";

        public string Module { get; set; } = ModuleName;
        public DesignBasis CommonDesignBasis { get; set; }

        public StormProjectSection Project { get; set; } = new StormProjectSection();
        public StormRainfallSection Rainfall { get; set; } = new StormRainfallSection();
        public StormHydrologySection Hydrology { get; set; } = new StormHydrologySection();
        public StormNetworkCriteriaSection NetworkCriteria { get; set; } = new StormNetworkCriteriaSection();
        public StormHydraulicCriteriaSection HydraulicCriteria { get; set; } = new StormHydraulicCriteriaSection();

        public IEnumerable<StormSectionBase> EnumerateSectionsInCanonicalOrder()
        {
            yield return Project;
            yield return Rainfall;
            yield return Hydrology;
            yield return NetworkCriteria;
            yield return HydraulicCriteria;
        }

        public StormSectionBase GetSection(StormFlowSectionIdentifier sectionId)
        {
            switch (sectionId)
            {
                case StormFlowSectionIdentifier.Project: return Project;
                case StormFlowSectionIdentifier.Rainfall: return Rainfall;
                case StormFlowSectionIdentifier.Hydrology: return Hydrology;
                case StormFlowSectionIdentifier.NetworkCriteria: return NetworkCriteria;
                case StormFlowSectionIdentifier.HydraulicCriteria: return HydraulicCriteria;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sectionId), "Unknown StormFlow section: " + sectionId);
            }
        }

        public StormSectionCompletenessReport GetSectionCompleteness(StormFlowSectionIdentifier sectionId)
        {
            var section = GetSection(sectionId);
            var validation = Validate();
            int issues = validation.Issues.Count(i => i.Field != null && i.Field.StartsWith(sectionId.ToString(), StringComparison.OrdinalIgnoreCase));
            return section.GetCompleteness(issues);
        }

        public List<StormSectionCompletenessReport> GetAllSectionCompleteness()
        {
            var list = new List<StormSectionCompletenessReport>();
            foreach (var s in EnumerateSectionsInCanonicalOrder()) list.Add(GetSectionCompleteness(s.SectionId));
            return list;
        }

        public DesignBasisValidationResult Validate()
        {
            var result = new DesignBasisValidationResult();

            if (!string.Equals(Module, ModuleName, StringComparison.Ordinal))
                result.AddError("INVALID_MODULE", "Module", "Module must be 'StormFlow'. GravityFlow and other modules are rejected.");

            if (CommonDesignBasis == null)
            {
                result.AddError("MISSING_COMMON_DESIGN_BASIS", "CommonDesignBasis", "CommonDesignBasis is required.");
            }
            else
            {
                foreach (var issue in CommonDesignBasis.Validate().Issues) result.Issues.Add(issue);
            }

            ValidateSection(Project, StormFlowSectionIdentifier.Project, result);
            ValidateSection(Rainfall, StormFlowSectionIdentifier.Rainfall, result);
            ValidateSection(Hydrology, StormFlowSectionIdentifier.Hydrology, result);
            ValidateSection(NetworkCriteria, StormFlowSectionIdentifier.NetworkCriteria, result);
            ValidateSection(HydraulicCriteria, StormFlowSectionIdentifier.HydraulicCriteria, result);

            return result;
        }

        private static void ValidateSection(StormSectionBase section, StormFlowSectionIdentifier expectedId, DesignBasisValidationResult result)
        {
            string sectionName = expectedId.ToString();
            if (section == null)
            {
                result.AddError("MISSING_SECTION", sectionName, "Section is required: " + sectionName);
                return;
            }
            if (section.SectionId != expectedId)
            {
                result.AddError("SECTION_MISMATCH", sectionName, "Section ID mismatch: " + section.SectionId);
                return;
            }
            if (section.Criteria == null) return;

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < section.Criteria.Count; i++)
            {
                var c = section.Criteria[i];
                if (c == null)
                {
                    result.AddError("NULL_CRITERION", sectionName + "[" + i + "]", "Criterion entry cannot be null.");
                    continue;
                }
                if (c.Section != expectedId)
                    result.AddError("CRITERION_SECTION_MISMATCH", sectionName + "[" + i + "].Section",
                        string.Format(CultureInfo.InvariantCulture, "Criterion Section ({0}) does not match parent container ({1}).", c.Section, expectedId));

                if (string.IsNullOrWhiteSpace(c.Key))
                {
                    result.AddError("MISSING_CRITERION_KEY", sectionName + "[" + i + "].Key", "Criterion key is required.");
                }
                else
                {
                    try
                    {
                        var paramKey = new StandardParameterKey(c.Key);
                        if (!seenKeys.Add(paramKey.CanonicalKey))
                            result.AddError("DUPLICATE_CRITERION_KEY", sectionName + "." + c.Key,
                                string.Format(CultureInfo.InvariantCulture, "Duplicate criterion key in section {0}: {1}", sectionName, c.Key));
                    }
                    catch (Exception ex) { result.AddError("INVALID_CRITERION_KEY", sectionName + "[" + i + "].Key", ex.Message); }
                }

                if (!Enum.IsDefined(typeof(StormCriterionRequirement), c.Requirement))
                    result.AddError("INVALID_REQUIREMENT", sectionName + "[" + i + "].Requirement", "Requirement value is invalid.");
                if (!Enum.IsDefined(typeof(StormCriterionStatus), c.Status))
                    result.AddError("INVALID_STATUS", sectionName + "[" + i + "].Status", "Status value is invalid.");

                if (c.Status == StormCriterionStatus.Defined)
                {
                    if (c.Value == null) result.AddError("DEFINED_WITHOUT_VALUE", sectionName + "[" + i + "].Value", "Criterion status is Defined but Value is null.");
                    else
                    {
                        try { c.Value.Validate(); }
                        catch (Exception ex) { result.AddError("INVALID_CRITERION_VALUE", sectionName + "[" + i + "].Value", ex.Message); }
                    }
                }

                if (c.Provenance != null && !Enum.IsDefined(typeof(StandardSourceLayer), c.Provenance.SourceLayer))
                    result.AddError("INVALID_PROVENANCE_LAYER", sectionName + "[" + i + "].Provenance.SourceLayer", "SourceLayer is invalid.");
            }
        }

        public void TransitionReviewState(DesignBasisReviewState newState, string actorId = null, string note = null, DateTime? utcTimestamp = null)
        {
            if (CommonDesignBasis == null)
                throw new DesignBasisValidationException("Cannot transition review state: CommonDesignBasis is null.");

            if (newState == DesignBasisReviewState.ReadyForReview)
            {
                var validation = Validate();
                if (!validation.IsValid)
                {
                    string firstError = validation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                    throw new DesignBasisValidationException(
                        "Cannot transition to ReadyForReview because StormFlow validation failed: " + firstError,
                        "REVIEW_VALIDATION_GATE_FAILED", validation);
                }
            }

            CommonDesignBasis.TransitionReviewState(newState, actorId, note, utcTimestamp);
        }

        public StormFlowDesignBasisSnapshot CreateSnapshot(DateTime? utcCreated = null)
        {
            var validation = Validate();
            if (!validation.IsValid)
            {
                string firstError = validation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                throw new DesignBasisValidationException("Cannot create StormFlow snapshot: " + firstError, "SNAPSHOT_VALIDATION_FAILED", validation);
            }

            var commonSnapshot = CommonDesignBasis.CreateSnapshot(utcCreated);
            var projectSnap = CloneSection(Project);
            var rainfallSnap = CloneSection(Rainfall);
            var hydrologySnap = CloneSection(Hydrology);
            var networkSnap = CloneSection(NetworkCriteria);
            var hydraulicSnap = CloneSection(HydraulicCriteria);

            string fingerprint = ComputeStormFingerprint(commonSnapshot.ContentFingerprint,
                projectSnap, rainfallSnap, hydrologySnap, networkSnap, hydraulicSnap);

            return new StormFlowDesignBasisSnapshot(commonSnapshot, fingerprint, utcCreated ?? DateTime.UtcNow,
                projectSnap, rainfallSnap, hydrologySnap, networkSnap, hydraulicSnap);
        }

        private static List<StormDesignCriterion> CloneSection(StormSectionBase section)
        {
            return section.Criteria != null
                ? section.Criteria.Select(c => c?.Clone()).Where(c => c != null).ToList()
                : new List<StormDesignCriterion>();
        }

        public static string ComputeStormFingerprint(string commonFingerprint,
            IEnumerable<StormDesignCriterion> project,
            IEnumerable<StormDesignCriterion> rainfall,
            IEnumerable<StormDesignCriterion> hydrology,
            IEnumerable<StormDesignCriterion> networkCriteria,
            IEnumerable<StormDesignCriterion> hydraulicCriteria)
        {
            var sb = new StringBuilder();
            sb.Append("COMMON_FINGERPRINT:").Append(commonFingerprint ?? "").Append("\n");
            sb.Append("MODULE:").Append(ModuleName).Append("\n");

            AppendSectionToFingerprint(sb, StormFlowSectionIdentifier.Project, project);
            AppendSectionToFingerprint(sb, StormFlowSectionIdentifier.Rainfall, rainfall);
            AppendSectionToFingerprint(sb, StormFlowSectionIdentifier.Hydrology, hydrology);
            AppendSectionToFingerprint(sb, StormFlowSectionIdentifier.NetworkCriteria, networkCriteria);
            AppendSectionToFingerprint(sb, StormFlowSectionIdentifier.HydraulicCriteria, hydraulicCriteria);

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void AppendSectionToFingerprint(StringBuilder sb, StormFlowSectionIdentifier sectionId, IEnumerable<StormDesignCriterion> criteria)
        {
            sb.Append("SECTION:").Append(sectionId.ToString()).Append("\n");
            if (criteria == null) return;

            var sorted = criteria.OrderBy(c => new StandardParameterKey(c.Key).CanonicalKey, StringComparer.Ordinal);
            foreach (var c in sorted)
            {
                var canonicalKey = new StandardParameterKey(c.Key).CanonicalKey;
                sb.Append("  CRITERION|").Append(canonicalKey).Append("|")
                  .Append(((int)c.Requirement).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(((int)c.Status).ToString(CultureInfo.InvariantCulture)).Append("|");
                if (c.Status == StormCriterionStatus.Defined && c.Value != null) AppendCanonicalValue(sb, c.Value);
                else sb.Append("UNDEFINED");
                sb.Append("|");
                if (c.Provenance != null)
                    sb.Append(((int)c.Provenance.SourceLayer).ToString(CultureInfo.InvariantCulture)).Append(":")
                      .Append(c.Provenance.StandardId ?? "").Append(":").Append(c.Provenance.StandardVersion ?? "").Append(":")
                      .Append(c.Provenance.ParameterKey ?? "");
                else sb.Append("NO_PROV");
                sb.Append("\n");
            }
        }

        private static void AppendCanonicalValue(StringBuilder sb, StandardValue value)
        {
            sb.Append(((int)value.Kind).ToString(CultureInfo.InvariantCulture)).Append(":");
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
                        sb.Append(value.QuantityValue.Value.ToString("R", CultureInfo.InvariantCulture)).Append(" ").Append(value.QuantityValue.Unit ?? "");
                    break;
            }
        }
    }

    public sealed class StormFlowDesignBasisSnapshot
    {
        public DesignBasisSnapshot CommonSnapshot { get; }
        public string StormContentFingerprint { get; }
        public DateTime CreatedUtc { get; }

        public ReadOnlyCollection<StormDesignCriterion> Project { get; }
        public ReadOnlyCollection<StormDesignCriterion> Rainfall { get; }
        public ReadOnlyCollection<StormDesignCriterion> Hydrology { get; }
        public ReadOnlyCollection<StormDesignCriterion> NetworkCriteria { get; }
        public ReadOnlyCollection<StormDesignCriterion> HydraulicCriteria { get; }

        public StormFlowDesignBasisSnapshot(DesignBasisSnapshot commonSnapshot, string stormContentFingerprint, DateTime createdUtc,
            IList<StormDesignCriterion> project, IList<StormDesignCriterion> rainfall, IList<StormDesignCriterion> hydrology,
            IList<StormDesignCriterion> networkCriteria, IList<StormDesignCriterion> hydraulicCriteria)
        {
            CommonSnapshot = commonSnapshot ?? throw new ArgumentNullException(nameof(commonSnapshot));
            StormContentFingerprint = stormContentFingerprint ?? throw new ArgumentNullException(nameof(stormContentFingerprint));
            CreatedUtc = createdUtc;

            Project = WrapReadOnly(project);
            Rainfall = WrapReadOnly(rainfall);
            Hydrology = WrapReadOnly(hydrology);
            NetworkCriteria = WrapReadOnly(networkCriteria);
            HydraulicCriteria = WrapReadOnly(hydraulicCriteria);
        }

        private static ReadOnlyCollection<StormDesignCriterion> WrapReadOnly(IList<StormDesignCriterion> list)
        {
            var cloned = list != null ? list.Select(c => c?.Clone()).Where(c => c != null).ToList() : new List<StormDesignCriterion>();
            return new ReadOnlyCollection<StormDesignCriterion>(cloned);
        }

        public IReadOnlyList<StormDesignCriterion> GetSectionCriteria(StormFlowSectionIdentifier sectionId)
        {
            switch (sectionId)
            {
                case StormFlowSectionIdentifier.Project: return Project;
                case StormFlowSectionIdentifier.Rainfall: return Rainfall;
                case StormFlowSectionIdentifier.Hydrology: return Hydrology;
                case StormFlowSectionIdentifier.NetworkCriteria: return NetworkCriteria;
                case StormFlowSectionIdentifier.HydraulicCriteria: return HydraulicCriteria;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sectionId), "Unknown StormFlow section: " + sectionId);
            }
        }
    }
}
