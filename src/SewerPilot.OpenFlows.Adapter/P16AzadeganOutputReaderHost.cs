using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SewerPilot.OpenFlows.Adapter
{
    // One extracted conduit value exactly as the installed reader returned it. The unit is
    // deliberately absent: no authoritative unit metadata exists on this reader surface, so a
    // unit label here would be an invention.
    internal sealed class P16AzadeganOutputReaderValue
    {
        public P16AzadeganOutputReaderValue() { }
        public int ElementId { get; set; }
        public int ConduitIndex { get; set; }
        public string RawValue { get; set; }
        public string ClrType { get; set; }
        public string Classification { get; set; }
    }

    // One separated trust claim. Claims are independent on purpose: a verified artifact must not
    // imply verified units, and a successful read must not imply engineering acceptance.
    internal sealed class P16AzadeganOutputReaderClaim
    {
        public P16AzadeganOutputReaderClaim() { }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Evidence { get; set; }
    }

    // Everything the output-reader path observed, with each trust claim kept separate.
    // FreshnessVerified and EngineeringAcceptance are structural constants of this path, not
    // outcomes: the artifact is a historical vendor calculation, not a SewerPilot run.
    internal sealed class P16AzadeganOutputReaderObservation
    {
        public P16AzadeganOutputReaderObservation() { }
        public string Status { get; set; } = "RESULT_CONTRACT_UNAVAILABLE";
        public string RunId { get; set; }
        public string CopyId { get; set; }
        public string ArtifactDirectory { get; set; }
        public string ArtifactFileName { get; set; }
        public string ArtifactPath { get; set; }
        public string SourceArtifactPath { get; set; }
        public string SourceArtifactSHA256Before { get; set; }
        public string SourceArtifactSHA256After { get; set; }
        public bool SourceArtifactUnchanged { get; set; }
        public string CopyArtifactSHA256Before { get; set; }
        public string CopyArtifactSHA256After { get; set; }
        public bool CopyArtifactUnchanged { get; set; }
        public long ArtifactBytes { get; set; }
        public bool ArtifactDirectoryIsolated { get; set; }
        public string[] ArtifactDirectoryEntries { get; set; } = new string[0];

        public string ReaderTypeName { get; set; }
        public string[] ReaderAssemblyNames { get; set; } = new string[0];
        public string[] ReaderAssemblySHA256 { get; set; } = new string[0];
        public string ReaderAssemblyRoot { get; set; }

        // How the reader type was chosen, recorded so selection can never be mistaken for a
        // factory decision that was never made.
        public string ReaderSelectionMechanism { get; set; }
        public string ReaderSelectionFactoryTypeName { get; set; }
        public string ReaderSelectionFactoryStatus { get; set; }
        public string ReaderSelectionBasis { get; set; }
        public bool ReaderSelectionBasisUsesFileNameSuffix { get; set; }

        // Structural zeroes. They are recorded in the payload so no consumer can read this
        // observation as if a model had been opened, a solver run or a save performed.
        public int ModelOpenInvocations { get; set; }
        public int SolverRunInvocations { get; set; }
        public int SaveInvocations { get; set; }
        public int NotificationApiInvocations { get; set; }
        public bool ModelOpened { get; set; }

        public bool ReaderConstructed { get; set; }
        public bool OpenInvoked { get; set; }
        public bool IsOpen { get; set; }
        public bool ReaderIsResultValid { get; set; }
        public bool CloseInvoked { get; set; }
        public string CloseError { get; set; }

        public int? MagicNumber { get; set; }
        public int? ExpectedMagicNumber { get; set; }
        public int? FooterMagicNumber { get; set; }
        public int? HeaderNumberOfConduits { get; set; }
        public int? HeaderNumberOfGravityNodes { get; set; }
        public bool FooterIsResultValid { get; set; }

        public int ConduitIdCount { get; set; }
        public int DistinctConduitIdCount { get; set; }
        public int[] ConduitIds { get; set; } = new int[0];
        public int ExpectedConduitCount { get; set; }
        public int RequestedElementId { get; set; }
        public int? ElementConduitIndex { get; set; }
        public bool ElementPresentInMap { get; set; }
        public string ElementIdentityStatus { get; set; }

        public string FieldName { get; set; }
        public int? FieldIndex { get; set; }
        public string FieldStatus { get; set; }
        public string FieldFailure { get; set; }
        public string[] SupportedFieldNames { get; set; } = new string[0];
        public bool? FieldIsExtendedResult { get; set; }
        public bool FieldDeclaredInEnum { get; set; }
        public int ExpectedFieldOrdinal { get; set; }
        public string FieldOrdinalStatus { get; set; }

        public int TimeStepCount { get; set; }
        public double[] TimeSeconds { get; set; } = new double[0];
        // Raw CalculationTime(0) observation for the SWRC-only index-0 path. It is not a
        // verified time-step vector and must not be promoted to temporal semantics.
        public double? RawTimeAtIndexZero { get; set; }
        public int? SelectedTimeStepIndex { get; set; }
        public string TimeStepSelectionBasis { get; set; }
        public string TimeStepStatus { get; set; }

        public int RequestedConduitCount { get; set; }
        public int ReadConduitCount { get; set; }
        public bool CoverageComplete { get; set; }
        public P16AzadeganOutputReaderValue[] Values { get; set; } = new P16AzadeganOutputReaderValue[0];

        public string WorkingUnitSymbol { get; set; }
        public string UnitEvidence { get; set; }
        public string UnitStatus { get; set; }

        public string ExtractionStatus { get; set; }
        public string ArtifactIntegrityStatus { get; set; }
        public string ScenarioProvenanceStatus { get; set; }
        public bool FreshnessVerified { get; set; }
        public bool EngineeringAcceptance { get; set; }
        public P16AzadeganOutputReaderClaim[] Claims { get; set; } = new P16AzadeganOutputReaderClaim[0];

        public string FailureOperation { get; set; }
        public string FailureClassification { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal sealed class P16AzadeganOutputReaderRequest
    {
        public P16AzadeganOutputReaderRequest() { }
        public int SchemaVersion { get; set; }
        public string RunId { get; set; }
        public string Operation { get; set; }
        public string ReaderRoot { get; set; }
        public string ArtifactPath { get; set; }
        public string SourceArtifactPath { get; set; }
        public string SourceArtifactSHA256 { get; set; }
        public string CopyId { get; set; }
        public HostMetadata[] ReaderAssemblies { get; set; }
        public Dictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
    }

    internal sealed class P16AzadeganOutputReaderResult
    {
        public P16AzadeganOutputReaderResult() { }
        public int SchemaVersion { get; set; }
        public string RunId { get; set; }
        public string Operation { get; set; }
        public bool Success { get; set; }
        public bool ResultCommitted { get; set; }
        public string Status { get; set; }
        public string FailureClassification { get; set; }
        public string FailureMessageSafe { get; set; }
        public string ObservationJson { get; set; }
        public HostMetadata[] LoadedReaderAssemblies { get; set; } = new HostMetadata[0];
        public int ModelOpenInvocations { get; set; }
        public int SolverRunInvocations { get; set; }
        public int SaveInvocations { get; set; }
        public int NotificationApiInvocations { get; set; }
        public Dictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
    }

    // Bounded, model-free extraction of scenario 353's conduit results from the vendor output
    // store through the installed public output reader. It opens no model, runs no solver,
    // saves nothing and touches no notification API. It mints no engineering acceptance.
    internal static class P16AzadeganOutputReaderHost
    {
        internal const string Operation = "P16AzadeganOutputFileReaderExtraction";
        internal const string LaunchSwitch = "--internal-p16-azadegan-output-reader";
        internal const string EvidenceKey = "P16AzadeganOutputReaderObservationJson";
        internal const string StatusKey = "P16AzadeganOutputReaderStatus";
        internal const string PassFlag = "P16AzadeganOutputReaderPassed";
        internal const string PassStatus = "PASS_OUTPUT_READER_EXTRACTION_MECHANICS";
        internal const string PartialStatus = "EXTRACTION_OBSERVED_TRUST_CLAIMS_INCOMPLETE";
        internal const string BlockedFailureCode = "P16_OUTPUT_READER_BLOCKED";

        internal const string ArtifactFileName = "Azadegab ssw 26.10.25.stsw_1_353-swrc-gvf.out";
        internal const int ExpectedMagicNumber = 1000;
        internal const int ScenarioId = 353;
        internal const int DataSetId = 1;
        internal const int ExpectedConduitCount = 136;
        internal const int ElementId = 119;
        internal const string ElementLabel = "SSW-001";

        // Verified SewerCAD field binding. The shared vendor result-definition key
        // GVFConduitResults_Flow resolves to TotalFlow (ordinal 36) in the SewerCAD vocabulary.
        // This is not the StormCAD-only CalculatedTotalGravityFlow field (ordinal 0).
        internal const string RequestedFieldName = "GVFConduitResults_Flow";
        internal const string ExpectedFieldEnumLiteralName = "TotalFlow";
        internal const int ExpectedFieldOrdinal = 36;

        internal const string ReaderTypeName = "Haestad.Calculations.Alabama.OutputReader.SewerGravityOutputReader";
        internal const string BaseReaderTypeName = "Haestad.Calculations.StormSewer.OutputReader.GravityOutputReaderBase";
        internal const string HeaderTypeName = "Haestad.Calculations.StormSewer.OutputReader.IGravityOutputReaderHeaderItem";
        internal const string FooterTypeName = "Haestad.Calculations.StormSewer.OutputReader.IGravityOutputReaderFooterItem";
        internal const string FooterReaderTypeName = "Haestad.Calculations.StormSewer.OutputReader.GravityOutputFooterReader";
        internal const string DynamicTypeName = "Haestad.Calculations.StormSewer.OutputReader.GravityTimeStepOutputReaderBase";
        internal const string ElementResultTypeName = "Haestad.Calculations.StormSewer.OutputReader.ElementResultType";
        internal const string FieldEnumTypeName = "Haestad.Calculations.Alabama.OutputReader.ConduitResultFields_SS";
        internal const string HmIdCollectionTypeName = "Haestad.Support.Support.HmIDCollection";
        internal const string IdToDoubleDictionaryTypeName = "Haestad.Support.Support.IHmIDToDoubleDictionary";

        // The authoritative SewerCAD selector is AlabamaOutputReaderWrapper.GetOutputReaderFor(int).
        // Its constructor requires an IDomainDataSet, unavailable on this model-free file-reader path,
        // so the factory is verified but not invoked. The static reader binding below is established
        // from the installed SewerCAD header layout and is never selected from a filename suffix.
        internal const string ReaderSelectionFactoryTypeName =
            "Haestad.Calculations.Alabama.OutputReader.AlabamaOutputReaderWrapper";
        internal const string ReaderSelectionFactoryMethod = "GetOutputReaderFor";

        // Load order: support collections, shared output-reader contracts, then SewerCAD reader.
        internal static readonly string[] ReaderAssemblyNames =
        {
            "Haestad.Support",
            "Haestad.Calculations.StormSewer.OutputReader",
            "Haestad.Calculations.SewerCAD.OutputReader"
        };

        internal const string ClaimArtifactIntegrity = "ArtifactIntegrity";
        internal const string ClaimScenarioProvenance = "ScenarioProvenance";
        internal const string ClaimResultExtraction = "ResultExtraction";
        internal const string ClaimUnitVerification = "UnitVerification";
        internal const string ClaimTimeStepVerification = "TimeStepVerification";
        internal const string ClaimFreshnessVerified = "FreshnessVerified";
        internal const string ClaimEngineeringAcceptance = "EngineeringAcceptance";

        internal const string NotVerified = "NOT_VERIFIED";
        internal const string NotMinted = "NOT_MINTED";

        internal const string ArtifactVerified = "VERIFIED";
        internal const string ArtifactSourceModified = "HARD_FAIL_SOURCE_ARTIFACT_MODIFIED";
        internal const string ArtifactCopyModified = "HARD_FAIL_COPY_ARTIFACT_MODIFIED";
        internal const string ArtifactFormatUnexpected = "ARTIFACT_FORMAT_UNEXPECTED";
        internal const string ArtifactUnverified = "ARTIFACT_INTEGRITY_UNVERIFIED";
        internal const string ArtifactReaderInvalid = "READER_REPORTS_RESULT_INVALID";

        internal const string ProvenanceVerified = "VERIFIED_BY_NAME_TIMESTAMP_AND_ENGINE";
        internal const string ProvenanceUnverified = "PROVENANCE_UNVERIFIED";

        internal const string IdentityVerified = "VERIFIED";
        internal const string IdentityElementAbsent = "ELEMENT_IDENTITY_UNVERIFIED";
        internal const string IdentityNotUnique = "CONDUIT_IDS_NOT_UNIQUE";
        internal const string IdentityCountMismatch = "CONDUIT_COUNT_MISMATCH";
        internal const string IdentityUnverified = "CONDUIT_IDENTITY_UNVERIFIED";

        internal const string FieldSupported = "SUPPORTED";
        internal const string FieldUnsupported = "RESULT_FIELD_UNSUPPORTED";
        internal const string FieldUnverified = "RESULT_FIELD_UNVERIFIED";
        internal const string FieldOrdinalUnexpected = "RESULT_FIELD_ORDINAL_UNEXPECTED";
        internal const string FieldOrdinalConfirmed = "FIELD_ORDINAL_CONFIRMED";

        // Reader selection outcomes. The mechanism is always the verified static binding on this
        // model-free path; the factory status records why the authoritative selector was not used.
        internal const string SelectionStaticBinding = "STATIC_VERIFIED_BINDING";
        internal const string SelectionFactoryVerifiedNotInvoked =
            "FACTORY_CONTRACT_VERIFIED_NOT_INVOKED_NO_DOMAIN_DATASET";
        internal const string SelectionFactoryContractUnavailable = "FACTORY_CONTRACT_UNAVAILABLE";

        internal const string SelectionBasisEvidence =
            "SewerGravityOutputReader is selected by the installed SewerCAD header contract, not by the artifact file-name suffix. " +
            "SewerGravityOutputHeaderReader reads conduit count at byte 12 for this format; the shared field key " +
            "GVFConduitResults_Flow resolves to TotalFlow ordinal 36 in ConduitResultFields_SS. " +
            "AlabamaOutputReaderWrapper.GetOutputReaderFor(int) is contract-verified but not invoked because its " +
            "constructor requires an IDomainDataSet and this model-free path does not open a model.";

        internal const string TimeStepSingleSelected = "SINGLE_TIME_STEP_SELECTED";
        internal const string TimeStepTargetSelected = "TARGET_TIME_STEP_SELECTED";
        internal const string TimeStepAbsent = "TARGET_TIME_STEP_NOT_PRESENT";
        internal const string TimeStepZeroNotAssumed = "TIME_STEP_ZERO_NOT_ASSUMED";
        internal const string TimeStepUnverified = "TIME_STEP_UNVERIFIED";
        // SewerCAD's SWRC reader explicitly supports only record index 0, while its public
        // time-step count contract reports 0 and does not establish temporal semantics.
        internal const string TimeStepSwrcIndex0RecordOnly = "SWRC_INDEX0_RECORD_ONLY_TIME_UNVERIFIED";
        internal const string TimeStepNoSteps = "NO_TIME_STEPS_PRESENT";
        internal const string TimeStepVectorIncomplete = "TIME_STEP_VECTOR_INCOMPLETE";
        internal const string TimeStepBoundExceeded = "TIME_STEP_BOUND_EXCEEDED";

        internal const string UnitVerified = "VERIFIED";
        internal const string UnitUnverified = "UNIT_UNVERIFIED";

        internal const string ExtractionObserved = "VALUES_OBSERVED_UNITS_UNVERIFIED";
        internal const string ExtractionIncomplete = "EXTRACTION_INCOMPLETE";
        internal const string ExtractionFieldBlocked = "EXTRACTION_NOT_ATTEMPTED_FIELD_UNSUPPORTED";
        internal const string ExtractionIdentityBlocked = "EXTRACTION_NOT_ACCEPTED_IDENTITY_UNVERIFIED";
        internal const string ExtractionNotAttempted = "EXTRACTION_NOT_ATTEMPTED";

        internal const string NoUnitMetadataEvidence =
            "The installed output-reader surface declares no unit, symbol, scale or conversion member: " +
            "a full literal scan of Haestad.Calculations.SewerCAD.OutputReader.dll, " +
            "Haestad.Calculations.StormSewer.OutputReader.dll and Haestad.Calculations.GVFOutputReader.dll " +
            "for unit|symbol|format|scale|measure|dimension returns only IFormatProvider and " +
            "AssemblyInformationalVersionAttribute. The reader returns bare doubles with no unit identity, " +
            "so no unit can be established from this path. Deriving one from the field name, the file layout, " +
            "a workbook value or a timestamp is forbidden.";

        internal const string FreshnessEvidence =
            "The artifact is the vendor product's own calculation from the delivered model state, not a " +
            "SewerPilot-executed calculation. Freshness is therefore structurally unprovable on this path.";

        internal const string EngineeringAcceptanceEvidence =
            "Extraction mechanics are not engineering acceptance. No hydraulic criterion, design flow " +
            "comparison or unit-consistent review is performed or implied by this operation.";

        internal static readonly string[] ClaimNames =
        {
            ClaimArtifactIntegrity, ClaimScenarioProvenance, ClaimResultExtraction,
            ClaimUnitVerification, ClaimTimeStepVerification,
            ClaimFreshnessVerified, ClaimEngineeringAcceptance
        };

        private const int MaxConduits = 20000, MaxTimeSteps = 10000, MaxFields = 1024;

        private static string _lastOperation;
        private static string LastOperation { get { return _lastOperation; } set { _lastOperation = value; } }

        // ------------------------------------------------------------ pure classification

        // Artifact integrity precedence: a modified source is the most severe outcome and can
        // never be outranked by a later observation.
        internal static string ArtifactIntegrityStatus(bool sourceUnchanged, bool copyUnchanged,
            bool readerResultValid, int? magic, int expectedMagic)
        {
            if (!sourceUnchanged) return ArtifactSourceModified;
            if (!copyUnchanged) return ArtifactCopyModified;
            if (!magic.HasValue) return ArtifactUnverified;
            if (magic.Value != expectedMagic) return ArtifactFormatUnexpected;
            if (!readerResultValid) return ArtifactReaderInvalid;
            return ArtifactVerified;
        }

        internal static string ElementIdentityStatus(int expectedElementId, bool elementPresent,
            int conduitIdCount, int distinctCount, int expectedConduitCount)
        {
            if (conduitIdCount <= 0) return IdentityUnverified;
            if (distinctCount != conduitIdCount) return IdentityNotUnique;
            if (expectedConduitCount > 0 && conduitIdCount != expectedConduitCount) return IdentityCountMismatch;
            if (expectedElementId > 0 && !elementPresent) return IdentityElementAbsent;
            return IdentityVerified;
        }

        internal static string FieldStatus(int? fieldIndex, string failure)
        {
            if (failure != null) return FieldUnsupported;
            if (!fieldIndex.HasValue || fieldIndex.Value < 0) return FieldUnsupported;
            return FieldSupported;
        }

        // The resolved index must be the documented ordinal. A mismatch means the installed
        // reader's vocabulary is not the one that was verified, so the field is not usable and
        // no other field is substituted in its place.
        internal static string FieldOrdinalStatus(int? fieldIndex, int expectedOrdinal)
        {
            if (!fieldIndex.HasValue) return FieldOrdinalUnexpected;
            return fieldIndex.Value == expectedOrdinal ? FieldOrdinalConfirmed : FieldOrdinalUnexpected;
        }

        // A single time step may be selected because it is the only candidate, which is a
        // cardinality fact, not an assumption about index 0. With more than one step the target
        // time must be present by value.
        internal static string TimeStepStatus(int count, double[] times, int? selected)
        {
            if (count <= 0) return TimeStepNoSteps;
            if (count > MaxTimeSteps) return TimeStepBoundExceeded;
            if (times == null || times.Length != count) return TimeStepVectorIncomplete;
            if (count == 1) return selected == 0 ? TimeStepSingleSelected : TimeStepUnverified;
            if (selected.HasValue)
            {
                if (selected.Value < 0 || selected.Value >= count) return TimeStepUnverified;
                return times[selected.Value] == 0d ? TimeStepTargetSelected : TimeStepUnverified;
            }
            return times.Any(t => t == 0d) ? TimeStepUnverified : TimeStepZeroNotAssumed;
        }

        internal static string UnitStatus(string symbol, string evidence)
        {
            if (!string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(evidence)) return UnitVerified;
            return UnitUnverified;
        }

        internal static string ExtractionStatus(string fieldStatus, string identityStatus,
            int requested, int read)
        {
            if (fieldStatus != FieldSupported) return ExtractionFieldBlocked;
            if (identityStatus != IdentityVerified) return ExtractionIdentityBlocked;
            if (requested <= 0 || read != requested) return ExtractionIncomplete;
            return ExtractionObserved;
        }

        internal static P16AzadeganOutputReaderClaim[] BuildClaims(P16AzadeganOutputReaderObservation o)
        {
            if (o == null) throw new ArgumentNullException("o");
            return new[]
            {
                new P16AzadeganOutputReaderClaim { Name = ClaimArtifactIntegrity,
                    Status = o.ArtifactIntegrityStatus ?? ArtifactUnverified,
                    Evidence = "Magic " + Show(o.MagicNumber) + " vs expected " + Show(o.ExpectedMagicNumber) +
                        "; source unchanged " + o.SourceArtifactUnchanged + "; copy unchanged " + o.CopyArtifactUnchanged +
                        "; reader IsResultValid " + o.ReaderIsResultValid + "; footer magic " + Show(o.FooterMagicNumber) + "." },
                new P16AzadeganOutputReaderClaim { Name = ClaimScenarioProvenance,
                    Status = o.ScenarioProvenanceStatus ?? ProvenanceUnverified,
                    Evidence = "Scenario " + ScenarioId + " / data set " + DataSetId + " binding carried from the " +
                        "artifact name convention and the project compute record. The delivered model database is " +
                        "consistent with this scenario: 136 live conduits and 136 live nodes equal the artifact's id " +
                        "sets, but element coverage cannot discriminate Scenario 353 from Scenario 350, which shares " +
                        "the same network. Provenance therefore remains unproven and is never promoted to freshness." },
                new P16AzadeganOutputReaderClaim { Name = ClaimResultExtraction,
                    Status = o.ExtractionStatus ?? ExtractionNotAttempted,
                    Evidence = "Field '" + (o.FieldName ?? RequestedFieldName) + "' status " + (o.FieldStatus ?? FieldUnverified) +
                        "; resolved index " + Show(o.FieldIndex) + " vs expected ordinal " + o.ExpectedFieldOrdinal +
                        " (" + (o.FieldOrdinalStatus ?? FieldOrdinalUnexpected) + ")" +
                        "; declared in reader enum " + o.FieldDeclaredInEnum +
                        "; reader selection " + (o.ReaderSelectionMechanism ?? "none") +
                        " (" + (o.ReaderSelectionFactoryStatus ?? "none") + ")" +
                        "; identity " + (o.ElementIdentityStatus ?? IdentityUnverified) + "; read " + o.ReadConduitCount +
                        " of " + o.RequestedConduitCount + "." },
                new P16AzadeganOutputReaderClaim { Name = ClaimUnitVerification,
                    Status = o.UnitStatus ?? UnitUnverified,
                    Evidence = o.UnitEvidence ?? NoUnitMetadataEvidence },
                new P16AzadeganOutputReaderClaim { Name = ClaimTimeStepVerification,
                    Status = o.TimeStepStatus ?? TimeStepUnverified,
                    Evidence = "Time steps " + o.TimeStepCount + "; selected " + Show(o.SelectedTimeStepIndex) +
                        "; raw CalculationTime(0) " + (o.RawTimeAtIndexZero.HasValue
                            ? o.RawTimeAtIndexZero.Value.ToString("R", CultureInfo.InvariantCulture) : "<none>") +
                        "; basis " + (o.TimeStepSelectionBasis ?? "none") + "." },
                new P16AzadeganOutputReaderClaim { Name = ClaimFreshnessVerified,
                    Status = NotVerified, Evidence = FreshnessEvidence },
                new P16AzadeganOutputReaderClaim { Name = ClaimEngineeringAcceptance,
                    Status = NotMinted, Evidence = EngineeringAcceptanceEvidence }
            };
        }

        private static string Show(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "<none>";

        // ------------------------------------------------------------ receiving-side validation

        internal static void ValidateObservation(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Azadegan output-reader observation is absent.");
            P16AzadeganOutputReaderObservation o;
            try { o = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganOutputReaderObservation>(json); }
            catch (Exception error) { throw new InvalidDataException("Malformed Azadegan output-reader observation: " + error.GetType().Name); }
            if (o == null) throw new InvalidDataException("Azadegan output-reader observation is empty.");
            if (o.Claims == null || o.Claims.Length != ClaimNames.Length)
                throw new InvalidDataException("Azadegan output-reader observation must carry all separated claims.");
            foreach (string name in ClaimNames)
            {
                var matches = o.Claims.Where(c => c != null && c.Name == name).ToArray();
                if (matches.Length != 1 || string.IsNullOrEmpty(matches[0].Status) || string.IsNullOrEmpty(matches[0].Evidence))
                    throw new InvalidDataException("Azadegan output-reader claim is missing or unevidenced: " + name);
            }
            // Structural: this path can never assert freshness or engineering acceptance.
            if (o.FreshnessVerified) throw new InvalidDataException("Output-reader path cannot assert freshness.");
            if (o.EngineeringAcceptance) throw new InvalidDataException("Output-reader path cannot mint engineering acceptance.");
            if (o.Claims.Single(c => c.Name == ClaimFreshnessVerified).Status != NotVerified)
                throw new InvalidDataException("Freshness claim must remain NOT_VERIFIED.");
            if (o.Claims.Single(c => c.Name == ClaimEngineeringAcceptance).Status != NotMinted)
                throw new InvalidDataException("Engineering acceptance claim must remain NOT_MINTED.");
            // Structural: no model, solver, save or notification activity is permitted on this path.
            if (o.ModelOpened || o.ModelOpenInvocations != 0 || o.SolverRunInvocations != 0 ||
                o.SaveInvocations != 0 || o.NotificationApiInvocations != 0)
                throw new InvalidDataException("Output-reader path must not open a model, solve, save or use notification APIs.");
            if (o.RequestedElementId != ElementId)
                throw new InvalidDataException("Output-reader observation targets an unexpected element.");
            // The corrected contract: the SewerCAD field binding and a selection that is never
            // justified by the artifact's file-name suffix.
            if (string.IsNullOrEmpty(o.FieldName))
                throw new InvalidDataException("Output-reader observation must name the requested field.");
            if (string.IsNullOrEmpty(o.ReaderSelectionMechanism) || string.IsNullOrEmpty(o.ReaderSelectionBasis))
                throw new InvalidDataException("Output-reader observation must record how the reader was selected.");
            if (o.ReaderSelectionBasisUsesFileNameSuffix)
                throw new InvalidDataException("Reader selection must not be derived from the artifact file-name suffix.");
            if (o.ExpectedFieldOrdinal != ExpectedFieldOrdinal)
                throw new InvalidDataException("Output-reader observation carries an unexpected field ordinal.");
            if (o.FieldStatus == FieldSupported && o.FieldOrdinalStatus != FieldOrdinalConfirmed)
                throw new InvalidDataException("A supported field requires its resolved ordinal to be confirmed.");
            if (o.ExtractionStatus == ExtractionObserved && o.FieldOrdinalStatus != FieldOrdinalConfirmed)
                throw new InvalidDataException("An observed extraction requires a confirmed field ordinal.");
            if (o.ArtifactIntegrityStatus == ArtifactSourceModified && o.SourceArtifactUnchanged)
                throw new InvalidDataException("Artifact integrity status contradicts its own evidence.");
            if (o.UnitStatus == UnitVerified && string.IsNullOrEmpty(o.WorkingUnitSymbol))
                throw new InvalidDataException("A verified unit claim requires a unit symbol.");
            if (o.ExtractionStatus == ExtractionObserved && !o.CoverageComplete)
                throw new InvalidDataException("An observed extraction must cover every requested conduit.");
            if (o.ExtractionStatus == ExtractionObserved &&
                (o.Values == null || o.Values.Length != o.ReadConduitCount))
                throw new InvalidDataException("An observed extraction must carry one value per read conduit.");
            if (o.TimeStepStatus == TimeStepSwrcIndex0RecordOnly &&
                (o.TimeStepCount != 0 || o.TimeSeconds == null || o.TimeSeconds.Length != 0 ||
                 o.SelectedTimeStepIndex != 0 || !o.RawTimeAtIndexZero.HasValue ||
                 o.ReaderAssemblyNames == null ||
                 !o.ReaderAssemblyNames.Contains("Haestad.Calculations.SewerCAD.OutputReader") ||
                 o.TimeStepSelectionBasis != "SWRC_INDEX0_ONLY_FOOTER_COUNT_ZERO"))
                throw new InvalidDataException("SWRC index-0-only observation is missing its exact incomplete-time contract.");
        }

        // ------------------------------------------------------------ request validation

        internal static void ValidateRequest(P16AzadeganOutputReaderRequest request)
        {
            if (request == null || request.SchemaVersion != 1 || request.Operation != Operation ||
                !Regex.IsMatch(request.RunId ?? "", "^[a-f0-9]{32}$"))
                throw new InvalidDataException("Unsupported or incomplete output-reader request.");
            if (!Regex.IsMatch(request.CopyId ?? "", "^[a-f0-9]{32}$"))
                throw new InvalidDataException("Output-reader copy identity is not a controlled per-run identifier.");
            if (!Path.IsPathRooted(request.ReaderRoot ?? "") || !Directory.Exists(request.ReaderRoot))
                throw new InvalidDataException("Output-reader runtime root is unavailable.");
            WorkingCopyIntegrity.RejectLinks(request.ReaderRoot);
            string root = Path.GetFullPath(request.ReaderRoot).TrimEnd('\\');

            if (!Path.IsPathRooted(request.ArtifactPath ?? "") || !File.Exists(request.ArtifactPath))
                throw new InvalidDataException("Output-reader artifact copy is unavailable.");
            string artifact = Path.GetFullPath(request.ArtifactPath);
            if (!string.Equals(Path.GetFileName(artifact), ArtifactFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output-reader artifact name is not the declared store.");
            WorkingCopyIntegrity.RejectLinks(artifact);
            // The artifact must live in its own disposable directory, directly in the controlled
            // per-run boundary, and that directory must contain nothing else.
            string directory = Path.GetDirectoryName(artifact);
            RuntimeHostProtocol.ControlledDirectory(directory, "OutputReader");
            if (!string.Equals(Path.GetFileName(directory), request.CopyId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output-reader copy directory does not match its run identity.");
            var entries = Directory.GetFileSystemEntries(directory)
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            if (entries.Length != 1 || !string.Equals(entries[0], ArtifactFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output-reader directory must contain only the copied artifact.");
            if (!Regex.IsMatch(request.SourceArtifactSHA256 ?? "", "^[A-F0-9]{64}$"))
                throw new InvalidDataException("Output-reader source artifact digest is malformed.");
            if (string.Equals(Path.GetFullPath(request.SourceArtifactPath ?? ""), artifact, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output-reader source and copy must be distinct files.");
            if (!Path.IsPathRooted(request.SourceArtifactPath ?? "") || !File.Exists(request.SourceArtifactPath))
                throw new InvalidDataException("Output-reader source artifact is unavailable.");

            if (request.ReaderAssemblies == null || request.ReaderAssemblies.Length != ReaderAssemblyNames.Length ||
                !ReaderAssemblyNames.All(name => request.ReaderAssemblies.Count(a => a != null && a.SimpleName == name) == 1))
                throw new InvalidDataException("Exactly the declared output-reader assemblies are required.");
            foreach (var assembly in request.ReaderAssemblies)
            {
                string expected = Path.Combine(root, assembly.SimpleName + ".dll");
                if (!string.Equals(Path.GetFullPath(assembly.Path ?? ""), expected, StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(assembly.SHA256 ?? "", "^[A-F0-9]{64}$"))
                    throw new InvalidDataException("Output-reader assembly escaped the selected runtime.");
                WorkingCopyIntegrity.RejectLinks(assembly.Path);
            }
            if (request.Diagnostics == null || request.Diagnostics.Count != 2 ||
                !request.Diagnostics.ContainsKey("P16AzadeganScenarioId") ||
                request.Diagnostics["P16AzadeganScenarioId"] != ScenarioId.ToString(CultureInfo.InvariantCulture) ||
                !request.Diagnostics.ContainsKey("P16AzadeganElementId") ||
                request.Diagnostics["P16AzadeganElementId"] != ElementId.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Output-reader request must carry the exact bounded scenario/element context.");
        }

        // ------------------------------------------------------------ host-side execution

        // Entry point for the dedicated launch switch. It never enters the model lifecycle.
        internal static int Run(string requestPath, string resultPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(requestPath));
                RuntimeHostProtocol.ControlledDirectory(directory, "RuntimeHost");
                if (Path.GetFileName(requestPath) != "request.json" ||
                    Path.GetFullPath(resultPath) != Path.Combine(directory, "result.json") || File.Exists(resultPath))
                    throw new InvalidDataException("Unexpected one-shot IPC paths.");
                Environment.SetEnvironmentVariable(RuntimeStageJournal.EnvironmentName, Path.Combine(directory, "stages.jsonl"));
                RuntimeStageJournal.Call("HostStarted", () => { });
                var request = RuntimeHostProtocol.Serializer()
                    .Deserialize<P16AzadeganOutputReaderRequest>(File.ReadAllText(requestPath));
                ValidateRequest(request);
                RuntimeStageJournal.Call("RequestValidated", () => { });
                var result = Execute(request);
                RuntimeStageJournal.Call("ResultWriteStarted", () => RuntimeHostProtocol.AtomicWrite(resultPath, result));
                RuntimeStageJournal.Call("ResultCommitted", () => { });
                return result.Success ? 0 : 1;
            }
            catch (Exception error)
            {
                RuntimeStageJournal.Record("HostFailure", "EXCEPTION", null, error.ToString());
                return 1;
            }
        }

        private static P16AzadeganOutputReaderResult Execute(P16AzadeganOutputReaderRequest request)
        {
            var o = new P16AzadeganOutputReaderObservation
            {
                RunId = request.RunId,
                CopyId = request.CopyId,
                ArtifactFileName = ArtifactFileName,
                ArtifactPath = request.ArtifactPath,
                ArtifactDirectory = Path.GetDirectoryName(request.ArtifactPath),
                SourceArtifactPath = request.SourceArtifactPath,
                SourceArtifactSHA256Before = request.SourceArtifactSHA256,
                ReaderAssemblyRoot = request.ReaderRoot,
                ReaderTypeName = ReaderTypeName,
                ReaderAssemblyNames = request.ReaderAssemblies.Select(a => a.SimpleName).ToArray(),
                ReaderAssemblySHA256 = request.ReaderAssemblies.Select(a => a.SHA256).ToArray(),
                ExpectedMagicNumber = ExpectedMagicNumber,
                ExpectedConduitCount = ExpectedConduitCount,
                RequestedElementId = ElementId,
                FieldName = RequestedFieldName,
                ExpectedFieldOrdinal = ExpectedFieldOrdinal,
                // Defaults are the honest state before selection runs; SelectReader refines them.
                ReaderSelectionMechanism = SelectionStaticBinding,
                ReaderSelectionFactoryTypeName = ReaderSelectionFactoryTypeName,
                ReaderSelectionFactoryStatus = SelectionFactoryContractUnavailable,
                ReaderSelectionBasis = SelectionBasisEvidence,
                ReaderSelectionBasisUsesFileNameSuffix = false,
                UnitEvidence = NoUnitMetadataEvidence,
                UnitStatus = UnitUnverified,
                WorkingUnitSymbol = null,
                FreshnessVerified = false,
                EngineeringAcceptance = false,
                ModelOpened = false, ModelOpenInvocations = 0, SolverRunInvocations = 0,
                SaveInvocations = 0, NotificationApiInvocations = 0
            };
            var result = new P16AzadeganOutputReaderResult
            {
                SchemaVersion = 1, RunId = request.RunId, Operation = Operation, ResultCommitted = true
            };
            var loaded = new List<HostMetadata>();
            object reader = null;
            Type readerType = null;
            Type baseType = null;
            try
            {
                o.CopyArtifactSHA256Before = WorkingCopyIntegrity.Fingerprint(request.ArtifactPath).SHA256;
                o.ArtifactBytes = new FileInfo(request.ArtifactPath).Length;
                o.ArtifactDirectoryEntries = Directory.GetFiles(o.ArtifactDirectory)
                    .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                o.ArtifactDirectoryIsolated = o.ArtifactDirectoryEntries.Length == 1;

                foreach (string name in ReaderAssemblyNames)
                    LoadReaderAssembly(request.ReaderRoot, name, loaded);
                o.ReaderAssemblySHA256 = loaded.Select(a => a.SHA256).ToArray();
                result.LoadedReaderAssemblies = loaded.ToArray();

                readerType = Contract(ReaderAssemblyNames[2], ReaderTypeName);
                baseType = Contract(ReaderAssemblyNames[1], BaseReaderTypeName);
                if (!baseType.IsAssignableFrom(readerType))
                    throw new InvalidOperationException("Installed reader does not derive from its declared base contract.");
                SelectReader(o, readerType, baseType);
                reader = RuntimeStageJournal.Call("P16AzReader:Construct",
                    () => Activator.CreateInstance(readerType), value => value == null ? "<null>" : value.GetType().FullName);
                o.ReaderConstructed = true;

                Invoke(reader, baseType, "Open", typeof(void), new[] { typeof(string) }, request.ArtifactPath);
                o.OpenInvoked = true;
                o.IsOpen = (bool)Invoke(reader, baseType, "IsOpen", typeof(bool), Type.EmptyTypes);
                o.ReaderIsResultValid = (bool)Get(reader, baseType, "IsResultValid", typeof(bool));

                ReadHeader(reader, baseType, o);
                ReadFooter(reader, baseType, o);
                ReadTimeSteps(reader, baseType, o);
                ResolveField(reader, readerType, baseType, o);
                ReadConduits(reader, baseType, o);
            }
            catch (Exception error)
            {
                var inner = Unwrap(error);
                o.ErrorType = inner.GetType().FullName;
                o.ErrorMessage = inner.Message;
                o.FailureOperation = LastOperation;
                RuntimeStageJournal.Record("OutputReaderFailure", "EXCEPTION", new { Operation = LastOperation }, inner.ToString());
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        if (baseType != null) Invoke(reader, baseType, "Close", typeof(void), Type.EmptyTypes);
                        o.CloseInvoked = true;
                    }
                    catch (Exception error) { o.CloseError = Unwrap(error).Message; }
                    try
                    {
                        var disposable = reader as IDisposable;
                        if (disposable != null) disposable.Dispose();
                    }
                    catch (Exception error) { o.CloseError = (o.CloseError ?? "") + " dispose: " + Unwrap(error).Message; }
                }
                try
                {
                    o.CopyArtifactSHA256After = WorkingCopyIntegrity.Fingerprint(request.ArtifactPath).SHA256;
                    o.CopyArtifactUnchanged = o.CopyArtifactSHA256Before == o.CopyArtifactSHA256After;
                    o.SourceArtifactSHA256After = File.Exists(request.SourceArtifactPath)
                        ? WorkingCopyIntegrity.Fingerprint(request.SourceArtifactPath).SHA256 : null;
                    o.SourceArtifactUnchanged = o.SourceArtifactSHA256Before != null &&
                        o.SourceArtifactSHA256Before == o.SourceArtifactSHA256After;
                }
                catch (Exception error) { o.ErrorMessage = (o.ErrorMessage ?? "") + " rehash: " + Unwrap(error).Message; }

                o.ArtifactIntegrityStatus = ArtifactIntegrityStatus(o.SourceArtifactUnchanged,
                    o.CopyArtifactUnchanged, o.ReaderIsResultValid, o.MagicNumber, ExpectedMagicNumber);
                o.ScenarioProvenanceStatus = ProvenanceUnverified;
                if (o.FieldStatus == null) o.FieldStatus = FieldUnverified;
                if (o.FieldOrdinalStatus == null) o.FieldOrdinalStatus = FieldOrdinalUnexpected;
                if (o.TimeStepStatus == null) o.TimeStepStatus = TimeStepUnverified;
                if (o.ElementIdentityStatus == null) o.ElementIdentityStatus = IdentityUnverified;
                o.CoverageComplete = o.RequestedConduitCount > 0 && o.ReadConduitCount == o.RequestedConduitCount;
                o.ExtractionStatus = ExtractionStatus(o.FieldStatus, o.ElementIdentityStatus,
                    o.RequestedConduitCount, o.ReadConduitCount);
                o.UnitStatus = UnitStatus(o.WorkingUnitSymbol, o.UnitEvidence);
                o.Claims = BuildClaims(o);
                o.Status = ClassifyOutcome(o);
                o.FailureClassification = o.Status == PassStatus ? null : o.Status;
            }

            // Success means the operation ran to completion and produced a self-consistent
            // observation. It is not a pass: the claims carry every trust decision.
            result.Success = o.Status == PassStatus || o.Status == PartialStatus;
            result.Status = o.Status;
            result.FailureClassification = o.FailureClassification;
            result.FailureMessageSafe = o.ErrorMessage;
            result.ObservationJson = RuntimeHostProtocol.Serializer().Serialize(o);
            result.Diagnostics[EvidenceKey] = result.ObservationJson;
            result.Diagnostics[StatusKey] = o.Status;
            result.Diagnostics["P16AzadeganArtifactIntegrity"] = o.ArtifactIntegrityStatus;
            result.Diagnostics["P16AzadeganScenarioProvenance"] = o.ScenarioProvenanceStatus;
            result.Diagnostics["P16AzadeganResultExtraction"] = o.ExtractionStatus;
            result.Diagnostics["P16AzadeganUnitVerification"] = o.UnitStatus;
            result.Diagnostics["P16AzadeganTimeStepVerification"] = o.TimeStepStatus;
            result.Diagnostics["P16AzadeganFreshnessVerified"] = "false";
            result.Diagnostics["P16AzadeganEngineeringAcceptance"] = "false";
            if (o.Status == PassStatus) result.Diagnostics[PassFlag] = "true";
            return result;
        }

        // The pass status asserts extraction mechanics only. A read that produced values while
        // any non-structural trust claim is still open is reported as a partial observation.
        internal static string ClassifyOutcome(P16AzadeganOutputReaderObservation o)
        {
            if (o == null) return BlockedFailureCode;
            if (o.ArtifactIntegrityStatus != ArtifactVerified) return o.ArtifactIntegrityStatus;
            if (o.ExtractionStatus != ExtractionObserved) return o.ExtractionStatus;
            // The SWRC reader's installed IL has a dedicated index-0 record path but exposes no
            // enumerable timestep count. Values may be captured for diagnosis, never accepted.
            if (o.TimeStepStatus == TimeStepSwrcIndex0RecordOnly) return PartialStatus;
            if (o.TimeStepStatus != TimeStepSingleSelected && o.TimeStepStatus != TimeStepTargetSelected)
                return o.TimeStepStatus;
            if (o.UnitStatus != UnitVerified) return PartialStatus;
            return PassStatus;
        }

        // ------------------------------------------------------------ vendor surface

        private static Type Contract(string assembly, string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == assembly).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Exact loaded reader assembly unavailable: " + assembly);
            return matches[0].GetType(name, true, false);
        }

        // Reader selection. The authoritative selector is
        // AlabamaOutputReaderWrapper.GetOutputReaderFor(int), whose constructor requires an
        // IDomainDataSet. This path opens no model and therefore does not own that dataset; the
        // factory is contract-verified and deliberately NOT invoked. The static binding is based
        // on the installed SewerCAD header contract, never the .out file-name suffix.
        private static void SelectReader(P16AzadeganOutputReaderObservation o, Type readerType, Type baseType)
        {
            o.ReaderSelectionMechanism = SelectionStaticBinding;
            o.ReaderSelectionBasisUsesFileNameSuffix = false;
            o.ReaderSelectionBasis = SelectionBasisEvidence;
            if (!baseType.IsAssignableFrom(readerType))
            {
                o.ReaderSelectionFactoryStatus = SelectionFactoryContractUnavailable;
                o.ReaderSelectionBasis = SelectionBasisEvidence +
                    " Reader type does not derive from the declared base contract.";
                return;
            }
            try
            {
                var factory = Contract(ReaderAssemblyNames[2], ReaderSelectionFactoryTypeName);
                var method = factory.GetMethod(ReaderSelectionFactoryMethod,
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (method == null || method.ReturnType != baseType)
                {
                    o.ReaderSelectionFactoryStatus = SelectionFactoryContractUnavailable;
                    o.ReaderSelectionBasis = SelectionBasisEvidence +
                        " Factory contract did not expose GetOutputReaderFor(int) returning the base reader contract.";
                    return;
                }
                o.ReaderSelectionFactoryStatus = SelectionFactoryVerifiedNotInvoked;
                RuntimeStageJournal.Record("ReaderSelection", "STATIC_BINDING",
                    new { Factory = ReaderSelectionFactoryTypeName, Method = ReaderSelectionFactoryMethod },
                    "Factory contract verified; not invoked because it requires an IDomainDataSet.");
            }
            catch (Exception error)
            {
                o.ReaderSelectionFactoryStatus = SelectionFactoryContractUnavailable;
                o.ReaderSelectionBasis = SelectionBasisEvidence +
                    " Factory contract unavailable: " + Unwrap(error).Message;
            }
        }

        private static void LoadReaderAssembly(string root, string simpleName, List<HostMetadata> loaded)
        {
            string path = Path.Combine(Path.GetFullPath(root), simpleName + ".dll");
            if (!File.Exists(path)) throw new FileNotFoundException("Declared output-reader assembly is absent: " + path);
            WorkingCopyIntegrity.RejectLinks(path);
            LastOperation = "Assembly.LoadFrom:" + simpleName;
            var assembly = RuntimeStageJournal.Call("P16AzReader:Load:" + simpleName,
                () => Assembly.LoadFrom(path), value => value.GetName().Name);
            loaded.Add(new HostMetadata
            {
                Path = path,
                SimpleName = simpleName,
                SHA256 = WorkingCopyIntegrity.Fingerprint(path).SHA256,
                AssemblyVersion = assembly.GetName().Version.ToString(),
                ImageRuntimeVersion = assembly.ImageRuntimeVersion
            });
        }

        private static object Invoke(object target, Type contract, string name, Type returns, Type[] parameters,
            params object[] values)
        {
            LastOperation = contract.FullName + "." + name;
            if (target == null || !contract.IsInstanceOfType(target))
                throw new InvalidOperationException("Reader contract instance unavailable: " + LastOperation);
            var method = contract.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
            if (method == null || method.ReturnType != returns || method.ContainsGenericParameters)
                throw new MissingMethodException("Exact reader signature unavailable: " + LastOperation);
            return RuntimeStageJournal.Call("P16AzReader:" + LastOperation,
                () => method.Invoke(target, values),
                value => value is bool || value is int || value is double ? value : value == null ? "<null>" : value.GetType().FullName);
        }

        private static object Get(object target, Type contract, string name, Type returns) =>
            Invoke(target, contract, "get_" + name, returns, Type.EmptyTypes);

        private static Exception Unwrap(Exception error)
        {
            var invocation = error as TargetInvocationException;
            return invocation != null && invocation.InnerException != null ? invocation.InnerException : error;
        }

        private static void ReadHeader(object reader, Type baseType, P16AzadeganOutputReaderObservation o)
        {
            var headerType = Contract(ReaderAssemblyNames[1], HeaderTypeName);
            object header = Get(reader, baseType, "Header", headerType);
            if (header == null) throw new InvalidOperationException("Reader header is unavailable.");
            o.MagicNumber = (int)Invoke(header, headerType, "get_MagicNumber", typeof(int), Type.EmptyTypes);
            o.HeaderNumberOfConduits = (int)Invoke(header, headerType, "get_NumberOfConduits", typeof(int), Type.EmptyTypes);
            o.HeaderNumberOfGravityNodes = (int)Invoke(header, headerType, "get_NumberOfGravityNodes", typeof(int), Type.EmptyTypes);
            object map = Invoke(header, headerType, "get_ConduitIdToIndexMap", typeof(IDictionary), Type.EmptyTypes);
            var dictionary = map as IDictionary;
            if (dictionary == null) throw new InvalidOperationException("Reader conduit id map is not an IDictionary.");
            if (dictionary.Count > MaxConduits) throw new InvalidDataException("Reader conduit count exceeds its bound.");
            var entries = new List<KeyValuePair<int, int>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!(entry.Key is int) || !(entry.Value is int))
                    throw new InvalidOperationException("Reader conduit id/index map must contain Int32 keys and values.");
                int id = (int)entry.Key;
                int index = (int)entry.Value;
                if (index < 0 || index >= dictionary.Count)
                    throw new InvalidDataException("Reader conduit index is outside the declared map.");
                entries.Add(new KeyValuePair<int, int>(id, index));
                if (id == ElementId) o.ElementConduitIndex = index;
            }
            var orderedIndices = entries.Select(e => e.Value).OrderBy(i => i).ToArray();
            if (!orderedIndices.SequenceEqual(Enumerable.Range(0, entries.Count)))
                throw new InvalidDataException("Reader conduit map indices are not a unique contiguous range.");
            entries.Sort((left, right) => left.Value.CompareTo(right.Value));
            var ids = entries.Select(e => e.Key).ToArray();
            o.ConduitIds = ids;
            o.ConduitIdCount = ids.Length;
            o.DistinctConduitIdCount = ids.Distinct().Count();
            o.ElementPresentInMap = ids.Contains(ElementId);
            o.ElementIdentityStatus = ElementIdentityStatus(ElementId, o.ElementPresentInMap,
                o.ConduitIdCount, o.DistinctConduitIdCount, ExpectedConduitCount);
        }

        private static void ReadFooter(object reader, Type baseType, P16AzadeganOutputReaderObservation o)
        {
            var footerType = Contract(ReaderAssemblyNames[1], FooterTypeName);
            object footer = Get(reader, baseType, "Footer", footerType);
            if (footer == null) throw new InvalidOperationException("Reader footer is unavailable.");
            o.FooterMagicNumber = (int)Invoke(footer, footerType, "get_FooterMagicNumber", typeof(int), Type.EmptyTypes);
            o.FooterIsResultValid = (bool)Invoke(footer, footerType, "get_IsResultValid", typeof(bool), Type.EmptyTypes);
        }

        private static void ReadTimeSteps(object reader, Type baseType, P16AzadeganOutputReaderObservation o)
        {
            var dynamicType = Contract(ReaderAssemblyNames[1], DynamicTypeName);
            object section = Get(reader, baseType, "Dynamic", dynamicType);
            if (section == null) throw new InvalidOperationException("Reader time-step section is unavailable.");

            // The installed SewerCAD SWRC reader has a specific index-0 record layout, but its
            // inherited NumberOfTimeStepsInSection getter is a stub returning 0 and its footer
            // reports NumberOfCalculationTimeSteps=0. IL confirms ResultStartPositionOfOneTimeStep(0)
            // returns DynamicOffset+4 while any nonzero index throws "not yet implemented for SWRC".
            // Preserve that as a raw record only; do not relabel it as a verified time step.
            var footerType = Contract(ReaderAssemblyNames[1], FooterTypeName);
            var footerReaderType = Contract(ReaderAssemblyNames[1], FooterReaderTypeName);
            object footer = Get(reader, baseType, "Footer", footerType);
            if (!footerReaderType.IsInstanceOfType(footer))
                throw new InvalidOperationException("Installed SewerCAD footer does not match its concrete count contract.");
            int count = (int)Invoke(footer, footerReaderType, "get_NumberOfCalculationTimeSteps",
                typeof(int), Type.EmptyTypes);
            o.TimeStepCount = count;
            if (count < 0 || count > MaxTimeSteps)
                throw new InvalidDataException("Reader time-step count exceeds its bound.");
            if (count == 0)
            {
                o.RawTimeAtIndexZero = (double)Invoke(section, dynamicType, "CalculationTime",
                    typeof(double), new[] { typeof(int) }, 0);
                o.TimeSeconds = new double[0];
                o.SelectedTimeStepIndex = 0;
                o.TimeStepSelectionBasis = "SWRC_INDEX0_ONLY_FOOTER_COUNT_ZERO";
                o.TimeStepStatus = TimeStepSwrcIndex0RecordOnly;
                return;
            }

            var times = new List<double>(count);
            for (int index = 0; index < count; index++)
                times.Add((double)Invoke(section, dynamicType, "CalculationTime", typeof(double), new[] { typeof(int) }, index));
            o.TimeSeconds = times.ToArray();
            // Selection is by cardinality or by value, never by assuming index 0.
            if (count == 1)
            {
                o.SelectedTimeStepIndex = 0;
                o.TimeStepSelectionBasis = "SOLE_TIME_STEP";
            }
            else
            {
                var matches = Enumerable.Range(0, count).Where(i => times[i] == 0d).ToArray();
                if (matches.Length == 1)
                {
                    o.SelectedTimeStepIndex = matches[0];
                    o.TimeStepSelectionBasis = "TARGET_TIME_ZERO_PRESENT_ONCE";
                }
                else
                {
                    o.SelectedTimeStepIndex = null;
                    o.TimeStepSelectionBasis = matches.Length == 0 ? "NO_TARGET_TIME_ZERO" : "TARGET_TIME_ZERO_AMBIGUOUS";
                }
            }
            o.TimeStepStatus = TimeStepStatus(count, o.TimeSeconds, o.SelectedTimeStepIndex);
        }

        private static void ResolveField(object reader, Type readerType, Type baseType,
            P16AzadeganOutputReaderObservation o)
        {
            var elementResultType = Contract(ReaderAssemblyNames[1], ElementResultTypeName);
            object conduit = Enum.Parse(elementResultType, "Conduit");
            try
            {
                object index = Invoke(reader, readerType, "CalculateResultFieldIndex", typeof(int),
                    new[] { elementResultType, typeof(string) }, conduit, RequestedFieldName);
                o.FieldIndex = (int)index;
                o.FieldStatus = FieldStatus(o.FieldIndex, null);
                if (o.FieldStatus != FieldSupported) o.FieldFailure = "Reader returned a negative field index.";
            }
            catch (Exception error)
            {
                var inner = Unwrap(error);
                o.FieldIndex = null;
                o.FieldFailure = inner.GetType().FullName + ": " + inner.Message;
                o.FieldStatus = FieldUnsupported;
                // Recorded, not repaired: the requested field is unavailable and no other field
                // is substituted for it.
                RuntimeStageJournal.Record("FieldResolutionFailed", "UNSUPPORTED",
                    new { Field = RequestedFieldName }, o.FieldFailure);
            }
            o.SupportedFieldNames = SupportedFieldNames();
            o.FieldDeclaredInEnum = o.SupportedFieldNames.Contains(ExpectedFieldEnumLiteralName);
            // The resolved index must be the documented ordinal. A mismatch means the installed
            // vocabulary is not the verified one, so the field stops being usable for extraction.
            o.FieldOrdinalStatus = FieldOrdinalStatus(o.FieldIndex, ExpectedFieldOrdinal);
            if (o.FieldStatus == FieldSupported && o.FieldOrdinalStatus != FieldOrdinalConfirmed)
            {
                o.FieldStatus = FieldOrdinalUnexpected;
                o.FieldFailure = "Resolved field index " + Show(o.FieldIndex) +
                    " does not match the documented ordinal " + ExpectedFieldOrdinal + ".";
            }
            if (o.FieldStatus == FieldSupported)
            {
                try
                {
                    o.FieldIsExtendedResult = (bool)Invoke(reader, baseType, "IsExtendedResult", typeof(bool),
                        new[] { typeof(string) }, RequestedFieldName);
                }
                catch (Exception error)
                {
                    o.FieldIsExtendedResult = null;
                    o.FieldFailure = Unwrap(error).Message;
                }
            }
        }

        // The declared conduit field literals of this reader's own enum, enumerated so the exact
        // supported set is on record. An absent enum yields an empty set rather than a guess.
        internal static string[] SupportedFieldNames()
        {
            try
            {
                var fieldEnum = Contract(ReaderAssemblyNames[2], FieldEnumTypeName);
                var names = Enum.GetNames(fieldEnum);
                return names.Length > MaxFields ? new string[0] : names;
            }
            catch (Exception) { return new string[0]; }
        }

        private static void ReadConduits(object reader, Type baseType, P16AzadeganOutputReaderObservation o)
        {
            o.RequestedConduitCount = o.ConduitIdCount;
            if (o.FieldStatus != FieldSupported || !o.SelectedTimeStepIndex.HasValue ||
                o.ElementIdentityStatus != IdentityVerified) return;

            var hmIdType = Contract(ReaderAssemblyNames[0], HmIdCollectionTypeName);
            var dictionaryType = Contract(ReaderAssemblyNames[0], IdToDoubleDictionaryTypeName);
            var elementResultType = Contract(ReaderAssemblyNames[1], ElementResultTypeName);
            object conduit = Enum.Parse(elementResultType, "Conduit");
            object ids = Activator.CreateInstance(hmIdType, new object[] { o.ConduitIds });
            // The trailing flag is the reader's own extended-result discriminator, resolved from
            // the reader rather than hardcoded.
            bool extended = o.FieldIsExtendedResult ?? false;
            object map = Invoke(reader, baseType, "GetDoubleResultAt", dictionaryType,
                new[] { typeof(int), elementResultType, hmIdType, typeof(string), typeof(bool) },
                o.SelectedTimeStepIndex.Value, conduit, ids, RequestedFieldName, extended);
            if (map == null) throw new InvalidOperationException("Reader returned no id-keyed value map.");

            var contains = dictionaryType.GetMethod("Contains", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(int) }, null);
            var item = dictionaryType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(int) }, null);
            if (contains == null || contains.ReturnType != typeof(bool) || item == null || item.ReturnType != typeof(double))
                throw new MissingMethodException("Exact reader id-keyed accessors unavailable.");

            var values = new List<P16AzadeganOutputReaderValue>(o.ConduitIdCount);
            foreach (int id in o.ConduitIds)
            {
                if (!(bool)contains.Invoke(map, new object[] { id })) continue;
                double value = (double)item.Invoke(map, new object[] { id });
                values.Add(new P16AzadeganOutputReaderValue
                {
                    ElementId = id,
                    ConduitIndex = Array.IndexOf(o.ConduitIds, id),
                    RawValue = value.ToString("R", CultureInfo.InvariantCulture),
                    ClrType = "System.Double",
                    Classification = double.IsNaN(value) || double.IsInfinity(value)
                        ? "NON_FINITE_DOUBLE" : "FINITE_DOUBLE_UNIT_UNVERIFIED"
                });
            }
            o.Values = values.ToArray();
            o.ReadConduitCount = values.Count;
        }

        // ------------------------------------------------------------ receiving-side result

        internal static void ParseResult(string path, P16AzadeganOutputReaderRequest request)
        {
            if (!File.Exists(path)) throw new InvalidDataException("Output-reader result is missing.");
            WorkingCopyIntegrity.RejectLinks(path);
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Output-reader result exceeds its bound.");
            P16AzadeganOutputReaderResult result;
            try { result = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganOutputReaderResult>(File.ReadAllText(path)); }
            catch (Exception error) { throw new InvalidDataException("Malformed output-reader result: " + error.GetType().Name); }
            if (result == null || result.SchemaVersion != 1 || result.RunId != request.RunId ||
                result.Operation != Operation || !result.ResultCommitted || result.Diagnostics == null)
                throw new InvalidDataException("Unsupported, mismatched or uncommitted output-reader result.");
            if (result.ModelOpenInvocations != 0 || result.SolverRunInvocations != 0 ||
                result.SaveInvocations != 0 || result.NotificationApiInvocations != 0)
                throw new InvalidDataException("Output-reader result reports forbidden model, solver, save or notification activity.");
            string json;
            if (!result.Diagnostics.TryGetValue(EvidenceKey, out json))
                throw new InvalidDataException("Output-reader observation is missing.");
            ValidateObservation(json);
            string status;
            if (!result.Diagnostics.TryGetValue(StatusKey, out status) || status != result.Status)
                throw new InvalidDataException("Output-reader status is missing or inconsistent.");
            if (result.Success && status != PassStatus && status != PartialStatus)
                throw new InvalidDataException("Output-reader success carries an inconsistent status.");
            if (status == PassStatus && (!result.Diagnostics.ContainsKey(PassFlag) || result.Diagnostics[PassFlag] != "true"))
                throw new InvalidDataException("Output-reader pass did not carry the extraction pass flag.");
        }
    }
}
