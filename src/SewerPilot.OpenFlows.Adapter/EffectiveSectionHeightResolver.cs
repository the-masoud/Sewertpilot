using System;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class EffectiveSectionHeightResolution
    {
        public double? Value { get; }
        public SourceUnitIdentity Unit { get; }
        public NetworkReadPropertyStatus Status { get; }
        public string Derivation { get; }
        public string SourceEvidence { get; }
        public string InconsistencyReason { get; }

        public bool IsProven => Status == NetworkReadPropertyStatus.PROVEN && Value.HasValue && string.IsNullOrEmpty(InconsistencyReason);
        public bool IsConflict => !string.IsNullOrEmpty(InconsistencyReason) && InconsistencyReason.IndexOf("CONFLICTING_GEOMETRY_EVIDENCE", StringComparison.OrdinalIgnoreCase) >= 0;
        public double? EffectiveHeight => Value;
        public SourceUnitIdentity EffectiveHeightUnit => Unit;
        public string Provenance => Derivation;

        public EffectiveSectionHeightResolution(
            double? value,
            SourceUnitIdentity unit,
            NetworkReadPropertyStatus status,
            string derivation,
            string sourceEvidence,
            string inconsistencyReason = null)
        {
            Value = value;
            Unit = unit;
            Status = status;
            Derivation = derivation ?? string.Empty;
            SourceEvidence = sourceEvidence ?? string.Empty;
            InconsistencyReason = inconsistencyReason;
        }

        public static EffectiveSectionHeightResolution Unresolved(string reason) =>
            new EffectiveSectionHeightResolution(null, null, NetworkReadPropertyStatus.SEMANTICS_UNRESOLVED, string.Empty, string.Empty, reason);

        public static EffectiveSectionHeightResolution NotAvailable(string reason) =>
            new EffectiveSectionHeightResolution(null, null, NetworkReadPropertyStatus.NOT_AVAILABLE, string.Empty, string.Empty, reason);
    }

    public static class EffectiveSectionHeightResolver
    {
        private static readonly SourceUnitIdentity FeetIdentity = new SourceUnitIdentity("length", "Feet", "feet", "ft");

        public static EffectiveSectionHeightResolution Resolve(
            string shape,
            double? diameter,
            SourceUnitIdentity diameterUnit,
            string diameterClassification,
            double? rise,
            SourceUnitIdentity riseUnit,
            string riseClassification)
        {
            return Resolve(0, shape, diameter, diameterUnit, diameterClassification, rise, riseUnit, riseClassification);
        }

        public static bool IsFeetUnit(SourceUnitIdentity unit)
        {
            if (unit == null) return false;
            return FeetIdentity.Equals(unit) ||
                   (string.Equals(unit.Dimension, "length", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(unit.UnitId, "Feet", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(unit.UnitName, "feet", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(unit.Symbol, "ft", StringComparison.OrdinalIgnoreCase)));
        }

        public static bool IsLengthDimension(SourceUnitIdentity unit)
        {
            if (unit == null) return false;
            return string.Equals(unit.Dimension, "length", StringComparison.OrdinalIgnoreCase);
        }

        public static EffectiveSectionHeightResolution Resolve(
            int conduitId,
            string shape,
            double? diameter,
            SourceUnitIdentity diameterUnit,
            string diameterClassification,
            double? rise,
            SourceUnitIdentity riseUnit,
            string riseClassification)
        {
            bool isCircular = !string.IsNullOrWhiteSpace(shape) &&
                string.Equals(shape.Trim(), "CircularPipe", StringComparison.OrdinalIgnoreCase);

            if (isCircular)
            {
                // 1. Validate diameter presence, finiteness and positivity
                if (!diameter.HasValue)
                    return EffectiveSectionHeightResolution.Unresolved("MISSING_DIAMETER: Circular pipe requires diameter observation for conduit " + conduitId);

                if (double.IsNaN(diameter.Value) || double.IsInfinity(diameter.Value) || diameter.Value <= 0)
                    return EffectiveSectionHeightResolution.Unresolved("NON_POSITIVE_OR_NON_FINITE_DIAMETER: Conduit " + conduitId + " diameter=" + diameter.Value);

                // 2. Validate diameter classification
                bool diameterProven = string.Equals(diameterClassification, NetworkReadPropertyStatus.PROVEN.ToString(), StringComparison.OrdinalIgnoreCase);
                if (!diameterProven)
                    return EffectiveSectionHeightResolution.Unresolved("DIAMETER_NOT_PROVEN: Diameter classification is not proven for conduit " + conduitId);

                // 3. Validate diameter unit
                if (diameterUnit == null || !IsLengthDimension(diameterUnit) || !IsFeetUnit(diameterUnit))
                    return EffectiveSectionHeightResolution.Unresolved("DIAMETER_INCOMPATIBLE_UNIT: Circular diameter requires Feet unit for conduit " + conduitId);

                // 4. Validate native Rise consistency
                if (rise.HasValue && rise.Value > 0)
                {
                    if (double.IsNaN(rise.Value) || double.IsInfinity(rise.Value) ||
                        (riseUnit != null && !IsFeetUnit(riseUnit)) ||
                        Math.Abs(rise.Value - diameter.Value) > 1e-4)
                    {
                        return EffectiveSectionHeightResolution.Unresolved(
                            "CONFLICTING_GEOMETRY_EVIDENCE: Positive native Rise (" + rise.Value + ") conflicts with circular Diameter (" + diameter.Value + ") on conduit " + conduitId);
                    }

                    return new EffectiveSectionHeightResolution(
                        diameter.Value,
                        diameterUnit,
                        NetworkReadPropertyStatus.PROVEN,
                        "VERIFIED_CIRCULAR_SECTION_HEIGHT",
                        "Conduit " + conduitId + ": Shape=CircularPipe, Diameter=" + diameter.Value + " " + diameterUnit.UnitName + ", Native Rise=" + rise.Value + " verified");
                }

                if (rise.HasValue && (rise.Value < 0 || double.IsNaN(rise.Value) || double.IsInfinity(rise.Value)))
                {
                    return EffectiveSectionHeightResolution.Unresolved(
                        "CONFLICTING_GEOMETRY_EVIDENCE: Non-finite or negative native Rise (" + rise.Value + ") on conduit " + conduitId);
                }

                // Raw Rise is 0 or null: unproven zero placeholder preserved, effective height derived from diameter
                return new EffectiveSectionHeightResolution(
                    diameter.Value,
                    diameterUnit,
                    NetworkReadPropertyStatus.PROVEN,
                    "DERIVED_FROM_CIRCULAR_DIAMETER",
                    "Conduit " + conduitId + ": Shape=CircularPipe, Diameter=" + diameter.Value + " " + diameterUnit.UnitName + ", Raw Rise=" + (rise.HasValue ? rise.Value.ToString() : "null") + " (unproven zero placeholder preserved)");
            }

            // Non-circular or unknown shape: circular derivation does not apply
            if (rise.HasValue && rise.Value > 0 && !double.IsNaN(rise.Value) && !double.IsInfinity(rise.Value) &&
                IsFeetUnit(riseUnit) &&
                string.Equals(riseClassification, NetworkReadPropertyStatus.PROVEN.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new EffectiveSectionHeightResolution(
                    rise.Value,
                    riseUnit,
                    NetworkReadPropertyStatus.PROVEN,
                    "NATIVE_RISE_OBSERVATION",
                    "Conduit " + conduitId + ": Shape=" + (shape ?? "null") + ", Native Rise=" + rise.Value + " " + riseUnit.UnitName);
            }

            string failReason = string.IsNullOrWhiteSpace(shape)
                ? "UNKNOWN_SHAPE: Section height cannot be derived without proven circular shape for conduit " + conduitId
                : "NON_CIRCULAR_SHAPE: Conduit " + conduitId + " shape=" + shape + " requires explicit native Rise observation";

            return EffectiveSectionHeightResolution.NotAvailable(failReason);
        }
    }
}
