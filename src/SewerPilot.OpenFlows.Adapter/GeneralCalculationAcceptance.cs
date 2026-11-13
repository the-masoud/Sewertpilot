using System;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // One deterministic acceptance decision for a general calculation. The service, the receipt and
    // the test harness all call this, so none of them can print a verdict the others disagree with.
    internal sealed class GeneralAcceptance
    {
        internal string Verdict;
        internal string Reason;
        internal string Explanation;
        internal bool WarningsUnresolved;
        internal bool WarningDetailsAvailable;
    }

    // The acceptance rule for a hydraulic calculation that has already been solved, persisted and
    // independently read back. It never decides whether a calculation may run - only what the finished
    // run is worth. A warning-bearing run keeps its hydraulic results and its evidence; it is simply
    // not promoted to PASS.
    internal static class GeneralCalculationAcceptance
    {
        internal const string Pass = "PASS";
        internal const string Partial = "PARTIAL";
        internal const string Blocked = "BLOCKED";

        internal const string WarningsUnresolved = "ENGINE_WARNINGS_UNRESOLVED";

        // The only warning-bearing outcome that may be accepted: the vendor raised its notification
        // exception, but every notification it carried was a fully captured Information-level notice,
        // so there is nothing left for a human to resolve. Deliberately distinct from
        // CLEAN_ENGINE_VERIFIED_READBACK - an informational exception is not an exception-free run.
        internal const string InformationalNotificationsVerifiedReadback =
            "INFORMATIONAL_NOTIFICATIONS_VERIFIED_READBACK";

        // The vendor's own severity token. Matched exactly after trimming: "Info", an empty value or a
        // numeric code is not the same thing and must not be inferred to mean this.
        internal const string InformationLevel = "Information";

        internal static GeneralAcceptance Classify(GeneralCalculationObservation calculation,
            GeneralReadbackObservation readback)
        {
            if (calculation == null) return BlockedOn("CALCULATION_EVIDENCE_MISSING",
                "No calculation observation was produced, so nothing can be accepted.");
            if (readback == null) return BlockedOn("READBACK_EVIDENCE_MISSING",
                "The calculation was never read back by an independent process.");

            // The solve itself. Exactly one invocation is the whole contract: zero means nothing ran,
            // more than one means the budget was exceeded and the result cannot be attributed.
            if (calculation.SolverRunInvocations != 1) return BlockedOn("SOLVER_NOT_RUN_ONCE",
                "The solver ran " + calculation.SolverRunInvocations + " time(s); exactly one is required.");

            // Persistence. A result that was not written down cannot be read back, so this precedes
            // every hydraulic question.
            if (!calculation.SaveSucceeded ||
                !string.Equals(calculation.SaveResultClassification, "PERSISTED", StringComparison.Ordinal))
                return BlockedOn("PERSISTENCE_INVALID",
                    "The calculated model was not persisted, so the results are not durable.");

            if (!string.Equals(calculation.ScenarioComputeStatus, "Success", StringComparison.Ordinal))
                return BlockedOn("SCENARIO_NOT_SUCCESS",
                    "The vendor reported scenario status '" + calculation.ScenarioComputeStatus + "'.");

            // Readback. The reopened model must hold results, and they must be real hydraulic values
            // rather than default-filled rows.
            if (!string.Equals(readback.Status, GeneralModelCalculationHost.ReadVerifiedStatus, StringComparison.Ordinal))
                return BlockedOn("READBACK_INVALID",
                    "The independent readback did not verify: " + (readback.Status ?? "(no status)") + ".");
            if (!readback.HasResults) return BlockedOn("READBACK_INVALID",
                "The reopened model reports no results.");
            if (readback.AvailableRowCount <= 0) return BlockedOn("READBACK_INVALID",
                "The reopened model holds no available result rows.");
            if (readback.Rows == null || !readback.Rows.Any(row => row != null && row.Value.HasValue &&
                    string.Equals(row.Status, "AVAILABLE", StringComparison.Ordinal)))
                return BlockedOn("NO_HYDRAULIC_ROWS",
                    "The readback holds no available numeric hydraulic value, so the solve cannot be shown to have produced results.");

            // Provenance. The rows must belong to this calculation and no other.
            if (!string.Equals(readback.CalculationEventId, calculation.CalculationEventId, StringComparison.Ordinal))
                return BlockedOn("PROVENANCE_MISMATCH",
                    "The readback event differs from the calculation event.");
            if (readback.ScenarioId != calculation.ScenarioId ||
                readback.CalculationOptionsId != calculation.CalculationOptionsId)
                return BlockedOn("PROVENANCE_MISMATCH",
                    "The readback scenario or calculation options differ from the calculation.");
            if (!string.Equals(readback.Engine, calculation.Engine, StringComparison.Ordinal))
                return BlockedOn("PROVENANCE_MISMATCH",
                    "The readback engine differs from the calculation engine.");

            // Engine outcome, last, because only a run that produced durable, attributable hydraulics
            // is worth grading on how cleanly the engine exited.
            bool warnings = IsWarningOutcome(calculation);
            string outcome = calculation.EngineOutcome;
            if (string.Equals(outcome, "COMPLETED_CLEAN", StringComparison.Ordinal))
            {
                if (warnings) return BlockedOn("ENGINE_OUTCOME_INCONSISTENT",
                    "A warning exception was reported but the engine outcome claims a clean completion.");
                return new GeneralAcceptance
                {
                    Verdict = Pass,
                    Reason = "CLEAN_ENGINE_VERIFIED_READBACK",
                    Explanation = "The engine completed without a reported exception, the result was persisted " +
                        "and an independent process read back " + readback.AvailableRowCount +
                        " hydraulic rows attributable to this calculation event.",
                    WarningsUnresolved = false,
                    WarningDetailsAvailable = false
                };
            }
            if (string.Equals(outcome, "COMPLETED_WITH_EXCEPTION_REPORTED", StringComparison.Ordinal))
            {
                if (warnings)
                {
                    // Severity decides here, and only here: a warning exception whose every notification
                    // is a completely captured Information notice is accepted, everything else keeps the
                    // conservative PARTIAL it has always had.
                    GeneralAcceptance informational = AcceptInformationalOnly(calculation, readback);
                    if (informational != null) return informational;

                    bool details = calculation.WarningNotificationCount.HasValue ||
                        (calculation.Warnings != null && calculation.Warnings.Length > 0);
                    return new GeneralAcceptance
                    {
                        Verdict = Partial,
                        Reason = WarningsUnresolved,
                        Explanation = "The engine raised EngineWarningsException. The results are real, persisted " +
                            "and independently read back, but the warnings are unresolved" +
                            (details ? "." : ", and their details were unavailable (UNKNOWN)."),
                        WarningsUnresolved = true,
                        WarningDetailsAvailable = details
                    };
                }
                return BlockedOn("ENGINE_EXCEPTION",
                    "The engine raised an exception that is not a warning exception (" +
                    (calculation.ErrorType ?? "unknown type") + ").");
            }
            return BlockedOn("ENGINE_OUTCOME_UNKNOWN",
                "The engine outcome is '" + (outcome ?? "(none)") + "', which is not a completed calculation.");
        }

        // Accepts a warning exception only when the notification evidence is complete, self-consistent
        // and entirely informational. Every other shape returns null so the caller keeps PARTIAL: a
        // missing, unknown, truncated or contradictory capture is never read as a clean notification set.
        private static GeneralAcceptance AcceptInformationalOnly(GeneralCalculationObservation calculation,
            GeneralReadbackObservation readback)
        {
            const string vendor = GeneralModelCalculationHost.EngineWarningsExceptionType;

            // 1-2. Exception identity, and no contradiction between the two recorded type fields.
            if (!string.Equals(calculation.WarningExceptionType, vendor, StringComparison.Ordinal)) return null;
            if (!string.IsNullOrEmpty(calculation.ErrorType) &&
                !string.Equals(calculation.ErrorType, vendor, StringComparison.Ordinal)) return null;

            // 3-5. The capture itself must have succeeded, with no recorded failure and a real count.
            if (!string.Equals(calculation.WarningCaptureStatus,
                    GeneralModelCalculationHost.WarningCaptureCaptured, StringComparison.Ordinal)) return null;
            if (!string.IsNullOrWhiteSpace(calculation.WarningCaptureFailure)) return null;
            if (!calculation.WarningNotificationCount.HasValue) return null;
            int declared = calculation.WarningNotificationCount.Value;
            if (declared <= 0) return null;

            // 10. More notifications than the collector can hold can only come from evidence that was
            // truncated or not produced by the bounded collector, so completeness cannot be claimed.
            if (declared > GeneralModelCalculationHost.MaxEngineWarnings) return null;

            // 6-7. The array must exist, match the declared count, and hold no holes.
            if (calculation.Warnings == null || calculation.Warnings.Length != declared) return null;

            // 8-9. Every notification needs its own identity and an exactly recognised severity.
            foreach (GeneralEngineWarning warning in calculation.Warnings)
            {
                if (warning == null) return null;
                if (string.IsNullOrWhiteSpace(warning.MessageKey)) return null;
                string level = warning.Level == null ? null : warning.Level.Trim();
                if (!string.Equals(level, InformationLevel, StringComparison.OrdinalIgnoreCase)) return null;
            }

            return new GeneralAcceptance
            {
                Verdict = Pass,
                Reason = InformationalNotificationsVerifiedReadback,
                Explanation = "The engine raised an informational notification exception " +
                    "(EngineWarningsException) carrying " + declared + " Information-level notification" +
                    (declared == 1 ? "" : "s") + ", completely captured. The verified hydraulic results were " +
                    "preserved: they were persisted and an independent process read back " +
                    readback.AvailableRowCount + " hydraulic rows attributable to this calculation event. " +
                    "This is not an exception-free engine run.",
                WarningsUnresolved = false,
                WarningDetailsAvailable = true
            };
        }

        // A warning outcome is recognised by the vendor type, recorded either by the capture itself or
        // by the generic error fields on observations written before capture existed.
        internal static bool IsWarningOutcome(GeneralCalculationObservation calculation)
        {
            if (calculation == null) return false;
            string type = calculation.WarningExceptionType ?? calculation.ErrorType;
            return string.Equals(type, GeneralModelCalculationHost.EngineWarningsExceptionType,
                StringComparison.Ordinal);
        }

        private static GeneralAcceptance BlockedOn(string reason, string explanation)
        {
            return new GeneralAcceptance
            {
                Verdict = Blocked,
                Reason = reason,
                Explanation = explanation,
                WarningsUnresolved = false,
                WarningDetailsAvailable = false
            };
        }
    }
}
