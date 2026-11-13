using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SewerPilot.OpenFlows.Adapter.HydraulicRules;

namespace SewerPilot.OpenFlows.Adapter
{
    // Deterministic verification of explicit hydraulic criteria against a supplied result set.
    // No custom solver, no automatic redesign and no inferred criterion. A missing value is never zero
    // and never a pass; an engineering violation is a reported finding, not a software failure.
    public static class GravityHydraulicVerificationEngine
    {
        public static HydraulicVerificationReport Verify(HydraulicResultSet results, IEnumerable<HydraulicVerificationCriterion> criteria)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var criterionList = criteria.Where(c => c != null).ToList();
            var report = new HydraulicVerificationReport
            {
                ResultFingerprint = HydraulicResultSet.ComputeFingerprint(results),
                CriteriaFingerprint = HydraulicVerificationReport.ComputeCriteriaFingerprint(criterionList),
                IsTestOnly = results.IsTestOnly
            };

            var findings = new List<HydraulicVerificationFinding>();
            var orderedCriteria = criterionList
                .OrderBy(c => c.CriterionKey ?? "", StringComparer.Ordinal)
                .ThenBy(c => (int)c.Metric)
                .ThenBy(c => c.AppliesToElementId ?? 0);

            foreach (var criterion in orderedCriteria)
            {
                if (!IsWellFormed(criterion))
                {
                    findings.Add(new HydraulicVerificationFinding
                    {
                        ElementId = criterion.AppliesToElementId ?? 0,
                        Metric = criterion.Metric,
                        Classification = HydraulicVerificationClassification.InvalidInput,
                        CriterionKey = criterion.CriterionKey,
                        RequiredOperator = criterion.Operator,
                        RequiredThreshold = criterion.Threshold,
                        RequiredUnit = criterion.Unit,
                        Provenance = criterion.Provenance,
                        Reason = "Criterion definition is incomplete or non-finite; no comparison is performed."
                    });
                    continue;
                }

                if (criterion.AppliesToElementId.HasValue)
                {
                    var element = results.FindElement(criterion.AppliesToElementId.Value);
                    if (element == null)
                    {
                        findings.Add(NotApplicable(criterion, criterion.AppliesToElementId.Value, null,
                            "Scoped element is not present in the supplied result set."));
                        continue;
                    }
                    findings.Add(Classify(criterion, element));
                }
                else
                {
                    var scoped = (results.Elements ?? new List<HydraulicElementResult>())
                        .Where(e => e != null && e.FindMetric(criterion.Metric) != null)
                        .OrderBy(e => e.ElementId).ToList();
                    if (scoped.Count == 0)
                    {
                        findings.Add(NotApplicable(criterion, 0, null, "No element in the result set declares this metric."));
                        continue;
                    }
                    foreach (var element in scoped) findings.Add(Classify(criterion, element));
                }
            }

            report.Findings = findings
                .OrderBy(f => f.CriterionKey ?? "", StringComparer.Ordinal)
                .ThenBy(f => f.ElementId)
                .ThenBy(f => (int)f.Metric)
                .ToList();

            foreach (var finding in report.Findings)
            {
                switch (finding.Classification)
                {
                    case HydraulicVerificationClassification.Pass: report.PassCount++; break;
                    case HydraulicVerificationClassification.Violation: report.ViolationCount++; break;
                    case HydraulicVerificationClassification.NotAvailable: report.NotAvailableCount++; break;
                    case HydraulicVerificationClassification.NotApplicable: report.NotApplicableCount++; break;
                    case HydraulicVerificationClassification.InvalidInput: report.InvalidInputCount++; break;
                }
            }

            report.ReportFingerprint = ComputeReportFingerprint(report);
            return report;
        }

        private static bool IsWellFormed(HydraulicVerificationCriterion criterion)
        {
            return !string.IsNullOrWhiteSpace(criterion.CriterionKey) &&
                Enum.IsDefined(typeof(HydraulicMetricKind), criterion.Metric) &&
                Enum.IsDefined(typeof(HydraulicCriterionOperator), criterion.Operator) &&
                !double.IsNaN(criterion.Threshold) && !double.IsInfinity(criterion.Threshold) &&
                !string.IsNullOrWhiteSpace(criterion.Unit);
        }

