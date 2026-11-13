using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // One-shot test-only CO-40 catalog-pointer persistence gate. This operation deliberately
    // saves but does not invoke the solver. The caller must independently verify persistence
    // before any subsequent calculation is authorized.
    internal static class P14CatalogPointerPersistenceHost
    {
        internal const string Operation = "P14CatalogPointerPersistenceTrial";
        internal const string LabelRepairOperation = "P14Co40NativeLabelRepair";
        internal const string PrepareOperation = "P14FourInvertPrepareOnly";
        internal const string ScopedRepairOperation="P14Co40ScopedSelectorRepair";
        internal const string ScopedComputeOperation="P14Co40ScopedPersistenceCompute";
        internal const string AnalysisPrepareOperation="P14FixedCandidateAnalysisPrepareOnly";
        internal const string AnalysisInputDigest="61B9CFBBF79D792CEC0D3EE58357F4B0987ACBE7B5B6AEC20ACCBD507BE47CE7";
        internal const string ScopedRepairInputDigest="902D975AFBCC91FC3636BCD9E10C12AD1F70A75CF62CEDD4C0C30702D752224F";
        internal const string LabelRepairInputDigest = "3E00F02ACA73445A38E8EE30A3544DF97710489392C5BB062A71763EBD574AD8";
        internal static bool IsOperation(string op) => op==Operation || op==LabelRepairOperation || op==PrepareOperation || op==ScopedRepairOperation || op==AnalysisPrepareOperation || op==P16AnalysisRemediationHost.PrepareOperation;
        internal static int ExpectedMutations(string op) => op==P16AnalysisRemediationHost.PrepareOperation ? P16AnalysisRemediationHost.ExpectedWrites : op==AnalysisPrepareOperation ? 1 : op==ScopedRepairOperation ? 3 : op==PrepareOperation ? 4 : op==LabelRepairOperation ? 1 : 2;
        internal const string PassFlag = "P14CatalogPointerTrialSaved";
        internal const int ConduitId = 150;
        internal const string TwelveInchGuid = "61634a43-f8e2-4469-a1eb-c47fedbf8c6a";
        internal const double TenInchFt = 0.8333333333333335;
        internal const double TwelveInchFt = 1.0;

        internal static bool RegistryReportsSize(System.Collections.Generic.IEnumerable<string> entries) =>
            entries.Any(x=>x.IndexOf("hasSizeField=true",StringComparison.OrdinalIgnoreCase)>=0);

        internal static object GvfModeField(object dataset)
        {
            object sm=dataset.GetType().GetProperty("ScenarioManager").GetValue(dataset,null);
            object manager=sm.GetType().GetMethod("CalculationOptionsManager",new[]{typeof(string)}).Invoke(sm,new object[]{P16FarzaneganNativePilotHost.Engine});
            object options=manager.GetType().GetMethod("Element",new[]{typeof(int)}).Invoke(manager,new object[]{29});
            if(Convert.ToString(options.GetType().GetProperty("NumericalEngineTypeName").GetValue(options,null))!=P16FarzaneganNativePilotHost.Engine)
                throw new InvalidDataException("Exact calculation-options engine required.");
            return manager.GetType().GetMethod("CalculationOptionsField",new[]{typeof(string)}).Invoke(manager,new object[]{"GvfCalculationType"});
        }
        internal static string GvfMode(object dataset)
        {
            object field=GvfModeField(dataset);object value=field.GetType().GetMethod("GetValue",new[]{typeof(int)}).Invoke(field,new object[]{29});
            Type mode=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Haestad.Domain.StormSewer").GetType("Haestad.Domain.StormSewer.GVFCalculationType",true);
            string name=Enum.GetName(mode,Convert.ToInt32(value,CultureInfo.InvariantCulture));
            if(name==null)throw new InvalidDataException("Unknown native GVF calculation mode.");return name;
        }

        internal static void VerifyComputeState(object dataset,object scenario,ModelReadEvidence evidence,string stage)
        {
            evidence.Diagnostics["ScopedSelector"+stage+"GvfMode"]=GvfMode(dataset);
            if((stage=="BeforeBinding" || stage=="AfterBinding" || stage=="AfterSolver" || stage=="AfterSave") && GvfMode(dataset)!="Analysis")
                throw new InvalidDataException("Fixed-candidate verification requires native Analysis mode at "+stage);
            var ends=P14FarzaneganTestOnlyHost.Snapshot(dataset);
            for(int i=0;i<P14FarzaneganTestOnlyHost.Ids.Length;i++)
            {
                var row=ends.Single(x=>x.Id==P14FarzaneganTestOnlyHost.Ids[i]);
                if(Math.Abs(row.Start-P14FarzaneganTestOnlyHost.Proposed[i])>1e-10 || row.SetStartToNode || row.SetStopToNode)
                    throw new InvalidDataException("Scoped calculation requires all four verified P14 starts.");
            }
            var sections=FarzaneganHydraulicRemediationHost.PersistedSections(dataset,scenario);
            evidence.Diagnostics["ScopedSelector"+stage+"EndpointsJson"]=RuntimeHostProtocol.Serializer().Serialize(ends);
            evidence.Diagnostics["ScopedSelector"+stage+"SectionsJson"]=RuntimeHostProtocol.Serializer().Serialize(sections);
            var target=sections.Single(x=>x.Id==ConduitId);
            if(target.CatalogSizeGuid!=TwelveInchGuid || target.DiameterFt!=1.0 || target.ShapeLabel!="Circle - 304.8 mm" || target.UseLocalShapeLabel ||
                sections.Any(x=>x.ConduitTypeValue==1 && Math.Abs(x.DiameterFt-double.Parse(x.CatalogSizeInsideDiameterFt,CultureInfo.InvariantCulture))>1e-10))
                throw new InvalidDataException("Scoped catalog consistency failed at "+stage);
        }

        internal static void Execute(object dataset, object project, object scenario, HostRequest request, ModelReadEvidence evidence)
        {
            if (request == null || !IsOperation(request.Operation))
                throw new InvalidDataException("Exact CO-40 catalog-pointer persistence trial required.");
            if (!P16SaveQuestionPolicy.SamePath(Convert.ToString(project.GetType().GetProperty("FullPath").GetValue(project, null), CultureInfo.InvariantCulture), request.WorkingModelPath))
                throw new InvalidDataException("Trial may only mutate its protected working copy.");
            int physicalType = ScenarioAlternativeIsolationHost.ResolvePhysicalAlternativeTypeId(dataset);
            int physicalAlternative = ScenarioAlternativeCapabilityMapper.ReadScenarioAlternativeAssignment(scenario, physicalType);
            if (physicalAlternative != 6) throw new InvalidDataException("Expected scenario 1 Physical alternative 6.");

            string modelDirectory = Path.GetDirectoryName(request.WorkingModelPath);
            var inventoryBeforeSave = P16AzadeganCalculationHost.Inventory(modelDirectory);
            if(request.Operation==P16AnalysisRemediationHost.PrepareOperation)
            {
                P16AnalysisRemediationHost.Prepare(dataset,project,scenario,evidence);
                SaveOnly(project,request,evidence,inventoryBeforeSave);return;
            }
            if(request.Operation==PrepareOperation)
            {
                P14FarzaneganTestOnlyHost.Apply(dataset,project,request,evidence,scenario);
                SaveOnly(project,request,evidence,inventoryBeforeSave);
                return;
            }
            bool scoped=request.Operation==ScopedRepairOperation;
            if(request.Operation==AnalysisPrepareOperation)
            {
                VerifyComputeState(dataset,scenario,evidence,"BeforeModeCorrection");
                if(GvfMode(dataset)!="Design")throw new InvalidDataException("Mode correction requires observed native Design precondition.");
                object field=GvfModeField(dataset);
                Type mode=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Haestad.Domain.StormSewer").GetType("Haestad.Domain.StormSewer.GVFCalculationType",true);
                AdapterWriteBinding.ApplyTestOnlyGvfAnalysisMode(field,29,Enum.Parse(mode,"Analysis"));
                evidence.Diagnostics["PropertySetCount"]="1";
                if(GvfMode(dataset)!="Analysis")throw new InvalidDataException("Native Analysis mode readback failed.");
                VerifyComputeState(dataset,scenario,evidence,"AfterModeCorrection");
                evidence.Diagnostics["P14NativeWriteReadbackVerified"]="true";
                SaveOnly(project,request,evidence,inventoryBeforeSave);return;
            }
            object sizeField=scoped ? P14NativeConduitLabelFormatter.PhysicalField(P14FarzaneganTestOnlyHost.Manager(dataset),FarzaneganHydraulicRemediationHost.CatalogSizeField)
                : FarzaneganHydraulicRemediationHost.CatalogSizeFieldHandle(dataset, scenario);
            if(scoped)
            {
                if(sizeField.GetType().FullName=="Haestad.Domain.ModelingObjects.GenericDomainElementAlternativeField")
                    throw new InvalidDataException("Scoped selector repair refuses the previously failing generic field.");
                var ends=P14FarzaneganTestOnlyHost.Snapshot(dataset);
                for(int i=0;i<P14FarzaneganTestOnlyHost.Ids.Length;i++)
                    if(Math.Abs(ends.Single(x=>x.Id==P14FarzaneganTestOnlyHost.Ids[i]).Start-P14FarzaneganTestOnlyHost.Proposed[i])>1e-10)
                        throw new InvalidDataException("Scoped selector repair requires all four persisted P14 starts.");
                evidence.Diagnostics["ScopedSelectorFieldType"]=sizeField.GetType().FullName;
                evidence.Diagnostics["ScopedRepairEndpointsBeforeJson"]=RuntimeHostProtocol.Serializer().Serialize(ends);
            }
            var sizes = FarzaneganHydraulicRemediationHost.CatalogSizes(dataset);
            if (!sizes.TryGetValue(FarzaneganHydraulicRemediationHost.TenInchCatalogSizeGuid, out string[] ten) ||
                ten[0] != "10 inch" || Math.Abs(double.Parse(ten[1], CultureInfo.InvariantCulture) - TenInchFt) > 1e-12 ||
                !sizes.TryGetValue(TwelveInchGuid, out string[] twelve) || twelve[0] != "12 inch" ||
                Math.Abs(double.Parse(twelve[1], CultureInfo.InvariantCulture) - TwelveInchFt) > 1e-12)
                throw new InvalidDataException("Native 10/12-inch catalog entries do not match the qualified GUID/diameter contract.");

            var before = FarzaneganHydraulicRemediationHost.Sections(dataset, sizeField, sizes);
            var target = before.Single(x => x.Id == ConduitId);
            bool repair=request.Operation==LabelRepairOperation;
            if (target.ConduitType != "CatalogConduit" || target.ConduitTypeValue != 1 ||
                target.CatalogId != "125" ||
                !string.Equals(target.CatalogSizeGuid, repair?TwelveInchGuid:FarzaneganHydraulicRemediationHost.TenInchCatalogSizeGuid, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(target.DiameterFt - (repair?TwelveInchFt:TenInchFt)) > 1e-12 ||
                target.Shape != "CircularPipe" || target.Material != "GRVE" ||
                Math.Abs(target.ManningsN - 0.01) > 1e-12)
                throw new InvalidDataException("CO-40 no longer matches the exact 10-inch source precondition.");

            object manager = P14FarzaneganTestOnlyHost.Manager(dataset);
            object conduit = manager.GetType().GetMethod("Element", new[] { typeof(int) }).Invoke(manager, new object[] { ConduitId });
            if (conduit == null) throw new InvalidDataException("CO-40 native element unavailable.");

            evidence.Diagnostics["PropertySetCount"] = "0";
            if(repair)
            {
                if(target.UseLocalShapeLabel) throw new InvalidDataException("Local shape-description override must remain disabled.");
                var generated=P14NativeConduitLabelFormatter.Generate(dataset,project,manager);
                evidence.Diagnostics["NativeLabelFormatterJson"]=RuntimeHostProtocol.Serializer().Serialize(generated);
                if(generated.GeneratedLabel==target.ShapeLabel) throw new InvalidDataException("Native formatter did not change the stale description.");
                AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(
                    P14NativeConduitLabelFormatter.PhysicalField(manager,"Physical_ConduitShapeLabel"),ConduitId,generated.GeneratedLabel);
                evidence.Diagnostics["PropertySetCount"]="1";
            }
            else
            {
                AdapterWriteBinding.ApplyTestOnlyCatalogSize(sizeField, ConduitId, TwelveInchGuid, typeof(string));
                evidence.Diagnostics["PropertySetCount"] = "1";
                AdapterWriteBinding.ApplyTestOnlyCo40TwelveInchMirrorDiameter(conduit, ConduitId, TwelveInchFt);
                evidence.Diagnostics["PropertySetCount"] = "2";
            }

            if(scoped)
            {
                var generated=P14NativeConduitLabelFormatter.Generate(dataset,project,manager);
                AdapterWriteBinding.ApplyTestOnlyNativeGeneratedConduitLabel(P14NativeConduitLabelFormatter.PhysicalField(manager,"Physical_ConduitShapeLabel"),ConduitId,generated.GeneratedLabel);
                evidence.Diagnostics["PropertySetCount"]="3";
                evidence.Diagnostics["NativeLabelFormatterJson"]=RuntimeHostProtocol.Serializer().Serialize(generated);
            }
            var after = FarzaneganHydraulicRemediationHost.Sections(dataset, sizeField, sizes);
            if(scoped)
            {
                evidence.Diagnostics["ScopedRepairSectionsBeforeJson"]=RuntimeHostProtocol.Serializer().Serialize(before);
                evidence.Diagnostics["ScopedRepairSectionsAfterJson"]=RuntimeHostProtocol.Serializer().Serialize(after);
            }
            var selected = after.Single(x => x.Id == ConduitId);
            if (!string.Equals(selected.CatalogSizeGuid, TwelveInchGuid, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(selected.DiameterFt - TwelveInchFt) > 1e-12 ||
                selected.ConduitType != "CatalogConduit" || selected.CatalogId != "125" ||
                selected.Shape != target.Shape || selected.Material != target.Material ||
                Math.Abs(selected.ManningsN - target.ManningsN) > 1e-12)
                throw new InvalidDataException("CO-40 immediate native readback failed.");
            int mismatches = after.Count(x => x.ConduitTypeValue == 1 &&
                (string.IsNullOrEmpty(x.CatalogSizeInsideDiameterFt) ||
                 Math.Abs(x.DiameterFt - double.Parse(x.CatalogSizeInsideDiameterFt, CultureInfo.InvariantCulture)) > 1e-10));
            if (mismatches != 0) throw new InvalidDataException("Model-wide catalog diameter invariant failed in session: " + mismatches);
            if(repair)
            {
                foreach(var row in after)
                {
                    var original=before.Single(x=>x.Id==row.Id);
                    string oldLabel=original.ShapeLabel;string newLabel=row.ShapeLabel;
                    original.ShapeLabel=newLabel;
                    bool equal=RuntimeHostProtocol.Serializer().Serialize(original)==RuntimeHostProtocol.Serializer().Serialize(row);
                    original.ShapeLabel=oldLabel;
                    if(!equal || (row.Id!=ConduitId && oldLabel!=newLabel))
                        throw new InvalidDataException("Label repair changed a non-allowlisted property: "+row.Id);
                }
                evidence.Diagnostics["LabelRepairSectionsBeforeJson"]=RuntimeHostProtocol.Serializer().Serialize(before);
                evidence.Diagnostics["LabelRepairSectionsAfterJson"]=RuntimeHostProtocol.Serializer().Serialize(after);
            }
            evidence.Diagnostics["CatalogPointerTrialBeforeJson"] = RuntimeHostProtocol.Serializer().Serialize(target);
            evidence.Diagnostics["CatalogPointerTrialAfterJson"] = RuntimeHostProtocol.Serializer().Serialize(selected);
            evidence.Diagnostics["CatalogPointerTrialInvariantMismatchesInSession"] = mismatches.ToString(CultureInfo.InvariantCulture);
            evidence.Diagnostics["P14NativeWriteReadbackVerified"] = "true";

            SaveOnly(project,request,evidence,inventoryBeforeSave);
        }

        private static void SaveOnly(object project, HostRequest request, ModelReadEvidence evidence,
            System.Collections.Generic.SortedDictionary<string,string> inventoryBeforeSave)
        {
            string modelDirectory=Path.GetDirectoryName(request.WorkingModelPath);
            Type projectContract = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Haestad.Framework")
                .GetType("Haestad.Framework.Application.IProject", true, false);
            Type propsType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Haestad.Framework")
                .GetType("Haestad.Framework.Application.ProjectProperties", true, false);
            Type questionType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Haestad.Support")
                .GetType("Haestad.Support.User.IMessageQuestionHandler", true, false);
            Type enumType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Haestad.Support")
                .GetType("Haestad.Support.User.MessageHandlerResult", true, false);
            if (!projectContract.IsInstanceOfType(project)) throw new InvalidDataException("Native IProject contract mismatch.");
            var questions = new P16SaveQuestionRecorder(request.WorkingModelPath);
            object props = Activator.CreateInstance(propsType);
            object handler = P16SaveQuestionHandler.Create(questionType, enumType, questions);
            AdapterWriteBinding.InvokeSetValue(propsType.GetProperty("MessageQuestionHandler"), props, handler);
            evidence.Diagnostics["SaveCurrentProjectInvoked"] = "true";
            RuntimeStageJournal.Call("P14CatalogPointerTrialSave", () => AdapterWriteBinding.ExecuteSave(project, propsType, props));
            bool dirty = Convert.ToBoolean(projectContract.GetProperty("IsDirty").GetValue(project, null), CultureInfo.InvariantCulture);
            var inventoryAfterSave = P16AzadeganCalculationHost.Inventory(modelDirectory);
            int changedFileCount = inventoryAfterSave.Keys.Count(k => !inventoryBeforeSave.ContainsKey(k) || inventoryBeforeSave[k] != inventoryAfterSave[k]) +
                inventoryBeforeSave.Keys.Count(k => !inventoryAfterSave.ContainsKey(k));
            evidence.Diagnostics["CatalogPointerTrialChangedFilesAfterSave"] = changedFileCount.ToString(CultureInfo.InvariantCulture);
            bool persisted = P16ProjectPersistenceResult.IsPersisted(P16ProjectPersistenceResult.Classify(false,
                questions.AnyUnauthorizedQuestion || questions.BudgetExceeded || questions.WarningBudgetExceeded, changedFileCount, dirty));
            if (!persisted) throw new InvalidDataException("Catalog pointer trial save was not classified as persisted.");
            evidence.Diagnostics["SaveCurrentProjectSucceeded"] = "true";
            evidence.Diagnostics[PassFlag] = "true";
            evidence.Diagnostics["CatalogPointerTrialSolverCalls"] = "0";
            evidence.Diagnostics["CatalogPointerTrialSaveCalls"] = "1";
            evidence.Diagnostics["CatalogPointerTrialMutationCalls"] = ExpectedMutations(request.Operation).ToString(CultureInfo.InvariantCulture);
        }
    }
}
