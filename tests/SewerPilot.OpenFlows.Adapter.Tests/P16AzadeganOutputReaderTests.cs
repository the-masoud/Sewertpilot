using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    // Offline, deterministic verification of the P16 output-reader and sub-engine probe contracts.
    // No vendor type is instantiated, no model is opened, no file outside a disposable temporary
    // directory is touched and no notification accessor is called.
    internal static partial class Program
    {
        private static P16AzadeganOutputReaderObservation OutputReaderObservation()
        {
            var ids = Enumerable.Range(73, P16AzadeganOutputReaderHost.ExpectedConduitCount).ToArray();
            var o = new P16AzadeganOutputReaderObservation
            {
                Status = P16AzadeganOutputReaderHost.PartialStatus,
                RunId = "0123456789abcdef0123456789abcdef",
                CopyId = "fedcba9876543210fedcba9876543210",
                ArtifactFileName = P16AzadeganOutputReaderHost.ArtifactFileName,
                SourceArtifactUnchanged = true,
                CopyArtifactUnchanged = true,
                MagicNumber = P16AzadeganOutputReaderHost.ExpectedMagicNumber,
                ExpectedMagicNumber = P16AzadeganOutputReaderHost.ExpectedMagicNumber,
                FooterMagicNumber = P16AzadeganOutputReaderHost.ExpectedMagicNumber,
                ReaderIsResultValid = true,
                ConduitIds = ids,
                ConduitIdCount = ids.Length,
                DistinctConduitIdCount = ids.Distinct().Count(),
                ExpectedConduitCount = P16AzadeganOutputReaderHost.ExpectedConduitCount,
                RequestedElementId = P16AzadeganOutputReaderHost.ElementId,
                ElementPresentInMap = true,
                ElementConduitIndex = Array.IndexOf(ids, P16AzadeganOutputReaderHost.ElementId),
                ElementIdentityStatus = P16AzadeganOutputReaderHost.IdentityVerified,
                FieldName = P16AzadeganOutputReaderHost.RequestedFieldName,
                FieldStatus = P16AzadeganOutputReaderHost.FieldSupported,
                // TotalFlow is ordinal 36 in the SewerCAD conduit field enum.
                FieldIndex = P16AzadeganOutputReaderHost.ExpectedFieldOrdinal,
                ExpectedFieldOrdinal = P16AzadeganOutputReaderHost.ExpectedFieldOrdinal,
                FieldOrdinalStatus = P16AzadeganOutputReaderHost.FieldOrdinalConfirmed,
                FieldDeclaredInEnum = true,
                ReaderTypeName = P16AzadeganOutputReaderHost.ReaderTypeName,
                ReaderAssemblyNames = P16AzadeganOutputReaderHost.ReaderAssemblyNames,
                SupportedFieldNames = new[] { P16AzadeganOutputReaderHost.ExpectedFieldEnumLiteralName },
                ReaderSelectionMechanism = P16AzadeganOutputReaderHost.SelectionStaticBinding,
                ReaderSelectionFactoryTypeName = P16AzadeganOutputReaderHost.ReaderSelectionFactoryTypeName,
                ReaderSelectionFactoryStatus = P16AzadeganOutputReaderHost.SelectionFactoryVerifiedNotInvoked,
                ReaderSelectionBasis = P16AzadeganOutputReaderHost.SelectionBasisEvidence,
                ReaderSelectionBasisUsesFileNameSuffix = false,
                TimeStepCount = 1,
                TimeSeconds = new[] { 0d },
                SelectedTimeStepIndex = 0,
                TimeStepSelectionBasis = "SOLE_TIME_STEP",
                TimeStepStatus = P16AzadeganOutputReaderHost.TimeStepSingleSelected,
                RequestedConduitCount = ids.Length,
                ReadConduitCount = ids.Length,
                CoverageComplete = true,
                Values = ids.Select(id => new P16AzadeganOutputReaderValue
                {
                    ElementId = id, ConduitIndex = Array.IndexOf(ids, id),
                    RawValue = "0.00557", ClrType = "System.Double",
                    Classification = "FINITE_DOUBLE_UNIT_UNVERIFIED"
                }).ToArray(),
                WorkingUnitSymbol = null,
                UnitEvidence = P16AzadeganOutputReaderHost.NoUnitMetadataEvidence,
                UnitStatus = P16AzadeganOutputReaderHost.UnitUnverified,
                ExtractionStatus = P16AzadeganOutputReaderHost.ExtractionObserved,
                ArtifactIntegrityStatus = P16AzadeganOutputReaderHost.ArtifactVerified,
                ScenarioProvenanceStatus = P16AzadeganOutputReaderHost.ProvenanceUnverified,
                FreshnessVerified = false,
                EngineeringAcceptance = false,
                ModelOpened = false,
                ModelOpenInvocations = 0,
                SolverRunInvocations = 0,
                SaveInvocations = 0,
                NotificationApiInvocations = 0
            };
            o.Claims = P16AzadeganOutputReaderHost.BuildClaims(o);
            return o;
        }

        private static string OutputReaderJson(P16AzadeganOutputReaderObservation o) =>
            RuntimeHostProtocol.Serializer().Serialize(o);

        private static void RejectOutputReaderObservation(P16AzadeganOutputReaderObservation o)
        {
            string json = OutputReaderJson(o);
            Throws<InvalidDataException>(() => P16AzadeganOutputReaderHost.ValidateObservation(json));
        }

        private static P16AzadeganSubEngineProbeObservation SubEngineProbeObservation()
        {
            return new P16AzadeganSubEngineProbeObservation
            {
                Status = P16AzadeganSubEngineProbeHost.PassStatus,
                ScenarioId = P16AzadeganSubEngineProbeHost.ScenarioId,
                ProductEngineName = P16AzadeganSubEngineProbeHost.ProductEngine,
                ExpectedActiveEngineName = P16AzadeganSubEngineProbeHost.SubEngine,
                ActiveEngineTypeNameForProduct = P16AzadeganSubEngineProbeHost.SubEngine,
                ActiveEngineMatchesExpected = true,
                ResultManagerAvailable = true,
                ResultManagerEngineName = P16AzadeganSubEngineProbeHost.SubEngine,
                ResultManagerScenarioId = P16AzadeganSubEngineProbeHost.ScenarioId,
                HasResults = true,
                ResultManagerClosed = true,
                NotificationSignatures = P16AzadeganSubEngineProbeHost.NotificationSignatures(),
                NotificationProbeEnabled = false,
                NotificationProbeDisabledReason = P16AzadeganSubEngineProbeHost.NotificationProbeReadiness(false, false),
                NotificationApiInvocations = 0,
                NotificationGateUnchanged = true,
                NotificationGateEvidence = P16AzadeganSubEngineProbeHost.GateEvidence,
                ModelOpenInvocations = 1,
                SolverRunInvocations = 0,
                SaveInvocations = 0,
                ScenarioCreateInvocations = 0,
                AlternativeCreateInvocations = 0,
                MutationInvocations = 0
            };
        }

        private static void RejectSubEngineObservation(P16AzadeganSubEngineProbeObservation o)
        {
            string json = RuntimeHostProtocol.Serializer().Serialize(o);
            Throws<InvalidDataException>(() => P16AzadeganSubEngineProbeHost.ValidateObservation(json));
        }

        // A complete observation is accepted, yet it can never assert freshness or acceptance.
        private static void P16OutputReader_ClaimsAreSeparatedAndNeverPromoted()
        {
            var o = OutputReaderObservation();
            P16AzadeganOutputReaderHost.ValidateObservation(OutputReaderJson(o));
            Check(o.Claims.Length == 7, "Every trust claim is carried separately.");
            Check(o.Claims.Single(c => c.Name == P16AzadeganOutputReaderHost.ClaimFreshnessVerified).Status ==
                P16AzadeganOutputReaderHost.NotVerified, "Freshness stays NOT_VERIFIED.");
            Check(o.Claims.Single(c => c.Name == P16AzadeganOutputReaderHost.ClaimEngineeringAcceptance).Status ==
                P16AzadeganOutputReaderHost.NotMinted, "Engineering acceptance stays NOT_MINTED.");

            var freshness = OutputReaderObservation(); freshness.FreshnessVerified = true; RejectOutputReaderObservation(freshness);
            var acceptance = OutputReaderObservation(); acceptance.EngineeringAcceptance = true; RejectOutputReaderObservation(acceptance);
            var wrongClaim = OutputReaderObservation();
            wrongClaim.Claims.Single(c => c.Name == P16AzadeganOutputReaderHost.ClaimFreshnessVerified).Status = "VERIFIED";
            RejectOutputReaderObservation(wrongClaim);
            var missingClaim = OutputReaderObservation();
            missingClaim.Claims = missingClaim.Claims.Take(6).ToArray();
            RejectOutputReaderObservation(missingClaim);
            var unevidenced = OutputReaderObservation();
            unevidenced.Claims[0].Evidence = null;
            RejectOutputReaderObservation(unevidenced);
        }

        // A modified source outranks every later observation; format and reader validity follow.
        private static void P16OutputReader_ArtifactIntegrityPrecedence()
        {
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(false, false, false, null, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactSourceModified, "A modified source dominates.");
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(true, false, false, 1000, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactCopyModified, "A modified copy is detected.");
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(true, true, false, null, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactUnverified, "An absent magic is unverified.");
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(true, true, false, 999, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactFormatUnexpected, "A foreign magic is not this format.");
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(true, true, false, 1000, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactReaderInvalid, "A reader-invalid result is not accepted.");
            Check(P16AzadeganOutputReaderHost.ArtifactIntegrityStatus(true, true, true, 1000, 1000) ==
                P16AzadeganOutputReaderHost.ArtifactVerified, "Full integrity is verified.");

            var tampered = OutputReaderObservation();
            tampered.ArtifactIntegrityStatus = P16AzadeganOutputReaderHost.ArtifactSourceModified;
            RejectOutputReaderObservation(tampered);
        }

        // Element 119 must be present, ids unique and coverage exact; nothing is substituted.
        private static void P16OutputReader_ElementIdentityRequiresExactCoverage()
        {
            Check(P16AzadeganOutputReaderHost.ElementIdentityStatus(119, true, 136, 136, 136) ==
                P16AzadeganOutputReaderHost.IdentityVerified, "Exact identity is verified.");
            Check(P16AzadeganOutputReaderHost.ElementIdentityStatus(119, false, 136, 136, 136) ==
                P16AzadeganOutputReaderHost.IdentityElementAbsent, "A missing element is not substituted.");
            Check(P16AzadeganOutputReaderHost.ElementIdentityStatus(119, true, 136, 135, 136) ==
                P16AzadeganOutputReaderHost.IdentityNotUnique, "Duplicate ids are rejected.");
            Check(P16AzadeganOutputReaderHost.ElementIdentityStatus(119, true, 135, 135, 136) ==
                P16AzadeganOutputReaderHost.IdentityCountMismatch, "A short conduit set is rejected.");
            Check(P16AzadeganOutputReaderHost.ElementIdentityStatus(119, true, 0, 0, 136) ==
                P16AzadeganOutputReaderHost.IdentityUnverified, "An empty conduit set is unverified.");

            var wrongElement = OutputReaderObservation(); wrongElement.RequestedElementId = 120;
            RejectOutputReaderObservation(wrongElement);
            var uncovered = OutputReaderObservation();
            uncovered.CoverageComplete = false;
            RejectOutputReaderObservation(uncovered);
            var shortValues = OutputReaderObservation();
            shortValues.Values = shortValues.Values.Take(135).ToArray();
            RejectOutputReaderObservation(shortValues);
        }

        // The verified field is addressed in the reader that actually declares it; the resolved
        // index must equal the documented ordinal, and nothing is substituted on failure.
        private static void P16OutputReader_FieldResolutionRecordsExactFailure()
        {
            Check(P16AzadeganOutputReaderHost.RequestedFieldName == "GVFConduitResults_Flow",
                "The verified SewerCAD result-definition key is requested verbatim.");
            Check(P16AzadeganOutputReaderHost.ExpectedFieldEnumLiteralName == "TotalFlow",
                "The resolved storage enum literal is TotalFlow.");
            Check(P16AzadeganOutputReaderHost.ExpectedFieldOrdinal == 36,
                "The documented SewerCAD ordinal is 36.");
            Check(P16AzadeganOutputReaderHost.ReaderTypeName ==
                "Haestad.Calculations.Alabama.OutputReader.SewerGravityOutputReader",
                "The reader is the SewerCAD gravity reader.");
            Check(P16AzadeganOutputReaderHost.FieldEnumTypeName ==
                "Haestad.Calculations.Alabama.OutputReader.ConduitResultFields_SS",
                "The field enum is the SewerCAD conduit field vocabulary.");
            Check(P16AzadeganOutputReaderHost.ReaderAssemblyNames.Contains("Haestad.Calculations.SewerCAD.OutputReader"),
                "The SewerCAD reader assembly is in the declared load set.");
            Check(!P16AzadeganOutputReaderHost.ReaderAssemblyNames.Contains("Haestad.Calculations.GVFOutputReader"),
                "The StormCAD GVF reader is not loaded on the SewerCAD path.");
            Check(P16AzadeganOutputReaderHost.ReaderSelectionFactoryTypeName ==
                "Haestad.Calculations.Alabama.OutputReader.AlabamaOutputReaderWrapper",
                "The authoritative SewerCAD reader-selection factory is declared.");

            Check(P16AzadeganOutputReaderHost.FieldStatus(0, null) == P16AzadeganOutputReaderHost.FieldSupported,
                "A non-negative index is supported.");
            Check(P16AzadeganOutputReaderHost.FieldStatus(-1, null) == P16AzadeganOutputReaderHost.FieldUnsupported,
                "A negative index is unsupported.");
            Check(P16AzadeganOutputReaderHost.FieldStatus(null, "ArgumentException: unknown field") ==
                P16AzadeganOutputReaderHost.FieldUnsupported, "A recorded rejection is unsupported.");
            Check(P16AzadeganOutputReaderHost.FieldStatus(null, null) == P16AzadeganOutputReaderHost.FieldUnsupported,
                "An unobserved field is unsupported.");

            Check(P16AzadeganOutputReaderHost.FieldOrdinalStatus(36, 36) ==
                P16AzadeganOutputReaderHost.FieldOrdinalConfirmed, "SewerCAD ordinal 36 is confirmed.");
            Check(P16AzadeganOutputReaderHost.FieldOrdinalStatus(0, 36) ==
                P16AzadeganOutputReaderHost.FieldOrdinalUnexpected, "StormCAD ordinal 0 is not accepted for SewerCAD.");
            Check(P16AzadeganOutputReaderHost.FieldOrdinalStatus(null, 36) ==
                P16AzadeganOutputReaderHost.FieldOrdinalUnexpected, "An unresolved index cannot confirm the SewerCAD ordinal.");

            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldUnsupported,
                P16AzadeganOutputReaderHost.IdentityVerified, 136, 0) ==
                P16AzadeganOutputReaderHost.ExtractionFieldBlocked, "An unsupported field blocks extraction.");
            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldOrdinalUnexpected,
                P16AzadeganOutputReaderHost.IdentityVerified, 136, 0) ==
                P16AzadeganOutputReaderHost.ExtractionFieldBlocked, "An unexpected ordinal blocks extraction.");

            // A resolved index that is not the documented ordinal is recorded and refused, never
            // re-mapped onto another field.
            var wrongOrdinal = OutputReaderObservation();
            wrongOrdinal.FieldIndex = 36;
            wrongOrdinal.FieldOrdinalStatus = P16AzadeganOutputReaderHost.FieldOrdinalUnexpected;
            wrongOrdinal.FieldStatus = P16AzadeganOutputReaderHost.FieldOrdinalUnexpected;
            wrongOrdinal.ExtractionStatus = P16AzadeganOutputReaderHost.ExtractionFieldBlocked;
            wrongOrdinal.Claims = P16AzadeganOutputReaderHost.BuildClaims(wrongOrdinal);
            P16AzadeganOutputReaderHost.ValidateObservation(OutputReaderJson(wrongOrdinal));
            Check(true, "An ordinal-mismatch observation is representable and self-consistent.");

            // A field reported as supported while its ordinal is unconfirmed contradicts itself.
            var unconfirmed = OutputReaderObservation();
            unconfirmed.FieldOrdinalStatus = P16AzadeganOutputReaderHost.FieldOrdinalUnexpected;
            RejectOutputReaderObservation(unconfirmed);

            // Selection justified by the artifact file-name suffix is never acceptable.
            var bySuffix = OutputReaderObservation();
            bySuffix.ReaderSelectionBasisUsesFileNameSuffix = true;
            RejectOutputReaderObservation(bySuffix);
        }

        // A single step is selected by cardinality; several steps require the target by value.
        private static void P16OutputReader_TimeStepNeverAssumesIndexZero()
        {
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(1, new[] { 0d }, 0) ==
                P16AzadeganOutputReaderHost.TimeStepSingleSelected, "The sole step is selected.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(3, new[] { 0d, 60d, 120d }, 0) ==
                P16AzadeganOutputReaderHost.TimeStepTargetSelected, "A present target time is selected by value.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(2, new[] { 60d, 120d }, null) ==
                P16AzadeganOutputReaderHost.TimeStepZeroNotAssumed, "A vector without zero is not inferred.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(2, new[] { 60d, 120d }, 0) ==
                P16AzadeganOutputReaderHost.TimeStepUnverified, "Index 0 without a zero time is not accepted.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(0, new double[0], null) ==
                P16AzadeganOutputReaderHost.TimeStepNoSteps, "An empty section has no enumerated steps.");

            var swrcIndexZero = OutputReaderObservation();
            swrcIndexZero.TimeStepCount = 0;
            swrcIndexZero.TimeSeconds = new double[0];
            swrcIndexZero.RawTimeAtIndexZero = 0d;
            swrcIndexZero.SelectedTimeStepIndex = 0;
            swrcIndexZero.TimeStepSelectionBasis = "SWRC_INDEX0_ONLY_FOOTER_COUNT_ZERO";
            swrcIndexZero.TimeStepStatus = P16AzadeganOutputReaderHost.TimeStepSwrcIndex0RecordOnly;
            swrcIndexZero.Claims = P16AzadeganOutputReaderHost.BuildClaims(swrcIndexZero);
            P16AzadeganOutputReaderHost.ValidateObservation(OutputReaderJson(swrcIndexZero));
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(swrcIndexZero) ==
                P16AzadeganOutputReaderHost.PartialStatus,
                "SWRC index-0 values may be recorded, but the result remains partial because time semantics are unverified.");

            var invalidSwrcIndexZero = OutputReaderObservation();
            invalidSwrcIndexZero.TimeStepCount = 0;
            invalidSwrcIndexZero.TimeSeconds = new double[0];
            invalidSwrcIndexZero.SelectedTimeStepIndex = 0;
            invalidSwrcIndexZero.TimeStepSelectionBasis = "SWRC_INDEX0_ONLY_FOOTER_COUNT_ZERO";
            invalidSwrcIndexZero.TimeStepStatus = P16AzadeganOutputReaderHost.TimeStepSwrcIndex0RecordOnly;
            invalidSwrcIndexZero.Claims = P16AzadeganOutputReaderHost.BuildClaims(invalidSwrcIndexZero);
            RejectOutputReaderObservation(invalidSwrcIndexZero);
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(3, new[] { 0d, 60d }, null) ==
                P16AzadeganOutputReaderHost.TimeStepVectorIncomplete, "A short vector is incomplete.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(1, new[] { 0d }, null) ==
                P16AzadeganOutputReaderHost.TimeStepUnverified, "A single step still requires explicit selection.");
            Check(P16AzadeganOutputReaderHost.TimeStepStatus(10001, new double[10001], 0) ==
                P16AzadeganOutputReaderHost.TimeStepBoundExceeded, "An oversized section is bounded.");
        }

        // A unit is only verified from authoritative evidence, which this reader surface lacks.
        private static void P16OutputReader_UnitVerificationRequiresAuthoritativeEvidence()
        {
            Check(P16AzadeganOutputReaderHost.UnitStatus(null, P16AzadeganOutputReaderHost.NoUnitMetadataEvidence) ==
                P16AzadeganOutputReaderHost.UnitUnverified, "No unit metadata means no unit claim.");
            Check(P16AzadeganOutputReaderHost.UnitStatus("L/s", null) ==
                P16AzadeganOutputReaderHost.UnitUnverified, "A symbol without evidence is not a claim.");
            Check(P16AzadeganOutputReaderHost.UnitStatus("L/s", "native enum literal") ==
                P16AzadeganOutputReaderHost.UnitVerified, "Symbol plus evidence verifies.");

            var inventedUnit = OutputReaderObservation();
            inventedUnit.UnitStatus = P16AzadeganOutputReaderHost.UnitVerified;
            inventedUnit.WorkingUnitSymbol = null;
            RejectOutputReaderObservation(inventedUnit);
        }

        // Extraction status is a composition of independent facts, not a single success flag.
        private static void P16OutputReader_ExtractionStatusComposition()
        {
            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldSupported,
                P16AzadeganOutputReaderHost.IdentityVerified, 136, 136) ==
                P16AzadeganOutputReaderHost.ExtractionObserved, "Full read is observed.");
            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldSupported,
                P16AzadeganOutputReaderHost.IdentityVerified, 136, 135) ==
                P16AzadeganOutputReaderHost.ExtractionIncomplete, "A partial read is incomplete.");
            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldSupported,
                P16AzadeganOutputReaderHost.IdentityElementAbsent, 136, 136) ==
                P16AzadeganOutputReaderHost.ExtractionIdentityBlocked, "Unverified identity blocks extraction.");
            Check(P16AzadeganOutputReaderHost.ExtractionStatus(P16AzadeganOutputReaderHost.FieldSupported,
                P16AzadeganOutputReaderHost.IdentityVerified, 0, 0) ==
                P16AzadeganOutputReaderHost.ExtractionIncomplete, "Nothing requested is nothing observed.");
        }

        // Mechanics completing is not a pass: an open unit claim keeps the outcome partial.
        private static void P16OutputReader_OutcomeClassificationKeepsClaimsSeparate()
        {
            var observed = OutputReaderObservation();
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(observed) ==
                P16AzadeganOutputReaderHost.PartialStatus, "Unverified units keep the outcome partial.");
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(observed) !=
                P16AzadeganOutputReaderHost.PassStatus, "A partial outcome is never a pass.");

            var verified = OutputReaderObservation();
            verified.WorkingUnitSymbol = "L/s";
            verified.UnitStatus = P16AzadeganOutputReaderHost.UnitVerified;
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(verified) ==
                P16AzadeganOutputReaderHost.PassStatus, "Only full verification is a pass.");

            var blocked = OutputReaderObservation();
            blocked.FieldStatus = P16AzadeganOutputReaderHost.FieldUnsupported;
            blocked.ExtractionStatus = P16AzadeganOutputReaderHost.ExtractionFieldBlocked;
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(blocked) ==
                P16AzadeganOutputReaderHost.ExtractionFieldBlocked, "An unsupported field is a blocked outcome.");

            var tampered = OutputReaderObservation();
            tampered.ArtifactIntegrityStatus = P16AzadeganOutputReaderHost.ArtifactCopyModified;
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(tampered) ==
                P16AzadeganOutputReaderHost.ArtifactCopyModified, "A modified copy is a blocked outcome.");

            var noSteps = OutputReaderObservation();
            noSteps.TimeStepStatus = P16AzadeganOutputReaderHost.TimeStepZeroNotAssumed;
            Check(P16AzadeganOutputReaderHost.ClassifyOutcome(noSteps) ==
                P16AzadeganOutputReaderHost.TimeStepZeroNotAssumed, "An unresolved timestep is a blocked outcome.");
        }

        // The reader request only ever addresses its own disposable directory and declared store.
        private static void P16OutputReader_RequestValidationRejectsForeignArtifact()
        {
            string root = Path.GetTempPath();
            string copyId = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(root, "SewerPilot", "OpenFlows", "OutputReader", copyId);
            string foreignId = Guid.NewGuid().ToString("N");
            string foreignDirectory = Path.Combine(root, "SewerPilot", "OpenFlows", "OutputReader", foreignId);
            string sourcePath = Path.Combine(root, "sp-source-" + Guid.NewGuid().ToString("N") + ".out");
            string looseArtifact = Path.Combine(root, P16AzadeganOutputReaderHost.ArtifactFileName);
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(foreignDirectory);
            try
            {
                string artifact = Path.Combine(directory, P16AzadeganOutputReaderHost.ArtifactFileName);
                File.WriteAllText(artifact, "x");
                File.WriteAllText(sourcePath, "x");
                File.WriteAllText(Path.Combine(foreignDirectory, "another.out"), "x");
                File.WriteAllText(looseArtifact, "x");

                var assemblies = P16AzadeganOutputReaderHost.ReaderAssemblyNames
                    .Select(name => new HostMetadata
                    {
                        SimpleName = name,
                        Path = Path.Combine(root, name + ".dll"),
                        SHA256 = new string('B', 64)
                    }).ToArray();
                var context = new Dictionary<string, string>
                {
                    { "P16AzadeganScenarioId", "353" },
                    { "P16AzadeganElementId", "119" }
                };

                P16AzadeganOutputReaderHost.ValidateRequest(OutputReaderRequest(root, artifact, sourcePath,
                    copyId, assemblies, context));
                Check(true, "A fully declared disposable copy is accepted.");

                // A foreign store name in an otherwise valid directory is rejected.
                Throws<InvalidDataException>(() => P16AzadeganOutputReaderHost.ValidateRequest(
                    OutputReaderRequest(root, Path.Combine(foreignDirectory, "another.out"), sourcePath,
                        foreignId, assemblies, context)));

                // A store outside the controlled per-run boundary is rejected.
                Throws<InvalidDataException>(() => P16AzadeganOutputReaderHost.ValidateRequest(
                    OutputReaderRequest(root, looseArtifact, sourcePath, copyId, assemblies, context)));

                // Exactly the three declared reader assemblies are required.
                Throws<InvalidDataException>(() => P16AzadeganOutputReaderHost.ValidateRequest(
                    OutputReaderRequest(root, artifact, sourcePath, copyId, assemblies.Take(2).ToArray(), context)));

                // The bounded scenario/element context cannot be substituted.
                Throws<InvalidDataException>(() => P16AzadeganOutputReaderHost.ValidateRequest(
                    OutputReaderRequest(root, artifact, sourcePath, copyId, assemblies,
                        new Dictionary<string, string>
                        {
                            { "P16AzadeganScenarioId", "359" },
                            { "P16AzadeganElementId", "119" }
                        })));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch (IOException) { }
                try { Directory.Delete(foreignDirectory, true); } catch (IOException) { }
                try { File.Delete(sourcePath); } catch (IOException) { }
                try { File.Delete(looseArtifact); } catch (IOException) { }
            }
        }

        private static P16AzadeganOutputReaderRequest OutputReaderRequest(string root, string artifact,
            string sourcePath, string copyId, HostMetadata[] assemblies, Dictionary<string, string> context)
        {
            return new P16AzadeganOutputReaderRequest
            {
                SchemaVersion = 1,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = P16AzadeganOutputReaderHost.Operation,
                ReaderRoot = root,
                ArtifactPath = artifact,
                SourceArtifactPath = sourcePath,
                SourceArtifactSHA256 = new string('A', 64),
                CopyId = copyId,
                ReaderAssemblies = assemblies,
                Diagnostics = context
            };
        }

        // The reader path cannot report a model open, a solve, a save or a notification call.
        private static void P16OutputReader_ObservationRejectsForbiddenActivity()
        {
            var modelOpened = OutputReaderObservation(); modelOpened.ModelOpened = true; RejectOutputReaderObservation(modelOpened);
            var modelCount = OutputReaderObservation(); modelCount.ModelOpenInvocations = 1; RejectOutputReaderObservation(modelCount);
            var solved = OutputReaderObservation(); solved.SolverRunInvocations = 1; RejectOutputReaderObservation(solved);
            var saved = OutputReaderObservation(); saved.SaveInvocations = 1; RejectOutputReaderObservation(saved);
            var notified = OutputReaderObservation(); notified.NotificationApiInvocations = 1; RejectOutputReaderObservation(notified);
        }

        // The notification surface is recorded as uninvoked signatures and stays disabled.
        private static void P16SubEngineProbe_NotificationProbeStaysDisabled()
        {
            var o = SubEngineProbeObservation();
            P16AzadeganSubEngineProbeHost.ValidateObservation(RuntimeHostProtocol.Serializer().Serialize(o));
            Check(o.NotificationSignatures.Length == 3, "All three notification signatures are recorded.");
            Check(o.NotificationSignatures.All(s => !s.Invoked), "No notification signature is invoked.");
            Check(!P16AzadeganSubEngineProbeHost.NotificationProbeEnabled(false, false),
                "The live notification probe is disabled by default.");
            Check(P16AzadeganSubEngineProbeHost.NotificationProbeReadiness(false, false) ==
                P16AzadeganSubEngineProbeHost.ArgumentSemanticsUnverified, "Unverified argument semantics block the probe.");
            Check(P16AzadeganSubEngineProbeHost.NotificationProbeReadiness(true, false) ==
                P16AzadeganSubEngineProbeHost.ArgumentSemanticsUnverified, "Argument semantics are required.");
            Check(P16AzadeganSubEngineProbeHost.NotificationProbeReadiness(false, true) ==
                P16AzadeganSubEngineProbeHost.ConnectionIdentityUnverified, "Connection identity is required.");
            Check(P16AzadeganSubEngineProbeHost.NotificationProbeReadiness(true, true) == null,
                "Both prerequisites would enable the probe.");

            var invoked = SubEngineProbeObservation();
            invoked.NotificationSignatures[0].Invoked = true;
            RejectSubEngineObservation(invoked);
            var called = SubEngineProbeObservation(); called.NotificationApiInvocations = 1; RejectSubEngineObservation(called);
            var contradictory = SubEngineProbeObservation();
            contradictory.NotificationProbeEnabled = true;
            RejectSubEngineObservation(contradictory);
        }

        // The sub-engine probe cannot solve, save, create, mutate or weaken the gate.
        private static void P16SubEngineProbe_ObservationRejectsWriteAndGateChange()
        {
            var solved = SubEngineProbeObservation(); solved.SolverRunInvocations = 1; RejectSubEngineObservation(solved);
            var saved = SubEngineProbeObservation(); saved.SaveInvocations = 1; RejectSubEngineObservation(saved);
            var scenario = SubEngineProbeObservation(); scenario.ScenarioCreateInvocations = 1; RejectSubEngineObservation(scenario);
            var alternative = SubEngineProbeObservation(); alternative.AlternativeCreateInvocations = 1; RejectSubEngineObservation(alternative);
            var mutated = SubEngineProbeObservation(); mutated.MutationInvocations = 1; RejectSubEngineObservation(mutated);
            var gate = SubEngineProbeObservation(); gate.NotificationGateUnchanged = false; RejectSubEngineObservation(gate);
            var otherScenario = SubEngineProbeObservation(); otherScenario.ScenarioId = 359; RejectSubEngineObservation(otherScenario);
            Check(SubEngineProbeObservation().NotificationGateEvidence == P16AzadeganSubEngineProbeHost.GateEvidence,
                "The completeness gate is carried unchanged.");
        }
    }
}