        private static HydraulicVerificationFinding NotApplicable(HydraulicVerificationCriterion criterion, int elementId,
            string elementKind, string reason)
        {
            return new HydraulicVerificationFinding
            {
                ElementId = elementId,
                ElementKind = elementKind,
                Metric = criterion.Metric,
                Classification = HydraulicVerificationClassification.NotApplicable,
                RequiredOperator = criterion.Operator,
                RequiredThreshold = criterion.Threshold,
                RequiredUnit = criterion.Unit,
                CriterionKey = criterion.CriterionKey,
                Provenance = criterion.Provenance,
                Reason = reason
            };
        }

        private static HydraulicVerificationFinding Classify(HydraulicVerificationCriterion criterion, HydraulicElementResult element)
        {
            var metric = element.FindMetric(criterion.Metric);
            var finding = new HydraulicVerificationFinding
            {
                ElementId = element.ElementId,
                ElementKind = element.ElementKind,
                Metric = criterion.Metric,
                CriterionKey = criterion.CriterionKey,
                RequiredOperator = criterion.Operator,
                RequiredThreshold = criterion.Threshold,
                RequiredUnit = criterion.Unit,
                Provenance = criterion.Provenance
            };

            if (metric == null)
            {
                finding.Classification = HydraulicVerificationClassification.NotAvailable;
                finding.Reason = "No result value is available for this metric; a missing mandatory value is not a pass.";
                return finding;
            }

            finding.ActualValue = metric.NumericValue;
            finding.ActualUnit = metric.Unit;

            if (metric.Availability == HydraulicResultAvailability.NotApplicable)
            {
                finding.Classification = HydraulicVerificationClassification.NotApplicable;
                finding.Reason = "The metric is explicitly not applicable for this element.";
                return finding;
            }
            if (metric.Availability == HydraulicResultAvailability.Invalid)
            {
                finding.Classification = HydraulicVerificationClassification.InvalidInput;
                finding.Reason = "The supplied result value is marked invalid.";
                return finding;
            }
            if (metric.Availability != HydraulicResultAvailability.Available || !metric.NumericValue.HasValue)
            {
                finding.Classification = HydraulicVerificationClassification.NotAvailable;
                finding.Reason = "The metric is unavailable; it is not treated as zero and is not a pass.";
                return finding;
            }
            if (double.IsNaN(metric.NumericValue.Value) || double.IsInfinity(metric.NumericValue.Value))
            {
                finding.Classification = HydraulicVerificationClassification.InvalidInput;
                finding.Reason = "The supplied result value is not finite.";
                return finding;
            }
            if (!string.Equals(metric.Unit, criterion.Unit, StringComparison.OrdinalIgnoreCase))
            {
                finding.Classification = HydraulicVerificationClassification.InvalidInput;
                finding.Reason = "Result and criterion units differ; no silent unit conversion is performed.";
                return finding;
            }

            // Minimum passes at and above the threshold; Maximum passes at and below it.
            bool pass = criterion.Operator == HydraulicCriterionOperator.Minimum
                ? metric.NumericValue.Value >= criterion.Threshold
                : metric.NumericValue.Value <= criterion.Threshold;
            finding.Classification = pass ? HydraulicVerificationClassification.Pass : HydraulicVerificationClassification.Violation;
            finding.Reason = pass
                ? "Value satisfies the explicit engineering criterion."
                : "Value violates the explicit engineering criterion; this is an engineering finding, not a software failure.";
            return finding;
        }

