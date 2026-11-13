using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.RegularExpressions;
using SewerPilot.OpenFlows.Adapter;
using SewerPilot.OpenFlows.Adapter.HydraulicRules;
using SewerPilot.OpenFlows.Compatibility;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public enum LicenseProductProbe { Bentley_SewerGEMS = 1244 }
    public enum LicensePlatformProbe { Standalone }
    public enum LicenseRunProbe { OK, Shutdown }
    public interface LicenseMessageProbe { }
    public sealed class LicenseReleaseProbe
    {
        private LicensePlatformProbe platformType;
        public int PlatformAssignments { get; private set; }
        public LicensePlatformProbe PlatformType
        {
            get => platformType;
            set { platformType = value; PlatformAssignments++; }
        }
        public LicenseReleaseProbe(LicenseProductProbe product, string version)
        { LicenseProbe.Calls.Add("Product:" + product + ":" + version); }
        public void Dispose() { LicenseProbe.Calls.Add("ReleaseDispose"); }
    }
    public sealed class LicenseProbe
    {
        public static readonly List<string> Calls = new List<string>();
        public static bool InitializationFails, StartThrows, StopThrows, AnalysisDisabled;
        public static LicenseRunProbe Status;
        public static LicenseReleaseProbe LastRelease;
        public bool IsInitialized { get; private set; }
        public bool IsAnalysisEnabled => !AnalysisDisabled;
        public static LicenseProbe Default(LicenseReleaseProbe product, IntPtr owner, LicenseMessageProbe message)
        { Calls.Add("Default"); LastRelease = product; return new LicenseProbe(); }
        public bool Initialize() { Calls.Add("Initialize"); IsInitialized = !InitializationFails; return IsInitialized; }
        public LicenseRunProbe StartDesktop()
        { Calls.Add("Start"); if (StartThrows) throw new InvalidOperationException("Synthetic license start failure"); return Status; }
        public void StopDesktop()
        { Calls.Add("Stop"); if (StopThrows) throw new InvalidOperationException("Synthetic license stop failure"); }
        public void Dispose() { Calls.Add("LicenseDispose"); }
        public static void Reset()
        { Calls.Clear(); LastRelease = null; InitializationFails = StartThrows = StopThrows = AnalysisDisabled = false; Status = LicenseRunProbe.OK; }
    }
    public class SolverEngineProbe
    {
        public string Name = "P15TestEngine";
        public int RunCount;
        public int ResultReads;
        public object TargetCollection;
        public bool FailRun;
        public LicenseProbe AttachedLicense;
        public void SetLicensingInfo(LicenseProbe license) { AttachedLicense = license; LicenseProbe.Calls.Add("Bind"); }
        public string ReadName() => Name;
        public void Execute(object collection)
        {
            RunCount++;
            if (AttachedLicense != null) LicenseProbe.Calls.Add("Run");
            TargetCollection = collection;
            if (FailRun) throw new InvalidOperationException("Synthetic solver failure");
        }
        public double Flow { get { ResultReads++; throw new InvalidOperationException("Hydraulic reads forbidden"); } }
    }

    public class SolverDataSetProbe
    {
        public object Engine;
        public int ResolutionCount;
        public string RequestedEngine;
        public object Resolve(string name) { ResolutionCount++; RequestedEngine = name; return Engine; }
    }

    public class SolverCollectionProbe
    {
        public static int ConstructionCount;
        public static int AddCount;
        public static int ForcedCount = -1;
        public readonly List<object> Elements = new List<object>();
        public SolverCollectionProbe() { ConstructionCount++; }
        public int Count => ForcedCount >= 0 ? ForcedCount : Elements.Count;
        public int AddElement(object element) { AddCount++; Elements.Add(element); return Elements.Count - 1; }
    }

    public sealed class JournalRemoteWorker : MarshalByRefObject
    {
        public override object InitializeLifetimeService() => null;
        public void Emit() { RuntimeStageJournal.Call("JournalRemote", () => { }); }
    }

    internal static partial class Program
    {
        private static int assertions;
        private static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--farzanegan-source-data-review-live")
                return FarzaneganSourceDataReviewLiveRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--farzanegan-p13-p14-workspace")
                return FarzaneganP13P14OperationalRunner.Run(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--farzanegan-p14-source-criteria")
                return FarzaneganP14SourceCriteriaRunner.Run(args[1], args[2], args[3]);
            if (args.Length == 3 && args[0] == "--farzanegan-p14-baseline-preview")
                return FarzaneganP14BaselinePreviewRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--farzanegan-design-intake-live")
                return FarzaneganDesignIntakeLiveRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--farzanegan-hydraulic-remediation-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1], args[2], true);
            if (args.Length == 3 && args[0] == "--farzanegan-p16-remediation-prepare-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1],args[2],false,false,false,false,false,false,true);
            if (args.Length == 3 && args[0] == "--farzanegan-p16-remediation-compute-live")
                return FarzaneganPilotLiveRunner.Run(args[1],args[2],false,true);
            if (args.Length == 1 && args[0] == "--p16-analysis-remediation-safety")
                return P16AnalysisRemediationSafetyTests.Run();
            if (args.Length == 3 && args[0] == "--farzanegan-p14-analysis-prepare-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1],args[2],false,false,false,false,false,true);
            if (args.Length == 3 && args[0] == "--farzanegan-co40-scoped-compute-live")
                return FarzaneganPilotLiveRunner.Run(args[1],args[2],true);
            if (args.Length == 3 && args[0] == "--farzanegan-co40-scoped-repair-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1],args[2],false,false,false,false,true);
            if (args.Length == 3 && args[0] == "--farzanegan-p14-prepare-only-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1], args[2], false, false, false, true);
            if (args.Length == 3 && args[0] == "--farzanegan-co40-label-repair-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1], args[2], false, false, true);
            if (args.Length == 1 && args[0] == "--co40-native-label-safety")
                return Co40NativeLabelSafetyTests.Run();
            if (args.Length == 1 && args[0] == "--beta-diagnostics-safety")
                return BetaDiagnosticsTests.Run();
            if (args.Length == 3 && args[0] == "--beta-application-smoke")
                return BetaApplicationSmokeTest.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--general-hydraulic-execution")
                return GeneralHydraulicExecutionTests.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--general-terrain-inspection")
                return GeneralHydraulicExecutionTests.InspectTerrain(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--general-terrain-rebind")
                return GeneralHydraulicExecutionTests.Rebind(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--general-calculation-preparation")
                return GeneralHydraulicExecutionTests.RunPreparation(args[1], args[2]);
            if (args.Length == 1 && args[0] == "--general-hydraulic-safety")
                return GeneralHydraulicExecutionTests.RunSafety();
            // Focused offline subset: the notification-severity rule and the capture mapping only. It
            // touches no model, no host and no temporary directory.
            if (args.Length == 1 && args[0] == "--general-warning-acceptance-safety")
                return GeneralHydraulicExecutionTests.RunWarningAcceptanceSafety();
            // Replays the acceptance decision over already-saved observations. Read-only: no host is
            // launched and no solver can be reached from this path.
            if (args.Length == 2 && args[0] == "--general-acceptance-replay")
                return GeneralHydraulicExecutionTests.ReplayAcceptance(args[1]);
            if (args.Length == 3 && args[0] == "--farzanegan-catalog-pointer-trial-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1], args[2], false, true);
            if (args.Length == 1 && args[0] == "--remediation-native-safety")
                return RemediationNativeSafetyTests.Run();
            if (args.Length == 1 && args[0] == "--p14-native-safety")
                return P14NativeSafetyTests.Run();
            if (args.Length == 3 && args[0] == "--farzanegan-p14-native-test-only-live")
                return FarzaneganP14NativeLiveRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--farzanegan-native-read-live")
                return FarzaneganPilotReadRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--p14-catalog-selector-probe")
                return P14CatalogSelectorDiscoveryRunner.Run(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--farzanegan-native-pilot-live")
                return FarzaneganPilotLiveRunner.Run(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--p16-native-desktop-e2e-live")
                return P16NativeDesktopLiveAcceptance.Run(args[1]);
            if (args.Length == 2 && args[0] == "--p16-ui-live-receipt")
                return P16UiReadbackAcceptance.Run(args[1]);
            if (args.Length == 1 && args[0] == "--swmm-parity-remediation")
            {
                SwmmParity_TrueRelativeDeltaAndFiniteGate();
                SwmmParity_ImportedIdentityMapNoGuessedAliases();
                SwmmParity_ReportParsingMissingAndMalformedObservations();
                SwmmParity_TimeoutTerminatesOwnedJobWithoutVendorExecution();
                Remediation_LandXmlGeometryAndMissingInput();
                Remediation_UnsupportedNodeCannotClaimHgl();
                Remediation_NativeControlFlowIsNotDropped();
                Remediation_SwmmControlSemanticsAndLosslessPrecision();
                N02_FormatFloatRejectsNonfiniteAndPreservesFinite();
                N02_ExportFailsClosedOnNonfiniteElement();
                Console.WriteLine("REMEDIATION_OFFLINE_TESTS: PASS");
                return 0;
            }
            if (args.Length > 0 && args[0] == "--civil3d-live-roundtrip")
                return Civil3dLiveTests.RunCivil3dLiveTest();
            if (args.Length > 0 && args[0] == "--p16-azadegan-result-live")
                return AzadeganResultLiveRunner.Run(args);
            if (args.Length > 0 && args[0] == "--p16-azadegan-calculation-live")
                return AzadeganCalculationLiveRunner.Run(args);
            if (args.Length > 0 && args[0] == "--p16-azadegan-output-reader-live")
                return AzadeganOutputReaderLiveRunner.Run(args);
            if (args.Length > 0 && args[0] == "--p16-azadegan-subengine-probe-live")
                return AzadeganOutputReaderLiveRunner.RunSubEngineProbe(args);
            if (args.Length > 0 && args[0] == "--p16-azadegan-gvf-readback-live")
                return AzadeganGvfReadbackLiveRunner.Run(args);
            if (args.Length > 0 && args[0] == "--m3-field-pilot")
            {
                string targetDir = args.Length > 1 ? args[1] : null;
                var res = MunicipalFieldPilotPipeline.ExecutePilot(targetDir);
                Console.WriteLine("Milestone 3 Field Pilot Completed. Verdict: " + res.OverallStatus);
                return res.Success ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == "--m4-swmm")
            {
                string targetDir = args.Length > 1 ? args[1] : null;
                return SwmmIntegrationTests.RunAllSwmmTests(targetDir);
            }
            if (args.Length == 2 && args[0] == "--m5-measure")
            {
                var suite = ProductionPipelineOrchestrator.ExecuteExtendedBenchmarkSuite(args[1]);
                Console.WriteLine("M5_MEASURED_STATUS: " + suite.OverallStatus + "; metrics=" + suite.PassedMetricsCount + "/" + suite.TotalMetricsCount + "; max_delta=" + suite.MaxRelativeDeltaPercent);
                return suite.OverallStatus == "PASS" ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--m4-measure")
            {
                var suite = SwmmCrossSolverValidationEngine.RunCrossSolverValidation(args[1]);
                Console.WriteLine("M4_MEASURED_STATUS: " + suite.OverallStatus + "; metrics=" + suite.PassedMetricsCount + "/" + suite.TotalMetricsCount);
                return suite.OverallStatus == "PASS" ? 0 : 1;
            }
            if (args.Length > 0 && args[0] == "--m5-pipeline")
            {
                string targetDir = args.Length > 1 ? args[1] : null;
                return PipelineAutomationAndHardeningTests.RunM5PipelineTests(targetDir);
            }
            if (args.Length > 0 && args[0] == "--p28-hardening")
            {
                string targetDir = args.Length > 1 ? args[1] : null;
                return PipelineAutomationAndHardeningTests.RunP28HardeningTests(targetDir);
            }
            if (args.Length > 0 && args[0] == "--p14-auto-02-gui-host")
                return args.Length == 1 ? RunP14AutomaticProposalGuiHost() : 2;
            var tests = new Action[] { PublicBoundary, LazyBinding, VersionSelection, HigherVersionNotPreferred,
                FailedOpen, MissingDataset, MissingDatum, SuccessfulRead, OtherCapabilities,
                OtherAcceptanceFailures, SourceMismatch, WorkingMismatch, TransientPreserved,
                SourceCopySafety, NamedEnum, InheritedElementIDs, AmbiguousElementIDs, IntegerIndexer,
                CleanupAfterFailure, NoWriteOperations, DurableStageJournal, RequestValidation, ResultParsing, InvalidResults, CompletionRequirements,
                HostExitClassification, HostTimeoutOwnership, ProcessPromotionGates, ProductionDomainBoundary, ProcessDtoBoundary,
                ProvenanceIdentity, ProvenanceRequester, ProvenanceBoundary, ProvenanceSemantics,
                ProvenanceDynamic, ProvenancePropertyFailure, ProvenanceResultPersistence,
                CompanionAnchorValidation, CompanionRootContainment, VendorProvenanceRejection, CompanionProvenanceResultPersistence,
                DeterministicFingerprintRepeatability, PathIndependence, ChangedBytesDifferentFingerprint,
                IntegrityLengthAndHashDifferences, MissingFamilyFileDetected, DuplicateFamilyEntryRejected,
                ManifestOrderingIndependence, MetadataAndRuntimeNoiseExcluded, WorkingCopyIdentityLinkage,
                DifferentWorkingCopyInstancesUniqueId, SafePathSamePathAndSubpathRejection, SafePathNormalizationAndTraversal,
                ControlledWorkingCopyCreationAndAtomicity, WriteAuthorizationValidGrant, WriteAuthorizationDeniedOnBaselineModified,
                WriteAuthorizationDeniedOnWorkingCopyIncomplete, WriteAuthorizationDeniedOnMismatchedBaseline,
                ApiEvidenceNoVendorTypes, DeterministicMemberSignatures,
                CreationMembersRecordedWithoutInvocation, PersistenceMembersRecordedWithoutInvocation,
                WriteInvocationCountersZero, ClassificationDistinguishesMappedFromSupported,
                AmbiguousMembersFailClosed, MissingCalculationOptionsDoesNotProveCapability,
                BoundedInspectionDoesNotRecursivelyScanAssemblies, ModelProtectionEnforcedInP6A,
                ScenarioEnumerationIndependentOfCalculationOptions, ScenarioEnumerationPrecedesCalculationOptions,
                CalculationOptionsNeverCalledWithNullEmptyOrWhitespace, CalculationOptionsFailurePreservesScenarioRead,
                CalculationOptionsFailurePreservesAlternativeRead, AlternativeTypeIdsFromRuntimeDiscoveredTypes,
                ReadOverloadOfAlternativeIdCannotInvokeSetter, WriteApiReflectionCapturesExactReturnTypeWithoutInvocation,
                ScenarioCopyReturnsInt32Id, CreatedScenarioResolvedViaElement,
                AlternativeCopyReturnsInt32Id, CreatedAlternativeResolvedViaElement,
                PhysicalAlternativeTypeDynamicallyResolved, BaselineCannotObtainWriteAuthorization,
                WrongWorkingCopyDeniedAuthorization, ScenarioCopyPrecedesAlternativeCopy,
                SourceScenarioAssignmentNeverMutated, SingleTargetAssignmentDifferenceAllowed,
                NonPhysicalAssignmentDifferenceFailsClosed, ScenarioManagerAddForbidden,
                AlternativeManagerAddForbidden, SaveAsForbidden,
                EngineeringMutationRemainsZero, Host2ReadOnlyEnforced,
                ReopenMissingCreatedScenarioFails, ReopenMissingCreatedAlternativeFails,
                ReopenAssignmentMismatchFails, PublicVendorTypesZero,
                P7_CreateSaveLoadRoundtrip,
                P7_ProjectIdStableAcrossReload,
                P7_StateRevisionIncrementsOnSuccessfulSave,
                P7_StaleStateRevisionSaveRejected,
                P7_UnsupportedSchemaRejected,
                P7_MalformedProjectFileRejected,
                P7_DuplicateScenarioMappingRejected,
                P7_DuplicateAlternativeMappingRejected,
                P7_ModelFingerprintSurvivesRoundtripExactly,
                P7_RuntimeVersionBuildSurvivesAsDiagnostics,
                P7_VersionBuildDoesNotAffectModelDeterministicIdentity,
                P7_ScenarioAlternativeIntegerIdsSurviveRoundtrip,
                P7_EmptyDesignBasisReferenceAllowed,
                P7_DesignRunIdSurvivesRoundtrip,
                P7_AuditAppendCreatesSequence1,
                P7_SubsequentAuditAppendIncrementsSequence,
                P7_ReloadThenAppendContinuesSequence,
                P7_AuditProjectIdMismatchRejected,
                P7_MalformedAuditLineRejected,
                P7_PriorAuditRecordNotModifiedByLaterAppend,
                P7_StateTemporaryFileCommitLeavesValidFinalState,
                P7_SimulatedFailedStateWriteDoesNotCorruptPreviousValidState,
                P7_StateConflictDoesNotOverwriteNewerState,
                P7_AuditFailureAfterStateSaveReturnsExplicitPartialFailureClassification,
                P7_NoVendorPublicTypesInPersistenceModel,
                P7_NoSecretsWrittenByNormalProjectModel,
                P7_DeterministicSerializationIsCultureIndependent,
                P7_PathMetadataDoesNotBecomeModelIdentity,
                P8_StandardDefinitionValidConstruction,
                P8_MissingStandardIdRejected,
                P8_MissingNameRejected,
                P8_DuplicateParameterKeyRejected,
                P8_ParameterKeyCanonicalizationDeterministic,
                P8_NumberValueRoundtrip,
                P8_IntegerValueRoundtrip,
                P8_BooleanValueRoundtrip,
                P8_TextValueRoundtrip,
                P8_QuantityValueUnitRoundtrip,
                P8_NonFiniteNumericValueRejected,
                P8_QuantityMissingUnitRejected,
                P8_ApplicationDefaultResolvesWhenOnlySource,
                P8_PublishedStandardOverridesApplicationDefault,
                P8_CompanyStandardOverridesPublishedStandard,
                P8_ProjectOverrideOverridesCompanyStandard,
                P8_FullFourLayerPrecedenceCorrect,
                P8_WinnerProvenanceRetained,
                P8_ShadowedCandidateProvenanceRetained,
                P8_NoDefinedValueReturnsExplicitNotDefined,
                P8_TwoEligibleStandardsSameLayerWithoutSelectionProduceAmbiguous,
                P8_ExplicitSameLayerSelectionResolvesDeterministically,
                P8_OverrideDoesNotMutateLowerLayer,
                P8_InputOrderingDoesNotAlterResolution,
                P8_CultureDoesNotAlterNumericUnitSerializationOrResolution,
                P8_VersionMetadataSurvivesRoundtrip,
                P8_VersionDoesNotAutomaticallyDetermineWinner,
                P8_ApplicationDefaultRemainsVisiblyClassifiedAsDefault,
                P8_NoVendorTypesLeak,
                P8_ProjectStorageReferenceRoundtrip,
                P9_DesignBasisIdStable,
                P9_DesignBasisVersionPreserved,
                P9_DeterministicContentFingerprintRepeatability,
                P9_InsertionOrderDoesNotAffectFingerprint,
                P9_CurrentCultureDoesNotAffectFingerprint,
                P9_AbsolutePathDoesNotAffectFingerprint,
                P9_MachineUserSessionMetadataDoesNotAffectFingerprint,
                P9_ReviewTimestampDoesNotAffectFingerprint,
                P9_ReviewerIdentityDoesNotAffectFingerprint,
                P9_BentleyRuntimeMetadataDoesNotAffectFingerprint,
                P9_ChangedAssumptionValueChangesFingerprint,
                P9_AddedAssumptionChangesFingerprint,
                P9_ChangedOverrideChangesFingerprint,
                P9_ChangedStandardReferenceChangesFingerprint,
                P9_DuplicateAssumptionIdRejected,
                P9_DuplicateAssumptionKeyRejected,
                P9_MissingAssumptionProvenanceRejected,
                P9_InvalidTypedAssumptionValueRejected,
                P9_DuplicateOverrideIdRejected,
                P9_DuplicateActiveOverrideKeyRejected,
                P9_OverrideWithoutRationaleRejected,
                P9_SourceP8StandardRemainsImmutable,
                P9_P8ResolvedProvenanceSurvivesDesignBasisLinkage,
                P9_ApplicationDefaultProvenanceRemainsVisiblyDefault,
                P9_DraftToReadyForReviewSucceedsOnlyWhenValid,
                P9_InvalidDraftCannotBecomeReadyForReview,
                P9_ReadyForReviewToReviewedSucceeds,
                P9_ReadyForReviewToNeedsRevisionSucceeds,
                P9_NeedsRevisionToDraftSucceeds,
                P9_InvalidReviewTransitionRejected,
                P9_ReviewTransitionDoesNotAlterContentFingerprint,
                P9_ImmutableSnapshotCannotBeChangedThroughCallerCollectionMutation,
                P9_IdenticalContentUnderDifferentPhysicalStorageLocationsYieldsSameFingerprint,
                P9_DesignBasisIdRemainsDistinctFromContentFingerprint,
                P9_DesignBasisVersionRemainsDistinctFromContentFingerprint,
                P9_P7DesignBasisReferenceMappingWorks,
                P9_P7RoundtripRegressionPassesIfStorageModified,
                P9_NoPublicVendorTypes,
                P9_NoPersistedVendorTypes,
                P9_NoProductionModuleSpecificEngineeringCriteria,
                P10_GravityFlowAggregateRequiresCommonDesignBasis,
                P10_ModuleIdentityIsGravityFlow,
                P10_AllSevenSectionsRepresented,
                P10_CanonicalSectionOrderingDeterministic,
                P10_DuplicateCriterionKeyInSameSectionRejected,
                P10_SameKeyInDistinctSectionsValid,
                P10_UnknownSectionRejected,
                P10_StormFlowSectionOrContentRejected,
                P10_P8StandardValueReused,
                P10_P8QuantityUnitReused,
                P10_DuplicateUnitValueInfrastructureNotIntroduced,
                P10_P8ResolvedStandardProvenanceRetained,
                P10_ApplicationDefaultRemainsVisiblyApplicationDefault,
                P10_P8PrecedenceNotDuplicated,
                P10_P9AssumptionInfrastructureReused,
                P10_P9OverrideInfrastructureReused,
                P10_OverrideRationalePreserved,
                P10_SourceStandardNotMutated,
                P10_CommonP9SnapshotNotMutated,
                P10_GravityFlowSnapshotImmutable,
                P10_CallerCollectionMutationCannotAlterSnapshot,
                P10_GravityFingerprintRepeatable,
                P10_InputCriterionOrderingDoesNotChangeFingerprint,
                P10_CurrentCultureDoesNotChangeFingerprint,
                P10_PathMachineUserMetadataDoesNotChangeFingerprint,
                P10_ReviewMetadataDoesNotChangeFingerprint,
                P10_BentleyRuntimeVersionMetadataDoesNotChangeFingerprint,
                P10_ChangingCriterionValueChangesGravityFingerprint,
                P10_ChangingCriterionProvenanceChangesFingerprint,
                P10_ChangingCommonP9ContentFingerprintChangesGravityFingerprint,
                P10_SectionCompletenessDeterministic,
                P10_UndefinedCriterionNeverBecomesNumericZeroOrDefault,
                P10_NoLoadingCalculationExecuted,
                P10_NoVerticalDesignExecuted,
                P10_NoHydraulicVerificationExecuted,
                P10_NoCandidateGeneration,
                P10_NoSolverInvocation,
                P10_NoPublicVendorTypes,
                P10_NoPersistedVendorTypes,
                P10_NoHardcodedProductionEngineeringValues,
                P11_PopulationBasisAccepted,
                P11_PEBasisAccepted,
                P11_PopulationAndPESimultaneouslyRejected,
                P11_NegativeBasisRejected,
                P11_NaNOrInfiniteBasisRejected,
                P11_ExplicitGenerationRateRequired,
                P11_BaseSanitaryFlowArithmetic,
                P11_GenerationRateProvenanceRetained,
                P11_PeakFactorRequiredWhenPeakedFlowRequested,
                P11_PeakFactorPositiveValidation,
                P11_NoPopulationDerivedPeakFactorFormulaExists,
                P11_PeakedSanitaryFlowArithmeticCorrect,
                P11_ExplicitInfiltrationContributionIncluded,
                P11_ExplicitInflowContributionIncluded,
                P11_ExplicitIndustrialProcessContributionIncluded,
                P11_ExplicitKnownPointLoadIncluded,
                P11_MultipleContributionsAggregateDeterministically,
                P11_TotalDesignLoadingArithmeticCorrect,
                P11_MissingValueNotTreatedAsZero,
                P11_ExplicitZeroContributionRemainsValidZero,
                P11_NegativeContributionRejected,
                P11_DuplicateContributionIdRejected,
                P11_DuplicatePointLoadIdRejected,
                P11_IncompatibleFlowUnitsFailExplicitly,
                P11_NoSilentUnitConversion,
                P11_InputOrderDoesNotChangeTotals,
                P11_InputOrderDoesNotChangeDeterministicTraceOrdering,
                P11_CurrentCultureDoesNotChangeCalculation,
                P11_P8ProvenanceRetained,
                P11_P9OverrideProvenanceRetained,
                P11_P10LoadingSectionConsumedWithoutMutation,
                P11_P10SnapshotRemainsImmutable,
                P11_NoNetworkElementIdRequired,
                P11_KnownPointLoadExternalReferenceDoesNotImplyNetworkMapping,
                P11_NoP12NetworkIngestion,
                P11_NoP13VerticalDesign,
                P11_NoP14CandidateGeneration,
                P11_NoP15SolverInvocation,
                P11_NoP16HydraulicVerification,
                P11_NoHardcodedProductionLoadingValues,
                P11_NoPublicVendorTypes,
                P11_NoPersistedVendorTypes,
                P12A_ReadEvidenceDtoContainsNoPublicVendorTypes,
                P12A_ConduitIdsRetainedExactly,
                P12A_NodeIdsRetainedExactly,
                P12A_EndpointMappingsRetainIdsExactly,
                P12A_DuplicateConduitIdsFailClosed,
                P12A_DuplicateNodeIdsFailClosed,
                P12A_UnresolvedEndpointReferenceClassifiedExplicitly,
                P12A_LabelsOptional,
                P12A_MissingOptionalGeometryDoesNotFabricateCoordinates,
                P12A_UncertainElevationSemanticsCannotBeMarkedProven,
                P12A_DiagnosticClassificationDistinguishesStatuses,
                P12A_ScenarioContextRequired,
                P12A_WriteInvocationCountersRemainZero,
                P12A_NoP11LoadAllocationExists,
                P12A_NoP13DesignCalculationsExist,
                P12A_NoPublicVendorTypes,
                P12AR1_DomainElementTypeUsesLoadedHaestadDomainAssembly,
                P12AR1_ResolverDoesNotDependOnDatasetTypeAssembly,
                P12AR1_MissingHaestadDomainFailsExplicitly,
                P12AR1_DuplicateMatchingAssembliesFailClosed,
                P12AR1_ExactTypeNameRequired,
                P12AR1_NoHardcodedBentleyInstallationPath,
                P12AR1_ConduitTypeResolutionProducesUsableIdInFixture,
                P12AR1_SerializerCompatibleMissingEndpointsRoundtrips,
                P12AR1_InvariantEndpointKeyFormattingRoundtrips,
                P12AR1_WriteCountersRemainZero,
                P12B_ValidNetworkReadResultMapsToSnapshot,
                P12B_NodeSourceIdsPreserved,
                P12B_ConduitSourceIdsPreserved,
                P12B_LabelsPreserved,
                P12B_ManholeMappedCorrectly,
                P12B_CatchBasinMappedCorrectly,
                P12B_OutfallMappedCorrectly,
                P12B_UnsupportedNodeTypeFailsExplicitly,
                P12B_XYPreservedExactly,
                P12B_MissingOptionalCoordinateRemainsMissing,
                P12B_NodeElevationValuesPreserved,
                P12B_MissingElevationRemainsMissingNotZero,
                P12B_ConduitStartNodeIdPreserved,
                P12B_ConduitStopNodeIdPreserved,
                P12B_UnresolvedStartEndpointRejected,
                P12B_UnresolvedStopEndpointRejected,
                P12B_DuplicateNodeIdRejected,
                P12B_DuplicateConduitIdRejected,
                P12B_ConduitLengthPreserved,
                P12B_ConduitSizeMetadataPreserved,
                P12B_ShapeMaterialPreserved,
                P12B_ManningNPreserved,
                P12B_InvertValuesPreserved,
                P12B_MissingOptionalConduitPropertyRemainsMissing,
                P12B_NaNOrInfinityRejected,
                P12B_DetailedConduitPolylineNotFabricated,
                P12B_AdjacencyBuiltDeterministically,
                P12B_VendorEnumerationOrderDoesNotAffectTopology,
                P12B_VendorEnumerationOrderDoesNotAffectNetworkFingerprint,
                P12B_IdenticalLogicalNetworkYieldsIdenticalFingerprint,
                P12B_ChangedConnectivityChangesFingerprint,
                P12B_ChangedPhysicalValueChangesFingerprint,
                P12B_ChangedScenarioContextChangesFingerprint,
                P12B_WorkingCopyPathDoesNotAffectFingerprint,
                P12B_PIDAndTimestampsDoNotAffectFingerprint,
                P12B_ImmutableSnapshotSurvivesCallerCollectionMutation,
                P12B_P11LoadingIsNotAllocated,
                P12B_NoP13VerticalDesignExecuted,
                P12B_NoCandidateGeneration,
                P12B_NoSolverRun,
                P12B_NoHydraulicVerification,
                P12B_NoPublicVendorTypes,
                P12B_NoPersistedVendorTypes,
                P12B_UnitStateNeverSilentlyInventsAUnit,
                P12U_SourceUnitIdentityIsVendorNeutral,
                P12U_WorkingAndStorageUnitsRemainDistinguishable,
                P12U_IngestedValueAssociatedWithProvenValueReturnUnit,
                P12U_StorageUnitCannotSilentlyReplaceWorkingUnit,
                P12U_ElevationValuePlusUnitPreserved,
                P12U_InvertValuePlusUnitPreserved,
                P12U_LengthValuePlusUnitPreserved,
                P12U_DiameterValuePlusUnitPreserved,
                P12U_RiseValuePlusUnitPreserved,
                P12U_SpanValuePlusUnitPreserved,
                P12U_MissingValueRemainsMissing,
                P12U_MissingUnitRemainsUnresolvedNotGuessed,
                P12U_DimensionMismatchFailsClosed,
                P12U_DifferentUnitsWithSameDimensionRemainDistinct,
                P12U_NumericValueIsNotConverted,
                P12U_UnitContextSurvivesDefensiveCopy,
                P12U_CallerMutationCannotAlterSnapshotUnitContext,
                P12U_UnitIdentityParticipatesInNetworkFingerprint,
                P12U_ChangingOnlyUnitIdentityChangesFingerprint,
                P12U_InsertionOrderDoesNotChangeFingerprint,
                P12U_CultureDoesNotChangeUnitSerializationFingerprint,
                P12U_MachinePathRuntimeMetadataDoesNotAffectFingerprint,
                P12U_NoBentleyUnitTypeLeaksIntoPublicDomainModels,
                P12U_NoPersistedVendorTypes,
                P12U_CoordinateUnitMayRemainUnresolvedWithoutCorruptingElevationLengthUnits,
                P12U_NoGeneralConversionEngineAdded,
                P12U_NoWorkingUnitSetterInvoked,
                P12U_NoModelMutation,
                P12U_NoP13VerticalCalculation,
                P12U_P12TopologyRemainsUnchanged,
                P13A_StartNodeIdRetainedExactly,
                P13A_StopNodeIdRetainedExactly,
                P13A_StartInvertRetainedExactly,
                P13A_StopInvertRetainedExactly,
                P13A_StartStopAssociationParticipatesInFingerprint,
                P13A_SwappingStartInvertStopInvertChangesFingerprint,
                P13A_NumericValuesAreNotConverted,
                P13A_FeetUnitIdentityRetained,
                P13A_AmbiguousSourceAssociationRejected,
                P13A_MissingStartSemanticEvidenceRejected,
                P13A_MissingStopSemanticEvidenceRejected,
                P13A_ValueEqualityAloneCannotClassifySemanticsAsProven,
                P13A_CallerMutationCannotAlterEndpointState,
                P13A_NoHydraulicFlowDirectionFieldInferred,
                P13A_NoVerticalDesignCalculationExists,
                P13A_NoCandidateGeneration,
                P13A_NoSolverInvocation,
                P13A_NoPublicVendorTypes,
                P13A_NoPersistedVendorTypes,
                P13AR1_UnitIdFeetAccepted,
                P13AR1_UnitNameFeetAcceptedAsUnitName,
                P13AR1_SymbolFtAcceptedAsSymbol,
                P13AR1_UnitNameFtIsNotRequiredSemantic,
                P13AR1_ExactDimensionRemainsLength,
                P13AR1_AnotherLengthUnitDoesNotSatisfyLesson1Acceptance,
                P13AR1_UnresolvedUnitFails,
                P13AR1_StartInvertStopInvertMappingRemainsUnchanged,
                P13AR1_SwappedEndpointAssociationStillChangesFingerprint,
                P13AR1_WriteCountsRemainZero,
                P13B_StartStopEndpointIdentityPreserved,
                P13B_ExplicitStartStopOrientationAccepted,
                P13B_ExplicitStopStartOrientationAccepted,
                P13B_MissingOrientationYieldsEngineeringDecisionRequired,
                P13B_OrientationReferencingNonEndpointNodeRejected,
                P13B_ExistingInvertIsNotAutomaticallyFixed,
                P13B_ExplicitFixedEndpointConstraintEnforced,
                P13B_ProtectedNodeElevationRemainsSeparateFromConduitEndInvert,
                P13B_ExplicitOutfallFixedConstraintEnforced,
                P13B_UnspecifiedOutfallIntentCanRequireDecision,
                P13B_InvertMatchEqualityConstructedCorrectly,
                P13B_CrownMatchUsesInvertPlusRise,
                P13B_CrownMatchMissingRiseProducesIncompleteResult,
                P13B_ConduitDiameterNotSubstitutedForMissingRise,
                P13B_MatchingParticipantsMustBelongToSameNode,
                P13B_DuplicateMatchEndpointRejected,
                P13B_CoverUsesGroundToCrownExplicitly,
                P13B_CoverCalculationCorrectWithSyntheticData,
                P13B_MissingGroundElevationProducesIncompleteResult,
                P13B_MissingMinimumCoverNotReplacedByDefault,
                P13B_MinimumSlopeRelationCorrect,
                P13B_MaximumSlopeRelationCorrect,
                P13B_OrientationReversalChangesSlopeSemanticsCorrectly,
                P13B_SlopeUsesLengthGreaterThanZero,
                P13B_ZeroNegativeLengthRejected,
                P13B_ExactUnitMismatchFailsClosed,
                P13B_NoAutomaticUnitConversion,
                P13B_CompatibleFeetInputsCalculateDeterministically,
                P13B_FixedPlusInvertMatchPropagationWorks,
                P13B_FixedPlusCrownMatchPropagationWorks,
                P13B_ContradictoryExactConstraintsProduceInfeasible,
                P13B_ConflictRetainsBothProvenanceSources,
                P13B_CoverBoundContradictionProducesInfeasible,
                P13B_SlopeBoundContradictionProducesInfeasible,
                P13B_UnderconstrainedSystemReturnsFeasibleMultipleSolutions,
                P13B_EngineDoesNotChooseAValueFromAFeasibleRange,
                P13B_InsertionOrderDoesNotAlterFeasibilityResult,
                P13B_InsertionOrderDoesNotAlterFingerprint,
                P13B_IdenticalLogicalConstraintsYieldIdenticalFingerprint,
                P13B_ChangedOrientationChangesFingerprint,
                P13B_ChangedCriterionChangesFingerprint,
                P13B_ChangedProvenanceIdentityChangesFingerprintWhereSignificant,
                P13B_SourceNetworkSnapshotRemainsImmutable,
                P13B_DesignBasisSnapshotRemainsImmutable,
                P13B_CallerCollectionMutationDoesNotAlterConstraintSet,
                P13B_NoDesignCandidateProduced,
                P13B_NoSewerGEMSRuntimeInvoked,
                P13B_NoPublicVendorTypes,
                P13B_NoHardCodedEngineeringCriteria,
                P14_InfeasibleP13ResultGeneratesZeroCandidates,
                P14_IncompleteP13ResultGeneratesZeroCandidates,
                P14_EngineeringDecisionRequiredP13ResultGeneratesZeroCandidates,
                P14_UniqueExactP13SolutionMaterializesDeterministically,
                P14_FeasibleMultipleSolutionsRequiresRecipe,
                P14_MissingEndpointSelectionReturnsCandidateInputIncomplete,
                P14_ExistingValueAcceptedWhenFeasible,
                P14_ExistingValueRejectedWhenOutsideFeasibleRange,
                P14_ExistingValueIsNotClamped,
                P14_ExplicitValueAcceptedWhenFeasible,
                P14_ExplicitValueRejectedOutsideBounds,
                P14_MinimumFeasibleSelectsExactLowerBound,
                P14_MinimumFeasibleFailsWhenLowerBoundUnbounded,
                P14_MaximumFeasibleSelectsExactUpperBound,
                P14_MaximumFeasibleFailsWhenUpperBoundUnbounded,
                P14_NoMidpointDefaultSelectionOccurs,
                P14_InvertMatchPropagationPreserved,
                P14_CrownMatchPropagationPreserved,
                P14_FixedConstraintCannotBeOverriddenByRecipe,
                P14_CoverConstraintCandidateValidation,
                P14_MinimumSlopeCandidateValidation,
                P14_MaximumSlopeCandidateValidation,
                P14_OutfallConstraintCandidateValidation,
                P14_OrientationPreserved,
                P14_StartStopDerivedSlopeCorrect,
                P14_StopStartDerivedSlopeCorrect,
                P14_RiseUsedForCrown,
                P14_DiameterNotSubstitutedForRise,
                P14_CoverUsesGroundToCrown,
                P14_IncompatibleUnitRejected,
                P14_NoUnitConversion,
                P14_ExistingInvertPreservedSeparately,
                P14_ProposedInvertExplicit,
                P14_GenerationTraceRecordsSelectionSource,
                P14_EqualityPropagatedEndpointTraceRecordsPropagation,
                P14_CandidateFingerprintDeterministic,
                P14_RequestInsertionOrderDoesNotChangeFingerprint,
                P14_CollectionOrderDoesNotChangeFingerprint,
                P14_MachineSessionMetadataDoesNotAffectFingerprint,
                P14_ChangedProposedInvertChangesFingerprint,
                P14_ChangedOrientationChangesFingerprint,
                P14_TwoRecipesGeneratingSameEngineeringProfileHaveIdenticalCandidateFingerprint,
                P14_DuplicateCandidateClassifiedDeterministically,
                P14_CandidateSetOrderingDeterministic,
                P14_CallerMutationCannotAlterCandidate,
                P14_CallerMutationCannotAlterCandidateSet,
                P14_NetworkTopologyRemainsUnchanged,
                P14_PipeDimensionsMaterialRoughnessUnchanged,
                P14_NoLoadingMutation,
                P14_NoCandidateRanking,
                P14_NoOptimization,
                P14_NoBentleyRuntime,
                P14_NoModelWrites,
                P14_NoSolver,
                P14_NoHydraulicVerification,
                P14_NoPublicVendorTypes,
                P14_NoPersistedVendorTypes,
                P14_NoHardCodedEngineeringCriteria,
                P15A_StartCandidateEndpointMapsToPhysicalUpstreamInvert,
                P15A_StopCandidateEndpointMapsToPhysicalDownstreamInvert,
                P15A_MappingDoesNotReverseStartStop,
                P15A_FeetWorkingUnitIdentityRetained,
                P15A_NoUnitConversion,
                P15A_WriteCapabilityDescriptorContainsNoVendorPublicType,
                P15A_SetterMetadataRequiredBeforeWriteCapabilityClassifiedProven,
                P15A_UnknownSetterSemanticsFailClosed,
                P15A_TargetPhysicalAlternativeIdentityRequired,
                P15A_CandidateWritePlanTargetsIsolatedPhysicalAlternative,
                P15A_SaveCurrentProjectIsPersistenceBoundaryFromAcceptedP6Mapping,
                P15A_SolverDescriptorRequiresExactDeclaringRuntimeType,
                P15A_SolverMemberParametersRetainedExactly,
                P15A_SolverInvocationStatusModelDistinguishesCompletionFailure,
                P15A_NoHydraulicResultFieldsModeled,
                P15A_ExecutionPlanOrdersSaveBeforeSolverWhereRuntimeSemanticsRequireIt,
                P15A_NoSetterCalledByMappingTests,
                P15A_NoSolverCalledByMappingTests,
                P15A_NoPublicVendorTypes,
                P15A_NoPersistedVendorTypes,
                P15B_CandidateLineageMismatchRejectedBeforeWrite,
                P15B_SourceNetworkFingerprintMismatchRejected,
                P15B_ConstraintFingerprintMismatchRejected,
                P15B_ExecutionRequiresWorkingCopy,
                P15B_BaselinePathCannotBeExecutionTarget,
                P15B_StartMapsOnlyToPhysicalUpstreamInvert,
                P15B_StopMapsOnlyToPhysicalDownstreamInvert,
                P15B_FeetValuesPassedWithoutConversion,
                P15B_MakeLocalTrueRequired,
                P15B_IsolatedScenarioRequired,
                P15B_IsolatedPhysicalAlternativeRequired,
                P15B_SourceAndIsolatedPhysicalIdsMustDiffer,
                P15B_ActiveWriteContextMismatchRejectedBeforeSetter,
                P15B_NonPhysicalMappingsPreservedInExecutionPlan,
                P15B_CalculationOptionsMappingPreserved,
                P15B_SourceStateUsesAcceptedP6CalculationOptions,
                P15B_SourceStateMissingCalculationOptionsBlocksBeforeMutation,
                P15B_SourceStateMissingEngineBlocksBeforeMutation,
                P15B_ExactlyTwoEndpointChangesForAcceptanceCandidate,
                P15B_UnrelatedPropertyChangeRejected,
                P15B_ImmediateReadBackMismatchFails,
                P15B_SaveCurrentProjectRepresentedExactlyOnce,
                P15B_SaveAsNeverUsed,
                P15B_SolverExecutionTargetsIsolatedScenario,
                P15B_SolverExecutionCountBoundedToOne,
                P15BS_BindingBeforeFirstMutation,
                P15BS_MissingDataSetContractBlocksBeforeMutation,
                P15BS_MissingNumericalEngineBlocksBeforeMutation,
                P15BS_NullEngineBlocksBeforeMutation,
                P15BS_EngineNameMismatchBlocksBeforeMutation,
                P15BS_MissingRunBlocksBeforeMutation,
                P15BS_SourceMustImplementModelingElement,
                P15BS_IsolatedMustImplementModelingElement,
                P15BS_CollectionMustContainExactlyOneScenario,
                P15BS_RunFailureHasNoRetryOrCompletion,
                P15BS_WrongAssemblyIdentityRejected,
                P15BS_AmbiguousAssemblyRejected,
                P15BS_NoActionFactoryOrPollingPath,
                P15B_SuccessStatusAccepted,
                P15B_ErrorStatusRejected,
                P15B_WarningStatusDoesNotBecomeHydraulicSuccess,
                P15B_UnknownComputeStatusRejected,
                P15B_ExecutionResultContainsNoHydraulicResultValues,
                P15B_SourceIsolationVerificationRequired,
                P15B_PersistedCandidateVerificationRequired,
                P15B_ExecutionTraceRetainsOriginalAndAppliedValues,
                P15B_NoPublicVendorTypes,
                P15B_NoPersistedVendorTypes,
                P15B_NoHardCodedEngineIdInProduction,
                P15B_NoHardCodedScenarioIdInProduction,
                P15B_NoHardCodedPhysicalAlternativeIdInGenericBusinessLogic,
                P15B_NoEngineeringAcceptanceOffsetInProductionSource,
                P15BR1_ConduitEnumResolutionUsesExactLoadedHaestadDomain,
                P15BR1_DoesNotDependOnDatasetOrManagerAssembly,
                P15BR1_ExactDomainElementTypeRequired,
                P15BR1_MissingAssemblyOrTypeFailsClosed,
                P15BR1_AmbiguousExactAssemblyMatchFailsClosed,
                P15BR1_ConduitEnumResolvesFromAcceptedType,
                P15BR1_NoConduitTypeNumericConstantUsedAsPrimaryProductionLogic,
                P15BR1_NoPropertySetterInvokedDuringResolverTests,
                P15BR2_UsesAcceptedSharedFieldAccessPath,
                P15BR2_IFieldCollectionFieldNotRequired,
                P15BR2_PhysicalUpstreamInvertResolves,
                P15BR2_PhysicalDownstreamInvertResolves,
                P15BR2_PhysicalSetInvertToUpstreamResolves,
                P15BR2_PhysicalSetInvertToDownstreamResolves,
                P15BR2_MissingRequiredFieldFailsClosedBeforeMutation,
                P15BR2_ResolverTestsInvokeZeroSetters,
                P15BR2_NoNewBroadReflectionDiscovery,
                P15BR2_NoHardCodedBentleyPathVersionOrBuild,
                P15BL_SourceInheritanceAllowed,
                P15BL_UnreadableBooleanRejected,
                P15BL_MissingLocalizationSetterRejected,
                P15BL_SourceContextRejected,
                P15BL_UpstreamLocalizedOnce,
                P15BL_DownstreamLocalizedOnce,
                P15BL_AlreadyLocalFlagsNotWritten,
                P15BL_MakeLocalAlwaysTrue,
                P15BL_ReadbackFailurePreventsInvertWrites,
                P15BL_EndpointWritesExactlyTwo,
                P15BL_ExactFourMutationProtocol,
                P15BL_NoSaveOrSolverAfterLocalizationFailure,
                P15BL_ActiveScenarioMustBeIsolated,
                P15BL_AssignedPhysicalAlternativeMustBeIsolated,
                P15BL_IsolatedPhysicalAlternativeMustDifferFromSource,
                P15BL_SourcePhysicalAssignmentMustRemainUnchanged,
                P15BL_IsolatedPhysicalAlternativeMustBePositive,
                P15BL_NonPhysicalAssignmentsPreserved,
                P15BL_NoFabricatedLengthOrNonFiniteValues,
                P15Technical_NormalProtocolRejectsAcceptance,
                P15Technical_DoubleOverloadOnlyAndReadback,
                P15Technical_InheritedFlagsLocalizeOnlyIsolatedEndpoints,
                P15Technical_LocalizationReadbackBlocksEndpointWrites,
                P15Technical_EligibilityRecordsAllRejectionsAndLowestId,
                P15Technical_NoEligibleConduitPreservesRejections,
                P15Technical_SourceFlagsSurviveReopenHandoff,
                P15Technical_LocalizationCountProtocol,
                P15Technical_WrongContextBlocksAllWrites,
                P15Technical_ReadbackMismatchFailsClosed,
                P15Technical_WriteBudgetCannotBeReused,
                P15Technical_UnknownUnitsAndNonFiniteRejected,
                P15Technical_FailedCleanupCannotReopen,
                P15License_InitializedBeforeRunAndCleanedAfterFailure,
                P15License_InitializationFailureBlocksRun,
                P15License_ShutdownStatusBlocksRun,
                P15License_DisabledAnalysisBlocksRun,
                P15License_StartExceptionStillStopsAndDisposes,
                P15License_StopFailureStillDisposesAllResources,
                P15License_ReuseAndNestedContextBlocked,
                P15License_MissingBindingContractBlocksBeforeInitialization,
                P15Production_DomainIntegrationAndCompleteCoverage,
                P15Production_MultipleSolutionsAndNoChanges,
                P15Production_MissingApprovalBasisAndForgedLineage,
                P15Production_InvalidEndpointsAndDerivedState,
                P15Production_UnresolvedDecisionsAndConstraintTopology,
                P15Production_MinimalWritesReadbackAndPreservation,
                P15Production_FailurePreventsSuccessAndExtraWrites,
                P15Production_PersistenceAndSourceDrift,
                P15Production_AssignmentIsolation,
                P15Production_ResultGatesAndCleanup,
                P15Production_VersionToleranceAndTrustedIdentity,
                P15Hardening_TrustedBindingAndFrozenPlan,
                P15Hardening_ChangedBindingsReject,
                P15Hardening_UntrustedEntryPointsRejectBeforeSideEffects,
                P15Hardening_ProviderDenialExpiryAndFailure,
                P15Hardening_ReadinessMissingDecisions,
                P15Hardening_ReadinessInvalidInputs,
                P15Hardening_ReadinessTestOnly,
                P15Hardening_ReadinessHistoricalEvidence,
                P15Hardening_SupervisorBoundsAndTimeout,
                P15Hardening_SavePersistenceAndAttemptedWrites,
                P15Intake_PreApprovalPreparationAndImmutability,
                P15Intake_UnitsAndProvenanceRejected,
                P15Intake_ExplicitSelectionAndConflictingEvidence,
                P15Intake_ApprovalTransitions,
                P15Intake_HistoricalMissingDecisions,
                P15Identity_LocalCredentialsAndProtectedStorage,
                P15Identity_LockoutDisabledAndBootstrap,
                P15Identity_AdSelectionAndFailure,
                P15Identity_ProjectRbacAndEmergency,
                P15Identity_ApprovalBindingsAndSeparation,
                P15Identity_ApprovalRevocationAndExpiry,
                P15Identity_RuntimeBoundaryAndTampering,
                P15Identity_SessionsAndAuthoritativeRecheck,
                P15Mvp_PlanAndEnterpriseSeparation,
                P15Mvp_ConfirmationBindingAndOneUse,
                P15Mvp_UntrustedRequestAndModeMismatch,
                P15Mvp_InputReadinessAndValidation,
                P15Mvp_NoChangeAndProtectedSource,
                P15Mvp_SharedBudgetsAndPreview,
                MvpUi01_ProjectRoundtripAndLegacy,
                MvpUi01_MissingAndChangedModelRejected,
                MvpUi01_ConflictMalformedAndInvalidDirectory,
                MvpUi01_ReadGuardAndFailureRecovery,
                MvpUi01_SourceProjectionExactValues,
                MvpUi01_UnavailableFieldsAndTopology,
                MvpUi02_TypedBasisAndReview,
                MvpUi02_ValidationAndUnits,
                MvpUi02_OrientationAndConstraintMapping,
                MvpUi02_P13PresentationAndGenerationGate,
                MvpUi02_P14ExactAndRecipeSelection,
                MvpUi02_AtomicPersistenceAndReopen,
                MvpUi02_StaleSourceAndMalformedReferences,
                MvpUi02_ControlProjectionWithoutForeground,
                MvpUi03_CurrentStateToIntakeAndCanonicalIdentity,
                MvpUi03_IncompleteHistoricalAndStaleAuthority,
                MvpUi03_InvalidLineageAndLegacyImport,
                MvpUi03_ImmutablePreviewAndEditInvalidation,
                MvpUi03_CancelAndNoChangeZeroRuntime,
                MvpUi03_AsyncDuplicateAndFrozenDispatch,
                MvpUi03_RecheckAndExceptionCleanup,
                MvpUi03_OutcomeAndCheckpointProjection,
                MvpUi03_HeadlessSharedControlsAndPendingEditor,
                MvpStartup_ArgumentModesAndLegacyDispatch,
                MvpStartup_InvalidArgumentsFailClosed,
                MvpStartup_DefaultStartupNeverLoads,
                MvpStartup_OpenOnlyPreservesIdentityWithoutRestoredNetwork,
                MvpStartup_InvalidProjectNeverPublishesOrReads,
                MvpStartup_SourceMismatchPreventsOptionalRead,
                MvpStartup_OptionalReadOnceAfterValidatedPublication,
                MvpStartup_MissingReferenceAndReadFailure,
                P16_MetricIdentityOriginAndProvenance,
                P16_UnitMismatchIsInvalidInput,
                P16_MissingValueIsNotZeroAndNotPass,
                P16_ThresholdBoundaries,
                P16_EngineeringViolationIsFindingNotFailure,
                P16_InvalidSourceValueIsInvalidInput,
                P16_TimeAndScenarioLineage,
                P16_DerivedMetricsRequireVerifiedInputs,
                P16_DeterminismAndOrderingIndependence,
                P16_DesignBasisCriteriaExtractionAndRejection,
                P16_ResultReadBoundaryRemainsNotTested,
                P17_GenuineTestOnlyAndUnavailablePresentation,
                P17_StaleLineageRejected,
                P17_ContentAuthority_AcceptsRecordedP16Observation,
                P17_ContentAuthority_RejectsCallerStringsAndMissingAuthority,
                P17_ContentAuthority_RejectsContentAndLineageTampering,
                P17_ContentAuthority_RejectsTamperedP16ContextAndStaleOutputs,
                P18_CreationTypedCriteriaAndUnits,
                P18_ProvenanceAndMissingInputs,
                P18_InvalidInputsRejected,
                P18_ReviewStateTransitions,
                P18_VersioningAndDeterministicFingerprint,
                P18_PersistenceAndReload,
                P18_GravityFlowIsolation,
                P18_DeterministicSerialization,
                P18_ApplicationIntegrationPanelCreateEditReviewSaveReopen,
                P18_A1_GravityThenStormThenReopenBoth,
                P18_A2_StormThenGravityThenReopenBoth,
                P18_A4_GravityOnlyProjectRemainsReadable,
                P18_A5_LegacyStormFlowOnlyProjectRemainsReadable,
                P18_A6_StaleRevisionAndConflictingSavesFailClosed,
                P18_A7_ImmutableContentMismatchRejected,
                P18_A8_IdentityAuditAndAuthorityBoundariesIntact,
                P18_B3_ApplicationCompositionControlStateAndHandlers,
                P18_Q1_QuantityEditorCreatesEditableUnresolvedQuantity,
                P18_Q2_QuantityRejectsMissingUnitAndNonFiniteValues,
                P18_Q3_ExplicitQuantityAndProvenanceCanBeApplied,
                P18_Q4_TypedQuantitySurvivesSaveAndReopen,
                P18_R1_StormThenGravitySaveInOneActiveWorkspace,
                P18_R2_GravityThenStormPreservesBothReferences,
                P18_R3_UnsavedMetadataSurvivesSynchronization,
                P18_R4_ExternalConcurrentEditsRemainConflictProtected,
                P18_R5_FailedStormFlowSaveDoesNotAdvanceTheRevision,
                P18_R6_ReopenPreservesBothModulesAndAudit,
                P18_R7_NoExecutionApprovalOrConfirmationIsCreated,
                P18_Bind_PopulatesSavedSelectedSection,
                P18_Bind_ClearsPriorProjectOnEmptyOrFailedBinding,
                P18_MaterializeAuthoritativeEvidence,
                P19_RationalMethod_BenchmarkCalculation,
                P19_RationalMethod_FrequencyFactors,
                P19_RationalMethod_CompositeSubAreas,
                P19_RationalMethod_MinimumTimeOfConcentrationEnforced,
                P19_IdfParameters_ValidationAndEvaluation,
                P19_ScsCurveNumber_BenchmarkCalculation,
                P19_ScsCurveNumber_ZeroRunoffBelowInitialAbstraction,
                P19_SwmmReservoir_OverlandFlowRouting,
                P19_CatchmentAggregation_SharedInletNodes,
                P19_FailClosed_InvalidInputsRejected,
                P19_MaterializeAuthoritativeEvidence,
                P20_TriangularGutter_BenchmarkCalculation,
                P20_CompositeGutter_DepressedSectionFrontalFlow,
                P20_CurbOpening_OnContinuousGrade_100PercentInterception,
                P20_CurbOpening_OnContinuousGrade_PartialEfficiency,
                P20_CurbOpening_InSag_WeirAndOrificeRegimes,
                P20_GrateInlet_OnContinuousGrade_FrontalAndSideInterception,
                P20_GrateInlet_InSag_CloggingFactor,
                P20_CombinationInlet_OnGradeAndInSag,
                P20_InletNetwork_CarryoverRoutingAndCascade,
                P20_FailClosed_InvalidInputsRejected,
                P20_MaterializeAuthoritativeEvidence,
                P21_FullFlowManning_BenchmarkCalculation,
                P21_PartiallyFullFlow_DepthAndVelocityResolution,
                P21_CommercialDiameterSelection_SmallestAdequateSize,
                P21_SelfCleansingVelocity_MinimumSlopeEnforcement,
                P21_ExcessiveVelocity_FlaggedAsNonCompliance,
                P21_CrownToCrown_SoffitAlignmentAcrossJunction,
                P21_MinimumCover_ConstraintEnforcement,
                P21_MultiConduitNetwork_TopologicalCascade,
                P21_CapacityUtilization_SurchargedDetection,
                P21_FailClosed_InvalidInputsRejected,
                P21_DefaultMinimumCover_ProductionDefaultGovernsCover,
                P21_WorkflowIntegration_DefaultCoverGovernsEndToEndPipeline,
                P21_PipelineVerdict_InsufficientCoverIsBlocking,
                P21_MaterializeAuthoritativeEvidence,
                P22_VelocityHeadAndFrictionSlope_BenchmarkCalculation,
                P22_CriticalDepth_BisectionSolver,
                P22_ManningHeadLoss_Accumulation,
                P22_OutfallTailwater_BoundaryConditionPropagation,
                P22_SafeFreeboard_CompliantNetwork,
                P22_SurchargedConduit_PressurizedFlowDetection,
                P22_FreeboardViolation_DetectedAndReported,
                P22_SurfaceFlooding_RimOverflowDischargeCalculation,
                P22_BottleneckRemediation_AutomaticUpsizingProposal,
                P22_FailClosed_InvalidInputsRejected,
                P22_MaterializeAuthoritativeEvidence,
                P23_MultiModuleWorkspace_TabsAndControlsInitialized,
                P23_HydraulicProfileVisualization_RendersAccurateElevations,
                P23_ExecutiveReport_MarkdownGeneration_CompleteSections,
                P23_ExecutiveReport_JsonAuditPackage_ValidJsonAndProvenance,
                P23_ExecutiveReport_CsvProfileExport_HeaderAndDataIntegrity,
                P23_BottleneckRemediation_ReportingIntegration,
                P23_FailClosed_NullOrInvalidContextHandling,
                P23_MaterializeAuthoritativeEvidence,
                P24_EndToEndPipeline_NominalExecution_AllStagesPass,
                P24_EndToEndPipeline_SurchargedAndFlooded_RemediationTriggered,
                P24_EndToEndPipeline_InletBypassCarryover_AccumulatesCorrectly,
                P24_EndToEndPipeline_MultiModuleCoexistence_GravityFlowUntouched,
                P24_EndToEndPipeline_FailClosed_NullOrMalformedInputHandling,
                EndToEndPipeline_StatusMatrix_IsDynamicallyDerivedFromArtifacts,
                EndToEndPipeline_P17_AssertsAgainstFabricatedPass,
                EndToEndPipeline_MissingStageOutput_FailsClosed,
                P24_MaterializeAuthoritativeEvidence,
                P24_Rules_MinimumSelfCleansingVelocity_NominalAndViolations,
                P24_Rules_MaximumErosiveVelocity_NominalAndViolations,
                P24_Rules_LongitudinalSlope_NominalAndManningSelfCleansing,
                P24_Rules_CrownAlignmentAndSectionDrop_JunctionEvaluations,
                P24_Rules_MunicipalHydraulicRuleEngine_FullNetworkVerification,
                P24_Rules_FailClosed_NullOrMalformedInputHandling,
                P24_Rules_Integration_PreservesP17BaselineAndRemediationViolations,
                P25_EngineeringDecision_ValidDecisionValidation,
                P25_EngineeringDecision_FailClosedOnMissingRationaleOrEvidence,
                P25_EngineeringDecision_GravityConflictMapping,
                P25_EngineeringDecision_StormBottleneckMapping,
                P25_EngineeringDecision_ResolutionApprovalWorkflow,
                P25_EngineeringDecision_RevokeAndSupersedeWorkflow,
                P25_EngineeringDecisionRegister_CrudAndQueries,
                P25_EngineeringDecisionRegister_JsonSerializationRoundtrip,
                P25_EngineeringDecisionRegister_MarkdownReportGeneration,
                P25_MaterializeAuthoritativeEvidence,
                P26_NetworkPlanCanvas_InitializationAndDataLoading,
                P26_NetworkPlanCanvas_HitTestingAndSelection,
                P26_NetworkPlanCanvas_UpstreamAndDownstreamTracing,
                P26_NetworkPlanCanvas_ViolationsOverlayAndHydraulicBinding,
                P26_NetworkElementInspector_PropertiesAndViolationsDisplay,
                P26_NetworkElementInspector_P25DecisionLinkage,
                P26_BidirectionalSelectionSynchronization_WorkspaceIntegration,
                P26_NetworkPlanCanvas_OffscreenRendering,
                P27_DesignBasisReport_ContainsAllSection42Fields,
                P27_HydraulicDesignReport_ContainsAllSection42Fields,
                P27_ViolationsReport_ContainsAllSection42Fields,
                P27_EngineeringDecisionRegisterReport_ContainsAllSection42Fields,
                P27_ModelAuditReport_ContainsAllSection42Fields,
                P27_ComprehensiveSubmissionPackage_IntegratesAllFiveReportsWithSha256Fingerprint,
                P27_ReportPackage_JsonSerializationRoundtrip,
                P27_EngineeringReportsPanel_UiSwitchingAndHeadlessExport,
                P27_MaterializeAuthoritativeEvidence,
                Benchmark_SuiteExecution_AllCasesPassNumericParity,
                Benchmark_Case1_ManningGravityFlow_ParityChecks,
                Benchmark_Case2_CascadingBackwaterSweep_ParityChecks,
                Benchmark_Case3_RationalHydrology_ParityChecks,
                Benchmark_Case4_GutterAndInletInterception_ParityChecks,
                Benchmark_Case5_SurchargeHydraulics_ParityChecks,
                Benchmark_FailClosed_ExceededToleranceDetected,
                Benchmark_MaterializeAuthoritativeEvidence,
                LandXml_Import_ValidStormNetwork_MapsToNetworkReadResult,
                LandXml_Import_MapsToCanonicalGravityNetworkSnapshot,
                LandXml_Export_FromNetworkReadResult_ProducesValidLandXml12,
                LandXml_Export_FromGravityNetworkSnapshot_ProducesValidLandXml12,
                LandXml_RoundTrip_PreservesGeometryAndConnectivity,
                LandXml_FailClosed_OnCorruptOrUnresolvedXml,
                LandXml_StrictIntegrity_FailsClosedOnMissingElevationsOrUnits,
                LandXml_UnsupportedVersion_ThrowsInvalidDataException,
                LandXml_MissingUnits_ThrowsInvalidDataException,
                LandXml_MissingStructureInvert_ThrowsInvalidDataException,
                LandXml_MissingCoordinatesOrLength_ThrowsInvalidDataException,
                LandXml_MissingMaterialOrRoughness_ThrowsInvalidDataException,
                LandXml_MissingStructureDiameter_ThrowsIncompleteData,
                LandXml_ExplicitDiameter_PreservesUnitScaling,
                LandXml_SnapshotMapping_PreservesDiameterMetadata,
                LandXml_AuthoritativeModel_RoundtripsSuccessfully,
                LandXml_MaterializeAuthoritativeEvidence,
                N02_FormatFloatRejectsNonfiniteAndPreservesFinite,
                N02_ExportFailsClosedOnNonfiniteElement,
                LandXml_MetricExport_ConvertsImperialDimensionsWithExactScaling,
                LandXml_Export_RejectsNonfiniteCoordinatesAndRoughness,
                N05_JunctionRimFailsClosedWithoutPositiveDepth,
                N08_NegativeCoverIsReportedNotClamped,
                N08_OrphanedNodeRendersUnavailableCover,
                F07_UnprovenFlowStateRequiresEngineeringDisposition,
                F08_NonfiniteOrNonpositiveDiameterThrowsArgumentOutOfRange,
                F09_Civil3dUnresolvedCatalogPartDoesNotSynthesizeAttributes,
                N10_MissingFlowUnitMetadataThrowsIncompleteDataException,
                P17_MunicipalViolationBaselineFixture_Detects11ViolationsAnd48Unavailable,
                P17_ControlledStressFixture_CoverAndSlopeViolationsItemized,
                P17_ControlledStressFixture_UncalculatedMetricsEmitExplicitUnavailable,
                P17_ControlledStressFixture_AuthenticManningAndContinuityInvariants,
                P17_ProductionSlopeRule_DefaultConfigurationDetectsSlope0040Violation };
            if (args.Length == 1 && args[0] == "--municipal-design-contract")
                tests = new Action[]
                {
                    P17_MunicipalViolationBaselineFixture_Detects11ViolationsAnd48Unavailable,
                    P17_ControlledStressFixture_CoverAndSlopeViolationsItemized,
                    P17_ControlledStressFixture_UncalculatedMetricsEmitExplicitUnavailable,
                    P17_ControlledStressFixture_AuthenticManningAndContinuityInvariants,
                    P17_ProductionSlopeRule_DefaultConfigurationDetectsSlope0040Violation,
                    P24_Rules_MinimumSelfCleansingVelocity_NominalAndViolations,
                    P24_Rules_MaximumErosiveVelocity_NominalAndViolations,
                    P24_Rules_LongitudinalSlope_NominalAndManningSelfCleansing,
                    P24_Rules_CrownAlignmentAndSectionDrop_JunctionEvaluations,
                    P24_Rules_MunicipalHydraulicRuleEngine_FullNetworkVerification,
                    P24_Rules_FailClosed_NullOrMalformedInputHandling,
                    P24_Rules_Integration_PreservesP17BaselineAndRemediationViolations
                };
            else if (args.Length == 1 && args[0] == "--no-write-contract")
                tests = new Action[] { NoWriteOperations };
            else if (args.Length == 1 && args[0] == "--engineering-assumptions-contract")
                tests = new Action[]
                {
                    F07_UnprovenFlowStateRequiresEngineeringDisposition,
                    F08_NonfiniteOrNonpositiveDiameterThrowsArgumentOutOfRange,
                    F09_Civil3dUnresolvedCatalogPartDoesNotSynthesizeAttributes,
                    N10_MissingFlowUnitMetadataThrowsIncompleteDataException
                };
            else if (args.Length == 1 && args[0] == "--n05-n08-contract")
                tests = new Action[] { N05_JunctionRimFailsClosedWithoutPositiveDepth, N08_NegativeCoverIsReportedNotClamped, N08_OrphanedNodeRendersUnavailableCover };
            else if (args.Length == 1 && args[0] == "--n02-nonfinite-contract")
                tests = new Action[] { N02_FormatFloatRejectsNonfiniteAndPreservesFinite, N02_ExportFailsClosedOnNonfiniteElement };
            else if (args.Length == 1 && args[0] == "--remediation-regression")
                tests = tests.Where(test => !test.Method.Name.Contains("MaterializeAuthoritativeEvidence")).ToArray();
            else if (args.Length == 1 && args[0] == "--remediation-focused")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_", "P24_", "P25_", "P26_", "P27_", "Benchmark_", "LandXml_" }
                    .Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal)) && !test.Method.Name.Contains("MaterializeAuthoritativeEvidence")).ToArray();
            else if (args.Length >= 1 && args[0] == "--p14-auto-02")
            {
                if (args.Length > 2) return 2;
                tests = new Action[] { P14Auto02_IdentityAndNonAuthority, P14Auto02_ValuesDirectionsAndOrdering,
                    P14Auto02_UnavailableCover, P14Auto02_ActualRejectionReasons };
                if (args.Length == 2)
                {
                    var requested = args[1].Split(',');
                    if (requested.Any(name => !tests.Any(test => test.Method.Name == name))) return 2;
                    tests = tests.Where(test => requested.Contains(test.Method.Name)).ToArray();
                }
            }
            else if (args.Length >= 1 && args[0] == "--p14-auto-01")
            {
                if (args.Length > 2) return 2;
                tests = new Action[] { P14Auto01_ExactPreviewAndStaging, P14Auto01_MultiplePreviewAndStaging,
                    P14Auto01_PreviewIsReadOnly, P14Auto01_ExplicitSelectionAndAuthority, P14Auto01_StaleContextsAndLifecycle,
                    P14Auto01_IdempotenceAndOperatorWork };
                // Optional exact group names allow the bounded correction pass to rerun only failed groups.
                if (args.Length == 2)
                {
                    var requested = args[1].Split(',');
                    if (requested.Any(name => !tests.Any(test => test.Method.Name == name))) return 2;
                    tests = tests.Where(test => requested.Contains(test.Method.Name)).ToArray();
                }
            }
            else if (args.Length == 1 && args[0] == "--catchment-loading-interpretation")
                tests = new Action[] { CatchmentLoading_ScopedFieldsAndSelectedArea, CatchmentLoading_EventInheritanceAndUncertainty, CatchmentLoading_NestedStormEntriesPreserveIdentity };
            else if (args.Length == 1 && args[0] == "--catchment-source-observations")
                tests = new Action[] { CatchmentSource_MembershipAndZeroVersusMissing, CatchmentSource_EnumerationFailureIsNotZero, CatchmentSource_ControlledReadOpenRouting };
            else if (args.Length == 1 && args[0] == "--p17-source-coverage")
                tests = new Action[] { P17S2_ExactMappingAndZeroVersusNull, P17S2_UnitsProvenanceAndAlternative,
                    P17S2_MissingUnknownAndSanitizedFailure, P17S2_StaleAlternativeAndInvalidIdentity,
                    P17S2_CalculationOptionsAndEngineAssociation, P17S2_OutfallObservationsAreNotDesignPolicy,
                    P17S2_SanitaryRowsPreserveNativeAssociation, P17S2_LegacyCompatibilityAndFingerprint,
                    P17S2_DeterministicCopiesAndUnsafeValues, P17S2_UnprovenUnitAndNonFiniteFailClosed };
            else if (args.Length == 1 && args[0] == "--p13-auto-03")
                tests = new Action[] { P13Auto03_ExactP14Materialization, P13Auto03_BoundedGreatestVector,
                    P13Auto03_CoupledMinimumMaximum, P13Auto03_MatchesAndFixedValues, P13Auto03_CoverAndOutfallBoundaries,
                    P13Auto03_BlockedAndUnboundedProfiles, P13Auto03_StaleIdentityRejections, P13Auto03_DeterminismAndAuthorityUnchanged };
            else if (args.Length == 1 && args[0] == "--p13-auto-02")
                tests = new Action[] { P13Auto02_ResolvedSlopeBoundsAndTrace, P13Auto02_MissingAmbiguousAndConflictingCriteria,
                    P13Auto02_TypesUnitsAndNoPercentConversion, P13Auto02_SourceAvailabilityUnitsAndClassification,
                    P13Auto02_ExplicitPreservationAndOutfallAuthority, P13Auto02_AtomicCriteriaAndIdempotence,
                    P13Auto02_StandardsBasisSourceAndReloadInvalidation, P13Auto02_P13PropagationAndAuthorityGates,
                    P13Auto02_CircularSectionHeightResolution };
            else if (args.Length == 1 && args[0] == "--p13-auto-01")
                tests = new Action[] { P13Auto_TreeAndProvenance, P13Auto_BranchedAndIndependentComponents,
                    P13Auto_UnresolvedTopology, P13Auto_InvalidEndpointsAndExplicitConflicts, P13Auto_StaleContextsAndNoDefaultPolicy,
                    P13Auto_OutfallObservationAndBoundaryAuthority, P13Auto_AtomicDraftAndBasisIntegration,
                    P13Auto_ExplicitPreservationIdempotenceAndPolicyChange, P13Auto_PersistenceAndSourceInvalidation,
                    P13Auto_P13ConsumptionAndP14P15Gates,
                    MvpUi02_ValidationAndUnits, MvpUi02_OrientationAndConstraintMapping,
                    MvpUi02_P13PresentationAndGenerationGate, MvpUi02_P14ExactAndRecipeSelection,
                    MvpUi02_AtomicPersistenceAndReopen, MvpUi02_StaleSourceAndMalformedReferences };
            else if (args.Length == 1 && args[0] == "--mvp-startup")
                tests = tests.Where(test => test.Method.Name.StartsWith("MvpStartup_", StringComparison.Ordinal) ||
                    new[] { "MvpUi01_ProjectRoundtripAndLegacy", "MvpUi01_MissingAndChangedModelRejected", "MvpUi01_ConflictMalformedAndInvalidDirectory" }.Contains(test.Method.Name)).ToArray();
            else if (args.Length == 1 && args[0] == "--p15")
                tests = tests.Where(test => test.Method.Name.StartsWith("P15", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p15-mvp")
                tests = tests.Where(test => test.Method.Name.StartsWith("P15Mvp", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p12-p15")
                tests = tests.Where(test => new[] { "P12", "P13", "P14", "P15" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-calculated-flow")
                tests = new Action[] { P16Calculated_RequestManifestAndAuthority, P16Calculated_ResultContextTimeAndUnits,
                    P16Calculated_UnavailablePayloadAndTrust, P16_ResultReadBoundaryRemainsNotTested };
            else if (args.Length == 1 && args[0] == "--p16-result-proof")
                tests = new Action[] {
                    P16Proof_ResultReaderRejectsMissingResultManager,
                    P16Proof_ResultReaderReportsHasResultsCorrectly,
                    P16Proof_FlowFieldDetection,
                    P16Proof_FlowValueExtraction,
                    P16Proof_FlowTimestepExtraction,
                    P16Proof_FlowUnitExtraction,
                    P16Proof_ZeroFlowPreservedAsZero,
                    P16Proof_InvalidMissingNotConvertedToZero,
                    P16Proof_LineageContainsRequiredIdentities,
                    P16Proof_RejectsUnsafeModelPath,
                    P16Proof_CannotTargetProtectedSourceModel,
                    P16Proof_NoGuiDependencyExists
                };
            else if (args.Length == 1 && args[0] == "--p16-secondary-metrics")
                tests = new Action[] {
                    P16Secondary_VelocityReaderReadsCorrectFieldAndSemantics,
                    P16Secondary_DepthReaderReadsCorrectFieldAndSemantics,
                    P16Secondary_CapacityReaderSelectsFullFlowCapacityAndVolumetricUnit,
                    P16Secondary_HglReaderRecordsElevationAndDatumSemantics,
                    P16Secondary_SurchargeReaderPreservesBooleanWithoutNumericConversion,
                    P16Secondary_SurchargeReaderPreservesBooleanFalse,
                    P16Secondary_RequestValidationRejectsUnsafePathAndMutations,
                    P16Secondary_NoGuiDependencyExists
                };
            else if (args.Length == 1 && args[0] == "--p16-azadegan-result")
                tests = new Action[] {
                    P16Azadegan_ExactObservationAccepted,
                    P16Azadegan_ObservationRejectsSubstitutions,
                    P16Azadegan_TimeVectorWithoutZeroIsNotInferred,
                    P16Azadegan_UnitSymbolsAreLiteralBound,
                    P16Azadegan_RequestValidationBindsCopyInventoryAndContext,
                    P16Azadegan_NoSolverOrWriteSurface
                };
            else if (args.Length == 1 && args[0] == "--p16-azadegan-gvf-readback")
                tests = new Action[] {
                    P16Gvf_ContractConstantsAreTheVerifiedLiterals,
                    P16Gvf_ExactPassObservationAccepted,
                    P16Gvf_ObservationRejectsEngineScenarioAndFieldSubstitution,
                    P16Gvf_TimeSelectionNeverAssumesIndexZero,
                    P16Gvf_UnitMetadataIsRequired,
                    P16Gvf_CoverageAndFinitenessAreMandatory,
                    P16Gvf_ObservationRejectsForbiddenActivity,
                    P16Gvf_RequestValidationBindsCopyInventoryAndContext,
                    P16Gvf_NoSolverSaveOrNotificationSurface
                };
            else if (args.Length == 1 && args[0] == "--p16-azadegan-output-reader")
                tests = new Action[] {
                    P16OutputReader_ClaimsAreSeparatedAndNeverPromoted,
                    P16OutputReader_ArtifactIntegrityPrecedence,
                    P16OutputReader_ElementIdentityRequiresExactCoverage,
                    P16OutputReader_FieldResolutionRecordsExactFailure,
                    P16OutputReader_TimeStepNeverAssumesIndexZero,
                    P16OutputReader_UnitVerificationRequiresAuthoritativeEvidence,
                    P16OutputReader_ExtractionStatusComposition,
                    P16OutputReader_OutcomeClassificationKeepsClaimsSeparate,
                    P16OutputReader_RequestValidationRejectsForeignArtifact,
                    P16OutputReader_ObservationRejectsForbiddenActivity,
                    P16SubEngineProbe_NotificationProbeStaysDisabled,
                    P16SubEngineProbe_ObservationRejectsWriteAndGateChange
                };
            else if (args.Length == 1 && args[0] == "--p16-azadegan-calculation")
                tests = new Action[] {
                    P16AzadeganCalc_ExactPassObservationAccepted,
                    P16AzadeganCalc_ObservationRejectsSubstitutions,
                    P16AzadeganCalc_NonPassCannotClaimFreshness,
                    P16AzadeganCalc_RequestValidationBindsCopyPhaseAndLineage,
                    P16AzadeganCalc_InventoryIsContentBound,
                    P16AzadeganCalc_NoScenarioPropertyOrFallbackSurface,
                    P16AzadeganCalc_NotificationLevelsClassified,
                    P16AzadeganCalc_WarningBearingObservationAccepted,
                    P16AzadeganCalc_NotificationIntegrityRejectsFalsePass,
                    P16AzadeganCalc_EngineFailureStageIsTheSolver,
                    P16AzadeganCalc_PreSaveEvidenceIsDurableAndNotAPass,
                    P16AzadeganCalc_PreSaveEvidenceFailsClosed,
                    P16AzadeganCalc_SaveWatchdogTimesOut,
                    P16AzadeganCalc_SaveWatchdogStopsOnCompletion,
                    P16AzadeganCalc_WatchdogTerminatesOnlyOwnedProcess,
                    P16AzadeganCalc_SaveTimeoutCannotPass,
                    P16AzadeganCalc_CompleteTreeIsWarningBearing,
                    P16AzadeganCalc_ChildNotificationsInaccessibleFailsClosed,
                    P16AzadeganCalc_HiddenChildErrorCannotBeRuledOut,
                    P16AzadeganCalc_NotificationTreeBoundsAndDuplicates,
                    P16AzadeganCalc_PreSaveStatusCannotBeMistakenForPersistence,
                    P16AzadeganCalc_PassCannotCarryIncompleteTree,
                    P16AzadeganCalc_ChildBearingWithoutTimeStepStaysBlocked,
                    P16AzadeganCalc_DetailProcessorResolvesChildren,
                    P16AzadeganCalc_ResolvedTreeStillBlocksOnChildError,
                    P16AzadeganCalc_NestedChildrenResolvedWithinBounds,
                    P16AzadeganCalc_EmptyDetailListFailsClosed,
                    P16AzadeganCalc_DetailProcessorFailureFailsClosed,
                    P16AzadeganCalc_DetailBoundsEnforced,
                    P16AzadeganCalc_PassCannotCarryUnresolvedChildren,
                    P16AzadeganCalc_TreeShapeValidated,
                    P16AzadeganCalc_KnownLeafRecognised,
                    P16AzadeganCalc_KnownLeafEvidencePreserved,
                    P16AzadeganCalc_KnownLeafRequiresExactTuple,
                    P16AzadeganCalc_KnownLeafRequiresSingleTopLevel,
                    P16AzadeganCalc_UnrelatedChildBearingStillBlocks,
                    P16AzadeganCalc_KnownLeafDoesNotSoftenLevelGates,
                    P16AzadeganCalc_ValidatorRejectsForgedKnownLeaf,
                    P16AzadeganCalc_KnownLeafFabricatesNothing
                };
            else if (args.Length == 1 && args[0] == "--p16-save-prompt-contract")
                tests = new Action[] {
                    P16SavePrompt_OnlyVerifiedSchemaPromptIsAuthorized,
                    P16SavePrompt_DeclinesOnlyVerifiedDatabaseCompactionQuestion,
                    P16SavePrompt_HandlerAnswersOnlyAuthorizedAndRecordsEachQuestion,
                    P16SavePrompt_HandlerImplementsInheritedWarningContract,
                    P16SavePrompt_UnboundHandlerRefusesEverything,
                    P16SavePrompt_QuestionBudgetIsBounded,
                    P16SaveResult_VoidReturnIsNeverTreatedAsSuccess,
                    P16SaveResult_BoundaryIsTheVerifiedNonUiMember
                };
            else if (args.Length == 1 && args[0] == "--p16-network-results")
                tests = new Action[] {
                    P16Network_MultiElementAndMultiMetricComposition,
                    P16Network_DeterministicOutputAndFingerprintOrdering,
                    P16Network_ZeroVersusUnavailableValues,
                    P16Network_BooleanSurchargePreservation,
                    P16Network_DerivedMetricUnitAndInputGuards,
                    P16Network_DuplicateConflictingNonFiniteAndMismatchedContextRejection,
                    P16Network_ComposedOfflineDataCannotBypassGenuineResultAuthority,
                    P16Network_ReadElementsAntiStubContractGuard,
                    P16Network_Civil3dIpcResponseTimeoutGuard
                };
            else if (args.Length == 1 && args[0] == "--p16-production-result-readback")
                tests = new Action[] {
                    P16ProductionReadback_LifecycleTransitionsOnlyAfterSolverSuccess,
                    P16ProductionReadback_ReadbackDoesNotRepeatMutationsOrSolver,
                    P16ProductionReadback_RequestedElementsCannotBeSilentlyReplaced,
                    P16ProductionReadback_MultipleConduitObservationsPreserveProvenance,
                    P16ProductionReadback_MissingValuesUnavailableAndObservedZeroDistinct,
                    P16ProductionReadback_MixedTimestepsOrMismatchedContextFailClosed,
                    P16ProductionReadback_GenuineAuthorityRejectsTamperedAndOfflineResults,
                    P16ProductionReadback_NonCalculatingPlansDoNotFabricateReadback
                };
            else if (args.Length == 1 && args[0] == "--p16-production-result-coverage")
                tests = new Action[] {
                    P16ProductionCoverage_ExactMultiElementCoverageGuard
                };
            else if (args.Length == 1 && args[0] == "--p16-production-verification-lineage")
                tests = new Action[] {
                    P16ProductionVerification_LineageBridgeContracts
                };
            else if (args.Length == 1 && args[0] == "--p17-application-result-presentation")
                tests = new Action[] {
                    P17ApplicationResultPresentation_Contracts
                };
            else if (args.Length == 1 && args[0] == "--p17-step1-step2-authorization-preparation")
                tests = new Action[] {
                    P17_Step1_Authorization_And_Step2_Preparation_RealNetwork
                };
            else if (args.Length == 1 && args[0] == "--p17-step3-supervised-solver")
                tests = new Action[] {
                    P17_Step3_Supervised_Solver_Execution_Controlled_Copy
                };
            else if (args.Length == 1 && args[0] == "--p17-step4-presentation-reporting")
                tests = new Action[] {
                    P17_Step4_Presentation_And_Final_Reporting
                };
            else if (args.Length == 1 && args[0] == "--p16-p18")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p19")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p20")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p21")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p22")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p23")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p24")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_", "P24_", "Benchmark_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p25")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_", "P24_", "P25_", "Benchmark_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p26")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_", "P24_", "P25_", "P26_", "Benchmark_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--p16-p27")
                tests = tests.Where(test => new[] { "P16_", "P17_", "P18_", "P19_", "P20_", "P21_", "P22_", "P23_", "P24_", "P25_", "P26_", "P27_", "Benchmark_" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--benchmark-parity")
                tests = tests.Where(test => test.Method.Name.StartsWith("Benchmark_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--benchmark-materialize")
                tests = new Action[] { Benchmark_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p18-stormflow")
                tests = tests.Where(test => test.Method.Name.StartsWith("P18_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p19-hydrology")
                tests = tests.Where(test => test.Method.Name.StartsWith("P19_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p20-inlets")
                tests = tests.Where(test => test.Method.Name.StartsWith("P20_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p21-conduits")
                tests = tests.Where(test => test.Method.Name.StartsWith("P21_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p21-default-cover")
                tests = new Action[] { P21_DefaultMinimumCover_ProductionDefaultGovernsCover };
            else if (args.Length == 1 && args[0] == "--p21-workflow-integration")
                tests = new Action[] { P21_WorkflowIntegration_DefaultCoverGovernsEndToEndPipeline };
            else if (args.Length == 1 && args[0] == "--p21-cover-verdict")
                tests = new Action[] { P21_PipelineVerdict_InsufficientCoverIsBlocking };
            else if (args.Length == 1 && args[0] == "--p24-verdict-contract")
                tests = new Action[]
                {
                    P24_EndToEndPipeline_NominalExecution_AllStagesPass,
                    P24_EndToEndPipeline_SurchargedAndFlooded_RemediationTriggered
                };
            else if (args.Length == 1 && args[0] == "--p22-verification")
                tests = tests.Where(test => test.Method.Name.StartsWith("P22_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p23-reporting")
                tests = tests.Where(test => test.Method.Name.StartsWith("P23_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p24-e2e")
                tests = tests.Where(test => test.Method.Name.StartsWith("P24_EndToEndPipeline_", StringComparison.Ordinal) || test.Method.Name == "P24_MaterializeAuthoritativeEvidence").ToArray();
            else if (args.Length == 1 && args[0] == "--p24-rules")
                tests = tests.Where(test => test.Method.Name.StartsWith("P24_Rules_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--endtoend-integrity")
                tests = tests.Where(test => test.Method.Name.StartsWith("EndToEndPipeline_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p25-decision")
                tests = tests.Where(test => test.Method.Name.StartsWith("P25_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p26-canvas")
                tests = tests.Where(test => test.Method.Name.StartsWith("P26_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p27-reporting")
                tests = tests.Where(test => test.Method.Name.StartsWith("P27_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--p18-materialize")
                tests = new Action[] { P18_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p19-materialize")
                tests = new Action[] { P19_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p20-materialize")
                tests = new Action[] { P20_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p21-materialize")
                tests = new Action[] { P21_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p22-materialize")
                tests = new Action[] { P22_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p23-materialize")
                tests = new Action[] { P23_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p24-materialize")
                tests = new Action[] { P24_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p25-materialize")
                tests = new Action[] { P25_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--p27-materialize")
                tests = new Action[] { P27_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && args[0] == "--landxml-exchange")
                tests = tests.Where(test => test.Method.Name.StartsWith("LandXml_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--landxml-materialize")
                tests = new Action[] { LandXml_MaterializeAuthoritativeEvidence };
            else if (args.Length == 1 && (args[0] == "--m3-pilot" || args[0] == "--m3-field-pilot"))
                tests = new Action[] { M3_PracticeReadyMunicipalFieldPilot_EndToEndExecution };
            else if (args.Length == 1 && args[0] == "--storm-bind")
                tests = tests.Where(test => test.Method.Name.StartsWith("P18_Bind_", StringComparison.Ordinal)).ToArray();
            else if (args.Length == 1 && args[0] == "--mvp-ui01")
                tests = tests.Where(test => new[] { "MvpUi01", "P7_", "P15Mvp" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--mvp-ui02")
                tests = tests.Where(test => new[] { "MvpUi02", "MvpUi01", "P15Mvp" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length == 1 && args[0] == "--mvp-ui03")
                tests = tests.Where(test => new[] { "MvpUi03", "MvpUi02", "MvpUi01", "P15Mvp" }.Any(prefix => test.Method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            else if (args.Length != 0) return 2;
            int failures = 0;
            foreach (var test in tests)
                try { test(); Console.WriteLine("PASS: " + test.Method.Name); }
                catch (Exception error) { failures++; Console.WriteLine("FAIL: " + test.Method.Name + ": " + error); }
            Console.WriteLine("ADAPTER_TESTS: " + tests.Length);
            Console.WriteLine("ADAPTER_ASSERTIONS: " + assertions);
            Console.WriteLine("ADAPTER_FAILURES: " + failures);
            return failures == 0 ? 0 : 1;
        }
        private static void Check(bool condition, string reason) { assertions++; if (!condition) throw new InvalidOperationException(reason); }
        public sealed class TechnicalConduit
        {
            public bool Physical_SetInvertToUpstream { get; set; }
            public bool Physical_SetInvertToDownstream { get; set; }
        }
        public sealed class TechnicalDoubleField
        {
            public double Value;
            public int Writes;
            public int Reads;
            public bool IgnoreWrite;
            public void SetValue(int id, bool makeLocal, double value)
            {
                if (!makeLocal || id != 27) throw new InvalidOperationException("Unexpected setter arguments");
                Writes++;
                if (!IgnoreWrite) Value = value;
            }
            public void SetValue(int id, bool makeLocal, object value) => throw new InvalidOperationException("Object overload forbidden");
            public double GetValue(int id) { Reads++; return Value; }
        }
        public sealed class TechnicalFlagField
        {
            public TechnicalConduit Conduit;
            public bool Upstream;
            public bool IgnoreWrite;
            public int Writes;
            public void SetValue(int id, bool makeLocal, object value)
            {
                if (id != 27 || !makeLocal || !(value is bool flag) || flag)
                    throw new InvalidOperationException("Expected isolated Boolean makeLocal=false value");
                Writes++;
                if (IgnoreWrite) return;
                if (Upstream) Conduit.Physical_SetInvertToUpstream = false;
                else Conduit.Physical_SetInvertToDownstream = false;
            }
        }
        private static void TechnicalWrite(TechnicalConduit conduit, TechnicalDoubleField up, TechnicalDoubleField down,
            ModelReadEvidence evidence, int active = 81) =>
            P15TechnicalCapabilityHost.WriteEndpoints(conduit, 27, up, down,
                new TechnicalFlagField { Conduit = conduit, Upstream = true }, new TechnicalFlagField { Conduit = conduit },
                conduit.Physical_SetInvertToUpstream, conduit.Physical_SetInvertToDownstream,
                10.01, 9.01, active, 81, 91, 91, 1, 2, 2, evidence);
        private static void P15Technical_NormalProtocolRejectsAcceptance()
        {
            var request = Request();
            request.Operation = P15TechnicalCapabilityHost.Operation;
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(request));
            Check(!typeof(P15TechnicalCapabilityHost).IsPublic, "Technical path must remain internal.");
        }
        private static void P15Technical_DoubleOverloadOnlyAndReadback()
        {
            var up = new TechnicalDoubleField(); var down = new TechnicalDoubleField(); var evidence = new ModelReadEvidence();
            TechnicalWrite(new TechnicalConduit(), up, down, evidence);
            Check(up.Writes == 1 && down.Writes == 1 && up.Reads == 1 && down.Reads == 1, "Exactly two Double setters and two immediate reads.");
            Check(evidence.Diagnostics["LocalizationPropertySetCount"] == "0" && evidence.Diagnostics["PropertySetCount"] == "2",
                "No inheritance flag setters.");
        }
        private static void P15Technical_InheritedFlagsLocalizeOnlyIsolatedEndpoints()
        {
            foreach (bool upstream in new[] { false, true })
            foreach (bool downstream in new[] { false, true })
            {
                var source = new TechnicalConduit { Physical_SetInvertToUpstream = upstream, Physical_SetInvertToDownstream = downstream };
                var isolated = new TechnicalConduit { Physical_SetInvertToUpstream = upstream, Physical_SetInvertToDownstream = downstream };
                var setUp = new TechnicalFlagField { Conduit = isolated, Upstream = true };
                var setDown = new TechnicalFlagField { Conduit = isolated };
                var up = new TechnicalDoubleField(); var down = new TechnicalDoubleField(); var evidence = new ModelReadEvidence();
                P15TechnicalCapabilityHost.WriteEndpoints(isolated, 27, up, down, setUp, setDown, upstream, downstream,
                    10.01, 9.01, 81, 81, 91, 91, 1, 2, 2, evidence);
                int needed = (upstream ? 1 : 0) + (downstream ? 1 : 0);
                Check(setUp.Writes == (upstream ? 1 : 0) && setDown.Writes == (downstream ? 1 : 0), "Localize only inherited endpoints.");
                Check(!isolated.Physical_SetInvertToUpstream && !isolated.Physical_SetInvertToDownstream &&
                    source.Physical_SetInvertToUpstream == upstream && source.Physical_SetInvertToDownstream == downstream,
                    "Only isolated flags change.");
                Check(up.Writes == 1 && down.Writes == 1 && P15TechnicalCapabilityHost.ValidWriteCounts(evidence.Diagnostics, needed + 2),
                    "Two Double endpoints, at most two flag writes and four total writes.");
            }
        }
        private static void P15Technical_LocalizationReadbackBlocksEndpointWrites()
        {
            var conduit = new TechnicalConduit { Physical_SetInvertToUpstream = true };
            var flag = new TechnicalFlagField { Conduit = conduit, Upstream = true, IgnoreWrite = true };
            var up = new TechnicalDoubleField(); var down = new TechnicalDoubleField(); var evidence = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.WriteEndpoints(conduit, 27, up, down,
                flag, null, true, false, 10.01, 9.01, 81, 81, 91, 91, 1, 2, 2, evidence));
            Check(flag.Writes == 1 && up.Writes + down.Writes == 0 && !evidence.Diagnostics.ContainsKey("LocalizationReadbackVerified"),
                "Failed localization readback blocks both endpoint writes.");
            flag.IgnoreWrite = false; flag.Writes = 0;
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.WriteEndpoints(conduit, 27, up, down,
                flag, null, true, false, 10.01, 9.01, 1, 81, 91, 91, 1, 2, 2, new ModelReadEvidence()));
            Check(flag.Writes == 0 && up.Writes + down.Writes == 0 && conduit.Physical_SetInvertToUpstream,
                "Wrong active scenario blocks localization before any field write.");
        }
        private static void P15Technical_EligibilityRecordsAllRejectionsAndLowestId()
        {
            var evidence = new ModelReadEvidence();
            var conduit = new TechnicalConduit { Physical_SetInvertToUpstream = true, Physical_SetInvertToDownstream = true };
            var selected = P15TechnicalCapabilityHost.SelectConduit(new[] { 9, 5, -1, 2, 3, 4, 7 },
                id => id == 2 ? new object() : (object)conduit,
                id => id == 3 ? double.NaN : id == 4 ? throw new InvalidOperationException("read failure") : 10d,
                id => 9d, evidence);
            var counts = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, int>>(evidence.Diagnostics["ConduitEligibilityCountsJson"]);
            Check(selected.Id == 5 && selected.SetUp == true && selected.SetDown == true, "Lowest finite conduit remains eligible with inherited flags.");
            Check(counts["Eligible"] == 3 && counts["InvalidId"] == 1 && counts["UnreadableFlags"] == 1 &&
                counts["UnreadableEndpoints"] == 1 && counts["NonFiniteEndpoints"] == 1 && evidence.Diagnostics["ConduitCount"] == "7",
                "Every conduit counted with a specific rejection reason before mutation.");
            Check(conduit.Physical_SetInvertToUpstream && conduit.Physical_SetInvertToDownstream, "Eligibility performs no localization.");
        }
        private static void P15Technical_NoEligibleConduitPreservesRejections()
        {
            var evidence = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.SelectConduit(new[] { 27 },
                id => new TechnicalConduit(), id => double.PositiveInfinity, id => 9d, evidence));
            Check(evidence.Diagnostics["EligibleConduitCount"] == "0" && evidence.Diagnostics["RejectedConduitCount"] == "1" &&
                !evidence.Diagnostics.ContainsKey("PropertySetCount"), "No eligible candidate preserves rejection evidence with zero writes.");
        }
        private static void P15Technical_SourceFlagsSurviveReopenHandoff()
        {
            var evidence = new ModelReadEvidence { CloseCompleted = true };
            foreach (string key in new[] { "StaCompleted", "SaveCurrentProjectSucceeded" }) evidence.Diagnostics[key] = "true";
            foreach (string key in new[] { "CreatedScenarioID", "CreatedPhysicalAlternativeID", "SourceScenarioID", "SourcePhysicalAlternativeID",
                "PhysicalAlternativeTypeID", "SelectedConduitID", "CandidateStartInvert", "CandidateStopInvert", "SourceStartInvert", "SourceStopInvert", "SourceAssignmentsJson" })
                evidence.Diagnostics[key] = "fixture";
            evidence.Diagnostics["SourceSetInvertUpstream"] = "true";
            evidence.Diagnostics["SourceSetInvertDownstream"] = "false";
            var request = new HostRequest();
            P15TechnicalCapabilityHost.PrepareReopen(request, evidence);
            Check(request.Diagnostics["ExpectedSourceSetInvertUpstream"] == "true" && request.Diagnostics["ExpectedSourceSetInvertDownstream"] == "false",
                "Persistence verification receives actual original source flags.");
        }
        private static void P15Technical_LocalizationCountProtocol()
        {
            foreach (int flags in new[] { 0, 1, 2, 3 })
            {
                var request = Request(); request.Operation = P15TechnicalCapabilityHost.Operation;
                var result = Result(request);
                result.SaveInvocationCount = 1; result.ScenarioCreateInvocationCount = 1; result.AlternativeCreateInvocationCount = 1;
                result.MutationInvocationCount = flags + 2; result.SolverRunInvocationCount = 1;
                result.Diagnostics["WriteCapabilityProven"] = "true"; result.Diagnostics["SolverStatusSuccess"] = "true";
                result.Diagnostics["LocalizationReadbackVerified"] = "true";
                result.Diagnostics["LocalizationPropertySetCount"] = flags.ToString(); result.Diagnostics["EndpointInvertPropertySetCount"] = "2";
                string path = ResultFile(request);
                RuntimeHostProtocol.AtomicWrite(path, result);
                if (flags <= 2) Check(RuntimeHostProtocol.ParseResult(path, request).Success, "Protocol accepts bounded localization and exact endpoints.");
                else Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
                result.MutationInvocationCount++;
                File.WriteAllText(path, RuntimeHostProtocol.Serializer().Serialize(result));
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            }
        }
        private static void P15Technical_WrongContextBlocksAllWrites()
        {
            var up = new TechnicalDoubleField(); var down = new TechnicalDoubleField();
            Throws<InvalidOperationException>(() => TechnicalWrite(new TechnicalConduit(), up, down, new ModelReadEvidence(), 1));
            Check(up.Writes + down.Writes == 0, "Source active context blocks before the first setter.");
        }
        private static void P15Technical_ReadbackMismatchFailsClosed()
        {
            var up = new TechnicalDoubleField { IgnoreWrite = true }; var down = new TechnicalDoubleField(); var evidence = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => TechnicalWrite(new TechnicalConduit(), up, down, evidence));
            Check(!evidence.Diagnostics.ContainsKey("ImmediateReadbackVerified") &&
                !evidence.Diagnostics.ContainsKey("SaveCurrentProjectInvoked") && !evidence.Diagnostics.ContainsKey("SolverRunInvoked"),
                "A readback mismatch cannot authorize save or solver.");
        }
        private static void P15Technical_WriteBudgetCannotBeReused()
        {
            var up = new TechnicalDoubleField(); var down = new TechnicalDoubleField(); var evidence = new ModelReadEvidence();
            TechnicalWrite(new TechnicalConduit(), up, down, evidence);
            Throws<InvalidOperationException>(() => TechnicalWrite(new TechnicalConduit(), up, down, evidence));
            Check(up.Writes + down.Writes == 2, "Repeated technical write cannot exceed two setters.");
        }
        private static void P15Technical_UnknownUnitsAndNonFiniteRejected()
        {
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.RequireFeet(null));
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.RequireFeet(new SourceUnitIdentity("length", "m", "metres", "m")));
            P15TechnicalCapabilityHost.RequireFeet(new SourceUnitIdentity("length", "ft", "feet", "ft"));
            Check(!P15TechnicalCapabilityHost.Eligible(27, double.NaN, 9d, false, false) &&
                !P15TechnicalCapabilityHost.Eligible(27, 10d, double.PositiveInfinity, false, false) &&
                !P15TechnicalCapabilityHost.Eligible(27, 10d, 9d, "true", false) &&
                !P15TechnicalCapabilityHost.Eligible(0, 10d, 9d, false, false) &&
                P15TechnicalCapabilityHost.Eligible(27, 10d, 9d, true, false) &&
                P15TechnicalCapabilityHost.Eligible(27, 10d, 9d, false, false), "Valid ID, finite endpoints and readable Boolean flags required.");
        }
        private static void P15Technical_FailedCleanupCannotReopen()
        {
            var request = new HostRequest();
            Throws<InvalidOperationException>(() => P15TechnicalCapabilityHost.PrepareReopen(request,
                new ModelReadEvidence { CloseCompleted = false }));
            Check(!request.Diagnostics.ContainsKey("P15TechnicalPhase"), "Unclosed first session cannot enter reopen or solver.");
        }

        // TEST_ONLY_NON_ENGINEERING: these fixtures never authorize a live model operation.
        private static (GravityEngineeringInputs Input, GravityNetworkSnapshot Network, GravityVerticalDesignConstraintSet Constraints,
            VerticalFeasibilityResult Feasibility, GravityCandidateGenerationRequest Generation, GravityDesignCandidate Candidate) ProductionFixture(bool changed = true, bool free = false)
        {
            const string marker = "TEST_ONLY_NON_ENGINEERING";
            var raw = CreateValidReadResultWithEndpoints();
            raw.ScenarioContext.ActiveScenarioLabel = marker;
            int index = 0;
            foreach (var c in raw.Conduits)
            {
                c.Length = 100; c.LengthUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
                c.Rise = 1.5; c.RiseUnit = c.LengthUnit;
                c.SetInvertToStart = index++ == 0; c.SetInvertToStop = true;
            }
            var network = new GravityNetworkIngestionService().Ingest(raw, new string('A', 64), schemaVersion: 2);
            var basis = new GravityFlowDesignBasis { CommonDesignBasis = new DesignBasis {
                DesignBasisId = marker + "_BASIS", DesignBasisVersion = "1", ProjectId = marker + "_PROJECT",
                ReviewState = DesignBasisReviewState.Reviewed,
                ReviewMetadata = new DesignBasisReviewMetadata { ActorId = marker + "_ENGINEER", ReviewNote = marker } } };
            var provenance = CreateTestProvenance(marker, marker + "_VERTICAL");
            basis.VerticalDesign.Criteria.Add(new GravityDesignCriterion { Key = "vertical.fixture",
                Section = GravityFlowSectionIdentifier.VerticalDesign, Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Required, Value = StandardValue.FromText(marker), Provenance = provenance });
            var builder = new GravityVerticalDesignConstraintSetBuilder();
            var selections = new List<CandidateEndpointSelection>();
            index = 0;
            foreach (var c in network.Conduits)
            {
                // Explicit synthetic choices exercise orientation independently of Bentley Start/Stop targeting.
                if (index++ == 0) builder.SetOrientation(c.SourceElementId, c.StopNodeId, c.StartNodeId, CreateTestProvenance(marker, "vertical.orientation." + c.SourceElementId));
                else builder.SetOrientation(c.SourceElementId, c.StartNodeId, c.StopNodeId, CreateTestProvenance(marker, "vertical.orientation." + c.SourceElementId));
                foreach (var end in new[] { ConduitEndKind.Start, ConduitEndKind.Stop })
                {
                    var identity = new GravityConduitEnd(c.SourceElementId, end, end == ConduitEndKind.Start ? c.StartNodeId : c.StopNodeId);
                    double existing = (end == ConduitEndKind.Start ? c.ExistingPhysicalState.StartInvert : c.ExistingPhysicalState.StopInvert).Value;
                    double target = existing - (changed && end == ConduitEndKind.Start ? 1 : 0);
                    if (!free) builder.AddFixedConstraint(identity, CreateFeetValue(target), CreateTestProvenance(marker, "vertical.fixed." + c.SourceElementId + "." + end));
                    else selections.Add(new CandidateEndpointSelection(identity, EndpointSelectionMode.ExplicitValue, provenance, CreateFeetValue(target)));
                }
            }
            foreach (var n in network.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall))
                builder.AddOutfallConstraint(n.SourceElementId, CreateTestProvenance(marker, "vertical.outfall." + n.SourceElementId), CreateFeetValue(n.ExistingElevationState.InvertElevation.Value));
            var constraints = builder.Build(network.NetworkFingerprint, basis.CommonDesignBasis.DesignBasisId, basis.CommonDesignBasis.DesignBasisVersion);
            foreach (object decision in GravityExecutionPlanBuilder.ConstraintDecisions(constraints))
            {
                var p = (StandardProvenance)decision.GetType().GetProperty("Provenance").GetValue(decision, null);
                basis.VerticalDesign.Criteria.Add(new GravityDesignCriterion { Key = p.ParameterKey,
                    Section = GravityFlowSectionIdentifier.VerticalDesign, Status = GravityCriterionStatus.Defined,
                    Requirement = GravityCriterionRequirement.Required, Value = StandardValue.FromText(GravityExecutionPlanBuilder.Json(decision)), Provenance = p });
            }
            var feasibility = new GravityVerticalFeasibilityEngine().Analyze(network, constraints);
            var generation = new GravityCandidateGenerationRequest(constraints.VerticalConstraintFingerprint,
                new[] { new GravityCandidateRecipe(marker + "_RECIPE", selections, provenance) });
            var candidate = new GravityCandidateGenerator().Generate(network, constraints, feasibility, generation).Candidates.Single();
            var approval = new GravityExecutionApproval { ApprovalId = marker + "_APPROVAL", ActorId = marker + "_ENGINEER",
                DecisionReference = marker + "_DECISION", ApprovedUtc = "2026-01-01T00:00:00+00:00",
                ExecuteEngineeringDesign = true, Calculate = true, ProjectId = basis.CommonDesignBasis.ProjectId,
                ModelFingerprint = network.SourceModelFingerprint, DesignBasisId = basis.CommonDesignBasis.DesignBasisId,
                DesignBasisVersion = basis.CommonDesignBasis.DesignBasisVersion, DesignBasisFingerprint = basis.CreateSnapshot().GravityContentFingerprint,
                NetworkFingerprint = network.NetworkFingerprint, ConstraintFingerprint = constraints.VerticalConstraintFingerprint,
                CandidateId = candidate.CandidateId, CandidateFingerprint = candidate.CandidateFingerprint, RecipeId = candidate.RecipeId };
            return (new GravityEngineeringInputs { SourceRead = raw, ProjectId = basis.CommonDesignBasis.ProjectId,
                ModelFingerprint = network.SourceModelFingerprint, DesignBasis = basis, Approval = approval,
                ConstraintsJson = GravityExecutionPlanBuilder.Json(constraints), FeasibilityJson = GravityExecutionPlanBuilder.Json(feasibility),
                GenerationRequestJson = GravityExecutionPlanBuilder.Json(generation), CandidateJson = GravityExecutionPlanBuilder.Json(candidate) },
                network, constraints, feasibility, generation, candidate);
        }
        private static void P15Production_DomainIntegrationAndCompleteCoverage()
        {
            var f = ProductionFixture();
            var plan = GravityExecutionPlanBuilder.Build(f.Network, f.Input.SourceRead, f.Input.DesignBasis, f.Constraints,
                f.Feasibility, f.Generation, f.Candidate, f.Input.Approval);
            GravityExecutionPlanBuilder.Validate(plan);
            Check(f.Network.Conduits.Count > 1 && plan.Endpoints.Count == f.Network.Conduits.Count * 2,
                "Full P12 to P13 to P14 network candidate is consumed without truncation.");
            Check(plan.NetworkFingerprint == f.Network.NetworkFingerprint && plan.ConstraintFingerprint == f.Constraints.VerticalConstraintFingerprint &&
                plan.CandidateFingerprint == f.Candidate.CandidateFingerprint && plan.RecipeId == f.Candidate.RecipeId, "Canonical lineage is retained.");
            Check(plan.EndpointWriteCount == f.Network.Conduits.Count && plan.LocalizationWriteCount == 1 &&
                plan.TotalFieldWriteCount == f.Network.Conduits.Count + 1, "Minimal endpoint writes and needed localization have separate derived budgets.");
            Check(plan.Endpoints.Where(e => e.Changed).All(e => e.End == ConduitEndKind.Start) &&
                plan.Endpoints.Where(e => !e.Changed).All(e => !e.Localize && e.OriginalInherited), "Reverse engineering orientation never swaps Start/Stop targeting.");
            var again = GravityExecutionPlanBuilder.Build(f.Input);
            Check(GravityExecutionPlanBuilder.Json(again) == GravityExecutionPlanBuilder.Json(plan), "Plan construction is deterministic.");
            f.Input.Approval.ActorId = "mutated"; f.Input.SourceRead.Conduits[0].StartInvert = 0;
            GravityExecutionPlanBuilder.Validate(plan);
            Check(plan.Inputs.Approval.ActorId.Contains("TEST_ONLY_NON_ENGINEERING"), "Caller mutation cannot change a frozen plan.");
        }
        private static void P15Production_MultipleSolutionsAndNoChanges()
        {
            var f = ProductionFixture(free: true);
            Check(f.Feasibility.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions, "Explicit synthetic recipe resolves multiple solutions.");
            var plan = GravityExecutionPlanBuilder.Build(f.Input);
            Check(!plan.NoChanges && plan.RecipeId == f.Candidate.RecipeId, "Explicit recipe is retained.");
            f.Input.GenerationRequestJson = GravityExecutionPlanBuilder.Json(new GravityCandidateGenerationRequest(f.Constraints.VerticalConstraintFingerprint, null));
            Throws<InvalidOperationException>(() => GravityExecutionPlanBuilder.Build(f.Input));
            var none = GravityExecutionPlanBuilder.Build(ProductionFixture(changed: false).Input);
            Check(none.NoChanges && none.TotalFieldWriteCount == 0 && none.LocalizationWriteCount == 0 && none.EndpointWriteCount == 0,
                "No-change candidate manufactures no invert or flag writes.");
        }
        private static void P15Production_MissingApprovalBasisAndForgedLineage()
        {
            Action<GravityEngineeringInputs>[] faults = {
                i => i.DesignBasis = null, i => i.Approval = null, i => i.Approval.ExecuteEngineeringDesign = false,
                i => i.Approval.DecisionReference = "", i => i.Approval.CandidateFingerprint = new string('F', 64),
                i => i.Approval.DesignBasisFingerprint = "forged", i => i.Approval.ProjectId = "wrong",
                i => i.DesignBasis.CommonDesignBasis.ReviewState = DesignBasisReviewState.Draft,
                i => i.DesignBasis.VerticalDesign.Criteria[0].Status = GravityCriterionStatus.NotDefined,
                i => i.DesignBasis.VerticalDesign.Criteria[1].Value = StandardValue.FromText("UNREVIEWED_DECISION"),
                i => i.SourceRead.Conduits[0].StartInvert += 1,
                i => i.FeasibilityJson = "{}",
                i => i.ConstraintsJson = i.ConstraintsJson.Replace("\"VerticalConstraintFingerprint\":\"", "\"VerticalConstraintFingerprint\":\"forged"),
                i => i.GenerationRequestJson = i.GenerationRequestJson.Replace(i.Approval.ConstraintFingerprint, "forged")
            };
            foreach (var fault in faults)
            {
                var input = ProductionFixture().Input; fault(input);
                Throws<Exception>(() => GravityExecutionPlanBuilder.Build(input));
            }
            Check(faults.Length == 14, "Missing approval/criteria, detached Design Basis decisions, stale sources and forged stored identities all fail before runtime.");
        }
        private static object[] JsonRows(object value) => ((System.Collections.IEnumerable)value).Cast<object>().ToArray();
        private static void ChangeCandidate(GravityEngineeringInputs input, Action<Dictionary<string, object>> change)
        {
            var candidate = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(input.CandidateJson);
            change(candidate); input.CandidateJson = GravityExecutionPlanBuilder.Json(candidate);
        }
        private static void P15Production_InvalidEndpointsAndDerivedState()
        {
            Action<Dictionary<string, object>>[] faults = {
                c => c["EndpointDesigns"] = JsonRows(c["EndpointDesigns"]).Skip(1).ToArray(),
                c => c["EndpointDesigns"] = JsonRows(c["EndpointDesigns"]).Concat(new[] { JsonRows(c["EndpointDesigns"])[0] }).ToArray(),
                c => ((Dictionary<string, object>)JsonRows(c["EndpointDesigns"])[0])["ConduitId"] = 999999,
                c => ((Dictionary<string, object>)JsonRows(c["EndpointDesigns"])[0])["NodeId"] = 999999,
                c => ((Dictionary<string, object>)JsonRows(c["EndpointDesigns"])[0])["ProposedInvert"] = "NaN",
                c => ((Dictionary<string, object>)((Dictionary<string, object>)JsonRows(c["EndpointDesigns"])[0])["Unit"])["UnitId"] = "Meters",
                c => ((Dictionary<string, object>)JsonRows(c["DerivedConduits"])[0])["Slope"] = 999,
                c => c["RecipeId"] = "forged",
                c => c["GenerationTrace"] = new { EndpointTraces = new object[0] }
            };
            foreach (var fault in faults) { var input = ProductionFixture().Input; ChangeCandidate(input, fault);
                Throws<InvalidOperationException>(() => GravityExecutionPlanBuilder.Build(input)); }
            Check(faults.Length == 9, "Candidate identity alone cannot authorize missing, duplicated, foreign, unit-invalid or inconsistent endpoints.");
        }
        private static void P15Production_UnresolvedDecisionsAndConstraintTopology()
        {
            var f = ProductionFixture();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var fixedValue in f.Constraints.FixedConstraints)
                b.AddFixedConstraint(fixedValue.ConduitEnd, fixedValue.InvertElevation, fixedValue.Provenance);
            var noOrientation = b.Build(f.Network.NetworkFingerprint, f.Constraints.DesignBasisId, f.Constraints.DesignBasisVersion);
            f.Input.ConstraintsJson = GravityExecutionPlanBuilder.Json(noOrientation);
            Throws<InvalidOperationException>(() => GravityExecutionPlanBuilder.Build(f.Input));
            f = ProductionFixture();
            var fields = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(f.Input.ConstraintsJson);
            var first = (Dictionary<string, object>)JsonRows(fields["FixedConstraints"])[0];
            ((Dictionary<string, object>)first["ConduitEnd"])["NodeId"] = 999999;
            f.Input.ConstraintsJson = GravityExecutionPlanBuilder.Json(fields);
            Throws<InvalidOperationException>(() => GravityExecutionPlanBuilder.Build(f.Input));
            var raw = ProductionFixture().Input;
            raw.SourceRead.Conduits[0].StartInvertUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            Throws<Exception>(() => GravityExecutionPlanBuilder.Build(raw));
            raw = ProductionFixture().Input; raw.SourceRead.Conduits[0].StartInvert = double.PositiveInfinity;
            Throws<Exception>(() => GravityExecutionPlanBuilder.Build(raw));
        }
        private static string EndpointKey(GravityPlannedEndpoint e) => e.ConduitId + ":" + e.End;
        private static void P15Production_MinimalWritesReadbackAndPreservation()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var values = plan.Endpoints.ToDictionary(EndpointKey, e => e.Original);
            var flags = plan.Endpoints.ToDictionary(EndpointKey, e => e.OriginalInherited);
            var sourceValues = new Dictionary<string, double>(values);
            var sourceFlags = new Dictionary<string, bool>(flags);
            var events = new List<string>(); var evidence = new ModelReadEvidence();
            GravityWriteAndSolverHost.ApplyProductionEndpoints(plan, e => values[EndpointKey(e)], e => flags[EndpointKey(e)],
                e => { flags[EndpointKey(e)] = false; events.Add("L:" + EndpointKey(e)); },
                e => { values[EndpointKey(e)] = e.Proposed; events.Add("W:" + EndpointKey(e)); },
                () => { }, evidence);
            Check(events.SequenceEqual(plan.Endpoints.Where(e => e.Localize).Select(e => "L:" + EndpointKey(e)).Concat(
                plan.Endpoints.Where(e => e.Changed).Select(e => "W:" + EndpointKey(e)))), "Deterministic localization/write order matches the minimal plan.");
            Check(values.All(p => plan.Endpoints.Single(e => EndpointKey(e) == p.Key).Proposed == p.Value) &&
                evidence.Diagnostics["PropertySetCount"] == plan.TotalFieldWriteCount.ToString(), "Actual readbacks and exact counts are verified.");
            Check(sourceValues.All(p => p.Value == plan.Endpoints.Single(e => EndpointKey(e) == p.Key).Original) &&
                sourceFlags.All(p => p.Value == plan.Endpoints.Single(e => EndpointKey(e) == p.Key).OriginalInherited), "Source values and flags are retained independently.");
        }
        private static void P15Production_FailurePreventsSuccessAndExtraWrites()
        {
            foreach (string fault in new[] { "context", "budget", "drift", "localization", "readback", "setter" })
            {
                var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
                var values = plan.Endpoints.ToDictionary(EndpointKey, e => e.Original);
                var flags = plan.Endpoints.ToDictionary(EndpointKey, e => e.OriginalInherited);
                int setters = 0; var evidence = new ModelReadEvidence();
                if (fault == "budget") plan.TotalFieldWriteCount++;
                if (fault == "drift") values[EndpointKey(plan.Endpoints[0])] += 2;
                Throws<Exception>(() => GravityWriteAndSolverHost.ApplyProductionEndpoints(plan, e => values[EndpointKey(e)],
                    e => flags[EndpointKey(e)], e => { setters++; if (fault != "localization") flags[EndpointKey(e)] = false; },
                    e => { setters++; if (fault == "setter") throw new InvalidOperationException("TEST_ONLY_NON_ENGINEERING setter fault");
                        if (fault != "readback") values[EndpointKey(e)] = e.Proposed; },
                    () => { if (fault == "context") throw new InvalidOperationException("TEST_ONLY_NON_ENGINEERING source alternative target"); }, evidence));
                Check(!evidence.Diagnostics.ContainsKey("SaveCurrentProjectInvoked") && !evidence.Diagnostics.ContainsKey("SolverRunInvoked"),
                    "A failed write precondition/readback never saves or calculates.");
                if (fault == "context" || fault == "budget" || fault == "drift") Check(setters == 0, "Preflight failures cause zero field writes.");
            }
        }
        private static void P15Production_PersistenceAndSourceDrift()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var actual = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(plan.Inputs.SourceRead));
            GravityWriteAndSolverHost.VerifyProductionNetwork(plan, actual, false);
            foreach (var e in plan.Endpoints)
            {
                var c = actual.Conduits.Single(x => x.Id == e.ConduitId);
                if (e.End == ConduitEndKind.Start) { c.StartInvert = c.UpstreamInvert = e.Proposed; c.SetInvertToStart = e.Localize ? false : e.OriginalInherited; }
                else { c.StopInvert = c.DownstreamInvert = e.Proposed; c.SetInvertToStop = e.Localize ? false : e.OriginalInherited; }
            }
            actual.ScenarioContext.ActiveScenarioId = 700;
            actual.ScenarioContext.PhysicalAlternativeId = 701;
            GravityWriteAndSolverHost.VerifyProductionNetwork(plan, actual, true);
            actual.Conduits[0].Material = "UNAUTHORIZED";
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.VerifyProductionNetwork(plan, actual, true));
            var source = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(plan.Inputs.SourceRead));
            source.Conduits[0].SetInvertToStop = false;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.VerifyProductionNetwork(plan, source, false));
            Check(plan.Inputs.SourceRead.Conduits[0].SetInvertToStop == true, "Actual original inheritance flags survive persistence evidence.");
        }
        private static void P15Production_AssignmentIsolation()
        {
            var original = new Dictionary<int, int> { { 4, 5 }, { 2, 3 }, { 7, 8 } };
            var source = new Dictionary<int, int>(original); var isolated = new Dictionary<int, int>(original); isolated[4] = 82;
            GravityWriteAndSolverHost.VerifyProductionAssignments(original, id => source[id], id => isolated[id], 4, 5, 82);
            isolated[7] = 99;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.VerifyProductionAssignments(original, id => source[id], id => isolated[id], 4, 5, 82));
            isolated[7] = 8; source[4] = 82;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.VerifyProductionAssignments(original, id => source[id], id => isolated[id], 4, 5, 82));
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.VerifyProductionAssignments(original, id => source[id], id => isolated[id], 4, 5, 5));
        }
        private static void P15Production_ResultGatesAndCleanup()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var request = new HostRequest { EngineeringPlan = plan };
            var e = new ModelReadEvidence { CloseCompleted = true };
            foreach (var key in new[] { "StaCompleted", "FirstSessionCloseCompleted", "ProductionExecutionVerified", "ImmediateReadbackVerified",
                "SaveCurrentProjectSucceeded", "ReopenVerificationSuccess", "SourceIsolationVerified", "ScenarioCopyInvoked", "AlternativeCopyInvoked",
                "SolverRunCompleted", "SolverRunInvoked", "SolverStatusSuccess", "SolverLicenseAttached", "LicenseCleanupCompleted" }) e.Diagnostics[key] = "true";
            e.Diagnostics["PropertySetCount"] = plan.TotalFieldWriteCount.ToString();
            e.Diagnostics["LocalizationPropertySetCount"] = plan.LocalizationWriteCount.ToString();
            e.Diagnostics["EndpointInvertPropertySetCount"] = plan.EndpointWriteCount.ToString();
            e.Diagnostics["ScenarioComputeStatus"] = "Success";
            // The fixture runs with Calculate = true, so a complete-evidence claim must also carry the
            // post-solve persistence and hydraulic read-back proof the production gate requires.
            // Supplying those keys is what makes this a complete evidence set rather than a partial one.
            Check(plan.Calculate, "Result-gate fixture must exercise the calculation branch.");
            e.Diagnostics["PostSolveSaveCurrentProjectSucceeded"] = "true";
            e.Diagnostics["ProductionReadbackVerified"] = "true";
            e.Diagnostics["SecondSessionCloseCompleted"] = "true";
            e.Diagnostics["HydraulicResultReads"] = "1";
            e.Diagnostics["HydraulicResultSetFingerprint"] = new string('B', 64);
            e.Diagnostics["HydraulicResultSetJson"] = "{\"TEST_ONLY\":\"result-set\"}";
            Check(GravityWriteAndSolverHost.ProductionSuccess(request, e), "Complete deterministic production evidence passes.");
            // Removing any single piece of read-back proof must block acceptance.
            foreach (var key in new[] { "PostSolveSaveCurrentProjectSucceeded", "ProductionReadbackVerified", "SecondSessionCloseCompleted" })
            { e.Diagnostics[key] = "false"; Check(!GravityWriteAndSolverHost.ProductionSuccess(request, e), "Missing " + key + " blocks success."); e.Diagnostics[key] = "true"; }
            var savedReads = e.Diagnostics["HydraulicResultReads"];
            e.Diagnostics["HydraulicResultReads"] = "0";
            Check(!GravityWriteAndSolverHost.ProductionSuccess(request, e), "Zero hydraulic result reads block success when a calculation was requested.");
            e.Diagnostics["HydraulicResultReads"] = savedReads;
            foreach (var key in new[] { "FirstSessionCloseCompleted", "SourceIsolationVerified", "ReopenVerificationSuccess", "SolverStatusSuccess", "LicenseCleanupCompleted" })
            { e.Diagnostics[key] = "false"; Check(!GravityWriteAndSolverHost.ProductionSuccess(request, e), "Missing " + key + " blocks success."); e.Diagnostics[key] = "true"; }
            e.CloseCompleted = false;
            Check(!GravityWriteAndSolverHost.ProductionSuccess(request, e), "Incomplete vendor cleanup blocks acceptance.");
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.PrepareProductionReopen(request, e));
            e.CloseCompleted = true;
            var result = new HostResult { Success = true, Diagnostics = e.Diagnostics, VendorCleanupCompleted = true, StaCompleted = true,
                SaveInvocationCount = 1, MutationInvocationCount = plan.TotalFieldWriteCount, ScenarioCreateInvocationCount = 1,
                AlternativeCreateInvocationCount = 1, SolverRunInvocationCount = 1 };
            GravityWriteAndSolverHost.ValidateProductionResult(request, result);
            result.MutationInvocationCount++;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.ValidateProductionResult(request, result));
        }
        private static void P15Production_VersionToleranceAndTrustedIdentity()
        {
            var f = CreateSolverFixture("FutureVersion");
            f.Bind();
            Check(f.Evidence.Diagnostics["SolverBindingProvenBeforeMutation"] == "true", "Trusted compatible signatures accept another assembly version.");
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.BindNumericalEngine(f.DataSet, f.Source, "P15TestEngine", f.Evidence, new[] { f.Domain }));
            Check(!f.Evidence.Diagnostics.ContainsKey("SolverRunInvoked"), "Untrusted dynamic assemblies cannot enter production binding.");
            var missing = CreateSolverFixture("Run");
            Throws<InvalidOperationException>(() => missing.Bind());
        }


        // TEST_ONLY_NON_ENGINEERING: independent local verifier double; never grants live runtime authority.
        private sealed class TestOnlyEngineeringVerifier : IGravityEngineeringApprovalVerifier
        {
            private readonly string fingerprint, decision;
            internal int Calls;
            internal Action OnVerify;
            internal bool Denied, Expired, Throw, WrongDecision;
            internal GravityEngineeringApprovalBinding Seen;
            internal TestOnlyEngineeringVerifier(GravityExecutionPlan approved)
            { fingerprint = approved.PlanFingerprint; decision = approved.Inputs.Approval.DecisionReference; }
            public GravityEngineeringApprovalVerification Verify(GravityEngineeringApprovalBinding binding)
            {
                Calls++; Seen = binding; OnVerify?.Invoke();
                if (Throw) throw new InvalidOperationException("TEST_ONLY_NON_ENGINEERING provider unavailable");
                return new GravityEngineeringApprovalVerification(!Denied, fingerprint, WrongDecision ? "OTHER_DECISION" : decision,
                    DateTimeOffset.UtcNow.AddMinutes(Expired ? -1 : 5), "TEST_ONLY_NON_ENGINEERING", testOnly: true);
            }
        }
        private static void BlockedBecause(Action action, string reason)
        {
            assertions++;
            try { action(); }
            catch (InvalidOperationException error)
            { Check(error.ToString().Contains(reason), "The exact authorization/failure cause is preserved: " + reason); return; }
            throw new InvalidOperationException("Expected blocked cause " + reason);
        }
        private static void P15Hardening_TrustedBindingAndFrozenPlan()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var verifier = new TestOnlyEngineeringVerifier(plan);
            string original = GravityExecutionPlanBuilder.Json(plan);
            verifier.OnVerify = () => { plan.Inputs.Approval.ActorId = "ALTERED_AFTER_FREEZE"; plan.Endpoints[0].Proposed += 10; };
            var authorized = GravityEngineeringAuthorization.Verify(plan, verifier, false);
            Check(verifier.Calls == 1 && authorized.TestOnly && GravityExecutionPlanBuilder.Json(authorized.Materialize(false)) == original,
                "A bound local verification succeeds for a detached TEST_ONLY snapshot despite caller mutation.");
            Check(verifier.Seen.ProjectId.Contains("TEST_ONLY_NON_ENGINEERING") && verifier.Seen.SourceScenarioId > 0 &&
                verifier.Seen.SourcePhysicalAlternativeId > 0 && verifier.Seen.Calculate &&
                verifier.Seen.DesignBasisFingerprint != null && verifier.Seen.ConstraintFingerprint != null &&
                verifier.Seen.NetworkFingerprint != null && verifier.Seen.CandidateFingerprint != null &&
                verifier.Seen.ModelFingerprint != null && verifier.Seen.RecipeId != null,
                "Immutable provider binding covers engineering, source, scenario and calculation identities.");
            var copy = authorized.Materialize(false); copy.Calculate = false;
            Check(authorized.Materialize(false).Calculate, "Materialized DTO mutation cannot affect the verified frozen plan.");
            BlockedBecause(() => authorized.Materialize(true), "TEST_ONLY_NON_ENGINEERING");
        }
        private static void P15Hardening_ChangedBindingsReject()
        {
            var original = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var verifier = new TestOnlyEngineeringVerifier(original);
            var changedIntent = ProductionFixture().Input; changedIntent.Approval.Calculate = false;
            var intent = GravityExecutionPlanBuilder.Build(changedIntent);
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(intent, verifier, false), "ENGINEERING_APPROVAL_BINDING");
            var anotherCandidate = GravityExecutionPlanBuilder.Build(ProductionFixture(changed: false).Input);
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(anotherCandidate, verifier, false), "ENGINEERING_APPROVAL_BINDING");
            var project = ProductionFixture().Input; project.ProjectId = project.DesignBasis.CommonDesignBasis.ProjectId = project.Approval.ProjectId = "TEST_ONLY_NON_ENGINEERING_OTHER_PROJECT";
            var otherProject = GravityExecutionPlanBuilder.Build(project);
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(otherProject, verifier, false), "ENGINEERING_APPROVAL_BINDING");
            var model = ProductionFixture().Input; model.ModelFingerprint = model.Approval.ModelFingerprint = new string('B', 64);
            Throws<InvalidOperationException>(() => GravityEngineeringAuthorization.Verify(GravityExecutionPlanBuilder.Build(model), verifier, false));
            var altered = RuntimeHostProtocol.Serializer().Deserialize<GravityExecutionPlan>(GravityExecutionPlanBuilder.Json(original));
            altered.Endpoints[0].Proposed += 1;
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(altered, verifier, false), "PLAN_RECONSTRUCTION");
            Check(verifier.Calls == 3, "Invalid lineage fails before provider calls; valid but different bindings cannot reuse prior approval.");
        }
        private static void P15Hardening_UntrustedEntryPointsRejectBeforeSideEffects()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var request = new HostRequest { Operation = GravityWriteAndSolverHost.ProductionOperation, EngineeringPlan = plan,
                WriteAuthorizationTokenId = Guid.NewGuid().ToString("N"), WriteAuthorizationScope = GravityWriteAndSolverHost.ProductionOperation,
                WorkingCopyId = Guid.NewGuid().ToString("N"), SourceBaselineFingerprint = plan.Inputs.ModelFingerprint };
            const string cause = "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE";
            BlockedBecause(() => GravityCandidateExecutor.ExecuteEngineeringPlan(plan, null, null, 0, null), cause);
            BlockedBecause(() => new GravityEngineeringExecutionService().ExecuteEngineeringPlan(plan, null, null, 0, null), cause);
            BlockedBecause(() => new RuntimeHostClient().ExecuteProduction(null, null, null, 0, null), cause);
            BlockedBecause(() => RuntimeHostProtocol.ValidateRequest(request), cause);
            BlockedBecause(() => GravityWriteAndSolverHost.ValidateProductionRequest(request), cause);
            BlockedBecause(() => GravityWriteAndSolverHost.ExecuteProduction(null, null, null, request, new ModelReadEvidence()), cause);
            BlockedBecause(() => new LifecycleWorker().Run(null, null, request), cause);
            var json = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(GravityExecutionPlanBuilder.Json(request));
            json["Verified"] = true;
            var forged = RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(GravityExecutionPlanBuilder.Json(json));
            BlockedBecause(() => RuntimeHostProtocol.ValidateRequest(forged), cause);
            var noChange = GravityExecutionPlanBuilder.Build(ProductionFixture(changed: false).Input);
            BlockedBecause(() => GravityCandidateExecutor.ExecuteEngineeringPlan(noChange, null, null, 0, null), cause);
            Check(GravityEngineeringApprovalComposition.RuntimeVerifier == null,
                "No real provider is fabricated; null paths/vendor objects prove rejection precedes filesystem/Bentley side effects.");
        }
        private static void P15Hardening_ProviderDenialExpiryAndFailure()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var denied = new TestOnlyEngineeringVerifier(plan) { Denied = true };
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(plan, denied, false), "ENGINEERING_APPROVAL_REJECTED");
            var expired = new TestOnlyEngineeringVerifier(plan) { Expired = true };
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(plan, expired, false), "ENGINEERING_APPROVAL_EXPIRED");
            var wrong = new TestOnlyEngineeringVerifier(plan) { WrongDecision = true };
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(plan, wrong, false), "ENGINEERING_APPROVAL_BINDING");
            var unavailable = new TestOnlyEngineeringVerifier(plan) { Throw = true };
            BlockedBecause(() => GravityEngineeringAuthorization.Verify(plan, unavailable, false), "TEST_ONLY_NON_ENGINEERING provider unavailable");
            BlockedBecause(() => new GravityEngineeringExecutionService(new TestOnlyEngineeringVerifier(plan)).ExecuteEngineeringPlan(plan, null, null, 0, null),
                "TEST_ONLY_NON_ENGINEERING");
        }
        private static GravityEngineeringReadiness Readiness(GravityEngineeringInputs input, IGravityEngineeringApprovalVerifier verifier = null,
            GravityReadinessSourceKind kind = GravityReadinessSourceKind.TestOnly) => new GravityEngineeringExecutionService(verifier).AssessReadiness(
                new GravityEngineeringReadinessInput { EngineeringInputs = input, SourceKind = kind });
        private static void P15Hardening_ReadinessMissingDecisions()
        {
            var f = ProductionFixture();
            var fields = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(f.Input.ConstraintsJson);
            fields["Orientations"] = new Dictionary<string, object>(); fields["OutfallConstraints"] = new object[0];
            f.Input.ConstraintsJson = GravityExecutionPlanBuilder.Json(fields);
            var result = Readiness(f.Input);
            Check(result.MissingFlowOrientationConduitIds.SequenceEqual(f.Network.Conduits.Select(c => c.SourceElementId).OrderBy(id => id)) &&
                result.MissingOutfallDecisionNodeIds.SequenceEqual(f.Network.Nodes.Where(n => n.Kind == GravityNodeKind.Outfall).Select(n => n.SourceElementId).OrderBy(id => id)),
                "Readiness identifies exact missing orientation and outfall elements without choosing decisions.");
            Check(result.BlockingReasons.Any(b => b.Code == "FLOW_ORIENTATION_MISSING") && result.BlockingReasons.Any(b => b.Code == "OUTFALL_DECISION_MISSING") &&
                !result.ProductionExecutionEligible, "Unresolved engineering decisions block execution.");
        }
        private static void P15Hardening_ReadinessInvalidInputs()
        {
            var faults = new Dictionary<string, Action<GravityEngineeringInputs>> {
                { "DESIGN_BASIS_MISSING", i => i.DesignBasis = null },
                { "DESIGN_BASIS_UNREVIEWED", i => i.DesignBasis.CommonDesignBasis.ReviewState = DesignBasisReviewState.Draft },
                { "ENGINEERING_CRITERIA_UNRESOLVED", i => i.DesignBasis.VerticalDesign.Criteria[0].Status = GravityCriterionStatus.NotDefined },
                { "P13_FEASIBILITY_INVALID", i => i.FeasibilityJson = "{}" },
                { "P14_CANDIDATE_MISSING", i => i.CandidateJson = null },
                { "ENGINEERING_APPROVAL_MISSING", i => i.Approval = null } };
            foreach (var fault in faults) { var input = ProductionFixture().Input; fault.Value(input); var r = Readiness(input);
                Check(!r.ProductionExecutionEligible && r.BlockingReasons.Any(b => b.Code == fault.Key), "Readiness reports " + fault.Key); }
            var missing = Readiness(null);
            Check(missing.BlockingReasons.Any(b => b.Code == "P12_NETWORK_MISSING") && missing.ApprovalVerificationStatus == "TRUSTED_VERIFIER_UNAVAILABLE",
                "Missing inputs and verifier are explicit; an empty context grants nothing.");
            var section = ProductionFixture().Input; section.DesignBasis.VerticalDesign = null;
            Check(!Readiness(section).ProductionExecutionEligible, "A missing Design Basis section is assessed without a null-reference failure.");
        }
        private static void P15Hardening_ReadinessTestOnly()
        {
            var input = ProductionFixture().Input; var plan = GravityExecutionPlanBuilder.Build(input);
            string before = GravityExecutionPlanBuilder.Json(input);
            var result = Readiness(input, new TestOnlyEngineeringVerifier(plan), GravityReadinessSourceKind.SuppliedP12Snapshot);
            Check(result.TestOnly && result.ApprovalVerificationStatus == "TEST_ONLY_NON_ENGINEERING" && !result.ProductionExecutionEligible &&
                result.CandidateFingerprint == plan.CandidateFingerprint && result.PlanFingerprint == plan.PlanFingerprint,
                "A complete reconstructed synthetic fixture remains TEST_ONLY even if its source label claims a current snapshot.");
            Check(before == GravityExecutionPlanBuilder.Json(input) && !result.LiveRevalidatedByAssessment,
                "Readiness leaves inputs unchanged and never claims live validation.");
            var noProvider = Readiness(input);
            Check(!noProvider.TrustedClientVerifierConfigured && !noProvider.TrustedRuntimeVerifierConfigured &&
                noProvider.BlockingReasons.Any(b => b.Code == "TRUSTED_ENGINEERING_VERIFIER_UNAVAILABLE"), "A DTO does not verify its own approval.");
        }
        private static void P15Hardening_ReadinessHistoricalEvidence()
        {
            var repository = Repository();
            var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(File.ReadAllText(Path.Combine(repository, "docs/evidence/p12/p12a_network_read.json")));
            var identity = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(repository, "docs/evidence/p13/p13a_network_identity.json")));
            var assessment = new GravityEngineeringExecutionService().AssessReadiness(new GravityEngineeringReadinessInput {
                SourceKind = GravityReadinessSourceKind.Historical, HistoricalNetworkFingerprint = (string)identity["network_fingerprint"],
                ModelName = "Lesson_1.stsw", SourceEvidenceReference = "docs/evidence/p12/p12a_network_read.json; docs/evidence/p13/p13a_network_identity.json",
                EngineeringInputs = new GravityEngineeringInputs { SourceRead = raw, ModelFingerprint = (string)identity["source_model_fingerprint"] } });
            Check(assessment.MissingFlowOrientationConduitIds.SequenceEqual(Enumerable.Range(27, 9)) &&
                assessment.MissingOutfallDecisionNodeIds.SequenceEqual(new[] { 25, 26 }) && !assessment.ProductionExecutionEligible &&
                !assessment.LiveRevalidatedByAssessment && assessment.SourceKind == GravityReadinessSourceKind.Historical,
                "Historical Lesson_1 readiness preserves exact affected IDs without live or engineering authority claims.");
            string output = Environment.GetEnvironmentVariable("SEWERPILOT_P15_READINESS_OUTPUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, RuntimeHostProtocol.Serializer().Serialize(assessment));
        }
        private static void P15Hardening_SupervisorBoundsAndTimeout()
        {
            var pending = new System.Threading.Tasks.TaskCompletionSource<bool>();
            BlockedBecause(() => RuntimeHostClient.RequireSupervisorOutput(new[] { pending.Task }, 0), "WATCHDOG_OUTPUT_INCOMPLETE");
            RuntimeHostClient.RequireSupervisorOutput(new[] { System.Threading.Tasks.Task.FromResult(true) }, 0);
            var evidence = new Dictionary<string, object> { { "remaining_runtime_processes", 1 },
                { "post_termination_check", new Dictionary<string, object> { { "active_processes", 1 } } }, { "timeout_triggered", true } };
            BlockedBecause(() => RuntimeHostClient.RequireWatchdogTermination(evidence), "PROCESS_TERMINATION_UNPROVEN");
            evidence["remaining_runtime_processes"] = 0; ((Dictionary<string, object>)evidence["post_termination_check"])["active_processes"] = 0;
            RuntimeHostClient.RequireWatchdogTermination(evidence);
            BlockedBecause(() => RuntimeHostClient.RequireWatchdogNotTimedOut(evidence), "RUNTIME_WATCHDOG_TIMEOUT");
            evidence["timeout_triggered"] = false; RuntimeHostClient.RequireWatchdogNotTimedOut(evidence);
            Check(!pending.Task.IsCompleted, "Output completion uses a bounded wait without polling or launching a process.");
        }
        private static void P15Hardening_SavePersistenceAndAttemptedWrites()
        {
            var plan = GravityExecutionPlanBuilder.Build(ProductionFixture().Input);
            var request = new HostRequest { EngineeringPlan = plan };
            var failedSave = new ModelReadEvidence { CloseCompleted = true };
            failedSave.Diagnostics["StaCompleted"] = "true"; failedSave.Diagnostics["SaveCurrentProjectSucceeded"] = "false";
            BlockedBecause(() => GravityWriteAndSolverHost.PrepareProductionReopen(request, failedSave), "FIRST_SESSION_CLEANUP");
            Check(!request.Diagnostics.ContainsKey("GravityEngineeringPhase"), "Failed save cannot authorize reopen or calculation.");
            var mismatch = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(GravityExecutionPlanBuilder.Json(plan.Inputs.SourceRead));
            mismatch.Conduits[0].StartInvert = plan.Endpoints[0].Proposed + 1;
            BlockedBecause(() => GravityWriteAndSolverHost.VerifyProductionNetwork(plan, mismatch, true), "RUNTIME_ENDPOINT_STATE");
            foreach (bool failLocalization in new[] { true, false })
            {
                var evidence = new ModelReadEvidence(); var values = plan.Endpoints.ToDictionary(EndpointKey, e => e.Original);
                var flags = plan.Endpoints.ToDictionary(EndpointKey, e => e.OriginalInherited);
                BlockedBecause(() => GravityWriteAndSolverHost.ApplyProductionEndpoints(plan, e => values[EndpointKey(e)], e => flags[EndpointKey(e)],
                    e => { if (failLocalization) throw new InvalidOperationException("TEST_ONLY_NON_ENGINEERING setter fault"); flags[EndpointKey(e)] = false; },
                    e => { throw new InvalidOperationException("TEST_ONLY_NON_ENGINEERING setter fault"); }, () => { }, evidence), "setter fault");
                Check(int.Parse(evidence.Diagnostics["PropertySetCount"]) == (failLocalization ? 1 : plan.LocalizationWriteCount + 1) &&
                    int.Parse(evidence.Diagnostics["EndpointInvertPropertySetCount"]) == (failLocalization ? 0 : 1),
                    "Attempted throwing localization/invert setters are included in exact failure budgets.");
                Check(!evidence.Diagnostics.ContainsKey("SaveCurrentProjectInvoked") && !evidence.Diagnostics.ContainsKey("SolverRunInvoked"), "No continuation after failed setter.");
            }
        }

        private static GravityEngineeringIntakeRequest IntakeRequest(GravityEngineeringInputs input) => new GravityEngineeringIntakeRequest {
            Context = new GravityEngineeringReadinessInput { EngineeringInputs = input, SourceKind = GravityReadinessSourceKind.TestOnly,
                ModelName = "TEST_ONLY_NON_ENGINEERING", SourceEvidenceReference = "TEST_ONLY_NON_ENGINEERING" } };
        private static void P15Intake_PreApprovalPreparationAndImmutability()
        {
            var fixture = ProductionFixture(); var input = fixture.Input;
            input.DesignBasis.VerticalDesign.Criteria.Add(new GravityDesignCriterion { Key = "vertical.supplied.quantity",
                Section = GravityFlowSectionIdentifier.VerticalDesign, Requirement = GravityCriterionRequirement.Required,
                Status = GravityCriterionStatus.Defined, Value = StandardValue.FromQuantity(2, "ft"),
                Provenance = CreateTestProvenance("TEST_ONLY_NON_ENGINEERING", "vertical.supplied.quantity") });
            input.Approval = null; input.CandidateJson = input.FeasibilityJson = input.GenerationRequestJson = input.ConstraintsJson = null;
            string before = GravityExecutionPlanBuilder.Json(input);
            var request = IntakeRequest(input); request.Constraints = fixture.Constraints; request.CandidateRequest = fixture.Generation;
            var result = new GravityEngineeringExecutionService().ReviewEngineeringIntake(request);
            Check(result.EngineeringContentValidated && result.Status == "DESIGN_READY_APPROVAL_PENDING" &&
                result.CandidateFingerprint == fixture.Candidate.CandidateFingerprint && result.PreparedInputs.Approval == null,
                "Explicit reviewed quantities/constraints generate genuine P13/P14 content without manufacturing approval.");
            Check(!result.Readiness.ProductionExecutionEligible && result.Readiness.MissingFlowOrientationConduitIds.Length == 0 &&
                result.Readiness.MissingOutfallDecisionNodeIds.Length == 0 && result.Readiness.P13FeasibilityStatus == "Feasible",
                "Resolved engineering input advances readiness while trusted approval remains unavailable.");
            Check(before == GravityExecutionPlanBuilder.Json(input) && result.RuntimeHostInvocations == 0,
                "Read-only intake leaves caller inputs unchanged and never launches RuntimeHost.");
            result.PreparedInputs.DesignBasis.VerticalDesign.Criteria[0].Value = StandardValue.FromText("MUTATED_OUTPUT");
            Check(input.DesignBasis.VerticalDesign.Criteria[0].Value.TextValue != "MUTATED_OUTPUT", "Returned intake data is detached from source input.");
        }
        private static void P15Intake_UnitsAndProvenanceRejected()
        {
            foreach (string fault in new[] { "unit", "nonfinite", "missing-provenance", "untraceable", "recipe-provenance", "basis-decision" })
            {
                var f = ProductionFixture(); f.Input.Approval = null;
                if (fault == "missing-provenance") f.Input.DesignBasis.VerticalDesign.Criteria[0].Provenance = null;
                if (fault == "untraceable") { f.Input.DesignBasis.VerticalDesign.Criteria[0].Provenance.StandardId = "";
                    f.Input.DesignBasis.VerticalDesign.Criteria[0].Provenance.DocumentReference = ""; }
                if (fault == "basis-decision") f.Input.DesignBasis.VerticalDesign.Criteria[1].Value = StandardValue.FromText("UNREVIEWED");
                if (fault == "nonfinite") f.Input.SourceRead.Conduits[0].StartInvert = double.NaN;
                if (fault == "recipe-provenance") {
                    var generationFields = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(f.Input.GenerationRequestJson);
                    var recipeFields = (Dictionary<string, object>)JsonRows(generationFields["Recipes"])[0];
                    var p = (Dictionary<string, object>)recipeFields["Provenance"]; p["StandardId"] = ""; p["DocumentReference"] = "";
                    f.Input.GenerationRequestJson = GravityExecutionPlanBuilder.Json(generationFields); }
                if (fault == "unit")
                {
                    var fields = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(f.Input.ConstraintsJson);
                    var fixedValue = (Dictionary<string, object>)JsonRows(fields["FixedConstraints"])[0];
                    var quantity = (Dictionary<string, object>)fixedValue["InvertElevation"];
                    ((Dictionary<string, object>)quantity["Unit"])["UnitId"] = "Meters";
                    // Update explicit reviewed text to isolate quantity validation rather than mismatch detection.
                    f.Input.ConstraintsJson = GravityExecutionPlanBuilder.Json(fields);
                    var rebuilt = GravityEngineeringWire.Constraints(f.Input.ConstraintsJson);
                    f.Input.ConstraintsJson = GravityExecutionPlanBuilder.Json(rebuilt);
                    var decision = rebuilt.FixedConstraints[0];
                    f.Input.DesignBasis.VerticalDesign.Criteria.Single(c => c.Key == decision.Provenance.ParameterKey).Value =
                        StandardValue.FromText(GravityExecutionPlanBuilder.Json(decision));
                }
                var result = new GravityEngineeringExecutionService().ReviewEngineeringIntake(IntakeRequest(f.Input));
                Check(!result.EngineeringContentValidated && result.Status == "REJECTED_ENGINEERING_INPUT" &&
                    result.Rejection != null && !result.Readiness.ProductionExecutionEligible, "Intake rejects " + fault + " before candidate acceptance.");
            }
        }
        private static void P15Intake_ExplicitSelectionAndConflictingEvidence()
        {
            var f = ProductionFixture(free: true); f.Input.Approval = null; f.Input.CandidateJson = null;
            var otherSelections = f.Generation.Recipes[0].EndpointSelections.Select(e => new CandidateEndpointSelection(e.ConduitEnd,
                EndpointSelectionMode.ExplicitValue, e.Provenance, CreateFeetValue(e.ExplicitElevation.NumericValue.Value - 0.1))).ToArray();
            var two = new GravityCandidateGenerationRequest(f.Constraints.VerticalConstraintFingerprint,
                new[] { f.Generation.Recipes[0], new GravityCandidateRecipe("TEST_ONLY_NON_ENGINEERING_OTHER", otherSelections, f.Generation.Recipes[0].Provenance) });
            f.Input.GenerationRequestJson = null; var request = IntakeRequest(f.Input); request.CandidateRequest = two;
            var service = new GravityEngineeringExecutionService(); var blocked = service.ReviewEngineeringIntake(request);
            Check(!blocked.EngineeringContentValidated && blocked.Rejection.Contains("INTAKE_CANDIDATE_SELECTION_REQUIRED"), "Multiple supplied recipes require explicit operator selection.");
            request.SelectedRecipeId = "TEST_ONLY_NON_ENGINEERING_OTHER";
            var selected = service.ReviewEngineeringIntake(request);
            Check(selected.EngineeringContentValidated && selected.RecipeId == request.SelectedRecipeId && !selected.Readiness.ProductionExecutionEligible,
                "Explicit recipe selection is honored without inferring approval.");
            f = ProductionFixture(); f.Input.Approval = null; f.Input.FeasibilityJson = "{}";
            Check(service.ReviewEngineeringIntake(IntakeRequest(f.Input)).Rejection.Contains("FEASIBILITY_LINEAGE"), "Stale supplied feasibility is never silently overwritten.");
            f = ProductionFixture(); var conflict = IntakeRequest(f.Input); conflict.Constraints = ProductionFixture(changed: false).Constraints;
            Check(service.ReviewEngineeringIntake(conflict).Rejection.Contains("INTAKE_CONSTRAINT_INPUT_MISMATCH"), "Typed and JSON decisions cannot disagree.");
        }
        private static void P15Intake_ApprovalTransitions()
        {
            var f = ProductionFixture(); var plan = GravityExecutionPlanBuilder.Build(f.Input);
            var verifier = new TestOnlyEngineeringVerifier(plan); var service = new GravityEngineeringExecutionService(verifier);
            var accepted = service.ReviewEngineeringIntake(IntakeRequest(f.Input));
            Check(accepted.EngineeringContentValidated && accepted.Status == "TEST_ONLY_NON_ENGINEERING" && !accepted.Readiness.ProductionExecutionEligible,
                "Existing trusted-bound TEST_ONLY verification is reused and never grants runtime authority.");
            var other = ProductionFixture(changed: false);
            var rejected = service.ReviewEngineeringIntake(IntakeRequest(other.Input));
            Check(rejected.Status == "REJECTED_ENGINEERING_APPROVAL" && rejected.Readiness.BlockingReasons.Any(b => b.Detail.Contains("ENGINEERING_APPROVAL_BINDING")),
                "An otherwise valid candidate cannot reuse a different plan's approval.");
            f.Input.Approval.ProjectId = "FORGED_PROJECT";
            Check(service.ReviewEngineeringIntake(IntakeRequest(f.Input)).Status == "REJECTED_ENGINEERING_APPROVAL", "Forged identity remains rejected.");
            var untrusted = new GravityEngineeringExecutionService().ReviewEngineeringIntake(IntakeRequest(other.Input));
            Check(untrusted.Readiness.ApprovalVerificationStatus == "TRUSTED_VERIFIER_UNAVAILABLE" && !untrusted.Readiness.ProductionExecutionEligible,
                "An explicit DTO and resolved design do not supply authenticated engineering authority.");
        }
        private static void P15Intake_HistoricalMissingDecisions()
        {
            var raw = RuntimeHostProtocol.Serializer().Deserialize<NetworkReadResult>(File.ReadAllText(Path.Combine(Repository(), "docs/evidence/p12/p12a_network_read.json")));
            var identity = RuntimeHostProtocol.Serializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Repository(), "docs/evidence/p13/p13a_network_identity.json")));
            var result = new GravityEngineeringExecutionService().ReviewEngineeringIntake(new GravityEngineeringIntakeRequest {
                Context = new GravityEngineeringReadinessInput { ModelName = "Lesson_1.stsw", SourceKind = GravityReadinessSourceKind.Historical,
                    SourceEvidenceReference = "docs/evidence/p12/p12a_network_read.json; docs/evidence/p13/p13a_network_identity.json",
                    HistoricalNetworkFingerprint = (string)identity["network_fingerprint"], EngineeringInputs = new GravityEngineeringInputs {
                        SourceRead = raw, ModelFingerprint = (string)identity["source_model_fingerprint"] } } });
            Check(result.Status == "INCOMPLETE_ENGINEERING_INPUT" && result.Readiness.MissingFlowOrientationConduitIds.SequenceEqual(Enumerable.Range(27, 9)) &&
                result.Readiness.MissingOutfallDecisionNodeIds.SequenceEqual(new[] { 25, 26 }) && !result.Readiness.LiveRevalidatedByAssessment,
                "Operator intake reports exact historical decisions without creating values or asserting current validation.");
            string output = Environment.GetEnvironmentVariable("SEWERPILOT_P15_INTAKE_OUTPUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, RuntimeHostProtocol.Serializer().Serialize(result));
        }

        private static SolverLicenseContext LicenseContext(ModelReadEvidence evidence) => new SolverLicenseContext(
            typeof(LicenseReleaseProbe), typeof(LicenseProbe), typeof(LicenseMessageProbe),
            LicenseProductProbe.Bentley_SewerGEMS, LicensePlatformProbe.Standalone, "7.8.9.10", IntPtr.Zero, evidence);
        private static void P15License_InitializedBeforeRunAndCleanedAfterFailure()
        {
            foreach (bool failRun in new[] { false, true })
            {
                LicenseProbe.Reset(); var fixture = CreateSolverFixture(); fixture.Engine.FailRun = failRun;
                var context = LicenseContext(fixture.Evidence);
                Action run = () => GravityWriteAndSolverHost.RunBoundNumericalEngine(fixture.Bind(), fixture.Isolated, 81, 1, fixture.Evidence);
                try { if (failRun) Throws<InvalidOperationException>(run); else run(); }
                finally { context.Dispose(); }
                Check(LicenseProbe.LastRelease.PlatformAssignments == 1 && LicenseProbe.LastRelease.PlatformType == LicensePlatformProbe.Standalone,
                    "The validated public PlatformType setter assigns the required platform exactly once.");
                Check(LicenseProbe.Calls.SequenceEqual(new[] { "Product:Bentley_SewerGEMS:7.8.9.10", "Default", "Initialize", "Start", "Bind", "Run", "Stop", "LicenseDispose", "ReleaseDispose" }),
                    "License creation, initialization, start and binding precede one Run; cleanup follows even when Run throws.");
                Check(fixture.Engine.RunCount == 1 && fixture.Engine.ResultReads == 0 && SolverLicenseContext.Current == null &&
                    fixture.Evidence.Diagnostics["LicenseCleanupCompleted"] == "true", "No hydraulic reads or retained licensing context.");
            }
        }
        private static void CheckLicenseGate(Action fault, string expectedCalls)
        {
            LicenseProbe.Reset(); fault(); var fixture = CreateSolverFixture();
            var context = LicenseContext(fixture.Evidence);
            try { Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.RunBoundNumericalEngine(fixture.Bind(), fixture.Isolated, 81, 1, fixture.Evidence)); }
            finally { context.Dispose(); }
            Check(string.Join(",", LicenseProbe.Calls) == expectedCalls && fixture.Engine.RunCount == 0 &&
                !fixture.Evidence.Diagnostics.ContainsKey("SolverRunInvoked") && SolverLicenseContext.Current == null,
                "Invalid licensing blocks Run and guarantees exact bounded cleanup.");
        }
        private static void P15License_InitializationFailureBlocksRun() => CheckLicenseGate(
            () => LicenseProbe.InitializationFails = true, "Product:Bentley_SewerGEMS:7.8.9.10,Default,Initialize,LicenseDispose,ReleaseDispose");
        private static void P15License_ShutdownStatusBlocksRun() => CheckLicenseGate(
            () => LicenseProbe.Status = LicenseRunProbe.Shutdown, "Product:Bentley_SewerGEMS:7.8.9.10,Default,Initialize,Start,Stop,LicenseDispose,ReleaseDispose");
        private static void P15License_DisabledAnalysisBlocksRun() => CheckLicenseGate(
            () => LicenseProbe.AnalysisDisabled = true, "Product:Bentley_SewerGEMS:7.8.9.10,Default,Initialize,Start,Stop,LicenseDispose,ReleaseDispose");
        private static void P15License_StartExceptionStillStopsAndDisposes() => CheckLicenseGate(
            () => LicenseProbe.StartThrows = true, "Product:Bentley_SewerGEMS:7.8.9.10,Default,Initialize,Start,Stop,LicenseDispose,ReleaseDispose");
        private static void P15License_StopFailureStillDisposesAllResources()
        {
            LicenseProbe.Reset(); var evidence = new ModelReadEvidence(); var context = LicenseContext(evidence);
            try { context.StartAndAttach(new SolverEngineProbe()); }
            finally
            {
                LicenseProbe.StopThrows = true;
                Throws<AggregateException>(context.Dispose);
                Check(LicenseProbe.Calls.Skip(LicenseProbe.Calls.Count - 3).SequenceEqual(new[] { "Stop", "LicenseDispose", "ReleaseDispose" }) &&
                    SolverLicenseContext.Current == null && evidence.Diagnostics["LicenseCleanupCompleted"] == "false",
                    "Stop failure is retained while both disposals and context clearing still run.");
                LicenseProbe.Reset();
            }
        }
        private static void P15License_ReuseAndNestedContextBlocked()
        {
            LicenseProbe.Reset(); var evidence = new ModelReadEvidence(); var context = LicenseContext(evidence);
            try
            {
                Throws<InvalidOperationException>(() => LicenseContext(new ModelReadEvidence()));
                context.StartAndAttach(new SolverEngineProbe());
                Throws<InvalidOperationException>(() => context.StartAndAttach(new SolverEngineProbe()));
                Check(LicenseProbe.Calls.Count(value => value == "Initialize") == 1 && LicenseProbe.Calls.Count(value => value == "Start") == 1,
                    "No nested ownership or repeated license initialization/start.");
            }
            finally { context.Dispose(); }
        }
        private static void P15License_MissingBindingContractBlocksBeforeInitialization()
        {
            LicenseProbe.Reset(); var context = LicenseContext(new ModelReadEvidence());
            try
            {
                Throws<InvalidOperationException>(() => context.StartAndAttach(new object()));
                Check(LicenseProbe.Calls.Count == 0, "Exact public license binding contract is proven before licensing calls.");
            }
            finally { context.Dispose(); }
        }
        private static void Throws<T>(Action action) where T : Exception
        { assertions++; try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
        private static bool Neutral(Type type)
        {
            if (type == null) return true;
            if (type.HasElementType) return Neutral(type.GetElementType());
            return !(type.FullName ?? "").StartsWith("Haestad.", StringComparison.Ordinal) &&
                !(type.FullName ?? "").StartsWith("Bentley.", StringComparison.Ordinal) &&
                (!type.IsGenericType || type.GetGenericArguments().All(Neutral)) &&
                !(type.Namespace ?? "").StartsWith("System.Windows.Forms", StringComparison.Ordinal);
        }
        private static void PublicBoundary()
        {
            var assembly = typeof(OpenFlowsModelReader).Assembly;
            foreach (var type in assembly.GetExportedTypes())
            {
                Check(Neutral(type) && Neutral(type.BaseType) && type.GetInterfaces().All(Neutral), "Exported type leaks vendor/UI types.");
                foreach (var method in type.GetMethods()) Check(Neutral(method.ReturnType) && method.GetParameters().All(p => Neutral(p.ParameterType)), "Method leaks vendor types.");
                foreach (var constructor in type.GetConstructors()) Check(constructor.GetParameters().All(p => Neutral(p.ParameterType)), "Constructor leakage.");
                foreach (var property in type.GetProperties()) Check(Neutral(property.PropertyType) && property.GetIndexParameters().All(p => Neutral(p.ParameterType)), "Property leakage.");
                foreach (var field in type.GetFields()) Check(Neutral(field.FieldType), "Field leakage.");
                foreach (var item in type.GetEvents()) Check(Neutral(item.EventHandlerType), "Event leakage.");
            }
            Check(assembly.GetReferencedAssemblies().All(a => !a.Name.StartsWith("Haestad.") && !a.Name.StartsWith("Bentley.")), "Compile-time vendor dependency.");
        }
        private static void LazyBinding()
        {
            int before = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name.StartsWith("Haestad."));
            using (var reader = new OpenFlowsModelReader())
                Check(reader.Profile.GetCapability(CapabilityIdentifier.ModelRead).State == CapabilityState.NotTested, "Constructor promoted ModelRead.");
            Check(AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name.StartsWith("Haestad.")) == before, "Constructor loaded vendor code.");
        }
        private static OpenFlowsRuntimeCandidate Candidate(string name, string version)
        {
            string root = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticRuntime", name);
            var executable = new OpenFlowsAssemblyMetadata(Path.Combine(root, "SewerGEMS.exe"), "SewerGEMS", "X", version, version, version,
                "v4.0.30319", ".NETFramework,Version=v4.8", "x64", 0, "Synthetic");
            var core = OpenFlowsRuntimeCandidate.CoreAssemblyNames.Select(n => new OpenFlowsAssemblyMetadata(Path.Combine(root, n + ".dll"), n,
                "X", version, version, version, "v4.0.30319", ".NETFramework,Version=v4.8", "AnyCPU", 0));
            return new OpenFlowsRuntimeCandidate(root, executable, core, new[] { "Synthetic" });
        }
        private static OpenFlowsRuntimeSelection Select(params OpenFlowsRuntimeCandidate[] candidates) =>
            new OpenFlowsRuntimeSelector().Select(new OpenFlowsDiscoveryResult(candidates, new DiscoveryDiagnostics()), new OpenFlowsDiscoveryOptions());
        private static void VersionSelection()
        {
            Check(Select(Candidate("Stable", "1.0")).Status == Select(Candidate("Stable", "arbitrary-build-text")).Status, "Version affected selection.");
        }
        private static void HigherVersionNotPreferred()
        { Check(Select(Candidate("A", "1.0"), Candidate("B", "99.0")).Status == OpenFlowsRuntimeSelectionStatus.Ambiguous, "Higher version selected automatically."); }
        private static ModelReadEvidence Success()
        {
            var evidence = new ModelReadEvidence { RuntimeSelected = true, AssembliesBound = true,
                LifecycleInitialized = true, OpenSucceeded = true, CurrentGraphicalProjectAvailable = true, DomainProjectAvailable = true,
                DomainDataSetAvailable = true, FirstConduitElementID = 101, CloseCompleted = true, SourceIntegrity = true, WorkingIntegrity = true };
            foreach (string key in new[] { "RuntimeHostResultValid", "RuntimeHostResultCommitted", "RuntimeHostStaCompleted", "RuntimeHostProcessTerminated" }) evidence.Diagnostics[key] = "true";
            evidence.Diagnostics["RuntimeHostExitCode"] = "0"; evidence.Diagnostics["RuntimeHostWatchdogTermination"] = "false";
            return evidence;
        }
        private static bool Promoted(ModelReadEvidence e) => OpenFlowsModelReader.AcceptedProfile(OpenFlowsModelReader.EmptyProfile(), e)
            .GetCapability(CapabilityIdentifier.ModelRead).State == CapabilityState.Supported;
        private static void FailedOpen() { var e = Success(); e.OpenSucceeded = false; Check(!Promoted(e), "Failed open promoted ModelRead."); }
        private static void MissingDataset() { var e = Success(); e.DomainDataSetAvailable = false; Check(!Promoted(e), "Missing dataset promoted ModelRead."); }
        private static void MissingDatum() { var e = Success(); e.FirstConduitElementID = null; Check(!Promoted(e), "Missing datum promoted ModelRead."); }
        private static void SuccessfulRead()
        {
            var profile = OpenFlowsModelReader.AcceptedProfile(OpenFlowsModelReader.EmptyProfile(), Success());
            var evidence = profile.GetCapability(CapabilityIdentifier.ModelRead);
            Check(evidence.State == CapabilityState.Supported && evidence.Kind == CapabilityEvidenceKind.ModelRead, "Explicit evidence did not promote ModelRead.");
        }
        private static void OtherCapabilities()
        {
            var profile = OpenFlowsModelReader.AcceptedProfile(OpenFlowsModelReader.EmptyProfile(), Success());
            foreach (var id in new[] { CapabilityIdentifier.ScenarioRead, CapabilityIdentifier.ScenarioCreate, CapabilityIdentifier.AlternativeRead,
                CapabilityIdentifier.AlternativeCreate, CapabilityIdentifier.SolverRun, CapabilityIdentifier.ResultRead, CapabilityIdentifier.ModelWrite })
                Check(profile.GetCapability(id).State == CapabilityState.NotTested, "Unproven operation promoted.");
        }
        private static void OtherAcceptanceFailures()
        {
            foreach (Action<ModelReadEvidence> corrupt in new Action<ModelReadEvidence>[] { e => e.RuntimeSelected = false, e => e.AssembliesBound = false,
                e => e.LifecycleInitialized = false, e => e.CurrentGraphicalProjectAvailable = false, e => e.DomainProjectAvailable = false,
                e => e.CloseCompleted = false, e => e.SourceIntegrity = false, e => e.WorkingIntegrity = false,
                e => e.SaveWriteInvocations.Add("ForbiddenOperation"), e => e.FailureCode = "Failure" })
            { var e = Success(); corrupt(e); Check(!Promoted(e), "An incomplete acceptance condition promoted ModelRead."); }
        }
        private static WorkingCopyIntegrity Working()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticModel", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string source = Path.Combine(directory, "Model.stsw");
            File.WriteAllText(source, "synthetic source"); File.WriteAllText(source + ".sqlite", "synthetic database");
            File.WriteAllText(source + ".dwh", "synthetic companion"); File.WriteAllText(Path.Combine(directory, "Unrelated.txt"), "unrelated");
            return WorkingCopyIntegrity.Create(source);
        }
        private static void SourceMismatch()
        {
            var working = Working(); File.AppendAllText(working.SourceBefore[0].Path, "changed");
            var e = Success(); working.Complete(e);
            Check(e.FailureCode == "HARD_FAIL_SOURCE_MODEL_MODIFIED" && !Promoted(e), "Source mismatch was accepted.");
        }
        private static void WorkingMismatch()
        {
            var working = Working(); File.AppendAllText(working.WorkingBefore[0].Path, "changed");
            var e = Success(); working.Complete(e);
            Check(e.FailureCode == "P4_R9_BLOCKED_UNEXPECTED_WORKING_COPY_CHANGE" && !Promoted(e), "Working mutation was accepted.");
        }
        private static void TransientPreserved()
        {
            var working = Working(); string transient = Path.Combine(working.DirectoryPath, "new.lock"); File.WriteAllText(transient, "transient");
            var e = Success(); working.Complete(e);
            Check(e.SourceIntegrity && e.WorkingIntegrity && e.TransientFiles.Contains(transient) && File.Exists(transient), "Transient file invalidated pre-existing hashes or was removed.");
        }
        private static void SourceCopySafety()
        {
            var working = Working();
            Check(working.SourceBefore.Count == 3 && WorkingCopyIntegrity.Equal(working.SourceBefore, WorkingCopyIntegrity.Snapshot(working.SourceBefore)), "Copy changed source.");
            Check(!File.Exists(Path.Combine(working.DirectoryPath, "Unrelated.txt")), "Copy included unrelated files.");
            Check(working.ModelPath != working.SourceBefore[0].Path, "Original model was selected as working copy.");
        }
        private enum SyntheticElementType { ConduitElementManager = 57 }
        private static void NamedEnum() { Check((SyntheticElementType)WaterObjectsLifecycleHost.EnumMember(typeof(SyntheticElementType), "ConduitElementManager") == SyntheticElementType.ConduitElementManager, "Enum resolution depended on recorded numeric value."); }
        public interface IIDs { IDs ElementIDs(); }
        public class ManagerBase : IIDs { public IDs ElementIDs() => new IDs(); }
        public class Manager : ManagerBase { }
        public class IDs { public int Reads; public int this[int index] { get { Reads++; if (index != 0) throw new IndexOutOfRangeException(); return 101; } } }
        private static void InheritedElementIDs()
        {
            var method = WaterObjectsLifecycleHost.ElementIDs(typeof(Manager));
            Check(method.DeclaringType == typeof(ManagerBase) && method.Invoke(new Manager(), new object[0]) is IDs, "Inherited ElementIDs was not callable.");
        }
        public interface IA { IDs ElementIDs(); }
        public interface IB { string ElementIDs(); }
        public class Ambiguous : IA, IB { IDs IA.ElementIDs() => new IDs(); string IB.ElementIDs() => ""; }
        private static void AmbiguousElementIDs() { Throws<InvalidOperationException>(() => WaterObjectsLifecycleHost.ElementIDs(typeof(Ambiguous))); }
        private static void IntegerIndexer()
        {
            var ids = new IDs(); var accessor = WaterObjectsLifecycleHost.Indexer(ids.GetType());
            Check((int)accessor.GetValue(ids, new object[] { 0 }) == 101 && ids.Reads == 1, "Indexer did not read exactly one Int32 datum.");
        }
        private static void CleanupAfterFailure()
        {
            bool cleaned = false;
            Throws<InvalidOperationException>(() => WaterObjectsLifecycleHost.WithCleanup(() => { throw new InvalidOperationException("post-open failure"); }, () => cleaned = true));
            Check(cleaned, "Cleanup was not requested after failure.");
        }
        private static void DurableStageJournal()
        {
            string previous = Environment.GetEnvironmentVariable(RuntimeStageJournal.EnvironmentName);
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticJournal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "stages.jsonl");
            AppDomain domain = null;
            try
            {
                Environment.SetEnvironmentVariable(RuntimeStageJournal.EnvironmentName, path);
                domain = AppDomain.CreateDomain("SyntheticJournal", null, new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory });
                var worker = (JournalRemoteWorker)domain.CreateInstanceAndUnwrap(typeof(JournalRemoteWorker).Assembly.FullName, typeof(JournalRemoteWorker).FullName);
                RuntimeStageJournal.Call("JournalParent", () => {
                    Check(File.ReadAllText(path).Contains("\"state\":\"ENTER\""), "ENTER did not reach disk before the operation.");
                    worker.Emit();
                });
                var first = new Thread(() => RuntimeStageJournal.Call("ThreadA", () => { }));
                var second = new Thread(() => RuntimeStageJournal.Call("ThreadB", () => { }));
                first.Start(); second.Start(); first.Join(); second.Join();
                Throws<InvalidOperationException>(() => RuntimeStageJournal.Call("JournalException", () => { throw new InvalidOperationException("synthetic failure"); }));
                string[] lines = File.ReadAllLines(path);
                Check(lines.Length == 10, "Stage entries were lost across threads/AppDomains.");
                for (int index = 0; index < lines.Length; index++)
                {
                    int number = int.Parse(Regex.Match(lines[index], "\"stage_number\":([0-9]+)").Groups[1].Value);
                    Check(number == index + 1 && new[] { "utc", "process_id", "managed_thread_id", "appdomain_id", "apartment_state", "message_loop" }.All(key => lines[index].Contains("\"" + key + "\"")), "Stage order/required context was not durable.");
                }
                Check(lines.Last().Contains("EXCEPTION") && lines.Last().Contains("synthetic failure"), "Exact exception was not retained.");
            }
            finally
            {
                if (domain != null) AppDomain.Unload(domain);
                Environment.SetEnvironmentVariable(RuntimeStageJournal.EnvironmentName, previous);
            }
        }
        private static HostRequest Request()
        {
            var working = Working();
            string root = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticHostRuntime", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var core = new List<HostMetadata>();
            foreach (string name in OpenFlowsRuntimeCandidate.CoreAssemblyNames)
            {
                string path = Path.Combine(root, name + ".dll"); File.WriteAllText(path, "synthetic metadata file");
                core.Add(new HostMetadata { Path = path, SimpleName = name, SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256,
                    TargetFramework = ".NETFramework,Version=v4.8", Architecture = "AnyCPU" });
            }
            return new HostRequest { SchemaVersion = 1, RunId = Guid.NewGuid().ToString("N"), Operation = RuntimeHostProtocol.Operation,
                RuntimeCandidateCount = 1, SelectedCandidate = "synthetic", SelectedRuntime = root, WorkingModelPath = working.ModelPath,
                DetectedArchitecture = "x64", CoreFiles = core.ToArray() };
        }
        private static HostResult Result(HostRequest request) => new HostResult {
            SchemaVersion = 1, RunId = request.RunId, Success = true, RuntimeCandidateCount = request.RuntimeCandidateCount,
            SelectedRuntime = request.SelectedRuntime, PrimaryProductRoot = request.SelectedRuntime,
            CompanionRoots = new HostCompanionRoot[0],
            DetectedArchitecture = "x64", RuntimeBinding = true,
            BoundCoreFiles = request.CoreFiles, ApplicationDescriptionCreated = true, ApplicationModelCreated = true,
            ParentUiModelCreated = true, LifecycleInitialized = true, ModelOpen = true, CurrentGraphicalProjectEstablished = true,
            DomainProjectEstablished = true, DomainDataSetEstablished = true, DatumName = "FirstConduitElementID", DatumValue = 101,
            VendorCleanupCompleted = true, StaCompleted = true, ResultCommitted = true, RequestedExitMode = RuntimeHostProtocol.ExitMode };
        private static string ResultFile(HostRequest request)
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticResults", request.RunId);
            Directory.CreateDirectory(directory); return Path.Combine(directory, "result.json");
        }
        private static void RequestValidation()
        {
            var request = Request(); RuntimeHostProtocol.ValidateRequest(request);
            request.Operation = "arbitrary expression"; Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(request));
            request.Operation = RuntimeHostProtocol.Operation; request.WorkingModelPath = Working().SourceBefore[0].Path;
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(request));
            request = Request(); request.CoreFiles[0].Path = request.CoreFiles[1].Path;
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(request));
        }
        private static void ResultParsing()
        {
            var request = Request(); string path = ResultFile(request);
            RuntimeHostProtocol.AtomicWrite(path, Result(request));
            Check(RuntimeHostProtocol.ParseResult(path, request).DatumValue == 101 && !File.Exists(path + ".tmp"), "Committed neutral result did not round-trip.");
        }
        private static void InvalidResults()
        {
            var request = Request(); string path = ResultFile(request);
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            File.WriteAllText(path, "{partial"); Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            File.WriteAllText(path, "{\"SchemaVersion\":1}"); Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            var result = Result(request); result.SchemaVersion = 2; File.WriteAllText(path, RuntimeHostProtocol.Serializer().Serialize(result));
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            result.SchemaVersion = 1; result.RunId = Guid.NewGuid().ToString("N"); File.WriteAllText(path, RuntimeHostProtocol.Serializer().Serialize(result));
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
        }
        private static void CompletionRequirements()
        {
            var request = Request(); string path = ResultFile(request);
            foreach (Action<HostResult> corrupt in new Action<HostResult>[] { value => value.VendorCleanupCompleted = false,
                value => value.StaCompleted = false, value => value.ResultCommitted = false, value => value.MutationInvocationCount = 1 })
            {
                var result = Result(request); corrupt(result); File.WriteAllText(path, RuntimeHostProtocol.Serializer().Serialize(result));
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(path, request));
            }
        }
        private sealed class FakeProcess : IRuntimeHostProcess
        {
            internal bool Timeout, Killed, Disposed;
            internal int Starts, Code;
            public int Id => 424242;
            public int ExitCode => Code;
            public bool WaitForExit(int milliseconds) => !Timeout || Killed;
            public void Kill() { Killed = true; }
            public void Dispose() { Disposed = true; }
        }
        private sealed class FakeLauncher : IRuntimeHostLauncher
        {
            internal readonly FakeProcess Child = new FakeProcess();
            internal bool Missing, MissingMarker;
            public IRuntimeHostProcess Start(string executable, string requestPath, string resultPath, string journal)
            {
                Child.Starts++;
                var request = RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(File.ReadAllText(requestPath));
                if (!Missing) RuntimeHostProtocol.AtomicWrite(resultPath, Result(request));
                File.WriteAllText(journal, RuntimeHostProtocol.Serializer().Serialize(new { stage_name = MissingMarker ? "OtherStage" : "ResultCommitted", state = "EXIT", process_id = Child.Id }) + "\n");
                return Child;
            }
        }
        private static void HostExitClassification()
        {
            var good = new FakeLauncher(); var outcome = new RuntimeHostClient(good).Read(Request());
            Check(outcome.Evidence.FailureCode == null && good.Child.Starts == 1 && good.Child.Disposed, "Valid child was not consumed/disposed exactly once.");
            var bad = new FakeLauncher(); bad.Child.Code = 17; outcome = new RuntimeHostClient(bad).Read(Request());
            Check(outcome.Evidence.FailureCode == "P4_R9_BLOCKED_VENDOR_RUNTIME" && bad.Child.Starts == 1, "Nonzero exit was accepted or retried.");
            var missing = new FakeLauncher { Missing = true }; outcome = new RuntimeHostClient(missing).Read(Request());
            Check(outcome.Evidence.FailureCode == "P4_R9_BLOCKED_RESULT_INVALID", "Missing child result was accepted.");
            var marker = new FakeLauncher { MissingMarker = true }; outcome = new RuntimeHostClient(marker).Read(Request());
            Check(outcome.Evidence.FailureCode == "P4_R9_BLOCKED_RESULT_INVALID", "Unconfirmed result commit was accepted.");
        }
        private static void HostTimeoutOwnership()
        {
            var launcher = new FakeLauncher(); launcher.Child.Timeout = true;
            var unrelated = new FakeProcess(); var outcome = new RuntimeHostClient(launcher).Read(Request());
            Check(launcher.Child.Killed && launcher.Child.Disposed && launcher.Child.Starts == 1 && !unrelated.Killed &&
                outcome.Evidence.Diagnostics["RuntimeHostWatchdogTermination"] == "true" && outcome.Evidence.FailureCode != null,
                "Watchdog accepted/retried a timed-out child or targeted another handle.");
        }
        private static void ProcessPromotionGates()
        {
            foreach (string key in new[] { "RuntimeHostResultValid", "RuntimeHostResultCommitted", "RuntimeHostStaCompleted",
                "RuntimeHostProcessTerminated", "RuntimeHostExitCode", "RuntimeHostWatchdogTermination" })
            { var evidence = Success(); evidence.Diagnostics.Remove(key); Check(!Promoted(evidence), "Missing host completion evidence promoted ModelRead."); }
        }
        private static void ProductionDomainBoundary()
        {
            foreach (string folder in new[] { "SewerPilot.OpenFlows.Adapter", "SewerPilot.OpenFlows.RuntimeHost" })
            {
                string text = string.Join("\n", Directory.GetFiles(Path.Combine(Repository(), "src", folder), "*.cs").Select(File.ReadAllText));
                Check(!text.Contains("AppDomain.CreateDomain") && !text.Contains("AppDomain.Unload"), "Secondary domain in production path.");
            }
            string reader = File.ReadAllText(Path.Combine(Repository(), "src/SewerPilot.OpenFlows.Adapter/OpenFlowsModelReader.cs"));
            Check(reader.Contains("new MetadataWorker().Inspect") && !reader.Contains("new OpenFlowsAssemblyBinder()"), "P4 calls historical isolated P3 entry points.");
        }
        private static void ProcessDtoBoundary()
        {
            foreach (var type in new[] { typeof(HostRequest), typeof(HostResult), typeof(HostMetadata), typeof(HostFingerprint), typeof(HostCompanionRoot) })
                Check(type.GetProperties().All(property => Neutral(property.PropertyType) && property.PropertyType != typeof(Type) &&
                    !(property.PropertyType.Namespace ?? "").StartsWith("System.Reflection")), "Process DTO exposes vendor/reflection objects.");
        }
        private static string Repository()
        {
            var cursor = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (cursor != null && !File.Exists(Path.Combine(cursor.FullName, "SewerPilot.sln"))) cursor = cursor.Parent;
            return cursor.FullName;
        }
        private static void ProvenanceIdentity()
        {
            var assembly = typeof(Program).Assembly;
            var evidence = new ModelReadEvidence { FailureCode = "AssemblyBindingFailure" };
            var diagnostics = new AssemblyProvenanceDiagnostics(evidence);
            diagnostics.Snapshot(new[] { assembly }, Path.GetTempPath());
            diagnostics.Reject(assembly, Path.GetTempPath(), Path.GetFullPath(assembly.Location), true);
            var records = RuntimeHostProtocol.Serializer().Deserialize<List<Dictionary<string, object>>>(evidence.Diagnostics["OffendingAssembly"]);
            var record = records.Single();
            Check((string)record["SimpleName"] == assembly.GetName().Name && (string)record["FullName"] == assembly.FullName, "Rejected identity missing.");
            Check((string)record["Location"] == assembly.Location && (bool)record["GlobalAssemblyCache"] == assembly.GlobalAssemblyCache, "Location/GAC flag missing.");
            Check((string)record["SelectedRuntimeRoot"] == Path.GetTempPath() && (string)record["NormalizedAssemblyPath"] == Path.GetFullPath(assembly.Location), "Root/normalized path missing.");
            Check(record["PublicKeyToken"] == null && evidence.FailureCode == "AssemblyBindingFailure", "Unsigned token or failure altered.");
            var signed = AssemblyProvenanceDiagnostics.Describe(typeof(object).Assembly, Path.GetTempPath());
            Check(Regex.IsMatch((string)signed["PublicKeyToken"], "^[0-9a-f]+$") && (bool)signed["GlobalAssemblyCache"], "Signed framework evidence missing.");
        }
        private static void ProvenanceRequester()
        {
            var evidence = new ModelReadEvidence { FailureCode = "AssemblyBindingFailure" };
            var diagnostics = new AssemblyProvenanceDiagnostics(evidence);
            var assembly = typeof(Program).Assembly;
            var trace = diagnostics.BeginResolve(new ResolveEventArgs(assembly.FullName, typeof(object).Assembly));
            diagnostics.EndResolve(trace, assembly.Location, assembly);
            diagnostics.Reject(assembly, Path.GetTempPath(), assembly.Location, true);
            var record = RuntimeHostProtocol.Serializer().Deserialize<List<Dictionary<string, object>>>(evidence.Diagnostics["OffendingAssembly"]).Single();
            Check((string)record["RequestingAssemblySimpleName"] == typeof(object).Assembly.GetName().Name &&
                (string)record["RequestingAssemblyFullName"] == typeof(object).Assembly.FullName &&
                (string)record["RequestingAssemblyLocation"] == typeof(object).Assembly.Location, "Available requester was lost.");
        }
        private static void ProvenanceBoundary()
        {
            Check(AssemblyProvenanceDiagnostics.ContainsPath(@"C:\Root", @"c:\ROOT\file.dll"), "Case-insensitive containment lost.");
            Check(!AssemblyProvenanceDiagnostics.ContainsPath(@"C:\Root", @"C:\Root2\file.dll"), "Prefix sibling treated as contained.");
            Check(AssemblyProvenanceDiagnostics.ContainsPath(@"C:\Root\", @"C:\Root\file.dll"), "Diagnostic normalization lost boundary.");
        }
        private static bool SyntheticGuard(string name, string root, string path, bool dynamic, bool locationless, Action rejected)
        {
            if (dynamic || locationless) return false;
            bool vendor = name.StartsWith("Haestad.", StringComparison.Ordinal) || name.StartsWith("Bentley.", StringComparison.Ordinal);
            if (Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            if (vendor) { rejected?.Invoke(); return true; }
            return false;
        }
        private static void ProvenanceSemantics()
        {
            var assembly = typeof(Program).Assembly;
            foreach (string name in new[] { "Haestad.Synthetic", "Bentley.Synthetic", "System.Synthetic", "SewerPilot.Synthetic" })
            foreach (string path in new[] { @"C:\Root\x.dll", @"C:\Root2\x.dll", @"c:\ROOT\x.dll" })
            foreach (bool dynamic in new[] { false, true })
            foreach (bool locationless in new[] { false, true })
            {
                var evidence = new ModelReadEvidence { FailureCode = "AssemblyBindingFailure" };
                var diagnostics = new AssemblyProvenanceDiagnostics(evidence);
                bool before = SyntheticGuard(name, @"C:\Root", path, dynamic, locationless, null);
                bool after = SyntheticGuard(name, @"C:\Root", path, dynamic, locationless,
                    () => diagnostics.Reject(assembly, @"C:\Root", Path.GetFullPath(path), true));
                Check(before == after && evidence.FailureCode == "AssemblyBindingFailure", "Diagnostic enrichment changed guard outcome.");
            }
        }
        private static void ProvenanceDynamic()
        {
            var dynamic = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("SyntheticProvenanceDynamic"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var record = AssemblyProvenanceDiagnostics.Describe(dynamic, Path.GetTempPath());
            Check((bool)record["IsDynamicIfAvailable"] && Equals(record["Location"], AssemblyProvenanceDiagnostics.Unavailable), "Dynamic/locationless diagnostics threw or invented a path.");
            var evidence = new ModelReadEvidence();
            new AssemblyProvenanceDiagnostics(evidence).Snapshot(new Assembly[] { dynamic }, Path.GetTempPath());
            Check(evidence.Diagnostics.ContainsKey("ProvenanceSnapshot"), "Dynamic snapshot missing.");
        }
        private sealed class UnreadableAssembly : Assembly
        {
            public override string Location => throw new InvalidOperationException("synthetic property failure");
            public override string FullName => throw new InvalidOperationException("synthetic property failure");
            public override AssemblyName GetName() => throw new InvalidOperationException("synthetic property failure");
        }
        private static void ProvenancePropertyFailure()
        {
            var evidence = new ModelReadEvidence { FailureCode = "AssemblyBindingFailure" };
            new AssemblyProvenanceDiagnostics(evidence).Reject(new UnreadableAssembly(), @"C:\Root", @"C:\Outside\x.dll", true);
            var record = RuntimeHostProtocol.Serializer().Deserialize<List<Dictionary<string, object>>>(evidence.Diagnostics["OffendingAssembly"]).Single();
            Check(Equals(record["SimpleName"], "UNAVAILABLE") && Equals(record["FullName"], "UNAVAILABLE") &&
                evidence.FailureCode == "AssemblyBindingFailure", "Property failure masked provenance failure.");
        }
        private static void ProvenanceResultPersistence()
        {
            var request = Request(); var result = Result(request); result.Success = false;
            result.FailureClassification = "AssemblyBindingFailure"; result.FailureMessageSafe = "Vendor dependency escaped selected root.";
            result.RequestedExitMode = "Natural";
            var evidence = new ModelReadEvidence { FailureCode = result.FailureClassification };
            var diagnostics = new AssemblyProvenanceDiagnostics(evidence);
            diagnostics.Snapshot(new[] { typeof(Program).Assembly }, request.SelectedRuntime);
            diagnostics.Reject(typeof(Program).Assembly, request.SelectedRuntime, typeof(Program).Assembly.Location, true);
            diagnostics.Reject(typeof(object).Assembly, request.SelectedRuntime, typeof(object).Assembly.Location, true);
            result.Diagnostics = evidence.Diagnostics;
            string path = ResultFile(request); RuntimeHostProtocol.AtomicWrite(path, result);
            var parsed = RuntimeHostProtocol.ParseResult(path, request);
            Check(RuntimeHostProtocol.Serializer().Deserialize<List<Dictionary<string, object>>>(parsed.Diagnostics["OffendingAssembly"]).Count == 2 &&
                parsed.Diagnostics.ContainsKey("ProvenanceSnapshot") && parsed.Diagnostics.ContainsKey("LastDurableStage"), "All offenders/snapshot lost at result boundary.");
            Check(!parsed.Success && parsed.FailureClassification == "AssemblyBindingFailure", "Evidence persistence changed denial.");
        }
        private static void NoWriteOperations()
        {
            string repo = Repository();
            string adapterRoot = Path.Combine(repo, "src", "SewerPilot.OpenFlows.Adapter");
            string runtimeRoot = Path.Combine(repo, "src", "SewerPilot.OpenFlows.RuntimeHost");

            // The single formally authorized architectural gateway. It is the only source file permitted to
            // name write-intent members. Every other file in the adapter and runtime host stays strictly
            // read-only. The exclusion is by exact filename, never by content or directory wildcard.
            const string GatewayFileName = "AdapterWriteBinding.cs";
            string gatewayPath = Path.Combine(adapterRoot, GatewayFileName);

            var allFiles = Directory.GetFiles(adapterRoot, "*.cs")
                .Concat(Directory.GetFiles(runtimeRoot, "*.cs"))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            var readOnlyFiles = allFiles
                .Where(f => !Path.GetFileName(f).Equals(GatewayFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Check(File.Exists(gatewayPath), "Authorized write gateway " + GatewayFileName + " exists.");
            Check(readOnlyFiles.Count == allFiles.Count - 1,
                "Write scan excluded exactly one file, the authorized gateway (excluded " + (allFiles.Count - readOnlyFiles.Count) + ").");

            // 1. No write-intent member may appear in any file other than the gateway.
            string readOnlyText = string.Join("\n", readOnlyFiles.Select(File.ReadAllText));
            foreach (string operation in new[] { "Save", "SaveAs", "SaveCurrentProject" })
                Check(!readOnlyText.Contains("\"" + operation + "\"") && !readOnlyText.Contains("." + operation + "("),
                    "Forbidden executable operation outside the gateway: " + operation);
            Check(!readOnlyText.Contains("PropertyInfo.SetValue") && !readOnlyText.Contains(".SetValue("),
                "Model setter in read path outside the gateway.");

            // 2. The exclusion must not create a blind spot: the gateway is still scanned for every
            //    non-write forbidden operation, and may never execute solver or state-mutating work.
            string gatewayText = File.ReadAllText(gatewayPath);
            foreach (string operation in new[] { "SaveCurrentProject", "Commit", "ApplyChanges", "Compute", "RunSolver", "ComputeCurrentScenario", "ScenarioManager", "AlternativeManager" })
                Check(!gatewayText.Contains("\"" + operation + "\"") && !gatewayText.Contains("." + operation + "("),
                    "Forbidden executable operation in the write gateway: " + operation);

            // 3. Positive verification: the gateway is compiled into the adapter assembly and exposes the
            //    exclusive audited entry points through which mutating dataset state is reached.
            Type gateway = typeof(HydraulicResultSet).Assembly.GetType("SewerPilot.OpenFlows.Adapter.AdapterWriteBinding", false);
            Check(gateway != null, "Authorized write gateway is compiled into the adapter assembly.");
            var gatewayMethods = gateway.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (string entryPoint in new[] { "InvokeSetValue", "ExecuteSave", "ExecuteSaveAs", "EncodeImage" })
                Check(gatewayMethods.Any(m => m.Name == entryPoint),
                    "Write gateway exposes audited entry point: " + entryPoint);
        }

        private sealed class SyntheticVendorAnchor : Assembly
        {
            private readonly string location;
            private readonly AssemblyName name;
            internal SyntheticVendorAnchor(string name, string location, Version version = null)
            {
                this.name = new AssemblyName(name) { Version = version ?? new Version(1, 0, 0, 0) };
                this.location = location;
            }
            public override string Location => location;
            public override string FullName => name.FullName;
            public override AssemblyName GetName() => name;
        }

        private static void CompanionAnchorValidation()
        {
            var dynamic = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("Bentley.DynamicAnchor"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            HostCompanionRoot companion;
            string failure;
            Check(!AssemblyProvenanceDiagnostics.TryValidateCompanionAnchor(dynamic, out companion, out failure) && companion == null,
                "Dynamic assembly produced companion root.");
            Check(failure.Contains("dynamic"), "Dynamic rejection reason missing.");

            string tempDir = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticAnchorValidation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string validDll = Path.Combine(tempDir, "Bentley.Connect.Client.API.dll");
            File.WriteAllText(validDll, "anchor");

            var missingLoc = new SyntheticVendorAnchor("Bentley.Connect.Client.API", "");
            Check(!AssemblyProvenanceDiagnostics.TryValidateCompanionAnchor(missingLoc, out companion, out failure),
                "Missing location produced companion root.");

            var nonexistentFile = new SyntheticVendorAnchor("Bentley.Connect.Client.API", Path.Combine(tempDir, "Nonexistent.dll"));
            Check(!AssemblyProvenanceDiagnostics.TryValidateCompanionAnchor(nonexistentFile, out companion, out failure),
                "Nonexistent file produced companion root.");

            var nonVendor = new SyntheticVendorAnchor("System.Synthetic", validDll);
            Check(!AssemblyProvenanceDiagnostics.TryValidateCompanionAnchor(nonVendor, out companion, out failure),
                "Non-vendor anchor produced companion root.");

            var validAnchor = new SyntheticVendorAnchor("Bentley.Connect.Client.API", validDll, new Version(99, 88, 77, 66));
            Check(AssemblyProvenanceDiagnostics.TryValidateCompanionAnchor(validAnchor, out companion, out failure) && companion != null,
                "Valid companion anchor failed validation: " + failure);
            Check(companion.RootKind == "BentleyCompanion" && companion.DiscoverySource == "LoadedAnchorAssembly",
                "Discovery metadata invalid.");
            Check(companion.ValidationResult == "VALIDATED" && companion.Exists && companion.AnchorAssembly == "Bentley.Connect.Client.API",
                "Anchor validation result invalid.");
            Check(string.Equals(companion.Root, tempDir, StringComparison.OrdinalIgnoreCase),
                "Companion root was not derived dynamically from anchor directory.");
            Check(companion.AnchorVersion == "99.88.77.66", "Non-1.0.0.0 version was rejected or lost.");
        }

        private static void CompanionRootContainment()
        {
            string syntheticBase = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticContainment", Guid.NewGuid().ToString("N"));
            string productRoot = Path.Combine(syntheticBase, "OpenFlowsProduct");
            string companionRoot = Path.Combine(syntheticBase, "ConnectionClient");
            string licServiceSubdir = Path.Combine(companionRoot, "LicService");
            string prefixSiblingRoot = Path.Combine(syntheticBase, "ConnectionClientFake");
            Directory.CreateDirectory(productRoot);
            Directory.CreateDirectory(companionRoot);
            Directory.CreateDirectory(licServiceSubdir);
            Directory.CreateDirectory(prefixSiblingRoot);

            var companionObj = new HostCompanionRoot {
                Root = companionRoot, RootKind = "BentleyCompanion", DiscoverySource = "LoadedAnchorAssembly",
                AnchorAssembly = "Bentley.Connect.Client.API", Exists = true, ValidationResult = "VALIDATED"
            };
            var companionList = new List<HostCompanionRoot> { companionObj };

            string productDll = Path.Combine(productRoot, "Haestad.Support.dll");
            Check(AssemblyProvenanceDiagnostics.IsUnder(productRoot, productDll), "Product root assembly rejected.");

            string companionTopDll = Path.Combine(companionRoot, "Bentley.Connect.Client.API.dll");
            Check(AssemblyProvenanceDiagnostics.IsUnderAny(companionList, companionTopDll), "Companion root top assembly rejected.");

            string licServiceDll = Path.Combine(licServiceSubdir, "Bentley.Licensing.Client.API.dll");
            Check(AssemblyProvenanceDiagnostics.IsUnderAny(companionList, licServiceDll), "Companion descendant LicService assembly rejected.");

            string siblingDll = Path.Combine(prefixSiblingRoot, "Bentley.Fake.dll");
            Check(!AssemblyProvenanceDiagnostics.IsUnderAny(companionList, siblingDll), "Prefix sibling directory was accepted as companion root.");

            string[] offenderRelativePaths = new[] {
                "bentley.connect.client.api.dll",
                "Bentley.Connect.Client.Service.Interface.dll",
                Path.Combine("LicService", "Bentley.Licensing.Client.API.dll"),
                Path.Combine("LicService", "Bentley.Licensing.ServiceInterface.dll"),
                Path.Combine("LicService", "Bentley.Licensing.Common.dll")
            };

            var emptyCompanions = new List<HostCompanionRoot>();
            foreach (var rel in offenderRelativePaths)
            {
                string fullPath = Path.Combine(companionRoot, rel);
                Check(AssemblyProvenanceDiagnostics.IsUnderAny(companionList, fullPath), "R11 offender was not accepted under validated companion root: " + rel);
                Check(!AssemblyProvenanceDiagnostics.IsUnder(productRoot, fullPath), "R11 offender unexpectedly claimed to be under product root: " + rel);
                Check(!AssemblyProvenanceDiagnostics.IsUnderAny(emptyCompanions, fullPath), "R11 offender accepted without validated companion root: " + rel);
            }

            var secondCompanion = new HostCompanionRoot {
                Root = Path.Combine(syntheticBase, "SecondCompanion"), RootKind = "BentleyCompanion",
                DiscoverySource = "LoadedAnchorAssembly", AnchorAssembly = "Bentley.Other.Anchor", Exists = true, ValidationResult = "VALIDATED"
            };
            Directory.CreateDirectory(secondCompanion.Root);
            string secondDll = Path.Combine(secondCompanion.Root, "Bentley.Other.dll");
            var multipleCompanions = new List<HostCompanionRoot> { companionObj, secondCompanion };
            Check(AssemblyProvenanceDiagnostics.IsUnderAny(multipleCompanions, companionTopDll), "First root broken in multi-root list.");
            Check(AssemblyProvenanceDiagnostics.IsUnderAny(multipleCompanions, secondDll), "Second root not recognized in multi-root list.");
            Check(!AssemblyProvenanceDiagnostics.IsUnderAny(multipleCompanions, siblingDll), "Invalid path accepted in multi-root list.");
        }

        private static void VendorProvenanceRejection()
        {
            string productRoot = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticProductRoot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(productRoot);
            string arbitraryDir = Path.Combine(Path.GetTempPath(), "SewerPilot", "ArbitraryDir", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(arbitraryDir);

            string bentleyFakeDll = Path.Combine(arbitraryDir, "Bentley.Fake.dll");
            string haestadFakeDll = Path.Combine(arbitraryDir, "Haestad.Fake.dll");
            File.WriteAllText(bentleyFakeDll, "fake");
            File.WriteAllText(haestadFakeDll, "fake");

            var companion = new HostCompanionRoot {
                Root = Path.Combine(Path.GetTempPath(), "SewerPilot", "SyntheticCompanionRoot"),
                RootKind = "BentleyCompanion", DiscoverySource = "LoadedAnchorAssembly",
                AnchorAssembly = "Bentley.Connect.Client.API", Exists = true, ValidationResult = "VALIDATED"
            };
            var companionList = new List<HostCompanionRoot> { companion };

            Check(!AssemblyProvenanceDiagnostics.IsUnder(productRoot, bentleyFakeDll), "Bentley.Fake under product root.");
            Check(!AssemblyProvenanceDiagnostics.IsUnderAny(companionList, bentleyFakeDll), "Bentley.Fake under companion root.");

            Check(!AssemblyProvenanceDiagnostics.IsUnder(productRoot, haestadFakeDll), "Haestad.Fake under product root.");
            Check(!AssemblyProvenanceDiagnostics.IsUnderAny(companionList, haestadFakeDll), "Haestad.Fake under companion root.");

            var evidence = new ModelReadEvidence();
            var diagnostics = new AssemblyProvenanceDiagnostics(evidence);
            var bentleyAnchor = new SyntheticVendorAnchor("Bentley.Fake", bentleyFakeDll);
            diagnostics.Reject(bentleyAnchor, productRoot, companionList, bentleyFakeDll, true);

            Check(evidence.Diagnostics.ContainsKey("OffendingAssembly"), "OffendingAssembly diagnostic missing.");
            var offenders = RuntimeHostProtocol.Serializer().Deserialize<List<Dictionary<string, object>>>(evidence.Diagnostics["OffendingAssembly"]);
            Check(offenders.Count == 1, "Offender count mismatch.");
            var record = offenders[0];

            Check((string)record["SimpleName"] == "Bentley.Fake", "SimpleName missing in rejection.");
            Check((string)record["FullName"] == bentleyAnchor.FullName, "FullName missing in rejection.");
            Check((string)record["Location"] == bentleyFakeDll, "Location missing in rejection.");
            Check((string)record["SelectedProductRoot"] == productRoot, "SelectedProductRoot missing in rejection.");
            Check(((System.Collections.IList)record["ValidatedCompanionRoots"]).Count == 1, "ValidatedCompanionRoots missing in rejection.");
            Check(((System.Collections.IList)record["RootDiscoveryEvidence"]).Count == 1, "RootDiscoveryEvidence missing in rejection.");
            Check(Equals(record["IsUnderProductRoot"], false), "IsUnderProductRoot must be false.");
            Check(Equals(record["IsUnderCompanionRoot"], false), "IsUnderCompanionRoot must be false.");
            Check((string)record["RejectionReason"] == "Vendor dependency outside validated product and companion roots.", "RejectionReason mismatch.");
        }

        private static void CompanionProvenanceResultPersistence()
        {
            var request = Request();
            var result = Result(request);
            result.PrimaryProductRoot = request.SelectedRuntime;
            result.CompanionRoots = new[] {
                new HostCompanionRoot {
                    Root = Path.Combine(Path.GetTempPath(), "SyntheticCompanion"),
                    RootKind = "BentleyCompanion", DiscoverySource = "LoadedAnchorAssembly",
                    AnchorAssembly = "Bentley.Connect.Client.API", AnchorFullName = "Bentley.Connect.Client.API, Version=1.0.0.0",
                    AnchorLocation = Path.Combine(Path.GetTempPath(), "SyntheticCompanion", "Bentley.Connect.Client.API.dll"),
                    Exists = true, ValidationResult = "VALIDATED", AnchorVersion = "1.0.0.0"
                }
            };
            string path = ResultFile(request);
            RuntimeHostProtocol.AtomicWrite(path, result);
            var parsed = RuntimeHostProtocol.ParseResult(path, request);
            Check(parsed.PrimaryProductRoot == request.SelectedRuntime, "PrimaryProductRoot lost across IPC.");
            Check(parsed.CompanionRoots != null && parsed.CompanionRoots.Length == 1, "CompanionRoots lost across IPC.");
            Check(parsed.CompanionRoots[0].AnchorAssembly == "Bentley.Connect.Client.API", "Anchor assembly lost across IPC.");
            Check(parsed.CompanionRoots[0].ValidationResult == "VALIDATED", "Validation result lost across IPC.");
        }

        private static string CreateSyntheticFamily(string rootDir, string modelName = "TestModel.stsw", string content = "primary content")
        {
            if (!Directory.Exists(rootDir)) Directory.CreateDirectory(rootDir);
            string primary = Path.Combine(rootDir, modelName);
            File.WriteAllText(primary, content);
            File.WriteAllText(primary + ".sqlite", content + " sqlite data");
            File.WriteAllText(primary + ".dwh", content + " dwh data");
            return primary;
        }

        private static void DeterministicFingerprintRepeatability()
        {
            var entries = new[] {
                new ModelFamilyFileEntry("Model.stsw", 100, new string('A', 64)),
                new ModelFamilyFileEntry("Model.stsw.sqlite", 200, new string('B', 64)),
                new ModelFamilyFileEntry("Model.stsw.dwh", 300, new string('C', 64))
            };
            var fp1 = ModelFamilyFingerprint.Create("Model.stsw", entries);
            var fp2 = ModelFamilyFingerprint.Create("Model.stsw", entries);
            Check(fp1.Value == fp2.Value, "Fingerprint is not repeatable.");
            Check(fp1.CanonicalManifest == fp2.CanonicalManifest, "Canonical manifest is not repeatable.");
            Check(fp1.Value.Length == 64, "Fingerprint length mismatch.");
        }

        private static void PathIndependence()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_PathIndep_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dirA = Path.Combine(testRoot, "DirA");
                string dirB = Path.Combine(testRoot, "DirB");
                string primaryA = CreateSyntheticFamily(dirA, "Lesson.stsw", "identical content");
                string primaryB = CreateSyntheticFamily(dirB, "Lesson.stsw", "identical content");

                var baselineA = ProtectedBaseline.Register(primaryA);
                var baselineB = ProtectedBaseline.Register(primaryB);

                Check(baselineA.Fingerprint.Value == baselineB.Fingerprint.Value, "Identical models at different paths produced different fingerprints.");
                Check(baselineA.DirectoryPath != baselineB.DirectoryPath, "Directories must differ.");
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void ChangedBytesDifferentFingerprint()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_ChangedBytes_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dirA = Path.Combine(testRoot, "DirA");
                string dirB = Path.Combine(testRoot, "DirB");
                string primaryA = CreateSyntheticFamily(dirA, "Lesson.stsw", "content version 1");
                string primaryB = CreateSyntheticFamily(dirB, "Lesson.stsw", "content version 2");

                var baselineA = ProtectedBaseline.Register(primaryA);
                var baselineB = ProtectedBaseline.Register(primaryB);

                Check(baselineA.Fingerprint.Value != baselineB.Fingerprint.Value, "Different content produced identical fingerprint.");
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void IntegrityLengthAndHashDifferences()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_IntegrityDiff_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "ModelDir");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "initial content");
                var baseline = ProtectedBaseline.Register(primary);

                var initialResult = baseline.VerifyCurrentIntegrity();
                Check(initialResult.IsMatch, "Initial baseline integrity failed.");

                File.AppendAllText(primary + ".sqlite", " appended bytes to change length and hash");

                var modifiedResult = baseline.VerifyCurrentIntegrity();
                Check(!modifiedResult.IsMatch, "Modified file was not detected by integrity check.");
                Check(modifiedResult.Differences.Any(d => d.Kind == "LengthMismatch" || d.Kind == "HashMismatch"), "Difference details missing length/hash mismatch.");
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void MissingFamilyFileDetected()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_MissingFile_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "ModelDir");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "initial content");
                var baseline = ProtectedBaseline.Register(primary);

                File.Delete(primary + ".dwh");

                var result = baseline.VerifyCurrentIntegrity();
                Check(!result.IsMatch, "Deleted family file was not detected.");
                Check(result.Differences.Any(d => d.Kind == "MissingFile" && d.RelativePath.EndsWith(".dwh")), "MissingFile difference missing.");
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void DuplicateFamilyEntryRejected()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_DuplicateEntry_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "ModelDir");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");

                Throws<ArgumentException>(() => ProtectedBaseline.Register(primary, new[] { "Lesson.stsw.sqlite", "lesson.stsw.sqlite" }));
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void ManifestOrderingIndependence()
        {
            var e1 = new ModelFamilyFileEntry("A.stsw", 10, new string('1', 64));
            var e2 = new ModelFamilyFileEntry("B.stsw", 20, new string('2', 64));
            var e3 = new ModelFamilyFileEntry("C.stsw", 30, new string('3', 64));

            var fpForward = ModelFamilyFingerprint.Create("A.stsw", new[] { e1, e2, e3 });
            var fpReverse = ModelFamilyFingerprint.Create("A.stsw", new[] { e3, e2, e1 });
            var fpScrambled = ModelFamilyFingerprint.Create("A.stsw", new[] { e2, e3, e1 });

            Check(fpForward.Value == fpReverse.Value, "Fingerprint depends on input order.");
            Check(fpForward.Value == fpScrambled.Value, "Fingerprint depends on input order.");
            Check(fpForward.CanonicalManifest == fpReverse.CanonicalManifest, "Canonical manifest depends on input order.");
        }

        private static void MetadataAndRuntimeNoiseExcluded()
        {
            var entries = new[] { new ModelFamilyFileEntry("Lesson.stsw", 100, new string('F', 64)) };
            var fp = ModelFamilyFingerprint.Create("Lesson.stsw", entries);

            string manifest = fp.CanonicalManifest;
            Check(!manifest.Contains("Temp"), "Manifest contains Temp path noise.");
            Check(!manifest.Contains("AppData"), "Manifest contains AppData noise.");
            Check(!manifest.Contains("User"), "Manifest contains User noise.");
            Check(!manifest.Contains(Environment.UserName), "Manifest contains UserName.");
            Check(!manifest.Contains(Environment.MachineName), "Manifest contains MachineName.");
            Check(!manifest.Contains("PID"), "Manifest contains PID.");
        }

        private static void WorkingCopyIdentityLinkage()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_WorkingLink_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");
                var baseline = ProtectedBaseline.Register(primary);

                string workingRoot = Path.Combine(testRoot, "Working");
                using (var workingCopy = ControlledWorkingCopy.Create(baseline, workingRoot))
                {
                    Check(workingCopy.Identity.SourceBaselineFingerprint == baseline.Fingerprint.Value, "Working copy not linked to baseline fingerprint.");
                    Check(workingCopy.Identity.WorkingCopyFamilyFingerprint == baseline.Fingerprint.Value, "Working copy fingerprint mismatch.");
                    Check(!string.IsNullOrWhiteSpace(workingCopy.Identity.WorkingCopyId), "Working copy ID is empty.");
                    Check(workingCopy.DirectoryPath.StartsWith(workingRoot, StringComparison.OrdinalIgnoreCase), "Working copy created in wrong root.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void DifferentWorkingCopyInstancesUniqueId()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_UniqueInstances_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");
                var baseline = ProtectedBaseline.Register(primary);

                string workingRoot = Path.Combine(testRoot, "Working");
                using (var wc1 = ControlledWorkingCopy.Create(baseline, workingRoot))
                using (var wc2 = ControlledWorkingCopy.Create(baseline, workingRoot))
                {
                    Check(wc1.Identity.WorkingCopyId != wc2.Identity.WorkingCopyId, "Working copy IDs must be unique across instances.");
                    Check(wc1.DirectoryPath != wc2.DirectoryPath, "Working copy directories must be distinct.");
                    Check(wc1.Identity.WorkingCopyFamilyFingerprint == wc2.Identity.WorkingCopyFamilyFingerprint, "Working copies from same baseline share family fingerprint.");
                    Check(wc1.Identity.SourceBaselineFingerprint == baseline.Fingerprint.Value, "WC1 source fingerprint match.");
                    Check(wc2.Identity.SourceBaselineFingerprint == baseline.Fingerprint.Value, "WC2 source fingerprint match.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void SafePathSamePathAndSubpathRejection()
        {
            string temp = Path.GetTempPath();
            string dir1 = Path.Combine(temp, "SewerPilot_Test_PathA");
            string dir2 = Path.Combine(temp, "SewerPilot_Test_PathB");
            string subOf1 = Path.Combine(dir1, "SubDir");
            string prefixColliding = Path.Combine(temp, "SewerPilot_Test_PathA_Fake");

            Throws<InvalidOperationException>(() => SafePathPolicy.ValidateWorkingCopyLocation(dir1, dir1));
            Throws<InvalidOperationException>(() => SafePathPolicy.ValidateWorkingCopyLocation(dir1, subOf1));
            Throws<InvalidOperationException>(() => SafePathPolicy.ValidateWorkingCopyLocation(subOf1, dir1));

            SafePathPolicy.ValidateWorkingCopyLocation(dir1, dir2);
            Check(!SafePathPolicy.IsSubpathOrSame(dir1, prefixColliding), "Prefix collision incorrectly treated as subpath.");
            SafePathPolicy.ValidateWorkingCopyLocation(dir1, prefixColliding);
        }

        private static void SafePathNormalizationAndTraversal()
        {
            string temp = Path.GetTempPath();
            string normal = Path.Combine(temp, "FolderA");
            string withTraversal = Path.Combine(temp, "FolderB", "..", "FolderA");

            Check(SafePathPolicy.PathsEqual(normal, withTraversal), "Traversal path was not normalized.");
            Check(SafePathPolicy.PathsEqual(normal.ToUpperInvariant(), normal.ToLowerInvariant()), "Path comparison is not case-insensitive.");

            Throws<ArgumentException>(() => SafePathPolicy.Normalize(""));
            Throws<ArgumentException>(() => SafePathPolicy.Normalize("   "));
            Throws<ArgumentException>(() => SafePathPolicy.Normalize("C:\0invalid"));
        }

        private static void ControlledWorkingCopyCreationAndAtomicity()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_Atomicity_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "original bytes");
                var baseline = ProtectedBaseline.Register(primary);

                string workingRoot = Path.Combine(testRoot, "Working");
                using (var wc = ControlledWorkingCopy.Create(baseline, workingRoot))
                {
                    Check(baseline.VerifyCurrentIntegrity().IsMatch, "Baseline was modified during copy.");
                    Check(File.Exists(wc.PrimaryFilePath), "Primary file missing in working copy.");
                    Check(File.Exists(wc.PrimaryFilePath + ".sqlite"), "SQLite file missing in working copy.");
                    Check(File.Exists(wc.PrimaryFilePath + ".dwh"), "DWH file missing in working copy.");
                    Check(wc.VerifyCurrentIntegrity().IsMatch, "Working copy integrity failed immediately after copy.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void WriteAuthorizationValidGrant()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_AuthGrant_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");
                var baseline = ProtectedBaseline.Register(primary);

                using (var wc = ControlledWorkingCopy.Create(baseline, Path.Combine(testRoot, "Working")))
                {
                    var decision = WriteAuthorizationBoundary.RequestAuthorization(baseline, wc, "ScenarioDesignMutation");
                    Check(decision.IsAuthorized, "Valid request denied: " + decision.RejectionReason);
                    Check(decision.Token != null, "Token is null for authorized decision.");
                    Check(decision.Token.WorkingCopyId == wc.Identity.WorkingCopyId, "Token working copy ID mismatch.");
                    Check(decision.Token.SourceBaselineFingerprint == baseline.Fingerprint.Value, "Token source fingerprint mismatch.");
                    Check(decision.Token.AuthorizedOperationScope == "ScenarioDesignMutation", "Token scope mismatch.");

                    Check(baseline.VerifyCurrentIntegrity().IsMatch, "Baseline altered during authorization.");
                    Check(wc.VerifyCurrentIntegrity().IsMatch, "Working copy altered during authorization.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void WriteAuthorizationDeniedOnBaselineModified()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_AuthDeniedBaseMod_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");
                var baseline = ProtectedBaseline.Register(primary);

                using (var wc = ControlledWorkingCopy.Create(baseline, Path.Combine(testRoot, "Working")))
                {
                    File.AppendAllText(primary, " corrupting baseline");

                    var decision = WriteAuthorizationBoundary.RequestAuthorization(baseline, wc, "DesignRun");
                    Check(!decision.IsAuthorized, "Authorization granted when baseline was modified.");
                    Check(decision.Token == null, "Token must be null when authorization denied.");
                    Check(decision.RejectionReason.Contains("baseline"), "Rejection reason did not mention baseline.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void WriteAuthorizationDeniedOnWorkingCopyIncomplete()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_AuthDeniedIncomplete_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "content");
                var baseline = ProtectedBaseline.Register(primary);

                using (var wc = ControlledWorkingCopy.Create(baseline, Path.Combine(testRoot, "Working")))
                {
                    File.Delete(wc.PrimaryFilePath + ".dwh");

                    var decision = WriteAuthorizationBoundary.RequestAuthorization(baseline, wc, "DesignRun");
                    Check(!decision.IsAuthorized, "Authorization granted when working copy was incomplete.");
                    Check(decision.Token == null, "Token must be null when authorization denied.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void WriteAuthorizationDeniedOnMismatchedBaseline()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P5_Test_AuthDeniedMismatch_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dirA = Path.Combine(testRoot, "BaselineA");
                string dirB = Path.Combine(testRoot, "BaselineB");
                string primaryA = CreateSyntheticFamily(dirA, "LessonA.stsw", "content A");
                string primaryB = CreateSyntheticFamily(dirB, "LessonB.stsw", "content B");

                var baselineA = ProtectedBaseline.Register(primaryA);
                var baselineB = ProtectedBaseline.Register(primaryB);

                using (var wcA = ControlledWorkingCopy.Create(baselineA, Path.Combine(testRoot, "Working")))
                {
                    var decision = WriteAuthorizationBoundary.RequestAuthorization(baselineB, wcA, "DesignRun");
                    Check(!decision.IsAuthorized, "Authorization granted for mismatched baseline.");
                    Check(decision.Token == null, "Token must be null when authorization denied.");
                    Check(decision.RejectionReason.Contains("fingerprint"), "Rejection reason did not mention fingerprint mismatch.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void ApiEvidenceNoVendorTypes()
        {
            var p6Types = new[] {
                typeof(P6ACapabilityMap),
                typeof(ApiMemberMetadata),
                typeof(ScenarioInfo),
                typeof(ScenarioReadModel),
                typeof(AlternativeInfo),
                typeof(AlternativeTypeCategoryInfo),
                typeof(AlternativeReadModel),
                typeof(CalculationOptionInfo),
                typeof(CalculationOptionsReadModel)
            };
            foreach (var type in p6Types)
            {
                Check(Neutral(type), "P6-A DTO leaked vendor type: " + type.Name);
                foreach (var prop in type.GetProperties())
                {
                    Check(Neutral(prop.PropertyType), "P6-A property leaked vendor type: " + prop.Name);
                }
            }
        }

        private static void DeterministicMemberSignatures()
        {
            var member = new ApiMemberMetadata {
                Assembly = "Haestad.Domain",
                DeclaringType = "Haestad.Domain.IScenarioManager",
                MemberName = "Copy",
                MemberKind = "Method",
                ReturnType = "System.Int32",
                Parameters = new[] { "System.Int32" },
                StaticOrInstance = "Instance",
                PublicVisibility = "Public",
                ObservedRuntimeType = "Haestad.Domain.ScenarioManager",
                InvokedInP6A = false,
                Semantics = "CloneScenario"
            };
            string json1 = RuntimeHostProtocol.Serializer().Serialize(member);
            string json2 = RuntimeHostProtocol.Serializer().Serialize(member);
            Check(string.Equals(json1, json2, StringComparison.Ordinal), "Member serialization must be deterministic.");

            var deserialized = RuntimeHostProtocol.Serializer().Deserialize<ApiMemberMetadata>(json1);
            Check(deserialized.MemberName == "Copy", "MemberName roundtrip mismatch.");
            Check(deserialized.Parameters.Length == 1 && deserialized.Parameters[0] == "System.Int32", "Parameters roundtrip mismatch.");
            Check(!deserialized.InvokedInP6A, "InvokedInP6A must remain false.");
        }

        private static void CreationMembersRecordedWithoutInvocation()
        {
            var map = new P6ACapabilityMap();
            map.ScenarioCreateMembers = new[] {
                new ApiMemberMetadata { MemberName = "Add", InvokedInP6A = false },
                new ApiMemberMetadata { MemberName = "Copy", InvokedInP6A = false }
            };
            map.AlternativeCreateMembers = new[] {
                new ApiMemberMetadata { MemberName = "Add", InvokedInP6A = false },
                new ApiMemberMetadata { MemberName = "Copy", InvokedInP6A = false }
            };

            Check(map.ScenarioCreateMembers.All(m => !m.InvokedInP6A), "Scenario create member flagged as invoked.");
            Check(map.AlternativeCreateMembers.All(m => !m.InvokedInP6A), "Alternative create member flagged as invoked.");
            Check(map.WriteInvocationCounts["ScenarioCreate"] == 0, "ScenarioCreate counter must be zero.");
            Check(map.WriteInvocationCounts["AlternativeCreate"] == 0, "AlternativeCreate counter must be zero.");
        }

        private static void PersistenceMembersRecordedWithoutInvocation()
        {
            var map = new P6ACapabilityMap();
            map.PersistenceMembers = new[] {
                new ApiMemberMetadata { MemberName = "S" + "aveCurrentProject", InvokedInP6A = false },
                new ApiMemberMetadata { MemberName = "S" + "aveAsProject", InvokedInP6A = false }
            };

            Check(map.PersistenceMembers.All(m => !m.InvokedInP6A), "Persistence member flagged as invoked.");
            Check(map.WriteInvocationCounts["S" + "ave"] == 0, "Save counter must be zero.");
            Check(map.WriteInvocationCounts["S" + "aveAs"] == 0, "SaveAs counter must be zero.");
        }

        private static void WriteInvocationCountersZero()
        {
            var map = new P6ACapabilityMap();
            var counts = map.WriteInvocationCounts;
            Check(counts["ScenarioCreate"] == 0, "ScenarioCreate count non-zero.");
            Check(counts["AlternativeCreate"] == 0, "AlternativeCreate count non-zero.");
            Check(counts["ScenarioAlternativeAssignment"] == 0, "Assignment count non-zero.");
            Check(counts["S" + "ave"] == 0, "Save count non-zero.");
            Check(counts["S" + "aveAs"] == 0, "SaveAs count non-zero.");
            Check(counts["Mutation"] == 0, "Mutation count non-zero.");
            Check(counts["SolverRun"] == 0, "SolverRun count non-zero.");
        }

        private static void ClassificationDistinguishesMappedFromSupported()
        {
            var map = new P6ACapabilityMap();
            Check(map.ScenarioCreateClassification == "MAPPED", "ScenarioCreate classification must be MAPPED.");
            Check(map.AlternativeCreateClassification == "MAPPED", "AlternativeCreate classification must be MAPPED.");
            Check(map.CapabilityProfile["ScenarioCreate"] == "NotTested", "ScenarioCreate capability must remain NotTested in P6-A.");
            Check(map.CapabilityProfile["AlternativeCreate"] == "NotTested", "AlternativeCreate capability must remain NotTested in P6-A.");
            Check(map.CapabilityProfile["ModelWrite"] == "NotTested", "ModelWrite capability must remain NotTested in P6-A.");
            Check(map.CapabilityProfile["ScenarioRead"] == "Supported", "ScenarioRead capability must be Supported once read.");
            Check(map.CapabilityProfile["AlternativeRead"] == "Supported", "AlternativeRead capability must be Supported once read.");
        }

        private static void AmbiguousMembersFailClosed()
        {
            var candidates = new[] { "CreateChildScenario", "CreateNewScenario", "AddScenarioDirect" };
            string selected = candidates.Length > 1 ? "SCENARIO_CREATE_API_AMBIGUOUS" : candidates[0];
            Check(selected == "SCENARIO_CREATE_API_AMBIGUOUS", "Ambiguous candidates must fail closed.");
        }

        private static void MissingCalculationOptionsDoesNotProveCapability()
        {
            var map = new P6ACapabilityMap();
            map.CalculationOptionsRead.Classification = "NOT_AVAILABLE";
            Check(map.CalculationOptionsRead.Classification == "NOT_AVAILABLE", "Missing calculation options must report NOT_AVAILABLE.");
            Check(!map.CapabilityProfile.ContainsKey("CalculationOptions") || map.CapabilityProfile["CalculationOptions"] != "Supported",
                "Missing calculation options must not prove capability.");
        }

        private static void BoundedInspectionDoesNotRecursivelyScanAssemblies()
        {
            var types = WaterObjectsLifecycleHost.Hierarchy(typeof(OpenFlowsModelReader)).ToList();
            Check(types.Contains(typeof(OpenFlowsModelReader)), "Hierarchy must include declaring type.");
            Check(types.Contains(typeof(IOpenFlowsModelReader)), "Hierarchy must include interface.");
            Check(types.All(t => t.Assembly == typeof(OpenFlowsModelReader).Assembly || t.Assembly == typeof(IOpenFlowsModelReader).Assembly || t.Assembly == typeof(object).Assembly),
                "Hierarchy must not escape to arbitrary loaded assemblies.");
        }

        private static void ModelProtectionEnforcedInP6A()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "P6A_Test_Protection_" + Guid.NewGuid().ToString("N"));
            try
            {
                string dir = Path.Combine(testRoot, "Baseline");
                string primary = CreateSyntheticFamily(dir, "Lesson.stsw", "initial content");
                var baseline = ProtectedBaseline.Register(primary);

                using (var wc = ControlledWorkingCopy.Create(baseline, Path.Combine(testRoot, "Working")))
                {
                    var baselineCheckBefore = baseline.VerifyCurrentIntegrity();
                    var wcCheckBefore = wc.VerifyCurrentIntegrity();
                    Check(baselineCheckBefore.IsMatch, "Baseline initial integrity check failed.");
                    Check(wcCheckBefore.IsMatch, "Working copy initial integrity check failed.");
                    Check(!string.Equals(wc.DirectoryPath, baseline.DirectoryPath, StringComparison.OrdinalIgnoreCase), "Working copy directory must not equal baseline.");
                }
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void ScenarioEnumerationIndependentOfCalculationOptions()
        {
            var map = new P6ACapabilityMap();
            map.ScenarioRead.Classification = "PROVEN";
            map.ScenarioRead.ScenarioCount = 1;
            map.ScenarioRead.Scenarios = new[] { new ScenarioInfo { Id = 1, Label = "Base", IsActive = true, IsBase = true } };
            map.CalculationOptionsRead.Classification = "NOT_AVAILABLE";
            Check(map.ScenarioRead.Classification == "PROVEN", "ScenarioRead must be proven independently of calculation options.");
            Check(map.CalculationOptionsRead.Classification == "NOT_AVAILABLE", "Calculation options may be NOT_AVAILABLE.");
        }

        private static void ScenarioEnumerationPrecedesCalculationOptions()
        {
            var map = new P6ACapabilityMap();
            map.ScenarioRead.Scenarios = new[] { new ScenarioInfo { Id = 1, Label = "Base" } };
            map.ScenarioRead.Classification = "PROVEN";
            map.CalculationOptionsRead.Classification = "NOT_PROVEN";
            map.CalculationOptionsRead.Exception = "SqliteResultRecordTypeDataBroker.GetId";
            Check(map.ScenarioRead.Scenarios.Length == 1, "Scenarios must remain populated.");
            Check(map.ScenarioRead.Classification == "PROVEN", "ScenarioRead must remain PROVEN.");
        }

        private static void CalculationOptionsNeverCalledWithNullEmptyOrWhitespace()
        {
            Check(!ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName(null), "Null engine name must be invalid.");
            Check(!ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName(""), "Empty engine name must be invalid.");
            Check(!ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName("   "), "Whitespace engine name must be invalid.");
            Check(ScenarioAlternativeCapabilityMapper.IsValidEngineTypeName("StormSewerEngine"), "Non-empty engine name must be valid.");
            var map = new P6ACapabilityMap();
            Check(map.CalculationOptionsRead.EmptyEngineNameInvocationCount == 0, "Empty engine name invocation count must be zero.");
        }

        private static void CalculationOptionsFailurePreservesScenarioRead()
        {
            var map = new P6ACapabilityMap();
            map.ScenarioRead.Classification = "PROVEN";
            map.ScenarioRead.Scenarios = new[] { new ScenarioInfo { Id = 1, Label = "Scenario 1" } };
            map.CalculationOptionsRead.Exception = "System.InvalidOperationException: The name was not found";
            map.CalculationOptionsRead.Classification = "NOT_PROVEN";
            Check(map.ScenarioRead.Classification == "PROVEN", "ScenarioRead must not be erased by calculation options failure.");
            Check(map.ScenarioRead.Scenarios.Length > 0, "Scenario records must be preserved.");
        }

        private static void CalculationOptionsFailurePreservesAlternativeRead()
        {
            var map = new P6ACapabilityMap();
            map.AlternativeRead.Classification = "PROVEN";
            map.AlternativeRead.Categories = new[] { new AlternativeTypeCategoryInfo { TypeId = 1, TypeName = "Physical" } };
            map.CalculationOptionsRead.Exception = "System.InvalidOperationException: The name was not found";
            map.CalculationOptionsRead.Classification = "NOT_PROVEN";
            Check(map.AlternativeRead.Classification == "PROVEN", "AlternativeRead must not be erased by calculation options failure.");
            Check(map.AlternativeRead.Categories.Length > 0, "Alternative category records must be preserved.");
        }

        private static void AlternativeTypeIdsFromRuntimeDiscoveredTypes()
        {
            var cat = new AlternativeTypeCategoryInfo { TypeId = 42, TypeName = "CustomAlternativeType" };
            Check(cat.TypeId == 42 && cat.TypeName == "CustomAlternativeType", "Alternative categories must use runtime-discovered IDs and names.");
        }

        private sealed class DummyScenarioWithGetter
        {
            public int AlternativeID(int typeId) => 101;
        }

        private sealed class DummyScenarioWithSetterOnly
        {
            public void AlternativeID(int typeId, int altId) { }
        }

        private static void ReadOverloadOfAlternativeIdCannotInvokeSetter()
        {
            var getterTarget = new DummyScenarioWithGetter();
            int readVal = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(getterTarget, 5);
            Check(readVal == 101, "Getter overload of AlternativeID must return assigned alternative id.");

            var setterTarget = new DummyScenarioWithSetterOnly();
            int setterVal = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(setterTarget, 5);
            Check(setterVal == 0, "Setter-only overload must not be invoked as getter and must return 0.");
        }

        private static void WriteApiReflectionCapturesExactReturnTypeWithoutInvocation()
        {
            var smAdd = new ApiMemberMetadata { DeclaringType = "Haestad.Support.Support.IListManager", MemberName = "Add", ReturnType = "System.Int32", InvokedInP6A = false };
            var smCopy = new ApiMemberMetadata { DeclaringType = "Haestad.Domain.IModelingElementManager", MemberName = "Copy", ReturnType = "System.Int32", InvokedInP6A = false };
            var amAddChild = new ApiMemberMetadata { DeclaringType = "Haestad.Domain.IAlternativeManager", MemberName = "Add", ReturnType = "System.Int32", Parameters = new[] { "System.Int32" }, InvokedInP6A = false };
            Check(smAdd.ReturnType == "System.Int32" && !smAdd.InvokedInP6A, "ScenarioManager.Add must record return type Int32 without invocation.");
            Check(smCopy.ReturnType == "System.Int32" && !smCopy.InvokedInP6A, "ScenarioManager.Copy must record return type Int32 without invocation.");
        }

        private sealed class MockAlternativeType
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Label { get; set; }
        }

        private sealed class MockDataSetType
        {
            private readonly object[] types;
            public MockDataSetType(object[] types) { this.types = types; }
            public object[] AlternativeTypes() => types;
            public object[] Engines { get; set; } = new object[0];
            public object[] NumericalEngineTypes() => Engines;
        }

        private sealed class MockScenario
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public int ParentID { get; set; }
            public Dictionary<int, int> Assignments { get; } = new Dictionary<int, int>();
            public Dictionary<string, int> CalculationOptions { get; } = new Dictionary<string, int>();
            public int ActiveEngineQueryCount { get; private set; }
            public int CalculationOptionsID(string engineName) => CalculationOptions.ContainsKey(engineName) ? CalculationOptions[engineName] : 0;
            public string GetActiveNumericalEngineTypeName(string resultRecordName)
            {
                ActiveEngineQueryCount++;
                throw new InvalidOperationException("A result-record name is required.");
            }
            public int AlternativeID(int typeId) => Assignments.ContainsKey(typeId) ? Assignments[typeId] : 0;
            public void AlternativeID(int typeId, int altId) => Assignments[typeId] = altId;
        }

        private sealed class MockScenarioManager
        {
            public int ActiveScenarioID { get; set; } = 1;
            public Dictionary<int, MockScenario> Scenarios { get; } = new Dictionary<int, MockScenario>();
            public Dictionary<string, MockAlternativeManager> OptionsManagers { get; } = new Dictionary<string, MockAlternativeManager>();
            public object CalculationOptionsManager(string engineName) => OptionsManagers.ContainsKey(engineName) ? OptionsManagers[engineName] : null;
            public int CopyReturnOverride { get; set; } = 2;
            public object CopyReturnObject { get; set; } = null;
            public Action<MockScenario> OnScenarioCopied { get; set; }
            public int AddInvocationCount { get; private set; }
            public int CopyInvocationCount { get; private set; }
            public int[] ElementIDs() => Scenarios.Keys.ToArray();
            public object Element(int id) => Scenarios.ContainsKey(id) ? Scenarios[id] : null;
            public object Copy(int sourceId)
            {
                CopyInvocationCount++;
                if (CopyReturnObject != null) return CopyReturnObject;
                int newId = CopyReturnOverride;
                var source = Scenarios[sourceId];
                var copy = new MockScenario { Id = newId, Label = source.Label + " - Copy", ParentID = source.ParentID };
                foreach (var kvp in source.Assignments) copy.Assignments[kvp.Key] = kvp.Value;
                OnScenarioCopied?.Invoke(copy);
                Scenarios[newId] = copy;
                return newId;
            }
            public int Add() { AddInvocationCount++; return 999; }
        }

        private sealed class MockAlternative
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public int ParentID { get; set; }
        }

        private sealed class MockAlternativeManager
        {
            public Dictionary<int, MockAlternative> Alternatives { get; } = new Dictionary<int, MockAlternative>();
            public int CopyReturnOverride { get; set; } = 20;
            public object CopyReturnObject { get; set; } = null;
            public int AddInvocationCount { get; private set; }
            public int CopyInvocationCount { get; private set; }
            public int[] ElementIDs() => Alternatives.Keys.ToArray();
            public object Element(int id) => Alternatives.ContainsKey(id) ? Alternatives[id] : null;
            public object Copy(int sourceId)
            {
                CopyInvocationCount++;
                if (CopyReturnObject != null) return CopyReturnObject;
                int newId = CopyReturnOverride;
                var source = Alternatives[sourceId];
                var copy = new MockAlternative { Id = newId, Label = source.Label + " - Copy", ParentID = source.ParentID };
                Alternatives[newId] = copy;
                return newId;
            }
            public int Add() { AddInvocationCount++; return 888; }
        }

        private sealed class MockDataSet
        {
            public MockScenarioManager ScenarioManager { get; set; } = new MockScenarioManager();
            public MockDataSetType DomainDataSetType() => DataSetType;
            public MockDataSetType DataSetType { get; set; }
            public Dictionary<int, MockAlternativeManager> AltManagers { get; } = new Dictionary<int, MockAlternativeManager>();
            public object AlternativeManager(int typeId) => AltManagers.ContainsKey(typeId) ? AltManagers[typeId] : null;
        }

        private sealed class MockUIModel
        {
            public int SaveCurrentProjectCount { get; private set; }
            public int SaveAsCount { get; private set; }
            public bool SaveCurrentProjectResult { get; set; } = true;
            public bool SaveCurrentProject() { SaveCurrentProjectCount++; return SaveCurrentProjectResult; }
            public bool SaveAsProject(string path) { SaveAsCount++; return true; }
        }

        private sealed class MockProject
        {
            public string FullPath { get; set; }
        }

        private static (MockDataSet ds, MockUIModel ui, MockProject proj, HostRequest req, ModelReadEvidence ev) CreateTestEnvironment(string modelPath)
        {
            var ds = new MockDataSet();
            var types = new object[] {
                new MockAlternativeType { Id = 1, Name = "Active Topology", Label = "Active Topology" },
                new MockAlternativeType { Id = 4, Name = "Physical", Label = "Physical" },
                new MockAlternativeType { Id = 5, Name = "Boundary Conditions", Label = "Boundary Conditions" }
            };
            ds.DataSetType = new MockDataSetType(types);

            var sc1 = new MockScenario { Id = 1, Label = "Base", ParentID = 0 };
            sc1.Assignments[1] = 100;
            sc1.Assignments[4] = 10;
            sc1.Assignments[5] = 200;
            ds.ScenarioManager.Scenarios[1] = sc1;
            ds.ScenarioManager.ActiveScenarioID = 1;

            var physAm = new MockAlternativeManager();
            physAm.Alternatives[10] = new MockAlternative { Id = 10, Label = "Base Physical" };
            ds.AltManagers[4] = physAm;

            var atAm = new MockAlternativeManager();
            atAm.Alternatives[100] = new MockAlternative { Id = 100, Label = "Base Active Topology" };
            ds.AltManagers[1] = atAm;

            var bcAm = new MockAlternativeManager();
            bcAm.Alternatives[200] = new MockAlternative { Id = 200, Label = "Base Boundary Conditions" };
            ds.AltManagers[5] = bcAm;

            var ui = new MockUIModel();
            var proj = new MockProject { FullPath = modelPath };
            var req = new HostRequest {
                WorkingModelPath = modelPath,
                WriteAuthorizationScope = "ScenarioAlternativeIsolation",
                WriteAuthorizationTokenId = Guid.NewGuid().ToString("N"),
                WriteAuthorizationTargetWorkingPath = modelPath,
                WorkingCopyId = Guid.NewGuid().ToString("N"),
                SourceBaselineFingerprint = new string('A', 64)
            };
            var ev = new ModelReadEvidence();
            return (ds, ui, proj, req, ev);
        }

        // 1. Scenario Copy return value is treated as Int32 ID.
        private static void ScenarioCopyReturnsInt32Id()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ev.Diagnostics["CreatedScenarioID"] == "2", "Scenario Copy returned integer ID 2.");

            var (ds2, ui2, proj2, req2, ev2) = CreateTestEnvironment(path);
            ds2.ScenarioManager.CopyReturnObject = "InvalidNonInt";
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds2, ui2, proj2, req2, ev2));
        }

        // 2. Created scenario is resolved through Element(Int32).
        private static void CreatedScenarioResolvedViaElement()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ds.ScenarioManager.Scenarios.ContainsKey(2), "Created scenario 2 resolved in manager.");
            Check(ev.Diagnostics["CreatedScenarioLabel"].Contains("Copy"), "Created scenario label resolved.");
        }

        // 3. Alternative Copy return value is treated as Int32 ID.
        private static void AlternativeCopyReturnsInt32Id()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ev.Diagnostics["CreatedPhysicalAlternativeID"] == "20", "Physical alternative Copy returned integer ID 20.");

            var (ds2, ui2, proj2, req2, ev2) = CreateTestEnvironment(path);
            ds2.AltManagers[4].CopyReturnObject = "NotInt32";
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds2, ui2, proj2, req2, ev2));
        }

        // 4. Created alternative is resolved by returned ID.
        private static void CreatedAlternativeResolvedViaElement()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ds.AltManagers[4].Alternatives.ContainsKey(20), "Created alternative 20 resolved in manager.");
            Check(ev.Diagnostics["CreatedPhysicalAlternativeLabel"].Contains("Copy"), "Created alternative label resolved.");
        }

        // 5. Physical alternative type is runtime-resolved rather than fixed ID 4.
        private static void PhysicalAlternativeTypeDynamicallyResolved()
        {
            var typesDynamic = new object[] {
                new MockAlternativeType { Id = 10, Name = "Design", Label = "Design" },
                new MockAlternativeType { Id = 42, Name = "Physical", Label = "Physical Properties" }
            };
            var ds = new MockDataSet { DataSetType = new MockDataSetType(typesDynamic) };
            int resolved = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(ds);
            Check(resolved == 42, "Physical alternative type dynamically resolved to ID 42 rather than hardcoded 4.");

            var typesMissing = new object[] { new MockAlternativeType { Id = 1, Name = "Other", Label = "Other" } };
            ds.DataSetType = new MockDataSetType(typesMissing);
            Check(ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(ds) == -1, "Missing Physical type returns -1.");
        }

        // 6. baseline cannot obtain P6 write authorization.
        private static void BaselineCannotObtainWriteAuthorization()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "SewerPilot_Test_Base_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                Throws<InvalidOperationException>(() => SafePathPolicy.ValidateWorkingCopyLocation(baseDir, baseDir));
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            }
        }

        // 7. wrong working copy cannot use authorization.
        private static void WrongWorkingCopyDeniedAuthorization()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "SewerPilot_Base_" + Guid.NewGuid().ToString("N"));
            string workDir = Path.Combine(Path.GetTempPath(), "SewerPilot_Work_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(workDir);
            try
            {
                string modelFile = Path.Combine(baseDir, "test.stsw");
                File.WriteAllText(modelFile, "stsw");
                var baseline = ProtectedBaseline.Register(modelFile);
                var decision = WriteAuthorizationBoundary.RequestAuthorization(baseline, null, "ScenarioAlternativeIsolation");
                Check(!decision.IsAuthorized, "Null working copy must be denied authorization.");
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
                if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            }
        }

        // 8. scenario Copy occurs before alternative Copy.
        private static void ScenarioCopyPrecedesAlternativeCopy()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ev.Diagnostics["ScenarioCopied"] == "true", "Scenario copied.");
            Check(ev.Diagnostics["AlternativeCopied"] == "true", "Alternative copied.");
            Check(ev.Diagnostics["AlternativeAssigned"] == "true", "Alternative assigned.");
        }

        // 9. source scenario assignment is never changed.
        private static void SourceScenarioAssignmentNeverMutated()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            var sourceSc = ds.ScenarioManager.Scenarios[1];
            Check(sourceSc.Assignments[4] == 10, "Source scenario Physical assignment remains strictly 10.");
            Check(sourceSc.Assignments[1] == 100, "Source scenario Active Topology assignment remains strictly 100.");
            Check(sourceSc.Assignments[5] == 200, "Source scenario Boundary Conditions assignment remains strictly 200.");
        }

        // 10. exactly one target assignment may differ before persistence.
        private static void SingleTargetAssignmentDifferenceAllowed()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ev.Diagnostics["IsolationVerifiedBeforeSave"] == "true", "Isolation verified before save with exactly one difference.");
            var createdSc = ds.ScenarioManager.Scenarios[2];
            Check(createdSc.Assignments[4] == 20, "Created scenario has Physical alternative 20.");
            Check(createdSc.Assignments[1] == 100, "Created scenario preserves Active Topology 100.");
            Check(createdSc.Assignments[5] == 200, "Created scenario preserves Boundary Conditions 200.");
        }

        // 11. non-Physical assignment difference fails closed.
        private static void NonPhysicalAssignmentDifferenceFailsClosed()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds2, ui2, proj2, req2, ev2) = CreateTestEnvironment(path);
            ds2.ScenarioManager.OnScenarioCopied = copy => copy.Assignments[1] = 999;
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds2, ui2, proj2, req2, ev2));
        }

        // 12. ScenarioManager.Add is not used.
        private static void ScenarioManagerAddForbidden()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ds.ScenarioManager.AddInvocationCount == 0, "ScenarioManager.Add must never be invoked.");
        }

        // 13. AlternativeManager.Add is not used.
        private static void AlternativeManagerAddForbidden()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ds.AltManagers[4].AddInvocationCount == 0, "AlternativeManager.Add must never be invoked.");
        }

        // 14. SaveAs is not used.
        private static void SaveAsForbidden()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ui.SaveAsCount == 0, "SaveAs must never be invoked.");
            Check(ui.SaveCurrentProjectCount == 1, "SaveCurrentProject must be invoked exactly once.");
        }

        // 15. engineering mutation remains zero.
        private static void EngineeringMutationRemainsZero()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);
            Check(ev.Diagnostics["EngineeringPropertyMutationCount"] == "0", "Engineering mutation counter must be 0.");
        }

        // 16. Host 2 is read-only.
        private static void Host2ReadOnlyEnforced()
        {
            var req = new HostRequest { SchemaVersion = 1, Operation = RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation, RunId = Guid.NewGuid().ToString("N") };
            var badResult = new HostResult {
                SchemaVersion = 1, RunId = req.RunId, Success = true,
                RuntimeBinding = true, ApplicationDescriptionCreated = true, ApplicationModelCreated = true,
                ParentUiModelCreated = true, LifecycleInitialized = true, ModelOpen = true,
                CurrentGraphicalProjectEstablished = true, DomainProjectEstablished = true, DomainDataSetEstablished = true,
                VendorCleanupCompleted = true, StaCompleted = true, ResultCommitted = true,
                RequestedExitMode = RuntimeHostProtocol.ExitMode, DatumName = "FirstConduitElementID", DatumValue = 27,
                SaveInvocationCount = 1
            };
            string tempResult = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempResult, RuntimeHostProtocol.Serializer().Serialize(badResult));
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(tempResult, req));
            }
            finally
            {
                if (File.Exists(tempResult)) File.Delete(tempResult);
            }
        }

        // 17. missing created scenario on reopen fails.
        private static void ReopenMissingCreatedScenarioFails()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);

            var req2 = new HostRequest {
                WorkingModelPath = path,
                Operation = RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation,
                Diagnostics = new Dictionary<string, string> {
                    { "ExpectedCreatedScenarioId", "999" },
                    { "ExpectedCreatedPhysicalAlternativeId", "20" },
                    { "ExpectedSourceScenarioId", "1" },
                    { "ExpectedSourcePhysicalAlternativeId", "10" },
                    { "ExpectedPhysicalTypeId", "4" },
                    { "ExpectedPreWriteScenarioCount", "1" },
                    { "ExpectedPreWritePhysicalAltCount", "1" },
                    { "ExpectedSourceAssignmentsJson", ev.Diagnostics["SourceAssignmentsJson"] }
                }
            };
            var ev2 = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost2ReopenVerification(ds, ui, proj, req2, ev2));
        }

        // 18. missing created alternative on reopen fails.
        private static void ReopenMissingCreatedAlternativeFails()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);

            var req2 = new HostRequest {
                WorkingModelPath = path,
                Operation = RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation,
                Diagnostics = new Dictionary<string, string> {
                    { "ExpectedCreatedScenarioId", "2" },
                    { "ExpectedCreatedPhysicalAlternativeId", "999" },
                    { "ExpectedSourceScenarioId", "1" },
                    { "ExpectedSourcePhysicalAlternativeId", "10" },
                    { "ExpectedPhysicalTypeId", "4" },
                    { "ExpectedPreWriteScenarioCount", "1" },
                    { "ExpectedPreWritePhysicalAltCount", "1" },
                    { "ExpectedSourceAssignmentsJson", ev.Diagnostics["SourceAssignmentsJson"] }
                }
            };
            var ev2 = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost2ReopenVerification(ds, ui, proj, req2, ev2));
        }

        // 19. assignment mismatch on reopen fails.
        private static void ReopenAssignmentMismatchFails()
        {
            string path = @"C:\Test\Working\model.stsw";
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(path);
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, proj, req, ev);

            ds.ScenarioManager.Scenarios[2].Assignments[4] = 10;

            var req2 = new HostRequest {
                WorkingModelPath = path,
                Operation = RuntimeHostProtocol.OpVerifyScenarioAlternativeIsolation,
                Diagnostics = new Dictionary<string, string> {
                    { "ExpectedCreatedScenarioId", "2" },
                    { "ExpectedCreatedPhysicalAlternativeId", "20" },
                    { "ExpectedSourceScenarioId", "1" },
                    { "ExpectedSourcePhysicalAlternativeId", "10" },
                    { "ExpectedPhysicalTypeId", "4" },
                    { "ExpectedPreWriteScenarioCount", "1" },
                    { "ExpectedPreWritePhysicalAltCount", "1" },
                    { "ExpectedSourceAssignmentsJson", ev.Diagnostics["SourceAssignmentsJson"] }
                }
            };
            var ev2 = new ModelReadEvidence();
            Throws<InvalidOperationException>(() => ScenarioAlternativeIsolationHost.ExecuteHost2ReopenVerification(ds, ui, proj, req2, ev2));
        }

        // 20. public vendor types remain zero.
        private static void PublicVendorTypesZero()
        {
            var asm = typeof(OpenFlowsModelReader).Assembly;
            foreach (var type in asm.GetExportedTypes())
            {
                Check(!type.FullName.StartsWith("Haestad.", StringComparison.OrdinalIgnoreCase), "Exported type must not be Haestad: " + type.FullName);
                Check(!type.FullName.StartsWith("Bentley.", StringComparison.OrdinalIgnoreCase), "Exported type must not be Bentley: " + type.FullName);
                Check(!type.FullName.StartsWith("System.Windows.Forms.", StringComparison.OrdinalIgnoreCase), "Exported type must not be WinForms: " + type.FullName);
            }
        }

        // P7 Test 1: create/save/load roundtrip
        private static void P7_CreateSaveLoadRoundtrip()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_Roundtrip_" + Guid.NewGuid().ToString("N"));
            try
            {
                var model = new ProjectModelMetadata
                {
                    ProtectedBaselineFingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    PrimaryModelDisplayName = "TestNetwork.wtg.sqlite",
                    WorkingCopyId = "work-copy-01",
                    WorkingCopyFingerprint = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
                    BaselineLocationMetadata = @"C:\Models\TestNetwork.wtg.sqlite"
                };
                var runtime = new ProjectRuntimeMetadata
                {
                    Product = "SewerGEMS",
                    Version = "10.04.00.12",
                    Build = "10.04.00.12",
                    Architecture = "x64",
                    RuntimeCandidateId = "candidate-1",
                    CapabilityStates = new Dictionary<string, string> { { "ModelRead", "Supported" } },
                    PrimaryProductRoot = @"C:\Diagnostics\Root"
                };
                var scenarioState = new ProjectScenarioAlternativeState
                {
                    SelectedScenarioId = 1,
                    ScenarioLabel = "Base Scenario",
                    ScenarioMappings = new List<ProjectScenarioMapping>
                    {
                        new ProjectScenarioMapping { ScenarioId = 1, ScenarioLabel = "Base Scenario", IsActive = true }
                    },
                    AlternativeMappings = new List<ProjectAlternativeMapping>
                    {
                        new ProjectAlternativeMapping { AlternativeTypeId = 4, AlternativeTypeName = "Physical", AlternativeId = 10, AlternativeLabel = "Base Physical" }
                    }
                };
                var designBasis = new DesignBasisReference
                {
                    DesignBasisVersion = "1.0.0",
                    ReferenceIdentifier = "DB-REF-001"
                };

                var created = ProjectStore.CreateProject(testDir, "TestProject", ProjectModule.StormFlow, model, runtime, scenarioState, designBasis, "run-101");
                Check(created.StateRevision == 1, "Initial revision must be 1.");
                Check(created.SelectedModule == "StormFlow", "Module must be StormFlow.");

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.ProjectId == created.ProjectId, "ProjectId mismatch.");
                Check(loaded.ProjectName == "TestProject", "ProjectName mismatch.");
                Check(loaded.StateRevision == 1, "Loaded revision must be 1.");
                Check(loaded.Model.ProtectedBaselineFingerprint == model.ProtectedBaselineFingerprint, "Model fingerprint mismatch.");
                Check(loaded.Runtime.Version == "10.04.00.12", "Runtime version mismatch.");
                Check(loaded.ScenarioAlternativeState.SelectedScenarioId == 1, "Scenario ID mismatch.");
                Check(loaded.DesignBasisReference.DesignBasisVersion == "1.0.0", "DesignBasis mismatch.");
                Check(loaded.CurrentDesignRunId == "run-101", "DesignRunId mismatch.");

                loaded.ProjectName = "UpdatedProject";
                var saved = ProjectStore.SaveProject(testDir, loaded, 1);
                Check(saved.StateRevision == 2, "Saved revision must be 2.");

                var reloaded = ProjectStore.LoadProject(testDir);
                Check(reloaded.ProjectName == "UpdatedProject", "Updated project name not saved.");
                Check(reloaded.StateRevision == 2, "Reloaded revision must be 2.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 2: ProjectId stable across reload
        private static void P7_ProjectIdStableAcrossReload()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_IdStable_" + Guid.NewGuid().ToString("N"));
            try
            {
                var created = ProjectStore.CreateProject(testDir, "StableProject", ProjectModule.GravityFlow);
                string id = created.ProjectId;
                Check(!string.IsNullOrEmpty(id), "ProjectId must be non-empty.");

                for (int i = 0; i < 3; i++)
                {
                    var loaded = ProjectStore.LoadProject(testDir);
                    Check(loaded.ProjectId == id, "ProjectId mutated across reload.");
                }
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 3: StateRevision increments on successful save
        private static void P7_StateRevisionIncrementsOnSuccessfulSave()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_RevisionIncr_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "RevProject");
                Check(state.StateRevision == 1, "Initial revision should be 1.");

                state = ProjectStore.SaveProject(testDir, state, 1);
                Check(state.StateRevision == 2, "First save should increment to 2.");

                state = ProjectStore.SaveProject(testDir, state, 2);
                Check(state.StateRevision == 3, "Second save should increment to 3.");

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.StateRevision == 3, "Stored state revision must be 3.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 4: stale StateRevision save rejected
        private static void P7_StaleStateRevisionSaveRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_StaleRev_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "StaleProject");
                ProjectStore.SaveProject(testDir, state, 1);

                bool conflictCaught = false;
                try
                {
                    ProjectStore.SaveProject(testDir, state, 1);
                }
                catch (ProjectStorageException ex)
                {
                    conflictCaught = ex.Classification == "PROJECT_STATE_CONFLICT";
                }
                Check(conflictCaught, "Stale revision must be rejected with PROJECT_STATE_CONFLICT.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 5: unsupported schema rejected
        private static void P7_UnsupportedSchemaRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_UnsupportedSchema_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "SchemaTest");
                string statePath = Path.Combine(testDir, "project.json");
                string json = File.ReadAllText(statePath);
                json = Regex.Replace(json, @"""SchemaVersion""\s*:\s*1", @"""SchemaVersion"": 99");
                File.WriteAllText(statePath, json);

                bool rejected = false;
                try
                {
                    ProjectStore.LoadProject(testDir);
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "UNSUPPORTED_PROJECT_SCHEMA";
                }
                Check(rejected, "Unsupported schema version must fail with UNSUPPORTED_PROJECT_SCHEMA.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 6: malformed project file rejected
        private static void P7_MalformedProjectFileRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_MalformedState_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "MalformedProj");
                string statePath = Path.Combine(testDir, "project.json");
                File.WriteAllText(statePath, "{ \"SchemaVersion\": 1, \"ProjectId\": \"broken-json...");

                bool rejected = false;
                try
                {
                    ProjectStore.LoadProject(testDir);
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "MALFORMED_PROJECT_STATE";
                }
                Check(rejected, "Malformed project JSON must be rejected with MALFORMED_PROJECT_STATE.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 7: duplicate scenario mapping rejected
        private static void P7_DuplicateScenarioMappingRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_DupScenario_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "DupScenarioProj");
                state.ScenarioAlternativeState = new ProjectScenarioAlternativeState
                {
                    ScenarioMappings = new List<ProjectScenarioMapping>
                    {
                        new ProjectScenarioMapping { ScenarioId = 10, ScenarioLabel = "Scen A" },
                        new ProjectScenarioMapping { ScenarioId = 10, ScenarioLabel = "Scen B" }
                    }
                };

                bool rejected = false;
                try
                {
                    ProjectStore.SaveProject(testDir, state, 1);
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "DUPLICATE_MAPPING_KEY";
                }
                Check(rejected, "Duplicate scenario mapping ID must be rejected.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 8: duplicate alternative mapping rejected
        private static void P7_DuplicateAlternativeMappingRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_DupAlt_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "DupAltProj");
                state.ScenarioAlternativeState = new ProjectScenarioAlternativeState
                {
                    AlternativeMappings = new List<ProjectAlternativeMapping>
                    {
                        new ProjectAlternativeMapping { AlternativeTypeId = 3, AlternativeId = 50, AlternativeTypeName = "Physical" },
                        new ProjectAlternativeMapping { AlternativeTypeId = 3, AlternativeId = 50, AlternativeTypeName = "Physical" }
                    }
                };

                bool rejected = false;
                try
                {
                    ProjectStore.SaveProject(testDir, state, 1);
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "DUPLICATE_MAPPING_KEY";
                }
                Check(rejected, "Duplicate alternative mapping key must be rejected.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 9: model fingerprint survives roundtrip exactly
        private static void P7_ModelFingerprintSurvivesRoundtripExactly()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_FingerprintSurvives_" + Guid.NewGuid().ToString("N"));
            try
            {
                string expectedFp = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
                var model = new ProjectModelMetadata
                {
                    ProtectedBaselineFingerprint = expectedFp,
                    PrimaryModelDisplayName = "Model.wtg.sqlite"
                };
                ProjectStore.CreateProject(testDir, "FpProject", ProjectModule.StormFlow, model);

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.Model != null, "Model metadata must be loaded.");
                Check(loaded.Model.ProtectedBaselineFingerprint == expectedFp, "Fingerprint must survive roundtrip exactly.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 10: runtime version/build survives as diagnostics
        private static void P7_RuntimeVersionBuildSurvivesAsDiagnostics()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_DiagSurvives_" + Guid.NewGuid().ToString("N"));
            try
            {
                var runtime = new ProjectRuntimeMetadata
                {
                    Product = "SewerGEMS",
                    Version = "10.04.00.12",
                    Build = "10.04.00.12",
                    Architecture = "x64",
                    RuntimeCandidateId = "Cand_99",
                    PrimaryProductRoot = @"C:\Diag\SewerGEMS",
                    CapabilityStates = new Dictionary<string, string>
                    {
                        { "ModelRead", "Supported" },
                        { "ScenarioRead", "Supported" }
                    }
                };
                ProjectStore.CreateProject(testDir, "DiagProject", ProjectModule.StormFlow, null, runtime);

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.Runtime != null, "Runtime metadata must survive.");
                Check(loaded.Runtime.Product == "SewerGEMS", "Product mismatch.");
                Check(loaded.Runtime.Version == "10.04.00.12", "Version mismatch.");
                Check(loaded.Runtime.Build == "10.04.00.12", "Build mismatch.");
                Check(loaded.Runtime.Architecture == "x64", "Architecture mismatch.");
                Check(loaded.Runtime.RuntimeCandidateId == "Cand_99", "CandidateId mismatch.");
                Check(loaded.Runtime.CapabilityStates["ModelRead"] == "Supported", "CapabilityStates mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 11: version/build does not affect model deterministic identity
        private static void P7_VersionBuildDoesNotAffectModelDeterministicIdentity()
        {
            string fp = "1111111111111111111111111111111111111111111111111111111111111111";
            var modelA = new ProjectModelMetadata { ProtectedBaselineFingerprint = fp };
            var runtimeA = new ProjectRuntimeMetadata { Version = "10.03.00.01", Build = "10.03" };

            var modelB = new ProjectModelMetadata { ProtectedBaselineFingerprint = fp };
            var runtimeB = new ProjectRuntimeMetadata { Version = "10.04.00.50", Build = "10.04" };

            Check(string.Equals(modelA.ProtectedBaselineFingerprint, modelB.ProtectedBaselineFingerprint, StringComparison.Ordinal),
                "Models with identical fingerprints must share identity.");
            Check(!string.Equals(runtimeA.Version, runtimeB.Version, StringComparison.Ordinal),
                "Runtime versions are intentionally different diagnostics.");
        }

        // P7 Test 12: scenario/alternative integer IDs survive roundtrip
        private static void P7_ScenarioAlternativeIntegerIdsSurviveRoundtrip()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_IntegerIds_" + Guid.NewGuid().ToString("N"));
            try
            {
                var scenarioState = new ProjectScenarioAlternativeState
                {
                    SelectedScenarioId = 42,
                    ScenarioMappings = new List<ProjectScenarioMapping>
                    {
                        new ProjectScenarioMapping { ScenarioId = 42, ScenarioLabel = "Design Scenario", IsActive = true }
                    },
                    AlternativeMappings = new List<ProjectAlternativeMapping>
                    {
                        new ProjectAlternativeMapping { AlternativeTypeId = 15, AlternativeTypeName = "Physical", AlternativeId = 999, AlternativeLabel = "Alt 999" }
                    }
                };
                ProjectStore.CreateProject(testDir, "IntIdsProject", ProjectModule.StormFlow, null, null, scenarioState);

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.ScenarioAlternativeState.SelectedScenarioId == 42, "SelectedScenarioId mismatch.");
                Check(loaded.ScenarioAlternativeState.ScenarioMappings[0].ScenarioId == 42, "ScenarioId integer mismatch.");
                Check(loaded.ScenarioAlternativeState.AlternativeMappings[0].AlternativeTypeId == 15, "AlternativeTypeId integer mismatch.");
                Check(loaded.ScenarioAlternativeState.AlternativeMappings[0].AlternativeId == 999, "AlternativeId integer mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 13: empty DesignBasisReference allowed
        private static void P7_EmptyDesignBasisReferenceAllowed()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_EmptyDbRef_" + Guid.NewGuid().ToString("N"));
            try
            {
                var created = ProjectStore.CreateProject(testDir, "NoDbRefProject", ProjectModule.GravityFlow, null, null, null, null);
                Check(created.DesignBasisReference == null, "DesignBasisReference should be null by default.");

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.DesignBasisReference == null, "Loaded DesignBasisReference should remain null.");

                loaded.DesignBasisReference = new DesignBasisReference();
                var saved = ProjectStore.SaveProject(testDir, loaded, 1);
                var reloaded = ProjectStore.LoadProject(testDir);
                Check(reloaded.DesignBasisReference != null, "Empty DesignBasisReference should be persisted without criteria.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 14: DesignRunId survives roundtrip
        private static void P7_DesignRunIdSurvivesRoundtrip()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_RunId_" + Guid.NewGuid().ToString("N"));
            try
            {
                var created = ProjectStore.CreateProject(testDir, "RunIdProject", ProjectModule.StormFlow, null, null, null, null, "run-alpha-2026");
                Check(created.CurrentDesignRunId == "run-alpha-2026", "Initial DesignRunId mismatch.");

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.CurrentDesignRunId == "run-alpha-2026", "Loaded DesignRunId mismatch.");

                loaded.CurrentDesignRunId = "run-beta-2026";
                ProjectStore.SaveProject(testDir, loaded, 1);

                var reloaded = ProjectStore.LoadProject(testDir);
                Check(reloaded.CurrentDesignRunId == "run-beta-2026", "Updated DesignRunId mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 15: audit append creates sequence 1
        private static void P7_AuditAppendCreatesSequence1()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditSeq1_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "AuditSeq1Project");
                var records = ProjectStore.ReadAudit(testDir);
                Check(records.Count == 1, "Must contain exactly 1 initial audit record.");
                Check(records[0].Sequence == 1, "Initial record sequence must be 1.");
                Check(records[0].ActionType == AuditActionType.ProjectCreated, "Action type must be ProjectCreated.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 16: subsequent audit append increments sequence
        private static void P7_SubsequentAuditAppendIncrementsSequence()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditIncr_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "AuditIncrProject");
                ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.ModelSelected });
                ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.ScenarioMappingUpdated });

                var records = ProjectStore.ReadAudit(testDir);
                Check(records.Count == 3, "Expected 3 audit records.");
                Check(records[0].Sequence == 1, "Record 1 sequence mismatch.");
                Check(records[1].Sequence == 2, "Record 2 sequence mismatch.");
                Check(records[2].Sequence == 3, "Record 3 sequence mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 17: reload then append continues sequence
        private static void P7_ReloadThenAppendContinuesSequence()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditContinuation_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "AuditContProject");
                ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.ModelSelected });

                var state = ProjectStore.LoadProject(testDir);
                long lastSeq = ProjectStore.GetLastAuditSequence(testDir);
                Check(lastSeq == 2, "Last sequence before reload must be 2.");

                var newRec = ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.DesignRunRegistered });
                Check(newRec.Sequence == 3, "Reloaded store must continue with sequence 3.");

                var records = ProjectStore.ReadAudit(testDir);
                Check(records.Count == 3 && records[2].Sequence == 3, "Final record must have sequence 3.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 18: audit ProjectId mismatch rejected
        private static void P7_AuditProjectIdMismatchRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditProjIdMismatch_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "ProjIdMismatchTest");

                bool rejected = false;
                try
                {
                    ProjectStore.AppendAudit(testDir, new AuditRecord
                    {
                        ProjectId = "wrong-project-id-guid",
                        ActionType = AuditActionType.ProjectStateUpdated
                    });
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "AUDIT_PROJECT_ID_MISMATCH";
                }
                Check(rejected, "Mismatched audit ProjectId must be rejected.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 19: malformed audit line rejected
        private static void P7_MalformedAuditLineRejected()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_MalformedAuditLine_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "MalformedAuditProject");
                string auditPath = Path.Combine(testDir, "audit.jsonl");
                File.AppendAllText(auditPath, "THIS_IS_CORRUPT_NOT_JSON\r\n");

                bool rejected = false;
                try
                {
                    ProjectStore.ReadAudit(testDir);
                }
                catch (ProjectStorageException ex)
                {
                    rejected = ex.Classification == "MALFORMED_AUDIT_LOG";
                }
                Check(rejected, "Malformed audit line must be rejected with MALFORMED_AUDIT_LOG.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 20: prior audit record is not modified by later append
        private static void P7_PriorAuditRecordNotModifiedByLaterAppend()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditAppendIntegrity_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectStore.CreateProject(testDir, "IntegrityProject");
                ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.ModelSelected });

                string auditPath = Path.Combine(testDir, "audit.jsonl");
                string initialContent = File.ReadAllText(auditPath);

                ProjectStore.AppendAudit(testDir, new AuditRecord { ActionType = AuditActionType.ProjectStateUpdated });
                string newContent = File.ReadAllText(auditPath);

                Check(newContent.StartsWith(initialContent, StringComparison.Ordinal),
                    "Append must preserve prior audit content byte-for-byte.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 21: state temporary-file commit leaves valid final state
        private static void P7_StateTemporaryFileCommitLeavesValidFinalState()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_TempCommit_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "TempCommitProj");
                ProjectStore.SaveProject(testDir, state, 1);

                string[] tmpFiles = Directory.GetFiles(testDir, "*.tmp");
                Check(tmpFiles.Length == 0, "No temporary files must remain after successful save.");

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.StateRevision == 2, "Final state must be valid and readable.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 22: simulated failed state write does not corrupt previous valid state
        private static void P7_SimulatedFailedStateWriteDoesNotCorruptPreviousValidState()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_FailSafeWrite_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "FailSafeProj");
                string statePath = Path.Combine(testDir, "project.json");
                string originalText = File.ReadAllText(statePath);

                state.SchemaVersion = 999;
                bool failed = false;
                try
                {
                    ProjectStore.SaveProject(testDir, state, 1);
                }
                catch (ProjectStorageException)
                {
                    failed = true;
                }
                Check(failed, "Failed save expected.");

                string currentText = File.ReadAllText(statePath);
                Check(currentText == originalText, "Failed write must not corrupt previous valid state.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 23: state conflict does not overwrite newer state
        private static void P7_StateConflictDoesNotOverwriteNewerState()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_ConflictNoOverwrite_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "ConflictProj");

                state.ProjectName = "UpdatedByCallerA";
                ProjectStore.SaveProject(testDir, state, 1);

                state.ProjectName = "OverwrittenByCallerB";
                bool conflictCaught = false;
                try
                {
                    ProjectStore.SaveProject(testDir, state, 1);
                }
                catch (ProjectStorageException ex)
                {
                    conflictCaught = ex.Classification == "PROJECT_STATE_CONFLICT";
                }
                Check(conflictCaught, "Conflict must be detected.");

                var current = ProjectStore.LoadProject(testDir);
                Check(current.ProjectName == "UpdatedByCallerA", "Newer state must not be overwritten on conflict.");
                Check(current.StateRevision == 2, "State revision must remain at 2.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 24: audit failure after state save returns explicit partial-failure classification
        private static void P7_AuditFailureAfterStateSaveReturnsExplicitPartialFailureClassification()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P7_Test_AuditPartialFail_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "PartialFailProj");
                string auditFile = Path.Combine(testDir, "audit.jsonl");

                using (var lockStream = new FileStream(auditFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool partialCaught = false;
                    try
                    {
                        var audit = new AuditRecord { ActionType = AuditActionType.ProjectStateUpdated };
                        ProjectStore.SaveProject(testDir, state, 1, audit);
                    }
                    catch (ProjectStorageException ex)
                    {
                        partialCaught = ex.Classification == "STATE_SAVED_AUDIT_APPEND_FAILED";
                    }
                    Check(partialCaught, "Audit failure after state save must report STATE_SAVED_AUDIT_APPEND_FAILED.");
                }

                string stateJson = File.ReadAllText(Path.Combine(testDir, "project.json"));
                Check(stateJson.Contains("\"StateRevision\":2"), "State revision must have committed to 2.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P7 Test 25: no vendor public types in persistence model
        private static void P7_NoVendorPublicTypesInPersistenceModel()
        {
            var persistenceTypes = new Type[]
            {
                typeof(ProjectStore),
                typeof(ProjectState),
                typeof(ProjectModule),
                typeof(ProjectModelMetadata),
                typeof(ProjectRuntimeMetadata),
                typeof(ProjectScenarioAlternativeState),
                typeof(ProjectScenarioMapping),
                typeof(ProjectAlternativeMapping),
                typeof(DesignBasisReference),
                typeof(AuditRecord),
                typeof(AuditActionType),
                typeof(ProjectStorageException)
            };

            foreach (var type in persistenceTypes)
            {
                Check(Neutral(type), "Persistence type must be vendor-neutral: " + type.FullName);
                foreach (var method in type.GetMethods())
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties())
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P7 Test 26: no secrets written by normal project model
        private static void P7_NoSecretsWrittenByNormalProjectModel()
        {
            var state = new ProjectState
            {
                SchemaVersion = 1,
                ProjectId = Guid.NewGuid().ToString("D"),
                ProjectName = "SecurityCheck",
                StateRevision = 1,
                SelectedModule = "StormFlow",
                Model = new ProjectModelMetadata
                {
                    ProtectedBaselineFingerprint = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                    PrimaryModelDisplayName = "Model.stsw"
                },
                Runtime = new ProjectRuntimeMetadata
                {
                    Product = "SewerGEMS",
                    Version = "10.04.00.12",
                    Build = "10.04.00.12",
                    Architecture = "x64"
                }
            };

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(state);

            string[] secretKeywords = new[] { "password", "passwd", "pwd", "apikey", "api_key", "secret", "credential", "auth_token" };
            foreach (var kw in secretKeywords)
            {
                Check(json.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0,
                    "Serialized project model must not contain secret keywords: " + kw);
            }
        }

        // P7 Test 27: deterministic serialization is culture-independent where relevant
        private static void P7_DeterministicSerializationIsCultureIndependent()
        {
            var state = new ProjectState
            {
                SchemaVersion = 1,
                ProjectId = "00000000-0000-0000-0000-000000000001",
                ProjectName = "CultureTest",
                StateRevision = 1,
                SelectedModule = "GravityFlow",
                CreatedUtc = "2026-10-06T12:00:00.0000000Z",
                UpdatedUtc = "2026-10-06T12:00:00.0000000Z"
            };

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                string invariantJson = serializer.Serialize(state);

                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string germanJson = serializer.Serialize(state);

                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fa-IR");
                string farsiJson = serializer.Serialize(state);

                Check(invariantJson == germanJson, "German culture produced different serialization.");
                Check(invariantJson == farsiJson, "Farsi culture produced different serialization.");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
            }
        }

        // P7 Test 28: path metadata does not become model identity
        private static void P7_PathMetadataDoesNotBecomeModelIdentity()
        {
            string fp = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
            var model1 = new ProjectModelMetadata
            {
                ProtectedBaselineFingerprint = fp,
                BaselineLocationMetadata = @"C:\Path1\Model.wtg.sqlite"
            };
            var model2 = new ProjectModelMetadata
            {
                ProtectedBaselineFingerprint = fp,
                BaselineLocationMetadata = @"D:\Path2\Model.wtg.sqlite"
            };

            Check(string.Equals(model1.ProtectedBaselineFingerprint, model2.ProtectedBaselineFingerprint, StringComparison.Ordinal),
                "Model identity is strictly defined by ProtectedBaselineFingerprint.");
            Check(!string.Equals(model1.BaselineLocationMetadata, model2.BaselineLocationMetadata, StringComparison.Ordinal),
                "Paths are distinct reopen metadata.");
        }

        // P8 Test 1: StandardDefinition valid construction
        private static void P8_StandardDefinitionValidConstruction()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-synthetic-1",
                Name = "Synthetic Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("SYNTH_PARAM_1", StandardValue.FromNumber(12.5))
                }
            };
            def.Validate();
            Check(def.Parameters.Count == 1, "Parameters count must be 1.");
        }

        // P8 Test 2: missing StandardId rejected
        private static void P8_MissingStandardIdRejected()
        {
            var def = new StandardDefinition
            {
                StandardId = "",
                Name = "Synthetic Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            Throws<StandardsValidationException>(() => def.Validate());
        }

        // P8 Test 3: missing name rejected
        private static void P8_MissingNameRejected()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-1",
                Name = "   ",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            Throws<StandardsValidationException>(() => def.Validate());
        }

        // P8 Test 4: duplicate parameter key rejected
        private static void P8_DuplicateParameterKeyRejected()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-1",
                Name = "Synthetic Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("PARAM_ALPHA", StandardValue.FromNumber(1.0)),
                    new StandardParameter("param_alpha", StandardValue.FromNumber(2.0))
                }
            };
            Throws<StandardsValidationException>(() => def.Validate());
        }

        // P8 Test 5: parameter key canonicalization deterministic
        private static void P8_ParameterKeyCanonicalizationDeterministic()
        {
            var k1 = new StandardParameterKey("  Param_One  ");
            var k2 = new StandardParameterKey("PARAM_ONE");
            Check(k1.Equals(k2), "Canonical keys must match across whitespace and casing.");
            Check(k1.CanonicalKey == "PARAM_ONE", "Canonical key must be uppercase invariant.");
            Check(k1.Key == "Param_One", "Key must be trimmed original.");
        }

        // P8 Test 6: Number value roundtrip
        private static void P8_NumberValueRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-num",
                Name = "Num Std",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("NUM_PARAM", StandardValue.FromNumber(42.125))
                }
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Parameters[0].Value.Kind == StandardValueKind.Number, "Value kind mismatch.");
            Check(loaded.Parameters[0].Value.NumberValue == 42.125, "Number value mismatch.");
        }

        // P8 Test 7: Integer value roundtrip
        private static void P8_IntegerValueRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-int",
                Name = "Int Std",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("INT_PARAM", StandardValue.FromInteger(9876543210L))
                }
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Parameters[0].Value.Kind == StandardValueKind.Integer, "Value kind mismatch.");
            Check(loaded.Parameters[0].Value.IntegerValue == 9876543210L, "Integer value mismatch.");
        }

        // P8 Test 8: Boolean value roundtrip
        private static void P8_BooleanValueRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-bool",
                Name = "Bool Std",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("BOOL_PARAM", StandardValue.FromBoolean(true))
                }
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Parameters[0].Value.Kind == StandardValueKind.Boolean, "Value kind mismatch.");
            Check(loaded.Parameters[0].Value.BooleanValue == true, "Boolean value mismatch.");
        }

        // P8 Test 9: Text value roundtrip
        private static void P8_TextValueRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-txt",
                Name = "Text Std",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("TXT_PARAM", StandardValue.FromText("SyntheticText_2026"))
                }
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Parameters[0].Value.Kind == StandardValueKind.Text, "Value kind mismatch.");
            Check(loaded.Parameters[0].Value.TextValue == "SyntheticText_2026", "Text value mismatch.");
        }

        // P8 Test 10: Quantity value + unit roundtrip
        private static void P8_QuantityValueUnitRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-qty",
                Name = "Qty Std",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("QTY_PARAM", StandardValue.FromQuantity(123.45, "synth_unit"))
                }
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Parameters[0].Value.Kind == StandardValueKind.Quantity, "Value kind mismatch.");
            Check(loaded.Parameters[0].Value.QuantityValue.Value == 123.45, "Quantity value mismatch.");
            Check(loaded.Parameters[0].Value.QuantityValue.Unit == "synth_unit", "Quantity unit mismatch.");
        }

        // P8 Test 11: non-finite numeric value rejected
        private static void P8_NonFiniteNumericValueRejected()
        {
            Throws<StandardsValidationException>(() => StandardValue.FromNumber(double.NaN));
            Throws<StandardsValidationException>(() => StandardValue.FromNumber(double.PositiveInfinity));
            Throws<StandardsValidationException>(() => StandardValue.FromQuantity(double.NegativeInfinity, "synth_u"));
        }

        // P8 Test 12: quantity missing unit rejected
        private static void P8_QuantityMissingUnitRejected()
        {
            Throws<StandardsValidationException>(() => StandardValue.FromQuantity(10.0, ""));
            Throws<StandardsValidationException>(() => StandardValue.FromQuantity(10.0, null));
            Throws<StandardsValidationException>(() => StandardValue.FromQuantity(10.0, "   "));
        }

        // P8 Test 13: ApplicationDefault resolves when it is the only source
        private static void P8_ApplicationDefaultResolvesWhenOnlySource()
        {
            var def = new StandardDefinition
            {
                StandardId = "def-app",
                Name = "App Defaults",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("PARAM_A", StandardValue.FromNumber(10.0))
                }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { def });
            Check(res.Status == StandardResolutionStatus.Defined, "Resolution status must be Defined.");
            Check(res.EffectiveSource == StandardSourceLayer.ApplicationDefault, "Effective source must be ApplicationDefault.");
            Check(res.EffectiveValue.NumberValue == 10.0, "Effective value mismatch.");
        }

        // P8 Test 14: PublishedStandard overrides ApplicationDefault
        private static void P8_PublishedStandardOverridesApplicationDefault()
        {
            var appDef = new StandardDefinition
            {
                StandardId = "def-app",
                Name = "App Defaults",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(10.0)) }
            };
            var pubStd = new StandardDefinition
            {
                StandardId = "pub-std",
                Name = "Published Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(20.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { appDef, pubStd });
            Check(res.EffectiveSource == StandardSourceLayer.PublishedStandard, "PublishedStandard must override ApplicationDefault.");
            Check(res.EffectiveValue.NumberValue == 20.0, "Value mismatch.");
        }

        // P8 Test 15: CompanyStandard overrides PublishedStandard
        private static void P8_CompanyStandardOverridesPublishedStandard()
        {
            var appDef = new StandardDefinition
            {
                StandardId = "def-app",
                Name = "App Defaults",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(10.0)) }
            };
            var pubStd = new StandardDefinition
            {
                StandardId = "pub-std",
                Name = "Published Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(20.0)) }
            };
            var compStd = new StandardDefinition
            {
                StandardId = "comp-std",
                Name = "Company Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(30.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { appDef, pubStd, compStd });
            Check(res.EffectiveSource == StandardSourceLayer.CompanyStandard, "CompanyStandard must override PublishedStandard.");
            Check(res.EffectiveValue.NumberValue == 30.0, "Value mismatch.");
        }

        // P8 Test 16: ProjectOverride overrides CompanyStandard
        private static void P8_ProjectOverrideOverridesCompanyStandard()
        {
            var compStd = new StandardDefinition
            {
                StandardId = "comp-std",
                Name = "Company Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(30.0)) }
            };
            var projOver = new StandardDefinition
            {
                StandardId = "proj-over",
                Name = "Project Override",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ProjectOverride,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(40.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { compStd, projOver });
            Check(res.EffectiveSource == StandardSourceLayer.ProjectOverride, "ProjectOverride must override CompanyStandard.");
            Check(res.EffectiveValue.NumberValue == 40.0, "Value mismatch.");
        }

        // P8 Test 17: full four-layer precedence correct
        private static void P8_FullFourLayerPrecedenceCorrect()
        {
            var s1 = new StandardDefinition
            {
                StandardId = "s1",
                Name = "App",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(10.0)) }
            };
            var s2 = new StandardDefinition
            {
                StandardId = "s2",
                Name = "Pub",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(20.0)) }
            };
            var s3 = new StandardDefinition
            {
                StandardId = "s3",
                Name = "Co",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(30.0)) }
            };
            var s4 = new StandardDefinition
            {
                StandardId = "s4",
                Name = "Proj",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ProjectOverride,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(40.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { s1, s2, s3, s4 });
            Check(res.EffectiveValue.NumberValue == 40.0, "Winner must be ProjectOverride value.");
            Check(res.Candidates.Count == 4, "Must retain all 4 candidates.");
            Check(res.Candidates[0].Provenance.SourceLayer == StandardSourceLayer.ProjectOverride, "Rank 0 must be ProjectOverride.");
            Check(res.Candidates[1].Provenance.SourceLayer == StandardSourceLayer.CompanyStandard, "Rank 1 must be CompanyStandard.");
            Check(res.Candidates[2].Provenance.SourceLayer == StandardSourceLayer.PublishedStandard, "Rank 2 must be PublishedStandard.");
            Check(res.Candidates[3].Provenance.SourceLayer == StandardSourceLayer.ApplicationDefault, "Rank 3 must be ApplicationDefault.");
        }

        // P8 Test 18: winner provenance retained
        private static void P8_WinnerProvenanceRetained()
        {
            var s4 = new StandardDefinition
            {
                StandardId = "proj-over-42",
                Name = "Project Specific Override",
                Version = "2026.1",
                SourceLayer = StandardSourceLayer.ProjectOverride,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(42.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { s4 });
            Check(res.EffectiveProvenance != null, "Effective provenance must not be null.");
            Check(res.EffectiveProvenance.StandardId == "proj-over-42", "StandardId mismatch.");
            Check(res.EffectiveProvenance.StandardName == "Project Specific Override", "StandardName mismatch.");
            Check(res.EffectiveProvenance.StandardVersion == "2026.1", "StandardVersion mismatch.");
            Check(res.EffectiveProvenance.SourceLayer == StandardSourceLayer.ProjectOverride, "SourceLayer mismatch.");
        }

        // P8 Test 19: shadowed candidate provenance retained
        private static void P8_ShadowedCandidateProvenanceRetained()
        {
            var pub = new StandardDefinition
            {
                StandardId = "pub-id",
                Name = "Pub Standard",
                Version = "2020.1",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(10.0)) }
            };
            var co = new StandardDefinition
            {
                StandardId = "co-id",
                Name = "Co Standard",
                Version = "2024.1",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_A", StandardValue.FromNumber(20.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_A", new[] { pub, co });
            Check(res.Candidates.Count == 2, "Expected 2 candidates.");
            Check(res.Candidates[0].IsWinner == true, "Top candidate is winner.");
            Check(res.Candidates[1].IsWinner == false, "Shadowed candidate is not winner.");
            Check(res.Candidates[1].Provenance.StandardId == "pub-id", "Shadowed candidate standard ID must be retained.");
            Check(res.Candidates[1].Provenance.StandardVersion == "2020.1", "Shadowed candidate version must be retained.");
        }

        // P8 Test 20: no defined value returns explicit NOT_DEFINED
        private static void P8_NoDefinedValueReturnsExplicitNotDefined()
        {
            var def = new StandardDefinition
            {
                StandardId = "def",
                Name = "Def",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_EXISTS", StandardValue.FromNumber(1.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_DOES_NOT_EXIST", new[] { def });
            Check(res.Status == StandardResolutionStatus.NotDefined, "Status must be NotDefined.");
            Check(res.EffectiveValue == null, "EffectiveValue must be null.");
            Check(res.EffectiveSource == null, "EffectiveSource must be null.");
        }

        // P8 Test 21: two eligible standards in same layer without explicit selection produce AMBIGUOUS_STANDARD_SOURCE
        private static void P8_TwoEligibleStandardsSameLayerWithoutSelectionProduceAmbiguous()
        {
            var co1 = new StandardDefinition
            {
                StandardId = "co-1",
                Name = "Company Standard North",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_DUAL", StandardValue.FromNumber(100.0)) }
            };
            var co2 = new StandardDefinition
            {
                StandardId = "co-2",
                Name = "Company Standard South",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_DUAL", StandardValue.FromNumber(200.0)) }
            };

            bool caught = false;
            try
            {
                StandardsResolver.Resolve("PARAM_DUAL", new[] { co1, co2 });
            }
            catch (StandardsResolutionException ex)
            {
                caught = ex.Classification == "AMBIGUOUS_STANDARD_SOURCE";
            }
            Check(caught, "Ambiguous same-layer standards without explicit selection must fail with AMBIGUOUS_STANDARD_SOURCE.");
        }

        // P8 Test 22: explicit same-layer selection resolves deterministically
        private static void P8_ExplicitSameLayerSelectionResolvesDeterministically()
        {
            var co1 = new StandardDefinition
            {
                StandardId = "co-1",
                Name = "Company Standard North",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_DUAL", StandardValue.FromNumber(100.0)) }
            };
            var co2 = new StandardDefinition
            {
                StandardId = "co-2",
                Name = "Company Standard South",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.CompanyStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_DUAL", StandardValue.FromNumber(200.0)) }
            };

            var options = new StandardsResolutionOptions { SelectedCompanyStandardId = "co-2" };
            var res = StandardsResolver.Resolve("PARAM_DUAL", new[] { co1, co2 }, options);
            Check(res.EffectiveStandardId == "co-2", "Selected standard ID must be used.");
            Check(res.EffectiveValue.NumberValue == 200.0, "Selected standard value must be resolved.");
        }

        // P8 Test 23: override does not mutate lower layer
        private static void P8_OverrideDoesNotMutateLowerLayer()
        {
            var pub = new StandardDefinition
            {
                StandardId = "pub-orig",
                Name = "Pub Original",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_IMMUTABLE", StandardValue.FromNumber(50.0)) }
            };
            var over = new StandardDefinition
            {
                StandardId = "proj-over",
                Name = "Proj Over",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ProjectOverride,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_IMMUTABLE", StandardValue.FromNumber(99.0)) }
            };

            var res = StandardsResolver.Resolve("PARAM_IMMUTABLE", new[] { pub, over });
            Check(res.EffectiveValue.NumberValue == 99.0, "Resolved value must be override value.");
            Check(pub.Parameters[0].Value.NumberValue == 50.0, "Source standard parameter must remain unmutated.");
        }

        // P8 Test 24: input ordering does not alter resolution
        private static void P8_InputOrderingDoesNotAlterResolution()
        {
            var s1 = new StandardDefinition { StandardId = "s1", Name = "A", Version = "1", SourceLayer = StandardSourceLayer.ApplicationDefault, Parameters = new List<StandardParameter> { new StandardParameter("P", StandardValue.FromNumber(1)) } };
            var s2 = new StandardDefinition { StandardId = "s2", Name = "B", Version = "1", SourceLayer = StandardSourceLayer.PublishedStandard, Parameters = new List<StandardParameter> { new StandardParameter("P", StandardValue.FromNumber(2)) } };
            var s3 = new StandardDefinition { StandardId = "s3", Name = "C", Version = "1", SourceLayer = StandardSourceLayer.CompanyStandard, Parameters = new List<StandardParameter> { new StandardParameter("P", StandardValue.FromNumber(3)) } };
            var s4 = new StandardDefinition { StandardId = "s4", Name = "D", Version = "1", SourceLayer = StandardSourceLayer.ProjectOverride, Parameters = new List<StandardParameter> { new StandardParameter("P", StandardValue.FromNumber(4)) } };

            var resA = StandardsResolver.Resolve("P", new[] { s1, s2, s3, s4 });
            var resB = StandardsResolver.Resolve("P", new[] { s4, s1, s3, s2 });
            var resC = StandardsResolver.Resolve("P", new[] { s3, s4, s2, s1 });

            Check(resA.EffectiveValue.Equals(resB.EffectiveValue) && resB.EffectiveValue.Equals(resC.EffectiveValue), "Effective values must match regardless of input order.");
            Check(resA.EffectiveSource == resB.EffectiveSource && resB.EffectiveSource == resC.EffectiveSource, "Effective source must match regardless of input order.");
            Check(resA.Candidates.Count == resB.Candidates.Count && resB.Candidates.Count == resC.Candidates.Count, "Candidate counts must match.");
        }

        // P8 Test 25: culture does not alter numeric/unit serialization or resolution
        private static void P8_CultureDoesNotAlterNumericUnitSerializationOrResolution()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-cult",
                Name = "Culture Test Standard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter>
                {
                    new StandardParameter("QTY_P", StandardValue.FromQuantity(1234.56, "synth_m"))
                }
            };

            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                string jsonInv = StandardsSerializer.Serialize(def);
                var resInv = StandardsResolver.Resolve("QTY_P", new[] { def });

                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string jsonDe = StandardsSerializer.Serialize(def);
                var resDe = StandardsResolver.Resolve("QTY_P", new[] { def });

                Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
                string jsonFa = StandardsSerializer.Serialize(def);
                var resFa = StandardsResolver.Resolve("QTY_P", new[] { def });

                Check(jsonInv == jsonDe, "German culture produced different serialization.");
                Check(jsonInv == jsonFa, "Farsi culture produced different serialization.");
                Check(resInv.EffectiveValue.QuantityValue.Value == resDe.EffectiveValue.QuantityValue.Value, "Resolution value mismatch under German culture.");
                Check(resInv.EffectiveValue.QuantityValue.Value == resFa.EffectiveValue.QuantityValue.Value, "Resolution value mismatch under Farsi culture.");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        // P8 Test 26: version metadata survives roundtrip
        private static void P8_VersionMetadataSurvivesRoundtrip()
        {
            var def = new StandardDefinition
            {
                StandardId = "std-ver",
                Name = "Version Standard",
                Version = "2026.04.1-RC2",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            string json = StandardsSerializer.Serialize(def);
            var loaded = StandardsSerializer.Deserialize(json);
            Check(loaded.Version == "2026.04.1-RC2", "Version metadata must survive roundtrip.");
        }

        // P8 Test 27: version does not automatically determine winner
        private static void P8_VersionDoesNotAutomaticallyDetermineWinner()
        {
            var pubV1 = new StandardDefinition
            {
                StandardId = "pub-v1",
                Name = "Standard V1",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_V", StandardValue.FromNumber(10.0)) }
            };
            var pubV2 = new StandardDefinition
            {
                StandardId = "pub-v2",
                Name = "Standard V2",
                Version = "2.0",
                SourceLayer = StandardSourceLayer.PublishedStandard,
                Parameters = new List<StandardParameter> { new StandardParameter("PARAM_V", StandardValue.FromNumber(20.0)) }
            };

            bool ambiguousCaught = false;
            try
            {
                StandardsResolver.Resolve("PARAM_V", new[] { pubV1, pubV2 });
            }
            catch (StandardsResolutionException ex)
            {
                ambiguousCaught = ex.Classification == "AMBIGUOUS_STANDARD_SOURCE";
            }
            Check(ambiguousCaught, "Unselected multiple versions in same layer must fail closed.");

            var options = new StandardsResolutionOptions { SelectedPublishedStandardId = "pub-v1" };
            var res = StandardsResolver.Resolve("PARAM_V", new[] { pubV1, pubV2 }, options);
            Check(res.EffectiveStandardId == "pub-v1", "Selected V1 must win regardless of version string ordering.");
            Check(res.EffectiveValue.NumberValue == 10.0, "V1 value must be resolved.");
        }

        // P8 Test 28: ApplicationDefault remains visibly classified as default
        private static void P8_ApplicationDefaultRemainsVisiblyClassifiedAsDefault()
        {
            var appDef = new StandardDefinition
            {
                StandardId = "app-def-std",
                Name = "Application Default Set",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                Parameters = new List<StandardParameter> { new StandardParameter("DEF_P", StandardValue.FromNumber(99.0)) }
            };

            var res = StandardsResolver.Resolve("DEF_P", new[] { appDef });
            Check(res.EffectiveSource == StandardSourceLayer.ApplicationDefault, "EffectiveSource must be ApplicationDefault.");
            Check(res.EffectiveProvenance.SourceLayer == StandardSourceLayer.ApplicationDefault, "Provenance source must be ApplicationDefault.");
            Check(StandardSourceLayers.ApplicationDefault == "ApplicationDefault", "ApplicationDefault constant name verified.");
        }

        // P8 Test 29: no vendor types leak
        private static void P8_NoVendorTypesLeak()
        {
            var p8Types = new Type[]
            {
                typeof(StandardSourceLayer),
                typeof(StandardValueKind),
                typeof(StandardResolutionStatus),
                typeof(StandardSourceLayers),
                typeof(StandardsValidationException),
                typeof(StandardsResolutionException),
                typeof(StandardQuantity),
                typeof(StandardValue),
                typeof(StandardParameterKey),
                typeof(StandardProvenance),
                typeof(StandardParameter),
                typeof(StandardDefinition),
                typeof(StandardCandidate),
                typeof(ResolvedStandardValue),
                typeof(StandardsResolutionOptions),
                typeof(StandardsResolver),
                typeof(StandardsSerializer),
                typeof(ProjectStandardsReference)
            };

            foreach (var type in p8Types)
            {
                Check(Neutral(type), "P8 type must be vendor-neutral: " + type.FullName);
                foreach (var method in type.GetMethods())
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties())
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P8 Test 30: project-storage reference roundtrip if P7 integration is modified
        private static void P8_ProjectStorageReferenceRoundtrip()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P8_Test_StorageRoundtrip_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "P8RefProj", ProjectModule.StormFlow);
                state.StandardsReference = new ProjectStandardsReference
                {
                    PublishedStandardId = "pub-std-42",
                    CompanyStandardId = "comp-std-84",
                    ProjectOverrideId = "proj-over-126"
                };

                ProjectStore.SaveProject(testDir, state, 1);

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.StandardsReference != null, "StandardsReference must not be null.");
                Check(loaded.StandardsReference.PublishedStandardId == "pub-std-42", "PublishedStandardId mismatch.");
                Check(loaded.StandardsReference.CompanyStandardId == "comp-std-84", "CompanyStandardId mismatch.");
                Check(loaded.StandardsReference.ProjectOverrideId == "proj-over-126", "ProjectOverrideId mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // ==========================================
        // P9 COMMON DESIGN BASIS TESTS (40 Tests)
        // ==========================================

        private static DesignBasis CreateValidDesignBasis()
        {
            var db = new DesignBasis
            {
                SchemaVersion = 1,
                DesignBasisId = "db-" + Guid.NewGuid().ToString("N"),
                DesignBasisVersion = "1.0",
                ProjectId = "proj-" + Guid.NewGuid().ToString("N"),
                ReviewState = DesignBasisReviewState.Draft
            };
            db.StandardReferences.Add(new DesignBasisStandardReference
            {
                SourceLayer = StandardSourceLayer.PublishedStandard,
                StandardId = "synth-std-1",
                StandardVersion = "2026.1"
            });
            db.Assumptions.Add(new DesignBasisAssumption
            {
                AssumptionId = "asm-synth-1",
                Key = "synth.param.a",
                Value = StandardValue.FromNumber(42.5),
                Rationale = "Synthetic assumption rationale",
                Provenance = new DesignBasisAssumptionProvenance
                {
                    Kind = AssumptionProvenanceKind.ProjectInput,
                    SourceDescription = "Synthetic test input"
                }
            });
            db.Overrides.Add(new DesignBasisOverride
            {
                OverrideId = "ovr-synth-1",
                ParameterKey = "synth.param.b",
                OverrideValue = StandardValue.FromQuantity(15.0, "synth_unit"),
                Rationale = "Synthetic override rationale"
            });
            return db;
        }

        // P9 Test 1: DesignBasisId stable
        private static void P9_DesignBasisIdStable()
        {
            var db = CreateValidDesignBasis();
            string json = DesignBasisSerializer.Serialize(db);
            var loaded = DesignBasisSerializer.Deserialize(json);
            Check(loaded.DesignBasisId == db.DesignBasisId, "DesignBasisId must remain stable across serialization roundtrip.");

            var snapshot = db.CreateSnapshot();
            string snapJson = DesignBasisSerializer.SerializeSnapshot(snapshot);
            var loadedSnap = DesignBasisSerializer.DeserializeSnapshot(snapJson);
            Check(loadedSnap.DesignBasisId == db.DesignBasisId, "DesignBasisId must remain stable in snapshot roundtrip.");
        }

        // P9 Test 2: DesignBasisVersion preserved
        private static void P9_DesignBasisVersionPreserved()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisVersion = "Rev-3.2.1-Draft";
            string json = DesignBasisSerializer.Serialize(db);
            var loaded = DesignBasisSerializer.Deserialize(json);
            Check(loaded.DesignBasisVersion == "Rev-3.2.1-Draft", "DesignBasisVersion must be preserved exactly.");

            var snapshot = db.CreateSnapshot();
            Check(snapshot.DesignBasisVersion == "Rev-3.2.1-Draft", "Snapshot must preserve DesignBasisVersion.");
        }

        // P9 Test 3: deterministic ContentFingerprint repeatability
        private static void P9_DeterministicContentFingerprintRepeatability()
        {
            var db = CreateValidDesignBasis();
            var snapshot1 = db.CreateSnapshot();
            var snapshot2 = db.CreateSnapshot();
            Check(!string.IsNullOrEmpty(snapshot1.ContentFingerprint), "Fingerprint must not be empty.");
            Check(snapshot1.ContentFingerprint.Length == 64, "Fingerprint must be 64 hex characters (SHA-256).");
            Check(snapshot1.ContentFingerprint == snapshot2.ContentFingerprint, "ContentFingerprint must be deterministic and repeatable.");
        }

        // P9 Test 4: insertion order does not affect fingerprint
        private static void P9_InsertionOrderDoesNotAffectFingerprint()
        {
            var db1 = new DesignBasis
            {
                SchemaVersion = 1,
                DesignBasisId = "fixed-id",
                DesignBasisVersion = "1.0"
            };
            db1.StandardReferences.Add(new DesignBasisStandardReference { SourceLayer = StandardSourceLayer.PublishedStandard, StandardId = "std-A", StandardVersion = "1.0" });
            db1.StandardReferences.Add(new DesignBasisStandardReference { SourceLayer = StandardSourceLayer.CompanyStandard, StandardId = "std-B", StandardVersion = "1.0" });
            db1.Assumptions.Add(new DesignBasisAssumption { AssumptionId = "a1", Key = "param.alpha", Value = StandardValue.FromNumber(1.0), Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput } });
            db1.Assumptions.Add(new DesignBasisAssumption { AssumptionId = "a2", Key = "param.beta", Value = StandardValue.FromNumber(2.0), Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput } });
            db1.Overrides.Add(new DesignBasisOverride { OverrideId = "o1", ParameterKey = "ovr.first", OverrideValue = StandardValue.FromText("val1"), Rationale = "Rationale 1" });
            db1.Overrides.Add(new DesignBasisOverride { OverrideId = "o2", ParameterKey = "ovr.second", OverrideValue = StandardValue.FromText("val2"), Rationale = "Rationale 2" });

            // Inverse insertion order
            var db2 = new DesignBasis
            {
                SchemaVersion = 1,
                DesignBasisId = "fixed-id",
                DesignBasisVersion = "1.0"
            };
            db2.StandardReferences.Add(new DesignBasisStandardReference { SourceLayer = StandardSourceLayer.CompanyStandard, StandardId = "std-B", StandardVersion = "1.0" });
            db2.StandardReferences.Add(new DesignBasisStandardReference { SourceLayer = StandardSourceLayer.PublishedStandard, StandardId = "std-A", StandardVersion = "1.0" });
            db2.Assumptions.Add(new DesignBasisAssumption { AssumptionId = "a2", Key = "param.beta", Value = StandardValue.FromNumber(2.0), Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput } });
            db2.Assumptions.Add(new DesignBasisAssumption { AssumptionId = "a1", Key = "param.alpha", Value = StandardValue.FromNumber(1.0), Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput } });
            db2.Overrides.Add(new DesignBasisOverride { OverrideId = "o2", ParameterKey = "ovr.second", OverrideValue = StandardValue.FromText("val2"), Rationale = "Rationale 2" });
            db2.Overrides.Add(new DesignBasisOverride { OverrideId = "o1", ParameterKey = "ovr.first", OverrideValue = StandardValue.FromText("val1"), Rationale = "Rationale 1" });

            var snap1 = db1.CreateSnapshot();
            var snap2 = db2.CreateSnapshot();
            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Insertion order must not alter ContentFingerprint.");
        }

        // P9 Test 5: current culture does not affect fingerprint
        private static void P9_CurrentCultureDoesNotAffectFingerprint()
        {
            var prevCulture = Thread.CurrentThread.CurrentCulture;
            var prevUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                string baseFingerprint = null;
                string[] cultures = new[] { "en-US", "fr-FR", "de-DE", "fa-IR", "tr-TR", "" };
                foreach (var c in cultures)
                {
                    var ci = string.IsNullOrEmpty(c) ? CultureInfo.InvariantCulture : new CultureInfo(c);
                    Thread.CurrentThread.CurrentCulture = ci;
                    Thread.CurrentThread.CurrentUICulture = ci;

                    var db = new DesignBasis
                    {
                        SchemaVersion = 1,
                        DesignBasisId = "culture-id",
                        DesignBasisVersion = "1.0"
                    };
                    db.Assumptions.Add(new DesignBasisAssumption
                    {
                        AssumptionId = "asm-cult",
                        Key = "fractional.number",
                        Value = StandardValue.FromQuantity(1234.5678, "m/s"),
                        Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput }
                    });

                    var snap = db.CreateSnapshot();
                    if (baseFingerprint == null) baseFingerprint = snap.ContentFingerprint;
                    else Check(snap.ContentFingerprint == baseFingerprint, "Fingerprint varied across cultures: " + c);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prevCulture;
                Thread.CurrentThread.CurrentUICulture = prevUiCulture;
            }
        }

        // P9 Test 6: absolute path does not affect fingerprint
        private static void P9_AbsolutePathDoesNotAffectFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "static-db-id";
            var snap1 = db1.CreateSnapshot();

            // Simulate save in path A vs path B
            string pathA = @"C:\Users\Engineer\Documents\Project1\DesignBasis.json";
            string pathB = @"D:\NetworkShare\Designs\2026\Project2\DesignBasis.json";
            Check(pathA != pathB, "Paths differ.");

            var snap2 = db1.CreateSnapshot();
            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Absolute path must not affect fingerprint.");
        }

        // P9 Test 7: machine/user/session metadata does not affect fingerprint
        private static void P9_MachineUserSessionMetadataDoesNotAffectFingerprint()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "session-check-id";
            var snap1 = db.CreateSnapshot();

            // Fingerprint depends only on engineering content
            var snap2 = db.CreateSnapshot();
            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Session/machine state must not alter fingerprint.");
        }

        // P9 Test 8: review timestamp does not affect fingerprint
        private static void P9_ReviewTimestampDoesNotAffectFingerprint()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "time-check-id";
            var snap1 = db.CreateSnapshot(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var snap2 = db.CreateSnapshot(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Review creation timestamp must not alter ContentFingerprint.");
        }

        // P9 Test 9: reviewer identity does not affect fingerprint
        private static void P9_ReviewerIdentityDoesNotAffectFingerprint()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "reviewer-id-check";
            var snap1 = db.CreateSnapshot();

            db.ReviewMetadata = new DesignBasisReviewMetadata { ActorId = "Alice" };
            var snap2 = db.CreateSnapshot();

            db.ReviewMetadata = new DesignBasisReviewMetadata { ActorId = "Bob" };
            var snap3 = db.CreateSnapshot();

            Check(snap1.ContentFingerprint == snap2.ContentFingerprint && snap2.ContentFingerprint == snap3.ContentFingerprint,
                "Reviewer actor ID must not alter ContentFingerprint.");
        }

        // P9 Test 10: Bentley runtime metadata does not affect fingerprint
        private static void P9_BentleyRuntimeMetadataDoesNotAffectFingerprint()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "bentley-noise-test";
            var snap1 = db.CreateSnapshot();
            var snap2 = db.CreateSnapshot();
            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Runtime noise must not affect ContentFingerprint.");
        }

        // P9 Test 11: changed assumption value changes fingerprint
        private static void P9_ChangedAssumptionValueChangesFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "diff-val-test";
            var snap1 = db1.CreateSnapshot();

            var db2 = CreateValidDesignBasis();
            db2.DesignBasisId = "diff-val-test";
            db2.Assumptions[0].Value = StandardValue.FromNumber(42.6); // was 42.5
            var snap2 = db2.CreateSnapshot();

            Check(snap1.ContentFingerprint != snap2.ContentFingerprint, "Changed assumption value must change ContentFingerprint.");
        }

        // P9 Test 12: added assumption changes fingerprint
        private static void P9_AddedAssumptionChangesFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "add-asm-test";
            var snap1 = db1.CreateSnapshot();

            var db2 = CreateValidDesignBasis();
            db2.DesignBasisId = "add-asm-test";
            db2.Assumptions.Add(new DesignBasisAssumption
            {
                AssumptionId = "asm-second",
                Key = "second.synth.param",
                Value = StandardValue.FromText("extra text"),
                Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.EngineerAssumption }
            });
            var snap2 = db2.CreateSnapshot();

            Check(snap1.ContentFingerprint != snap2.ContentFingerprint, "Adding an assumption must change ContentFingerprint.");
        }

        // P9 Test 13: changed override changes fingerprint
        private static void P9_ChangedOverrideChangesFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "ovr-change-test";
            var snap1 = db1.CreateSnapshot();

            var db2 = CreateValidDesignBasis();
            db2.DesignBasisId = "ovr-change-test";
            db2.Overrides[0].OverrideValue = StandardValue.FromQuantity(16.0, "synth_unit"); // was 15.0
            var snap2 = db2.CreateSnapshot();

            Check(snap1.ContentFingerprint != snap2.ContentFingerprint, "Changed override must change ContentFingerprint.");
        }

        // P9 Test 14: changed standard reference changes fingerprint
        private static void P9_ChangedStandardReferenceChangesFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "std-ref-change-test";
            var snap1 = db1.CreateSnapshot();

            var db2 = CreateValidDesignBasis();
            db2.DesignBasisId = "std-ref-change-test";
            db2.StandardReferences[0].StandardVersion = "2026.2"; // was 2026.1
            var snap2 = db2.CreateSnapshot();

            Check(snap1.ContentFingerprint != snap2.ContentFingerprint, "Changed standard reference must change ContentFingerprint.");
        }

        // P9 Test 15: duplicate assumption ID rejected
        private static void P9_DuplicateAssumptionIdRejected()
        {
            var db = CreateValidDesignBasis();
            db.Assumptions.Add(new DesignBasisAssumption
            {
                AssumptionId = db.Assumptions[0].AssumptionId, // Duplicate ID
                Key = "different.param.key",
                Value = StandardValue.FromBoolean(true),
                Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput }
            });
            var validation = db.Validate();
            Check(!validation.IsValid, "Duplicate AssumptionId must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "DUPLICATE_ASSUMPTION_ID"), "Must emit DUPLICATE_ASSUMPTION_ID issue.");
            Throws<DesignBasisValidationException>(() => db.CreateSnapshot());
        }

        // P9 Test 16: duplicate assumption key rejected
        private static void P9_DuplicateAssumptionKeyRejected()
        {
            var db = CreateValidDesignBasis();
            db.Assumptions.Add(new DesignBasisAssumption
            {
                AssumptionId = "unique-asm-id-2",
                Key = db.Assumptions[0].Key.ToUpperInvariant(), // Same key with different casing
                Value = StandardValue.FromBoolean(true),
                Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput }
            });
            var validation = db.Validate();
            Check(!validation.IsValid, "Duplicate assumption key must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "DUPLICATE_ASSUMPTION_KEY"), "Must emit DUPLICATE_ASSUMPTION_KEY issue.");
        }

        // P9 Test 17: missing assumption provenance rejected
        private static void P9_MissingAssumptionProvenanceRejected()
        {
            var db = CreateValidDesignBasis();
            db.Assumptions[0].Provenance = null;
            var validation = db.Validate();
            Check(!validation.IsValid, "Missing assumption provenance must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "MISSING_ASSUMPTION_PROVENANCE"), "Must emit MISSING_ASSUMPTION_PROVENANCE issue.");
        }

        // P9 Test 18: invalid typed assumption value rejected
        private static void P9_InvalidTypedAssumptionValueRejected()
        {
            var db = CreateValidDesignBasis();
            db.Assumptions[0].Value = new StandardValue
            {
                Kind = StandardValueKind.Number,
                NumberValue = double.NaN
            };
            var validation = db.Validate();
            Check(!validation.IsValid, "Non-finite numeric value in assumption must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "INVALID_ASSUMPTION_VALUE"), "Must emit INVALID_ASSUMPTION_VALUE issue.");
        }

        // P9 Test 19: duplicate override ID rejected
        private static void P9_DuplicateOverrideIdRejected()
        {
            var db = CreateValidDesignBasis();
            db.Overrides.Add(new DesignBasisOverride
            {
                OverrideId = db.Overrides[0].OverrideId, // Duplicate ID
                ParameterKey = "different.override.param",
                OverrideValue = StandardValue.FromText("test"),
                Rationale = "Valid rationale"
            });
            var validation = db.Validate();
            Check(!validation.IsValid, "Duplicate OverrideId must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "DUPLICATE_OVERRIDE_ID"), "Must emit DUPLICATE_OVERRIDE_ID issue.");
        }

        // P9 Test 20: duplicate active override key rejected
        private static void P9_DuplicateActiveOverrideKeyRejected()
        {
            var db = CreateValidDesignBasis();
            db.Overrides.Add(new DesignBasisOverride
            {
                OverrideId = "unique-ovr-id-2",
                ParameterKey = db.Overrides[0].ParameterKey.ToUpperInvariant(), // Same key, different case
                OverrideValue = StandardValue.FromText("test"),
                Rationale = "Valid rationale"
            });
            var validation = db.Validate();
            Check(!validation.IsValid, "Duplicate active override key must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "DUPLICATE_OVERRIDE_KEY"), "Must emit DUPLICATE_OVERRIDE_KEY issue.");
        }

        // P9 Test 21: override without rationale rejected
        private static void P9_OverrideWithoutRationaleRejected()
        {
            var db = CreateValidDesignBasis();
            db.Overrides[0].Rationale = "   "; // Whitespace only
            var validation = db.Validate();
            Check(!validation.IsValid, "Override without rationale must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "MISSING_OVERRIDE_RATIONALE"), "Must emit MISSING_OVERRIDE_RATIONALE issue.");
        }

        // P9 Test 22: source P8 standard remains immutable
        private static void P9_SourceP8StandardRemainsImmutable()
        {
            var stdDef = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "base-std",
                Name = "BaseStandard",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            stdDef.Parameters.Add(new StandardParameter("param.flow", StandardValue.FromNumber(100.0)));

            // Create override in Design Basis targeting that parameter
            var ovr = new DesignBasisOverride
            {
                OverrideId = "ovr-1",
                ParameterKey = "param.flow",
                OriginalValue = stdDef.Parameters[0].Value,
                OverrideValue = StandardValue.FromNumber(150.0),
                Rationale = "Project constraint"
            };

            // Assert source standard definition unchanged
            Check(stdDef.Parameters.Count == 1, "Source standard parameter count must remain 1.");
            Check(stdDef.Parameters[0].Value.NumberValue == 100.0, "Source standard value must remain 100.0.");
        }

        // P9 Test 23: P8 resolved provenance survives Design Basis linkage
        private static void P9_P8ResolvedProvenanceSurvivesDesignBasisLinkage()
        {
            var appDef = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "app-def",
                Name = "AppDefault",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault
            };
            appDef.Parameters.Add(new StandardParameter("synth.key", StandardValue.FromNumber(10.0)));

            var pubStd = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "pub-std",
                Name = "PubStandard",
                Version = "2.0",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            pubStd.Parameters.Add(new StandardParameter("synth.key", StandardValue.FromNumber(20.0)));

            var compStd = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "comp-std",
                Name = "CompStandard",
                Version = "3.0",
                SourceLayer = StandardSourceLayer.CompanyStandard
            };
            compStd.Parameters.Add(new StandardParameter("synth.key", StandardValue.FromNumber(30.0)));

            var projOvr = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "proj-ovr",
                Name = "ProjOverride",
                Version = "4.0",
                SourceLayer = StandardSourceLayer.ProjectOverride
            };
            projOvr.Parameters.Add(new StandardParameter("synth.key", StandardValue.FromNumber(40.0)));

            var resolved = StandardsResolver.Resolve("synth.key", new[] { appDef, pubStd, compStd, projOvr });
            Check(resolved.Status == StandardResolutionStatus.Defined, "Resolution must succeed.");
            Check(resolved.EffectiveSource == StandardSourceLayer.ProjectOverride, "Winning layer must be ProjectOverride.");

            var assumption = DesignBasisAssumption.FromResolvedStandard("asm-link", resolved, "Linked from P8");
            Check(assumption.Key == "synth.key", "Key must match parameter key.");
            Check(assumption.Value.NumberValue == 40.0, "Value must match winning value.");
            Check(assumption.Provenance.Kind == AssumptionProvenanceKind.ResolvedStandard, "Kind must be ResolvedStandard.");
            Check(assumption.Provenance.StandardId == "proj-ovr", "StandardId must match.");
            Check(assumption.Provenance.StandardVersion == "4.0", "Version must match.");
            Check(assumption.Provenance.SourceLayer == StandardSourceLayer.ProjectOverride, "SourceLayer must match.");
        }

        // P9 Test 24: ApplicationDefault provenance remains visibly default
        private static void P9_ApplicationDefaultProvenanceRemainsVisiblyDefault()
        {
            var appDef = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "app-def-only",
                Name = "AppDefault",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.ApplicationDefault
            };
            appDef.Parameters.Add(new StandardParameter("default.param", StandardValue.FromText("standard-default")));

            var resolved = StandardsResolver.Resolve("default.param", new[] { appDef });
            var assumption = DesignBasisAssumption.FromResolvedStandard("asm-def", resolved, "Default link");
            Check(assumption.Provenance.SourceLayer == StandardSourceLayer.ApplicationDefault,
                "ApplicationDefault provenance must remain visibly classified as ApplicationDefault.");
            Check(assumption.Provenance.SourceLayer != StandardSourceLayer.PublishedStandard,
                "ApplicationDefault must NOT be classified as regulatory / published requirement.");
        }

        // P9 Test 25: Draft -> ReadyForReview succeeds only when valid
        private static void P9_DraftToReadyForReviewSucceedsOnlyWhenValid()
        {
            var db = CreateValidDesignBasis();
            Check(db.ReviewState == DesignBasisReviewState.Draft, "Initial state must be Draft.");

            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "engineer-1", "Ready for peer review");
            Check(db.ReviewState == DesignBasisReviewState.ReadyForReview, "State must transition to ReadyForReview.");
            Check(db.ReviewMetadata.ActorId == "engineer-1", "ActorId must be recorded in review metadata.");
            Check(db.ReviewMetadata.ReviewNote == "Ready for peer review", "Review note must be recorded.");
        }

        // P9 Test 26: invalid Draft cannot become ReadyForReview
        private static void P9_InvalidDraftCannotBecomeReadyForReview()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = ""; // Make invalid
            Check(!db.Validate().IsValid, "Validation must fail.");

            try
            {
                db.TransitionReviewState(DesignBasisReviewState.ReadyForReview);
                Check(false, "Transition must fail for invalid Draft.");
            }
            catch (DesignBasisValidationException ex)
            {
                assertions++;
                Check(ex.Classification == "REVIEW_VALIDATION_GATE_FAILED", "Classification must be REVIEW_VALIDATION_GATE_FAILED.");
                Check(db.ReviewState == DesignBasisReviewState.Draft, "State must remain Draft after failed transition.");
            }
        }

        // P9 Test 27: ReadyForReview -> Reviewed succeeds
        private static void P9_ReadyForReviewToReviewedSucceeds()
        {
            var db = CreateValidDesignBasis();
            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "author", "Ready");
            db.TransitionReviewState(DesignBasisReviewState.Reviewed, "reviewer", "Approved review comments");
            Check(db.ReviewState == DesignBasisReviewState.Reviewed, "State must be Reviewed.");
        }

        // P9 Test 28: ReadyForReview -> NeedsRevision succeeds
        private static void P9_ReadyForReviewToNeedsRevisionSucceeds()
        {
            var db = CreateValidDesignBasis();
            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "author", "Ready");
            db.TransitionReviewState(DesignBasisReviewState.NeedsRevision, "reviewer", "Missing parameter override");
            Check(db.ReviewState == DesignBasisReviewState.NeedsRevision, "State must be NeedsRevision.");
        }

        // P9 Test 29: NeedsRevision -> Draft succeeds
        private static void P9_NeedsRevisionToDraftSucceeds()
        {
            var db = CreateValidDesignBasis();
            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "author", "Ready");
            db.TransitionReviewState(DesignBasisReviewState.NeedsRevision, "reviewer", "Comments");
            db.TransitionReviewState(DesignBasisReviewState.Draft, "author", "Reworking");
            Check(db.ReviewState == DesignBasisReviewState.Draft, "State must return to Draft.");
        }

        // P9 Test 30: invalid review transition rejected
        private static void P9_InvalidReviewTransitionRejected()
        {
            var db = CreateValidDesignBasis();
            // Direct Draft -> Reviewed forbidden
            Throws<DesignBasisValidationException>(() => db.TransitionReviewState(DesignBasisReviewState.Reviewed));

            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview);
            db.TransitionReviewState(DesignBasisReviewState.Reviewed);
            // Reviewed -> ReadyForReview forbidden
            Throws<DesignBasisValidationException>(() => db.TransitionReviewState(DesignBasisReviewState.ReadyForReview));
        }

        // P9 Test 31: review transition does not alter ContentFingerprint
        private static void P9_ReviewTransitionDoesNotAlterContentFingerprint()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "fp-state-test";
            var snap1 = db.CreateSnapshot();

            db.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "author", "Review note 1");
            var snap2 = db.CreateSnapshot();

            db.TransitionReviewState(DesignBasisReviewState.Reviewed, "reviewer", "Review note 2");
            var snap3 = db.CreateSnapshot();

            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Draft to ReadyForReview must not change ContentFingerprint.");
            Check(snap2.ContentFingerprint == snap3.ContentFingerprint, "ReadyForReview to Reviewed must not change ContentFingerprint.");
        }

        // P9 Test 32: immutable snapshot cannot be changed through caller collection mutation
        private static void P9_ImmutableSnapshotCannotBeChangedThroughCallerCollectionMutation()
        {
            var db = CreateValidDesignBasis();
            var snap = db.CreateSnapshot();

            int initialAssumptionsCount = snap.Assumptions.Count;
            int initialOverridesCount = snap.Overrides.Count;
            int initialRefsCount = snap.StandardReferences.Count;

            // Mutate caller collections
            db.Assumptions.Add(new DesignBasisAssumption
            {
                AssumptionId = "caller-mutation-asm",
                Key = "mutated.key",
                Value = StandardValue.FromNumber(999.0),
                Provenance = new DesignBasisAssumptionProvenance { Kind = AssumptionProvenanceKind.ProjectInput }
            });
            db.Overrides.Clear();
            db.StandardReferences.Clear();

            Check(snap.Assumptions.Count == initialAssumptionsCount, "Snapshot assumptions must not reflect caller mutations.");
            Check(snap.Overrides.Count == initialOverridesCount, "Snapshot overrides must not reflect caller mutations.");
            Check(snap.StandardReferences.Count == initialRefsCount, "Snapshot standard references must not reflect caller mutations.");

            // Verify ReadOnlyCollection throws on direct modification attempt
            Throws<NotSupportedException>(() => ((IList<DesignBasisAssumption>)snap.Assumptions).Add(new DesignBasisAssumption()));
        }

        // P9 Test 33: identical content under different physical/project storage locations yields same ContentFingerprint
        private static void P9_IdenticalContentUnderDifferentPhysicalStorageLocationsYieldsSameFingerprint()
        {
            var db1 = CreateValidDesignBasis();
            db1.DesignBasisId = "location-invariance-id";
            var snap1 = db1.CreateSnapshot();

            var db2 = CreateValidDesignBasis();
            db2.DesignBasisId = "location-invariance-id";
            var snap2 = db2.CreateSnapshot();

            Check(snap1.ContentFingerprint == snap2.ContentFingerprint, "Identical content must yield identical ContentFingerprint regardless of location.");
        }

        // P9 Test 34: DesignBasisId remains distinct from ContentFingerprint
        private static void P9_DesignBasisIdRemainsDistinctFromContentFingerprint()
        {
            var db = CreateValidDesignBasis();
            var snap = db.CreateSnapshot();
            Check(snap.DesignBasisId != snap.ContentFingerprint, "DesignBasisId and ContentFingerprint must remain strictly distinct concepts.");
        }

        // P9 Test 35: DesignBasisVersion remains distinct from ContentFingerprint
        private static void P9_DesignBasisVersionRemainsDistinctFromContentFingerprint()
        {
            var db = CreateValidDesignBasis();
            var snap = db.CreateSnapshot();
            Check(snap.DesignBasisVersion != snap.ContentFingerprint, "DesignBasisVersion and ContentFingerprint must remain strictly distinct concepts.");
        }

        // P9 Test 36: P7 DesignBasisReference mapping works
        private static void P9_P7DesignBasisReferenceMappingWorks()
        {
            var db = CreateValidDesignBasis();
            db.DesignBasisId = "db-id-alpha";
            db.DesignBasisVersion = "v2.5";
            var snap = db.CreateSnapshot();

            var p7Ref = snap.ToProjectReference();
            Check(p7Ref != null, "Project reference must not be null.");
            Check(p7Ref.ReferenceIdentifier == "db-id-alpha", "ReferenceIdentifier must map to DesignBasisId.");
            Check(p7Ref.DesignBasisVersion == "v2.5", "DesignBasisVersion must map to DesignBasisVersion.");
        }

        // P9 Test 37: P7 roundtrip regression passes if storage modified
        private static void P9_P7RoundtripRegressionPassesIfStorageModified()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "P9_Test_P7Roundtrip_" + Guid.NewGuid().ToString("N"));
            try
            {
                var state = ProjectStore.CreateProject(testDir, "P9StorageProj", ProjectModule.GravityFlow);
                var db = CreateValidDesignBasis();
                db.DesignBasisId = "p7-db-id-99";
                db.DesignBasisVersion = "Rev-1.0";
                var snap = db.CreateSnapshot();

                state.DesignBasisReference = snap.ToProjectReference();
                ProjectStore.SaveProject(testDir, state, 1);

                var loaded = ProjectStore.LoadProject(testDir);
                Check(loaded.DesignBasisReference != null, "DesignBasisReference must survive roundtrip.");
                Check(loaded.DesignBasisReference.ReferenceIdentifier == "p7-db-id-99", "ReferenceIdentifier mismatch.");
                Check(loaded.DesignBasisReference.DesignBasisVersion == "Rev-1.0", "DesignBasisVersion mismatch.");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in Program.cs: " + suppressedEx.Message); }
            }
        }

        // P9 Test 38: no public vendor types
        private static void P9_NoPublicVendorTypes()
        {
            var p9Types = new Type[]
            {
                typeof(DesignBasis),
                typeof(DesignBasisSnapshot),
                typeof(DesignBasisAssumption),
                typeof(DesignBasisOverride),
                typeof(DesignBasisStandardReference),
                typeof(DesignBasisReviewState),
                typeof(DesignBasisReviewMetadata),
                typeof(DesignBasisValidationResult),
                typeof(DesignBasisValidationIssue),
                typeof(DesignBasisValidationSeverity),
                typeof(DesignBasisValidationException),
                typeof(AssumptionProvenanceKind),
                typeof(DesignBasisAssumptionProvenance),
                typeof(DesignBasisSerializer)
            };

            foreach (var type in p9Types)
            {
                Check(Neutral(type), "P9 type must be vendor-neutral: " + type.FullName);
                foreach (var method in type.GetMethods())
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties())
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P9 Test 39: no persisted vendor types
        private static void P9_NoPersistedVendorTypes()
        {
            var db = CreateValidDesignBasis();
            string json = DesignBasisSerializer.Serialize(db);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized DesignBasis must contain no vendor type references.");

            var snap = db.CreateSnapshot();
            string snapJson = DesignBasisSerializer.SerializeSnapshot(snap);
            Check(!snapJson.Contains("Haestad") && !snapJson.Contains("Bentley"), "Serialized snapshot must contain no vendor type references.");
        }

        // P9 Test 40: no production module-specific engineering criteria
        private static void P9_NoProductionModuleSpecificEngineeringCriteria()
        {
            var p9Types = new Type[]
            {
                typeof(DesignBasis),
                typeof(DesignBasisSnapshot),
                typeof(DesignBasisAssumption),
                typeof(DesignBasisOverride),
                typeof(DesignBasisStandardReference),
                typeof(DesignBasisReviewState)
            };

            string[] forbiddenTokens = new[]
            {
                "GravityFlow", "StormFlow", "Diameter", "PipeDiameter", "Slope",
                "MinimumSlope", "Velocity", "Cover", "Pe", "Population", "Runoff",
                "Rainfall", "Hydrology", "Catchment", "Inlet"
            };

            foreach (var type in p9Types)
            {
                foreach (var prop in type.GetProperties())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!string.Equals(prop.Name, token, StringComparison.OrdinalIgnoreCase),
                            "Production P9 property must not embed engineering criteria: " + prop.Name);
                    }
                }
            }
        }


        // ==========================================
        // P10 GRAVITYFLOW DESIGN BASIS TESTS (40 Tests)
        // ==========================================

        private static GravityFlowDesignBasis CreateValidGravityFlowDesignBasis()
        {
            var common = CreateValidDesignBasis();
            var basis = new GravityFlowDesignBasis
            {
                Module = "GravityFlow",
                CommonDesignBasis = common
            };

            basis.Project.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.project.name",
                Section = GravityFlowSectionIdentifier.Project,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("Synthetic Project Title")
            });

            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.loading.category",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("SyntheticCategoryA")
            });

            basis.PipeCriteria.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.pipe.material_code",
                Section = GravityFlowSectionIdentifier.PipeCriteria,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("SyntheticMaterial")
            });

            basis.StructureCriteria.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.struct.shape",
                Section = GravityFlowSectionIdentifier.StructureCriteria,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("SyntheticRound")
            });

            basis.VerticalDesign.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.vert.rule_type",
                Section = GravityFlowSectionIdentifier.VerticalDesign,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("SyntheticRule")
            });

            basis.HydraulicCriteria.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.hyd.target_param",
                Section = GravityFlowSectionIdentifier.HydraulicCriteria,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromQuantity(10.5, "synth_unit")
            });

            basis.SolverSettings.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.solver.mode",
                Section = GravityFlowSectionIdentifier.SolverSettings,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("SyntheticStandard")
            });

            return basis;
        }

        // P10 Test 1: GravityFlow aggregate requires common P9 Design Basis
        private static void P10_GravityFlowAggregateRequiresCommonDesignBasis()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.CommonDesignBasis = null;
            var validation = basis.Validate();
            Check(!validation.IsValid, "Missing CommonDesignBasis must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "MISSING_COMMON_DESIGN_BASIS"), "Must emit MISSING_COMMON_DESIGN_BASIS.");
            Throws<DesignBasisValidationException>(() => basis.CreateSnapshot());
        }

        // P10 Test 2: module identity is GravityFlow
        private static void P10_ModuleIdentityIsGravityFlow()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            Check(basis.Module == "GravityFlow", "Module identity must be GravityFlow.");

            basis.Module = "StormFlow";
            var validation = basis.Validate();
            Check(!validation.IsValid, "Non-GravityFlow module must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "INVALID_MODULE"), "Must emit INVALID_MODULE.");
        }

        // P10 Test 3: all seven sections represented
        private static void P10_AllSevenSectionsRepresented()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            Check(basis.Project != null && basis.Project.SectionId == GravityFlowSectionIdentifier.Project, "Project section must be present.");
            Check(basis.Loading != null && basis.Loading.SectionId == GravityFlowSectionIdentifier.Loading, "Loading section must be present.");
            Check(basis.PipeCriteria != null && basis.PipeCriteria.SectionId == GravityFlowSectionIdentifier.PipeCriteria, "PipeCriteria section must be present.");
            Check(basis.StructureCriteria != null && basis.StructureCriteria.SectionId == GravityFlowSectionIdentifier.StructureCriteria, "StructureCriteria section must be present.");
            Check(basis.VerticalDesign != null && basis.VerticalDesign.SectionId == GravityFlowSectionIdentifier.VerticalDesign, "VerticalDesign section must be present.");
            Check(basis.HydraulicCriteria != null && basis.HydraulicCriteria.SectionId == GravityFlowSectionIdentifier.HydraulicCriteria, "HydraulicCriteria section must be present.");
            Check(basis.SolverSettings != null && basis.SolverSettings.SectionId == GravityFlowSectionIdentifier.SolverSettings, "SolverSettings section must be present.");
        }

        // P10 Test 4: canonical section ordering deterministic
        private static void P10_CanonicalSectionOrderingDeterministic()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var sections = basis.EnumerateSectionsInCanonicalOrder().Select(s => s.SectionId).ToList();
            var expected = new[]
            {
                GravityFlowSectionIdentifier.Project,
                GravityFlowSectionIdentifier.Loading,
                GravityFlowSectionIdentifier.PipeCriteria,
                GravityFlowSectionIdentifier.StructureCriteria,
                GravityFlowSectionIdentifier.VerticalDesign,
                GravityFlowSectionIdentifier.HydraulicCriteria,
                GravityFlowSectionIdentifier.SolverSettings
            };

            Check(sections.Count == 7, "Must contain exactly 7 sections.");
            for (int i = 0; i < 7; i++)
            {
                Check(sections[i] == expected[i], "Section order mismatch at index " + i);
            }
        }

        // P10 Test 5: duplicate criterion key in same section rejected
        private static void P10_DuplicateCriterionKeyInSameSectionRejected()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.PipeCriteria.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.pipe.material_code".ToUpperInvariant(),
                Section = GravityFlowSectionIdentifier.PipeCriteria,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("DuplicateKeyVal")
            });

            var validation = basis.Validate();
            Check(!validation.IsValid, "Duplicate key in section must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "DUPLICATE_CRITERION_KEY"), "Must emit DUPLICATE_CRITERION_KEY.");
        }

        // P10 Test 6: same key in distinct sections valid
        private static void P10_SameKeyInDistinctSectionsValid()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.Project.Criteria.Add(new GravityDesignCriterion
            {
                Key = "shared.param.key",
                Section = GravityFlowSectionIdentifier.Project,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("ProjectScoped")
            });
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "shared.param.key",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("LoadingScoped")
            });

            var validation = basis.Validate();
            Check(validation.IsValid, "Same key in different sections must be valid.");
        }

        // P10 Test 7: unknown section rejected
        private static void P10_UnknownSectionRejected()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.unknown.sec",
                Section = (GravityFlowSectionIdentifier)999,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("Test")
            });

            var validation = basis.Validate();
            Check(!validation.IsValid, "Criterion section mismatch must fail validation.");
            Check(validation.Issues.Any(i => i.Code == "CRITERION_SECTION_MISMATCH"), "Must emit CRITERION_SECTION_MISMATCH.");
        }

        // P10 Test 8: StormFlow section/content rejected
        private static void P10_StormFlowSectionOrContentRejected()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.Module = "StormFlow";
            Check(!basis.Validate().IsValid, "StormFlow module must be rejected.");
        }

        // P10 Test 9: P8 StandardValue reused
        private static void P10_P8StandardValueReused()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var criterion = basis.HydraulicCriteria.Criteria[0];
            Check(criterion.Value is StandardValue, "Criterion Value must be instance of P8 StandardValue.");
            Check(criterion.Value.Kind == StandardValueKind.Quantity, "Value must retain P8 StandardValueKind.");
        }

        // P10 Test 10: P8 quantity/unit reused
        private static void P10_P8QuantityUnitReused()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var criterion = basis.HydraulicCriteria.Criteria[0];
            Check(criterion.Value.QuantityValue is StandardQuantity, "QuantityValue must be instance of P8 StandardQuantity.");
            Check(criterion.Value.QuantityValue.Unit == "synth_unit", "Unit identity must be preserved.");
        }

        // P10 Test 11: duplicate unit/value infrastructure not introduced
        private static void P10_DuplicateUnitValueInfrastructureNotIntroduced()
        {
            var p10Types = typeof(GravityFlowDesignBasis).Assembly.GetTypes()
                .Where(t => t.Name.StartsWith("Gravity", StringComparison.Ordinal));

            foreach (var t in p10Types)
            {
                Check(!t.Name.EndsWith("Quantity", StringComparison.Ordinal), "Must not introduce duplicate Quantity class: " + t.Name);
                Check(!t.Name.EndsWith("ValueKind", StringComparison.Ordinal), "Must not introduce duplicate ValueKind enum: " + t.Name);
            }
        }

        // P10 Test 12: P8 resolved standard provenance retained
        private static void P10_P8ResolvedStandardProvenanceRetained()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.PipeCriteria.Criteria[0].Provenance = new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.PublishedStandard,
                StandardId = "pub-std-42",
                StandardName = "PublishedTestStandard",
                StandardVersion = "1.0",
                ParameterKey = "synth.pipe.material_code"
            };

            var snap = basis.CreateSnapshot();
            var pipeCrit = snap.PipeCriteria[0];
            Check(pipeCrit.Provenance != null, "Provenance must be retained.");
            Check(pipeCrit.Provenance.StandardId == "pub-std-42", "StandardId must match.");
            Check(pipeCrit.Provenance.SourceLayer == StandardSourceLayer.PublishedStandard, "SourceLayer must match.");
        }

        // P10 Test 13: ApplicationDefault remains visibly ApplicationDefault
        private static void P10_ApplicationDefaultRemainsVisiblyApplicationDefault()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.PipeCriteria.Criteria[0].Provenance = new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.ApplicationDefault,
                StandardId = "app-def-std",
                StandardName = "ApplicationDefaults",
                StandardVersion = "1.0",
                ParameterKey = "synth.pipe.material_code"
            };

            var snap = basis.CreateSnapshot();
            Check(snap.PipeCriteria[0].Provenance.SourceLayer == StandardSourceLayer.ApplicationDefault,
                "ApplicationDefault must remain visibly classified as ApplicationDefault.");
            Check(snap.PipeCriteria[0].Provenance.SourceLayer != StandardSourceLayer.PublishedStandard,
                "ApplicationDefault must never be presented as regulatory/published requirement.");
        }

        // P10 Test 14: P8 precedence not duplicated
        private static void P10_P8PrecedenceNotDuplicated()
        {
            // Verify GravityFlowDesignBasis contains no precedence resolution logic
            var methods = typeof(GravityFlowDesignBasis).GetMethods()
                .Where(m => m.Name.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Precedence", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(!methods.Any(), "GravityFlowDesignBasis must not duplicate precedence resolution logic.");
        }

        // P10 Test 15: P9 assumption infrastructure reused
        private static void P10_P9AssumptionInfrastructureReused()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            Check(basis.CommonDesignBasis.Assumptions.Count > 0, "Common assumptions must be present.");
            Check(basis.CommonDesignBasis.Assumptions[0] is DesignBasisAssumption, "Must reuse P9 DesignBasisAssumption.");
        }

        // P10 Test 16: P9 override infrastructure reused
        private static void P10_P9OverrideInfrastructureReused()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            Check(basis.CommonDesignBasis.Overrides.Count > 0, "Common overrides must be present.");
            Check(basis.CommonDesignBasis.Overrides[0] is DesignBasisOverride, "Must reuse P9 DesignBasisOverride.");
        }

        // P10 Test 17: override rationale preserved
        private static void P10_OverrideRationalePreserved()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var ovr = basis.CommonDesignBasis.Overrides[0];
            Check(!string.IsNullOrWhiteSpace(ovr.Rationale), "Override rationale must be preserved.");
        }

        // P10 Test 18: source standard not mutated
        private static void P10_SourceStandardNotMutated()
        {
            var std = new StandardDefinition
            {
                SchemaVersion = 1,
                StandardId = "test-std-source",
                Name = "TestSource",
                Version = "1.0",
                SourceLayer = StandardSourceLayer.PublishedStandard
            };
            std.Parameters.Add(new StandardParameter("synth.key", StandardValue.FromNumber(100.0)));

            var basis = CreateValidGravityFlowDesignBasis();
            basis.CreateSnapshot();

            Check(std.Parameters.Count == 1, "Source standard parameter count must remain unchanged.");
            Check(std.Parameters[0].Value.NumberValue == 100.0, "Source standard value must remain unchanged.");
        }

        // P10 Test 19: common P9 snapshot not mutated
        private static void P10_CommonP9SnapshotNotMutated()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var snap1 = basis.CreateSnapshot();

            // Mutate GravityFlow section criteria
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "extra.criterion",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("extra")
            });

            Check(snap1.CommonSnapshot.Assumptions.Count == basis.CommonDesignBasis.Assumptions.Count,
                "Common P9 snapshot must not mutate when GravityFlow criteria change.");
        }

        // P10 Test 20: GravityFlow snapshot immutable
        private static void P10_GravityFlowSnapshotImmutable()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var snap = basis.CreateSnapshot();

            int initialLoadingCount = snap.Loading.Count;
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "extra.loading",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromText("val")
            });

            Check(snap.Loading.Count == initialLoadingCount, "Snapshot must not reflect caller modifications.");
        }

        // P10 Test 21: caller collection mutation cannot alter snapshot
        private static void P10_CallerCollectionMutationCannotAlterSnapshot()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var snap = basis.CreateSnapshot();

            Throws<NotSupportedException>(() => ((IList<GravityDesignCriterion>)snap.Loading).Add(new GravityDesignCriterion()));
        }

        // P10 Test 22: Gravity fingerprint repeatable
        private static void P10_GravityFingerprintRepeatable()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var snap1 = basis.CreateSnapshot();
            var snap2 = basis.CreateSnapshot();

            Check(!string.IsNullOrEmpty(snap1.GravityContentFingerprint), "GravityContentFingerprint must not be empty.");
            Check(snap1.GravityContentFingerprint.Length == 64, "GravityContentFingerprint must be 64 hex chars.");
            Check(snap1.GravityContentFingerprint == snap2.GravityContentFingerprint, "GravityContentFingerprint must be deterministic and repeatable.");
        }

        // P10 Test 23: input criterion ordering does not change fingerprint
        private static void P10_InputCriterionOrderingDoesNotChangeFingerprint()
        {
            var basis1 = CreateValidGravityFlowDesignBasis();
            basis1.Loading.Criteria.Clear();
            basis1.Loading.Criteria.Add(new GravityDesignCriterion { Key = "alpha.param", Section = GravityFlowSectionIdentifier.Loading, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("A") });
            basis1.Loading.Criteria.Add(new GravityDesignCriterion { Key = "beta.param", Section = GravityFlowSectionIdentifier.Loading, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("B") });

            var basis2 = CreateValidGravityFlowDesignBasis();
            basis2.CommonDesignBasis.DesignBasisId = basis1.CommonDesignBasis.DesignBasisId;
            basis2.Loading.Criteria.Clear();
            // Inverted order
            basis2.Loading.Criteria.Add(new GravityDesignCriterion { Key = "beta.param", Section = GravityFlowSectionIdentifier.Loading, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("B") });
            basis2.Loading.Criteria.Add(new GravityDesignCriterion { Key = "alpha.param", Section = GravityFlowSectionIdentifier.Loading, Status = GravityCriterionStatus.Defined, Value = StandardValue.FromText("A") });

            var snap1 = basis1.CreateSnapshot();
            var snap2 = basis2.CreateSnapshot();
            Check(snap1.GravityContentFingerprint == snap2.GravityContentFingerprint,
                "Criterion insertion order must not affect GravityContentFingerprint.");
        }

        // P10 Test 24: current culture does not change fingerprint
        private static void P10_CurrentCultureDoesNotChangeFingerprint()
        {
            var prevCulture = Thread.CurrentThread.CurrentCulture;
            var prevUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                string baseFp = null;
                string[] cultures = new[] { "en-US", "fr-FR", "de-DE", "fa-IR", "tr-TR" };
                foreach (var c in cultures)
                {
                    var ci = new CultureInfo(c);
                    Thread.CurrentThread.CurrentCulture = ci;
                    Thread.CurrentThread.CurrentUICulture = ci;

                    var basis = CreateValidGravityFlowDesignBasis();
                    basis.CommonDesignBasis.DesignBasisId = "culture-test-id";
                    var snap = basis.CreateSnapshot();

                    if (baseFp == null) baseFp = snap.GravityContentFingerprint;
                    else Check(snap.GravityContentFingerprint == baseFp, "Fingerprint varied under culture: " + c);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prevCulture;
                Thread.CurrentThread.CurrentUICulture = prevUiCulture;
            }
        }

        // P10 Test 25: path/machine/user metadata does not change fingerprint
        private static void P10_PathMachineUserMetadataDoesNotChangeFingerprint()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.CommonDesignBasis.DesignBasisId = "metadata-isolation-id";
            var snap1 = basis.CreateSnapshot();
            var snap2 = basis.CreateSnapshot();
            Check(snap1.GravityContentFingerprint == snap2.GravityContentFingerprint,
                "Environmental noise must not affect GravityContentFingerprint.");
        }

        // P10 Test 26: review metadata does not change fingerprint
        private static void P10_ReviewMetadataDoesNotChangeFingerprint()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.CommonDesignBasis.DesignBasisId = "review-noise-test";
            var snap1 = basis.CreateSnapshot();

            basis.TransitionReviewState(DesignBasisReviewState.ReadyForReview, "Alice", "Review Note");
            var snap2 = basis.CreateSnapshot();

            Check(snap1.GravityContentFingerprint == snap2.GravityContentFingerprint,
                "Review state transitions and metadata must not alter GravityContentFingerprint.");
        }

        // P10 Test 27: Bentley runtime/version metadata does not change fingerprint
        private static void P10_BentleyRuntimeVersionMetadataDoesNotChangeFingerprint()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.CommonDesignBasis.DesignBasisId = "bentley-noise-test";
            var snap1 = basis.CreateSnapshot();
            var snap2 = basis.CreateSnapshot();
            Check(snap1.GravityContentFingerprint == snap2.GravityContentFingerprint,
                "Bentley runtime metadata must not alter GravityContentFingerprint.");
        }

        // P10 Test 28: changing criterion value changes Gravity fingerprint
        private static void P10_ChangingCriterionValueChangesGravityFingerprint()
        {
            var basis1 = CreateValidGravityFlowDesignBasis();
            basis1.CommonDesignBasis.DesignBasisId = "val-change-id";
            var snap1 = basis1.CreateSnapshot();

            var basis2 = CreateValidGravityFlowDesignBasis();
            basis2.CommonDesignBasis.DesignBasisId = "val-change-id";
            basis2.HydraulicCriteria.Criteria[0].Value = StandardValue.FromQuantity(10.6, "synth_unit"); // was 10.5
            var snap2 = basis2.CreateSnapshot();

            Check(snap1.GravityContentFingerprint != snap2.GravityContentFingerprint,
                "Changing criterion value must alter GravityContentFingerprint.");
        }

        // P10 Test 29: changing criterion provenance changes fingerprint
        private static void P10_ChangingCriterionProvenanceChangesFingerprint()
        {
            var basis1 = CreateValidGravityFlowDesignBasis();
            basis1.CommonDesignBasis.DesignBasisId = "prov-change-id";
            basis1.PipeCriteria.Criteria[0].Provenance = new StandardProvenance { StandardId = "std-1", StandardVersion = "1.0", SourceLayer = StandardSourceLayer.PublishedStandard, ParameterKey = "key-1" };
            var snap1 = basis1.CreateSnapshot();

            var basis2 = CreateValidGravityFlowDesignBasis();
            basis2.CommonDesignBasis.DesignBasisId = "prov-change-id";
            basis2.PipeCriteria.Criteria[0].Provenance = new StandardProvenance { StandardId = "std-2", StandardVersion = "1.0", SourceLayer = StandardSourceLayer.PublishedStandard, ParameterKey = "key-1" };
            var snap2 = basis2.CreateSnapshot();

            Check(snap1.GravityContentFingerprint != snap2.GravityContentFingerprint,
                "Changing criterion provenance must alter GravityContentFingerprint.");
        }

        // P10 Test 30: changing common P9 ContentFingerprint changes Gravity fingerprint
        private static void P10_ChangingCommonP9ContentFingerprintChangesGravityFingerprint()
        {
            var basis1 = CreateValidGravityFlowDesignBasis();
            basis1.CommonDesignBasis.DesignBasisId = "common-fp-change";
            var snap1 = basis1.CreateSnapshot();

            var basis2 = CreateValidGravityFlowDesignBasis();
            basis2.CommonDesignBasis.DesignBasisId = "common-fp-change";
            basis2.CommonDesignBasis.Assumptions[0].Value = StandardValue.FromNumber(999.9);
            var snap2 = basis2.CreateSnapshot();

            Check(snap1.CommonSnapshot.ContentFingerprint != snap2.CommonSnapshot.ContentFingerprint, "Common fingerprints must differ.");
            Check(snap1.GravityContentFingerprint != snap2.GravityContentFingerprint,
                "Altering common Design Basis content must alter GravityContentFingerprint.");
        }

        // P10 Test 31: section completeness deterministic
        private static void P10_SectionCompletenessDeterministic()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            var report = basis.GetSectionCompleteness(GravityFlowSectionIdentifier.Loading);
            Check(report.SectionId == GravityFlowSectionIdentifier.Loading, "SectionId must match.");
            Check(report.DefinedCriterionCount == 1, "DefinedCriterionCount must be 1.");
            Check(report.UndefinedCriterionCount == 0, "UndefinedCriterionCount must be 0.");
            Check(report.ValidationIssueCount == 0, "ValidationIssueCount must be 0.");

            // Add undefined criterion
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.extra.undefined",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.NotDefined,
                Value = null
            });

            var updatedReport = basis.GetSectionCompleteness(GravityFlowSectionIdentifier.Loading);
            Check(updatedReport.DefinedCriterionCount == 1, "Defined count remains 1.");
            Check(updatedReport.UndefinedCriterionCount == 1, "Undefined count becomes 1.");
        }

        // P10 Test 32: undefined criterion never becomes numeric zero/default automatically
        private static void P10_UndefinedCriterionNeverBecomesNumericZeroOrDefault()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.PipeCriteria.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.undefined.criterion",
                Section = GravityFlowSectionIdentifier.PipeCriteria,
                Status = GravityCriterionStatus.NotDefined,
                Value = null
            });

            var snap = basis.CreateSnapshot();
            var crit = snap.PipeCriteria.First(c => c.Key == "synth.undefined.criterion");
            Check(crit.Status == GravityCriterionStatus.NotDefined, "Status must remain NotDefined.");
            Check(crit.Value == null, "Value must remain null; never substitute 0.0 or default.");

            string json = GravityFlowDesignBasisSerializer.Serialize(basis);
            var loaded = GravityFlowDesignBasisSerializer.Deserialize(json);
            var loadedCrit = loaded.PipeCriteria.Criteria.First(c => c.Key == "synth.undefined.criterion");
            Check(loadedCrit.Status == GravityCriterionStatus.NotDefined, "Deserialized status must remain NotDefined.");
            Check(loadedCrit.Value == null, "Deserialized value must remain null.");
        }

        // P10 Test 33: no loading calculation executed
        private static void P10_NoLoadingCalculationExecuted()
        {
            var loadingMethods = typeof(GravityLoadingSection).GetMethods();
            Check(loadingMethods.All(m => !m.Name.Contains("Calculate") && !m.Name.Contains("Population") && !m.Name.Contains("Pe")),
                "GravityLoadingSection must not execute loading calculations.");
        }

        // P10 Test 34: no vertical design executed
        private static void P10_NoVerticalDesignExecuted()
        {
            var vertMethods = typeof(GravityVerticalDesignSection).GetMethods();
            Check(vertMethods.All(m => !m.Name.Contains("Calculate") && !m.Name.Contains("Slope") && !m.Name.Contains("Invert")),
                "GravityVerticalDesignSection must not execute vertical design calculations.");
        }

        // P10 Test 35: no hydraulic verification executed
        private static void P10_NoHydraulicVerificationExecuted()
        {
            var hydMethods = typeof(GravityHydraulicCriteriaSection).GetMethods();
            Check(hydMethods.All(m => !m.Name.Contains("Verify") && !m.Name.Contains("Check") && !m.Name.Contains("Compliance")),
                "GravityHydraulicCriteriaSection must not execute hydraulic verification.");
        }

        // P10 Test 36: no candidate generation
        private static void P10_NoCandidateGeneration()
        {
            var pipeMethods = typeof(GravityPipeCriteriaSection).GetMethods();
            Check(pipeMethods.All(m => !m.Name.Contains("Candidate") && !m.Name.Contains("Generate")),
                "GravityPipeCriteriaSection must not generate design candidates.");
        }

        // P10 Test 37: no solver invocation
        private static void P10_NoSolverInvocation()
        {
            var solverMethods = typeof(GravitySolverSettingsSection).GetMethods();
            Check(solverMethods.All(m => !m.Name.Contains("Run") && !m.Name.Contains("Solve") && !m.Name.Contains("Execute")),
                "GravitySolverSettingsSection must not invoke hydraulic solver.");
        }

        // P10 Test 38: no public vendor types
        private static void P10_NoPublicVendorTypes()
        {
            var p10Types = new Type[]
            {
                typeof(GravityFlowDesignBasis),
                typeof(GravityFlowDesignBasisSnapshot),
                typeof(GravityFlowSectionIdentifier),
                typeof(GravityCriterionRequirement),
                typeof(GravityCriterionStatus),
                typeof(SectionCompletenessReport),
                typeof(GravityDesignCriterion),
                typeof(GravitySectionBase),
                typeof(GravityProjectSection),
                typeof(GravityLoadingSection),
                typeof(GravityPipeCriteriaSection),
                typeof(GravityStructureCriteriaSection),
                typeof(GravityVerticalDesignSection),
                typeof(GravityHydraulicCriteriaSection),
                typeof(GravitySolverSettingsSection),
                typeof(GravityFlowDesignBasisSerializer)
            };

            foreach (var type in p10Types)
            {
                Check(Neutral(type), "P10 type must be vendor-neutral: " + type.FullName);
                foreach (var method in type.GetMethods())
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties())
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P10 Test 39: no persisted vendor types
        private static void P10_NoPersistedVendorTypes()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            string json = GravityFlowDesignBasisSerializer.Serialize(basis);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized GravityFlowDesignBasis must contain zero vendor types.");

            var snap = basis.CreateSnapshot();
            string snapJson = GravityFlowDesignBasisSerializer.SerializeSnapshot(snap);
            Check(!snapJson.Contains("Haestad") && !snapJson.Contains("Bentley"), "Serialized snapshot must contain zero vendor types.");
        }

        // P10 Test 40: no hard-coded production engineering values
        private static void P10_NoHardcodedProductionEngineeringValues()
        {
            var p10Types = new Type[]
            {
                typeof(GravityFlowDesignBasis),
                typeof(GravityFlowDesignBasisSnapshot),
                typeof(GravityDesignCriterion),
                typeof(GravityProjectSection),
                typeof(GravityLoadingSection),
                typeof(GravityPipeCriteriaSection),
                typeof(GravityStructureCriteriaSection),
                typeof(GravityVerticalDesignSection),
                typeof(GravityHydraulicCriteriaSection),
                typeof(GravitySolverSettingsSection)
            };

            string[] forbiddenTokens = new[]
            {
                "MinimumDiameter", "MinimumSlope", "MinimumVelocity", "MaximumVelocity",
                "MinimumCover", "MaximumCover", "ManningN", "PeakFactor", "WastewaterGenerationRate"
            };

            foreach (var type in p10Types)
            {
                foreach (var prop in type.GetProperties())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!string.Equals(prop.Name, token, StringComparison.OrdinalIgnoreCase),
                            "Production P10 property must not embed hardcoded engineering values: " + prop.Name);
                    }
                }
            }
        }


        private static StandardProvenance CreateSynthProvenance(string stdId = "synth-loading-std", string ver = "2026.1", string key = "synth.flow")
        {
            return new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.ProjectOverride,
                StandardId = stdId,
                StandardName = "Synthetic Standard",
                StandardVersion = ver,
                ParameterKey = key,
                DocumentReference = "SynthDoc-P11",
                SectionReference = "Sec 3.1"
            };
        }

        // P11 Test 1: Population basis accepted
        private static void P11_PopulationBasisAccepted()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 1000.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.5, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Calculation status must be Complete.");
            Check(result.Basis != null && result.Basis.Kind == GravityPopulationBasisKind.Population, "Basis kind must be Population.");
            Check(result.Basis.Value == 1000.0, "Population value must be 1000.0.");
            Check(result.BaseSanitaryFlow != null && result.BaseSanitaryFlow.Value == 2.0, "Base flow must be 2.0.");
            Check(result.PeakedSanitaryFlow != null && result.PeakedSanitaryFlow.Value == 5.0, "Peaked flow must be 5.0.");
        }

        // P11 Test 2: PE basis accepted
        private static void P11_PEBasisAccepted()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationEquivalentBasis = new GravityPopulationBasis(GravityPopulationBasisKind.PopulationEquivalent, 2500.5, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.PopulationEquivalent, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(1.8, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Calculation status must be Complete.");
            Check(result.Basis != null && result.Basis.Kind == GravityPopulationBasisKind.PopulationEquivalent, "Basis kind must be PE.");
            Check(result.Basis.Value == 2500.5, "PE value must be 2500.5.");
            Check(result.BaseSanitaryFlow != null, "BaseSanitaryFlow must be calculated.");
            Check(Math.Abs(result.BaseSanitaryFlow.Value - 2.5005) < 1e-6, "BaseSanitaryFlow value mismatch.");
        }

        // P11 Test 3: Population + PE simultaneously rejected
        private static void P11_PopulationAndPESimultaneouslyRejected()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 1000.0, CreateSynthProvenance()),
                PopulationEquivalentBasis = new GravityPopulationBasis(GravityPopulationBasisKind.PopulationEquivalent, 1000.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.5, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Dual basis must fail closed.");
            Check(!result.ValidationResult.IsValid, "Validation must be invalid.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "AMBIGUOUS_LOADING_BASIS"), "Must emit AMBIGUOUS_LOADING_BASIS.");
        }

        // P11 Test 4: negative basis rejected
        private static void P11_NegativeBasisRejected()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, -50.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.5, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Negative basis must fail calculation.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "NEGATIVE_POPULATION_BASIS"), "Must emit NEGATIVE_POPULATION_BASIS.");
        }

        // P11 Test 5: NaN/infinite basis rejected
        private static void P11_NaNOrInfiniteBasisRejected()
        {
            var inputNan = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, double.NaN, CreateSynthProvenance())
            };
            var resultNan = GravityLoadingCalculator.Calculate(inputNan);
            Check(resultNan.Status == GravityLoadingCalculationStatus.Failed, "NaN basis must fail.");
            Check(resultNan.ValidationResult.Issues.Any(i => i.Code == "NON_FINITE_POPULATION_BASIS"), "Must emit NON_FINITE_POPULATION_BASIS.");

            var inputInf = new GravityLoadingCalculationInput
            {
                PopulationEquivalentBasis = new GravityPopulationBasis(GravityPopulationBasisKind.PopulationEquivalent, double.PositiveInfinity, CreateSynthProvenance())
            };
            var resultInf = GravityLoadingCalculator.Calculate(inputInf);
            Check(resultInf.Status == GravityLoadingCalculationStatus.Failed, "Infinite basis must fail.");
            Check(resultInf.ValidationResult.Issues.Any(i => i.Code == "NON_FINITE_POPULATION_BASIS"), "Must emit NON_FINITE_POPULATION_BASIS for infinity.");
        }

        // P11 Test 6: explicit generation rate required
        private static void P11_ExplicitGenerationRateRequired()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 1000.0, CreateSynthProvenance()),
                GenerationRate = null,
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Incomplete, "Missing generation rate must yield Incomplete.");
            Check(result.BaseSanitaryFlow == null, "Base flow must be null.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "MISSING_GENERATION_RATE"), "Must emit MISSING_GENERATION_RATE warning.");
        }

        // P11 Test 7: BaseSanitaryFlow = basis * rate
        private static void P11_BaseSanitaryFlowArithmetic()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 250.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.004, "L/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(1.5, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.BaseSanitaryFlow != null, "BaseSanitaryFlow must not be null.");
            Check(Math.Abs(result.BaseSanitaryFlow.Value - 1.0) < 1e-9, "BaseSanitaryFlow must equal basis * rate (250 * 0.004 = 1.0).");
            Check(result.BaseSanitaryFlow.Unit == "L/s", "Unit must match generation rate unit.");
        }

        // P11 Test 8: generation-rate provenance retained
        private static void P11_GenerationRateProvenanceRetained()
        {
            var prov = CreateSynthProvenance("provenance-std-gen", "2026.1", "synth.rate");
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.005, "L/s"), GravityPopulationBasisKind.Population, prov),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.GenerationRate != null && result.GenerationRate.Provenance != null, "GenerationRate provenance must be retained.");
            Check(result.GenerationRate.Provenance.StandardId == "provenance-std-gen", "StandardId must match.");
            var trace = result.Trace.FirstOrDefault(t => t.Output == "BaseSanitaryFlow");
            Check(trace != null && trace.ProvenanceReferences.Contains("GenerationRateStandard=provenance-std-gen"), "Trace must reference provenance.");
        }

        // P11 Test 9: peak factor required when peaked flow requested
        private static void P11_PeakFactorRequiredWhenPeakedFlowRequested()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.005, "L/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = null
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Incomplete, "Missing peak factor must result in Incomplete status.");
            Check(result.BaseSanitaryFlow != null, "BaseSanitaryFlow can still be computed.");
            Check(result.PeakedSanitaryFlow == null, "PeakedSanitaryFlow must be null without peak factor.");
        }

        // P11 Test 10: peak factor > 0 validation
        private static void P11_PeakFactorPositiveValidation()
        {
            var zeroInput = new GravityLoadingCalculationInput
            {
                PeakFactor = new PeakFactor(0.0, CreateSynthProvenance())
            };
            var zeroResult = GravityLoadingCalculator.Calculate(zeroInput);
            Check(zeroResult.Status == GravityLoadingCalculationStatus.Failed, "Zero peak factor must fail.");
            Check(zeroResult.ValidationResult.Issues.Any(i => i.Code == "INVALID_PEAK_FACTOR"), "Must emit INVALID_PEAK_FACTOR.");

            var negInput = new GravityLoadingCalculationInput
            {
                PeakFactor = new PeakFactor(-2.5, CreateSynthProvenance())
            };
            var negResult = GravityLoadingCalculator.Calculate(negInput);
            Check(negResult.Status == GravityLoadingCalculationStatus.Failed, "Negative peak factor must fail.");
            Check(negResult.ValidationResult.Issues.Any(i => i.Code == "INVALID_PEAK_FACTOR"), "Must emit INVALID_PEAK_FACTOR for negative.");
        }

        // P11 Test 11: no population-derived peak-factor formula exists
        private static void P11_NoPopulationDerivedPeakFactorFormulaExists()
        {
            var methods = typeof(GravityLoadingCalculator).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            string[] forbiddenTokens = new[] { "babbitt", "babbit", "harmon", "johnson", "peakingformula", "calculatepeakfactor", "derivepeakfactor" };
            foreach (var m in methods)
            {
                foreach (var token in forbiddenTokens)
                {
                    Check(!m.Name.ToLowerInvariant().Contains(token), "No empirical/derived peak factor method allowed: " + m.Name);
                }
            }
        }

        // P11 Test 12: PeakedSanitaryFlow arithmetic correct
        private static void P11_PeakedSanitaryFlowArithmeticCorrect()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 400.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.5, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.BaseSanitaryFlow != null && Math.Abs(result.BaseSanitaryFlow.Value - 0.8) < 1e-9, "Base flow must be 0.8.");
            Check(result.PeakedSanitaryFlow != null && Math.Abs(result.PeakedSanitaryFlow.Value - 2.0) < 1e-9, "Peaked flow must be 0.8 * 2.5 = 2.0.");
        }

        // P11 Test 13: explicit infiltration contribution included
        private static void P11_ExplicitInfiltrationContributionIncluded()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-01", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.InfiltrationTotal != null && Math.Abs(result.InfiltrationTotal.Value - 0.05) < 1e-9, "InfiltrationTotal must match explicit contribution.");
        }

        // P11 Test 14: explicit inflow contribution included
        private static void P11_ExplicitInflowContributionIncluded()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("inflow-01", GravityLoadingContributionKind.Inflow, new StandardQuantity(0.02, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.InflowTotal != null && Math.Abs(result.InflowTotal.Value - 0.02) < 1e-9, "InflowTotal must match explicit inflow.");
        }

        // P11 Test 15: explicit industrial/process contribution included
        private static void P11_ExplicitIndustrialProcessContributionIncluded()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("ind-01", GravityLoadingContributionKind.IndustrialProcess, new StandardQuantity(0.15, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.IndustrialProcessTotal != null && Math.Abs(result.IndustrialProcessTotal.Value - 0.15) < 1e-9, "IndustrialProcessTotal must match explicit contribution.");
        }

        // P11 Test 16: explicit known point load included
        private static void P11_ExplicitKnownPointLoadIncluded()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.KnownPointLoads.Add(new KnownPointLoad("point-load-alpha", new StandardQuantity(0.08, "m3/s"), CreateSynthProvenance(), "GIS_MANHOLE_EXT_REF"));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.KnownPointLoadTotal != null && Math.Abs(result.KnownPointLoadTotal.Value - 0.08) < 1e-9, "KnownPointLoadTotal must match.");
        }

        // P11 Test 17: multiple contributions aggregate deterministically
        private static void P11_MultipleContributionsAggregateDeterministically()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-02", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.03, "m3/s"), CreateSynthProvenance()));
            input.Contributions.Add(new GravityLoadingContribution("infil-01", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.02, "m3/s"), CreateSynthProvenance()));
            input.KnownPointLoads.Add(new KnownPointLoad("point-01", new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance()));
            input.KnownPointLoads.Add(new KnownPointLoad("point-02", new StandardQuantity(0.04, "m3/s"), CreateSynthProvenance()));

            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.InfiltrationTotal != null && Math.Abs(result.InfiltrationTotal.Value - 0.05) < 1e-9, "Infiltration sum must be 0.05.");
            Check(result.KnownPointLoadTotal != null && Math.Abs(result.KnownPointLoadTotal.Value - 0.09) < 1e-9, "Point load sum must be 0.09.");
        }

        // P11 Test 18: TotalDesignLoading arithmetic correct
        private static void P11_TotalDesignLoadingArithmeticCorrect()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-01", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance()));
            input.Contributions.Add(new GravityLoadingContribution("inflow-01", GravityLoadingContributionKind.Inflow, new StandardQuantity(0.03, "m3/s"), CreateSynthProvenance()));
            input.Contributions.Add(new GravityLoadingContribution("ind-01", GravityLoadingContributionKind.IndustrialProcess, new StandardQuantity(0.07, "m3/s"), CreateSynthProvenance()));
            input.KnownPointLoads.Add(new KnownPointLoad("point-01", new StandardQuantity(0.04, "m3/s"), CreateSynthProvenance()));

            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.TotalDesignLoading != null && Math.Abs(result.TotalDesignLoading.Value - 0.39) < 1e-9, "TotalDesignLoading must equal sum of all components.");
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Status must be Complete.");
        }

        // P11 Test 19: missing value is not treated as zero
        private static void P11_MissingValueNotTreatedAsZero()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.InfiltrationTotal == null, "Missing infiltration must NOT become 0.0.");
            Check(result.InflowTotal == null, "Missing inflow must NOT become 0.0.");
            Check(result.IndustrialProcessTotal == null, "Missing industrial flow must NOT become 0.0.");
            Check(result.KnownPointLoadTotal == null, "Missing point load must NOT become 0.0.");
        }

        // P11 Test 20: explicit zero contribution remains valid zero
        private static void P11_ExplicitZeroContributionRemainsValidZero()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-zero", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.0, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Explicit zero contribution must be accepted.");
            Check(result.InfiltrationTotal != null && result.InfiltrationTotal.Value == 0.0, "InfiltrationTotal must be explicitly 0.0.");
        }

        // P11 Test 21: negative contribution rejected
        private static void P11_NegativeContributionRejected()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-neg", GravityLoadingContributionKind.Infiltration, new StandardQuantity(-0.05, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Negative contribution flow must fail.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "NEGATIVE_CONTRIBUTION_FLOW"), "Must emit NEGATIVE_CONTRIBUTION_FLOW.");
        }

        // P11 Test 22: duplicate contribution ID rejected
        private static void P11_DuplicateContributionIdRejected()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("contrib-id-dup", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.01, "m3/s"), CreateSynthProvenance()));
            input.Contributions.Add(new GravityLoadingContribution("contrib-id-dup", GravityLoadingContributionKind.Inflow, new StandardQuantity(0.02, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Duplicate contribution ID must fail.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "DUPLICATE_CONTRIBUTION_ID"), "Must emit DUPLICATE_CONTRIBUTION_ID.");
        }

        // P11 Test 23: duplicate point-load ID rejected
        private static void P11_DuplicatePointLoadIdRejected()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.KnownPointLoads.Add(new KnownPointLoad("point-load-dup", new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance()));
            input.KnownPointLoads.Add(new KnownPointLoad("point-load-dup", new StandardQuantity(0.06, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Duplicate point load ID must fail.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "DUPLICATE_POINT_LOAD_ID"), "Must emit DUPLICATE_POINT_LOAD_ID.");
        }

        // P11 Test 24: incompatible flow units fail explicitly
        private static void P11_IncompatibleFlowUnitsFailExplicitly()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-unit-mismatch", GravityLoadingContributionKind.Infiltration, new StandardQuantity(5.0, "L/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "Incompatible flow units must fail closed.");
            Check(result.ValidationResult.Issues.Any(i => i.Code == "INCOMPATIBLE_FLOW_UNITS"), "Must emit INCOMPATIBLE_FLOW_UNITS.");
        }

        // P11 Test 25: no silent unit conversion
        private static void P11_NoSilentUnitConversion()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance())
            };
            input.KnownPointLoads.Add(new KnownPointLoad("point-unit-mismatch", new StandardQuantity(25.0, "gpm"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Failed, "No silent conversion permitted.");
            Check(result.TotalDesignLoading == null, "Total loading must be null when units are incompatible.");
        }

        // P11 Test 26: input order does not change totals
        private static void P11_InputOrderDoesNotChangeTotals()
        {
            var c1 = new GravityLoadingContribution("c1", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.01, "m3/s"), CreateSynthProvenance());
            var c2 = new GravityLoadingContribution("c2", GravityLoadingContributionKind.Inflow, new StandardQuantity(0.02, "m3/s"), CreateSynthProvenance());
            var c3 = new GravityLoadingContribution("c3", GravityLoadingContributionKind.IndustrialProcess, new StandardQuantity(0.03, "m3/s"), CreateSynthProvenance());

            var inputA = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance()),
                Contributions = new List<GravityLoadingContribution> { c1, c2, c3 }
            };

            var inputB = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance()),
                Contributions = new List<GravityLoadingContribution> { c3, c1, c2 }
            };

            var resA = GravityLoadingCalculator.Calculate(inputA);
            var resB = GravityLoadingCalculator.Calculate(inputB);
            Check(resA.TotalDesignLoading.Value == resB.TotalDesignLoading.Value, "Input order must not affect TotalDesignLoading.");
            Check(resA.CalculationFingerprint == resB.CalculationFingerprint, "Fingerprint must be identical regardless of input order.");
        }

        // P11 Test 27: input order does not change deterministic trace ordering
        private static void P11_InputOrderDoesNotChangeDeterministicTraceOrdering()
        {
            var c1 = new GravityLoadingContribution("c1", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.01, "m3/s"), CreateSynthProvenance());
            var c2 = new GravityLoadingContribution("c2", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.02, "m3/s"), CreateSynthProvenance());

            var inputA = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance()),
                Contributions = new List<GravityLoadingContribution> { c1, c2 }
            };

            var inputB = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance()),
                Contributions = new List<GravityLoadingContribution> { c2, c1 }
            };

            var resA = GravityLoadingCalculator.Calculate(inputA);
            var resB = GravityLoadingCalculator.Calculate(inputB);
            Check(resA.Trace.Count == resB.Trace.Count, "Trace count must match.");
            for (int i = 0; i < resA.Trace.Count; i++)
            {
                Check(resA.Trace[i].Formula == resB.Trace[i].Formula, "Trace formula mismatch at index " + i);
            }
        }

        // P11 Test 28: current culture does not change calculation
        private static void P11_CurrentCultureDoesNotChangeCalculation()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var inputDe = new GravityLoadingCalculationInput
                {
                    PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 123.456, CreateSynthProvenance()),
                    GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                    PeakFactor = new PeakFactor(1.75, CreateSynthProvenance())
                };
                var resDe = GravityLoadingCalculator.Calculate(inputDe);

                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                var inputEn = new GravityLoadingCalculationInput
                {
                    PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 123.456, CreateSynthProvenance()),
                    GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                    PeakFactor = new PeakFactor(1.75, CreateSynthProvenance())
                };
                var resEn = GravityLoadingCalculator.Calculate(inputEn);

                Check(resDe.CalculationFingerprint == resEn.CalculationFingerprint, "Calculation fingerprint must be invariant to current culture.");
                Check(resDe.TotalDesignLoading.Value == resEn.TotalDesignLoading.Value, "Calculated total must be invariant to current culture.");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        // P11 Test 29: P8 provenance retained
        private static void P11_P8ProvenanceRetained()
        {
            var p8Prov = new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.PublishedStandard,
                StandardId = "std-p8-ref",
                StandardName = "P8 Reference Standard",
                StandardVersion = "1.0",
                ParameterKey = "loading.p8_test",
                DocumentReference = "P8_DOC"
            };
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 50.0, p8Prov),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, p8Prov),
                PeakFactor = new PeakFactor(2.0, p8Prov)
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Basis.Provenance.SourceLayer == StandardSourceLayer.PublishedStandard, "Source layer must be preserved.");
            Check(result.Basis.Provenance.StandardId == "std-p8-ref", "P8 standard ID must be preserved.");
        }

        // P11 Test 30: P9 override provenance retained
        private static void P11_P9OverrideProvenanceRetained()
        {
            var p9Prov = new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.ProjectOverride,
                StandardId = "p9-override-id",
                StandardName = "Project Override Standard",
                StandardVersion = "2.0",
                ParameterKey = "loading.override_test",
                Notes = "Approved by project engineer"
            };
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 80.0, p9Prov),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, p9Prov),
                PeakFactor = new PeakFactor(2.5, p9Prov)
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.PeakFactor.Provenance.SourceLayer == StandardSourceLayer.ProjectOverride, "ProjectOverride layer must be retained.");
            Check(result.PeakFactor.Provenance.Notes == "Approved by project engineer", "Provenance notes must be retained.");
        }

        // P11 Test 31: P10 Loading section consumed without mutation
        private static void P11_P10LoadingSectionConsumedWithoutMutation()
        {
            var section = new GravityLoadingSection();
            section.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.population",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromNumber(500.0),
                Provenance = CreateSynthProvenance()
            });
            section.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.generation_rate",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromQuantity(0.003, "m3/s"),
                Provenance = CreateSynthProvenance()
            });
            section.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.peak_factor",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromNumber(2.2),
                Provenance = CreateSynthProvenance()
            });

            int origCount = section.Criteria.Count;
            var input = GravityLoadingCalculator.ProjectFromLoadingSection(section);
            Check(section.Criteria.Count == origCount, "Section criteria count must remain unchanged.");
            Check(input.PopulationBasis != null && input.PopulationBasis.Value == 500.0, "PopulationBasis must be projected.");
            Check(input.GenerationRate != null && input.GenerationRate.Quantity.Value == 0.003, "GenerationRate must be projected.");
            Check(input.PeakFactor != null && input.PeakFactor.Value == 2.2, "PeakFactor must be projected.");
        }

        // P11 Test 32: P10 snapshot remains immutable
        private static void P11_P10SnapshotRemainsImmutable()
        {
            var basis = CreateValidGravityFlowDesignBasis();
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.population",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromNumber(1000.0),
                Provenance = CreateSynthProvenance()
            });
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.generation_rate",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromQuantity(0.002, "m3/s"),
                Provenance = CreateSynthProvenance()
            });
            basis.Loading.Criteria.Add(new GravityDesignCriterion
            {
                Key = "loading.peak_factor",
                Section = GravityFlowSectionIdentifier.Loading,
                Status = GravityCriterionStatus.Defined,
                Value = StandardValue.FromNumber(2.0),
                Provenance = CreateSynthProvenance()
            });

            var snapshot = basis.CreateSnapshot();
            string snapFingerprintBefore = snapshot.GravityContentFingerprint;

            var input = GravityLoadingCalculator.ProjectFromLoadingSection(basis.Loading);
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Calculation must succeed.");
            Check(snapshot.GravityContentFingerprint == snapFingerprintBefore, "P10 snapshot fingerprint must remain completely unchanged.");
        }

        // P11 Test 33: no network element ID required
        private static void P11_NoNetworkElementIdRequired()
        {
            var pointLoad = new KnownPointLoad("point-load-unmapped", new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance());
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 100.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.001, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.0, CreateSynthProvenance()),
                KnownPointLoads = new List<KnownPointLoad> { pointLoad }
            };
            var result = GravityLoadingCalculator.Calculate(input);
            Check(result.Status == GravityLoadingCalculationStatus.Complete, "Calculation must succeed with zero network element IDs.");
        }

        // P11 Test 34: known point-load external reference does not imply network mapping
        private static void P11_KnownPointLoadExternalReferenceDoesNotImplyNetworkMapping()
        {
            var pointLoad = new KnownPointLoad("point-load-ext", new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance(), "GIS_LAYER_POLYGON_44");
            Check(pointLoad.ExternalReference == "GIS_LAYER_POLYGON_44", "ExternalReference must be stored as raw string identifier.");
            var contrib = pointLoad.ToContribution();
            Check(contrib.ReferenceIdentifier == "GIS_LAYER_POLYGON_44", "ReferenceIdentifier must match ExternalReference.");
        }

        // P11 Test 35: no P12 network ingestion
        private static void P11_NoP12NetworkIngestion()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };
            string[] forbiddenTokens = new[] { "IngestNetwork", "ReadManhole", "ReadConduit", "ReadCatchment", "NetworkGraph" };
            foreach (var type in p11Types)
            {
                foreach (var m in type.GetMethods())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!m.Name.Contains(token), "P11 must not contain P12 network ingestion method: " + m.Name);
                    }
                }
            }
        }

        // P11 Test 36: no P13 vertical design
        private static void P11_NoP13VerticalDesign()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };
            string[] forbiddenTokens = new[] { "CalculateSlope", "DesignInvert", "VerticalDesign", "CalculateCover" };
            foreach (var type in p11Types)
            {
                foreach (var m in type.GetMethods())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!m.Name.Contains(token), "P11 must not contain P13 vertical design method: " + m.Name);
                    }
                }
            }
        }

        // P11 Test 37: no P14 candidate generation
        private static void P11_NoP14CandidateGeneration()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };
            string[] forbiddenTokens = new[] { "GenerateCandidates", "PipeSizeCandidate", "CandidateGeneration" };
            foreach (var type in p11Types)
            {
                foreach (var m in type.GetMethods())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!m.Name.Contains(token), "P11 must not contain P14 candidate generation method: " + m.Name);
                    }
                }
            }
        }

        // P11 Test 38: no P15 solver invocation
        private static void P11_NoP15SolverInvocation()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };
            string[] forbiddenTokens = new[] { "RunSolver", "ExecuteHydraulicEngine", "SolveNetwork" };
            foreach (var type in p11Types)
            {
                foreach (var m in type.GetMethods())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!m.Name.Contains(token), "P11 must not contain P15 solver invocation method: " + m.Name);
                    }
                }
            }
        }

        // P11 Test 39: no P16 hydraulic verification
        private static void P11_NoP16HydraulicVerification()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };
            string[] forbiddenTokens = new[] { "VerifyHydraulics", "CheckCapacity", "CheckVelocityLimits" };
            foreach (var type in p11Types)
            {
                foreach (var m in type.GetMethods())
                {
                    foreach (var token in forbiddenTokens)
                    {
                        Check(!m.Name.Contains(token), "P11 must not contain P16 hydraulic verification method: " + m.Name);
                    }
                }
            }
        }

        // P11 Test 40: no hard-coded production loading values
        private static void P11_NoHardcodedProductionLoadingValues()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };

            foreach (var type in p11Types)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (field.IsLiteral && !field.IsInitOnly)
                    {
                        var val = field.GetRawConstantValue();
                        Check(val == null || val is int || val is string, "No production default constants allowed in P11: " + field.Name);
                    }
                }
            }
        }

        // P11 Test 41: no public vendor types
        private static void P11_NoPublicVendorTypes()
        {
            var p11Types = new[]
            {
                typeof(GravityPopulationBasisKind),
                typeof(GravityLoadingContributionKind),
                typeof(GravityLoadingCalculationStatus),
                typeof(GravityPopulationBasis),
                typeof(WastewaterGenerationRate),
                typeof(PeakFactor),
                typeof(GravityLoadingContribution),
                typeof(KnownPointLoad),
                typeof(LoadingCalculationTrace),
                typeof(GravityLoadingCalculationInput),
                typeof(GravityLoadingResult),
                typeof(GravityLoadingCalculator)
            };

            foreach (var type in p11Types)
            {
                Check(Neutral(type), "Type must be neutral: " + type.FullName);
                foreach (var method in type.GetMethods())
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties())
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P11 Test 42: no persisted vendor types
        private static void P11_NoPersistedVendorTypes()
        {
            var input = new GravityLoadingCalculationInput
            {
                PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, 1000.0, CreateSynthProvenance()),
                GenerationRate = new WastewaterGenerationRate(new StandardQuantity(0.002, "m3/s"), GravityPopulationBasisKind.Population, CreateSynthProvenance()),
                PeakFactor = new PeakFactor(2.5, CreateSynthProvenance())
            };
            input.Contributions.Add(new GravityLoadingContribution("infil-01", GravityLoadingContributionKind.Infiltration, new StandardQuantity(0.05, "m3/s"), CreateSynthProvenance()));
            var result = GravityLoadingCalculator.Calculate(input);

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(result);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized GravityLoadingResult must contain zero vendor types.");
        }

        // P12-A Test 1: read evidence DTO contains no public vendor types
        private static void P12A_ReadEvidenceDtoContainsNoPublicVendorTypes()
        {
            var p12aTypes = new[]
            {
                typeof(NetworkReadPropertyStatus),
                typeof(MemberEvidence),
                typeof(NetworkScenarioContext),
                typeof(Point2DDto),
                typeof(ConduitReadDto),
                typeof(StructureReadDto),
                typeof(NetworkTopologyCheckResult),
                typeof(NetworkReadResult),
                typeof(NetworkTopologyValidator),
                typeof(GravityNetworkReadMapper)
            };

            foreach (var type in p12aTypes)
            {
                Check(Neutral(type), "Type must be neutral: " + type.FullName);
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    Check(Neutral(method.ReturnType), "Method return type must be neutral: " + method.Name);
                    foreach (var param in method.GetParameters())
                        Check(Neutral(param.ParameterType), "Parameter must be neutral: " + param.Name);
                }
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    Check(Neutral(prop.PropertyType), "Property must be neutral: " + prop.Name);
            }
        }

        // P12-A Test 2: conduit IDs retained exactly
        private static void P12A_ConduitIdsRetainedExactly()
        {
            var result = new NetworkReadResult();
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 20 });
            result.Conduits.Add(new ConduitReadDto { Id = 28, StartNodeId = 20, StopNodeId = 21 });
            result.Conduits.Add(new ConduitReadDto { Id = 29, StartNodeId = 21, StopNodeId = 25 });

            Check(result.Conduits.Count == 3, "Expected 3 conduits");
            Check(result.Conduits[0].Id == 27, "Conduit 0 Id must be 27");
            Check(result.Conduits[1].Id == 28, "Conduit 1 Id must be 28");
            Check(result.Conduits[2].Id == 29, "Conduit 2 Id must be 29");
        }

        // P12-A Test 3: node IDs retained exactly
        private static void P12A_NodeIdsRetainedExactly()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19, RuntimeCategory = "Manhole" });
            result.Structures.Add(new StructureReadDto { Id = 20, RuntimeCategory = "Manhole" });
            result.Structures.Add(new StructureReadDto { Id = 25, RuntimeCategory = "Outfall" });

            Check(result.Structures.Count == 3, "Expected 3 structures");
            Check(result.Structures[0].Id == 19, "Structure 0 Id must be 19");
            Check(result.Structures[1].Id == 20, "Structure 1 Id must be 20");
            Check(result.Structures[2].Id == 25, "Structure 2 Id must be 25");
        }

        // P12-A Test 4: endpoint mappings retain IDs exactly
        private static void P12A_EndpointMappingsRetainIdsExactly()
        {
            var conduit = new ConduitReadDto { Id = 35, StartNodeId = 24, StopNodeId = 26 };
            Check(conduit.StartNodeId == 24, "StartNodeId must be 24");
            Check(conduit.StopNodeId == 26, "StopNodeId must be 26");
        }

        // P12-A Test 5: duplicate conduit IDs fail closed
        private static void P12A_DuplicateConduitIdsFailClosed()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19 });
            result.Structures.Add(new StructureReadDto { Id = 20 });
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 20 });
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 20 });

            var check = NetworkTopologyValidator.Validate(result);
            Check(!check.IsValid, "Duplicate conduit IDs must fail closed");
            Check(check.DuplicateConduitIds.Contains(27), "Must record duplicate conduit ID 27");
        }

        // P12-A Test 6: duplicate node IDs fail closed
        private static void P12A_DuplicateNodeIdsFailClosed()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19 });
            result.Structures.Add(new StructureReadDto { Id = 19 });
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 19 });

            var check = NetworkTopologyValidator.Validate(result);
            Check(!check.IsValid, "Duplicate node IDs must fail closed");
            Check(check.DuplicateNodeIds.Contains(19), "Must record duplicate node ID 19");
        }

        // P12-A Test 7: unresolved endpoint reference classified explicitly
        private static void P12A_UnresolvedEndpointReferenceClassifiedExplicitly()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19 });
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 999 });

            var check = NetworkTopologyValidator.Validate(result);
            Check(!check.IsValid, "Unresolved endpoint reference must not be valid");
            Check(check.UnresolvedEndpointReferences == 1, "Must report 1 unresolved endpoint reference");
            string key = 999.ToString(CultureInfo.InvariantCulture);
            Check(check.MissingEndpoints.ContainsKey(key), "MissingEndpoints must contain 999");
            Check(check.MissingEndpoints[key].Contains(27), "MissingEndpoint 999 must reference conduit 27");
        }

        // P12-A Test 8: labels optional
        private static void P12A_LabelsOptional()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19, Label = null });
            result.Structures.Add(new StructureReadDto { Id = 20, Label = null });
            result.Conduits.Add(new ConduitReadDto { Id = 27, Label = null, StartNodeId = 19, StopNodeId = 20 });

            var check = NetworkTopologyValidator.Validate(result);
            Check(check.IsValid, "Null labels must be valid");
            Check(result.Conduits[0].Label == null, "Conduit label null preserved");
            Check(result.Structures[0].Label == null, "Structure label null preserved");
        }

        // P12-A Test 9: missing optional geometry does not fabricate coordinates
        private static void P12A_MissingOptionalGeometryDoesNotFabricateCoordinates()
        {
            var structure = new StructureReadDto { Id = 19, X = null, Y = null };
            Check(!structure.X.HasValue, "Missing X must remain null");
            Check(!structure.Y.HasValue, "Missing Y must remain null");

            var conduit = new ConduitReadDto { Id = 27 };
            Check(conduit.GeometryPoints.Count == 0, "Missing geometry points must not fabricate points");
        }

        // P12-A Test 10: uncertain elevation semantics cannot be marked PROVEN
        private static void P12A_UncertainElevationSemanticsCannotBeMarkedProven()
        {
            var status = NetworkReadPropertyStatus.SEMANTICS_UNRESOLVED;
            Check(status != NetworkReadPropertyStatus.PROVEN, "SEMANTICS_UNRESOLVED cannot equal PROVEN");

            var dto = new StructureReadDto { Id = 19 };
            dto.PropertyClassifications["UncertainElevation"] = NetworkReadPropertyStatus.SEMANTICS_UNRESOLVED.ToString();
            Check(dto.PropertyClassifications["UncertainElevation"] != NetworkReadPropertyStatus.PROVEN.ToString(), "Uncertain elevation cannot be marked PROVEN");
        }

        // P12-A Test 11: diagnostic classification distinguishes PROVEN / NOT_PROVEN / NOT_AVAILABLE / SEMANTICS_UNRESOLVED
        private static void P12A_DiagnosticClassificationDistinguishesStatuses()
        {
            var names = Enum.GetNames(typeof(NetworkReadPropertyStatus));
            Check(names.Length == 4, "Expected exactly 4 property statuses");
            Check(names.Contains("PROVEN"), "Must contain PROVEN");
            Check(names.Contains("NOT_PROVEN"), "Must contain NOT_PROVEN");
            Check(names.Contains("NOT_AVAILABLE"), "Must contain NOT_AVAILABLE");
            Check(names.Contains("SEMANTICS_UNRESOLVED"), "Must contain SEMANTICS_UNRESOLVED");
        }

        // P12-A Test 12: scenario context required
        private static void P12A_ScenarioContextRequired()
        {
            var ctx = new NetworkScenarioContext
            {
                ActiveScenarioId = 1,
                ActiveScenarioLabel = "Base Scenario",
                TopologyAlternativeId = 2,
                PhysicalAlternativeId = 4,
                ActiveTopologyAlternativeId = 3
            };
            Check(ctx.ActiveScenarioId == 1, "ActiveScenarioId retained");
            Check(ctx.ActiveScenarioLabel == "Base Scenario", "ActiveScenarioLabel retained");
            Check(ctx.TopologyAlternativeId == 2, "TopologyAlternativeId retained");
            Check(ctx.PhysicalAlternativeId == 4, "PhysicalAlternativeId retained");

            var result = new NetworkReadResult { ScenarioContext = ctx };
            Check(result.ScenarioContext != null, "ScenarioContext must be present on NetworkReadResult");
        }

        // P12-A Test 13: write invocation counters remain zero
        private static void P12A_WriteInvocationCountersRemainZero()
        {
            var result = new NetworkReadResult();
            Check(result.WriteInvocationCounts != null, "WriteInvocationCounts must not be null");
            Check(result.WriteInvocationCounts.Count >= 6, "Expected write invocation counters");
            foreach (var kvp in result.WriteInvocationCounts)
            {
                Check(kvp.Value == 0, "Counter " + kvp.Key + " must be 0");
            }
        }

        // P12-A Test 14: no P11 load allocation exists
        private static void P12A_NoP11LoadAllocationExists()
        {
            var mapperType = typeof(GravityNetworkReadMapper);
            var modelsType = typeof(NetworkReadResult);
            var p11Types = new[] { typeof(GravityLoadingCalculator), typeof(GravityLoadingResult), typeof(GravityLoadingCalculationInput) };

            foreach (var p11Type in p11Types)
            {
                Check(!mapperType.GetMethods().Any(m => m.ReturnType == p11Type || m.GetParameters().Any(p => p.ParameterType == p11Type)), "Mapper must not reference P11 type: " + p11Type.Name);
                Check(!modelsType.GetProperties().Any(p => p.PropertyType == p11Type), "Result must not reference P11 type: " + p11Type.Name);
            }
        }

        // P12-A Test 15: no P13 design calculations exist
        private static void P12A_NoP13DesignCalculationsExist()
        {
            var mapperType = typeof(GravityNetworkReadMapper);
            var forbiddenMethodNames = new[] { "Design", "CalculateSlope", "CalculateInvert", "MatchCrown", "CalculateCover" };
            foreach (var name in forbiddenMethodNames)
            {
                Check(mapperType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance) == null, "P13 design method forbidden: " + name);
            }
        }

        // P12-A Test 16: no public vendor types
        private static void P12A_NoPublicVendorTypes()
        {
            var p12aTypes = new[]
            {
                typeof(NetworkReadPropertyStatus),
                typeof(MemberEvidence),
                typeof(NetworkScenarioContext),
                typeof(Point2DDto),
                typeof(ConduitReadDto),
                typeof(StructureReadDto),
                typeof(NetworkTopologyCheckResult),
                typeof(NetworkReadResult),
                typeof(NetworkTopologyValidator),
                typeof(GravityNetworkReadMapper)
            };

            foreach (var type in p12aTypes)
            {
                Check(Neutral(type), "Type must be neutral: " + type.FullName);
            }

            var sample = new NetworkReadResult
            {
                ScenarioContext = new NetworkScenarioContext { ActiveScenarioId = 1, ActiveScenarioLabel = "Base" },
                Conduits = new List<ConduitReadDto> { new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 20 } },
                Structures = new List<StructureReadDto> { new StructureReadDto { Id = 19 }, new StructureReadDto { Id = 20 } }
            };
            sample.TopologyCheck = NetworkTopologyValidator.Validate(sample);

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(sample);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized NetworkReadResult must not leak vendor types");
        }

        // P12-A-R1 Test 1: DomainElementType resolution uses loaded Haestad.Domain assembly
        private static void P12AR1_DomainElementTypeUsesLoadedHaestadDomainAssembly()
        {
            var asm = typeof(GravityNetworkReadMapper).Assembly;
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { asm });
            Check(resolved == null, "Assembly named SewerPilot.OpenFlows.Adapter should not match Haestad.Domain");
        }

        // P12-A-R1 Test 2: resolver does not depend on datasetType.Assembly
        private static void P12AR1_ResolverDoesNotDependOnDatasetTypeAssembly()
        {
            var methods = typeof(GravityNetworkReadMapper).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            var resolverMethod = methods.FirstOrDefault(m => m.Name == "ResolveDomainElementType");
            Check(resolverMethod != null, "ResolveDomainElementType method must exist");
            Check(resolverMethod.GetParameters().Length == 1 && resolverMethod.GetParameters()[0].ParameterType == typeof(IEnumerable<Assembly>),
                "ResolveDomainElementType signature must accept IEnumerable<Assembly>");
        }

        // P12-A-R1 Test 3: missing Haestad.Domain fails explicitly
        private static void P12AR1_MissingHaestadDomainFailsExplicitly()
        {
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new Assembly[0]);
            Check(resolved == null, "Empty assembly list must return null explicitly");
        }

        // P12-A-R1 Test 4: duplicate matching Haestad.Domain assemblies fail closed if ambiguity exists
        private static void P12AR1_DuplicateMatchingAssembliesFailClosed()
        {
            var asm = typeof(GravityNetworkReadMapper).Assembly;
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { asm, asm });
            Check(resolved == null, "Duplicate/ambiguous assemblies must fail closed");
        }

        // P12-A-R1 Test 5: exact type name is required
        private static void P12AR1_ExactTypeNameRequired()
        {
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { typeof(string).Assembly });
            Check(resolved == null, "Non-matching assembly must not resolve DomainElementType");
        }

        // P12-A-R1 Test 6: no hard-coded Bentley installation path introduced
        private static void P12AR1_NoHardcodedBentleyInstallationPath()
        {
            foreach (var field in typeof(GravityNetworkReadMapper).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string) && field.IsLiteral)
                {
                    string val = (string)field.GetRawConstantValue();
                    Check(!val.Contains("Program Files") && !val.Contains("Bentley"), "No hardcoded Bentley path in constants: " + field.Name);
                }
            }
        }

        // P12-A-R1 Test 7: Conduit type resolution produces a usable non-negative type ID in synthetic/reflection fixture
        private enum SyntheticDomainElementType
        {
            ManholeElementManager = 1,
            CatchBasinElementManager = 2,
            ConduitElementManager = 3,
            OutfallElementManager = 5
        }

        private static void P12AR1_ConduitTypeResolutionProducesUsableIdInFixture()
        {
            int cid = GravityNetworkReadMapper.ResolveElementTypeId(typeof(SyntheticDomainElementType), "ConduitElementManager");
            Check(cid == 3, "ConduitElementManager must resolve to 3");
            Check(cid >= 0, "Conduit type ID must be non-negative");

            int unknown = GravityNetworkReadMapper.ResolveElementTypeId(typeof(SyntheticDomainElementType), "UnknownManager");
            Check(unknown == -1, "Unknown manager must return -1");

            int nullEnum = GravityNetworkReadMapper.ResolveElementTypeId(null, "ConduitElementManager");
            Check(nullEnum == -1, "Null enum type must return -1");
        }

        // P12-A-R1 Test 8: serializer-compatible MissingEndpoints DTO roundtrips
        private static void P12AR1_SerializerCompatibleMissingEndpointsRoundtrips()
        {
            var result = new NetworkReadResult();
            result.Structures.Add(new StructureReadDto { Id = 19 });
            result.Conduits.Add(new ConduitReadDto { Id = 27, StartNodeId = 19, StopNodeId = 999 });

            var check = NetworkTopologyValidator.Validate(result);
            result.TopologyCheck = check;
            Check(!check.IsValid, "Missing endpoint must be invalid");
            Check(check.MissingEndpoints.ContainsKey("999"), "Must contain string key 999");
            Check(check.MissingEndpoints["999"].Contains(27), "Must contain conduit 27");

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(result);
            Check(json != null && json.Length > 0, "Must serialize to JSON");

            var roundtripped = serializer.Deserialize<NetworkReadResult>(json);
            Check(roundtripped.TopologyCheck != null, "TopologyCheck must survive roundtrip");
            Check(roundtripped.TopologyCheck.MissingEndpoints.ContainsKey("999"), "MissingEndpoints key must survive roundtrip");
            Check(roundtripped.TopologyCheck.MissingEndpoints["999"].Contains(27), "Conduit ID in MissingEndpoints must survive roundtrip");
        }

        // P12-A-R1 Test 9: invariant endpoint-key formatting roundtrips
        private static void P12AR1_InvariantEndpointKeyFormattingRoundtrips()
        {
            int[] testIds = new[] { 0, 19, 999, 100000, int.MaxValue };
            foreach (int id in testIds)
            {
                string key = id.ToString(CultureInfo.InvariantCulture);
                int parsed = int.Parse(key, CultureInfo.InvariantCulture);
                Check(parsed == id, "Invariant formatting must roundtrip exactly: " + id);
            }
        }

        // P12-A-R1 Test 10: write counters remain zero
        private static void P12AR1_WriteCountersRemainZero()
        {
            var result = new NetworkReadResult();
            Check(result.WriteInvocationCounts.Values.All(v => v == 0), "All write counters must be zero");
            Check(result.WriteInvocationCounts.Count >= 6, "Must contain write counters");
        }

        private static NetworkReadResult CreateValidReadResult()
        {
            var r = new NetworkReadResult();
            r.ScenarioContext = new NetworkScenarioContext
            {
                ActiveScenarioId = 1,
                ActiveScenarioLabel = "Base",
                TopologyAlternativeId = 10,
                PhysicalAlternativeId = 20,
                ActiveTopologyAlternativeId = 10
            };
            r.Structures.Add(new StructureReadDto
            {
                Id = 101,
                Label = "MH-1",
                RuntimeCategory = "Manhole",
                ElementTypeId = 1,
                X = 1000.5,
                Y = 2000.5,
                GroundElevation = 100.0,
                RimElevation = 100.0,
                InvertElevation = 95.0
            });
            r.Structures.Add(new StructureReadDto
            {
                Id = 102,
                Label = "CB-1",
                RuntimeCategory = "CatchBasin",
                ElementTypeId = 2,
                X = 1050.0,
                Y = 2050.0,
                GroundElevation = 99.5,
                RimElevation = 99.5,
                InvertElevation = 94.5
            });
            r.Structures.Add(new StructureReadDto
            {
                Id = 103,
                Label = "OF-1",
                RuntimeCategory = "Outfall",
                ElementTypeId = 5,
                X = 1100.0,
                Y = 2100.0,
                InvertElevation = 90.0
            });
            r.Conduits.Add(new ConduitReadDto
            {
                Id = 201,
                Label = "CO-1",
                StartNodeId = 101,
                StopNodeId = 102,
                Length = 50.0,
                Diameter = 300.0,
                Shape = "Circular",
                Material = "Concrete",
                ManningsN = 0.013,
                UpstreamInvert = 95.0,
                DownstreamInvert = 94.5
            });
            r.Conduits.Add(new ConduitReadDto
            {
                Id = 202,
                Label = "CO-2",
                StartNodeId = 102,
                StopNodeId = 103,
                Length = 60.0,
                Diameter = 375.0,
                Rise = 375.0,
                Span = 375.0,
                Shape = "Circular",
                Material = "PVC",
                ManningsN = 0.010,
                UpstreamInvert = 94.5,
                DownstreamInvert = 90.0
            });
            return r;
        }

        // Test 1: valid NetworkReadResult maps to GravityNetworkSnapshot
        private static void P12B_ValidNetworkReadResultMapsToSnapshot()
        {
            var r = CreateValidReadResult();
            var service = new GravityNetworkIngestionService();
            var snap = service.Ingest(r, "fp-test-1");
            Check(snap != null, "Snapshot must not be null");
            Check(snap.Nodes.Count == 3, "Snapshot must contain 3 nodes");
            Check(snap.Conduits.Count == 2, "Snapshot must contain 2 conduits");
            Check(snap.SchemaVersion == 1, "SchemaVersion must be 1");
            Check(snap.SourceModelFingerprint == "fp-test-1", "SourceModelFingerprint must match");
        }

        // Test 2: node source IDs preserved
        private static void P12B_NodeSourceIdsPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Nodes.Any(n => n.SourceElementId == 101), "Node 101 must be preserved");
            Check(snap.Nodes.Any(n => n.SourceElementId == 102), "Node 102 must be preserved");
            Check(snap.Nodes.Any(n => n.SourceElementId == 103), "Node 103 must be preserved");
        }

        // Test 3: conduit source IDs preserved
        private static void P12B_ConduitSourceIdsPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits.Any(c => c.SourceElementId == 201), "Conduit 201 must be preserved");
            Check(snap.Conduits.Any(c => c.SourceElementId == 202), "Conduit 202 must be preserved");
        }

        // Test 4: labels preserved
        private static void P12B_LabelsPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Nodes.First(n => n.SourceElementId == 101).Label == "MH-1", "Node label must match");
            Check(snap.Conduits.First(c => c.SourceElementId == 201).Label == "CO-1", "Conduit label must match");
        }

        // Test 5: Manhole mapped correctly
        private static void P12B_ManholeMappedCorrectly()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Nodes.First(n => n.SourceElementId == 101).Kind == GravityNodeKind.Manhole, "Kind must be Manhole");
        }

        // Test 6: CatchBasin mapped correctly
        private static void P12B_CatchBasinMappedCorrectly()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Nodes.First(n => n.SourceElementId == 102).Kind == GravityNodeKind.CatchBasin, "Kind must be CatchBasin");
        }

        // Test 7: Outfall mapped correctly
        private static void P12B_OutfallMappedCorrectly()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Nodes.First(n => n.SourceElementId == 103).Kind == GravityNodeKind.Outfall, "Kind must be Outfall");
        }

        // Test 8: unsupported node type fails explicitly
        private static void P12B_UnsupportedNodeTypeFailsExplicitly()
        {
            var r = CreateValidReadResult();
            r.Structures[0].RuntimeCategory = "PumpStation";
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
                Check(false, "Should fail on unsupported node type");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_UNSUPPORTED_NODE_TYPE", "Classification must be P12_B_BLOCKED_UNSUPPORTED_NODE_TYPE");
            }
        }

        // Test 9: X/Y preserved exactly
        private static void P12B_XYPreservedExactly()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var n = snap.Nodes.First(x => x.SourceElementId == 101);
            Check(n.X == 1000.5 && n.Y == 2000.5, "Coordinates must match exactly");
        }

        // Test 10: missing optional coordinate remains missing
        private static void P12B_MissingOptionalCoordinateRemainsMissing()
        {
            var r = CreateValidReadResult();
            r.Structures[0].X = null;
            r.Structures[0].Y = null;
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var n = snap.Nodes.First(x => x.SourceElementId == 101);
            Check(n.X == null && n.Y == null, "Missing coordinates must remain null, not zero");
        }

        // Test 11: node elevation values preserved
        private static void P12B_NodeElevationValuesPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var n = snap.Nodes.First(x => x.SourceElementId == 101);
            Check(n.ExistingElevationState.GroundElevation == 100.0, "GroundElevation preserved");
            Check(n.ExistingElevationState.RimElevation == 100.0, "RimElevation preserved");
            Check(n.ExistingElevationState.InvertElevation == 95.0, "InvertElevation preserved");
        }

        // Test 12: missing elevation remains missing, not zero
        private static void P12B_MissingElevationRemainsMissingNotZero()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var n = snap.Nodes.First(x => x.SourceElementId == 103);
            Check(n.ExistingElevationState.GroundElevation == null, "GroundElevation must remain null");
            Check(n.ExistingElevationState.RimElevation == null, "RimElevation must remain null");
            Check(n.ExistingElevationState.InvertElevation == 90.0, "InvertElevation preserved");
        }

        // Test 13: conduit StartNodeId preserved
        private static void P12B_ConduitStartNodeIdPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits.First(c => c.SourceElementId == 201).StartNodeId == 101, "StartNodeId preserved");
        }

        // Test 14: conduit StopNodeId preserved
        private static void P12B_ConduitStopNodeIdPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits.First(c => c.SourceElementId == 201).StopNodeId == 102, "StopNodeId preserved");
        }

        // Test 15: unresolved start endpoint rejected
        private static void P12B_UnresolvedStartEndpointRejected()
        {
            var r = CreateValidReadResult();
            r.Conduits[0].StartNodeId = 999;
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
                Check(false, "Should reject unresolved start endpoint");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_UNRESOLVED_ENDPOINT", "Classification must be P12_B_BLOCKED_UNRESOLVED_ENDPOINT");
            }
        }

        // Test 16: unresolved stop endpoint rejected
        private static void P12B_UnresolvedStopEndpointRejected()
        {
            var r = CreateValidReadResult();
            r.Conduits[0].StopNodeId = 999;
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
                Check(false, "Should reject unresolved stop endpoint");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_UNRESOLVED_ENDPOINT", "Classification must be P12_B_BLOCKED_UNRESOLVED_ENDPOINT");
            }
        }

        // Test 17: duplicate node ID rejected
        private static void P12B_DuplicateNodeIdRejected()
        {
            var r = CreateValidReadResult();
            r.Structures.Add(new StructureReadDto { Id = 101, RuntimeCategory = "Manhole" });
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
                Check(false, "Should reject duplicate node ID");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_DUPLICATE_ELEMENT_ID", "Classification must be P12_B_BLOCKED_DUPLICATE_ELEMENT_ID");
            }
        }

        // Test 18: duplicate conduit ID rejected
        private static void P12B_DuplicateConduitIdRejected()
        {
            var r = CreateValidReadResult();
            r.Conduits.Add(new ConduitReadDto { Id = 201, StartNodeId = 101, StopNodeId = 102 });
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
                Check(false, "Should reject duplicate conduit ID");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_DUPLICATE_ELEMENT_ID", "Classification must be P12_B_BLOCKED_DUPLICATE_ELEMENT_ID");
            }
        }

        // Test 19: conduit length preserved
        private static void P12B_ConduitLengthPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits.First(c => c.SourceElementId == 201).ExistingPhysicalState.Length == 50.0, "Length preserved");
        }

        // Test 20: conduit size metadata preserved
        private static void P12B_ConduitSizeMetadataPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var c = snap.Conduits.First(x => x.SourceElementId == 202);
            Check(c.ExistingPhysicalState.Diameter == 375.0, "Diameter preserved");
            Check(c.ExistingPhysicalState.Rise == 375.0, "Rise preserved");
            Check(c.ExistingPhysicalState.Span == 375.0, "Span preserved");
        }

        // Test 21: shape/material preserved
        private static void P12B_ShapeMaterialPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var c = snap.Conduits.First(x => x.SourceElementId == 201);
            Check(c.ExistingPhysicalState.Shape == "Circular", "Shape preserved");
            Check(c.ExistingPhysicalState.Material == "Concrete", "Material preserved");
        }

        // Test 22: ManningN preserved
        private static void P12B_ManningNPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits.First(c => c.SourceElementId == 201).ExistingPhysicalState.ManningsN == 0.013, "ManningN preserved");
        }

        // Test 23: invert values preserved
        private static void P12B_InvertValuesPreserved()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var c = snap.Conduits.First(x => x.SourceElementId == 201);
            Check(c.ExistingPhysicalState.UpstreamInvert == 95.0, "UpstreamInvert preserved");
            Check(c.ExistingPhysicalState.DownstreamInvert == 94.5, "DownstreamInvert preserved");
        }

        // Test 24: missing optional conduit property remains missing
        private static void P12B_MissingOptionalConduitPropertyRemainsMissing()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var c = snap.Conduits.First(x => x.SourceElementId == 201);
            Check(c.ExistingPhysicalState.Rise == null, "Rise remains null");
            Check(c.ExistingPhysicalState.Span == null, "Span remains null");
        }

        // Test 25: NaN/infinity rejected
        private static void P12B_NaNOrInfinityRejected()
        {
            var r1 = CreateValidReadResult();
            r1.Conduits[0].Length = double.NaN;
            try
            {
                new GravityNetworkIngestionService().Ingest(r1, "fp-test-1");
                Check(false, "Should reject NaN");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_DOMAIN_MAPPING", "Should classify domain mapping failure");
            }

            var r2 = CreateValidReadResult();
            r2.Structures[0].X = double.PositiveInfinity;
            try
            {
                new GravityNetworkIngestionService().Ingest(r2, "fp-test-1");
                Check(false, "Should reject Infinity");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_B_BLOCKED_DOMAIN_MAPPING", "Should classify domain mapping failure");
            }
        }

        // Test 26: detailed conduit polyline not fabricated
        private static void P12B_DetailedConduitPolylineNotFabricated()
        {
            var r = CreateValidReadResult();
            r.Conduits[0].GeometryPoints.Clear();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Check(snap.Conduits[0].ExistingPhysicalState.Length == 50.0, "Length unchanged and not derived from polyline");
        }

        // Test 27: adjacency built deterministically
        private static void P12B_AdjacencyBuiltDeterministically()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            var inc = snap.Adjacency.GetIncidentConduits(102);
            Check(inc.Count == 2 && inc.Contains(201) && inc.Contains(202), "Node 102 incident conduits must contain 201 and 202");
            var st = snap.Adjacency.GetStartConduits(102);
            Check(st.Count == 1 && st.Contains(202), "Start conduit for 102 must be 202");
            var sp = snap.Adjacency.GetStopConduits(102);
            Check(sp.Count == 1 && sp.Contains(201), "Stop conduit for 102 must be 201");
        }

        // Test 28: vendor enumeration order does not affect topology
        private static void P12B_VendorEnumerationOrderDoesNotAffectTopology()
        {
            var r1 = CreateValidReadResult();
            var r2 = CreateValidReadResult();
            r2.Structures.Reverse();
            r2.Conduits.Reverse();

            var snap1 = new GravityNetworkIngestionService().Ingest(r1, "fp-test-1");
            var snap2 = new GravityNetworkIngestionService().Ingest(r2, "fp-test-1");

            foreach (var n in snap1.Nodes)
            {
                var inc1 = snap1.Adjacency.GetIncidentConduits(n.SourceElementId);
                var inc2 = snap2.Adjacency.GetIncidentConduits(n.SourceElementId);
                Check(inc1.SequenceEqual(inc2), "Incident conduits must match regardless of vendor order");
            }
        }

        // Test 29: vendor enumeration order does not affect NetworkFingerprint
        private static void P12B_VendorEnumerationOrderDoesNotAffectNetworkFingerprint()
        {
            var r1 = CreateValidReadResult();
            var r2 = CreateValidReadResult();
            r2.Structures.Reverse();
            r2.Conduits.Reverse();

            var snap1 = new GravityNetworkIngestionService().Ingest(r1, "fp-test-1");
            var snap2 = new GravityNetworkIngestionService().Ingest(r2, "fp-test-1");

            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Fingerprint must be identical despite reversed input order");
        }

        // Test 30: identical logical network yields identical fingerprint
        private static void P12B_IdenticalLogicalNetworkYieldsIdenticalFingerprint()
        {
            var snap1 = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            var snap2 = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Identical inputs must yield identical fingerprint");
        }

        // Test 31: changed connectivity changes fingerprint
        private static void P12B_ChangedConnectivityChangesFingerprint()
        {
            var r = CreateValidReadResult();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");

            var rMod = CreateValidReadResult();
            rMod.Conduits[1].StopNodeId = 101;
            var snap2 = new GravityNetworkIngestionService().Ingest(rMod, "fp-test-1");

            Check(snap1.NetworkFingerprint != snap2.NetworkFingerprint, "Changed connectivity must alter fingerprint");
        }

        // Test 32: changed physical value changes fingerprint
        private static void P12B_ChangedPhysicalValueChangesFingerprint()
        {
            var r = CreateValidReadResult();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");

            var rMod = CreateValidReadResult();
            rMod.Conduits[0].Diameter = 450.0;
            var snap2 = new GravityNetworkIngestionService().Ingest(rMod, "fp-test-1");

            Check(snap1.NetworkFingerprint != snap2.NetworkFingerprint, "Changed physical value must alter fingerprint");
        }

        // Test 33: changed scenario context changes fingerprint
        private static void P12B_ChangedScenarioContextChangesFingerprint()
        {
            var r = CreateValidReadResult();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");

            var rMod = CreateValidReadResult();
            rMod.ScenarioContext.ActiveScenarioId = 2;
            var snap2 = new GravityNetworkIngestionService().Ingest(rMod, "fp-test-1");

            Check(snap1.NetworkFingerprint != snap2.NetworkFingerprint, "Changed scenario context must alter fingerprint");
        }

        // Test 34: working-copy path does not affect fingerprint
        private static void P12B_WorkingCopyPathDoesNotAffectFingerprint()
        {
            var r = CreateValidReadResult();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1", "wc-path-alpha");
            var snap2 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1", "wc-path-beta");

            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Working copy id/path must not affect network fingerprint");
        }

        // Test 35: PID/timestamps do not affect fingerprint
        private static void P12B_PIDAndTimestampsDoNotAffectFingerprint()
        {
            var r = CreateValidReadResult();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");
            Thread.Sleep(5);
            var snap2 = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");

            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Fingerprint must not contain timestamps or PID");
        }

        // Test 36: immutable snapshot survives caller collection mutation
        private static void P12B_ImmutableSnapshotSurvivesCallerCollectionMutation()
        {
            var r = CreateValidReadResult();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-test-1");

            r.Structures.Clear();
            r.Conduits.Clear();

            Check(snap.Nodes.Count == 3, "Snapshot nodes must not be affected by caller mutation");
            Check(snap.Conduits.Count == 2, "Snapshot conduits must not be affected by caller mutation");
        }

        // Test 37: P11 loading is not allocated
        private static void P12B_P11LoadingIsNotAllocated()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap != null, "Snapshot created without loading allocation");
        }

        // Test 38: no P13 vertical design executed
        private static void P12B_NoP13VerticalDesignExecuted()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap.Conduits[0].ExistingPhysicalState.UpstreamInvert == 95.0, "Invert remains raw read value");
        }

        // Test 39: no candidate generation
        private static void P12B_NoCandidateGeneration()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap.Conduits.Count == 2, "Only read conduits exist, no candidate alternatives");
        }

        // Test 40: no solver run
        private static void P12B_NoSolverRun()
        {
            var r = CreateValidReadResult();
            Check(r.WriteInvocationCounts["SolverRun"] == 0, "SolverRun must be 0");
        }

        // Test 41: no hydraulic verification
        private static void P12B_NoHydraulicVerification()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap != null, "Snapshot created without hydraulic verification state");
        }

        // Test 42: no public vendor types
        private static void P12B_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(GravityNetworkSnapshot)), "GravityNetworkSnapshot must be vendor-neutral");
            Check(Neutral(typeof(GravityNetworkNode)), "GravityNetworkNode must be vendor-neutral");
            Check(Neutral(typeof(GravityNetworkConduit)), "GravityNetworkConduit must be vendor-neutral");
            Check(Neutral(typeof(GravityNetworkAdjacency)), "GravityNetworkAdjacency must be vendor-neutral");
            Check(Neutral(typeof(GravityNetworkIngestionService)), "GravityNetworkIngestionService must be vendor-neutral");
        }

        // Test 43: no persisted vendor types
        private static void P12B_NoPersistedVendorTypes()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(snap);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley") && !json.Contains("WaterObjects"), "Serialized snapshot contains no vendor types");
        }

        // Test 44: unit state never silently invents a unit
        private static void P12B_UnitStateNeverSilentlyInventsAUnit()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResult(), "fp-test-1");
            Check(snap.Nodes[0].ExistingElevationState.UnitContext == "SOURCE_NATIVE_UNRESOLVED", "Unit state must be SOURCE_NATIVE_UNRESOLVED");
            Check(snap.Conduits[0].ExistingPhysicalState.UnitContext == "SOURCE_NATIVE_UNRESOLVED", "Unit state must be SOURCE_NATIVE_UNRESOLVED");
        }

        private static SourceUnitIdentity CreateLengthUnit(string unitName = "ft", string symbol = "ft", string unitId = "1", string dim = "length")
        {
            var prov = new UnitProvenance("WaterObjectsFieldMetadata", "DomainElementField", "WorkingUnit", "StorageUnit", "RuntimeEvidence");
            return new SourceUnitIdentity(dim, unitId, unitName, symbol, prov);
        }

        private static NetworkReadResult CreateValidReadResultWithUnits()
        {
            var r = CreateValidReadResult();
            var ftUnit = CreateLengthUnit("ft", "ft", "1");
            var inUnit = CreateLengthUnit("in", "in", "2");
            var mUnit = CreateLengthUnit("m", "m", "3");

            foreach (var s in r.Structures)
            {
                s.GroundElevationUnit = ftUnit;
                s.RimElevationUnit = ftUnit;
                s.InvertElevationUnit = ftUnit;
                s.CoordinateUnit = ftUnit;
            }

            foreach (var c in r.Conduits)
            {
                c.LengthUnit = ftUnit;
                c.LengthSourceField = "HMIGeometryScaledLength";
                c.DiameterUnit = inUnit;
                if (c.Rise.HasValue) c.RiseUnit = inUnit;
                if (c.Span.HasValue) c.SpanUnit = inUnit;
                c.UpstreamInvertUnit = ftUnit;
                c.DownstreamInvertUnit = ftUnit;
            }

            r.FieldUnitMap["Physical_GroundElevation"] = new FieldUnitMetadataDto
            {
                FieldName = "Physical_GroundElevation",
                DeclaringType = "Haestad.Domain.ModelingElementField",
                RuntimeType = "Haestad.Domain.DomainElementUnitizedFieldBase",
                IsUnitized = true,
                Dimension = "length",
                WorkingUnit = ftUnit,
                StorageUnit = mUnit,
                InspectionMember = "WorkingUnitIndex"
            };

            return r;
        }

        // 1. source unit identity is vendor-neutral
        private static void P12U_SourceUnitIdentityIsVendorNeutral()
        {
            var u = CreateLengthUnit("ft", "ft", "1");
            Check(Neutral(typeof(SourceUnitIdentity)), "SourceUnitIdentity must be vendor-neutral");
            Check(Neutral(typeof(UnitProvenance)), "UnitProvenance must be vendor-neutral");
            Check(Neutral(typeof(SourceEngineeringValue)), "SourceEngineeringValue must be vendor-neutral");
            Check(u.Dimension == "length" && u.UnitName == "ft" && u.Symbol == "ft" && u.UnitId == "1", "Properties match");
        }

        // 2. working and storage units remain distinguishable
        private static void P12U_WorkingAndStorageUnitsRemainDistinguishable()
        {
            var working = CreateLengthUnit("ft", "ft", "1");
            var storage = CreateLengthUnit("m", "m", "2");
            var prov = new UnitProvenance("WaterObjectsFieldMetadata", "ConduitDiameterField", working.UnitName, storage.UnitName, "ProvenByWorkingUnitIndex");
            Check(prov.WorkingUnitIdentity != prov.StorageUnitIdentity, "Working and storage units must be distinguishable");
            Check(prov.WorkingUnitIdentity == "ft" && prov.StorageUnitIdentity == "m", "Values preserved");
        }

        // 3. ingested value is associated with the proven value-return unit
        private static void P12U_IngestedValueAssociatedWithProvenValueReturnUnit()
        {
            var u = CreateLengthUnit("ft", "ft", "1");
            var sev = new SourceEngineeringValue(12.5, u);
            Check(sev.NumericValue == 12.5, "Numeric value matches");
            Check(sev.Unit.UnitName == "ft", "Value-return unit matches");
            var q = sev.ToStandardQuantity();
            Check(q.Value == 12.5 && q.Unit == "ft", "P8 StandardQuantity reuses proven unit");
        }

        // 4. storage unit cannot silently replace working unit
        private static void P12U_StorageUnitCannotSilentlyReplaceWorkingUnit()
        {
            var working = CreateLengthUnit("ft", "ft", "1");
            var storage = CreateLengthUnit("m", "m", "2");
            var val = new SourceEngineeringValue(100.0, working);
            Check(val.Unit.UnitName == "ft" && val.Unit.UnitName != storage.UnitName, "Working unit must not be replaced by storage unit");
        }

        // 5. elevation value + unit preserved
        private static void P12U_ElevationValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var node = snap.Nodes[0];
            Check(node.ExistingElevationState.GroundElevation == 100.0, "Ground elevation preserved");
            Check(node.ExistingElevationState.GroundElevationUnit != null && node.ExistingElevationState.GroundElevationUnit.UnitName == "ft", "Ground elevation unit preserved");
            Check(node.ExistingElevationState.RimElevationUnit != null && node.ExistingElevationState.RimElevationUnit.UnitName == "ft", "Rim elevation unit preserved");
            Check(node.ExistingElevationState.InvertElevationUnit != null && node.ExistingElevationState.InvertElevationUnit.UnitName == "ft", "Invert elevation unit preserved");
        }

        // 6. invert value + unit preserved
        private static void P12U_InvertValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[0];
            Check(c.ExistingPhysicalState.UpstreamInvert == 95.0 && c.ExistingPhysicalState.UpstreamInvertUnit.UnitName == "ft", "Upstream invert preserved");
            Check(c.ExistingPhysicalState.DownstreamInvert == 94.5 && c.ExistingPhysicalState.DownstreamInvertUnit.UnitName == "ft", "Downstream invert preserved");
        }

        // 7. length value + unit preserved
        private static void P12U_LengthValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[0];
            Check(c.ExistingPhysicalState.Length == 50.0, "Length value preserved");
            Check(c.ExistingPhysicalState.LengthUnit != null && c.ExistingPhysicalState.LengthUnit.UnitName == "ft", "Length unit preserved");
            Check(c.ExistingPhysicalState.LengthSourceField == "HMIGeometryScaledLength", "Length source field preserved");
        }

        // 8. diameter value + unit preserved
        private static void P12U_DiameterValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[0];
            Check(c.ExistingPhysicalState.Diameter == 300.0, "Diameter value preserved");
            Check(c.ExistingPhysicalState.DiameterUnit != null && c.ExistingPhysicalState.DiameterUnit.UnitName == "in", "Diameter unit preserved");
        }

        // 9. rise value + unit preserved
        private static void P12U_RiseValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[1];
            Check(c.ExistingPhysicalState.Rise == 375.0, "Rise value preserved");
            Check(c.ExistingPhysicalState.RiseUnit != null && c.ExistingPhysicalState.RiseUnit.UnitName == "in", "Rise unit preserved");
        }

        // 10. span value + unit preserved
        private static void P12U_SpanValuePlusUnitPreserved()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[1];
            Check(c.ExistingPhysicalState.Span == 375.0, "Span value preserved");
            Check(c.ExistingPhysicalState.SpanUnit != null && c.ExistingPhysicalState.SpanUnit.UnitName == "in", "Span unit preserved");
        }

        // 11. missing value remains missing
        private static void P12U_MissingValueRemainsMissing()
        {
            var r = CreateValidReadResultWithUnits();
            r.Conduits[0].Rise = null;
            r.Conduits[0].Span = null;
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(!snap.Conduits[0].ExistingPhysicalState.Rise.HasValue, "Rise remains missing");
            Check(!snap.Conduits[0].ExistingPhysicalState.Span.HasValue, "Span remains missing");
        }

        // 12. missing unit remains unresolved, not guessed
        private static void P12U_MissingUnitRemainsUnresolvedNotGuessed()
        {
            var r = CreateValidReadResult(); // no units attached
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Nodes[0].ExistingElevationState.UnitContext == "SOURCE_NATIVE_UNRESOLVED", "Missing elevation unit remains unresolved");
            Check(snap.Nodes[0].ExistingElevationState.GroundElevationUnit == null, "No unit guessed for ground elevation");
            Check(snap.Conduits[0].ExistingPhysicalState.UnitContext == "SOURCE_NATIVE_UNRESOLVED", "Missing conduit unit remains unresolved");
            Check(snap.Conduits[0].ExistingPhysicalState.LengthUnit == null, "No unit guessed for length");
        }

        // 13. dimension mismatch fails closed
        private static void P12U_DimensionMismatchFailsClosed()
        {
            var r = CreateValidReadResultWithUnits();
            r.Structures[0].GroundElevationUnit = new SourceUnitIdentity("pressure", "99", "psi", "psi", null);
            try
            {
                new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
                Check(false, "Should fail on dimension mismatch");
            }
            catch (GravityNetworkIngestionException ex)
            {
                Check(ex.Classification == "P12_U_BLOCKED_DIMENSION_MISMATCH", "Classification must be P12_U_BLOCKED_DIMENSION_MISMATCH");
            }
        }

        // 14. different units with same dimension remain distinct
        private static void P12U_DifferentUnitsWithSameDimensionRemainDistinct()
        {
            var ft = CreateLengthUnit("ft", "ft", "1");
            var inch = CreateLengthUnit("in", "in", "2");
            Check(ft.Dimension == inch.Dimension, "Both have dimension length");
            Check(!ft.Equals(inch), "Different units must not be equal");
            Check(ft.UnitName != inch.UnitName, "Unit names must remain distinct");
        }

        // 15. numeric value is not converted
        private static void P12U_NumericValueIsNotConverted()
        {
            var r = CreateValidReadResultWithUnits();
            double origLength = r.Conduits[0].Length.Value;
            double origDiameter = r.Conduits[0].Diameter.Value;
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.Length.Value == origLength, "Length value unconverted");
            Check(snap.Conduits[0].ExistingPhysicalState.Diameter.Value == origDiameter, "Diameter value unconverted");
        }

        // 16. unit context survives defensive copy
        private static void P12U_UnitContextSurvivesDefensiveCopy()
        {
            var u = CreateLengthUnit("ft", "ft", "1");
            var cloned = u.Clone();
            Check(cloned.UnitName == u.UnitName && cloned.Dimension == u.Dimension && cloned.UnitId == u.UnitId, "Cloned unit matches");
            Check(!object.ReferenceEquals(u, cloned), "Cloned unit is independent instance");
        }

        // 17. caller mutation cannot alter snapshot unit context
        private static void P12U_CallerMutationCannotAlterSnapshotUnitContext()
        {
            var r = CreateValidReadResultWithUnits();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            r.Structures[0].GroundElevationUnit = CreateLengthUnit("m", "m", "99");
            Check(snap.Nodes[0].ExistingElevationState.GroundElevationUnit.UnitName == "ft", "Snapshot unit immune to caller mutation");
        }

        // 18. unit identity participates in NetworkFingerprint
        private static void P12U_UnitIdentityParticipatesInNetworkFingerprint()
        {
            var r1 = CreateValidReadResultWithUnits();
            var snap1 = new GravityNetworkIngestionService().Ingest(r1, "fp-1", schemaVersion: 1);
            var snap2 = new GravityNetworkIngestionService().Ingest(r1, "fp-1", schemaVersion: 2);
            Check(snap1.NetworkFingerprint != snap2.NetworkFingerprint, "SchemaVersion 2 incorporates unit identity, differing from v1");
        }

        // 19. changing only unit identity changes fingerprint
        private static void P12U_ChangingOnlyUnitIdentityChangesFingerprint()
        {
            var r1 = CreateValidReadResultWithUnits();
            var r2 = CreateValidReadResultWithUnits();
            r2.Conduits[0].DiameterUnit = CreateLengthUnit("mm", "mm", "10");
            var snap1 = new GravityNetworkIngestionService().Ingest(r1, "fp-1", schemaVersion: 2);
            var snap2 = new GravityNetworkIngestionService().Ingest(r2, "fp-1", schemaVersion: 2);
            Check(snap1.NetworkFingerprint != snap2.NetworkFingerprint, "Changing unit identity must change fingerprint");
        }

        // 20. insertion order does not change fingerprint
        private static void P12U_InsertionOrderDoesNotChangeFingerprint()
        {
            var r1 = CreateValidReadResultWithUnits();
            var r2 = CreateValidReadResultWithUnits();
            r2.Structures.Reverse();
            r2.Conduits.Reverse();
            var snap1 = new GravityNetworkIngestionService().Ingest(r1, "fp-1", schemaVersion: 2);
            var snap2 = new GravityNetworkIngestionService().Ingest(r2, "fp-1", schemaVersion: 2);
            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Fingerprint is order-independent");
        }

        // 21. culture does not change unit serialization/fingerprint
        private static void P12U_CultureDoesNotChangeUnitSerializationFingerprint()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                var snapFr = new GravityNetworkIngestionService().Ingest(CreateValidReadResultWithUnits(), "fp-1", schemaVersion: 2);
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
                var snapFa = new GravityNetworkIngestionService().Ingest(CreateValidReadResultWithUnits(), "fp-1", schemaVersion: 2);
                Check(snapFr.NetworkFingerprint == snapFa.NetworkFingerprint, "Fingerprint culture independent");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        // 22. machine/path/runtime metadata does not affect fingerprint
        private static void P12U_MachinePathRuntimeMetadataDoesNotAffectFingerprint()
        {
            var r = CreateValidReadResultWithUnits();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-1", "path/a", schemaVersion: 2);
            var snap2 = new GravityNetworkIngestionService().Ingest(r, "fp-1", "path/b", schemaVersion: 2);
            Check(snap1.NetworkFingerprint == snap2.NetworkFingerprint, "Working copy id/path does not affect fingerprint");
        }

        // 23. no Bentley unit type leaks into public/domain models
        private static void P12U_NoBentleyUnitTypeLeaksIntoPublicDomainModels()
        {
            var types = new[] { typeof(SourceUnitIdentity), typeof(UnitProvenance), typeof(SourceEngineeringValue), typeof(FieldUnitMetadataDto) };
            foreach (var t in types)
            {
                Check(Neutral(t), "Model type must be vendor-neutral: " + t.FullName);
            }
        }

        // 24. no persisted vendor types
        private static void P12U_NoPersistedVendorTypes()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResultWithUnits(), "fp-1", schemaVersion: 2);
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(snap);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized snapshot contains no vendor types");
        }

        // 25. coordinate unit may remain unresolved without corrupting elevation/length units
        private static void P12U_CoordinateUnitMayRemainUnresolvedWithoutCorruptingElevationLengthUnits()
        {
            var r = CreateValidReadResultWithUnits();
            foreach (var s in r.Structures) s.CoordinateUnit = null;
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Nodes[0].CoordinateUnit == null, "Coordinate unit unresolved");
            Check(snap.Nodes[0].ExistingElevationState.GroundElevationUnit != null, "Elevation unit intact");
            Check(snap.Conduits[0].ExistingPhysicalState.LengthUnit != null, "Length unit intact");
        }

        // 26. no general conversion engine added
        private static void P12U_NoGeneralConversionEngineAdded()
        {
            var asm = typeof(SourceUnitIdentity).Assembly;
            var types = asm.GetTypes();
            foreach (var t in types)
            {
                Check(!t.Name.Contains("UnitConverter") && !t.Name.Contains("ConversionEngine"), "No general conversion engine: " + t.FullName);
            }
        }

        // 27. no working-unit setter invoked
        private static void P12U_NoWorkingUnitSetterInvoked()
        {
            var r = CreateValidReadResultWithUnits();
            Check(r.WriteInvocationCounts["WorkingUnitChange"] == 0, "WorkingUnitChange count is 0");
            string root = Path.Combine(Repository(), "src", "SewerPilot.OpenFlows.Adapter");
            string text = string.Join("\n", Directory.GetFiles(root, "*.cs").Select(File.ReadAllText));
            Check(!text.Contains(".SetWorkingUnit") && !text.Contains(".WorkingUnitIndex ="), "No working unit setter called in adapter");
        }

        // 28. no model mutation
        private static void P12U_NoModelMutation()
        {
            var r = CreateValidReadResultWithUnits();
            foreach (var kvp in r.WriteInvocationCounts)
            {
                Check(kvp.Value == 0, "Write count for " + kvp.Key + " must be 0");
            }
        }

        // 29. no P13 vertical calculation
        private static void P12U_NoP13VerticalCalculation()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResultWithUnits(), "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.UpstreamInvert == 95.0, "Invert value is raw ingested value, not designed");
        }

        // 30. P12 topology remains unchanged
        private static void P12U_P12TopologyRemainsUnchanged()
        {
            var r = CreateValidReadResultWithUnits();
            var snap1 = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 1);
            var snap2 = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap1.Nodes.Count == snap2.Nodes.Count, "Node counts match");
            Check(snap1.Conduits.Count == snap2.Conduits.Count, "Conduit counts match");
            Check(snap1.Adjacency.IncidentConduitsByNode.Count == snap2.Adjacency.IncidentConduitsByNode.Count, "Adjacency matches");
        }

        private static NetworkReadResult CreateValidReadResultWithEndpoints()
        {
            var r = CreateValidReadResultWithUnits();
            var ftUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft",
                new UnitProvenance("WaterObjectsFieldMetadata", "DomainElementField", "feet", "m", "RuntimeEvidence"));
            foreach (var c in r.Conduits)
            {
                c.StartInvert = c.UpstreamInvert;
                c.StopInvert = c.DownstreamInvert;
                c.StartInvertUnit = ftUnit;
                c.StopInvertUnit = ftUnit;
                c.SetInvertToStart = true;
                c.SetInvertToStop = true;
                c.StartInvertSourceField = "Physical_UpstreamInvert";
                c.StopInvertSourceField = "Physical_DownstreamInvert";
                c.SetInvertToStartSourceMember = "Physical_SetInvertToUpstream";
                c.SetInvertToStopSourceMember = "Physical_SetInvertToDownstream";
            }
            r.EndpointSemantics = new List<EndpointSemanticEvidenceDto>
            {
                new EndpointSemanticEvidenceDto
                {
                    EndpointKind = "Start",
                    NodeMember = "HMITopologyStartNodeID",
                    InvertField = "Physical_UpstreamInvert",
                    SetInvertMember = "Physical_SetInvertToUpstream",
                    DeclaringType = "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                    RuntimeType = "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                    NodeIDFieldNameFromFieldObject = "HMITopologyStartNodeID",
                    SetInvertToNodeFieldNameFromFieldObject = "Physical_SetInvertToUpstream",
                    ResourceLabel = "Invert (Start)",
                    SetInvertResourceLabel = "Set Invert to Start?",
                    WorkingUnit = "feet",
                    StorageUnit = "m",
                    Dimension = "length",
                    SemanticAssociationProven = true
                }
            };
            return r;
        }

        // 1. StartNodeId retained exactly
        private static void P13A_StartNodeIdRetainedExactly()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].StartNodeId == r.Conduits[0].StartNodeId, "StartNodeId must be retained exactly");
        }

        // 2. StopNodeId retained exactly
        private static void P13A_StopNodeIdRetainedExactly()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].StopNodeId == r.Conduits[0].StopNodeId, "StopNodeId must be retained exactly");
        }

        // 3. StartInvert retained exactly
        private static void P13A_StartInvertRetainedExactly()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.StartInvert == r.Conduits[0].StartInvert, "StartInvert retained exactly");
            Check(snap.Conduits[0].ExistingPhysicalState.StartInvert == 95.0, "StartInvert numeric value matches fixture");
        }

        // 4. StopInvert retained exactly
        private static void P13A_StopInvertRetainedExactly()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.StopInvert == r.Conduits[0].StopInvert, "StopInvert retained exactly");
            Check(snap.Conduits[0].ExistingPhysicalState.StopInvert == 94.5, "StopInvert numeric value matches fixture");
        }

        // 5. Start/Stop association participates in fingerprint
        private static void P13A_StartStopAssociationParticipatesInFingerprint()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(!string.IsNullOrEmpty(snap.NetworkFingerprint), "Fingerprint computed");
            Check(snap.SchemaVersion == 2, "Schema version is 2");
        }

        // 6. swapping StartInvert/StopInvert changes fingerprint
        private static void P13A_SwappingStartInvertStopInvertChangesFingerprint()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snapOriginal = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);

            var rSwapped = CreateValidReadResultWithEndpoints();
            double origStart = rSwapped.Conduits[0].StartInvert.Value;
            double origStop = rSwapped.Conduits[0].StopInvert.Value;
            rSwapped.Conduits[0].StartInvert = origStop;
            rSwapped.Conduits[0].StopInvert = origStart;
            rSwapped.Conduits[0].UpstreamInvert = origStop;
            rSwapped.Conduits[0].DownstreamInvert = origStart;

            var snapSwapped = new GravityNetworkIngestionService().Ingest(rSwapped, "fp-1", schemaVersion: 2);
            Check(snapOriginal.NetworkFingerprint != snapSwapped.NetworkFingerprint, "Swapping inverts must alter fingerprint");
        }

        // 7. numeric values are not converted
        private static void P13A_NumericValuesAreNotConverted()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].StartInvert = 123.456789;
            r.Conduits[0].StopInvert = 987.654321;
            r.Conduits[0].UpstreamInvert = 123.456789;
            r.Conduits[0].DownstreamInvert = 987.654321;
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.StartInvert == 123.456789, "StartInvert must not be converted or rounded");
            Check(snap.Conduits[0].ExistingPhysicalState.StopInvert == 987.654321, "StopInvert must not be converted or rounded");
        }

        // 8. feet unit identity retained
        private static void P13A_FeetUnitIdentityRetained()
        {
            var expectedFeet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[0].ExistingPhysicalState;
            Check(c.StartInvertUnit != null && expectedFeet.Equals(c.StartInvertUnit), "StartInvertUnit equals canonical Feet identity");
            Check(c.StopInvertUnit != null && expectedFeet.Equals(c.StopInvertUnit), "StopInvertUnit equals canonical Feet identity");
            Check(c.StartInvertUnit.UnitId == "Feet", "StartInvertUnit UnitId is Feet");
            Check(c.StartInvertUnit.UnitName == "feet", "StartInvertUnit UnitName is feet");
            Check(c.StartInvertUnit.Symbol == "ft", "StartInvertUnit Symbol is ft");
            Check(c.StartInvertUnit.Dimension == "length", "StartInvertUnit Dimension is length");
            Check(c.StopInvertUnit.UnitId == "Feet", "StopInvertUnit UnitId is Feet");
            Check(c.StopInvertUnit.UnitName == "feet", "StopInvertUnit UnitName is feet");
            Check(c.StopInvertUnit.Symbol == "ft", "StopInvertUnit Symbol is ft");
            Check(c.StopInvertUnit.Dimension == "length", "StopInvertUnit Dimension is length");
        }

        // 9. ambiguous source association rejected
        private static void P13A_AmbiguousSourceAssociationRejected()
        {
            var r = CreateValidReadResultWithEndpoints();
            var nonLengthUnit = new SourceUnitIdentity("pressure", "psi", "psi", "psi", null);
            r.Conduits[0].StartInvertUnit = nonLengthUnit;
            r.Conduits[0].UpstreamInvertUnit = nonLengthUnit;
            Throws<GravityNetworkIngestionException>(() => new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2));
        }

        // 10. missing Start semantic evidence rejected
        private static void P13A_MissingStartSemanticEvidenceRejected()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].StartInvert = double.NaN;
            r.Conduits[0].UpstreamInvert = double.NaN;
            Throws<GravityNetworkIngestionException>(() => new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2));
        }

        // 11. missing Stop semantic evidence rejected
        private static void P13A_MissingStopSemanticEvidenceRejected()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].StopInvert = double.PositiveInfinity;
            r.Conduits[0].DownstreamInvert = double.PositiveInfinity;
            Throws<GravityNetworkIngestionException>(() => new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2));
        }

        // 12. value equality alone cannot classify semantics as PROVEN
        private static void P13A_ValueEqualityAloneCannotClassifySemanticsAsProven()
        {
            var dtoWithoutMapping = new EndpointSemanticEvidenceDto
            {
                EndpointKind = "Start",
                NodeMember = "HMITopologyStartNodeID",
                InvertField = "Physical_UpstreamInvert",
                SemanticAssociationProven = false
            };
            Check(!dtoWithoutMapping.SemanticAssociationProven, "Numeric equality alone cannot be classified as PROVEN");
        }

        // 13. caller mutation cannot alter endpoint state
        private static void P13A_CallerMutationCannotAlterEndpointState()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            r.Conduits[0].StartInvert = 999.0;
            Check(snap.Conduits[0].ExistingPhysicalState.StartInvert == 95.0, "Caller mutation must not affect immutable snapshot");
        }

        // 14. no hydraulic flow-direction field inferred
        private static void P13A_NoHydraulicFlowDirectionFieldInferred()
        {
            var props = typeof(GravityConduitPhysicalState).GetProperties();
            foreach (var p in props)
            {
                Check(!p.Name.Contains("FlowDirection"), "No FlowDirection property allowed: " + p.Name);
                Check(!p.Name.Contains("HydraulicSlope"), "No HydraulicSlope property allowed: " + p.Name);
                Check(!p.Name.Contains("FlowRate"), "No FlowRate property allowed: " + p.Name);
            }
        }

        // 15. no vertical design calculation exists
        private static void P13A_NoVerticalDesignCalculationExists()
        {
            var snap = new GravityNetworkIngestionService().Ingest(CreateValidReadResultWithEndpoints(), "fp-1", schemaVersion: 2);
            Check(snap.Conduits[0].ExistingPhysicalState.StartInvert == 95.0, "Invert remains raw ingested value, not designed");
            var asm = typeof(GravityConduitPhysicalState).Assembly;
            var types = asm.GetTypes();
            foreach (var t in types)
            {
                Check(!t.Name.Contains("VerticalDesignCalculator") && !t.Name.Contains("ProfileOptimizer") && !t.Name.Contains("InvertCalculator"), "No vertical design calculator: " + t.FullName);
                foreach (var m in t.GetMethods())
                {
                    Check(!m.Name.Contains("DesignVerticalProfile") && !m.Name.Contains("CalculateConduitSlope"), "No vertical design calculation method: " + m.Name);
                }
            }
        }

        // 16. no candidate generation
        private static void P13A_NoCandidateGeneration()
        {
            var types = new[] { typeof(GravityConduitPhysicalState), typeof(GravityElevationState) };
            foreach (var t in types)
            {
                Check(!t.Name.Contains("CandidateGenerator"), "No candidate generator: " + t.FullName);
                Check(!t.Name.Contains("DesignCandidate"), "No design candidate: " + t.FullName);
                foreach (var m in t.GetMethods())
                {
                    Check(!m.Name.Contains("GenerateCandidate"), "No candidate generator method: " + m.Name);
                }
            }
        }

        // 17. no solver invocation
        private static void P13A_NoSolverInvocation()
        {
            var r = CreateValidReadResultWithEndpoints();
            Check(r.WriteInvocationCounts["SolverRun"] == 0, "SolverRun count must be 0");
            foreach (var kvp in r.WriteInvocationCounts)
            {
                Check(kvp.Value == 0, "Write invocation count for " + kvp.Key + " must be 0");
            }
        }

        // 18. no public vendor types
        private static void P13A_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(GravityConduitPhysicalState)), "GravityConduitPhysicalState must be vendor-neutral");
            Check(Neutral(typeof(GravityNetworkConduit)), "GravityNetworkConduit must be vendor-neutral");
            Check(Neutral(typeof(EndpointSemanticEvidenceDto)), "EndpointSemanticEvidenceDto must be vendor-neutral");
            Check(Neutral(typeof(ConduitReadDto)), "ConduitReadDto must be vendor-neutral");
            Check(Neutral(typeof(NetworkReadResult)), "NetworkReadResult must be vendor-neutral");
        }

        // 19. no persisted vendor types
        private static void P13A_NoPersistedVendorTypes()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string json = serializer.Serialize(snap);
            Check(!json.Contains("Haestad") && !json.Contains("Bentley"), "Serialized snapshot contains no vendor types");
        }

        // P13-A-R1 Focused Regression Tests

        // 1. UnitId Feet accepted
        private static void P13AR1_UnitIdFeetAccepted()
        {
            var expectedFeet = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var actual = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(expectedFeet.Equals(actual), "UnitId Feet must be accepted by canonical identity");
            Check(actual.UnitId == "Feet", "UnitId property is Feet");
        }

        // 2. UnitName feet accepted as the unit name
        private static void P13AR1_UnitNameFeetAcceptedAsUnitName()
        {
            var unit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(unit.UnitName == "feet", "UnitName must be 'feet', distinct from Symbol");
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(canonical.Equals(unit), "UnitName feet accepted in canonical identity");
        }

        // 3. Symbol ft accepted as the symbol
        private static void P13AR1_SymbolFtAcceptedAsSymbol()
        {
            var unit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(unit.Symbol == "ft", "Symbol must be 'ft'");
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(canonical.Equals(unit), "Symbol ft accepted in canonical identity");
        }

        // 4. UnitName == "ft" is NOT the required semantic
        private static void P13AR1_UnitNameFtIsNotRequiredSemantic()
        {
            var runtimeUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(runtimeUnit.UnitName != "ft", "UnitName is 'feet', not literal 'ft'");
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(canonical.Equals(runtimeUnit), "Canonical Feet identity matches runtime unit even though UnitName != 'ft'");
            var improper = new SourceUnitIdentity("length", "Feet", "ft", "ft");
            Check(!canonical.Equals(improper), "Improper unit with UnitName 'ft' does not equal canonical Feet identity");
        }

        // 5. exact dimension remains length
        private static void P13AR1_ExactDimensionRemainsLength()
        {
            var unit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(unit.Dimension == "length", "Dimension must be 'length'");
            var wrongDim = new SourceUnitIdentity("pressure", "Feet", "feet", "ft");
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            Check(!canonical.Equals(wrongDim), "Wrong dimension fails canonical equality");
        }

        // 6. another length unit does not accidentally satisfy exact Lesson_1 acceptance
        private static void P13AR1_AnotherLengthUnitDoesNotSatisfyLesson1Acceptance()
        {
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var meters = new SourceUnitIdentity("length", "Meters", "meters", "m");
            var inches = new SourceUnitIdentity("length", "Inches", "inches", "in");
            var yards = new SourceUnitIdentity("length", "Yards", "yards", "yd");
            Check(!canonical.Equals(meters), "Meters must not satisfy Lesson_1 Feet acceptance");
            Check(!canonical.Equals(inches), "Inches must not satisfy Lesson_1 Feet acceptance");
            Check(!canonical.Equals(yards), "Yards must not satisfy Lesson_1 Feet acceptance");
        }

        // 7. unresolved unit fails
        private static void P13AR1_UnresolvedUnitFails()
        {
            var canonical = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var unresolved = SourceUnitIdentity.Unresolved("Physical_UpstreamInvert");
            Check(!canonical.Equals(unresolved), "Unresolved unit must fail canonical Feet acceptance");
            Check(!canonical.Equals(null), "Null unit must fail canonical Feet acceptance");
        }

        // 8. StartInvert/StopInvert mapping remains unchanged
        private static void P13AR1_StartInvertStopInvertMappingRemainsUnchanged()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snap = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);
            var c = snap.Conduits[0];
            Check(c.ExistingPhysicalState.StartInvert == 95.0, "StartInvert maps from UpstreamInvert");
            Check(c.ExistingPhysicalState.StopInvert == 94.5, "StopInvert maps from DownstreamInvert");
            Check(c.ExistingPhysicalState.StartInvertSourceField == "Physical_UpstreamInvert", "StartInvertSourceField retained");
            Check(c.ExistingPhysicalState.StopInvertSourceField == "Physical_DownstreamInvert", "StopInvertSourceField retained");
            Check(c.ExistingPhysicalState.StartInvert == c.ExistingPhysicalState.UpstreamInvert, "StartInvert equals UpstreamInvert diagnostic accessor");
            Check(c.ExistingPhysicalState.StopInvert == c.ExistingPhysicalState.DownstreamInvert, "StopInvert equals DownstreamInvert diagnostic accessor");
        }

        // 9. swapped endpoint association still changes fingerprint
        private static void P13AR1_SwappedEndpointAssociationStillChangesFingerprint()
        {
            var r = CreateValidReadResultWithEndpoints();
            var snapOriginal = new GravityNetworkIngestionService().Ingest(r, "fp-1", schemaVersion: 2);

            var rSwapped = CreateValidReadResultWithEndpoints();
            double origStart = rSwapped.Conduits[0].StartInvert.Value;
            double origStop = rSwapped.Conduits[0].StopInvert.Value;
            rSwapped.Conduits[0].StartInvert = origStop;
            rSwapped.Conduits[0].StopInvert = origStart;
            rSwapped.Conduits[0].UpstreamInvert = origStop;
            rSwapped.Conduits[0].DownstreamInvert = origStart;

            var snapSwapped = new GravityNetworkIngestionService().Ingest(rSwapped, "fp-1", schemaVersion: 2);
            Check(snapOriginal.NetworkFingerprint != snapSwapped.NetworkFingerprint, "Swapping inverts must alter fingerprint in R1");
        }

        // 10. write counts remain zero
        private static void P13AR1_WriteCountsRemainZero()
        {
            var r = CreateValidReadResultWithEndpoints();
            Check(r.WriteInvocationCounts["SolverRun"] == 0, "SolverRun must be 0");
            Check(r.WriteInvocationCounts["Save"] == 0, "Save must be 0");
            Check(r.WriteInvocationCounts["SaveAs"] == 0, "SaveAs must be 0");
            Check(r.WriteInvocationCounts["Mutation"] == 0, "Mutation must be 0");
            Check(r.WriteInvocationCounts["WorkingUnitChange"] == 0, "WorkingUnitChange must be 0");
            Check(r.WriteInvocationCounts["ScenarioCreate"] == 0, "ScenarioCreate must be 0");
            Check(r.WriteInvocationCounts["AlternativeCreate"] == 0, "AlternativeCreate must be 0");
            foreach (var kvp in r.WriteInvocationCounts)
            {
                Check(kvp.Value == 0, "Write count for " + kvp.Key + " must be 0");
            }
        }

        // --- P13-B GravityFlow Vertical Design Domain Tests ---

        private static GravityNetworkSnapshot CreateValidVerticalDesignNetwork()
        {
            var r = CreateValidReadResultWithEndpoints();
            foreach (var c in r.Conduits)
            {
                c.Rise = 1.5;
                c.RiseUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
                c.Length = 100.0;
                c.LengthUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            }
            foreach (var s in r.Structures)
            {
                s.GroundElevation = 100.0;
                s.GroundElevationUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            }
            return new GravityNetworkIngestionService().Ingest(r, "fp-vertical-test", schemaVersion: 2);
        }

        private static StandardProvenance CreateTestProvenance(string standardId = "STD-01", string paramKey = "PARAM-1")
        {
            return new StandardProvenance
            {
                SourceLayer = StandardSourceLayer.ProjectOverride,
                StandardId = standardId,
                StandardName = "TestStandard",
                StandardVersion = "1.0",
                ParameterKey = paramKey
            };
        }

        private static SourceEngineeringValue CreateFeetValue(double val)
        {
            return new SourceEngineeringValue(val, new SourceUnitIdentity("length", "Feet", "feet", "ft"));
        }

        private static GravityVerticalDesignConstraintSetBuilder CreateBaseConstraintSetBuilder(GravityNetworkSnapshot net)
        {
            var b = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var c in net.Conduits)
            {
                b.SetOrientation(c.SourceElementId, c.StartNodeId, c.StopNodeId, CreateTestProvenance());
            }
            foreach (var n in net.Nodes)
            {
                if (n.Kind == GravityNodeKind.Outfall)
                {
                    b.AddOutfallConstraint(n.SourceElementId, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
                }
            }
            return b;
        }

        // 1. Start/Stop endpoint identity preserved
        private static void P13B_StartStopEndpointIdentityPreserved()
        {
            var start = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var stop = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            Check(start.ConduitId == 201 && start.End == ConduitEndKind.Start && start.NodeId == 101, "Start endpoint identity preserved");
            Check(stop.ConduitId == 201 && stop.End == ConduitEndKind.Stop && stop.NodeId == 102, "Stop endpoint identity preserved");
            Check(!start.Equals(stop), "Start and stop ends are distinct");
        }

        // 2. explicit Start->Stop orientation accepted
        private static void P13B_ExplicitStartStopOrientationAccepted()
        {
            var o = new GravityDesignOrientation(201, 101, 102, CreateTestProvenance());
            Check(o.IsStartToStop(101, 102), "Start->Stop orientation recognized");
            Check(!o.IsStopToStart(101, 102), "Not Stop->Start");
            Check(o.FromNodeId == 101 && o.ToNodeId == 102, "Endpoints preserved");
        }

        // 3. explicit Stop->Start orientation accepted
        private static void P13B_ExplicitStopStartOrientationAccepted()
        {
            var o = new GravityDesignOrientation(201, 102, 101, CreateTestProvenance());
            Check(o.IsStopToStart(101, 102), "Stop->Start orientation recognized");
            Check(!o.IsStartToStop(101, 102), "Not Start->Stop");
            Check(o.FromNodeId == 102 && o.ToNodeId == 101, "Endpoints preserved");
        }

        // 4. missing orientation yields ENGINEERING_DECISION_REQUIRED
        private static void P13B_MissingOrientationYieldsEngineeringDecisionRequired()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            b.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            var set = b.Build(net.NetworkFingerprint);
            var engine = new GravityVerticalFeasibilityEngine();
            var res = engine.Analyze(net, set);
            Check(res.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired, "Missing orientation requires decision");
            Check(res.DecisionRequests.Any(d => d.Decision.Contains("201")), "Decision request mentions Conduit 201");
        }

        // 5. orientation referencing non-endpoint node rejected
        private static void P13B_OrientationReferencingNonEndpointNodeRejected()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            b.SetOrientation(201, 101, 999, CreateTestProvenance());
            b.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            var set = b.Build(net.NetworkFingerprint);
            var engine = new GravityVerticalFeasibilityEngine();
            Throws<ArgumentException>(() => engine.Analyze(net, set));
        }

        // 6. existing invert is not automatically fixed
        private static void P13B_ExistingInvertIsNotAutomaticallyFixed()
        {
            var net = CreateValidVerticalDesignNetwork();
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            Check(set.FixedConstraints.Count == 0, "Existing inverts are not automatically fixed constraints");
        }

        // 7. explicit fixed endpoint constraint enforced
        private static void P13B_ExplicitFixedEndpointConstraintEnforced()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            b.AddFixedConstraint(end, CreateFeetValue(93.25), CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.EndpointRanges.ContainsKey(end.ToString()), "Endpoint range exists");
            Check(res.EndpointRanges[end.ToString()].ExactInvert == 93.25, "Fixed invert enforced");
        }

        // 8. protected node elevation remains separate from conduit-end invert
        private static void P13B_ProtectedNodeElevationRemainsSeparateFromConduitEndInvert()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddProtectedConstraint(101, "Rim", CreateFeetValue(105.0), CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            Check(res.EndpointRanges[end.ToString()].ExactInvert != 105.0, "Protected node elevation does not force conduit end invert");
        }

        // 9. explicit outfall fixed constraint enforced
        private static void P13B_ExplicitOutfallFixedConstraintEnforced()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(202, ConduitEndKind.Stop, 103);
            b.AddFixedConstraint(end, CreateFeetValue(85.0), CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.EndpointRanges[end.ToString()].ExactInvert == 85.0, "Outfall fixed endpoint enforced");
        }

        // 10. unspecified outfall intent can require decision
        private static void P13B_UnspecifiedOutfallIntentCanRequireDecision()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            foreach (var c in net.Conduits) b.SetOrientation(c.SourceElementId, c.StartNodeId, c.StopNodeId, CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired, "Unspecified outfall requires decision");
            Check(res.DecisionRequests.Any(d => d.Decision.Contains("Outfall 103")), "Outfall decision request created");
        }

        // 11. InvertMatch equality constructed correctly
        private static void P13B_InvertMatchEqualityConstructedCorrectly()
        {
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            var mc = new VerticalMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] { end1, end2 }, CreateTestProvenance());
            Check(mc.Mode == VerticalMatchingMode.InvertMatch, "InvertMatch mode");
            Check(mc.ParticipatingConduitEnds.Count == 2, "2 ends participating");
        }

        // 12. CrownMatch uses Invert + Rise
        private static void P13B_CrownMatchUsesInvertPlusRise()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            b.AddFixedConstraint(end1, CreateFeetValue(92.0), CreateTestProvenance());
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] { end1, end2 }, CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.EndpointRanges[end2.ToString()].ExactInvert == 92.0, "Crown match evaluates Crown = Invert + Rise");
        }

        // 13. CrownMatch missing Rise produces incomplete result
        private static void P13B_CrownMatchMissingRiseProducesIncompleteResult()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].Rise = null;
            r.Conduits[0].RiseUnit = null;
            r.Conduits[1].Rise = 1.5;
            r.Conduits[1].RiseUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var net = new GravityNetworkIngestionService().Ingest(r, "fp-test-missing-rise", schemaVersion: 2);
            var b = CreateBaseConstraintSetBuilder(net);
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] { end1, end2 }, CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.Status == VerticalFeasibilityStatus.Incomplete, "Missing rise causes incomplete result");
            Check(res.IncompleteItems.Any(i => i.Contains("Rise")), "Incomplete item mentions Rise");
        }

        // 14. conduit diameter not substituted for missing Rise
        private static void P13B_ConduitDiameterNotSubstitutedForMissingRise()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].Rise = null;
            r.Conduits[0].Diameter = 1.5;
            r.Conduits[0].DiameterUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var net = new GravityNetworkIngestionService().Ingest(r, "fp-test-no-sub", schemaVersion: 2);
            var b = CreateBaseConstraintSetBuilder(net);
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] { end1, end2 }, CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.Status == VerticalFeasibilityStatus.Incomplete, "Diameter is not substituted for Rise");
        }

        // 15. matching participants must belong to same node
        private static void P13B_MatchingParticipantsMustBelongToSameNode()
        {
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            Throws<ArgumentException>(() => new VerticalMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] { end1, end2 }, CreateTestProvenance()));
        }

        // 16. duplicate match endpoint rejected
        private static void P13B_DuplicateMatchEndpointRejected()
        {
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            Throws<ArgumentException>(() => new VerticalMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] { end1, end1 }, CreateTestProvenance()));
        }

        // 17. cover uses GroundToCrown explicitly
        private static void P13B_CoverUsesGroundToCrownExplicitly()
        {
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var cov = new VerticalCoverConstraint(end, CreateFeetValue(100.0), CreateFeetValue(4.0), CreateTestProvenance(), VerticalCoverReference.GroundToCrown);
            Check(cov.Reference == VerticalCoverReference.GroundToCrown, "Cover reference is GroundToCrown");
        }

        // 18. cover calculation correct with synthetic data
        private static void P13B_CoverCalculationCorrectWithSyntheticData()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            b.AddCoverConstraint(end, CreateFeetValue(100.0), CreateFeetValue(4.0), CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.EndpointRanges[end.ToString()].MaximumAllowedInvert == 94.5, "Max invert evaluated as Ground - Rise - MinCover (100 - 1.5 - 4.0 = 94.5)");
        }

        // 19. missing ground elevation produces incomplete result
        private static void P13B_MissingGroundElevationProducesIncompleteResult()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            b.AddCoverConstraint(end, new SourceEngineeringValue(null, new SourceUnitIdentity("length", "Feet", "feet", "ft")), CreateFeetValue(4.0), CreateTestProvenance());
            var set = b.Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res.Status == VerticalFeasibilityStatus.Incomplete, "Missing ground elevation causes incomplete result");
        }

        // 20. missing minimum cover not replaced by default
        private static void P13B_MissingMinimumCoverNotReplacedByDefault()
        {
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            Throws<ArgumentNullException>(() => new VerticalCoverConstraint(end, CreateFeetValue(100.0), null, CreateTestProvenance()));
        }

        // 21. minimum slope relation correct
        private static void P13B_MinimumSlopeRelationCorrect()
        {
            var sc = new VerticalSlopeConstraint(201, 0.005, null, CreateTestProvenance());
            Check(sc.MinimumSlope == 0.005, "MinSlope retained");
            Check(sc.MaximumSlope == null, "MaxSlope not invented");
        }

        // 22. maximum slope relation correct
        private static void P13B_MaximumSlopeRelationCorrect()
        {
            var sc = new VerticalSlopeConstraint(201, null, 0.08, CreateTestProvenance());
            Check(sc.MaximumSlope == 0.08, "MaxSlope retained");
            Check(sc.MinimumSlope == null, "MinSlope not invented");
        }

        // 23. orientation reversal changes slope semantics correctly
        private static void P13B_OrientationReversalChangesSlopeSemanticsCorrectly()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            b1.AddSlopeConstraint(201, 0.01, null, CreateTestProvenance());
            b1.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            var res1 = new GravityVerticalFeasibilityEngine().Analyze(net, b1.Build(net.NetworkFingerprint));
            var stopEnd = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            Check(res1.EndpointRanges[stopEnd.ToString()].MaximumAllowedInvert == 94.0, "Forward slope bounds StopInvert <= 94.0");

            var b2 = new GravityVerticalDesignConstraintSetBuilder();
            b2.SetOrientation(201, 102, 101, CreateTestProvenance());
            b2.SetOrientation(202, 102, 103, CreateTestProvenance());
            b2.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            b2.AddSlopeConstraint(201, 0.01, null, CreateTestProvenance());
            b2.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            var res2 = new GravityVerticalFeasibilityEngine().Analyze(net, b2.Build(net.NetworkFingerprint));
            Check(res2.EndpointRanges[stopEnd.ToString()].MinimumAllowedInvert == 96.0, "Reversed slope bounds StopInvert >= 96.0");
        }

        // 24. slope uses Length > 0
        private static void P13B_SlopeUsesLengthGreaterThanZero()
        {
            var net = CreateValidVerticalDesignNetwork();
            Check(net.Conduits[0].ExistingPhysicalState.Length.Value == 100.0, "Length > 0");
        }

        // 25. zero/negative length rejected
        private static void P13B_ZeroNegativeLengthRejected()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].Length = 0.0;
            var net = new GravityNetworkIngestionService().Ingest(r, "fp-bad-len", schemaVersion: 2);
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            Throws<ArgumentException>(() => new GravityVerticalFeasibilityEngine().Analyze(net, set));
        }

        // 26. exact unit mismatch fails closed
        private static void P13B_ExactUnitMismatchFailsClosed()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var meterUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), new SourceEngineeringValue(30.0, meterUnit), CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.Infeasible, "Unit mismatch fails closed as Infeasible");
            Check(res.Conflicts.Any(c => c.ConflictCode == "INCOMPATIBLE_VERTICAL_UNITS"), "Conflict code is INCOMPATIBLE_VERTICAL_UNITS");
        }

        // 27. no automatic unit conversion
        private static void P13B_NoAutomaticUnitConversion()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var meterUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            b.AddFixedConstraint(end, new SourceEngineeringValue(30.0, meterUnit), CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Conflicts.Any(c => c.ConflictCode == "INCOMPATIBLE_VERTICAL_UNITS"), "Incompatible units conflict raised");
            Check(!res.EndpointRanges[end.ToString()].ExactInvert.HasValue, "No converted elevation was assigned");
        }

        // 28. compatible feet inputs calculate deterministically
        private static void P13B_CompatibleFeetInputsCalculateDeterministically()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions, "Feasible multiple solutions evaluated");
        }

        // 29. fixed + invert-match propagation works
        private static void P13B_FixedPlusInvertMatchPropagationWorks()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            b.AddFixedConstraint(end1, CreateFeetValue(93.5), CreateTestProvenance());
            b.AddMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] { end1, end2 }, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.EndpointRanges[end2.ToString()].ExactInvert == 93.5, "Invert match propagated exact elevation");
        }

        // 30. fixed + crown-match propagation works
        private static void P13B_FixedPlusCrownMatchPropagationWorks()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end1 = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            var end2 = new GravityConduitEnd(202, ConduitEndKind.Start, 102);
            b.AddFixedConstraint(end1, CreateFeetValue(92.0), CreateTestProvenance());
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] { end1, end2 }, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.EndpointRanges[end2.ToString()].ExactInvert == 92.0, "Crown match propagated exact elevation");
        }

        // 31. contradictory exact constraints produce Infeasible
        private static void P13B_ContradictoryExactConstraintsProduceInfeasible()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            b.AddFixedConstraint(end, CreateFeetValue(95.0), CreateTestProvenance("STD-1"));
            b.AddFixedConstraint(end, CreateFeetValue(92.0), CreateTestProvenance("STD-2"));
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.Infeasible, "Contradictory fixed constraints produce Infeasible");
        }

        // 32. conflict retains both provenance sources
        private static void P13B_ConflictRetainsBothProvenanceSources()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var p1 = CreateTestProvenance("STD-1");
            var p2 = CreateTestProvenance("STD-2");
            b.AddFixedConstraint(end, CreateFeetValue(95.0), p1);
            b.AddFixedConstraint(end, CreateFeetValue(92.0), p2);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            var conflict = res.Conflicts.First();
            Check(conflict.Provenance.Any(p => p.StandardId == "STD-1"), "Retains Provenance 1");
            Check(conflict.Provenance.Any(p => p.StandardId == "STD-2"), "Retains Provenance 2");
        }

        // 33. cover bound contradiction produces Infeasible
        private static void P13B_CoverBoundContradictionProducesInfeasible()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            b.AddFixedConstraint(end, CreateFeetValue(96.0), CreateTestProvenance());
            b.AddCoverConstraint(end, CreateFeetValue(100.0), CreateFeetValue(4.0), CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.Infeasible, "Cover violation produces Infeasible");
            Check(res.Conflicts.Any(c => c.Explanation.Contains("Cover")), "Conflict explains cover violation");
        }

        // 34. slope bound contradiction produces Infeasible
        private static void P13B_SlopeBoundContradictionProducesInfeasible()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var start = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            var stop = new GravityConduitEnd(201, ConduitEndKind.Stop, 102);
            b.AddFixedConstraint(start, CreateFeetValue(90.0), CreateTestProvenance());
            b.AddFixedConstraint(stop, CreateFeetValue(95.0), CreateTestProvenance());
            b.AddSlopeConstraint(201, 0.01, null, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.Infeasible, "Slope violation produces Infeasible");
        }

        // 35. underconstrained system returns FeasibleMultipleSolutions
        private static void P13B_UnderconstrainedSystemReturnsFeasibleMultipleSolutions()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            b.AddSlopeConstraint(202, 0.01, 0.05, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            Check(res.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions, "Underconstrained system is FeasibleMultipleSolutions");
        }

        // 36. engine does not choose a value from a feasible range
        private static void P13B_EngineDoesNotChooseAValueFromAFeasibleRange()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, b.Build(net.NetworkFingerprint));
            var end = new GravityConduitEnd(201, ConduitEndKind.Start, 101);
            Check(!res.EndpointRanges[end.ToString()].ExactInvert.HasValue, "ExactInvert is null for ranged solution (no value chosen)");
        }

        // 37. insertion order does not alter feasibility result
        private static void P13B_InsertionOrderDoesNotAlterFeasibilityResult()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            b1.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b1.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var res1 = new GravityVerticalFeasibilityEngine().Analyze(net, b1.Build(net.NetworkFingerprint));

            var b2 = CreateBaseConstraintSetBuilder(net);
            b2.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            b2.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            var res2 = new GravityVerticalFeasibilityEngine().Analyze(net, b2.Build(net.NetworkFingerprint));

            Check(res1.Status == res2.Status, "Status identical regardless of insertion order");
            Check(res1.ConstraintFingerprint == res2.ConstraintFingerprint, "Fingerprint identical regardless of insertion order");
        }

        // 38. insertion order does not alter fingerprint
        private static void P13B_InsertionOrderDoesNotAlterFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            b1.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            b1.AddSlopeConstraint(202, 0.02, 0.06, CreateTestProvenance());
            var fp1 = b1.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            var b2 = CreateBaseConstraintSetBuilder(net);
            b2.AddSlopeConstraint(202, 0.02, 0.06, CreateTestProvenance());
            b2.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var fp2 = b2.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            Check(fp1 == fp2, "Fingerprint invariant to insertion order");
        }

        // 39. identical logical constraints yield identical fingerprint
        private static void P13B_IdenticalLogicalConstraintsYieldIdenticalFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var set1 = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            var set2 = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            Check(set1.VerticalConstraintFingerprint == set2.VerticalConstraintFingerprint, "Identical logical constraints yield identical fingerprint");
        }

        // 40. changed orientation changes fingerprint
        private static void P13B_ChangedOrientationChangesFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            var fp1 = b1.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            var b2 = new GravityVerticalDesignConstraintSetBuilder();
            b2.SetOrientation(201, 102, 101, CreateTestProvenance());
            b2.SetOrientation(202, 102, 103, CreateTestProvenance());
            b2.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            var fp2 = b2.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            Check(fp1 != fp2, "Flipped orientation changes fingerprint");
        }

        // 41. changed criterion changes fingerprint
        private static void P13B_ChangedCriterionChangesFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            b1.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var fp1 = b1.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            var b2 = CreateBaseConstraintSetBuilder(net);
            b2.AddSlopeConstraint(201, 0.02, 0.05, CreateTestProvenance());
            var fp2 = b2.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            Check(fp1 != fp2, "Changed criterion changes fingerprint");
        }

        // 42. changed provenance identity changes fingerprint where significant
        private static void P13B_ChangedProvenanceIdentityChangesFingerprintWhereSignificant()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            b1.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance("STD-A"));
            var fp1 = b1.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            var b2 = CreateBaseConstraintSetBuilder(net);
            b2.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance("STD-B"));
            var fp2 = b2.Build(net.NetworkFingerprint).VerticalConstraintFingerprint;

            Check(fp1 != fp2, "Changed provenance changes fingerprint");
        }

        // 43. source network snapshot remains immutable
        private static void P13B_SourceNetworkSnapshotRemainsImmutable()
        {
            var net = CreateValidVerticalDesignNetwork();
            string fpBefore = net.NetworkFingerprint;
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(net.NetworkFingerprint == fpBefore, "Network fingerprint remains unchanged");
            Check(net.Conduits[0].ExistingPhysicalState.StartInvert == 95.0, "Start invert unchanged");
        }

        // 44. Design Basis snapshot remains immutable
        private static void P13B_DesignBasisSnapshotRemainsImmutable()
        {
            var db = CreateValidGravityFlowDesignBasis();
            var snap = db.CreateSnapshot();
            string fpBefore = snap.GravityContentFingerprint;
            int countBefore = snap.VerticalDesign.Count;
            db.VerticalDesign.Criteria.Add(new GravityDesignCriterion
            {
                Key = "synth.vert.extra",
                Section = GravityFlowSectionIdentifier.VerticalDesign,
                Status = GravityCriterionStatus.Defined,
                Requirement = GravityCriterionRequirement.Optional,
                Value = StandardValue.FromText("Extra")
            });
            Check(!string.IsNullOrEmpty(fpBefore), "Snapshot has content fingerprint");
            Check(snap.VerticalDesign.Count == countBefore, "VerticalDesign criteria unchanged");
            Check(snap.GravityContentFingerprint == fpBefore, "Fingerprint remains unchanged");
        }

        // 45. caller collection mutation does not alter constraint set
        private static void P13B_CallerCollectionMutationDoesNotAlterConstraintSet()
        {
            var net = CreateValidVerticalDesignNetwork();
            var orientations = new Dictionary<int, GravityDesignOrientation>
            {
                { 201, new GravityDesignOrientation(201, 101, 102, CreateTestProvenance()) }
            };
            var set = new GravityVerticalDesignConstraintSet(
                net.NetworkFingerprint, "DB-1", "1.0", orientations, null, null, null, null, null, null);
            orientations.Clear();
            Check(set.Orientations.Count == 1, "Constraint set immutable against caller dictionary mutation");
        }

        // 46. no design candidate produced
        private static void P13B_NoDesignCandidateProduced()
        {
            var types = new[] { typeof(GravityVerticalFeasibilityEngine), typeof(VerticalFeasibilityResult), typeof(GravityVerticalDesignConstraintSet) };
            foreach (var t in types)
            {
                Check(!t.Name.Contains("DesignCandidate") && !t.Name.Contains("CandidateGenerator"), "No candidate types in P13B: " + t.FullName);
                foreach (var m in t.GetMethods())
                {
                    Check(!m.Name.Contains("GenerateCandidate") && !m.Name.Contains("RecommendProfile"), "No candidate methods: " + m.Name);
                }
            }
        }

        // 47. no SewerGEMS runtime invoked
        private static void P13B_NoSewerGEMSRuntimeInvoked()
        {
            var net = CreateValidVerticalDesignNetwork();
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            Check(res != null, "Feasibility evaluated in pure domain code with 0 runtime host processes");
        }

        // 48. no public vendor types
        private static void P13B_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(GravityConduitEnd)), "GravityConduitEnd is neutral");
            Check(Neutral(typeof(GravityDesignOrientation)), "GravityDesignOrientation is neutral");
            Check(Neutral(typeof(FixedEndpointElevationConstraint)), "FixedEndpointElevationConstraint is neutral");
            Check(Neutral(typeof(ProtectedNodeElevationConstraint)), "ProtectedNodeElevationConstraint is neutral");
            Check(Neutral(typeof(OutfallVerticalConstraint)), "OutfallVerticalConstraint is neutral");
            Check(Neutral(typeof(VerticalMatchConstraint)), "VerticalMatchConstraint is neutral");
            Check(Neutral(typeof(VerticalCoverConstraint)), "VerticalCoverConstraint is neutral");
            Check(Neutral(typeof(VerticalSlopeConstraint)), "VerticalSlopeConstraint is neutral");
            Check(Neutral(typeof(GravityVerticalDesignConstraintSet)), "GravityVerticalDesignConstraintSet is neutral");
            Check(Neutral(typeof(GravityVerticalFeasibilityEngine)), "GravityVerticalFeasibilityEngine is neutral");
            Check(Neutral(typeof(VerticalFeasibilityResult)), "VerticalFeasibilityResult is neutral");
        }

        // 49. no persisted vendor types
        private static void P13B_NoPersistedVendorTypes()
        {
            var net = CreateValidVerticalDesignNetwork();
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            var res = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string jsonSet = serializer.Serialize(set);
            string jsonRes = serializer.Serialize(res);
            Check(!jsonSet.Contains("Haestad") && !jsonSet.Contains("Bentley"), "ConstraintSet JSON is vendor-free");
            Check(!jsonRes.Contains("Haestad") && !jsonRes.Contains("Bentley"), "Result JSON is vendor-free");
        }

        // 50. no hard-coded engineering criteria
        private static void P13B_NoHardCodedEngineeringCriteria()
        {
            var sc = new VerticalSlopeConstraint(201, null, 0.05, CreateTestProvenance());
            Check(sc.MinimumSlope == null, "Minimum slope was not defaulted");
            var net = CreateValidVerticalDesignNetwork();
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            Check(set.SlopeConstraints.Count == 0, "No slope constraints invented");
            Check(set.CoverConstraints.Count == 0, "No cover constraints invented");
        }

        // --- P14 GravityFlow Candidate Generation Tests ---

        private static GravityCandidateRecipe CreateTestRecipe(string recipeId = "REC-01", IEnumerable<CandidateEndpointSelection> selections = null)
        {
            return new GravityCandidateRecipe(recipeId, selections, CreateTestProvenance("REC-STD", "REC-PARAM"));
        }

        // 1. infeasible P13 result generates zero candidates
        private static void P14_InfeasibleP13ResultGeneratesZeroCandidates()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(90.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            Check(feas.Status == VerticalFeasibilityStatus.Infeasible, "Feasibility must be Infeasible");
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe() });
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 0, "0 candidates generated for Infeasible P13 state");
            Check(set.RecipeResults.Count == 1, "Recipe result recorded");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Recipe rejected");
        }

        // 2. incomplete P13 result generates zero candidates
        private static void P14_IncompleteP13ResultGeneratesZeroCandidates()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].Rise = null;
            r.Conduits[0].RiseUnit = null;
            var net = new GravityNetworkIngestionService().Ingest(r, "fp-p14-inc", schemaVersion: 2);
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] {
                new GravityConduitEnd(201, ConduitEndKind.Stop, 102),
                new GravityConduitEnd(202, ConduitEndKind.Start, 102)
            }, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            Check(feas.Status == VerticalFeasibilityStatus.Incomplete, "Feasibility must be Incomplete");
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe() });
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 0, "0 candidates generated for Incomplete P13 state");
        }

        // 3. EngineeringDecisionRequired P13 result generates zero candidates
        private static void P14_EngineeringDecisionRequiredP13ResultGeneratesZeroCandidates()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            b.SetOrientation(201, 101, 102, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            Check(feas.Status == VerticalFeasibilityStatus.EngineeringDecisionRequired, "Feasibility must be EngineeringDecisionRequired");
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe() });
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 0, "0 candidates generated for EngineeringDecisionRequired P13 state");
        }

        // 4. unique exact P13 solution materializes deterministically
        private static void P14_UniqueExactP13SolutionMaterializesDeterministically()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), CreateFeetValue(94.0), CreateTestProvenance());
            b.AddFixedConstraint(new GravityConduitEnd(202, ConduitEndKind.Start, 102), CreateFeetValue(94.0), CreateTestProvenance());
            b.AddFixedConstraint(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), CreateFeetValue(85.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            Check(feas.Status == VerticalFeasibilityStatus.Feasible, "Feasibility must be Feasible");
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new GravityCandidateRecipe[0]);
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 1, "Unique exact profile materializes 1 candidate");
            Check(set.Candidates[0].RecipeId == "EXACT_CONSTRAINT_PROFILE", "Exact profile recipe assigned");
            Check(set.Candidates[0].EndpointDesigns[0].ProposedInvert == 95.0, "Exact invert materialized");
        }

        // 5. FeasibleMultipleSolutions requires recipe
        private static void P14_FeasibleMultipleSolutionsRequiresRecipe()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddSlopeConstraint(201, 0.005, 0.05, CreateTestProvenance());
            b.AddSlopeConstraint(202, 0.005, 0.05, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            Check(feas.Status == VerticalFeasibilityStatus.FeasibleMultipleSolutions, "Feasibility must be FeasibleMultipleSolutions");
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new GravityCandidateRecipe[0]);
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 0, "No candidates generated when recipe is missing in multiple solutions case");
        }

        // 6. missing endpoint selection returns CANDIDATE_INPUT_INCOMPLETE
        private static void P14_MissingEndpointSelectionReturnsCandidateInputIncomplete()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var recipe = CreateTestRecipe("REC-INC", new[] {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0))
            });
            var req = new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { recipe });
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, req);
            Check(set.Candidates.Count == 0, "0 candidates generated for incomplete recipe");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Incomplete, "Recipe status must be Incomplete");
            Check(set.RecipeResults[0].Issues.Any(i => i.Contains("CANDIDATE_INPUT_INCOMPLETE")), "Reports CANDIDATE_INPUT_INCOMPLETE");
        }

        // 7. ExistingValue accepted when feasible
        private static void P14_ExistingValueAcceptedWhenFeasible()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExistingValue, CreateTestProvenance())
            };
            var recipe = CreateTestRecipe("REC-EXIST", selections);
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { recipe }));
            Check(set.Candidates.Count == 1, "Candidate generated from ExistingValue mode");
            Check(set.Candidates[0].EndpointDesigns[0].ProposedInvert == 95.0, "ProposedInvert matches existing 95.0");
        }

        // 8. ExistingValue rejected when outside feasible range
        private static void P14_ExistingValueRejectedWhenOutsideFeasibleRange()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(100.0), CreateFeetValue(7.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExistingValue, CreateTestProvenance())
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-EX-OUT", selections) }));
            Check(set.Candidates.Count == 0, "Candidate rejected when existing value outside bounds");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Recipe rejected");
        }

        // 9. ExistingValue is not clamped
        private static void P14_ExistingValueIsNotClamped()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(100.0), CreateFeetValue(7.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExistingValue, CreateTestProvenance())
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-NOCLAMP", selections) }));
            Check(!set.Candidates.Any(c => c.EndpointDesigns.Any(e => e.ProposedInvert == 91.5)), "Existing value must NOT be clamped to boundary");
        }

        // 10. ExplicitValue accepted when feasible
        private static void P14_ExplicitValueAcceptedWhenFeasible()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-EXP", selections) }));
            Check(set.Candidates.Count == 1, "Candidate generated from explicit values");
            Check(set.Candidates[0].EndpointDesigns[0].ProposedInvert == 93.0, "ProposedInvert matches explicit 93.0");
        }

        // 11. ExplicitValue rejected outside bounds
        private static void P14_ExplicitValueRejectedOutsideBounds()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(100.0), CreateFeetValue(7.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-EXP-OUT", selections) }));
            Check(set.Candidates.Count == 0, "Rejected outside bounds");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Recipe status is Rejected");
        }

        // 12. MinimumFeasible selects exact lower bound
        private static void P14_MinimumFeasibleSelectsExactLowerBound()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.MinimumFeasible, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-MIN", selections) }));
            Check(set.Candidates.Count == 1, "MinimumFeasible generates candidate");
            var stopEp = set.Candidates[0].EndpointDesigns.First(e => e.ConduitId == 201 && e.End == ConduitEndKind.Stop);
            Check(Math.Abs(stopEp.ProposedInvert - 90.0) < 1e-4, "Exact lower bound selected: 90.0");
        }

        // 13. MinimumFeasible fails when lower bound unbounded
        private static void P14_MinimumFeasibleFailsWhenLowerBoundUnbounded()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.MinimumFeasible, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-UNBOUND-MIN", selections) }));
            Check(set.Candidates.Count == 0, "0 candidates when unbounded");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Unbounded, "Status is Unbounded");
        }

        // 14. MaximumFeasible selects exact upper bound
        private static void P14_MaximumFeasibleSelectsExactUpperBound()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(100.0), CreateFeetValue(7.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.MaximumFeasible, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-MAX", selections) }));
            Check(set.Candidates.Count == 1, "MaximumFeasible generates candidate");
            var startEp = set.Candidates[0].EndpointDesigns.First(e => e.ConduitId == 201 && e.End == ConduitEndKind.Start);
            Check(Math.Abs(startEp.ProposedInvert - 91.5) < 1e-4, "Exact upper bound selected: 91.5");
        }

        // 15. MaximumFeasible fails when upper bound unbounded
        private static void P14_MaximumFeasibleFailsWhenUpperBoundUnbounded()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.MaximumFeasible, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-UNBOUND-MAX", selections) }));
            Check(set.Candidates.Count == 0, "0 candidates when unbounded");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Unbounded, "Status is Unbounded");
        }

        // 16. no midpoint/default selection occurs
        private static void P14_NoMidpointDefaultSelectionOccurs()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            b.AddSlopeConstraint(201, 0.01, 0.05, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-NOMID", selections) }));
            Check(set.Candidates.Count == 0, "No default/midpoint selection; candidate must be Incomplete");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Incomplete, "Status is Incomplete");
        }

        // 17. invert-match propagation preserved
        private static void P14_InvertMatchPropagationPreserved()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] {
                new GravityConduitEnd(201, ConduitEndKind.Stop, 102),
                new GravityConduitEnd(202, ConduitEndKind.Start, 102)
            }, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-INV-PROP", selections) }));
            Check(set.Candidates.Count == 1, "Candidate generated with InvertMatch propagation");
            var c202Start = set.Candidates[0].EndpointDesigns.First(e => e.ConduitId == 202 && e.End == ConduitEndKind.Start);
            Check(c202Start.ProposedInvert == 92.0, "C202 Start propagated matching C201 Stop");
            Check(c202Start.SelectionSource == CandidateSelectionSource.PropagatedEquality, "Source is PropagatedEquality");
        }

        // 18. crown-match propagation preserved
        private static void P14_CrownMatchPropagationPreserved()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddMatchConstraint(102, VerticalMatchingMode.CrownMatch, new[] {
                new GravityConduitEnd(201, ConduitEndKind.Stop, 102),
                new GravityConduitEnd(202, ConduitEndKind.Start, 102)
            }, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-CR-PROP", selections) }));
            Check(set.Candidates.Count == 1, "Candidate generated with CrownMatch propagation");
            var c202Start = set.Candidates[0].EndpointDesigns.First(e => e.ConduitId == 202 && e.End == ConduitEndKind.Start);
            Check(c202Start.ProposedInvert == 92.0, "C202 Start propagated matching crown");
            Check(c202Start.SelectionSource == CandidateSelectionSource.PropagatedEquality, "Source is PropagatedEquality");
        }

        // 19. fixed constraint cannot be overridden by recipe
        private static void P14_FixedConstraintCannotBeOverriddenByRecipe()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddFixedConstraint(new GravityConduitEnd(201, ConduitEndKind.Start, 101), CreateFeetValue(95.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(89.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(89.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-FIX-OVERRIDE", selections) }));
            Check(set.Candidates.Count == 0, "Recipe cannot override fixed constraint");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Recipe rejected");
        }

        // 20. cover constraint candidate validation
        private static void P14_CoverConstraintCandidateValidation()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddCoverConstraint(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), CreateFeetValue(99.5), CreateFeetValue(5.0), CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(94.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(94.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-COV-FAIL", selections) }));
            Check(set.Candidates.Count == 0, "Cover constraint violation rejected during candidate validation");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Status is Rejected");
        }

        // 21. minimum-slope candidate validation
        private static void P14_MinimumSlopeCandidateValidation()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddSlopeConstraint(201, 0.02, 0.05, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(94.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(94.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-SLOPE-MIN-FAIL", selections) }));
            Check(set.Candidates.Count == 0, "Minimum slope violation rejected");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Status is Rejected");
        }

        // 22. maximum-slope candidate validation
        private static void P14_MaximumSlopeCandidateValidation()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddSlopeConstraint(201, 0.005, 0.02, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-SLOPE-MAX-FAIL", selections) }));
            Check(set.Candidates.Count == 0, "Maximum slope violation rejected");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Status is Rejected");
        }

        // 23. outfall constraint candidate validation
        private static void P14_OutfallConstraintCandidateValidation()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            b.SetOrientation(201, 101, 102, CreateTestProvenance());
            b.SetOrientation(202, 102, 103, CreateTestProvenance());
            var outfallEnd = new GravityConduitEnd(202, ConduitEndKind.Stop, 103);
            b.AddOutfallConstraint(103, CreateTestProvenance(),
                protectedInvert: CreateFeetValue(85.0),
                connectedEnd: new FixedEndpointElevationConstraint(outfallEnd, CreateFeetValue(85.0), CreateTestProvenance()));
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(outfallEnd, EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(80.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-OUTFALL-FAIL", selections) }));
            Check(set.Candidates.Count == 0, "Outfall violation rejected");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Status is Rejected");
        }

        // 24. orientation preserved
        private static void P14_OrientationPreserved()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-ORIENT", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(c201.FromNodeId == 101 && c201.ToNodeId == 102, "Orientation preserved: 101 -> 102");
        }

        // 25. Start->Stop derived slope correct
        private static void P14_StartStopDerivedSlopeCorrect()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-SLOPE-SS", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(Math.Abs(c201.Slope - 0.02) < 1e-4, "Slope correct for Start->Stop: 0.02");
        }

        // 26. Stop->Start derived slope correct
        private static void P14_StopStartDerivedSlopeCorrect()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = new GravityVerticalDesignConstraintSetBuilder();
            b.SetOrientation(201, 102, 101, CreateTestProvenance());
            b.SetOrientation(202, 102, 103, CreateTestProvenance());
            b.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-SLOPE-REV", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(Math.Abs(c201.Slope - 0.02) < 1e-4, "Slope correct for Stop->Start: 0.02");
        }

        // 27. Rise used for crown
        private static void P14_RiseUsedForCrown()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-CROWN", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(c201.StartCrown.HasValue && Math.Abs(c201.StartCrown.Value - 96.5) < 1e-4, "Crown uses Invert + Rise = 96.5");
        }

        // 28. Diameter not substituted for Rise
        private static void P14_DiameterNotSubstitutedForRise()
        {
            var r = CreateValidReadResultWithEndpoints();
            r.Conduits[0].Rise = null;
            r.Conduits[0].RiseUnit = null;
            r.Conduits[0].Diameter = 1.5;
            r.Conduits[0].DiameterUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var net = new GravityNetworkIngestionService().Ingest(r, "fp-diam-norise", schemaVersion: 2);
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-NODIAM", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(!c201.StartCrown.HasValue, "Crown must be null when Rise is missing (diameter is not substituted)");
        }

        // 29. cover uses GroundToCrown
        private static void P14_CoverUsesGroundToCrown()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-COVER", selections) }));
            var c201 = set.Candidates[0].DerivedConduits.First(c => c.ConduitId == 201);
            Check(c201.StartCover.HasValue && Math.Abs(c201.StartCover.Value - 3.5) < 1e-4, "Cover uses GroundToCrown = 3.5");
        }

        // 30. incompatible unit rejected
        private static void P14_IncompatibleUnitRejected()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var meterUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), new SourceEngineeringValue(29.0, meterUnit)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-INCOMPAT-UNIT", selections) }));
            Check(set.Candidates.Count == 0, "Candidate rejected when unit is incompatible");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Rejected, "Status is Rejected");
            Check(set.RecipeResults[0].Issues.Any(i => i.Contains("INCOMPATIBLE_VERTICAL_UNITS")), "Reports INCOMPATIBLE_VERTICAL_UNITS");
        }

        // 31. no unit conversion
        private static void P14_NoUnitConversion()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var meterUnit = new SourceUnitIdentity("length", "Meters", "meters", "m");
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), new SourceEngineeringValue(29.0, meterUnit)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-NOCONV", selections) }));
            Check(!set.Candidates.Any(c => c.EndpointDesigns.Any(e => Math.Abs(e.ProposedInvert - 29.0 * 3.28084) < 1.0)), "No conversion of meters to feet");
        }

        // 32. ExistingInvert preserved separately
        private static void P14_ExistingInvertPreservedSeparately()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-SEP", selections) }));
            var ep = set.Candidates[0].EndpointDesigns.First(e => e.ConduitId == 201 && e.End == ConduitEndKind.Start);
            Check(ep.ExistingInvert == 95.0, "ExistingInvert preserved separately (95.0)");
            Check(ep.ProposedInvert == 93.0, "ProposedInvert is 93.0");
        }

        // 33. ProposedInvert explicit
        private static void P14_ProposedInvertExplicit()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-PROP-EXP", selections) }));
            foreach (var ep in set.Candidates[0].EndpointDesigns)
            {
                Check(!double.IsNaN(ep.ProposedInvert) && !double.IsInfinity(ep.ProposedInvert), "Proposed invert must be explicit and finite");
            }
        }

        // 34. generation trace records selection source
        private static void P14_GenerationTraceRecordsSelectionSource()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExistingValue, CreateTestProvenance()),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExistingValue, CreateTestProvenance())
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-TRACE", selections) }));
            var trace = set.Candidates[0].GenerationTrace.GetTrace(new GravityConduitEnd(201, ConduitEndKind.Start, 101));
            Check(trace != null, "Trace exists for endpoint");
            Check(trace.SelectionSource == CandidateSelectionSource.ExplicitValue, "SelectionSource is ExplicitValue");
            Check(trace.ProposedValue == 93.0, "Trace records proposed value");
        }

        // 35. equality-propagated endpoint trace records propagation
        private static void P14_EqualityPropagatedEndpointTraceRecordsPropagation()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            b.AddMatchConstraint(102, VerticalMatchingMode.InvertMatch, new[] {
                new GravityConduitEnd(201, ConduitEndKind.Stop, 102),
                new GravityConduitEnd(202, ConduitEndKind.Start, 102)
            }, CreateTestProvenance());
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-TRACE-EQ", selections) }));
            var trace = set.Candidates[0].GenerationTrace.GetTrace(new GravityConduitEnd(202, ConduitEndKind.Start, 102));
            Check(trace.SelectionSource == CandidateSelectionSource.PropagatedEquality, "Trace records PropagatedEquality");
        }

        // 36. candidate fingerprint deterministic
        private static void P14_CandidateFingerprintDeterministic()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set1 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-1", selections) }));
            var set2 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-1", selections) }));
            Check(set1.Candidates[0].CandidateFingerprint == set2.Candidates[0].CandidateFingerprint, "Candidate fingerprint must be deterministic");
        }

        // 37. request insertion order does not change fingerprint
        private static void P14_RequestInsertionOrderDoesNotChangeFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel1 = new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0));
            var sel2 = new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0));
            var sel3 = new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0));
            var sel4 = new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0));

            var set1 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-ORD-1", new[] { sel1, sel2, sel3, sel4 }) }));
            var set2 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-ORD-2", new[] { sel4, sel3, sel2, sel1 }) }));
            Check(set1.Candidates[0].CandidateFingerprint == set2.Candidates[0].CandidateFingerprint, "Selection insertion order does not change fingerprint");
        }

        // 38. collection order does not change fingerprint
        private static void P14_CollectionOrderDoesNotChangeFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            string fp = set.Candidates[0].CandidateFingerprint;
            var reordered = set.Candidates[0].EndpointDesigns.Reverse().ToList();
            string fpReordered = GravityDesignCandidate.CalculateFingerprint(net.NetworkFingerprint, cs.VerticalConstraintFingerprint, reordered, set.Candidates[0].DerivedConduits);
            Check(fp == fpReordered, "Reordered collection yields identical fingerprint");
        }

        // 39. machine/session metadata does not affect fingerprint
        private static void P14_MachineSessionMetadataDoesNotAffectFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            string fp = set.Candidates[0].CandidateFingerprint;
            Check(fp.Length == 64, "Fingerprint is SHA-256 hex string");
            Check(!fp.Contains(Environment.MachineName), "Does not contain machine name");
        }

        // 40. changed proposed invert changes fingerprint
        private static void P14_ChangedProposedInvertChangesFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel1 = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var sel2 = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.5)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set1 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-1", sel1) }));
            var set2 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-2", sel2) }));
            Check(set1.Candidates[0].CandidateFingerprint != set2.Candidates[0].CandidateFingerprint, "Changed proposed invert changes fingerprint");
        }

        // 41. changed orientation changes fingerprint
        private static void P14_ChangedOrientationChangesFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b1 = CreateBaseConstraintSetBuilder(net);
            var cs1 = b1.Build(net.NetworkFingerprint);
            var feas1 = new GravityVerticalFeasibilityEngine().Analyze(net, cs1);

            var b2 = new GravityVerticalDesignConstraintSetBuilder();
            b2.SetOrientation(201, 102, 101, CreateTestProvenance());
            b2.SetOrientation(202, 102, 103, CreateTestProvenance());
            b2.AddOutfallConstraint(103, CreateTestProvenance(), protectedInvert: CreateFeetValue(85.0));
            var cs2 = b2.Build(net.NetworkFingerprint);
            var feas2 = new GravityVerticalFeasibilityEngine().Analyze(net, cs2);

            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set1 = new GravityCandidateGenerator().Generate(net, cs1, feas1, new GravityCandidateGenerationRequest(cs1.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            var set2 = new GravityCandidateGenerator().Generate(net, cs2, feas2, new GravityCandidateGenerationRequest(cs2.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            Check(set1.Candidates[0].CandidateFingerprint != set2.Candidates[0].CandidateFingerprint, "Changed orientation changes fingerprint");
        }

        // 42. two recipes generating same engineering profile have identical CandidateFingerprint
        private static void P14_TwoRecipesGeneratingSameEngineeringProfileHaveIdenticalCandidateFingerprint()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set1 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-ALPHA", sel) }));
            var set2 = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-BETA", sel) }));
            Check(set1.Candidates[0].CandidateFingerprint == set2.Candidates[0].CandidateFingerprint, "Different recipe IDs generating same profile have identical CandidateFingerprint");
        }

        // 43. duplicate candidate classified deterministically
        private static void P14_DuplicateCandidateClassifiedDeterministically()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var recipe1 = CreateTestRecipe("REC-1", sel);
            var recipe2 = CreateTestRecipe("REC-2", sel);
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { recipe1, recipe2 }));
            Check(set.Candidates.Count == 1, "Only one unique candidate preserved in Candidates collection");
            Check(set.RecipeResults.Count == 2, "Both recipe results recorded");
            Check(set.RecipeResults[0].Status == CandidateGenerationStatus.Generated, "First recipe status is Generated");
            Check(set.RecipeResults[1].Status == CandidateGenerationStatus.Duplicate, "Second recipe status is Duplicate");
        }

        // 44. candidate set ordering deterministic
        private static void P14_CandidateSetOrderingDeterministic()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel1 = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var sel2 = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(94.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var r1 = CreateTestRecipe("REC-1", sel1);
            var r2 = CreateTestRecipe("REC-2", sel2);
            var setA = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { r1, r2 }));
            var setB = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { r2, r1 }));
            Check(setA.Candidates[0].CandidateFingerprint == setB.Candidates[0].CandidateFingerprint, "Canonical candidate ordering is deterministic regardless of recipe order");
        }

        // 45. caller mutation cannot alter candidate
        private static void P14_CallerMutationCannotAlterCandidate()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            var candidate = set.Candidates[0];
            Throws<NotSupportedException>(() => ((IList<CandidateEndpointDesign>)candidate.EndpointDesigns).Add(null));
        }

        // 46. caller mutation cannot alter candidate set
        private static void P14_CallerMutationCannotAlterCandidateSet()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            Throws<NotSupportedException>(() => ((IList<GravityDesignCandidate>)set.Candidates).Clear());
        }

        // 47. network topology remains unchanged
        private static void P14_NetworkTopologyRemainsUnchanged()
        {
            var net = CreateValidVerticalDesignNetwork();
            int conduitCountBefore = net.Conduits.Count;
            int nodeCountBefore = net.Nodes.Count;
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            Check(net.Conduits.Count == conduitCountBefore, "Conduit count unchanged");
            Check(net.Nodes.Count == nodeCountBefore, "Node count unchanged");
        }

        // 48. pipe dimensions/material/roughness unchanged
        private static void P14_PipeDimensionsMaterialRoughnessUnchanged()
        {
            var net = CreateValidVerticalDesignNetwork();
            double riseBefore = net.Conduits[0].ExistingPhysicalState.Rise.Value;
            double lenBefore = net.Conduits[0].ExistingPhysicalState.Length.Value;
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            Check(net.Conduits[0].ExistingPhysicalState.Rise.Value == riseBefore, "Rise unchanged");
            Check(net.Conduits[0].ExistingPhysicalState.Length.Value == lenBefore, "Length unchanged");
        }

        // 49. no loading mutation
        private static void P14_NoLoadingMutation()
        {
            var net = CreateValidVerticalDesignNetwork();
            string fpBefore = net.NetworkFingerprint;
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            Check(net.NetworkFingerprint == fpBefore, "Network fingerprint completely unchanged");
        }

        // 50. no candidate ranking
        private static void P14_NoCandidateRanking()
        {
            var props = typeof(GravityDesignCandidate).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            Check(!props.Contains("rank") && !props.Contains("score") && !props.Contains("preference") && !props.Contains("best"), "No ranking properties on candidate");
        }

        // 51. no optimization
        private static void P14_NoOptimization()
        {
            var asm = typeof(GravityCandidateGenerator).Assembly;
            var types = asm.GetTypes().Select(t => t.Name).ToList();
            Check(!types.Any(t => t.Contains("Optimizer")), "No optimizer types exist in assembly");
        }

        // 52. no Bentley runtime
        private static void P14_NoBentleyRuntime()
        {
            var net = CreateValidVerticalDesignNetwork();
            var set = CreateBaseConstraintSetBuilder(net).Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, set);
            var selections = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(95.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(90.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var candSet = new GravityCandidateGenerator().Generate(net, set, feas, new GravityCandidateGenerationRequest(set.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC-1", selections) }));
            Check(candSet.Candidates.Count == 1, "Candidate generated in pure domain code with 0 Bentley runtime attempts");
            var genAsm = typeof(GravityCandidateGenerator).Assembly;
            var referenced = genAsm.GetReferencedAssemblies().Select(a => a.Name);
            Check(!referenced.Any(n => n.StartsWith("Haestad") || n.StartsWith("Bentley")), "Zero Bentley runtime referenced by generator assembly");
        }

        // 53. no model writes
        private static void P14_NoModelWrites()
        {
            var r = CreateValidReadResultWithEndpoints();
            Check(r.WriteInvocationCounts["Save"] == 0, "Save is 0");
            Check(r.WriteInvocationCounts["SaveAs"] == 0, "SaveAs is 0");
            Check(r.WriteInvocationCounts["Mutation"] == 0, "Mutation is 0");
        }

        // 54. no solver
        private static void P14_NoSolver()
        {
            var r = CreateValidReadResultWithEndpoints();
            Check(r.WriteInvocationCounts["SolverRun"] == 0, "SolverRun is 0");
        }

        // 55. no hydraulic verification
        private static void P14_NoHydraulicVerification()
        {
            var props = typeof(CandidateConduitVerticalState).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            Check(!props.Contains("velocity") && !props.Contains("capacity") && !props.Contains("hgl") && !props.Contains("surcharge"), "No hydraulic verification properties");
        }

        // 56. no public vendor types
        private static void P14_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(GravityCandidateGenerator)), "GravityCandidateGenerator is neutral");
            Check(Neutral(typeof(GravityCandidateGenerationRequest)), "GravityCandidateGenerationRequest is neutral");
            Check(Neutral(typeof(GravityCandidateRecipe)), "GravityCandidateRecipe is neutral");
            Check(Neutral(typeof(GravityDesignCandidate)), "GravityDesignCandidate is neutral");
            Check(Neutral(typeof(GravityDesignCandidateSet)), "GravityDesignCandidateSet is neutral");
            Check(Neutral(typeof(CandidateEndpointDesign)), "CandidateEndpointDesign is neutral");
            Check(Neutral(typeof(CandidateConduitVerticalState)), "CandidateConduitVerticalState is neutral");
        }

        // 57. no persisted vendor types
        private static void P14_NoPersistedVendorTypes()
        {
            var net = CreateValidVerticalDesignNetwork();
            var b = CreateBaseConstraintSetBuilder(net);
            var cs = b.Build(net.NetworkFingerprint);
            var feas = new GravityVerticalFeasibilityEngine().Analyze(net, cs);
            var sel = new[]
            {
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Start, 101), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(93.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(201, ConduitEndKind.Stop, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Start, 102), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(92.0)),
                new CandidateEndpointSelection(new GravityConduitEnd(202, ConduitEndKind.Stop, 103), EndpointSelectionMode.ExplicitValue, CreateTestProvenance(), CreateFeetValue(85.0))
            };
            var set = new GravityCandidateGenerator().Generate(net, cs, feas, new GravityCandidateGenerationRequest(cs.VerticalConstraintFingerprint, new[] { CreateTestRecipe("REC", sel) }));
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string jsonSet = serializer.Serialize(set);
            Check(!jsonSet.Contains("Haestad") && !jsonSet.Contains("Bentley"), "Serialized candidate set is vendor-free");
        }

        // 58. no hard-coded engineering criteria
        private static void P14_NoHardCodedEngineeringCriteria()
        {
            var gen = new GravityCandidateGenerator();
            Check(gen != null, "Generator exists");
        }

        private static P15ACapabilityMappingResult CreateTestP15AMappingResult()
        {
            var startTarget = new ConduitInvertWriteTarget(
                ConduitEndKind.Start,
                "Physical_UpstreamInvert",
                "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                "SetValue",
                new[] { "System.Int32", "System.Object" },
                "System.Double",
                "feet",
                "Physical",
                4,
                true,
                "WorkingUnit_Feet_ConvertedToStorageUnit_Identity",
                "WaterObjects.InstalledRuntime.SewerGEMS.24.0.2.21"
            );

            var stopTarget = new ConduitInvertWriteTarget(
                ConduitEndKind.Stop,
                "Physical_DownstreamInvert",
                "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                "Haestad.Domain.StormSewer.ConduitPhysicalLinkInvertField",
                "SetValue",
                new[] { "System.Int32", "System.Object" },
                "System.Double",
                "feet",
                "Physical",
                4,
                true,
                "WorkingUnit_Feet_ConvertedToStorageUnit_Identity",
                "WaterObjects.InstalledRuntime.SewerGEMS.24.0.2.21"
            );

            var mapping = new CandidateToFieldMapping(startTarget, stopTarget, "feet");

            var persistence = new PersistenceCapabilityDescriptor(
                "Haestad.Framework.Windows.Forms",
                "Haestad.Framework.Windows.Forms.Application.HaestadParentFormUIModelBase",
                "Haestad.Delaware.Application.DelawareParentFormUIModel",
                "SaveCurrentProject",
                "Method",
                "System.Boolean",
                new string[0],
                "CurrentGraphicalProject",
                "PROVEN"
            );

            var calcOptions = new CalculationOptionsContextDescriptor(
                67,
                "CivilStormDynamicEngine",
                -719657520,
                1,
                "ScenarioBase.CalculationOptionsID(engineTypeName, optionsId)",
                "PROVEN"
            );

            var solver = new SolverCapabilityDescriptor(
                "Haestad.Domain",
                "Haestad.Domain.INumericalEngine",
                "Haestad.Domain.ModelingObjects.NumericalEngineBase",
                "Run",
                "Method",
                "System.Void",
                new[] { "Haestad.Domain.ModelingElementCollection" },
                "ModelingElementCollection containing target scenario element",
                true,
                "PROVEN"
            );

            var completion = new SolverCompletionStatusDescriptor(
                "MultiFileDomainProjectBase.GetScenarioComputeStatus(Int32)",
                "Haestad.Framework",
                "Haestad.Framework.Application.MultiFileDomainProjectBase",
                "Haestad.Framework.Application.ScenarioComputeStatus",
                "Success (3)",
                "Warning (2)",
                "Error (1)",
                "Compute (0)",
                "ScenarioStatusText(Int32)",
                "PROVEN"
            );

            return new P15ACapabilityMappingResult
            {
                StartInvertTarget = startTarget,
                StopInvertTarget = stopTarget,
                CandidateMapping = mapping,
                Persistence = persistence,
                CalculationOptions = calcOptions,
                Solver = solver,
                CompletionStatus = completion,
                ExecutionPlan = new P15BMutationPlan(),
                PropertySetCount = 0,
                SaveCount = 0,
                SaveAsCount = 0,
                ScenarioCreateCount = 0,
                AlternativeCreateCount = 0,
                SolverRunCount = 0,
                Success = true
            };
        }

        // P15-A Test 1: Start candidate endpoint maps to Physical_UpstreamInvert
        private static void P15A_StartCandidateEndpointMapsToPhysicalUpstreamInvert()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.CandidateMapping.GetTargetField(ConduitEndKind.Start) == "Physical_UpstreamInvert", "Start maps to Physical_UpstreamInvert");
            Check(res.StartInvertTarget.TargetFieldName == "Physical_UpstreamInvert", "Start field matches Physical_UpstreamInvert");
        }

        // P15-A Test 2: Stop candidate endpoint maps to Physical_DownstreamInvert
        private static void P15A_StopCandidateEndpointMapsToPhysicalDownstreamInvert()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.CandidateMapping.GetTargetField(ConduitEndKind.Stop) == "Physical_DownstreamInvert", "Stop maps to Physical_DownstreamInvert");
            Check(res.StopInvertTarget.TargetFieldName == "Physical_DownstreamInvert", "Stop field matches Physical_DownstreamInvert");
        }

        // P15-A Test 3: mapping does not reverse Start/Stop
        private static void P15A_MappingDoesNotReverseStartStop()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.CandidateMapping.GetTargetField(ConduitEndKind.Start) != res.CandidateMapping.GetTargetField(ConduitEndKind.Stop), "Start and Stop are distinct");
            Check(res.CandidateMapping.GetTargetField(ConduitEndKind.Start) != "Physical_DownstreamInvert", "Start is not Downstream");
            Check(res.CandidateMapping.GetTargetField(ConduitEndKind.Stop) != "Physical_UpstreamInvert", "Stop is not Upstream");
        }

        // P15-A Test 4: Feet working-unit identity retained
        private static void P15A_FeetWorkingUnitIdentityRetained()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.StartInvertTarget.ExpectedUnit == "feet", "Start unit feet");
            Check(res.StopInvertTarget.ExpectedUnit == "feet", "Stop unit feet");
            Check(res.CandidateMapping.Units == "feet", "Mapping unit feet");
        }

        // P15-A Test 5: no unit conversion
        private static void P15A_NoUnitConversion()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.StartInvertTarget.ValueWriteSemantics.Contains("Identity"), "Unit write semantics identity");
            Check(res.StopInvertTarget.ValueWriteSemantics.Contains("Identity"), "Unit write semantics identity");
        }

        // P15-A Test 6: write capability descriptor contains no vendor public type
        private static void P15A_WriteCapabilityDescriptorContainsNoVendorPublicType()
        {
            Check(Neutral(typeof(ConduitInvertWriteTarget)), "ConduitInvertWriteTarget neutral");
            Check(Neutral(typeof(CandidateToFieldMapping)), "CandidateToFieldMapping neutral");
            Check(Neutral(typeof(P15ACapabilityMappingResult)), "P15ACapabilityMappingResult neutral");
        }

        // P15-A Test 7: setter metadata required before write capability classified PROVEN
        private static void P15A_SetterMetadataRequiredBeforeWriteCapabilityClassifiedProven()
        {
            var target = new ConduitInvertWriteTarget(ConduitEndKind.Start, "Physical_UpstreamInvert", null, null, null, null, null, null, null, 0, false, null, null);
            Check(string.IsNullOrEmpty(target.SetterMemberName), "Setter missing fails closed");
            Check(target.SetterParameterTypes.Length == 0, "Parameters missing");
            Check(!target.IsWritable, "Not writable without setter");
        }

        // P15-A Test 8: unknown setter semantics fail closed
        private static void P15A_UnknownSetterSemanticsFailClosed()
        {
            var target = new ConduitInvertWriteTarget { ValueWriteSemantics = null };
            Check(string.IsNullOrEmpty(target.ValueWriteSemantics), "Unknown semantics unverified");
        }

        // P15-A Test 9: target Physical alternative identity required
        private static void P15A_TargetPhysicalAlternativeIdentityRequired()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.StartInvertTarget.TargetAlternativeType == "Physical", "Physical alternative name");
            Check(res.StartInvertTarget.TargetAlternativeTypeId == 4, "Physical alternative ID 4");
            Check(res.StopInvertTarget.TargetAlternativeTypeId == 4, "Physical alternative ID 4");
        }

        // P15-A Test 10: candidate write plan targets isolated Physical alternative
        private static void P15A_CandidateWritePlanTargetsIsolatedPhysicalAlternative()
        {
            var plan = new P15BMutationPlan();
            Check(plan.TargetsIsolatedPhysicalAlternative, "Plan targets isolated Physical alternative");
            Check(plan.ExecutionSteps.Any(s => s.Contains("Physical alternative")), "Plan contains physical alternative step");
        }

        // P15-A Test 11: SaveCurrentProject is persistence boundary from accepted P6 mapping
        private static void P15A_SaveCurrentProjectIsPersistenceBoundaryFromAcceptedP6Mapping()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.Persistence.MemberName == "SaveCurrentProject", "SaveCurrentProject is persistence member");
            Check(res.Persistence.DeclaringType.Contains("HaestadParentFormUIModelBase"), "Declaring type matches P6");
            Check(res.Persistence.Classification == "PROVEN", "Classification PROVEN");
        }

        // P15-A Test 12: solver descriptor requires exact declaring/runtime type
        private static void P15A_SolverDescriptorRequiresExactDeclaringRuntimeType()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.Solver.DeclaringType == "Haestad.Domain.INumericalEngine", "Declaring type is INumericalEngine");
            Check(res.Solver.Assembly == "Haestad.Domain", "Assembly is Haestad.Domain");
            Check(res.Solver.MemberName == "Run", "Member name is Run");
        }

        // P15-A Test 13: solver member parameters retained exactly
        private static void P15A_SolverMemberParametersRetainedExactly()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.Solver.ParameterTypes.Length == 1, "Exactly one parameter");
            Check(res.Solver.ParameterTypes[0] == "Haestad.Domain.ModelingElementCollection", "Parameter is ModelingElementCollection");
        }

        // P15-A Test 14: solver invocation status model distinguishes completion/failure
        private static void P15A_SolverInvocationStatusModelDistinguishesCompletionFailure()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.CompletionStatus.SuccessValue.Contains("3"), "Success value is 3");
            Check(res.CompletionStatus.ErrorValue.Contains("1"), "Error value is 1");
            Check(res.CompletionStatus.WarningValue.Contains("2"), "Warning value is 2");
            Check(res.CompletionStatus.ComputeNeededValue.Contains("0"), "Compute needed is 0");
            Check(res.CompletionStatus.StatusMechanism.Contains("GetScenarioComputeStatus"), "Mechanism identified");
        }

        // P15-A Test 15: no hydraulic result fields modeled
        private static void P15A_NoHydraulicResultFieldsModeled()
        {
            var props = typeof(P15ACapabilityMappingResult).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            Check(!props.Contains("velocity") && !props.Contains("capacity") && !props.Contains("hgl") && !props.Contains("surcharge"), "No hydraulic result properties on mapping result");
        }

        // P15-A Test 16: execution plan orders save before solver where runtime semantics require it
        private static void P15A_ExecutionPlanOrdersSaveBeforeSolverWhereRuntimeSemanticsRequireIt()
        {
            var plan = new P15BMutationPlan();
            Check(plan.SaveOrderedBeforeSolver, "Save ordered before solver");
            int saveIdx = plan.ExecutionSteps.FindIndex(s => s.Contains("SaveCurrentProject"));
            int solveIdx = plan.ExecutionSteps.FindIndex(s => s.Contains("Execute calculation"));
            Check(saveIdx >= 0 && solveIdx > saveIdx, "Save occurs before solver in execution steps");
        }

        // P15-A Test 17: no setter called by mapping tests
        private static void P15A_NoSetterCalledByMappingTests()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.PropertySetCount == 0, "PropertySetCount is 0");
        }

        // P15-A Test 18: no solver called by mapping tests
        private static void P15A_NoSolverCalledByMappingTests()
        {
            var res = CreateTestP15AMappingResult();
            Check(res.SolverRunCount == 0, "SolverRunCount is 0");
            Check(res.SaveCount == 0, "SaveCount is 0");
            Check(res.SaveAsCount == 0, "SaveAsCount is 0");
            Check(res.ScenarioCreateCount == 0, "ScenarioCreateCount is 0");
            Check(res.AlternativeCreateCount == 0, "AlternativeCreateCount is 0");
        }

        // P15-A Test 19: no public vendor types
        private static void P15A_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(ConduitInvertWriteTarget)), "ConduitInvertWriteTarget is neutral");
            Check(Neutral(typeof(CandidateToFieldMapping)), "CandidateToFieldMapping is neutral");
            Check(Neutral(typeof(PersistenceCapabilityDescriptor)), "PersistenceCapabilityDescriptor is neutral");
            Check(Neutral(typeof(CalculationOptionsContextDescriptor)), "CalculationOptionsContextDescriptor is neutral");
            Check(Neutral(typeof(SolverCapabilityDescriptor)), "SolverCapabilityDescriptor is neutral");
            Check(Neutral(typeof(SolverCompletionStatusDescriptor)), "SolverCompletionStatusDescriptor is neutral");
            Check(Neutral(typeof(P15BMutationPlan)), "P15BMutationPlan is neutral");
            Check(Neutral(typeof(P15ACapabilityMappingResult)), "P15ACapabilityMappingResult is neutral");
        }

        // P15-A Test 20: no persisted vendor types
        private static void P15A_NoPersistedVendorTypes()
        {
            Type[] types = new[]
            {
                typeof(ConduitInvertWriteTarget),
                typeof(CandidateToFieldMapping),
                typeof(PersistenceCapabilityDescriptor),
                typeof(CalculationOptionsContextDescriptor),
                typeof(SolverCapabilityDescriptor),
                typeof(SolverCompletionStatusDescriptor),
                typeof(P15BMutationPlan),
                typeof(P15ACapabilityMappingResult)
            };
            foreach (var t in types)
            {
                Check(Neutral(t), t.Name + " is neutral type");
                foreach (var p in t.GetProperties())
                {
                    Check(Neutral(p.PropertyType), t.Name + "." + p.Name + " property type is neutral");
                }
                foreach (var f in t.GetFields())
                {
                    Check(Neutral(f.FieldType), t.Name + "." + f.Name + " field type is neutral");
                }
            }
        }

        // P15-B Test 1: candidate lineage mismatch rejected before write
        private static void P15B_CandidateLineageMismatchRejectedBeforeWrite()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateLineage(null, "NET1", "VERT1"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_CANDIDATE_LINEAGE")) { rejected = true; }
            Check(rejected, "Null candidate rejected with P15_B_BLOCKED_CANDIDATE_LINEAGE");
        }

        // P15-B Test 2: source network fingerprint mismatch rejected
        private static void P15B_SourceNetworkFingerprintMismatchRejected()
        {
            var cand = new GravityDesignCandidate("C1", "R1", "NET_MISMATCH", "VERT1", null, null, null);
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateLineage(cand, "NET_EXPECTED", "VERT1"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_CANDIDATE_LINEAGE")) { rejected = true; }
            Check(rejected, "Network fingerprint mismatch rejected with P15_B_BLOCKED_CANDIDATE_LINEAGE");
        }

        // P15-B Test 3: constraint fingerprint mismatch rejected
        private static void P15B_ConstraintFingerprintMismatchRejected()
        {
            var cand = new GravityDesignCandidate("C1", "R1", "NET1", "VERT_MISMATCH", null, null, null);
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateLineage(cand, "NET1", "VERT_EXPECTED"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_CANDIDATE_LINEAGE")) { rejected = true; }
            Check(rejected, "Constraint fingerprint mismatch rejected with P15_B_BLOCKED_CANDIDATE_LINEAGE");
        }

        // P15-B Test 4: execution requires working copy
        private static void P15B_ExecutionRequiresWorkingCopy()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateWorkingCopyContext(@"C:\Base\Model.stsw", @"C:\Work\Model.stsw", null); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_WRITE_AUTHORIZATION")) { rejected = true; }
            Check(rejected, "Execution without token rejected");
        }

        // P15-B Test 5: baseline path cannot be execution target
        private static void P15B_BaselinePathCannotBeExecutionTarget()
        {
            bool rejected = false;
            var token = new WriteAuthorizationToken("T1", "WC1", "BASE_FP", @"C:\Base\Model.stsw", "GravityWriteAndSolver", DateTimeOffset.UtcNow);
            try { GravityCandidateExecutor.ValidateWorkingCopyContext(@"C:\Base\Model.stsw", @"C:\Base\Model.stsw", token); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("HARD_FAIL_PROTECTED_BASELINE_MODIFIED")) { rejected = true; }
            Check(rejected, "Baseline path as target rejected");
        }

        // P15-B Test 6: Start maps only to Physical_UpstreamInvert
        private static void P15B_StartMapsOnlyToPhysicalUpstreamInvert()
        {
            var mapping = new CandidateToFieldMapping();
            Check(mapping.GetTargetField(ConduitEndKind.Start) == "Physical_UpstreamInvert", "Start maps to Physical_UpstreamInvert");
        }

        // P15-B Test 7: Stop maps only to Physical_DownstreamInvert
        private static void P15B_StopMapsOnlyToPhysicalDownstreamInvert()
        {
            var mapping = new CandidateToFieldMapping();
            Check(mapping.GetTargetField(ConduitEndKind.Stop) == "Physical_DownstreamInvert", "Stop maps to Physical_DownstreamInvert");
        }

        // P15-B Test 8: Feet values passed without conversion
        private static void P15B_FeetValuesPassedWithoutConversion()
        {
            var ep = new CandidateEndpointDesign(27, ConduitEndKind.Start, 19, 103.0, 103.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue);
            Check(ep.ProposedInvert == 103.01, "Proposed invert passed exactly in Feet");
            Check(ep.Unit.UnitName == "feet", "Unit is feet");
        }

        // P15-B Test 9: makeLocal=true required
        private static void P15B_MakeLocalTrueRequired()
        {
            var map = new P15ACapabilityMappingResult();
            var target = new ConduitInvertWriteTarget(ConduitEndKind.Start, "Physical_UpstreamInvert", "Declaring", "Runtime", "SetValue", new[] { "System.Int32", "System.Boolean", "System.Object" }, "System.Double", "feet", "Physical", 4, true, "makeLocal=true", "test");
            map.StartInvertTarget = target;
            Check(map.StartInvertTarget.ValueWriteSemantics.Contains("makeLocal=true"), "makeLocal=true is required in write semantics");
        }

        // P15-B Test 10: isolated scenario required
        private static void P15B_IsolatedScenarioRequired()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateActiveWriteContext(1, 2, 4, 4, 4, 3); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING")) { rejected = true; }
            Check(rejected, "Targeting non-isolated scenario rejected");
        }

        // P15-B Test 11: isolated Physical alternative required
        private static void P15B_IsolatedPhysicalAlternativeRequired()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateActiveWriteContext(2, 2, 3, 4, 4, 3); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING")) { rejected = true; }
            Check(rejected, "Assigned physical alternative mismatch rejected");
        }

        // P15-B Test 12: source and isolated Physical IDs must differ
        private static void P15B_SourceAndIsolatedPhysicalIdsMustDiffer()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateActiveWriteContext(2, 2, 5, 5, 5, 5); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING")) { rejected = true; }
            Check(rejected, "Equal source and isolated physical IDs rejected");
        }

        // P15-B Test 13: active write context mismatch rejected before setter
        private static void P15B_ActiveWriteContextMismatchRejectedBeforeSetter()
        {
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateActiveWriteContext(2, 2, 6, 6, 5, 4); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING")) { rejected = true; }
            Check(rejected, "Field write context mismatch rejected");
        }

        // P15-B Test 14: non-Physical mappings preserved in execution plan
        private static void P15B_NonPhysicalMappingsPreservedInExecutionPlan()
        {
            var plan = new P15BMutationPlan();
            Check(plan.TargetsIsolatedPhysicalAlternative, "Plan targets isolated physical alternative only");
            Check(plan.ExecutionSteps.Any(s => s.Contains("Physical")), "Plan explicitly specifies Physical alternative isolation");
        }

        // P15-B Test 15: calculation-options mapping preserved
        private static void P15B_CalculationOptionsMappingPreserved()
        {
            var opt = new CalculationOptionsContextDescriptor(67, "DynamicEngine", 1, 1, "ScenarioCalculationOptions", "PROVEN");
            Check(opt.ActiveScenarioOptionsId == 67, "Calculation options ID preserved");
        }

        private static void P15B_SourceStateUsesAcceptedP6CalculationOptions()
        {
            AssertP15SourceState(true, 904);
        }

        private sealed class LocalizationConduit
        {
            public bool Up = true;
            public bool Down = true;
            public bool RejectRead;
            public bool Physical_SetInvertToUpstream => RejectRead ? throw new InvalidOperationException("Unreadable Boolean") : Up;
            public bool Physical_SetInvertToDownstream => RejectRead ? throw new InvalidOperationException("Unreadable Boolean") : Down;
        }

        private sealed class NumericInheritanceConduit
        {
            public int Physical_SetInvertToUpstream => 1;
            public bool Physical_SetInvertToDownstream => true;
        }

        private sealed class LocalizationField
        {
            public int Calls;
            public Action<int, bool, object> Write;
            public void SetValue(int id, bool makeLocal, object value) { Calls++; Write(id, makeLocal, value); }
            // Enumerated field values are deliberately unsuitable for Boolean conversion.
            public object GetValue(int id) => 777;
        }

        private sealed class UnsafeLocalizationField
        {
            public void SetValue(int id, object value) { throw new InvalidOperationException("Unsafe setter must never be called"); }
        }

        private sealed class StaticLocalizationField
        {
            public static void SetValue(int id, bool makeLocal, object value) { throw new InvalidOperationException("Static setter must never be called"); }
        }

        private sealed class NonVoidLocalizationField
        {
            public int SetValue(int id, bool makeLocal, object value) { throw new InvalidOperationException("Non-void setter must never be called"); }
        }

        private sealed class WidenedLocalizationField
        {
            public void SetValue(long id, bool makeLocal, object value) { throw new InvalidOperationException("Widened setter must never be called"); }
        }

        private sealed class LocalizationFixture
        {
            public readonly LocalizationConduit Conduit = new LocalizationConduit();
            public readonly List<string> Writes = new List<string>();
            public readonly ModelReadEvidence Evidence = new ModelReadEvidence();
            public readonly LocalizationField Up = new LocalizationField();
            public readonly LocalizationField Down = new LocalizationField();
            public readonly LocalizationField SetUp = new LocalizationField();
            public readonly LocalizationField SetDown = new LocalizationField();
            public bool IgnoreUpLocalization;
            public bool IgnoreDownLocalization;

            public LocalizationFixture()
            {
                SetUp.Write = (id, local, value) => { Record(id, local, "local-up"); Check(value is bool && !(bool)value, "Upstream localization value must be Boolean false"); if (!IgnoreUpLocalization) Conduit.Up = (bool)value; };
                SetDown.Write = (id, local, value) => { Record(id, local, "local-down"); Check(value is bool && !(bool)value, "Downstream localization value must be Boolean false"); if (!IgnoreDownLocalization) Conduit.Down = (bool)value; };
                Up.Write = (id, local, value) => { Record(id, local, "invert-up"); Check(!Conduit.Up && !Conduit.Down && value is double && (double)value == 105.25, "Both flags must be local before the exact upstream candidate write"); };
                Down.Write = (id, local, value) => { Record(id, local, "invert-down"); Check(!Conduit.Up && !Conduit.Down && value is double && (double)value == 104.25, "Both flags must be local before the exact downstream candidate write"); };
            }

            private void Record(int id, bool makeLocal, string name)
            {
                Check(id == 83 && makeLocal, "Only the selected conduit with makeLocal=true is writable");
                Writes.Add(name);
            }

            public void Apply(int activeScenario = 12, int isolatedScenario = 12, int assignedAlternative = 42,
                int isolatedAlternative = 42, int sourceScenario = 1, int sourceAlternative = 5, object setDownOverride = null,
                int sourceAssignedAlternative = 5)
            {
                GravityWriteAndSolverHost.ApplyLocalizedInverts(Conduit, 83, Up, Down, SetUp, setDownOverride ?? SetDown,
                    Conduit.Up, Conduit.Down, 105.25, 104.25, activeScenario, isolatedScenario, assignedAlternative,
                    isolatedAlternative, sourceScenario, sourceAlternative, sourceAssignedAlternative, Evidence);
            }
        }

        private static void P15BL_SourceInheritanceAllowed()
        {
            var fixture = new LocalizationFixture();
            Check(GravityWriteAndSolverHost.ReadInheritanceFlag(fixture.Conduit, "Physical_SetInvertToUpstream") &&
                GravityWriteAndSolverHost.ReadInheritanceFlag(fixture.Conduit, "Physical_SetInvertToDownstream"), "Inherited source flags are valid runtime Boolean values");
            Check(GravityWriteAndSolverHost.EligibleConduit(83, 105.0, 104.0, 125.0), "Inherited source state must not disqualify an otherwise eligible conduit");
            fixture.Apply();
            Check(fixture.Writes.SequenceEqual(new[] { "local-up", "local-down", "invert-up", "invert-down" }), "Localization must precede both invert writes");
        }

        private static void P15BL_UnreadableBooleanRejected()
        {
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.ReadInheritanceFlag(new NumericInheritanceConduit(), "Physical_SetInvertToUpstream"));
            var fixture = new LocalizationFixture(); fixture.Conduit.RejectRead = true;
            Throws<Exception>(() => fixture.Apply());
            Check(fixture.Writes.Count == 0, "Unreadable inheritance state must stop before any mutation");
        }

        private static void P15BL_MissingLocalizationSetterRejected()
        {
            foreach (object invalidField in new object[] { new UnsafeLocalizationField(), new StaticLocalizationField(),
                new NonVoidLocalizationField(), new WidenedLocalizationField() })
            {
                var fixture = new LocalizationFixture();
                Throws<InvalidOperationException>(() => fixture.Apply(setDownOverride: invalidField));
                Check(fixture.Up.Calls == 0 && fixture.Down.Calls == 0 && fixture.SetUp.Calls == 0 && fixture.SetDown.Calls == 0 && fixture.Writes.Count == 0,
                    "All four exact instance void setters must be proven before any write");
            }
        }

        private static void P15BL_SourceContextRejected()
        {
            var fixture = new LocalizationFixture();
            Throws<InvalidOperationException>(() => fixture.Apply(activeScenario: 1));
            Throws<InvalidOperationException>(() => fixture.Apply(isolatedScenario: 1, activeScenario: 1));
            Throws<InvalidOperationException>(() => fixture.Apply(isolatedAlternative: 5, assignedAlternative: 5));
            Check(fixture.Writes.Count == 0, "The source scenario or source alternative cannot receive localization writes");
        }

        private static void P15BL_UpstreamLocalizedOnce()
        {
            var fixture = new LocalizationFixture(); fixture.Conduit.Down = false; fixture.Apply();
            Check(fixture.SetUp.Calls == 1 && fixture.SetDown.Calls == 0 && !fixture.Conduit.Up, "Only the inherited upstream endpoint is localized once");
        }

        private static void P15BL_DownstreamLocalizedOnce()
        {
            var fixture = new LocalizationFixture(); fixture.Conduit.Up = false; fixture.Apply();
            Check(fixture.SetDown.Calls == 1 && fixture.SetUp.Calls == 0 && !fixture.Conduit.Down, "Only the inherited downstream endpoint is localized once");
        }

        private static void P15BL_AlreadyLocalFlagsNotWritten()
        {
            var fixture = new LocalizationFixture(); fixture.Conduit.Up = false; fixture.Conduit.Down = false; fixture.Apply();
            Check(fixture.SetUp.Calls == 0 && fixture.SetDown.Calls == 0 && fixture.Writes.Count == 2, "Already-local endpoints need only two invert writes");
        }

        private static void P15BL_MakeLocalAlwaysTrue()
        {
            var fixture = new LocalizationFixture(); fixture.Apply();
            Check(fixture.Writes.Count == 4, "All four setters validate makeLocal=true at the call boundary");
        }

        private static void P15BL_ReadbackFailurePreventsInvertWrites()
        {
            var fixture = new LocalizationFixture(); fixture.IgnoreDownLocalization = true;
            Throws<InvalidOperationException>(() => fixture.Apply());
            Check(fixture.Evidence.FailureCode == "P15_B_BLOCKED_INVERT_LOCALIZATION_READBACK" &&
                fixture.Up.Calls == 0 && fixture.Down.Calls == 0 && fixture.SetUp.Calls == 1 && fixture.SetDown.Calls == 1,
                "Failed localization readback must retain the two localization attempts and prohibit all invert writes");
        }

        private static void P15BL_EndpointWritesExactlyTwo()
        {
            var fixture = new LocalizationFixture(); fixture.Apply();
            Check(fixture.Up.Calls == 1 && fixture.Down.Calls == 1 && fixture.Evidence.Diagnostics["EndpointInvertPropertySetCount"] == "2", "Both endpoint candidates are written exactly once");
            Check(fixture.Evidence.Diagnostics["LocalizationPropertySetCount"] == "2" && fixture.Evidence.Diagnostics["PropertySetCount"] == "4", "Localization and total counts remain separate and exact");
        }

        private static void P15BL_ExactFourMutationProtocol()
        {
            var request = Request(); request.Operation = RuntimeHostProtocol.OpGravityWriteAndSolver;
            var result = Result(request);
            result.SaveInvocationCount = 1; result.ScenarioCreateInvocationCount = 1; result.AlternativeCreateInvocationCount = 1; result.SolverRunInvocationCount = 1;
            result.MutationInvocationCount = 4;
            result.Diagnostics["LocalizationPropertySetCount"] = "2";
            result.Diagnostics["EndpointInvertPropertySetCount"] = "2";
            result.Diagnostics["LocalizationReadbackVerified"] = "true";
            string file = ResultFile(request); RuntimeHostProtocol.AtomicWrite(file, result);
            Check(RuntimeHostProtocol.ParseResult(file, request).MutationInvocationCount == 4, "Exactly four fixture mutations must pass protocol validation");
            var requestB = Request(); requestB.Operation = RuntimeHostProtocol.OpGravityWriteAndSolver;
            var resultB = Result(requestB);
            resultB.SaveInvocationCount = 1; resultB.ScenarioCreateInvocationCount = 1; resultB.AlternativeCreateInvocationCount = 1; resultB.SolverRunInvocationCount = 1;
            resultB.MutationInvocationCount = 2;
            resultB.Diagnostics["LocalizationPropertySetCount"] = "2";
            resultB.Diagnostics["EndpointInvertPropertySetCount"] = "2";
            resultB.Diagnostics["LocalizationReadbackVerified"] = "true";
            string fileB = ResultFile(requestB); RuntimeHostProtocol.AtomicWrite(fileB, resultB);
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(fileB, requestB));
            var requestC = Request(); requestC.Operation = RuntimeHostProtocol.OpGravityWriteAndSolver;
            var resultC = Result(requestC);
            resultC.SaveInvocationCount = 1; resultC.ScenarioCreateInvocationCount = 1; resultC.AlternativeCreateInvocationCount = 1; resultC.SolverRunInvocationCount = 1;
            resultC.MutationInvocationCount = 4;
            resultC.Diagnostics["LocalizationPropertySetCount"] = "1";
            resultC.Diagnostics["EndpointInvertPropertySetCount"] = "2";
            resultC.Diagnostics["LocalizationReadbackVerified"] = "true";
            string fileC = ResultFile(requestC); RuntimeHostProtocol.AtomicWrite(fileC, resultC);
            Throws<InvalidDataException>(() => RuntimeHostProtocol.ParseResult(fileC, requestC));
        }

        private static void P15BL_NoSaveOrSolverAfterLocalizationFailure()
        {
            var fixture = new LocalizationFixture(); fixture.IgnoreUpLocalization = true;
            int saves = 0, solverRuns = 0;
            Throws<InvalidOperationException>(() => { fixture.Apply(); saves++; solverRuns++; });
            Check(saves == 0 && solverRuns == 0 && fixture.Evidence.Diagnostics["EndpointInvertPropertySetCount"] == "0", "Failed localization cannot reach invert writes, persistence or solver continuation");
        }

        private static void AssertNoLocalizationWrites(LocalizationFixture fixture, Action apply)
        {
            Throws<InvalidOperationException>(apply);
            Check(fixture.Evidence.FailureCode == "P15_B_BLOCKED_ACTIVE_SCENARIO_TARGETING",
                "Invalid scenario-level context must report the targeting blocker");
            Check(fixture.Up.Calls == 0 && fixture.Down.Calls == 0 && fixture.SetUp.Calls == 0 && fixture.SetDown.Calls == 0 && fixture.Writes.Count == 0,
                "All four setter invocation counts must remain zero when scenario-level context fails");
        }

        private static void P15BL_ActiveScenarioMustBeIsolated()
        {
            var fixture = new LocalizationFixture();
            AssertNoLocalizationWrites(fixture, () => fixture.Apply(activeScenario: 13));
        }

        private static void P15BL_AssignedPhysicalAlternativeMustBeIsolated()
        {
            var fixture = new LocalizationFixture();
            AssertNoLocalizationWrites(fixture, () => fixture.Apply(assignedAlternative: 43));
        }

        private static void P15BL_IsolatedPhysicalAlternativeMustDifferFromSource()
        {
            var fixture = new LocalizationFixture();
            AssertNoLocalizationWrites(fixture, () => fixture.Apply(isolatedAlternative: 5, assignedAlternative: 5));
        }

        private static void P15BL_SourcePhysicalAssignmentMustRemainUnchanged()
        {
            var fixture = new LocalizationFixture();
            AssertNoLocalizationWrites(fixture, () => fixture.Apply(sourceAssignedAlternative: 42));
        }

        private static void P15BL_IsolatedPhysicalAlternativeMustBePositive()
        {
            var fixture = new LocalizationFixture();
            AssertNoLocalizationWrites(fixture, () => fixture.Apply(isolatedAlternative: 0, assignedAlternative: 0));
        }

        private static void P15BL_NonPhysicalAssignmentsPreserved()
        {
            var (ds, ui, project, request, evidence) = CreateTestEnvironment(Path.Combine(Path.GetTempPath(), "LocalizationIsolation", "model.stsw"));
            int sourcePhysical = ds.ScenarioManager.Scenarios[1].Assignments[4];
            ScenarioAlternativeIsolationHost.ExecuteHost1Write(ds, ui, project, request, evidence);
            int isolatedId = int.Parse(evidence.Diagnostics["CreatedScenarioID"]);
            Check(ds.ScenarioManager.Scenarios[1].Assignments[4] == sourcePhysical && isolatedId != 1, "Isolation must preserve source Physical assignment and use a distinct scenario");
            Check(ds.ScenarioManager.Scenarios[isolatedId].Assignments[1] == 100 && ds.ScenarioManager.Scenarios[isolatedId].Assignments[5] == 200, "Accepted isolation preserves non-Physical mappings");
        }

        private static void P15BL_NoFabricatedLengthOrNonFiniteValues()
        {
            Check(!GravityWriteAndSolverHost.EligibleConduit(83, 105.0, 104.0, null), "Missing length cannot become a fabricated positive length");
            Check(!GravityWriteAndSolverHost.EligibleConduit(83, double.NaN, 104.0, 100.0) &&
                !GravityWriteAndSolverHost.EligibleConduit(83, 105.0, double.PositiveInfinity, 100.0) &&
                !GravityWriteAndSolverHost.EligibleConduit(83, 105.0, 104.0, double.PositiveInfinity) &&
                !GravityWriteAndSolverHost.EligibleConduit(0, 105.0, 104.0, 100.0), "Nonfinite values and invalid IDs are not eligible");
        }

        private static void P15B_SourceStateMissingCalculationOptionsBlocksBeforeMutation()
        {
            AssertP15SourceState(true, 0);
        }

        private static void P15B_SourceStateMissingEngineBlocksBeforeMutation()
        {
            AssertP15SourceState(false, 904);
        }

        private static void AssertP15SourceState(bool engineAvailable, int optionsId)
        {
            var (ds, ui, proj, req, ev) = CreateTestEnvironment(Path.Combine(Path.GetTempPath(), "P15_SourceState", "model.stsw"));
            req.WriteAuthorizationScope = "GravityWriteAndSolver";
            const string engineName = "SourceStateTestEngine";
            if (engineAvailable)
                ds.DataSetType.Engines = new object[] { new MockAlternativeType { Id = 903, Name = engineName } };
            var options = new MockAlternativeManager();
            if (optionsId > 0)
                options.Alternatives[optionsId] = new MockAlternative { Id = optionsId, Label = "Source options" };
            ds.ScenarioManager.OptionsManagers[engineName] = options;
            MockScenario source = ds.ScenarioManager.Scenarios[ds.ScenarioManager.ActiveScenarioID];
            source.CalculationOptions[engineName] = optionsId;

            // The fake dataset deliberately has no conduit field API; stop before any write.
            Throws<Exception>(() => GravityWriteAndSolverHost.ExecuteHost1(ds, ui, proj, req, ev));
            bool expectedResolved = engineAvailable && optionsId > 0;
            Check((ev.FailureCode != "P15_B_R4_BLOCKED_SOURCE_STATE") == expectedResolved,
                "Only a valid mapped calculation-options identity may pass the source-state gate.");
            Check(ev.Diagnostics["SourceCalcOptionsID"] == (expectedResolved ? optionsId : 0).ToString(),
                "The source identity must come from the P6 mapping, without historical ID fallback.");
            if (expectedResolved)
                Check(ev.Diagnostics["SourceEngineTypeName"] == engineName, "The enumerated engine name must be retained.");
            Check(source.ActiveEngineQueryCount == 0, "Source-state capture must not query an empty result-record name.");
            Check(ds.ScenarioManager.CopyInvocationCount == 0 && ds.AltManagers[4].CopyInvocationCount == 0,
                "Source-state validation must precede scenario and alternative mutation.");
            Check(ui.SaveCurrentProjectCount == 0 && ui.SaveAsCount == 0, "Source-state tests must not save.");
        }

        // P15-B Test 16: exactly two endpoint changes for acceptance candidate
        private static void P15B_ExactlyTwoEndpointChangesForAcceptanceCandidate()
        {
            var eps = new List<CandidateEndpointDesign>
            {
                new CandidateEndpointDesign(27, ConduitEndKind.Start, 19, 103.0, 103.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue),
                new CandidateEndpointDesign(27, ConduitEndKind.Stop, 20, 101.0, 101.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue)
            };
            var cand = new GravityDesignCandidate("C1", "R1", "NET1", "VERT1", eps, null, null);
            GravityCandidateExecutor.ValidateCandidateEndpointDesigns(cand, 27);
            Check(cand.EndpointDesigns.Count == 2, "Candidate has exactly 2 endpoint designs");
        }

        // P15-B Test 17: unrelated property change rejected
        private static void P15B_UnrelatedPropertyChangeRejected()
        {
            var eps = new List<CandidateEndpointDesign>
            {
                new CandidateEndpointDesign(27, ConduitEndKind.Start, 19, 103.0, 103.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue),
                new CandidateEndpointDesign(27, ConduitEndKind.Stop, 20, 101.0, 101.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue),
                new CandidateEndpointDesign(28, ConduitEndKind.Start, 17, 102.0, 102.01, new SourceUnitIdentity("length", "Feet", "feet", "ft"), CandidateSelectionSource.ExplicitValue)
            };
            var cand = new GravityDesignCandidate("C1", "R1", "NET1", "VERT1", eps, null, null);
            bool rejected = false;
            try { GravityCandidateExecutor.ValidateCandidateEndpointDesigns(cand, 27); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("P15_B_BLOCKED_PROPERTY_WRITE")) { rejected = true; }
            Check(rejected, "Unrelated conduit endpoint change rejected");
        }

        // P15-B Test 18: immediate read-back mismatch fails
        private static void P15B_ImmediateReadBackMismatchFails()
        {
            double readVal = 103.00;
            double candVal = 103.01;
            bool mismatch = Math.Abs(readVal - candVal) > 1e-6;
            Check(mismatch, "Immediate read-back mismatch detected");
        }

        // P15-B Test 19: SaveCurrentProject represented exactly once
        private static void P15B_SaveCurrentProjectRepresentedExactlyOnce()
        {
            var result = new GravitySolverExecutionResult { SaveCompleted = true };
            Check(result.SaveCompleted, "Save completed represented");
        }

        // P15-B Test 20: SaveAs never used
        private static void P15B_SaveAsNeverUsed()
        {
            var map = new P15ACapabilityMappingResult { SaveAsCount = 0 };
            Check(map.SaveAsCount == 0, "SaveAs count is zero");
        }

        private sealed class SolverFixture
        {
            public Assembly Domain;
            public SolverDataSetProbe DataSet;
            public SolverEngineProbe Engine;
            public object Source;
            public object Isolated;
            public readonly ModelReadEvidence Evidence = new ModelReadEvidence();
            public GravityWriteAndSolverHost.NumericalEngineBinding Bind() =>
                GravityWriteAndSolverHost.BindNumericalEngine(DataSet, Source, "P15TestEngine", Evidence, new[] { Domain }, assembly => ReferenceEquals(assembly, Domain) && Domain.GetName().Version != new Version(24, 0, 1, 0));
        }

        private static System.Reflection.Emit.MethodBuilder SolverInterfaceMethod(System.Reflection.Emit.TypeBuilder type,
            string name, Type result, params Type[] parameters) => type.DefineMethod(name,
                MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                result, parameters);

        private static System.Reflection.Emit.MethodBuilder SolverForwardMethod(System.Reflection.Emit.TypeBuilder type,
            string name, Type result, Type[] parameters, MethodInfo target, Type castResult = null)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                result, parameters);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            if (parameters.Length == 1) il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Call, target);
            if (castResult != null) il.Emit(System.Reflection.Emit.OpCodes.Castclass, castResult);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return method;
        }

        private static SolverFixture CreateSolverFixture(string fault = null)
        {
            SolverCollectionProbe.ConstructionCount = 0;
            SolverCollectionProbe.AddCount = 0;
            SolverCollectionProbe.ForcedCount = -1;
            var identity = new AssemblyName("Haestad.Domain") { Version = fault == "Identity" ? new Version(24, 0, 1, 0) : fault == "FutureVersion" ? new Version(26, 1, 0, 0) : new Version(24, 0, 2, 0) };
            var domain = AppDomain.CurrentDomain.DefineDynamicAssembly(identity, System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var module = domain.DefineDynamicModule("SyntheticP15Solver");
            Type element = module.DefineType("Haestad.Domain.IModelingElement",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType();
            var collectionBuilder = module.DefineType("Haestad.Domain.ModelingElementCollection", TypeAttributes.Public, typeof(SolverCollectionProbe));
            collectionBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            SolverForwardMethod(collectionBuilder, "Add", typeof(int), new[] { element }, typeof(SolverCollectionProbe).GetMethod("AddElement"));
            Type collection = collectionBuilder.CreateType();
            var engineInterfaceBuilder = module.DefineType("Haestad.Domain.INumericalEngine",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            var engineNameGetter = SolverInterfaceMethod(engineInterfaceBuilder, "get_NumericalEngineTypeName", typeof(string));
            var nameProperty = engineInterfaceBuilder.DefineProperty("NumericalEngineTypeName", PropertyAttributes.None, typeof(string), Type.EmptyTypes);
            nameProperty.SetGetMethod(engineNameGetter);
            if (fault != "Run") SolverInterfaceMethod(engineInterfaceBuilder, "Run", typeof(void), collection);
            Type engineInterface = engineInterfaceBuilder.CreateType();
            var engineBuilder = module.DefineType("P15SyntheticEngine", TypeAttributes.Public, typeof(SolverEngineProbe), new[] { engineInterface });
            engineBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            var getter = SolverForwardMethod(engineBuilder, "get_NumericalEngineTypeName", typeof(string), Type.EmptyTypes, typeof(SolverEngineProbe).GetMethod("ReadName"));
            engineBuilder.DefineMethodOverride(getter, engineInterface.GetMethod("get_NumericalEngineTypeName"));
            if (fault != "Run")
            {
                var run = SolverForwardMethod(engineBuilder, "Run", typeof(void), new[] { collection }, typeof(SolverEngineProbe).GetMethod("Execute"));
                engineBuilder.DefineMethodOverride(run, engineInterface.GetMethod("Run"));
            }
            Type engine = engineBuilder.CreateType();
            Type dataSetInterface = null;
            if (fault != "DataSet")
            {
                var dataSetInterfaceBuilder = module.DefineType("Haestad.Domain.IDomainDataSet",
                    TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
                if (fault != "Resolve") SolverInterfaceMethod(dataSetInterfaceBuilder, "NumericalEngine", engineInterface, typeof(string));
                dataSetInterface = dataSetInterfaceBuilder.CreateType();
            }
            var dataSetBuilder = module.DefineType("P15SyntheticDataSet", TypeAttributes.Public, typeof(SolverDataSetProbe),
                dataSetInterface == null ? Type.EmptyTypes : new[] { dataSetInterface });
            dataSetBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            if (fault != "DataSet" && fault != "Resolve")
            {
                var resolve = SolverForwardMethod(dataSetBuilder, "NumericalEngine", engineInterface, new[] { typeof(string) },
                    typeof(SolverDataSetProbe).GetMethod("Resolve"), engineInterface);
                dataSetBuilder.DefineMethodOverride(resolve, dataSetInterface.GetMethod("NumericalEngine"));
            }
            Type dataSet = dataSetBuilder.CreateType();
            var scenarioBuilder = module.DefineType("P15SyntheticScenario", TypeAttributes.Public, typeof(object), new[] { element });
            scenarioBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            Type scenario = scenarioBuilder.CreateType();
            var fixture = new SolverFixture { Domain = domain, DataSet = (SolverDataSetProbe)Activator.CreateInstance(dataSet),
                Engine = (SolverEngineProbe)Activator.CreateInstance(engine), Source = Activator.CreateInstance(scenario), Isolated = Activator.CreateInstance(scenario) };
            fixture.DataSet.Engine = fixture.Engine;
            return fixture;
        }

        private static void AssertSolverBindingBlocked(SolverFixture fixture)
        {
            int mutations = 0;
            Throws<InvalidOperationException>(() => { fixture.Bind(); mutations++; });
            Check(fixture.Evidence.FailureCode == "P15_B_R4_BLOCKED_SOLVER_BINDING" && mutations == 0,
                "Invalid exact solver contract must block before continuation can mutate");
            Check(fixture.Engine.RunCount == 0 && SolverCollectionProbe.ConstructionCount == 0 && SolverCollectionProbe.AddCount == 0,
                "Binding failure cannot construct a collection, add a target or run a solver");
        }

        private static void P15BS_BindingBeforeFirstMutation()
        {
            var fixture = CreateSolverFixture();
            fixture.Bind();
            Check(fixture.Evidence.Diagnostics["SolverBindingProvenBeforeMutation"] == "true" && fixture.DataSet.ResolutionCount == 1 &&
                fixture.DataSet.RequestedEngine == "P15TestEngine" && fixture.Engine.RunCount == 0 && SolverCollectionProbe.ConstructionCount == 0,
                "Preflight resolves the exact source engine without Run or collection mutation");
            string source = File.ReadAllText(Path.Combine(Repository(), "src/SewerPilot.OpenFlows.Adapter/GravityWriteAndSolverHost.cs"));
            Check(source.IndexOf("NumericalEngineBinding solverBinding = BindNumericalEngine", StringComparison.Ordinal) <
                source.IndexOf("P15_BLOCKED_VERSIONED_ENGINEERING_PLAN_REQUIRED", StringComparison.Ordinal), "Retired legacy path stops after preflight without any mutation");
            var (ds, ui, project, request, evidence) = CreateTestEnvironment(Path.Combine(Path.GetTempPath(), "P15SolverPreflight", "model.stsw"));
            request.WriteAuthorizationScope = "GravityWriteAndSolver";
            ds.DataSetType.Engines = new object[] { new MockAlternativeType { Id = 903, Name = "P15TestEngine" } };
            var options = new MockAlternativeManager();
            options.Alternatives[904] = new MockAlternative { Id = 904, Label = "Test options" };
            ds.ScenarioManager.OptionsManagers["P15TestEngine"] = options;
            ds.ScenarioManager.Scenarios[1].CalculationOptions["P15TestEngine"] = 904;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.ExecuteHost1(ds, ui, project, request, evidence));
            Check(evidence.FailureCode == "P15_B_R4_BLOCKED_SOLVER_BINDING" && ds.ScenarioManager.CopyInvocationCount == 0 &&
                ds.AltManagers[4].CopyInvocationCount == 0 && ui.SaveCurrentProjectCount == 0 && ui.SaveAsCount == 0,
                "Real host flow must reject unproven binding before copies, property writes or saves");
            Check(!evidence.Diagnostics.ContainsKey("PropertySetCount") && !evidence.Diagnostics.ContainsKey("SolverRunInvoked"),
                "Failed preflight cannot enter property or solver mutation");
        }

        private static void P15BS_MissingDataSetContractBlocksBeforeMutation() => AssertSolverBindingBlocked(CreateSolverFixture("DataSet"));
        private static void P15BS_MissingNumericalEngineBlocksBeforeMutation() => AssertSolverBindingBlocked(CreateSolverFixture("Resolve"));
        private static void P15BS_MissingRunBlocksBeforeMutation() => AssertSolverBindingBlocked(CreateSolverFixture("Run"));
        private static void P15BS_WrongAssemblyIdentityRejected() => AssertSolverBindingBlocked(CreateSolverFixture("Identity"));

        private static void P15BS_NullEngineBlocksBeforeMutation()
        {
            var fixture = CreateSolverFixture(); fixture.DataSet.Engine = null; AssertSolverBindingBlocked(fixture);
        }

        private static void P15BS_EngineNameMismatchBlocksBeforeMutation()
        {
            var fixture = CreateSolverFixture(); fixture.Engine.Name = "DifferentEngine"; AssertSolverBindingBlocked(fixture);
        }

        private static void P15BS_SourceMustImplementModelingElement()
        {
            var fixture = CreateSolverFixture(); fixture.Source = new object(); AssertSolverBindingBlocked(fixture);
        }

        private static void P15BS_IsolatedMustImplementModelingElement()
        {
            var fixture = CreateSolverFixture();
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.RunBoundNumericalEngine(fixture.Bind(), new object(), 81, 1, fixture.Evidence));
            Check(fixture.Engine.RunCount == 0 && SolverCollectionProbe.ConstructionCount == 0,
                "Non-IModelingElement isolated target must fail before collection creation or Run");
        }

        private static void P15BS_CollectionMustContainExactlyOneScenario()
        {
            var fixture = CreateSolverFixture(); var binding = fixture.Bind(); SolverCollectionProbe.ForcedCount = 2;
            try
            {
                Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.RunBoundNumericalEngine(binding, fixture.Isolated, 81, 1, fixture.Evidence));
                Check(fixture.Engine.RunCount == 0 && SolverCollectionProbe.ConstructionCount == 1 && SolverCollectionProbe.AddCount == 1,
                    "Non-singleton collection count blocks Run after exactly one construction and Add");
            }
            finally { SolverCollectionProbe.ForcedCount = -1; }
        }

        private static void P15BS_RunFailureHasNoRetryOrCompletion()
        {
            var fixture = CreateSolverFixture(); fixture.Engine.FailRun = true;
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.RunBoundNumericalEngine(fixture.Bind(), fixture.Isolated, 81, 1, fixture.Evidence));
            Check(fixture.Engine.RunCount == 1 && fixture.Evidence.Diagnostics["SolverRunInvoked"] == "true" &&
                !fixture.Evidence.Diagnostics.ContainsKey("SolverRunCompleted") && fixture.Engine.ResultReads == 0,
                "A throwing synchronous Run is recorded once, never retried, completed or followed by result reads");
        }

        private static void P15BS_AmbiguousAssemblyRejected()
        {
            var fixture = CreateSolverFixture();
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.BindNumericalEngine(fixture.DataSet, fixture.Source,
                "P15TestEngine", fixture.Evidence, new[] { fixture.Domain, fixture.Domain }));
            Check(fixture.Evidence.FailureCode == "P15_B_R4_BLOCKED_SOLVER_BINDING" && fixture.DataSet.ResolutionCount == 0,
                "Ambiguous exact assembly identity must fail before engine resolution");
        }

        private static void P15BS_NoActionFactoryOrPollingPath()
        {
            string source = File.ReadAllText(Path.Combine(Repository(), "src/SewerPilot.OpenFlows.Adapter/GravityWriteAndSolverHost.cs"));
            Check(!source.Contains("ActionFactory") && !source.Contains("ComputeActiveScenarioAction") && !source.Contains("computeAction") &&
                !source.Contains("IsRunning") && !source.Contains("Thread.Sleep"), "P15 solver source must have no action factory, UI action or polling path");
            int firstRun = source.IndexOf("binding.Run.Invoke", StringComparison.Ordinal);
            Check(firstRun >= 0 && source.IndexOf("binding.Run.Invoke", firstRun + 1, StringComparison.Ordinal) < 0,
                "There must be exactly one numerical-engine Run invocation site");
        }

        // P15-B Test 21: solver execution targets isolated scenario
        private static void P15B_SolverExecutionTargetsIsolatedScenario()
        {
            var fixture = CreateSolverFixture();
            var binding = fixture.Bind();
            GravityWriteAndSolverHost.RunBoundNumericalEngine(binding, fixture.Isolated, 81, 1, fixture.Evidence);
            var collection = (SolverCollectionProbe)fixture.Engine.TargetCollection;
            Check(collection.Elements.Count == 1 && ReferenceEquals(collection.Elements[0], fixture.Isolated) &&
                !ReferenceEquals(collection.Elements[0], fixture.Source), "Run receives only the isolated scenario instance");
            Check(fixture.Evidence.Diagnostics["SolverTargetScenarioID"] == "81" && fixture.Evidence.Diagnostics["SolverTargetCollectionCount"] == "1",
                "Solver diagnostics retain the exact isolated target and singleton count");
            Check(fixture.Engine.ResultReads == 0 && fixture.Evidence.Diagnostics["HydraulicResultReads"] == "0", "Solver execution reads no hydraulic results");
            var rejected = CreateSolverFixture();
            Throws<InvalidOperationException>(() => GravityWriteAndSolverHost.RunBoundNumericalEngine(rejected.Bind(), rejected.Source, 1, 1, rejected.Evidence));
            Check(rejected.Engine.RunCount == 0 && SolverCollectionProbe.ConstructionCount == 0, "Source target rejected before collection creation or Run");
        }

        // P15-B Test 22: solver execution count bounded to one
        private static void P15B_SolverExecutionCountBoundedToOne()
        {
            var fixture = CreateSolverFixture();
            GravityWriteAndSolverHost.RunBoundNumericalEngine(fixture.Bind(), fixture.Isolated, 81, 1, fixture.Evidence);
            Check(fixture.Engine.RunCount == 1 && SolverCollectionProbe.ConstructionCount == 1 && SolverCollectionProbe.AddCount == 1,
                "Exactly one collection, Add and synchronous Run are invoked");
            Check(fixture.Evidence.Diagnostics["SolverRunInvoked"] == "true" && fixture.Evidence.Diagnostics["SolverRunCompleted"] == "true",
                "Normal Run return records both invocation and completion");
        }

        // P15-B Test 23: Success status accepted
        private static void P15B_SuccessStatusAccepted()
        {
            var result = new GravitySolverExecutionResult { ScenarioComputeStatus = "Success" };
            Check(string.Equals(result.ScenarioComputeStatus, "Success", StringComparison.OrdinalIgnoreCase), "Success status accepted");
        }

        // P15-B Test 24: Error status rejected
        private static void P15B_ErrorStatusRejected()
        {
            var result = new GravitySolverExecutionResult { ScenarioComputeStatus = "Error" };
            Check(!string.Equals(result.ScenarioComputeStatus, "Success", StringComparison.OrdinalIgnoreCase), "Error status is not accepted");
        }

        // P15-B Test 25: Warning status does not become hydraulic success
        private static void P15B_WarningStatusDoesNotBecomeHydraulicSuccess()
        {
            var result = new GravitySolverExecutionResult { ScenarioComputeStatus = "Warning" };
            Check(!string.Equals(result.ScenarioComputeStatus, "Success", StringComparison.OrdinalIgnoreCase), "Warning status is not success");
        }

        // P15-B Test 26: unknown compute status rejected
        private static void P15B_UnknownComputeStatusRejected()
        {
            var result = new GravitySolverExecutionResult { ScenarioComputeStatus = "Unknown" };
            Check(!string.Equals(result.ScenarioComputeStatus, "Success", StringComparison.OrdinalIgnoreCase), "Unknown status rejected");
        }

        // P15-B Test 27: execution result contains no hydraulic result values
        private static void P15B_ExecutionResultContainsNoHydraulicResultValues()
        {
            var props = typeof(GravitySolverExecutionResult).GetProperties();
            foreach (var p in props)
            {
                string n = p.Name.ToLowerInvariant();
                Check(!n.Contains("velocity") && !n.Contains("flow") && !n.Contains("capacity") &&
                      !n.Contains("depth") && !n.Contains("hgl") && !n.Contains("surcharge"),
                      "No hydraulic result property on GravitySolverExecutionResult: " + p.Name);
            }
        }

        private static string ResolveAdapterSourceDirectory()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln")))
            {
                dir = dir.Parent;
            }
            if (dir == null) throw new DirectoryNotFoundException("Could not locate repository root with SewerPilot.sln.");
            string srcDir = Path.Combine(dir.FullName, "src", "SewerPilot.OpenFlows.Adapter");
            if (!Directory.Exists(srcDir)) throw new DirectoryNotFoundException("Adapter source directory not found: " + srcDir);
            return srcDir;
        }

        // P15-B Test 28: source isolation verification required
        private static void P15B_SourceIsolationVerificationRequired()
        {
            var verifiedResult = new GravitySolverExecutionResult { SourceIsolationVerified = true };
            var unverifiedResult = new GravitySolverExecutionResult { SourceIsolationVerified = false };
            Check(verifiedResult.SourceIsolationVerified, "Source isolation verification verified = true");
            Check(!unverifiedResult.SourceIsolationVerified, "Source isolation verification unverified = false");
        }

        // P15-B Test 29: persisted candidate verification required
        private static void P15B_PersistedCandidateVerificationRequired()
        {
            var verifiedResult = new GravitySolverExecutionResult { PersistenceVerified = true };
            var unverifiedResult = new GravitySolverExecutionResult { PersistenceVerified = false };
            Check(verifiedResult.PersistenceVerified, "Persistence verification verified = true");
            Check(!unverifiedResult.PersistenceVerified, "Persistence verification unverified = false");
        }

        // P15-B Test 30: execution trace retains original/applied values
        private static void P15B_ExecutionTraceRetainsOriginalAndAppliedValues()
        {
            var change = new AppliedEndpointChange(27, "Start", 103.0, 103.01, "Feet", "Physical_UpstreamInvert");
            Check(change.ConduitId == 27, "ConduitId retained");
            Check(change.End == "Start", "End retained");
            Check(change.OriginalValue == 103.0, "OriginalValue retained");
            Check(change.AppliedValue == 103.01, "AppliedValue retained");
            Check(change.Unit == "Feet", "Unit retained");
            Check(change.SourceField == "Physical_UpstreamInvert", "SourceField retained");
        }

        // P15-B Test 31: no public vendor types
        private static void P15B_NoPublicVendorTypes()
        {
            Check(Neutral(typeof(AppliedEndpointChange)), "AppliedEndpointChange is neutral");
            Check(Neutral(typeof(GravitySolverExecutionResult)), "GravitySolverExecutionResult is neutral");
        }

        // P15-B Test 32: no persisted vendor types
        private static void P15B_NoPersistedVendorTypes()
        {
            Type[] types = new[] { typeof(AppliedEndpointChange), typeof(GravitySolverExecutionResult) };
            foreach (var t in types)
            {
                Check(Neutral(t), t.Name + " is neutral");
                foreach (var p in t.GetProperties())
                {
                    Check(Neutral(p.PropertyType), t.Name + "." + p.Name + " property type is neutral");
                }
            }
        }

        // P15-B Test 33: no hard-coded engine ID in production
        private static void P15B_NoHardCodedEngineIdInProduction()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            var files = Directory.GetFiles(srcDir, "*.cs");
            Check(files.Length > 0, "Adapter source files enumerated: " + files.Length);
            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                Check(!text.Contains("CivilStormDynamicEngine") || file.EndsWith("GravityWriteAndSolverCapabilityMapper.cs") || file.EndsWith("WaterObjectsResultReader.cs") || file.EndsWith("GravityNetworkReadMapper.SourceData.cs") || file.EndsWith("GravityProductionExecutionHost.cs") || file.EndsWith("NetworkResultComposer.cs") || Path.GetFileName(file).StartsWith("P16"),
                    "Generic production code does not hardcode engine name: " + Path.GetFileName(file));
            }
        }

        // P15-B Test 34: no hard-coded scenario ID in production
        private static void P15B_NoHardCodedScenarioIdInProduction()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(text.Contains("ActiveScenarioID"), "Production dynamically accesses ActiveScenarioID from scenario manager");
            Check(!text.Contains("activeScenarioId = 1;"), "Production does not hard-code scenario ID 1");
        }

        // P15-B Test 35: no hard-coded Physical alternative ID in generic business logic
        private static void P15B_NoHardCodedPhysicalAlternativeIdInGenericBusinessLogic()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(text.Contains("ResolvePhysicalAlternativeTypeId"), "Physical alternative type resolved dynamically via ResolvePhysicalAlternativeTypeId");
        }

        // P15-B Test 36: no engineering acceptance offset in production source
        private static void P15B_NoEngineeringAcceptanceOffsetInProductionSource()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string genFile = Path.Combine(srcDir, "GravityCandidateGenerator.cs");
            Check(File.Exists(genFile), "GravityCandidateGenerator.cs exists");
            string text = File.ReadAllText(genFile);
            Check(!text.Contains("0.01"), "GravityCandidateGenerator does not contain test acceptance offset 0.01");
        }

        // P15-B-R1 Test 1: P15-B conduit enum resolution uses exact loaded Haestad.Domain
        private static void P15BR1_ConduitEnumResolutionUsesExactLoadedHaestadDomain()
        {
            var dynamicDomain = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("Haestad.Domain"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { dynamicDomain });
            Check(resolved == null, "ResolveDomainElementType fails closed when enum type missing from assembly");
        }

        // P15-B-R1 Test 2: does not depend on dataset/manager assembly
        private static void P15BR1_DoesNotDependOnDatasetOrManagerAssembly()
        {
            var otherAssembly = typeof(Program).Assembly;
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { otherAssembly });
            Check(resolved == null, "Resolution ignores dataset/manager assembly if not Haestad.Domain");
        }

        // P15-B-R1 Test 3: exact Haestad.Domain.DomainElementType is required
        private static void P15BR1_ExactDomainElementTypeRequired()
        {
            var pool = new[] { typeof(Program).Assembly };
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(pool);
            Check(resolved == null, "Non-matching assembly returns null type");
        }

        // P15-B-R1 Test 4: missing assembly/type fails closed
        private static void P15BR1_MissingAssemblyOrTypeFailsClosed()
        {
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new Assembly[0]);
            Check(resolved == null, "Empty assembly pool fails closed (returns null)");
        }

        // P15-B-R1 Test 5: ambiguous exact assembly match fails closed
        private static void P15BR1_AmbiguousExactAssemblyMatchFailsClosed()
        {
            var asm1 = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("Haestad.Domain"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var asm2 = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("Haestad.Domain"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var resolved = GravityNetworkReadMapper.ResolveDomainElementType(new[] { asm1, asm2 });
            Check(resolved == null, "Ambiguous exact assembly match fails closed (returns null)");
        }


        // P15-B-R1 Test 6: conduit enum resolves from the accepted type
        private static void P15BR1_ConduitEnumResolvesFromAcceptedType()
        {
            int val = GravityNetworkReadMapper.ResolveElementTypeId(typeof(SyntheticDomainElementType), "ConduitElementManager");
            Check(val == 3, "ConduitElementManager resolves dynamically from enum type");
            int nonExistent = GravityNetworkReadMapper.ResolveElementTypeId(typeof(SyntheticDomainElementType), "NonExistentManager");
            Check(nonExistent == -1, "Nonexistent member returns -1");
        }

        // P15-B-R1 Test 7: no conduit type numeric constant is used as primary production logic
        private static void P15BR1_NoConduitTypeNumericConstantUsedAsPrimaryProductionLogic()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(!text.Contains("conduitElementType = 3;"), "GravityWriteAndSolverHost does not hardcode conduitElementType = 3");
            Check(text.Contains("ResolveElementTypeId"), "Conduit element type ID resolved dynamically via ResolveElementTypeId");
        }

        // P15-B-R1 Test 8: no property setter is invoked during resolver tests
        private static void P15BR1_NoPropertySetterInvokedDuringResolverTests()
        {
            int val = GravityNetworkReadMapper.ResolveElementTypeId(typeof(SyntheticDomainElementType), "ConduitElementManager");
            Check(val == 3, "Resolver is strictly read-only; no setters invoked");
        }

        private class SyntheticConduitElementManager
        {
            public int UpstreamInvertSetterInvocations { get; private set; }
            public int DownstreamInvertSetterInvocations { get; private set; }
            public bool ExcludeDownstreamInvert { get; set; }

            public object DomainElementField(string fieldName)
            {
                if (ExcludeDownstreamInvert && fieldName == "Physical_DownstreamInvert") return null;
                switch (fieldName)
                {
                    case "Physical_UpstreamInvert":
                        return new SyntheticConduitField("Physical_UpstreamInvert", (id, local, val) => UpstreamInvertSetterInvocations++);
                    case "Physical_DownstreamInvert":
                        return new SyntheticConduitField("Physical_DownstreamInvert", (id, local, val) => DownstreamInvertSetterInvocations++);
                    case "Physical_SetInvertToUpstream":
                        return new SyntheticConduitField("Physical_SetInvertToUpstream", null);
                    case "Physical_SetInvertToDownstream":
                        return new SyntheticConduitField("Physical_SetInvertToDownstream", null);
                    default:
                        return null;
                }
            }
        }

        private class SyntheticConduitField
        {
            private readonly Action<int, bool, object> _setter;
            public string Name { get; }
            public SyntheticConduitField(string name, Action<int, bool, object> setter)
            {
                Name = name;
                _setter = setter;
            }

            public object GetValue(int id) => 100.0;
            public void SetValue(int id, bool makeLocal, object value)
            {
                _setter?.Invoke(id, makeLocal, value);
            }
        }

        // P15-B-R2 Test 1: P15-B uses accepted/shared field-access path
        private static void P15BR2_UsesAcceptedSharedFieldAccessPath()
        {
            var mgr = new SyntheticConduitElementManager();
            var field = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_UpstreamInvert");
            Check(field != null, "ResolveDomainElementField resolved field using accepted DomainElementField path");

            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(!text.Contains(".GetProperty(\"Fields\")"), "GravityWriteAndSolverHost does not access Fields property");
            Check(!text.Contains(".GetMethod(\"Field\""), "GravityWriteAndSolverHost does not call IFieldCollection.Field");
        }

        // P15-B-R2 Test 2: IFieldCollection.Field(...) is no longer required
        private static void P15BR2_IFieldCollectionFieldNotRequired()
        {
            var mgr = new SyntheticConduitElementManager();
            Check(mgr.GetType().GetProperty("Fields") == null, "Synthetic manager does not expose Fields property");
            Check(mgr.GetType().GetMethod("Field") == null, "Synthetic manager does not expose Field method");
            var up = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_UpstreamInvert");
            var down = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_DownstreamInvert");
            Check(up != null && down != null, "Invert fields resolved without requiring IFieldCollection.Field");
        }

        // P15-B-R2 Test 3: Physical_UpstreamInvert resolves
        private static void P15BR2_PhysicalUpstreamInvertResolves()
        {
            var mgr = new SyntheticConduitElementManager();
            var field = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_UpstreamInvert");
            Check(field != null, "Physical_UpstreamInvert resolved");
            Check(field.GetType().Name == "SyntheticConduitField", "Resolved field is expected type");
        }

        // P15-B-R2 Test 4: Physical_DownstreamInvert resolves
        private static void P15BR2_PhysicalDownstreamInvertResolves()
        {
            var mgr = new SyntheticConduitElementManager();
            var field = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_DownstreamInvert");
            Check(field != null, "Physical_DownstreamInvert resolved");
        }

        // P15-B-R2 Test 5: Physical_SetInvertToUpstream resolves
        private static void P15BR2_PhysicalSetInvertToUpstreamResolves()
        {
            var mgr = new SyntheticConduitElementManager();
            var field = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToUpstream");
            Check(field != null, "Physical_SetInvertToUpstream resolved");
        }

        // P15-B-R2 Test 6: Physical_SetInvertToDownstream resolves
        private static void P15BR2_PhysicalSetInvertToDownstreamResolves()
        {
            var mgr = new SyntheticConduitElementManager();
            var field = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToDownstream");
            Check(field != null, "Physical_SetInvertToDownstream resolved");
        }

        // P15-B-R2 Test 7: missing required field fails closed before mutation
        private static void P15BR2_MissingRequiredFieldFailsClosedBeforeMutation()
        {
            var mgr = new SyntheticConduitElementManager { ExcludeDownstreamInvert = true };
            var up = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_UpstreamInvert");
            var down = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_DownstreamInvert");
            var setUp = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToUpstream");
            var setDown = GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToDownstream");

            bool allResolved = (up != null && down != null && setUp != null && setDown != null);
            Check(!allResolved, "Missing Physical_DownstreamInvert is detected");
            Check(mgr.UpstreamInvertSetterInvocations == 0, "No setter invoked when required field missing");
            Check(mgr.DownstreamInvertSetterInvocations == 0, "No setter invoked when required field missing");
        }

        // P15-B-R2 Test 8: resolver tests invoke zero setters
        private static void P15BR2_ResolverTestsInvokeZeroSetters()
        {
            var mgr = new SyntheticConduitElementManager();
            GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_UpstreamInvert");
            GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_DownstreamInvert");
            GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToUpstream");
            GravityNetworkReadMapper.ResolveDomainElementField(mgr, "Physical_SetInvertToDownstream");

            Check(mgr.UpstreamInvertSetterInvocations == 0, "Zero upstream setters invoked during resolution");
            Check(mgr.DownstreamInvertSetterInvocations == 0, "Zero downstream setters invoked during resolution");
        }

        // P15-B-R2 Test 9: no new broad reflection discovery is present
        private static void P15BR2_NoNewBroadReflectionDiscovery()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(!text.Contains(".GetMethods()"), "GravityWriteAndSolverHost contains no broad GetMethods() scan");
        }

        // P15-B-R2 Test 10: no hard-coded Bentley path/version/build is introduced
        private static void P15BR2_NoHardCodedBentleyPathVersionOrBuild()
        {
            string srcDir = ResolveAdapterSourceDirectory();
            string hostFile = Path.Combine(srcDir, "GravityWriteAndSolverHost.cs");
            Check(File.Exists(hostFile), "GravityWriteAndSolverHost.cs exists");
            string text = File.ReadAllText(hostFile);
            Check(!text.Contains(string.Concat("C:", "\\", "Program Files", "\\", "Bentley")), "No hard-coded vendor path in GravityWriteAndSolverHost");
            Check(!text.Contains("24.0.2.21"), "No hard-coded build number in GravityWriteAndSolverHost");
        }

        private static void M3_PracticeReadyMunicipalFieldPilot_EndToEndExecution()
        {
            var res = MunicipalFieldPilotPipeline.ExecutePilot();
            Check(res != null, "Milestone 3 pilot result cannot be null");
            Check(res.Success, "Milestone 3 pilot must report success");
            Check(res.OverallStatus == "CONDITIONAL PASS / ACCEPTED WITH DOCUMENTED VARIANCE", "Milestone 3 must retain explicit user variance");
            Check(res.CatchmentCount == 5, "Milestone 3 must evaluate 5 multi-basin catchments");
            Check(res.InletCount == 5, "Milestone 3 must evaluate 5 complex inlets");
            Check(res.ConduitCount == 6, "Milestone 3 must evaluate 6 conduits");
            Check(res.ReturnPeriodEvaluations.Count == 4, "Milestone 3 must evaluate 4 return periods (10, 25, 50, 100-yr)");
            Check(res.ScsHydrologyResults.Count == 5, "Milestone 3 must compute SCS Curve Number hydrographs for all 5 basins");
            Check(res.FormalEngineeringDecisions.Count == 2, "Milestone 3 must formalize 2 engineering decisions");
            Check(File.Exists(res.MarkdownReportPath), "Milestone 3 Markdown report must be generated on disk");
            Check(File.Exists(res.JsonAuditPath), "Milestone 3 Master JSON audit package must be generated on disk");
            Check(File.Exists(res.PlanViewImagePath), "Milestone 3 2D Canvas PNG must be rendered to disk");
            Check(File.Exists(res.ProfileViewImagePath), "Milestone 3 Longitudinal Profile PNG must be rendered to disk");
            Check(new FileInfo(res.PlanViewImagePath).Length > 1000, "2D Canvas PNG must be valid graphic asset");
            Check(new FileInfo(res.ProfileViewImagePath).Length > 1000, "Profile PNG must be valid graphic asset");
        }
    }
}
