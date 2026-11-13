using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Focused offline verification of the GVF persisted-result readback contract. These tests do
    // not open a model, do not load vendor assemblies and do not read hydraulic values: they
    // exercise the receiving-side validator, the request binder and the timestep/unit rules.
    internal static partial class Program
    {
        private static P16AzadeganGvfObservation GvfPassObservation()
        {
            return new P16AzadeganGvfObservation
            {
                Status = P16AzadeganGvfReadbackHost.PassStatus,
                OpenedModelPath = @"C:\temp\ReadOnly\copy\Azadegab ssw 26.10.25.stsw",
                DataSetId = 1,
                DataSetGuid = "00000000-0000-0000-0000-000000000001",
                ActiveScenarioBefore = 353,
                ActiveScenarioAfter = 353,
                RequestedScenarioId = 353,
                CurrentPhysicalAlternative = 352,
                CurrentCalculationOptions = 24,
                ExpectedCalculationOptions = 24,
                Engine = "GVFPressureEngine",
                ResultManagerEngineName = "GVFPressureEngine",
                ResultManagerScenarioId = 353,
                ResultManagerAvailable = true,
                ResultManagerWasActive = false,
                HasResults = true,
                FieldHasResults = true,
                FieldIsActive = true,
                FlowFieldSupported = true,
                SupportedFieldCount = 2,
                SupportedFieldNames = new[] { "BasicFlowResults_Flow", "BasicFlowResults_Velocity" },
                FieldRuntimeType = "Haestad.Domain.ResultField",
                FieldName = "BasicFlowResults_Flow",
                RecordName = "BasicFlowResults",
                RequestedElementLabel = "SSW-001",
                RequestedElementId = 119,
                ObservedElementLabel = "SSW-001",
                ObservedElementId = 119,
                LabelMatchCount = 1,
                ElementIdentityVerified = true,
                LabelEvidence = "Haestad.Domain.IModelingElement.Label",
                ModelConduitCount = 3,
                RequestedConduitCount = 3,
                ReadConduitCount = 3,
                DistinctReadCount = 3,
                CoverageComplete = true,
                ConduitIds = new[] { 119, 120, 121 },
                FlowValues = new[] { "0.5", "0.25", "0.125" },
                OverTimeValues = new[] { "0.5", "0.25", "0.125" },
                OverTimeLengths = new[] { 3, 3, 3 },
                OverTimeConsistent = new[] { true, true, true },
                UnreadableConduitIds = new int[0],
                OverTimeMismatchCount = 0,
                NonFiniteValueCount = 0,
                FocusedElementValue = "0.5",
                FocusedElementOverTimeValue = "0.5",
                TimeStepIndex = 0,
                TimeSeconds = 0d,
                TimeStepCount = 3,
                AvailableTimeSeconds = new[] { 0d, 60d, 120d },
                TimeStepSelectionBasis = "TARGET_TIME_PRESENT_IN_VECTOR",
                WorkingUnitSymbol = "L/s",
                WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                StorageUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                UnitEvidence = "Native IUnitizedField working-unit enum literal read from the installed field.",
                ValueClrType = "System.Double",
                ValueClassification = "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED",
                OverTimeReadInvocations = 3,
                ValueReadInvocations = 3,
                FreshnessVerified = false,
                FreshnessEvidence = "Pre-existing persisted project state only; no run event verified.",
                ResultCleanupCompleted = true,
                CleanupError = null,
                FailureOperation = null,
                ErrorType = null,
                ErrorHResult = null,
                ErrorMessage = null
            };
        }

        private static string GvfJson(P16AzadeganGvfObservation observation) =>
            RuntimeHostProtocol.Serializer().Serialize(observation);

        private static void GvfCheckRejected(P16AzadeganGvfObservation observation, string reason)
        {
            Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateObservation(GvfJson(observation)));
            Check(true, reason);
        }

        private static void P16Gvf_ContractConstantsAreTheVerifiedLiterals()
        {
            Check(P16AzadeganGvfReadbackHost.Engine == "GVFPressureEngine", "Engine is the verified GVF literal.");
            Check(P16AzadeganGvfReadbackHost.FlowField == "BasicFlowResults_Flow", "Field literal unchanged.");
            Check(P16AzadeganGvfReadbackHost.FlowRecord == "BasicFlowResults", "Record literal unchanged.");
            Check(P16AzadeganGvfReadbackHost.ScenarioId == 353, "Scenario id is explicit 353.");
            Check(P16AzadeganGvfReadbackHost.PhysicalAlternativeId == 352, "Physical alternative is 352.");
            Check(P16AzadeganGvfReadbackHost.ExpectedCalculationOptionsId == 24, "GVF calculation options is 24.");
            Check(P16AzadeganGvfReadbackHost.ElementId == 119 && P16AzadeganGvfReadbackHost.ElementLabel == "SSW-001", "Focused identity is SSW-001/119.");
            Check(P16AzadeganGvfReadbackHost.Family.Length == 6, "Exactly six family files are copied.");
            Check(P16AzadeganGvfReadbackHost.Engine != P16AzadeganReadOnlyResultHost.Engine, "GVF engine is not the Delaware engine.");
        }

        private static void P16Gvf_ExactPassObservationAccepted()
        {
            P16AzadeganGvfReadbackHost.ValidateObservation(GvfJson(GvfPassObservation()));
            Check(true, "Exact GVF pass observation accepted.");
        }

        private static void P16Gvf_ObservationRejectsEngineScenarioAndFieldSubstitution()
        {
            var wrongEngine = GvfPassObservation();
            wrongEngine.Engine = "DelawareProductEngine";
            GvfCheckRejected(wrongEngine, "Foreign engine rejected.");

            var wrongManagerEngine = GvfPassObservation();
            wrongManagerEngine.ResultManagerEngineName = "DelawareProductEngine";
            GvfCheckRejected(wrongManagerEngine, "Foreign result-manager engine rejected.");

            var wrongCalc = GvfPassObservation();
            wrongCalc.CurrentCalculationOptions = 29;
            GvfCheckRejected(wrongCalc, "Unexpected calculation options rejected.");

            var wrongAlternative = GvfPassObservation();
            wrongAlternative.CurrentPhysicalAlternative = 351;
            GvfCheckRejected(wrongAlternative, "Foreign physical alternative rejected.");

            var wrongScenario = GvfPassObservation();
            wrongScenario.ResultManagerScenarioId = 350;
            GvfCheckRejected(wrongScenario, "Foreign result-manager scenario rejected.");

            var wrongField = GvfPassObservation();
            wrongField.FieldName = "BasicFlowResults_Velocity";
            GvfCheckRejected(wrongField, "Substituted field rejected.");

            var wrongRecord = GvfPassObservation();
            wrongRecord.RecordName = "OtherResults";
            GvfCheckRejected(wrongRecord, "Substituted record rejected.");

            var wrongElement = GvfPassObservation();
            wrongElement.ObservedElementId = 120;
            GvfCheckRejected(wrongElement, "Substituted element rejected.");

            var duplicateLabel = GvfPassObservation();
            duplicateLabel.LabelMatchCount = 2;
            GvfCheckRejected(duplicateLabel, "Non-unique label rejected.");
        }

        private static void P16Gvf_TimeSelectionNeverAssumesIndexZero()
        {
            // A vector without a zero time yields no selection at all: SelectTime throws and the
            // host records RESULT_TIME_UNAVAILABLE rather than defaulting to index 0.
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new[] { 60d, 120d }));
            Check(true, "Time vector without the target time is not selectable.");

            // A single step at the target time is a cardinality fact, not an index assumption.
            Check(P16CalculatedFlowDiagnostic.SelectTime(new[] { 0d }) == 0, "Sole zero-time step is selected.");
            Check(P16CalculatedFlowDiagnostic.SelectTime(new[] { 0d, 60d }) == 0, "Target time located by value.");
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new[] { 30d, 60d, 90d }));
            Check(true, "Non-zero vector is not silently mapped to index 0.");

            // A declared index that is not the evidenced target index is rejected.
            var shifted = GvfPassObservation();
            shifted.TimeStepIndex = 1;
            shifted.TimeSeconds = 60d;
            GvfCheckRejected(shifted, "Index that does not match the target time rejected.");

            var noBasis = GvfPassObservation();
            noBasis.TimeStepSelectionBasis = null;
            GvfCheckRejected(noBasis, "Selection without a recorded basis rejected.");
        }

        private static void P16Gvf_UnitMetadataIsRequired()
        {
            var noSymbol = GvfPassObservation();
            noSymbol.WorkingUnitSymbol = null;
            GvfCheckRejected(noSymbol, "Missing working unit symbol rejected.");

            var unknownLiteral = GvfPassObservation();
            unknownLiteral.WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:MillionGallonsPerDay:99";
            unknownLiteral.WorkingUnitSymbol = "MGD";
            GvfCheckRejected(unknownLiteral, "Unit symbol not bound to the installed literal rejected.");

            var noStorage = GvfPassObservation();
            noStorage.StorageUnitIdentity = null;
            GvfCheckRejected(noStorage, "Missing storage unit identity rejected.");

            var noEvidence = GvfPassObservation();
            noEvidence.UnitEvidence = null;
            GvfCheckRejected(noEvidence, "Missing unit evidence rejected.");
        }

        private static void P16Gvf_CoverageAndFinitenessAreMandatory()
        {
            var incomplete = GvfPassObservation();
            incomplete.CoverageComplete = false;
            GvfCheckRejected(incomplete, "Incomplete coverage rejected.");

            var shortRead = GvfPassObservation();
            shortRead.ReadConduitCount = 2;
            GvfCheckRejected(shortRead, "Partial read rejected.");

            var notModelSet = GvfPassObservation();
            notModelSet.ModelConduitCount = 136;
            GvfCheckRejected(notModelSet, "Coverage smaller than the model conduit set rejected.");

            var duplicate = GvfPassObservation();
            duplicate.DistinctReadCount = 2;
            GvfCheckRejected(duplicate, "Duplicate element ids rejected.");

            var nonFinite = GvfPassObservation();
            nonFinite.NonFiniteValueCount = 1;
            GvfCheckRejected(nonFinite, "Non-finite value rejected.");

            var mismatch = GvfPassObservation();
            mismatch.OverTimeMismatchCount = 1;
            GvfCheckRejected(mismatch, "Over-time/value disagreement rejected.");

            var unreadable = GvfPassObservation();
            unreadable.UnreadableConduitIds = new[] { 120 };
            GvfCheckRejected(unreadable, "Unreadable element rejected.");

            var focusedAbsent = GvfPassObservation();
            focusedAbsent.ConduitIds = new[] { 120, 121, 122 };
            focusedAbsent.OverTimeReadInvocations = 3;
            focusedAbsent.ValueReadInvocations = 3;
            GvfCheckRejected(focusedAbsent, "Focused element absent from the read set rejected.");

            var readCountMismatch = GvfPassObservation();
            readCountMismatch.ValueReadInvocations = 1;
            GvfCheckRejected(readCountMismatch, "Read-invocation count disagreeing with coverage rejected.");

            var arrayShape = GvfPassObservation();
            arrayShape.OverTimeValues = new[] { "0.5", "0.25" };
            GvfCheckRejected(arrayShape, "Mismatched parallel-array shape rejected.");
        }

        private static void P16Gvf_ObservationRejectsForbiddenActivity()
        {
            var stale = GvfPassObservation();
            stale.ResultCleanupCompleted = false;
            GvfCheckRejected(stale, "Unclean result-manager close rejected.");

            var leaky = GvfPassObservation();
            leaky.CleanupError = "close failed";
            GvfCheckRejected(leaky, "Recorded cleanup error rejected.");

            var scenarioDrift = GvfPassObservation();
            scenarioDrift.ActiveScenarioAfter = 350;
            GvfCheckRejected(scenarioDrift, "Active scenario drift rejected.");

            var fresh = GvfPassObservation();
            fresh.FreshnessVerified = true;
            GvfCheckRejected(fresh, "Freshness promotion rejected.");

            var errored = GvfPassObservation();
            errored.ErrorType = "System.Exception";
            GvfCheckRejected(errored, "Pass observation carrying an error rejected.");

            var failure = GvfPassObservation();
            failure.FailureOperation = "GetValue";
            GvfCheckRejected(failure, "Pass observation carrying a failure operation rejected.");

            var unknownStatus = GvfPassObservation();
            unknownStatus.Status = "PASS";
            GvfCheckRejected(unknownStatus, "Unrecognised status literal rejected.");
        }

        private static void P16Gvf_RequestValidationBindsCopyInventoryAndContext()
        {
            string root = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly");
            string copyId = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(root, copyId);
            Directory.CreateDirectory(directory);
            try
            {
                foreach (string name in P16AzadeganGvfReadbackHost.Family)
                    File.WriteAllBytes(Path.Combine(directory, name), new byte[] { 1, 2, 3 });
                string digest = P16AzadeganGvfReadbackHost.InventoryDigest(directory);
                string modelPath = Path.Combine(directory, P16AzadeganGvfReadbackHost.ModelFileName);

                Func<HostRequest> valid = () => new HostRequest
                {
                    SchemaVersion = 1,
                    RunId = Guid.NewGuid().ToString("N"),
                    Operation = P16AzadeganGvfReadbackHost.Operation,
                    WorkingModelPath = modelPath,
                    WorkingCopyId = copyId,
                    RuntimeCandidateCount = 1,
                    DetectedArchitecture = "x64",
                    SelectedRuntime = root,
                    CoreFiles = new HostMetadata[0],
                    Diagnostics = P16AzadeganGvfReadbackHost.Context(copyId, digest)
                };

                P16AzadeganGvfReadbackHost.ValidateRequest(valid());
                Check(true, "Exact bounded GVF request accepted.");

                var foreign = valid();
                foreign.Operation = P16AzadeganReadOnlyResultHost.Operation;
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(foreign));
                Check(true, "Foreign operation rejected.");

                var wrongCopy = valid();
                wrongCopy.WorkingCopyId = Guid.NewGuid().ToString("N");
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(wrongCopy));
                Check(true, "Mismatched copy identity rejected.");

                var wrongPath = valid();
                wrongPath.WorkingModelPath = Path.Combine(directory, "1cm", P16AzadeganGvfReadbackHost.ModelFileName);
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(wrongPath));
                Check(true, "Path outside the disposable copy rejected.");

                var wrongEngine = valid();
                wrongEngine.Diagnostics["P16AzadeganGvfEngine"] = "DelawareProductEngine";
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(wrongEngine));
                Check(true, "Substituted engine in request rejected.");

                var extra = valid();
                extra.Diagnostics["P16AzadeganGvfExtra"] = "1";
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(extra));
                Check(true, "Undeclared diagnostic key rejected.");

                var writePlan = valid();
                writePlan.WriteAuthorizationScope = "production";
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(writePlan));
                Check(true, "Write authority rejected.");

                File.WriteAllBytes(Path.Combine(directory, "unexpected.tmp"), new byte[] { 9 });
                Throws<InvalidDataException>(() => P16AzadeganGvfReadbackHost.ValidateRequest(valid()));
                Check(true, "Extra file in the copy rejected.");
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch (Exception) { }
            }
        }

        private static void P16Gvf_NoSolverSaveOrNotificationSurface()
        {
            string source = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "src", "SewerPilot.OpenFlows.Adapter", "P16AzadeganGvfReadbackHost.cs"));
            string[] forbidden = { "SaveCurrentProject", "SaveAs", "RunSolver", ".Calculate", "Notification", "set_WorkingUnitIndex", "BeginCalculation" };
            foreach (string token in forbidden)
                Check(source.IndexOf(token, StringComparison.Ordinal) < 0, "Readback source declares no '" + token + "' surface.");
            Check(source.IndexOf("GetValuesOverTime", StringComparison.Ordinal) >= 0, "Readback uses GetValuesOverTime.");
            Check(source.IndexOf("GetValue\"", StringComparison.Ordinal) >= 0, "Readback uses GetValue at the evidenced index.");
            Check(source.IndexOf("SelectTime", StringComparison.Ordinal) >= 0, "Readback resolves the index through SelectTime.");
            Check(source.IndexOf("WorkingUnitIndex", StringComparison.Ordinal) >= 0, "Readback reads working-unit metadata.");
            Check(source.IndexOf("StorageUnitIndex", StringComparison.Ordinal) >= 0, "Readback reads storage-unit metadata.");
        }
    }
}