        private static string ComputeReportFingerprint(HydraulicVerificationReport report)
        {
            var sb = new StringBuilder();
            sb.Append("RESULT:").Append(report.ResultFingerprint ?? "").Append("\n");
            sb.Append("CRITERIA:").Append(report.CriteriaFingerprint ?? "").Append("\n");
            sb.Append("TESTONLY:").Append(report.IsTestOnly ? "true" : "false").Append("\n");
            foreach (var finding in report.Findings)
            {
                sb.Append("F|").Append(finding.CriterionKey ?? "").Append("|")
                  .Append(finding.ElementId.ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(((int)finding.Metric).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(((int)finding.Classification).ToString(CultureInfo.InvariantCulture)).Append("|")
                  .Append(finding.ActualValue.HasValue ? finding.ActualValue.Value.ToString("R", CultureInfo.InvariantCulture) : "")
                  .Append("|").Append(finding.ActualUnit ?? "").Append("|")
                  .Append(finding.RequiredOperator.HasValue ? ((int)finding.RequiredOperator.Value).ToString(CultureInfo.InvariantCulture) : "")
                  .Append("|")
                  .Append(finding.RequiredThreshold.HasValue ? finding.RequiredThreshold.Value.ToString("R", CultureInfo.InvariantCulture) : "")
                  .Append("|").Append(finding.RequiredUnit ?? "").Append("\n");
            }
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }

        public static MunicipalRuleEvaluationReport VerifyMunicipalRules(
            HydraulicResultSet results,
            NetworkGeometryInfo geometry,
            MunicipalHydraulicRuleEngine engine = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            var eng = engine ?? new MunicipalHydraulicRuleEngine();
            return eng.EvaluateNetwork(results, geometry);
        }

        public static MunicipalRuleEvaluationReport VerifyMunicipalRules(
            HydraulicResultSet results,
            GravityNetworkSnapshot snapshot,
            MunicipalHydraulicRuleEngine engine = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var geometry = NetworkGeometryInfo.FromSnapshot(snapshot);
            return VerifyMunicipalRules(results, geometry, engine);
        }

        public static MunicipalRuleEvaluationReport VerifyMunicipalRules(
            HydraulicResultSet results,
            NetworkReadResult readResult,
            MunicipalHydraulicRuleEngine engine = null)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (readResult == null) throw new ArgumentNullException(nameof(readResult));
            var geometry = NetworkGeometryInfo.FromNetworkRead(readResult);
            return VerifyMunicipalRules(results, geometry, engine);
        }
    }

    // Reads explicit hydraulic criteria from a reviewed Design Basis using the established structured-Text
    // convention already used by P13 vertical decisions: a Defined Text criterion whose canonical JSON
    // equals the criterion's structured content, keyed by its own provenance ParameterKey.
    public static class HydraulicVerificationCriteria
    {
        public static List<HydraulicVerificationCriterion> ReadFromDesignBasis(GravityFlowDesignBasis basis)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            var result = new List<HydraulicVerificationCriterion>();
            var section = basis.HydraulicCriteria;
            if (section == null || section.Criteria == null) return result;

            foreach (var entry in section.Criteria)
            {
                if (entry == null || entry.Status != GravityCriterionStatus.Defined) continue;
                if (entry.Value == null || entry.Value.Kind != StandardValueKind.Text)
                    throw new DesignBasisValidationException(
                        "Hydraulic criterion '" + entry.Key + "' must be a Defined Text value containing structured criterion JSON.",
                        "HYDRAULIC_CRITERION_INVALID");
                HydraulicVerificationCriterion criterion;
                try
                {
                    criterion = RuntimeHostProtocol.Serializer().Deserialize<HydraulicVerificationCriterion>(entry.Value.TextValue);
                }
                catch (Exception ex)
                {
                    throw new DesignBasisValidationException(
                        "Hydraulic criterion '" + entry.Key + "' contains malformed structured JSON: " + ex.Message,
                        "HYDRAULIC_CRITERION_INVALID");
                }
                if (criterion == null)
                    throw new DesignBasisValidationException("Hydraulic criterion '" + entry.Key + "' deserialized to null.", "HYDRAULIC_CRITERION_INVALID");
                if (criterion.Provenance == null || entry.Provenance == null ||
                    GravityExecutionPlanBuilder.Json(criterion.Provenance) != GravityExecutionPlanBuilder.Json(entry.Provenance))
                    throw new DesignBasisValidationException(
                        "Hydraulic criterion '" + entry.Key + "' provenance does not match its Design Basis criterion entry.",
                        "HYDRAULIC_CRITERION_PROVENANCE");
                if (!string.Equals(entry.Key, criterion.Provenance.ParameterKey, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(entry.Key, criterion.CriterionKey, StringComparison.OrdinalIgnoreCase))
                    throw new DesignBasisValidationException(
                        "Hydraulic criterion key, provenance key and criterion identity must agree.", "HYDRAULIC_CRITERION_KEY");
                GravityExecutionPlanBuilder.ValidateEngineeringProvenance(criterion.Provenance);
                result.Add(criterion);
            }
            return result;
        }
    }
}
