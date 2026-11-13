using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    /// <summary>
    /// Single audited architectural gateway for every mutating operation across OpenFlows and hydraulic datasets.
    ///
    /// The adapter is primarily a read/analysis layer. Its architectural guard (the NoWriteOperations acceptance
    /// test) forbids write-intent member names in every source file EXCEPT this one. This file is the single
    /// formally whitelisted exception: it is excluded by filename in that test, and is additionally scanned
    /// there for solver-execution operations so the exclusion cannot become a blind spot.
    ///
    /// Because the exclusion is explicit and filename-based, write-intent member names appear here literally.
    /// No string splitting or token obfuscation is used or permitted: obfuscation would defeat the guard for
    /// every other file while providing no isolation benefit.
    ///
    /// This gateway centralizes, audits, and isolates write-intent execution:
    /// - InvokeSetValue(...)
    /// - ExecuteSave(...)
    /// - ExecuteSaveAs(...)
    /// - EncodeImage(...)
    ///
    /// Every operation is authorized, executed, and recorded in the audit trail.
    /// </summary>
    internal static class AdapterWriteBinding
    {
        internal static void ApplyP16RemediationSize(object field,int id,string guid)
        {
            if(field==null || !P16AnalysisRemediationHost.MatchSize(id,guid) ||
                Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field,null))!=FarzaneganHydraulicRemediationHost.CatalogSizeField)
                throw new InvalidOperationException("P16 TEST_ONLY size is out of scope.");
            var setter=GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("P16TestOnlyCatalogueSelection",()=>setter.Invoke(field,new object[]{id,true,guid}));
        }
        internal static void ApplyP16RemediationDiameter(object conduit,int id,double feet)
        {
            if(conduit==null || !P16AnalysisRemediationHost.MatchDiameter(id,feet))throw new InvalidOperationException("P16 TEST_ONLY diameter is out of scope.");
            var property=conduit.GetType().GetProperty("ConduitDiameter");
            if(property?.GetSetMethod()==null)throw new InvalidOperationException("Native diameter setter unavailable.");
            RuntimeStageJournal.Call("P16TestOnlyEffectiveDiameter",()=>property.SetValue(conduit,feet,null));
        }
        internal static void ApplyP16RemediationLabel(object field,int id,string nativeLabel)
        {
            if(field==null || P16AnalysisRemediationHost.Find(id)==null || string.IsNullOrWhiteSpace(nativeLabel) ||
                Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field,null))!="Physical_ConduitShapeLabel")
                throw new InvalidOperationException("P16 TEST_ONLY generated label is out of scope.");
            var setter=GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("P16TestOnlyNativeGeneratedLabel",()=>setter.Invoke(field,new object[]{id,true,nativeLabel}));
        }
        internal static void ApplyTestOnlyGvfAnalysisMode(object field,int id,object analysis)
        {
            if(field==null || id!=29 || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field,null))!="GvfCalculationType" ||
                analysis==null || !analysis.GetType().IsEnum || analysis.GetType().Name!="GVFCalculationType" || analysis.ToString()!="Analysis")
                throw new InvalidOperationException("TEST_ONLY fixed-candidate Analysis mode is out of scope.");
            var setter=field.GetType().GetMethod("SetValue",new[]{typeof(int),typeof(object)});
            if(setter==null || setter.IsStatic || setter.ReturnType!=typeof(void)) throw new InvalidOperationException("Native option setter unavailable.");
            RuntimeStageJournal.Call("P14TestOnlyGvfAnalysisMode",()=>setter.Invoke(field,new[]{(object)id,analysis}));
        }

        internal static void ApplyTestOnlyNativeGeneratedConduitLabel(object field, int id, string nativeLabel)
        {
            if(field==null || id!=150 || string.IsNullOrWhiteSpace(nativeLabel) ||
                Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field,null))!="Physical_ConduitShapeLabel")
                throw new InvalidOperationException("TEST_ONLY CO-40 generated-label write is out of scope.");
            var setter=GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("P14NativeGeneratedConduitLabelWrite",()=>setter.Invoke(field,new object[]{id,true,nativeLabel}));
        }
        internal static void ApplyTestOnlyConduitDiameter(object field, int id, double diameterFt)
        {
            if (field == null || id != 150 || diameterFt != 1.0 ||
                Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != "ConduitDiameter")
                throw new InvalidOperationException("TEST_ONLY CO-40 diameter selection is out of scope.");
            foreach (string name in new[] { "WorkingUnitIndex", "StorageUnitIndex" })
                if (Convert.ToString(field.GetType().GetProperty(name)?.GetValue(field, null)) != "Feet")
                    throw new InvalidOperationException("Native diameter working/storage units must be Feet.");
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("FarzaneganTestOnlyConduitDiameterSelection", () =>
                setter.Invoke(field, new object[] { id, true, diameterFt }));
        }
        // TEST_ONLY: switch CO-40 through the actual public ConduitElement.ConduitType property.
        internal static void ApplyTestOnlyConduitType(object conduit, int id)
        {
            if (conduit == null || id != 150 || conduit.GetType().GetProperty("ConduitType") == null)
                throw new InvalidOperationException("TEST_ONLY CO-40 native conduit-type property unavailable.");
            var property = conduit.GetType().GetProperty("ConduitType");
            if (property.PropertyType.FullName != "Haestad.Domain.StormSewer.ConduitTypeEnum" || property.GetSetMethod() == null || !property.PropertyType.IsEnum)
                throw new InvalidOperationException("TEST_ONLY ConduitType property contract mismatch.");
            string userDefinedName = null;
            foreach (string name in Enum.GetNames(property.PropertyType))
                if (name.IndexOf("UserDefined", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (userDefinedName != null) throw new InvalidOperationException("TEST_ONLY ambiguous user-defined conduit enum.");
                    userDefinedName = name;
                }
            if (userDefinedName == null) throw new InvalidOperationException("TEST_ONLY user-defined conduit enum member unavailable.");
            object value = Enum.Parse(property.PropertyType, userDefinedName, false);
            if (Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0)
                throw new InvalidOperationException("TEST_ONLY user-defined enum value differs from the native field enumeration.");
            RuntimeStageJournal.Call("FarzaneganTestOnlyConduitTypeNativeProperty", () => property.SetValue(conduit, value, null));
            if (Convert.ToInt32(property.GetValue(conduit, null), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("CO-40 ConduitElement.ConduitType did not read back as UserDefinedConduitType.");
        }

        // TEST_ONLY: exact CO-40 catalog-pointer persistence experiment only.
        internal static void ApplyTestOnlyCo40TwelveInchMirrorDiameter(object conduit, int id, double diameterFt)
        {
            if (conduit == null || id != 150 || Math.Abs(diameterFt - 1.0) > 1e-12)
                throw new InvalidOperationException("TEST_ONLY CO-40 12-inch mirror write is out of scope.");
            var property = conduit.GetType().GetProperty("ConduitDiameter");
            if (property == null || property.PropertyType != typeof(double) || property.GetSetMethod() == null)
                throw new InvalidOperationException("TEST_ONLY native ConduitDiameter setter unavailable.");
            RuntimeStageJournal.Call("P14CatalogPointerTrialMirrorDiameter", () => property.SetValue(conduit, diameterFt, null));
            if (Math.Abs(Convert.ToDouble(property.GetValue(conduit, null), CultureInfo.InvariantCulture) - diameterFt) > 1e-12)
                throw new InvalidDataException("CO-40 12-inch mirror diameter did not read back.");
        }

        // TEST_ONLY: sets an allowlisted conduit's effective diameter through the actual public
        // ConduitElement property. Only the eleven (id, diameter) pairs in
        // FarzaneganHydraulicRemediationHost.Targets can reach the native setter.
        internal static void ApplyTestOnlyConduitElementDiameter(object conduit, int id, double diameterFt)
        {
            if (conduit == null || !FarzaneganHydraulicRemediationHost.IsAllowlistedDiameter(id, diameterFt))
                throw new InvalidOperationException("TEST_ONLY allowlisted native diameter change is out of scope.");
            var property = conduit.GetType().GetProperty("ConduitDiameter");
            if (property == null || property.PropertyType != typeof(double) || property.GetSetMethod() == null)
                throw new InvalidOperationException("TEST_ONLY ConduitElement.ConduitDiameter setter unavailable.");
            RuntimeStageJournal.Call("FarzaneganTestOnlyConduitDiameterNativeProperty", () => property.SetValue(conduit, diameterFt, null));
            if (Math.Abs(Convert.ToDouble(property.GetValue(conduit, null), CultureInfo.InvariantCulture) - diameterFt) > 1e-10)
                throw new InvalidDataException("Allowlisted ConduitElement.ConduitDiameter did not read back as its target.");
        }

        // TEST_ONLY: set the catalog variant's own public pipe-ID property to its no-catalog value before switching type.
        internal static void ApplyTestOnlyCatalogPipeIdOnVariant(object conduit, int id, int catalogPipeId)
        {
            if (conduit == null || id != 150 || catalogPipeId != 0)
                throw new InvalidOperationException("TEST_ONLY CO-40 catalog pipe-ID variant write is out of scope.");
            var method = conduit.GetType().GetMethod("ConduitTypeObject", Type.EmptyTypes);
            object variant = method?.Invoke(conduit, null);
            if (variant?.GetType().FullName != "Haestad.Domain.ModelingObjects.Dynamic.ConduitTypeObject_CatalogConduitType")
                throw new InvalidOperationException("TEST_ONLY CO-40 catalog variant is not the qualified type.");
            var property = variant.GetType().GetProperty("CatalogPipeType_CatalogPipeID");
            if (property == null || property.PropertyType != typeof(int) || property.GetSetMethod() == null)
                throw new InvalidOperationException("TEST_ONLY catalog variant pipe-ID setter unavailable.");
            RuntimeStageJournal.Call("FarzaneganTestOnlyCatalogVariantPipeId", () => property.SetValue(variant, catalogPipeId, null));
            if (Convert.ToInt32(property.GetValue(variant, null), CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("CO-40 catalog variant pipe-ID did not read back as zero.");
        }

        // TEST_ONLY: clears only CO-40's catalog references so a user-defined section can persist.
        internal static void ClearTestOnlyCatalogSize(object field, int id)
        {
            if (field == null || id != 150 || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != FarzaneganHydraulicRemediationHost.CatalogSizeField)
                throw new InvalidOperationException("TEST_ONLY CO-40 catalog-size clear is out of scope.");
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("FarzaneganTestOnlyClearCatalogSize", () => setter.Invoke(field, new object[] { id, true, string.Empty }));
            object value = field.GetType().GetMethod("GetValue", new[] { typeof(int) })?.Invoke(field, new object[] { id });
            if (!string.IsNullOrEmpty(Convert.ToString(value, CultureInfo.InvariantCulture)))
                throw new InvalidDataException("CO-40 catalog-size reference did not clear to empty.");
        }

        internal static void ClearTestOnlyCatalogPipeId(object field, int id)
        {
            if (field == null || id != 150 || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != FarzaneganHydraulicRemediationHost.CatalogPipeIdField)
                throw new InvalidOperationException("TEST_ONLY CO-40 catalog-pipe-ID clear is out of scope.");
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("FarzaneganTestOnlyClearCatalogPipeId", () => setter.Invoke(field, new object[] { id, true, null }));
            object value = field.GetType().GetMethod("GetValue", new[] { typeof(int) })?.Invoke(field, new object[] { id });
            if (value != null) throw new InvalidDataException("CO-40 catalog-pipe-ID reference did not clear to null.");
        }

        // TEST_ONLY: clears the native shape-label-changed flag to match persisted user-defined examples.
        internal static void ApplyTestOnlyShapeLabelChanged(object field, int id, bool changed)
        {
            if (field == null || id != 150 || changed || Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != "Physical_HasConduitShapeLabelChanged")
                throw new InvalidOperationException("TEST_ONLY CO-40 shape-label-changed flag is out of scope.");
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("FarzaneganTestOnlyShapeLabelChanged", () => setter.Invoke(field, new object[] { id, true, false }));
            object value = field.GetType().GetMethod("GetValue", new[] { typeof(int) })?.Invoke(field, new object[] { id });
            if (value == null || Convert.ToBoolean(value, CultureInfo.InvariantCulture))
                throw new InvalidDataException("CO-40 shape-label-changed flag did not read back false.");
        }

        // TEST_ONLY: synchronizes CO-40's native user-defined section label with the 12-inch diameter.
        internal static void ApplyTestOnlyConduitShapeLabel(object conduit, int id, string label)
        {
            if (conduit == null || id != 150 || label != "Circle - 304.8 mm")
                throw new InvalidOperationException("TEST_ONLY CO-40 shape label is out of scope.");
            var property = conduit.GetType().GetProperty("Physical_ConduitShapeLabel");
            if (property == null || property.PropertyType != typeof(string) || property.GetSetMethod() == null)
                throw new InvalidOperationException("TEST_ONLY native shape-label setter unavailable.");
            RuntimeStageJournal.Call("FarzaneganTestOnlyConduitShapeLabel", () => property.SetValue(conduit, label, null));
            if (!string.Equals(Convert.ToString(property.GetValue(conduit, null), CultureInfo.InvariantCulture), label, StringComparison.Ordinal))
                throw new InvalidDataException("CO-40 native shape label did not read back as 12-inch circle.");
        }

        internal static void ApplyTestOnlyUseLocalShapeLabel(object conduit, int id, bool useLocal)
        {
            if (conduit == null || id != 150 || useLocal)
                throw new InvalidOperationException("TEST_ONLY CO-40 local shape-label override is out of scope.");
            var property = conduit.GetType().GetProperty("Physical_UseLocalConduitShapeLabel");
            if (property == null || property.PropertyType != typeof(bool) || property.GetSetMethod() == null)
                throw new InvalidOperationException("TEST_ONLY native local shape-label flag setter unavailable.");
            RuntimeStageJournal.Call("FarzaneganTestOnlyUseLocalShapeLabel", () => property.SetValue(conduit, false, null));
            if ((bool)property.GetValue(conduit, null))
                throw new InvalidDataException("CO-40 native local shape-label flag did not read back false.");
        }

        // TEST_ONLY: selects the qualified 12-inch catalog entry on CO-40 through the alternative-scoped
        // catalog-size field. The field name, target element and accepted value are all fixed here so the
        // gateway cannot be repurposed into a general catalog editor.
        internal static void ApplyTestOnlyCatalogSize(object field, int id, string size, Type valueType)
        {
            if (field == null || id != 150 || valueType != typeof(string) ||
                !string.Equals(size, FarzaneganHydraulicRemediationHost.TwelveInchCatalogSizeGuid, StringComparison.OrdinalIgnoreCase) ||
                Convert.ToString(field.GetType().GetProperty("Name")?.GetValue(field, null)) != FarzaneganHydraulicRemediationHost.CatalogSizeField)
                throw new InvalidOperationException("TEST_ONLY CO-40 catalog size selection is out of scope.");
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("FarzaneganTestOnlyCatalogSizeSelection", () =>
                setter.Invoke(field, new object[] { id, true, size }));
        }
        internal static void ApplyTestOnlyConduitStart(object field, int id, double value)
        {
            var setter = GravityWriteAndSolverHost.RequireLocalSetter(field);
            RuntimeStageJournal.Call("P14TestOnlyNativeStartInvertWrite", () =>
                setter.Invoke(field, new object[] { id, true, value }));
        }
        // Project persistence members are resolved by name because the concrete project type is supplied by the
        // Bentley runtime and is not known at compile time. The names are literal by design (see type remarks).
        private const string SaveMemberName = "Save";
        private const string SaveAsMemberName = "SaveAs";

        /// <summary>
        /// Explicit gateway method for setting property values on reflected types.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void InvokeSetValue(PropertyInfo property, object target, object value)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            RuntimeStageJournal.Call("AdapterWriteBinding_InvokeSetValue_Property", () =>
            {
                property.SetValue(target, value, null);
            });
        }

        /// <summary>
        /// Explicit gateway method for setting indexed property values on reflected types.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void InvokeSetValue(PropertyInfo property, object target, object value, object[] index)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            RuntimeStageJournal.Call("AdapterWriteBinding_InvokeSetValue_PropertyIndexed", () =>
            {
                property.SetValue(target, value, index);
            });
        }

        /// <summary>
        /// Explicit gateway method for setting field values on reflected types.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void InvokeSetValue(FieldInfo field, object target, object value)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            RuntimeStageJournal.Call("AdapterWriteBinding_InvokeSetValue_Field", () =>
            {
                field.SetValue(target, value);
            });
        }

        // Backward compatibility overloads matching legacy call sites:
        internal static void SetPropertyValue(PropertyInfo property, object target, object value) => InvokeSetValue(property, target, value);
        internal static void SetPropertyValue(PropertyInfo property, object target, object value, object[] index) => InvokeSetValue(property, target, value, index);
        internal static void SetFieldValue(FieldInfo field, object target, object value) => InvokeSetValue(field, target, value);

        /// <summary>
        /// Explicit gateway method for executing project save operations.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void ExecuteSave(object project, Type projectPropertiesType, object projectProperties)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            RuntimeStageJournal.Call("AdapterWriteBinding_ExecuteSave", () =>
            {
                Type propsType = projectPropertiesType ?? (projectProperties != null ? projectProperties.GetType() : typeof(object));
                MethodInfo method = ResolveProjectSave(project.GetType(), propsType);
                method.Invoke(project, new object[] { projectProperties });
            });
        }

        /// <summary>
        /// Explicit gateway method for executing project save operations.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void ExecuteSave(object project, object projectProperties)
        {
            ExecuteSave(project, projectProperties != null ? projectProperties.GetType() : null, projectProperties);
        }

        /// <summary>
        /// Explicit gateway method for executing project save-as operations.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void ExecuteSaveAs(object project, Type projectPropertiesType, object projectProperties)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            RuntimeStageJournal.Call("AdapterWriteBinding_ExecuteSaveAs", () =>
            {
                Type propsType = projectPropertiesType ?? (projectProperties != null ? projectProperties.GetType() : typeof(object));
                MethodInfo method = ResolveProjectSaveAs(project.GetType(), propsType);
                method.Invoke(project, new object[] { projectProperties });
            });
        }

        /// <summary>
        /// Explicit gateway method for executing project save-as operations.
        /// Audited and gated through RuntimeStageJournal.
        /// </summary>
        internal static void ExecuteSaveAs(object project, object projectProperties)
        {
            ExecuteSaveAs(project, projectProperties != null ? projectProperties.GetType() : null, projectProperties);
        }

        internal static MethodInfo ResolveProjectSave(Type projectType, Type projectPropertiesType)
        {
            if (projectType == null) throw new ArgumentNullException(nameof(projectType));
            MethodInfo method = projectType.GetMethod(SaveMemberName, new[] { projectPropertiesType });
            if (method == null)
            {
                foreach (var m in projectType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == SaveMemberName && m.GetParameters().Length == 1 &&
                        (projectPropertiesType == null || m.GetParameters()[0].ParameterType.IsAssignableFrom(projectPropertiesType)))
                    {
                        method = m;
                        break;
                    }
                }
            }
            if (method == null)
            {
                throw new InvalidOperationException(
                    "CRITICAL_VENDOR_REFLECTION_FAILURE: project persistence member could not be resolved on " + projectType.FullName + ". Fail-closed contract enforced.");
            }
            return method;
        }

        internal static MethodInfo ResolveProjectSaveAs(Type projectType, Type projectPropertiesType)
        {
            if (projectType == null) throw new ArgumentNullException(nameof(projectType));
            MethodInfo method = projectType.GetMethod(SaveAsMemberName, new[] { projectPropertiesType });
            if (method == null)
            {
                foreach (var m in projectType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == SaveAsMemberName && m.GetParameters().Length == 1 &&
                        (projectPropertiesType == null || m.GetParameters()[0].ParameterType.IsAssignableFrom(projectPropertiesType)))
                    {
                        method = m;
                        break;
                    }
                }
            }
            if (method == null)
            {
                throw new InvalidOperationException(
                    "CRITICAL_VENDOR_REFLECTION_FAILURE: project persistence-as member could not be resolved on " + projectType.FullName + ". Fail-closed contract enforced.");
            }
            return method;
        }

        /// <summary>Encodes a rendered bitmap to disk through the GDI+ encoder.</summary>
        internal static void EncodeImage(Image image, string path, ImageFormat format)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Image output path is required.", nameof(path));
            RuntimeStageJournal.Call("AdapterWriteBinding_EncodeImage", () =>
            {
                image.Save(path, format);
            });
        }
    }
}


