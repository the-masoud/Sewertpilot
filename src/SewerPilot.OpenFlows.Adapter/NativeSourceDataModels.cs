using System.Collections.Generic;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    // Additive observations, deliberately outside the P12 network fingerprint and engineering decisions.
    public sealed class NativeSourceParameter
    {
        public string Category { get; set; }
        public int? SourceId { get; set; }
        public string CollectionIdentity { get; set; }
        public string NativeRowKey { get; set; }
        public string ParameterIdentity { get; set; }
        public int? ScenarioId { get; set; }
        public int? AlternativeTypeId { get; set; }
        public int? AlternativeId { get; set; }
        public int? ScenarioAssignedAlternativeId { get; set; }
        public string EngineTypeName { get; set; }
        public int? CalculationOptionsId { get; set; }
        public object Value { get; set; }
        public string NativeValueType { get; set; }
        public string NativeEnumName { get; set; }
        public SourceUnitIdentity Unit { get; set; }
        public string UnitAvailability { get; set; } = "NOT_EXPORTED";
        public string SourceProvenance { get; set; }
        public string Availability { get; set; } = "NOT_EXPORTED";
        public string SourceClassification { get; set; } = "NOT_PROVEN";
        public string Reason { get; set; }
        public string ManagerRuntimeType { get; set; }
        public string ModelingElementType { get; set; }
        public int? ElementTypeId { get; set; }
        public int? EngineTypeId { get; set; }
        public int? SupportedFieldsCount { get; set; }
        public string SupportedFieldsSample { get; set; }
        public string EngineLookupResult { get; set; }
        public string Authority => "NATIVE_CONFIGURATION_ONLY_NO_ENGINEERING_POLICY";
        public string Interpretation => "RAW_NATIVE_PARAMETER_NOT_CALCULATED_DESIGN_LOADING";
        public string Applicability => "NOT_INTERPRETED_AS_ACTIVE_DESIGN_INPUT";

        public NativeSourceParameter Copy()
        {
            var copy = (NativeSourceParameter)MemberwiseClone();
            if (SourceClassification == "PROVEN" && !GravityNetworkReadMapper.RecognizedNativeSourceParameter(Category, ParameterIdentity))
            {
                copy.Availability = "SEMANTICS_UNRESOLVED"; copy.SourceClassification = "NOT_PROVEN";
                copy.Reason = "UNRECOGNIZED_STORED_NATIVE_PARAMETER";
            }
            // Only scalar CLR values are admitted by the mapper; no live vendor objects escape.
            if (Value != null && !(Value is string || Value is bool || Value is byte || Value is short || Value is int ||
                Value is long || Value is float || Value is double || Value is decimal))
            {
                copy.Value = null; copy.Availability = "SEMANTICS_UNRESOLVED"; copy.SourceClassification = "NOT_PROVEN";
                copy.Reason = "UNSUPPORTED_STORED_NATIVE_VALUE_TYPE";
            }
            copy.Unit = Unit?.Clone();
            return copy;
        }
    }

    public sealed class NativeSourceData
    {
        public int ObservationSchemaVersion { get; set; } = 1;
        public List<NativeSourceParameter> Parameters { get; set; } = new List<NativeSourceParameter>();
        public NativeSourceData Copy() => new NativeSourceData { ObservationSchemaVersion = ObservationSchemaVersion,
            Parameters = (Parameters ?? new List<NativeSourceParameter>()).Where(p => p != null).Select(p => p.Copy())
                .OrderBy(p => p.Category, System.StringComparer.Ordinal).ThenBy(p => p.SourceId)
                .ThenBy(p => p.CollectionIdentity, System.StringComparer.Ordinal).ThenBy(p => p.NativeRowKey, System.StringComparer.Ordinal)
                .ThenBy(p => p.ParameterIdentity, System.StringComparer.Ordinal).ToList() };
    }
}
