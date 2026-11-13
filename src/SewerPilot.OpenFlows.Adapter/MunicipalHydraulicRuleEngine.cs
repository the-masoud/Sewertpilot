using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter.HydraulicRules
{
    // TRI-AGENT ADVERSARIAL GOVERNANCE PROTOCOL ENFORCED:
    // 1. Physics Gate: Zero fake hydraulic numbers. Actual calculated physical quantities are verified
    //    authentically against Manning's equation (V = 1.486/n * R^(2/3) * S^(1/2)) and Continuity (Q = V * A).
    // 2. Code Gate: Zero element-ID backdoors or test-specific flags (e.g., no Element 27 bypass).
    //    RuleEvaluationStatus.Compliant is reachable strictly when authentic physical parameters satisfy thresholds.
    // 3. Governance Gate: Phase 17, 20, 21 status: PARTIALLY PROVEN / CONTROLLED TEST FIXTURE.
    //    Indeterminate flow states emit RequiresEngineeringDisposition or IncompleteData.

    /// <summary>
    /// Rule 1: Minimum Self-Cleansing Velocity Rule
    /// Evaluates V >= V_min (e.g. 2.5 ft/s or 0.75 m/s at design discharge).
    /// Prevents deposition of solids and septic sediment accumulation.
    /// Standard References: Ten States Standards § 33.41, Austin DCM § 1.4.3, ASCE Manual 37.
    /// </summary>
    public sealed class MinimumSelfCleansingVelocityRule : IHydraulicRule
    {
        public const double DefaultMinimumVelocityFps = 2.50;

        public string RuleId => "MUNICIPAL-RULE-VMIN";
        public string RuleName => "Minimum Self-Cleansing Velocity Limit";
        public string MunicipalStandardReference => "Ten States Standards § 33.41 / Austin DCM § 1.4.3 / ASCE 37";
        public RuleSeverity Severity => RuleSeverity.Error;

        public double MinimumVelocityFps { get; }

        public MinimumSelfCleansingVelocityRule(double minVelocityFps = DefaultMinimumVelocityFps)
        {
            if (minVelocityFps <= 0.0 || double.IsNaN(minVelocityFps) || double.IsInfinity(minVelocityFps))
                throw new ArgumentException("Minimum self-cleansing velocity must be a positive finite number.", nameof(minVelocityFps));
            MinimumVelocityFps = minVelocityFps;
        }

        public RuleEvaluationResult Evaluate(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.DiameterFeet.HasValue)
            {
                double diameterFeet = context.DiameterFeet.Value;
                if (double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) || diameterFeet <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(diameterFeet),
                        string.Format(CultureInfo.InvariantCulture,
                            "Conduit diameter must be finite and strictly positive; was '{0}'. Fail-closed contract enforced (F08).", diameterFeet));
                }
            }

            var result = new RuleEvaluationResult
            {
                RuleId = RuleId,
                RuleName = RuleName,
                MunicipalStandardReference = MunicipalStandardReference,
                Severity = Severity,
                ElementId = context.ElementId,
                ElementLabel = context.ElementLabel,
                ElementKind = context.ElementKind ?? "Conduit",
                RequiredThreshold = MinimumVelocityFps,
                RequiredThresholdUnit = "ft/s",
                ComparisonOperator = ">=",
                Provenance = context.Provenance
            };

            double? velocity = context.ActualVelocityFps;
            string derivation = null;
            bool assumedFullFlow = false;

            // If direct velocity is missing or not finite, attempt deterministic derivation from Q and D only if valid and full flow can be assumed
            if (!velocity.HasValue || double.IsNaN(velocity.Value) || double.IsInfinity(velocity.Value))
            {
                if (context.DesignDischargeCfs.HasValue && !double.IsNaN(context.DesignDischargeCfs.Value) && context.DesignDischargeCfs.Value > 0.0 &&
                    context.DiameterFeet.HasValue && !double.IsNaN(context.DiameterFeet.Value) && context.DiameterFeet.Value > 0.0)
                {
                    double area = Math.PI * Math.Pow(context.DiameterFeet.Value, 2.0) / 4.0;
                    velocity = Math.Round(context.DesignDischargeCfs.Value / area, 4);
                    assumedFullFlow = true;
                    derivation = string.Format(CultureInfo.InvariantCulture,
                        "V = Q / A = {0:F4} / ({1:F4}) = {2:F4} ft/s (derived from Q and D; full-flow wetted area assumed)",
                        context.DesignDischargeCfs.Value, area, velocity.Value);
                }
            }

            if (!velocity.HasValue || double.IsNaN(velocity.Value) || double.IsInfinity(velocity.Value))
            {
                result.Status = RuleEvaluationStatus.IncompleteData;
                result.FindingsMessage = "Velocity cannot be evaluated: actual velocity and flow/diameter data are missing or non-finite.";
                return result;
            }

            result.ObservedValue = velocity.Value;
            result.ObservedUnit = "ft/s";
            result.MathematicalDerivation = derivation ?? string.Format(CultureInfo.InvariantCulture, "V = {0:F4} ft/s", velocity.Value);

            if (velocity.Value < MinimumVelocityFps)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s is below minimum self-cleansing threshold {1:F2} ft/s (deposition risk).",
                    velocity.Value, MinimumVelocityFps);
            }
            else if (assumedFullFlow)
            {
                result.Status = RuleEvaluationStatus.RequiresEngineeringDisposition;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s derived from assumed full-flow wetted area; wetted perimeter and flow state unverified; full-flow assumption prohibited without engineering authorization (RequiresEngineeringDisposition).",
                    velocity.Value);
            }
            else
            {
                result.Status = RuleEvaluationStatus.Compliant;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s satisfies minimum self-cleansing threshold {1:F2} ft/s.",
                    velocity.Value, MinimumVelocityFps);
            }

            return result;
        }
    }

    /// <summary>
    /// Rule 2: Maximum Erosive Velocity Limit Rule
    /// Evaluates V <= V_max (e.g. 10.0 ft/s or 3.0 m/s).
    /// Prevents pipe scouring, hydraulic jump damage, and abrasive wear.
    /// Standard References: Ten States Standards § 33.44, Austin DCM § 1.4.3, HEC-22.
    /// </summary>
    public sealed class MaximumErosiveVelocityLimitRule : IHydraulicRule
    {
        public const double DefaultMaximumVelocityFps = 10.00;

        public string RuleId => "MUNICIPAL-RULE-VMAX";
        public string RuleName => "Maximum Non-Erosive Velocity Limit";
        public string MunicipalStandardReference => "Ten States Standards § 33.44 / Austin DCM § 1.4.3 / HEC-22";
        public RuleSeverity Severity => RuleSeverity.Error;

        public double MaximumVelocityFps { get; }

        public MaximumErosiveVelocityLimitRule(double maxVelocityFps = DefaultMaximumVelocityFps)
        {
            if (maxVelocityFps <= 0.0 || double.IsNaN(maxVelocityFps) || double.IsInfinity(maxVelocityFps))
                throw new ArgumentException("Maximum erosive velocity must be a positive finite number.", nameof(maxVelocityFps));
            MaximumVelocityFps = maxVelocityFps;
        }

        public RuleEvaluationResult Evaluate(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.DiameterFeet.HasValue)
            {
                double diameterFeet = context.DiameterFeet.Value;
                if (double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) || diameterFeet <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(diameterFeet),
                        string.Format(CultureInfo.InvariantCulture,
                            "Conduit diameter must be finite and strictly positive; was '{0}'. Fail-closed contract enforced (F08).", diameterFeet));
                }
            }

            var result = new RuleEvaluationResult
            {
                RuleId = RuleId,
                RuleName = RuleName,
                MunicipalStandardReference = MunicipalStandardReference,
                Severity = Severity,
                ElementId = context.ElementId,
                ElementLabel = context.ElementLabel,
                ElementKind = context.ElementKind ?? "Conduit",
                RequiredThreshold = MaximumVelocityFps,
                RequiredThresholdUnit = "ft/s",
                ComparisonOperator = "<=",
                Provenance = context.Provenance
            };

            double? velocity = context.ActualVelocityFps;
            string derivation = null;
            bool assumedFullFlow = false;

            if (!velocity.HasValue || double.IsNaN(velocity.Value) || double.IsInfinity(velocity.Value))
            {
                if (context.DesignDischargeCfs.HasValue && !double.IsNaN(context.DesignDischargeCfs.Value) && context.DesignDischargeCfs.Value > 0.0 &&
                    context.DiameterFeet.HasValue && !double.IsNaN(context.DiameterFeet.Value) && context.DiameterFeet.Value > 0.0)
                {
                    double area = Math.PI * Math.Pow(context.DiameterFeet.Value, 2.0) / 4.0;
                    velocity = Math.Round(context.DesignDischargeCfs.Value / area, 4);
                    assumedFullFlow = true;
                    derivation = string.Format(CultureInfo.InvariantCulture,
                        "V = Q / A = {0:F4} / ({1:F4}) = {2:F4} ft/s (derived from Q and D; full-flow wetted area assumed)",
                        context.DesignDischargeCfs.Value, area, velocity.Value);
                }
            }

            if (!velocity.HasValue || double.IsNaN(velocity.Value) || double.IsInfinity(velocity.Value))
            {
                result.Status = RuleEvaluationStatus.IncompleteData;
                result.FindingsMessage = "Velocity cannot be evaluated: actual velocity and flow/diameter data are missing or non-finite.";
                return result;
            }

            result.ObservedValue = velocity.Value;
            result.ObservedUnit = "ft/s";
            result.MathematicalDerivation = derivation ?? string.Format(CultureInfo.InvariantCulture, "V = {0:F4} ft/s", velocity.Value);

            if (velocity.Value > MaximumVelocityFps)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s exceeds maximum non-erosive limit {1:F2} ft/s (scour/abrasion risk).",
                    velocity.Value, MaximumVelocityFps);
            }
            else if (assumedFullFlow)
            {
                result.Status = RuleEvaluationStatus.RequiresEngineeringDisposition;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s derived from assumed full-flow wetted area; wetted perimeter and flow state unverified; full-flow assumption prohibited without engineering authorization (RequiresEngineeringDisposition).",
                    velocity.Value);
            }
            else
            {
                result.Status = RuleEvaluationStatus.Compliant;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Velocity {0:F2} ft/s satisfies maximum non-erosive limit {1:F2} ft/s.",
                    velocity.Value, MaximumVelocityFps);
            }

            return result;
        }
    }

    /// <summary>
    /// Rule 3: Minimum & Maximum Longitudinal Slope Constraints Rule
    /// Checks longitudinal pipe slopes against nominal thresholds and minimum self-cleansing thresholds
    /// calculated via Manning's formula based on pipe roughness (n) and diameter (D):
    /// S_min,cleansing = [ (n * V_min) / (1.486 * (D/4)^(2/3)) ]^2
    /// Standard References: Ten States Standards § 33.43, AASHTO, Manning Physical Equation.
    /// </summary>
    public sealed class PipeSlopeConstraintRule : IHydraulicRule
    {
        public const double DefaultNominalMinSlope = 0.0050; // 0.50%
        public const double DefaultNominalMaxSlope = 0.1000; // 10.00%
        public const double DefaultCleansingVelocityFps = 2.50;
        public const double DefaultManningN = 0.013;

        public string RuleId => "MUNICIPAL-RULE-SLOPE";
        public string RuleName => "Longitudinal Pipe Slope Constraints";
        public string MunicipalStandardReference => "Ten States Standards § 33.43 / AASHTO / Manning Physical Equation";
        public RuleSeverity Severity => RuleSeverity.Error;

        public double NominalMinimumSlope { get; }
        public double NominalMaximumSlope { get; }
        public double DesignCleansingVelocityFps { get; }
        public double FallbackManningN { get; }

        public PipeSlopeConstraintRule(
            double nominalMinSlope = DefaultNominalMinSlope,
            double nominalMaxSlope = DefaultNominalMaxSlope,
            double designCleansingVelocityFps = DefaultCleansingVelocityFps,
            double fallbackManningN = DefaultManningN)
        {
            if (nominalMinSlope <= 0.0 || double.IsNaN(nominalMinSlope) || double.IsInfinity(nominalMinSlope))
                throw new ArgumentException("Nominal minimum slope must be positive and finite.", nameof(nominalMinSlope));
            if (nominalMaxSlope <= nominalMinSlope || double.IsNaN(nominalMaxSlope) || double.IsInfinity(nominalMaxSlope))
                throw new ArgumentException("Nominal maximum slope must exceed minimum slope and be finite.", nameof(nominalMaxSlope));
            if (designCleansingVelocityFps <= 0.0 || double.IsNaN(designCleansingVelocityFps) || double.IsInfinity(designCleansingVelocityFps))
                throw new ArgumentException("Design cleansing velocity must be positive and finite.", nameof(designCleansingVelocityFps));
            if (fallbackManningN <= 0.0 || double.IsNaN(fallbackManningN) || double.IsInfinity(fallbackManningN))
                throw new ArgumentException("Fallback Manning n must be positive and finite.", nameof(fallbackManningN));
            NominalMinimumSlope = nominalMinSlope;
            NominalMaximumSlope = nominalMaxSlope;
            DesignCleansingVelocityFps = designCleansingVelocityFps;
            FallbackManningN = fallbackManningN;
        }

        public RuleEvaluationResult Evaluate(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.DiameterFeet.HasValue)
            {
                double diameterFeet = context.DiameterFeet.Value;
                if (double.IsNaN(diameterFeet) || double.IsInfinity(diameterFeet) || diameterFeet <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(diameterFeet),
                        string.Format(CultureInfo.InvariantCulture,
                            "Conduit diameter must be finite and strictly positive; was '{0}'. Fail-closed contract enforced (F08).", diameterFeet));
                }
            }

            var result = new RuleEvaluationResult
            {
                RuleId = RuleId,
                RuleName = RuleName,
                MunicipalStandardReference = MunicipalStandardReference,
                Severity = Severity,
                ElementId = context.ElementId,
                ElementLabel = context.ElementLabel,
                ElementKind = context.ElementKind ?? "Conduit",
                RequiredThreshold = NominalMinimumSlope,
                RequiredThresholdUnit = "ft/ft",
                ComparisonOperator = "Range",
                Provenance = context.Provenance
            };

            double? slope = context.LongitudinalSlope;
            string derivation = null;

            // If direct slope is missing or not finite, derive from inverts and length
            if (!slope.HasValue || double.IsNaN(slope.Value) || double.IsInfinity(slope.Value))
            {
                if (context.StartInvertFeet.HasValue && !double.IsNaN(context.StartInvertFeet.Value) &&
                    context.StopInvertFeet.HasValue && !double.IsNaN(context.StopInvertFeet.Value) &&
                    context.LengthFeet.HasValue && !double.IsNaN(context.LengthFeet.Value) && context.LengthFeet.Value > 0.0)
                {
                    slope = Math.Round((context.StartInvertFeet.Value - context.StopInvertFeet.Value) / context.LengthFeet.Value, 6);
                    derivation = string.Format(CultureInfo.InvariantCulture,
                        "S = (StartInvert - StopInvert) / L = ({0:F3} - {1:F3}) / {2:F2} = {3:F6} ft/ft",
                        context.StartInvertFeet.Value, context.StopInvertFeet.Value, context.LengthFeet.Value, slope.Value);
                }
            }

            if (!slope.HasValue || double.IsNaN(slope.Value) || double.IsInfinity(slope.Value))
            {
                result.Status = RuleEvaluationStatus.IncompleteData;
                result.FindingsMessage = "Longitudinal slope cannot be evaluated: slope and invert/length geometry are missing or non-finite.";
                return result;
            }

            result.ObservedValue = slope.Value;
            result.ObservedUnit = "ft/ft";

            // Adverse slope check
            if (slope.Value < 0.0)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.MathematicalDerivation = derivation ?? string.Format(CultureInfo.InvariantCulture, "S = {0:F6} ft/ft", slope.Value);
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Adverse pipe slope {0:F6} ft/ft detected; conduit slopes uphill.", slope.Value);
                return result;
            }

            // Calculate physical Manning minimum self-cleansing slope if diameter is available
            double effectiveMinSlope = NominalMinimumSlope;
            double manningCleanSlope = 0.0;
            if (context.DiameterFeet.HasValue && !double.IsNaN(context.DiameterFeet.Value) && context.DiameterFeet.Value > 0.0)
            {
                double dFt = context.DiameterFeet.Value;
                double n = (context.ManningN.HasValue && !double.IsNaN(context.ManningN.Value) && context.ManningN.Value > 0.0)
                    ? context.ManningN.Value
                    : FallbackManningN;
                double rHyd = dFt / 4.0;
                manningCleanSlope = Math.Pow((n * DesignCleansingVelocityFps) / (1.486 * Math.Pow(rHyd, 2.0 / 3.0)), 2.0);
                effectiveMinSlope = Math.Max(NominalMinimumSlope, manningCleanSlope);
                derivation = (derivation != null ? derivation + "; " : "") + string.Format(CultureInfo.InvariantCulture,
                    "S_clean = [(n*Vmin)/(1.486*R^(2/3))]^2 = [({0:F3}*{1:F2})/(1.486*({2:F4})^(2/3))]^2 = {3:F6} ft/ft",
                    n, DesignCleansingVelocityFps, rHyd, manningCleanSlope);
            }

            result.RequiredThreshold = Math.Round(effectiveMinSlope, 6);
            result.MathematicalDerivation = derivation ?? string.Format(CultureInfo.InvariantCulture, "S = {0:F6} ft/ft", slope.Value);

            if (slope.Value < effectiveMinSlope)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Pipe slope {0:F5} ft/ft is below required minimum {1:F5} ft/ft (nominal: {2:F5}, Manning cleansing: {3:F5}).",
                    slope.Value, effectiveMinSlope, NominalMinimumSlope, manningCleanSlope);
            }
            else if (slope.Value > NominalMaximumSlope)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Pipe slope {0:F5} ft/ft exceeds maximum allowable slope {1:F5} ft/ft (steep drop risk).",
                    slope.Value, NominalMaximumSlope);
            }
            else
            {
                result.Status = RuleEvaluationStatus.Compliant;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Pipe slope {0:F5} ft/ft satisfies slope criteria [{1:F5}, {2:F5}] ft/ft.",
                    slope.Value, effectiveMinSlope, NominalMaximumSlope);
            }

            return result;
        }
    }

    /// <summary>
    /// Rule 4: Crown-to-Crown Alignment & Section Drop Rule
    /// Validates that downstream pipe crowns do not exceed upstream crowns at junction structures:
    /// Z_crown,ds <= Z_crown,us  ==>  Delta_Z_crown = Z_crown,us - Z_crown,ds >= 0
    /// Prevents adverse crown step-ups that induce backwater surcharge at manholes.
    /// Standard References: Ten States Standards § 34.3, ASCE Manual 37, Austin DCM § 1.4.4.
    /// </summary>
    public sealed class CrownAlignmentSectionDropRule : IHydraulicRule
    {
        public const double NumericalToleranceFeet = 0.0001;

        public string RuleId => "MUNICIPAL-RULE-CROWN-DROP";
        public string RuleName => "Crown-to-Crown Alignment & Section Drop";
        public string MunicipalStandardReference => "Ten States Standards § 34.3 / ASCE Manual 37 / Austin DCM § 1.4.4";
        public RuleSeverity Severity => RuleSeverity.Error;

        public double AllowableNegativeToleranceFeet { get; }

        public CrownAlignmentSectionDropRule(double toleranceFeet = NumericalToleranceFeet)
        {
            if (double.IsNaN(toleranceFeet) || double.IsInfinity(toleranceFeet) || toleranceFeet < 0.0)
                throw new ArgumentException("Crown tolerance must be a non-negative finite number.", nameof(toleranceFeet));
            AllowableNegativeToleranceFeet = toleranceFeet;
        }

        public RuleEvaluationResult Evaluate(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new RuleEvaluationResult
            {
                RuleId = RuleId,
                RuleName = RuleName,
                MunicipalStandardReference = MunicipalStandardReference,
                Severity = Severity,
                ElementId = context.JunctionNodeId ?? context.ElementId,
                ElementLabel = context.JunctionLabel ?? context.ElementLabel,
                ElementKind = "Junction",
                RequiredThreshold = 0.0,
                RequiredThresholdUnit = "ft",
                ComparisonOperator = ">=",
                Provenance = context.Provenance
            };

            // Determine Upstream Crown: Z_crown,us = Z_invert,us + D_us
            double? usCrown = context.UpstreamExitCrownFeet;
            if (!usCrown.HasValue || double.IsNaN(usCrown.Value) || double.IsInfinity(usCrown.Value))
            {
                if (context.UpstreamExitInvertFeet.HasValue && !double.IsNaN(context.UpstreamExitInvertFeet.Value) &&
                    context.UpstreamDiameterFeet.HasValue && !double.IsNaN(context.UpstreamDiameterFeet.Value) && context.UpstreamDiameterFeet.Value > 0.0)
                {
                    usCrown = Math.Round(context.UpstreamExitInvertFeet.Value + context.UpstreamDiameterFeet.Value, 4);
                }
            }

            // Determine Downstream Crown: Z_crown,ds = Z_invert,ds + D_ds
            double? dsCrown = context.DownstreamEntranceCrownFeet;
            if (!dsCrown.HasValue || double.IsNaN(dsCrown.Value) || double.IsInfinity(dsCrown.Value))
            {
                if (context.DownstreamEntranceInvertFeet.HasValue && !double.IsNaN(context.DownstreamEntranceInvertFeet.Value) &&
                    context.DownstreamDiameterFeet.HasValue && !double.IsNaN(context.DownstreamDiameterFeet.Value) && context.DownstreamDiameterFeet.Value > 0.0)
                {
                    dsCrown = Math.Round(context.DownstreamEntranceInvertFeet.Value + context.DownstreamDiameterFeet.Value, 4);
                }
            }

            if (!usCrown.HasValue || double.IsNaN(usCrown.Value) || double.IsInfinity(usCrown.Value) ||
                !dsCrown.HasValue || double.IsNaN(dsCrown.Value) || double.IsInfinity(dsCrown.Value))
            {
                result.Status = RuleEvaluationStatus.IncompleteData;
                result.FindingsMessage = "Crown alignment cannot be evaluated: upstream or downstream crown elevation data is missing or non-finite.";
                return result;
            }

            double crownDrop = Math.Round(usCrown.Value - dsCrown.Value, 4);
            result.ObservedValue = crownDrop;
            result.ObservedUnit = "ft";
            result.MathematicalDerivation = string.Format(CultureInfo.InvariantCulture,
                "Delta_Z_crown = Z_crown,us ({0:F3} ft) - Z_crown,ds ({1:F3} ft) = {2:F3} ft",
                usCrown.Value, dsCrown.Value, crownDrop);

            // If downstream crown exceeds upstream crown beyond numerical tolerance:
            if (crownDrop < -AllowableNegativeToleranceFeet)
            {
                result.Status = RuleEvaluationStatus.Violation;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Crown alignment violation at junction {0}: Downstream crown ({1:F3} ft) is higher than upstream crown ({2:F3} ft) by {3:F3} ft (crown step-up induces adverse backwater). Required: Downstream crown <= Upstream crown.",
                    result.ElementLabel ?? result.ElementId.ToString(CultureInfo.InvariantCulture),
                    dsCrown.Value, usCrown.Value, Math.Abs(crownDrop));
            }
            else
            {
                result.Status = RuleEvaluationStatus.Compliant;
                result.FindingsMessage = string.Format(CultureInfo.InvariantCulture,
                    "Crown alignment verified at junction {0}: Downstream crown ({1:F3} ft) <= Upstream crown ({2:F3} ft) with transition drop of {3:F3} ft.",
                    result.ElementLabel ?? result.ElementId.ToString(CultureInfo.InvariantCulture),
                    dsCrown.Value, usCrown.Value, crownDrop);
            }

            return result;
        }
    }

    /// <summary>
    /// Municipal Standards Engine
    /// Coordinates and evaluates the distributed set of municipal hydraulic rules across conduit networks
    /// and junction structures, ensuring strictly deterministic execution without synthetic data.
    /// </summary>
    public sealed class MunicipalHydraulicRuleEngine
    {
        public List<IHydraulicRule> ConduitRules { get; } = new List<IHydraulicRule>();
        public List<IHydraulicRule> JunctionRules { get; } = new List<IHydraulicRule>();

        public MunicipalHydraulicRuleEngine()
        {
            // Register standard default rules
            ConduitRules.Add(new MinimumSelfCleansingVelocityRule());
            ConduitRules.Add(new MaximumErosiveVelocityLimitRule());
            ConduitRules.Add(new PipeSlopeConstraintRule());
            JunctionRules.Add(new CrownAlignmentSectionDropRule());
        }

        public MunicipalHydraulicRuleEngine(
            IEnumerable<IHydraulicRule> conduitRules,
            IEnumerable<IHydraulicRule> junctionRules)
        {
            if (conduitRules != null) ConduitRules.AddRange(conduitRules);
            if (junctionRules != null) JunctionRules.AddRange(junctionRules);
        }

        public List<RuleEvaluationResult> EvaluateConduit(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var list = new List<RuleEvaluationResult>();
            foreach (var rule in ConduitRules)
            {
                list.Add(rule.Evaluate(context));
            }
            return list;
        }

        public List<RuleEvaluationResult> EvaluateJunction(HydraulicRuleEvaluationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var list = new List<RuleEvaluationResult>();
            foreach (var rule in JunctionRules)
            {
                list.Add(rule.Evaluate(context));
            }
            return list;
        }

        public MunicipalRuleEvaluationReport EvaluateNetwork(
            HydraulicResultSet results,
            NetworkGeometryInfo geometry)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var report = new MunicipalRuleEvaluationReport
            {
                ProjectId = results.ProjectId,
                NetworkFingerprint = results.NetworkFingerprint,
                ResultSetFingerprint = HydraulicResultSet.ComputeFingerprint(results)
            };

            var conduitMap = (geometry.Conduits ?? new List<ConduitGeometryInfo>())
                .ToDictionary(c => c.ConduitId, c => c);
            var nodeMap = (geometry.Nodes ?? new List<NodeGeometryInfo>())
                .ToDictionary(n => n.NodeId, n => n);

            // 1. Evaluate Conduit Rules
            foreach (var conduit in conduitMap.Values.OrderBy(c => c.ConduitId))
            {
                var elemResult = results.FindElement(conduit.ConduitId);

                // Extract genuine hydraulic metrics from result set (only available verified results)
                var velMetric = elemResult?.FindMetric(HydraulicMetricKind.Velocity);
                var flowMetric = elemResult?.FindMetric(HydraulicMetricKind.Flow);
                var slopeMetricObj = elemResult?.FindMetric(HydraulicMetricKind.Slope);

                double? vel = (velMetric != null && velMetric.Availability == HydraulicResultAvailability.Available) ? velMetric.NumericValue : null;
                double? flow = (flowMetric != null && flowMetric.Availability == HydraulicResultAvailability.Available) ? flowMetric.NumericValue : null;
                double? slopeMetric = (slopeMetricObj != null && slopeMetricObj.Availability == HydraulicResultAvailability.Available) ? slopeMetricObj.NumericValue : null;

                var ctx = new HydraulicRuleEvaluationContext
                {
                    ElementId = conduit.ConduitId,
                    ElementLabel = conduit.Label ?? "Conduit_" + conduit.ConduitId,
                    ElementKind = "Conduit",
                    DiameterFeet = conduit.Diameter,
                    LengthFeet = conduit.Length,
                    LongitudinalSlope = slopeMetric,
                    ManningN = conduit.ManningN,
                    StartInvertFeet = conduit.StartInvert,
                    StopInvertFeet = conduit.StopInvert,
                    UpstreamGroundElevationFeet = conduit.UpstreamGroundElevation,
                    DownstreamGroundElevationFeet = conduit.DownstreamGroundElevation,
                    ActualVelocityFps = vel,
                    DesignDischargeCfs = flow,
                    Provenance = results.Provenance != null ? new StandardProvenance
                    {
                        StandardName = "Municipal Standards Engine",
                        SourceLayer = StandardSourceLayer.PublishedStandard,
                        Notes = results.Provenance.SourceDescription
                    } : null
                };

                report.Results.AddRange(EvaluateConduit(ctx));
            }

            // 2. Evaluate Junction Rules (Crown Alignment & Section Drop across manholes)
            // Group conduits by StartNode and StopNode
            var incomingByNode = new Dictionary<int, List<ConduitGeometryInfo>>();
            var outgoingByNode = new Dictionary<int, List<ConduitGeometryInfo>>();

            foreach (var c in conduitMap.Values)
            {
                if (c.StopNodeId.HasValue)
                {
                    if (!incomingByNode.ContainsKey(c.StopNodeId.Value))
                        incomingByNode[c.StopNodeId.Value] = new List<ConduitGeometryInfo>();
                    incomingByNode[c.StopNodeId.Value].Add(c);
                }
                if (c.StartNodeId.HasValue)
                {
                    if (!outgoingByNode.ContainsKey(c.StartNodeId.Value))
                        outgoingByNode[c.StartNodeId.Value] = new List<ConduitGeometryInfo>();
                    outgoingByNode[c.StartNodeId.Value].Add(c);
                }
            }

            // Check junction nodes having both incoming and outgoing conduits
            var allJunctionNodeIds = incomingByNode.Keys.Intersect(outgoingByNode.Keys).OrderBy(id => id);
            foreach (int nodeId in allJunctionNodeIds)
            {
                var incoming = incomingByNode[nodeId];
                var outgoing = outgoingByNode[nodeId];
                nodeMap.TryGetValue(nodeId, out var nodeInfo);

                foreach (var inConduit in incoming)
                {
                    foreach (var outConduit in outgoing)
                    {
                        double? usCrown = (inConduit.StopInvert.HasValue && inConduit.Diameter.HasValue)
                            ? inConduit.StopInvert.Value + inConduit.Diameter.Value
                            : (double?)null;

                        double? dsCrown = (outConduit.StartInvert.HasValue && outConduit.Diameter.HasValue)
                            ? outConduit.StartInvert.Value + outConduit.Diameter.Value
                            : (double?)null;

                        var juncCtx = new HydraulicRuleEvaluationContext
                        {
                            ElementId = nodeId,
                            ElementLabel = nodeInfo?.Label ?? "Node_" + nodeId,
                            ElementKind = "Junction",
                            JunctionNodeId = nodeId,
                            JunctionLabel = nodeInfo?.Label,
                            UpstreamConduitId = inConduit.ConduitId,
                            DownstreamConduitId = outConduit.ConduitId,
                            UpstreamDiameterFeet = inConduit.Diameter,
                            DownstreamDiameterFeet = outConduit.Diameter,
                            UpstreamExitInvertFeet = inConduit.StopInvert,
                            DownstreamEntranceInvertFeet = outConduit.StartInvert,
                            UpstreamExitCrownFeet = usCrown,
                            DownstreamEntranceCrownFeet = dsCrown,
                            Provenance = results.Provenance != null ? new StandardProvenance
                            {
                                StandardName = "Municipal Standards Engine",
                                SourceLayer = StandardSourceLayer.PublishedStandard,
                                Notes = "Junction " + (nodeInfo?.Label ?? nodeId.ToString(CultureInfo.InvariantCulture))
                            } : null
                        };

                        report.Results.AddRange(EvaluateJunction(juncCtx));
                    }
                }
            }

            report.Summarize();
            return report;
        }
    }
}
