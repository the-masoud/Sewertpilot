using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static P16AzadeganObservation AzadeganPassObservation()
        {
            return new P16AzadeganObservation
            {
                Status = P16AzadeganReadOnlyResultHost.PassStatus,
                OpenedModelPath = @"C:\temp\ReadOnly\copy\Azadegab ssw 26.10.25.stsw",
                DataSetId = 1,
                DataSetGuid = "00000000-0000-0000-0000-000000000001",
                ActiveScenarioBefore = 353,
                ActiveScenarioAfter = 353,
                RequestedScenarioId = 353,
                CurrentPhysicalAlternative = 352,
                CurrentCalculationOptions = 29,
                Engine = "DelawareProductEngine",
                ResultManagerEngineName = "DelawareProductEngine",
                ResultManagerScenarioId = 353,
                ResultManagerAvailable = true,
                ResultManagerWasActive = false,
                HasResults = true,
                FlowFieldSupported = true,
                SupportedFieldCount = 2,
                SupportedFieldNames = new[] { "BasicFlowResults_Flow", "BasicFlowResults_Velocity" },
                FieldRuntimeType = "Haestad.Domain.ResultField",
                FieldName = "BasicFlowResults_Flow",
                RecordName = "BasicFlowResults",
                FieldHasResults = true,
                RequestedElementLabel = "SSW-001",
                RequestedElementId = 119,
                ObservedElementLabel = "SSW-001",
                ObservedElementId = 119,
                LabelMatchCount = 1,
                ElementIdentityVerified = true,
                LabelEvidence = "Haestad.Domain.IModelingElement.Label",
                TimeStepIndex = 0,
                TimeSeconds = 0d,
                TimeStepCount = 3,
                AvailableTimeSeconds = new[] { 0d, 60d, 120d },
                WorkingUnitSymbol = "L/s",
                WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                StorageUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47",
                UnitEvidence = "Native IUnitizedField working-unit enum literal only.",
                ValueClrType = "System.Double",
                RawValue = "0.757082",
                RawValueClassification = "FINITE_DOUBLE_SENTINEL_SEMANTICS_UNVERIFIED",
                FlowReadInvocations = 1,
                FreshnessVerified = false,
                FreshnessEvidence = "Pre-existing project state; no calculation lineage verified.",
                ResultCleanupCompleted = true,
                CleanupError = null,
                FailureOperation = null,
                ErrorType = null,
                ErrorHResult = null,
                ErrorMessage = null
            };
        }

        private static string AzadeganJson(P16AzadeganObservation observation) =>
            RuntimeHostProtocol.Serializer().Serialize(observation);

        // A complete, exact read-only observation is accepted by the receiving-side validator.
        private static void P16Azadegan_ExactObservationAccepted()
        {
            P16AzadeganReadOnlyResultHost.ValidateObservation(AzadeganJson(AzadeganPassObservation()));
            Check(true, "Exact pass observation accepted.");
        }

        private static void RejectAzadeganObservation(P16AzadeganObservation observation)
        {
            string json = AzadeganJson(observation);
            Throws<InvalidDataException>(() => P16AzadeganReadOnlyResultHost.ValidateObservation(json));
        }

        private static P16AzadeganObservation AzadeganVariant()
        {
            return AzadeganPassObservation();
        }

        // No substitute engine, element, field, timestep or unit can be relabelled as a pass.
        private static void P16Azadegan_ObservationRejectsSubstitutions()
        {
            var o = AzadeganVariant(); o.Engine = "CivilStormDynamicEngine"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ResultManagerEngineName = "CivilStormDynamicEngine"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ResultManagerScenarioId = 359; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.CurrentPhysicalAlternative = 351; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.CurrentCalculationOptions = 24; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ActiveScenarioAfter = 359; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ObservedElementId = 120; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ObservedElementLabel = "SSW-002"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.LabelMatchCount = 2; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ElementIdentityVerified = false; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FieldName = "BasicFlowResults_Velocity"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.RecordName = "BasicResults"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.TimeStepIndex = 1; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.TimeSeconds = 60d; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.WorkingUnitSymbol = "ft3/s"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.WorkingUnitIdentity = "Haestad.Support.Units.UnitConversionManager+UnitIndex:UnknownFlowUnit:999"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ValueClrType = "System.String"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.RawValue = "NaN"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FlowReadInvocations = 0; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FlowReadInvocations = 2; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FreshnessVerified = true; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.ResultCleanupCompleted = false; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.CleanupError = "close failed"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.Status = "VALUE_OBSERVED_UNTRUSTED"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FailureOperation = "Haestad.Domain.IScenario.ResultManager"; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.HasResults = false; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FieldHasResults = false; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.FlowFieldSupported = false; RejectAzadeganObservation(o);
            o = AzadeganVariant(); o.AvailableTimeSeconds = new[] { 60d, 120d }; o.TimeStepCount = 2; o.TimeStepIndex = 0; o.TimeSeconds = 60d; RejectAzadeganObservation(o);
        }

        // A timestep vector without the requested 0 s time is reported, never index-inferred.
        private static void P16Azadegan_TimeVectorWithoutZeroIsNotInferred()
        {
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new[] { 60d, 120d }));
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new double[0]));
            Check(P16CalculatedFlowDiagnostic.SelectTime(new[] { 0d, 60d }) == 0, "Zero second timestep resolved by value.");
            Throws<InvalidDataException>(() => P16CalculatedFlowDiagnostic.SelectTime(new[] { -60d, 0d }));
        }

        // The unit symbol is a label for an exact enum literal only; unknown literals stay unverified.
        private static void P16Azadegan_UnitSymbolsAreLiteralBound()
        {
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("LitersPerSecond") == "L/s", "LitersPerSecond labelled L/s.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("CubicMetersPerSecond") == "m3/s", "CubicMetersPerSecond labelled m3/s.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("CubicFeetPerSecond") == "ft3/s", "CubicFeetPerSecond labelled ft3/s.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("GallonsPerMinute") == "gal/min", "GallonsPerMinute labelled gal/min.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("MillionGallonsPerDay") == null, "Unverified literal stays unmapped.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol("SomethingElse") == null, "Unknown literal stays unmapped.");
            Check(P16AzadeganReadOnlyResultHost.FlowUnitSymbol(null) == null, "Absent literal stays unmapped.");
            Check(P16AzadeganReadOnlyResultHost.UnitEnumName(
                "Haestad.Support.Units.UnitConversionManager+UnitIndex:LitersPerSecond:47") == "LitersPerSecond", "Unit enum name parsed from identity.");
            Check(P16AzadeganReadOnlyResultHost.UnitEnumName("malformed") == null, "Malformed identity has no enum name.");
        }

        private static string CreateAzadeganBoundaryCopy()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SewerPilot", "OpenFlows", "ReadOnly", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            foreach (string name in P16AzadeganReadOnlyResultHost.Family)
                File.WriteAllText(Path.Combine(directory, name), "boundary:" + name);
            return directory;
        }

        private static HostRequest AzadeganRequest(string directory, string digest)
        {
            string copyId = Path.GetFileName(directory);
            return new HostRequest
            {
                SchemaVersion = 1,
                RunId = Guid.NewGuid().ToString("N"),
                Operation = P16AzadeganReadOnlyResultHost.Operation,
                WorkingModelPath = Path.Combine(directory, P16AzadeganReadOnlyResultHost.ModelFileName),
                WorkingCopyId = copyId,
                RuntimeCandidateCount = 1,
                DetectedArchitecture = "x64",
                SelectedRuntime = directory,
                CoreFiles = new[]
                {
                    new HostMetadata { SimpleName = "Haestad.Support", Path = Path.Combine(directory, "Haestad.Support.dll"), SHA256 = new string('A', 64) },
                    new HostMetadata { SimpleName = "Haestad.Domain", Path = Path.Combine(directory, "Haestad.Domain.dll"), SHA256 = new string('B', 64) }
                },
                Diagnostics = P16AzadeganReadOnlyResultHost.Context(copyId, digest)
            };
        }

        // Request validation binds the attempt to one controlled copy, one inventory and one context.
        private static void P16Azadegan_RequestValidationBindsCopyInventoryAndContext()
        {
            string directory = CreateAzadeganBoundaryCopy();
            try
            {
                string digest = P16AzadeganReadOnlyResultHost.InventoryDigest(directory);
                var good = AzadeganRequest(directory, digest);
                RuntimeHostProtocol.ValidateRequest(good, false, null, false, false, false, true);

                // The operation is unreachable without its dedicated launch authorization.
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(good));

                var wrongEngine = AzadeganRequest(directory, digest);
                wrongEngine.Diagnostics["P16AzadeganEngine"] = "CivilStormDynamicEngine";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(wrongEngine, false, null, false, false, false, true));

                var wrongScenario = AzadeganRequest(directory, digest);
                wrongScenario.Diagnostics["P16AzadeganScenarioId"] = "359";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(wrongScenario, false, null, false, false, false, true));

                var wrongTime = AzadeganRequest(directory, digest);
                wrongTime.Diagnostics["P16AzadeganTimeSeconds"] = "180";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(wrongTime, false, null, false, false, false, true));

                var wrongElement = AzadeganRequest(directory, digest);
                wrongElement.Diagnostics["P16AzadeganElementId"] = "120";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(wrongElement, false, null, false, false, false, true));

                var extraKey = AzadeganRequest(directory, digest);
                extraKey.Diagnostics["P16AzadeganExtra"] = "x";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(extraKey, false, null, false, false, false, true));

                var writeAuthority = AzadeganRequest(directory, digest);
                writeAuthority.WriteAuthorizationTokenId = "token";
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(writeAuthority, false, null, false, false, false, true));

                var plan = AzadeganRequest(directory, digest);
                plan.EngineeringPlan = new GravityExecutionPlan();
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(plan, false, null, false, false, false, true));

                // A digest that does not describe the copied bytes is rejected.
                var staleDigest = AzadeganRequest(directory, new string('C', 64));
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(staleDigest, false, null, false, false, false, true));

                // A different model path inside the same copy is rejected.
                var wrongPath = AzadeganRequest(directory, digest);
                wrongPath.WorkingModelPath = Path.Combine(directory, "1cm", P16AzadeganReadOnlyResultHost.ModelFileName);
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(wrongPath, false, null, false, false, false, true));

                // Any extra file in the controlled copy breaks the exact family contract.
                File.WriteAllText(Path.Combine(directory, "unexpected.tmp"), "x");
                Throws<InvalidDataException>(() => RuntimeHostProtocol.ValidateRequest(AzadeganRequest(directory, digest), false, null, false, false, false, true));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        }

        // The bounded attempt performs no solver, save, scenario or alternative call.
        private static void P16Azadegan_NoSolverOrWriteSurface()
        {
            string source = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), "src", "SewerPilot.OpenFlows.Adapter",
                "P16AzadeganReadOnlyResultHost.cs"));
            foreach (string forbidden in new[] { "SaveCurrentProject", "SaveAs", "CreateScenario", "CopyScenario", "Run(", "BindNumericalEngine" })
                Check(!source.Contains(forbidden), "Read-only host must not reference " + forbidden + ".");
            Check(source.Contains("FlowField = \"BasicFlowResults_Flow\""), "Flow field name is explicit.");
            Check(source.Contains("FlowRecord = \"BasicFlowResults\""), "Flow record name is explicit.");
            Check(source.Contains("Engine = \"DelawareProductEngine\""), "Engine is explicit and not substituted.");
        }
    }
}
