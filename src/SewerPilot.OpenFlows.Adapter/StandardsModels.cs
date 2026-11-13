using System;
using System.Collections.Generic;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum StandardSourceLayer
    {
        ApplicationDefault = 1,
        PublishedStandard = 2,
        CompanyStandard = 3,
        ProjectOverride = 4
    }

    public enum StandardValueKind
    {
        Number = 1,
        Integer = 2,
        Boolean = 3,
        Text = 4,
        Quantity = 5
    }

    public enum StandardResolutionStatus
    {
        Defined = 1,
        NotDefined = 2,
        Ambiguous = 3
    }

    public static class StandardSourceLayers
    {
        public const string ApplicationDefault = "ApplicationDefault";
        public const string PublishedStandard = "PublishedStandard";
        public const string CompanyStandard = "CompanyStandard";
        public const string ProjectOverride = "ProjectOverride";
    }

    public sealed class StandardsValidationException : Exception
    {
        public string Classification { get; }

        public StandardsValidationException(string message, string classification = "STANDARDS_VALIDATION_ERROR")
            : base(message)
        {
            Classification = classification ?? "STANDARDS_VALIDATION_ERROR";
        }
    }

    public sealed class StandardsResolutionException : Exception
    {
        public string Classification { get; }

        public StandardsResolutionException(string classification, string message)
            : base(message)
        {
            Classification = classification ?? "STANDARDS_RESOLUTION_ERROR";
        }
    }

    public sealed class StandardQuantity : IEquatable<StandardQuantity>
    {
        public double Value { get; set; }
        public string Unit { get; set; }

        public StandardQuantity() { }

        public StandardQuantity(double value, string unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new StandardsValidationException("Quantity numeric value must be finite.");
            if (string.IsNullOrWhiteSpace(unit))
                throw new StandardsValidationException("Quantity unit symbol/identifier must be non-empty.");
            Value = value;
            Unit = unit.Trim();
        }

        public bool Equals(StandardQuantity other)
        {
            if (other == null) return false;
            return Value.Equals(other.Value) && string.Equals(Unit, other.Unit, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as StandardQuantity);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Value.GetHashCode() * 397) ^ (Unit != null ? StringComparer.Ordinal.GetHashCode(Unit) : 0);
            }
        }

        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture) + " " + Unit;
        }
    }

    public sealed class StandardValue : IEquatable<StandardValue>
    {
        public StandardValueKind Kind { get; set; }
        public double? NumberValue { get; set; }
        public long? IntegerValue { get; set; }
        public bool? BooleanValue { get; set; }
        public string TextValue { get; set; }
        public StandardQuantity QuantityValue { get; set; }

        public static StandardValue FromNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new StandardsValidationException("Number value must be finite.");
            return new StandardValue { Kind = StandardValueKind.Number, NumberValue = value };
        }

        public static StandardValue FromInteger(long value)
        {
            return new StandardValue { Kind = StandardValueKind.Integer, IntegerValue = value };
        }

        public static StandardValue FromBoolean(bool value)
        {
            return new StandardValue { Kind = StandardValueKind.Boolean, BooleanValue = value };
        }

        public static StandardValue FromText(string value)
        {
            if (value == null)
                throw new StandardsValidationException("Text value cannot be null.");
            return new StandardValue { Kind = StandardValueKind.Text, TextValue = value };
        }

        public static StandardValue FromQuantity(double value, string unit)
        {
            return new StandardValue { Kind = StandardValueKind.Quantity, QuantityValue = new StandardQuantity(value, unit) };
        }

        public void Validate()
        {
            switch (Kind)
            {
                case StandardValueKind.Number:
                    if (!NumberValue.HasValue || double.IsNaN(NumberValue.Value) || double.IsInfinity(NumberValue.Value))
                        throw new StandardsValidationException("Number value must be finite.");
                    break;
                case StandardValueKind.Integer:
                    if (!IntegerValue.HasValue)
                        throw new StandardsValidationException("Integer value must be specified.");
                    break;
                case StandardValueKind.Boolean:
                    if (!BooleanValue.HasValue)
                        throw new StandardsValidationException("Boolean value must be specified.");
                    break;
                case StandardValueKind.Text:
                    if (TextValue == null)
                        throw new StandardsValidationException("Text value cannot be null.");
                    break;
                case StandardValueKind.Quantity:
                    if (QuantityValue == null)
                        throw new StandardsValidationException("Quantity value must be specified.");
                    if (double.IsNaN(QuantityValue.Value) || double.IsInfinity(QuantityValue.Value))
                        throw new StandardsValidationException("Quantity numeric value must be finite.");
                    if (string.IsNullOrWhiteSpace(QuantityValue.Unit))
                        throw new StandardsValidationException("Quantity unit symbol/identifier must be non-empty.");
                    break;
                default:
                    throw new StandardsValidationException("Unknown or invalid standard value kind: " + Kind);
            }
        }

        public bool Equals(StandardValue other)
        {
            if (other == null || Kind != other.Kind) return false;
            switch (Kind)
            {
                case StandardValueKind.Number:
                    return NumberValue.GetValueOrDefault().Equals(other.NumberValue.GetValueOrDefault());
                case StandardValueKind.Integer:
                    return IntegerValue == other.IntegerValue;
                case StandardValueKind.Boolean:
                    return BooleanValue == other.BooleanValue;
                case StandardValueKind.Text:
                    return string.Equals(TextValue, other.TextValue, StringComparison.Ordinal);
                case StandardValueKind.Quantity:
                    return Equals(QuantityValue, other.QuantityValue);
                default:
                    return false;
            }
        }

        public override bool Equals(object obj) => Equals(obj as StandardValue);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                if (NumberValue.HasValue) hash = (hash * 397) ^ NumberValue.Value.GetHashCode();
                if (IntegerValue.HasValue) hash = (hash * 397) ^ IntegerValue.Value.GetHashCode();
                if (BooleanValue.HasValue) hash = (hash * 397) ^ BooleanValue.Value.GetHashCode();
                if (TextValue != null) hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TextValue);
                if (QuantityValue != null) hash = (hash * 397) ^ QuantityValue.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class StandardParameterKey : IEquatable<StandardParameterKey>, IComparable<StandardParameterKey>
    {
        public string Key { get; }
        public string CanonicalKey { get; }

        public StandardParameterKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new StandardsValidationException("Parameter key must be nonempty and not whitespace.");
            Key = key.Trim();
            CanonicalKey = Key.ToUpperInvariant();
        }

        public bool Equals(StandardParameterKey other)
        {
            if (other == null) return false;
            return string.Equals(CanonicalKey, other.CanonicalKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as StandardParameterKey);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalKey);

        public int CompareTo(StandardParameterKey other)
        {
            if (other == null) return 1;
            return string.Compare(CanonicalKey, other.CanonicalKey, StringComparison.Ordinal);
        }

        public override string ToString() => Key;
    }

    public sealed class StandardProvenance
    {
        public StandardSourceLayer SourceLayer { get; set; }
        public string StandardId { get; set; }
        public string StandardName { get; set; }
        public string StandardVersion { get; set; }
        public string ParameterKey { get; set; }
        public string DocumentReference { get; set; }
        public string SectionReference { get; set; }
        public string Notes { get; set; }
    }

    public sealed class StandardParameter
    {
        public string Key { get; set; }
        public StandardValue Value { get; set; }
        public StandardProvenance Provenance { get; set; }

        public StandardParameter() { }

        public StandardParameter(string key, StandardValue value, StandardProvenance provenance = null)
        {
            var paramKey = new StandardParameterKey(key);
            if (value == null)
                throw new StandardsValidationException("Parameter value cannot be null.");
            value.Validate();

            Key = paramKey.Key;
            Value = value;
            Provenance = provenance;
        }
    }

    public sealed class StandardDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string StandardId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public StandardSourceLayer SourceLayer { get; set; }
        public List<StandardParameter> Parameters { get; set; } = new List<StandardParameter>();
        public Dictionary<string, string> ProvenanceMetadata { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(StandardId))
                throw new StandardsValidationException("StandardId is required.");
            if (string.IsNullOrWhiteSpace(Name))
                throw new StandardsValidationException("Standard Name is required.");
            bool hasRevisionSpecified = !string.IsNullOrWhiteSpace(Version);
            if (!hasRevisionSpecified)
                throw new StandardsValidationException("Standard Version is required.");
            if (!Enum.IsDefined(typeof(StandardSourceLayer), SourceLayer))
                throw new StandardsValidationException("Invalid SourceLayer.");

            if (Parameters != null)
            {
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in Parameters)
                {
                    if (p == null)
                        throw new StandardsValidationException("Parameter entry cannot be null.");
                    var paramKey = new StandardParameterKey(p.Key);
                    if (!seenKeys.Add(paramKey.CanonicalKey))
                        throw new StandardsValidationException("Duplicate parameter key rejected: " + p.Key);
                    if (p.Value == null)
                        throw new StandardsValidationException("Parameter value cannot be null for key: " + p.Key);
                    p.Value.Validate();
                }
            }
        }
    }

    public sealed class StandardCandidate
    {
        public StandardValue Value { get; set; }
        public StandardProvenance Provenance { get; set; }
        public bool IsWinner { get; set; }
    }

    public sealed class ResolvedStandardValue
    {
        public string ParameterKey { get; set; }
        public StandardResolutionStatus Status { get; set; }
        public StandardValue EffectiveValue { get; set; }
        public StandardSourceLayer? EffectiveSource { get; set; }
        public string EffectiveStandardId { get; set; }
        public string EffectiveVersion { get; set; }
        public StandardProvenance EffectiveProvenance { get; set; }
        public List<StandardCandidate> Candidates { get; set; } = new List<StandardCandidate>();

        public static ResolvedStandardValue NotDefined(string parameterKey)
        {
            return new ResolvedStandardValue
            {
                ParameterKey = parameterKey,
                Status = StandardResolutionStatus.NotDefined
            };
        }
    }
}
