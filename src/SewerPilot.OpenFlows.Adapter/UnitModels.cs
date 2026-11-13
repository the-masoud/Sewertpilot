using System;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class UnitProvenance : IEquatable<UnitProvenance>
    {
        public string Source { get; set; } = "WaterObjectsFieldMetadata";
        public string FieldIdentity { get; set; }
        public string WorkingUnitIdentity { get; set; }
        public string StorageUnitIdentity { get; set; }
        public string RuntimeEvidence { get; set; }

        public UnitProvenance() { }

        public UnitProvenance(
            string source,
            string fieldIdentity,
            string workingUnitIdentity,
            string storageUnitIdentity = null,
            string runtimeEvidence = null)
        {
            Source = source ?? "WaterObjectsFieldMetadata";
            FieldIdentity = fieldIdentity ?? string.Empty;
            WorkingUnitIdentity = workingUnitIdentity ?? string.Empty;
            StorageUnitIdentity = storageUnitIdentity ?? string.Empty;
            RuntimeEvidence = runtimeEvidence ?? string.Empty;
        }

        public UnitProvenance Clone()
        {
            return new UnitProvenance(
                Source,
                FieldIdentity,
                WorkingUnitIdentity,
                StorageUnitIdentity,
                RuntimeEvidence);
        }

        public bool Equals(UnitProvenance other)
        {
            if (other == null) return false;
            return string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(FieldIdentity, other.FieldIdentity, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(WorkingUnitIdentity, other.WorkingUnitIdentity, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(StorageUnitIdentity, other.StorageUnitIdentity, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as UnitProvenance);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Source ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(FieldIdentity ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(WorkingUnitIdentity ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(StorageUnitIdentity ?? string.Empty);
                return hash;
            }
        }
    }

    public sealed class SourceUnitIdentity : IEquatable<SourceUnitIdentity>
    {
        public string Dimension { get; set; }
        public string UnitId { get; set; }
        public string UnitName { get; set; }
        public string Symbol { get; set; }
        public UnitProvenance Provenance { get; set; }

        public SourceUnitIdentity() { }

        public SourceUnitIdentity(
            string dimension,
            string unitId,
            string unitName,
            string symbol = null,
            UnitProvenance provenance = null)
        {
            Dimension = dimension ?? string.Empty;
            UnitId = unitId ?? string.Empty;
            UnitName = unitName ?? string.Empty;
            Symbol = symbol ?? string.Empty;
            Provenance = provenance?.Clone();
        }

        public SourceUnitIdentity(
            string dimension,
            int unitId,
            string unitName,
            string symbol = null,
            UnitProvenance provenance = null)
            : this(dimension, unitId.ToString(CultureInfo.InvariantCulture), unitName, symbol, provenance)
        {
        }

        public static SourceUnitIdentity Unresolved(string fieldIdentity = null)
        {
            return new SourceUnitIdentity(
                "UNRESOLVED",
                "SOURCE_NATIVE_UNRESOLVED",
                "SOURCE_NATIVE_UNRESOLVED",
                "UNRESOLVED",
                new UnitProvenance(
                    "WaterObjectsFieldMetadata",
                    fieldIdentity ?? "Unknown",
                    "SOURCE_NATIVE_UNRESOLVED",
                    "SOURCE_NATIVE_UNRESOLVED",
                    "UnresolvedSourceNative"));
        }

        public SourceUnitIdentity Clone()
        {
            return new SourceUnitIdentity(Dimension, UnitId, UnitName, Symbol, Provenance);
        }

        public bool Equals(SourceUnitIdentity other)
        {
            if (other == null) return false;
            return string.Equals(Dimension, other.Dimension, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(UnitId, other.UnitId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(UnitName, other.UnitName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Symbol, other.Symbol, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as SourceUnitIdentity);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Dimension ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(UnitId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(UnitName ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Symbol ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} [{1}:{2}]", UnitName, Dimension, Symbol);
        }
    }

    public sealed class SourceEngineeringValue : IEquatable<SourceEngineeringValue>
    {
        public double? NumericValue { get; }
        public SourceUnitIdentity Unit { get; }

        public SourceEngineeringValue(double? numericValue, SourceUnitIdentity unit)
        {
            NumericValue = numericValue;
            Unit = unit?.Clone();
        }

        public bool HasValue => NumericValue.HasValue;

        public StandardQuantity ToStandardQuantity()
        {
            if (!NumericValue.HasValue) return null;
            string u = !string.IsNullOrWhiteSpace(Unit?.Symbol) ? Unit.Symbol : Unit?.UnitName;
            if (string.IsNullOrWhiteSpace(u)) u = "dimensionless";
            return new StandardQuantity(NumericValue.Value, u);
        }

        public SourceEngineeringValue Clone()
        {
            return new SourceEngineeringValue(NumericValue, Unit);
        }

        public bool Equals(SourceEngineeringValue other)
        {
            if (other == null) return false;
            if (NumericValue.HasValue != other.NumericValue.HasValue) return false;
            if (NumericValue.HasValue && !NumericValue.Value.Equals(other.NumericValue.Value)) return false;
            if (Unit == null && other.Unit == null) return true;
            if (Unit == null || other.Unit == null) return false;
            return Unit.Equals(other.Unit);
        }

        public override bool Equals(object obj) => Equals(obj as SourceEngineeringValue);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = NumericValue.HasValue ? NumericValue.Value.GetHashCode() : 0;
                hash = (hash * 397) ^ (Unit != null ? Unit.GetHashCode() : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            if (!NumericValue.HasValue) return "NULL";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}",
                NumericValue.Value.ToString("R", CultureInfo.InvariantCulture),
                Unit?.Symbol ?? Unit?.UnitName ?? string.Empty);
        }
    }

    public sealed class FieldUnitMetadataDto
    {
        public string FieldName { get; set; }
        public string DeclaringType { get; set; }
        public string RuntimeType { get; set; }
        public bool Unitized { get; set; }
        public bool IsUnitized { get => Unitized; set => Unitized = value; }
        public string Dimension { get; set; }
        public string WorkingUnitId { get; set; }
        public string WorkingUnitName { get; set; }
        public string WorkingUnitSymbol { get; set; }
        public SourceUnitIdentity WorkingUnit { get; set; }
        public string StorageUnitId { get; set; }
        public string StorageUnitName { get; set; }
        public string StorageUnitSymbol { get; set; }
        public SourceUnitIdentity StorageUnit { get; set; }
        public string ExactMembersUsed { get; set; }
        public string InspectionMember { get => ExactMembersUsed; set => ExactMembersUsed = value; }
    }
}
