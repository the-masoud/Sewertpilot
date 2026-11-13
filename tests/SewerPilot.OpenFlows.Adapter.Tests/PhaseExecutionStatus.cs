using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    /// <summary>
    /// Authentic per-phase execution status. A phase may only be reported as
    /// <see cref="VerifiedComplete"/> when it was actually evaluated, every metric resolved,
    /// and no violations or pending engineering dispositions remain.
    /// </summary>
    internal enum PhaseExecutionStatus
    {
        VerifiedComplete,
        Partial,
        DesignViolation,
        IncompleteData,
        RequiresEngineeringDisposition
    }

    /// <summary>
    /// Evidence inputs for a single phase. Counts must come from executed outcomes or from a
    /// retained audit report; they must never be asserted as constants.
    /// </summary>
    internal sealed class PhaseEvaluationInput
    {
        public string Phase { get; set; }
        public string Module { get; set; }
        public string Focus { get; set; }

        /// <summary>False when the harness did not evaluate this phase at all.</summary>
        public bool Evaluated { get; set; }
        public string Basis { get; set; }

        public int MetricsTotal { get; set; }
        public int MetricsPassed { get; set; }
        public int MetricsNotAvailable { get; set; }
        public int MetricsViolation { get; set; }
        public int PendingDispositions { get; set; }
    }

    internal static class PhaseStatusEvaluator
    {
        /// <summary>
        /// Deterministic precedence: a retained design violation outranks incomplete data,
        /// which outranks an unresolved disposition. Anything not fully resolved can never
        /// be reported as verified.
        /// </summary>
        public static PhaseExecutionStatus Evaluate(PhaseEvaluationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.MetricsTotal < 0 || input.MetricsPassed < 0 || input.MetricsNotAvailable < 0 ||
                input.MetricsViolation < 0 || input.PendingDispositions < 0)
                return PhaseExecutionStatus.IncompleteData;
            if (input.MetricsPassed > input.MetricsTotal) return PhaseExecutionStatus.IncompleteData;
            if (input.MetricsViolation > 0) return PhaseExecutionStatus.DesignViolation;
            if (!input.Evaluated) return PhaseExecutionStatus.IncompleteData;
            if (input.MetricsTotal <= 0) return PhaseExecutionStatus.IncompleteData;
            if (input.MetricsNotAvailable > 0) return PhaseExecutionStatus.IncompleteData;
            if (input.PendingDispositions > 0) return PhaseExecutionStatus.RequiresEngineeringDisposition;
            if (input.MetricsPassed == input.MetricsTotal) return PhaseExecutionStatus.VerifiedComplete;
            return PhaseExecutionStatus.Partial;
        }

        /// <summary>
        /// Rejects any caller that tries to publish a phase as verified while its evidence still
        /// carries unresolved metrics or design violations. The P17 branch is explicit because
        /// past releases shipped a fabricated all-clear matrix for that phase.
        /// </summary>
        public static void AssertNoFalseVerification(string phaseId, PhaseEvaluationInput input, PhaseExecutionStatus claimedStatus)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (claimedStatus != PhaseExecutionStatus.VerifiedComplete) return;

            bool unresolved = !input.Evaluated
                || input.MetricsTotal <= 0
                || input.MetricsTotal < 0
                || input.MetricsPassed < 0
                || input.MetricsPassed != input.MetricsTotal
                || input.MetricsViolation != 0
                || input.MetricsNotAvailable != 0
                || input.PendingDispositions != 0
                || Evaluate(input) != PhaseExecutionStatus.VerifiedComplete;
            if (!unresolved) return;

            if (string.Equals(phaseId, "P17", StringComparison.Ordinal))
                throw new InvalidOperationException("Integrity Breach: Phase P17 evaluated to VerifiedComplete despite retaining unresolved metrics or design violations.");
            throw new InvalidOperationException("Integrity Breach: Phase " + (phaseId ?? "<unknown>") +
                " evaluated to VerifiedComplete despite retaining unresolved metrics or design violations.");
        }

        /// <summary>
        /// Invariant that must hold for every row: unresolved metrics or dispositions can never
        /// be rendered as verified completion.
        /// </summary>
        public static bool IsStatusConsistent(PhaseEvaluationInput input)
        {
            var status = Evaluate(input);
            bool unresolved = input.MetricsViolation > 0 || input.MetricsNotAvailable > 0 || input.PendingDispositions > 0;
            return !(unresolved && status == PhaseExecutionStatus.VerifiedComplete);
        }

        public static string RenderStatus(PhaseEvaluationInput input)
        {
            var status = Evaluate(input);
            var sb = new StringBuilder();
            sb.Append(status.ToString().ToUpperInvariant());
            sb.Append(" (");
            sb.Append(input.MetricsPassed.ToString(CultureInfo.InvariantCulture));
            sb.Append('/');
            sb.Append(input.MetricsTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(" resolved");
            if (input.MetricsViolation > 0)
                sb.Append(", " + input.MetricsViolation.ToString(CultureInfo.InvariantCulture) + " violation(s)");
            if (input.MetricsNotAvailable > 0)
                sb.Append(", " + input.MetricsNotAvailable.ToString(CultureInfo.InvariantCulture) + " not available");
            if (input.PendingDispositions > 0)
                sb.Append(", " + input.PendingDispositions.ToString(CultureInfo.InvariantCulture) + " pending disposition(s)");
            sb.Append(')');
            return sb.ToString();
        }

        public static string BuildMatrixMarkdown(IEnumerable<PhaseEvaluationInput> inputs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SewerPilot — System Verification & Capability Matrix");
            sb.AppendLine();
            sb.AppendLine("> Generated from executed pipeline outcomes and retained audit reports.");
            sb.AppendLine("> Rows are emitted only for phases this harness actually evaluates; a phase with");
            sb.AppendLine("> unresolved metrics is never reported as VERIFIED_COMPLETE.");
            sb.AppendLine();
            sb.AppendLine("| Phase | Subsystem / Module | Focus | Status | Basis |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var input in inputs)
            {
                if (input == null) throw new ArgumentNullException(nameof(inputs));
                AssertNoFalseVerification(input.Phase, input, Evaluate(input));
                sb.Append("| ");
                sb.Append(input.Phase);
                sb.Append(" | ");
                sb.Append(input.Module);
                sb.Append(" | ");
                sb.Append(input.Focus);
                sb.Append(" | **");
                sb.Append(RenderStatus(input));
                sb.Append("** | ");
                sb.Append(input.Basis);
                sb.AppendLine(" |");
            }
            sb.AppendLine();
            sb.AppendLine("*P1–P16 are outside this artifact's evaluation scope and must be accepted from their own retained receipts.*");
            return sb.ToString();
        }

        /// <summary>
        /// Reads the P17 disposition from its retained audit report rather than asserting constants.
        /// </summary>
        public static PhaseEvaluationInput LoadP17FromEvidence(string repositoryRoot)
        {
            string relative = Path.Combine("docs", "evidence", "p17", "20261018-live-02-native-integration",
                "step4-presentation-reporting", "final-design-audit-report.md");
            string path = Path.Combine(repositoryRoot ?? string.Empty, relative);
            string basis = relative.Replace('\\', '/');
            if (!File.Exists(path))
            {
                var missing = NewP17Input();
                missing.Evaluated = false;
                missing.Basis = basis + " (missing)";
                return missing;
            }

            return LoadP17FromText(File.ReadAllText(path), basis);
        }

        /// <summary>
        /// Parses a P17 audit report payload. Used by evidence loading and by the regression
        /// suite to prove that fabricated completion is rejected for retained findings.
        /// </summary>
        public static PhaseEvaluationInput LoadP17FromText(string auditText, string basis)
        {
            var input = NewP17Input();
            if (string.IsNullOrWhiteSpace(auditText))
            {
                input.Evaluated = false;
                input.Basis = basis + " (missing)";
                return input;
            }

            Match match = Regex.Match(auditText, @"Findings:\s*pass=(\d+)\s+violation=(\d+)\s+notAvailable=(\d+)");
            if (!match.Success)
            {
                input.Evaluated = false;
                input.Basis = basis + " (unparseable)";
                return input;
            }

            int passed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int violation = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int notAvailable = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            input.MetricsPassed = passed;
            input.MetricsViolation = violation;
            input.MetricsNotAvailable = notAvailable;
            input.MetricsTotal = passed + violation + notAvailable;
            input.PendingDispositions = violation;
            input.Evaluated = input.MetricsTotal > 0;
            input.Basis = basis;
            return input;
        }

        private static PhaseEvaluationInput NewP17Input()
        {
            return new PhaseEvaluationInput
            {
                Phase = "P17",
                Module = "Single Operator / GravityFlow E2E",
                Focus = "Desktop workflow, content authority, hydraulic completeness"
            };
        }
    }
}
