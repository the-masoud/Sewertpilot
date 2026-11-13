using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum GravityFlowSectionIdentifier
    {
        Project = 1,
        Loading = 2,
        PipeCriteria = 3,
        StructureCriteria = 4,
        VerticalDesign = 5,
        HydraulicCriteria = 6,
        SolverSettings = 7
    }

    public enum GravityCriterionRequirement
    {
        Optional = 1,
        Required = 2
    }

    public enum GravityCriterionStatus
    {
        NotDefined = 1,
        Defined = 2
    }

    public sealed class SectionCompletenessReport
    {
        public GravityFlowSectionIdentifier SectionId { get; set; }
        public int DefinedCriterionCount { get; set; }
        public int UndefinedCriterionCount { get; set; }
        public int ValidationIssueCount { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: Defined={1}, Undefined={2}, Issues={3}",
                SectionId, DefinedCriterionCount, UndefinedCriterionCount, ValidationIssueCount);
        }
    }

    public sealed class GravityDesignCriterion
    {
        public string Key { get; set; }
        public GravityFlowSectionIdentifier Section { get; set; }
        public GravityCriterionRequirement Requirement { get; set; } = GravityCriterionRequirement.Optional;
        public GravityCriterionStatus Status { get; set; } = GravityCriterionStatus.NotDefined;
        public StandardValue Value { get; set; }
        public StandardProvenance Provenance { get; set; }
        public string Rationale { get; set; }

        public GravityDesignCriterion Clone()
        {
            return new GravityDesignCriterion
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

    public abstract class GravitySectionBase
    {
        public abstract GravityFlowSectionIdentifier SectionId { get; }
        public List<GravityDesignCriterion> Criteria { get; set; } = new List<GravityDesignCriterion>();

        public SectionCompletenessReport GetCompleteness(int issueCount = 0)
        {
            int defined = 0;
            int undefined = 0;
            if (Criteria != null)
            {
                foreach (var c in Criteria)
                {
                    if (c == null) continue;
                    if (c.Status == GravityCriterionStatus.Defined && c.Value != null)
                        defined++;
                    else
                        undefined++;
                }
            }

            return new SectionCompletenessReport
            {
                SectionId = SectionId,
                DefinedCriterionCount = defined,
                UndefinedCriterionCount = undefined,
                ValidationIssueCount = issueCount
            };
        }
    }

    public sealed class GravityProjectSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.Project;
    }

    public sealed class GravityLoadingSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.Loading;
    }

    public sealed class GravityPipeCriteriaSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.PipeCriteria;
    }

    public sealed class GravityStructureCriteriaSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.StructureCriteria;
    }

    public sealed class GravityVerticalDesignSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.VerticalDesign;
    }

    public sealed class GravityHydraulicCriteriaSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.HydraulicCriteria;
    }

    public sealed class GravitySolverSettingsSection : GravitySectionBase
    {
        public override GravityFlowSectionIdentifier SectionId => GravityFlowSectionIdentifier.SolverSettings;
    }

    public sealed class GravityFlowDesignBasis
    {
        public string Module { get; set; } = "GravityFlow";
        public DesignBasis CommonDesignBasis { get; set; }

        public GravityProjectSection Project { get; set; } = new GravityProjectSection();
        public GravityLoadingSection Loading { get; set; } = new GravityLoadingSection();
        public GravityPipeCriteriaSection PipeCriteria { get; set; } = new GravityPipeCriteriaSection();
        public GravityStructureCriteriaSection StructureCriteria { get; set; } = new GravityStructureCriteriaSection();
        public GravityVerticalDesignSection VerticalDesign { get; set; } = new GravityVerticalDesignSection();
        public GravityHydraulicCriteriaSection HydraulicCriteria { get; set; } = new GravityHydraulicCriteriaSection();
        public GravitySolverSettingsSection SolverSettings { get; set; } = new GravitySolverSettingsSection();

        public IEnumerable<GravitySectionBase> EnumerateSectionsInCanonicalOrder()
        {
            yield return Project;
            yield return Loading;
            yield return PipeCriteria;
            yield return StructureCriteria;
            yield return VerticalDesign;
            yield return HydraulicCriteria;
            yield return SolverSettings;
        }

        public GravitySectionBase GetSection(GravityFlowSectionIdentifier sectionId)
        {
            switch (sectionId)
            {
                case GravityFlowSectionIdentifier.Project: return Project;
                case GravityFlowSectionIdentifier.Loading: return Loading;
                case GravityFlowSectionIdentifier.PipeCriteria: return PipeCriteria;
                case GravityFlowSectionIdentifier.StructureCriteria: return StructureCriteria;
                case GravityFlowSectionIdentifier.VerticalDesign: return VerticalDesign;
                case GravityFlowSectionIdentifier.HydraulicCriteria: return HydraulicCriteria;
                case GravityFlowSectionIdentifier.SolverSettings: return SolverSettings;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sectionId), "Unknown GravityFlow section: " + sectionId);
            }
        }

        public SectionCompletenessReport GetSectionCompleteness(GravityFlowSectionIdentifier sectionId)
        {
            var section = GetSection(sectionId);
            var validation = Validate();
            int issues = validation.Issues.Count(i => i.Field != null && i.Field.StartsWith(sectionId.ToString(), StringComparison.OrdinalIgnoreCase));
            return section.GetCompleteness(issues);
        }

        public List<SectionCompletenessReport> GetAllSectionCompleteness()
        {
            var list = new List<SectionCompletenessReport>();
            foreach (var s in EnumerateSectionsInCanonicalOrder())
            {
                list.Add(GetSectionCompleteness(s.SectionId));
            }
            return list;
        }

        public DesignBasisValidationResult Validate()
        {
            var result = new DesignBasisValidationResult();

            // Module identity check
            if (!string.Equals(Module, "GravityFlow", StringComparison.Ordinal))
            {
                result.AddError("INVALID_MODULE", "Module", "Module must be 'GravityFlow'. StormFlow and other modules are rejected.");
            }

            // Common Design Basis check
            if (CommonDesignBasis == null)
            {
                result.AddError("MISSING_COMMON_DESIGN_BASIS", "CommonDesignBasis", "CommonDesignBasis is required.");
            }
            else
            {
                var commonValidation = CommonDesignBasis.Validate();
                foreach (var issue in commonValidation.Issues)
                {
                    result.Issues.Add(issue);
                }
            }

            // All seven sections must be present and valid
            ValidateSection(Project, GravityFlowSectionIdentifier.Project, result);
            ValidateSection(Loading, GravityFlowSectionIdentifier.Loading, result);
            ValidateSection(PipeCriteria, GravityFlowSectionIdentifier.PipeCriteria, result);
            ValidateSection(StructureCriteria, GravityFlowSectionIdentifier.StructureCriteria, result);
            ValidateSection(VerticalDesign, GravityFlowSectionIdentifier.VerticalDesign, result);
            ValidateSection(HydraulicCriteria, GravityFlowSectionIdentifier.HydraulicCriteria, result);
            ValidateSection(SolverSettings, GravityFlowSectionIdentifier.SolverSettings, result);

            return result;
        }

        private static void ValidateSection(GravitySectionBase section, GravityFlowSectionIdentifier expectedId, DesignBasisValidationResult result)
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
                {
                    result.AddError("CRITERION_SECTION_MISMATCH", sectionName + "[" + i + "].Section",
                        string.Format(CultureInfo.InvariantCulture, "Criterion Section ({0}) does not match parent container ({1}).", c.Section, expectedId));
                }

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
                        {
                            result.AddError("DUPLICATE_CRITERION_KEY", sectionName + "." + c.Key,
                                string.Format(CultureInfo.InvariantCulture, "Duplicate criterion key in section {0}: {1}", sectionName, c.Key));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddError("INVALID_CRITERION_KEY", sectionName + "[" + i + "].Key", ex.Message);
                    }
                }

                if (!Enum.IsDefined(typeof(GravityCriterionRequirement), c.Requirement))
                    result.AddError("INVALID_REQUIREMENT", sectionName + "[" + i + "].Requirement", "Requirement value is invalid.");

                if (!Enum.IsDefined(typeof(GravityCriterionStatus), c.Status))
                    result.AddError("INVALID_STATUS", sectionName + "[" + i + "].Status", "Status value is invalid.");

                if (c.Status == GravityCriterionStatus.Defined)
                {
                    if (c.Value == null)
                    {
                        result.AddError("DEFINED_WITHOUT_VALUE", sectionName + "[" + i + "].Value",
                            "Criterion status is Defined but Value is null.");
                    }
                    else
                    {
                        try
                        {
                            c.Value.Validate();
                        }
                        catch (Exception ex)
                        {
                            result.AddError("INVALID_CRITERION_VALUE", sectionName + "[" + i + "].Value", ex.Message);
                        }
                    }
                }

                if (c.Provenance != null)
                {
                    if (!Enum.IsDefined(typeof(StandardSourceLayer), c.Provenance.SourceLayer))
                        result.AddError("INVALID_PROVENANCE_LAYER", sectionName + "[" + i + "].Provenance.SourceLayer", "SourceLayer is invalid.");
                }
            }
        }

        public void TransitionReviewState(DesignBasisReviewState newState, string actorId = null, string note = null, DateTime? utcTimestamp = null)
        {
            if (CommonDesignBasis == null)
                throw new DesignBasisValidationException("Cannot transition review state: CommonDesignBasis is null.");

            if (newState == DesignBasisReviewState.ReadyForReview)
            {
                var gravityValidation = Validate();
                if (!gravityValidation.IsValid)
                {
                    string firstError = gravityValidation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                    throw new DesignBasisValidationException(
                        "Cannot transition to ReadyForReview because GravityFlow validation failed: " + firstError,
                        "REVIEW_VALIDATION_GATE_FAILED",
                        gravityValidation);
                }
            }

            CommonDesignBasis.TransitionReviewState(newState, actorId, note, utcTimestamp);
        }

        public GravityFlowDesignBasisSnapshot CreateSnapshot(DateTime? utcCreated = null)
        {
            var validation = Validate();
            if (!validation.IsValid)
            {
                string firstError = validation.Issues.FirstOrDefault(i => i.Severity == DesignBasisValidationSeverity.Error)?.Message ?? "Validation failed.";
                throw new DesignBasisValidationException("Cannot create GravityFlow snapshot: " + firstError, "SNAPSHOT_VALIDATION_FAILED", validation);
            }

            var commonSnapshot = CommonDesignBasis.CreateSnapshot(utcCreated);

            var projectSnap = CloneSection(Project);
            var loadingSnap = CloneSection(Loading);
            var pipeSnap = CloneSection(PipeCriteria);
            var structSnap = CloneSection(StructureCriteria);
            var vertSnap = CloneSection(VerticalDesign);
            var hydSnap = CloneSection(HydraulicCriteria);
            var solverSnap = CloneSection(SolverSettings);

            string gravityFingerprint = ComputeGravityFingerprint(
                commonSnapshot.ContentFingerprint,
                projectSnap,
                loadingSnap,
                pipeSnap,
                structSnap,
                vertSnap,
                hydSnap,
                solverSnap);

            return new GravityFlowDesignBasisSnapshot(
                commonSnapshot,
                gravityFingerprint,
                utcCreated ?? DateTime.UtcNow,
                projectSnap,
                loadingSnap,
                pipeSnap,
                structSnap,
                vertSnap,
                hydSnap,
                solverSnap);
        }

        private static List<GravityDesignCriterion> CloneSection(GravitySectionBase section)
        {
            return section.Criteria != null
                ? section.Criteria.Select(c => c?.Clone()).Where(c => c != null).ToList()
                : new List<GravityDesignCriterion>();
        }

        public static string ComputeGravityFingerprint(
            string commonFingerprint,
            IEnumerable<GravityDesignCriterion> project,
            IEnumerable<GravityDesignCriterion> loading,
            IEnumerable<GravityDesignCriterion> pipeCriteria,
            IEnumerable<GravityDesignCriterion> structureCriteria,
            IEnumerable<GravityDesignCriterion> verticalDesign,
            IEnumerable<GravityDesignCriterion> hydraulicCriteria,
            IEnumerable<GravityDesignCriterion> solverSettings)
        {
            var sb = new StringBuilder();
            sb.Append("COMMON_FINGERPRINT:").Append(commonFingerprint ?? "").Append("\n");
            sb.Append("MODULE:GravityFlow\n");

            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.Project, project);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.Loading, loading);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.PipeCriteria, pipeCriteria);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.StructureCriteria, structureCriteria);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.VerticalDesign, verticalDesign);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.HydraulicCriteria, hydraulicCriteria);
            AppendSectionToFingerprint(sb, GravityFlowSectionIdentifier.SolverSettings, solverSettings);

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void AppendSectionToFingerprint(StringBuilder sb, GravityFlowSectionIdentifier sectionId, IEnumerable<GravityDesignCriterion> criteria)
        {
            sb.Append("SECTION:").Append(sectionId.ToString()).Append("\n");
            if (criteria == null) return;

            var sorted = criteria
                .OrderBy(c => new StandardParameterKey(c.Key).CanonicalKey, StringComparer.Ordinal);

            foreach (var c in sorted)
            {
                var canonicalKey = new StandardParameterKey(c.Key).CanonicalKey;
                sb.Append("  CRITERION|")
                  .Append(canonicalKey).Append("|")
                  .Append((int)c.Requirement).Append("|")
                  .Append((int)c.Status).Append("|");

                if (c.Status == GravityCriterionStatus.Defined && c.Value != null)
                {
                    AppendCanonicalValue(sb, c.Value);
                }
                else
                {
                    sb.Append("UNDEFINED");
                }

                sb.Append("|");
                if (c.Provenance != null)
                {
                    sb.Append((int)c.Provenance.SourceLayer).Append(":")
                      .Append(c.Provenance.StandardId ?? "").Append(":")
                      .Append(c.Provenance.StandardVersion ?? "").Append(":")
                      .Append(c.Provenance.ParameterKey ?? "");
                }
                else
                {
                    sb.Append("NO_PROV");
                }
                sb.Append("\n");
            }
        }

        private static void AppendCanonicalValue(StringBuilder sb, StandardValue value)
        {
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
    }

    public sealed class GravityFlowDesignBasisSnapshot
    {
        public DesignBasisSnapshot CommonSnapshot { get; }
        public string GravityContentFingerprint { get; }
        public DateTime CreatedUtc { get; }

        public ReadOnlyCollection<GravityDesignCriterion> Project { get; }
        public ReadOnlyCollection<GravityDesignCriterion> Loading { get; }
        public ReadOnlyCollection<GravityDesignCriterion> PipeCriteria { get; }
        public ReadOnlyCollection<GravityDesignCriterion> StructureCriteria { get; }
        public ReadOnlyCollection<GravityDesignCriterion> VerticalDesign { get; }
        public ReadOnlyCollection<GravityDesignCriterion> HydraulicCriteria { get; }
        public ReadOnlyCollection<GravityDesignCriterion> SolverSettings { get; }

        public GravityFlowDesignBasisSnapshot(
            DesignBasisSnapshot commonSnapshot,
            string gravityContentFingerprint,
            DateTime createdUtc,
            IList<GravityDesignCriterion> project,
            IList<GravityDesignCriterion> loading,
            IList<GravityDesignCriterion> pipeCriteria,
            IList<GravityDesignCriterion> structureCriteria,
            IList<GravityDesignCriterion> verticalDesign,
            IList<GravityDesignCriterion> hydraulicCriteria,
            IList<GravityDesignCriterion> solverSettings)
        {
            CommonSnapshot = commonSnapshot ?? throw new ArgumentNullException(nameof(commonSnapshot));
            GravityContentFingerprint = gravityContentFingerprint ?? throw new ArgumentNullException(nameof(gravityContentFingerprint));
            CreatedUtc = createdUtc;

            Project = WrapReadOnly(project);
            Loading = WrapReadOnly(loading);
            PipeCriteria = WrapReadOnly(pipeCriteria);
            StructureCriteria = WrapReadOnly(structureCriteria);
            VerticalDesign = WrapReadOnly(verticalDesign);
            HydraulicCriteria = WrapReadOnly(hydraulicCriteria);
            SolverSettings = WrapReadOnly(solverSettings);
        }

        private static ReadOnlyCollection<GravityDesignCriterion> WrapReadOnly(IList<GravityDesignCriterion> list)
        {
            var cloned = list != null ? list.Select(c => c?.Clone()).Where(c => c != null).ToList() : new List<GravityDesignCriterion>();
            return new ReadOnlyCollection<GravityDesignCriterion>(cloned);
        }

        public IReadOnlyList<GravityDesignCriterion> GetSectionCriteria(GravityFlowSectionIdentifier sectionId)
        {
            switch (sectionId)
            {
                case GravityFlowSectionIdentifier.Project: return Project;
                case GravityFlowSectionIdentifier.Loading: return Loading;
                case GravityFlowSectionIdentifier.PipeCriteria: return PipeCriteria;
                case GravityFlowSectionIdentifier.StructureCriteria: return StructureCriteria;
                case GravityFlowSectionIdentifier.VerticalDesign: return VerticalDesign;
                case GravityFlowSectionIdentifier.HydraulicCriteria: return HydraulicCriteria;
                case GravityFlowSectionIdentifier.SolverSettings: return SolverSettings;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sectionId), "Unknown GravityFlow section: " + sectionId);
            }
        }
    }
}
