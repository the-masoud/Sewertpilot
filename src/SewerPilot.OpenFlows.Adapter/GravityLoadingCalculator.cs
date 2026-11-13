using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class GravityLoadingCalculator
    {
        public static GravityLoadingResult Calculate(GravityLoadingCalculationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var result = new GravityLoadingResult();
            var validation = result.ValidationResult;

            // 1. Check for ambiguous dual basis
            if (input.PopulationBasis != null && input.PopulationEquivalentBasis != null)
            {
                validation.AddError("AMBIGUOUS_LOADING_BASIS", "Basis", "Both Population and PopulationEquivalent were supplied simultaneously. Exactly one basis must be used.");
                result.Status = GravityLoadingCalculationStatus.Failed;
                return result;
            }

            var basis = input.PopulationBasis ?? input.PopulationEquivalentBasis;
            result.Basis = basis?.Clone();
            result.GenerationRate = input.GenerationRate?.Clone();
            result.PeakFactor = input.PeakFactor?.Clone();

            bool isComplete = true;

            // 2. Validate basis if present
            if (basis != null)
            {
                if (double.IsNaN(basis.Value) || double.IsInfinity(basis.Value))
                {
                    validation.AddError("NON_FINITE_POPULATION_BASIS", "Basis.Value", "Population/PE basis value must be finite.");
                }
                else if (basis.Value < 0)
                {
                    validation.AddError("NEGATIVE_POPULATION_BASIS", "Basis.Value", "Population/PE basis value cannot be negative.");
                }
            }
            else
            {
                isComplete = false;
            }

            // 3. Validate generation rate
            if (basis != null && input.GenerationRate == null)
            {
                validation.AddWarning("MISSING_GENERATION_RATE", "GenerationRate", "Population basis is supplied but WastewaterGenerationRate is missing.");
                isComplete = false;
            }
            else if (input.GenerationRate != null)
            {
                var q = input.GenerationRate.Quantity;
                if (q == null || double.IsNaN(q.Value) || double.IsInfinity(q.Value))
                {
                    validation.AddError("INVALID_GENERATION_RATE", "GenerationRate.Quantity", "Generation rate quantity must be finite.");
                }
                else if (q.Value < 0)
                {
                    validation.AddError("NEGATIVE_GENERATION_RATE", "GenerationRate.Quantity", "Generation rate quantity cannot be negative.");
                }
                else if (string.IsNullOrWhiteSpace(q.Unit))
                {
                    validation.AddError("MISSING_GENERATION_RATE_UNIT", "GenerationRate.Quantity.Unit", "Generation rate unit is required.");
                }

                if (basis != null && input.GenerationRate.BasisKind != basis.Kind)
                {
                    validation.AddError("BASIS_KIND_MISMATCH", "GenerationRate.BasisKind",
                        string.Format(CultureInfo.InvariantCulture, "Generation rate basis kind ({0}) does not match supplied population basis kind ({1}).",
                        input.GenerationRate.BasisKind, basis.Kind));
                }
            }

            // 4. Validate peak factor
            if (input.PeakFactor != null)
            {
                if (double.IsNaN(input.PeakFactor.Value) || double.IsInfinity(input.PeakFactor.Value) || input.PeakFactor.Value <= 0)
                {
                    validation.AddError("INVALID_PEAK_FACTOR", "PeakFactor.Value", "Peak factor must be finite and strictly greater than zero.");
                }
            }
            else
            {
                isComplete = false;
            }

            // 5. Aggregate and validate contributions & known point loads
            var allContributions = new List<GravityLoadingContribution>();
            if (input.Contributions != null)
            {
                foreach (var c in input.Contributions)
                {
                    if (c != null) allContributions.Add(c.Clone());
                }
            }

            if (input.KnownPointLoads != null)
            {
                var seenPointLoadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kp in input.KnownPointLoads)
                {
                    if (kp == null) continue;
                    if (string.IsNullOrWhiteSpace(kp.LoadId))
                    {
                        validation.AddError("MISSING_POINT_LOAD_ID", "KnownPointLoads", "Point load ID is required.");
                    }
                    else if (!seenPointLoadIds.Add(kp.LoadId.Trim()))
                    {
                        validation.AddError("DUPLICATE_POINT_LOAD_ID", "KnownPointLoads." + kp.LoadId, "Duplicate point load ID: " + kp.LoadId);
                    }
                    allContributions.Add(kp.ToContribution());
                }
            }

            var seenContributionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allContributions)
            {
                if (string.IsNullOrWhiteSpace(c.ContributionId))
                {
                    validation.AddError("MISSING_CONTRIBUTION_ID", "Contributions", "Contribution ID is required.");
                }
                else if (!seenContributionIds.Add(c.ContributionId.Trim()))
                {
                    validation.AddError("DUPLICATE_CONTRIBUTION_ID", "Contributions." + c.ContributionId, "Duplicate contribution ID: " + c.ContributionId);
                }

                if (c.Flow == null || double.IsNaN(c.Flow.Value) || double.IsInfinity(c.Flow.Value))
                {
                    validation.AddError("INVALID_CONTRIBUTION_FLOW", "Contributions." + c.ContributionId, "Contribution flow must be finite.");
                }
                else if (c.Flow.Value < 0)
                {
                    validation.AddError("NEGATIVE_CONTRIBUTION_FLOW", "Contributions." + c.ContributionId, "Contribution flow cannot be negative.");
                }
                else if (string.IsNullOrWhiteSpace(c.Flow.Unit))
                {
                    validation.AddError("MISSING_CONTRIBUTION_FLOW_UNIT", "Contributions." + c.ContributionId, "Contribution flow unit is required.");
                }

                if (c.Provenance == null)
                {
                    validation.AddError("MISSING_CONTRIBUTION_PROVENANCE", "Contributions." + c.ContributionId, "Contribution provenance is required.");
                }
            }

            // 6. Unit compatibility check
            string canonicalFlowUnit = null;
            if (input.GenerationRate?.Quantity != null && !string.IsNullOrWhiteSpace(input.GenerationRate.Quantity.Unit))
            {
                canonicalFlowUnit = input.GenerationRate.Quantity.Unit.Trim();
            }

            foreach (var c in allContributions)
            {
                if (c.Flow != null && !string.IsNullOrWhiteSpace(c.Flow.Unit))
                {
                    string unit = c.Flow.Unit.Trim();
                    if (canonicalFlowUnit == null)
                    {
                        canonicalFlowUnit = unit;
                    }
                    else if (!string.Equals(canonicalFlowUnit, unit, StringComparison.OrdinalIgnoreCase))
                    {
                        validation.AddError("INCOMPATIBLE_FLOW_UNITS", "Unit",
                            string.Format(CultureInfo.InvariantCulture, "Incompatible flow units detected: '{0}' vs '{1}'. Silent conversion is forbidden.",
                            canonicalFlowUnit, unit));
                    }
                }
            }

            if (!validation.IsValid)
            {
                result.Status = GravityLoadingCalculationStatus.Failed;
                return result;
            }

            // 7. Deterministic arithmetic calculations
            var traces = result.Trace;

            if (basis != null && input.GenerationRate?.Quantity != null)
            {
                double baseFlow = basis.Value * input.GenerationRate.Quantity.Value;
                result.BaseSanitaryFlow = new StandardQuantity(baseFlow, canonicalFlowUnit);

                var trace = new LoadingCalculationTrace
                {
                    Output = "BaseSanitaryFlow",
                    Operation = "Multiply",
                    Formula = string.Format(CultureInfo.InvariantCulture, "Basis ({0}) * GenerationRate ({1})",
                        basis.Value.ToString("R", CultureInfo.InvariantCulture),
                        input.GenerationRate.Quantity.Value.ToString("R", CultureInfo.InvariantCulture)),
                    CalculatedValue = result.BaseSanitaryFlow
                };
                trace.InputReferences.Add("Basis=" + basis.Kind);
                if (input.GenerationRate.Provenance != null)
                    trace.ProvenanceReferences.Add("GenerationRateStandard=" + input.GenerationRate.Provenance.StandardId);
                traces.Add(trace);
            }

            if (result.BaseSanitaryFlow != null && input.PeakFactor != null)
            {
                double peakedFlow = result.BaseSanitaryFlow.Value * input.PeakFactor.Value;
                result.PeakedSanitaryFlow = new StandardQuantity(peakedFlow, canonicalFlowUnit);

                var trace = new LoadingCalculationTrace
                {
                    Output = "PeakedSanitaryFlow",
                    Operation = "Multiply",
                    Formula = string.Format(CultureInfo.InvariantCulture, "BaseSanitaryFlow ({0}) * PeakFactor ({1})",
                        result.BaseSanitaryFlow.Value.ToString("R", CultureInfo.InvariantCulture),
                        input.PeakFactor.Value.ToString("R", CultureInfo.InvariantCulture)),
                    CalculatedValue = result.PeakedSanitaryFlow
                };
                trace.InputReferences.Add("BaseSanitaryFlow");
                if (input.PeakFactor.Provenance != null)
                    trace.ProvenanceReferences.Add("PeakFactorStandard=" + input.PeakFactor.Provenance.StandardId);
                traces.Add(trace);
            }

            // Deterministic ordering of contributions by ContributionId
            var orderedContributions = allContributions
                .OrderBy(c => c.ContributionId, StringComparer.Ordinal)
                .ToList();

            // Sum by contribution kind
            var infilItems = orderedContributions.Where(c => c.Kind == GravityLoadingContributionKind.Infiltration).ToList();
            if (infilItems.Count > 0)
            {
                double sum = infilItems.Sum(c => c.Flow.Value);
                result.InfiltrationTotal = new StandardQuantity(sum, canonicalFlowUnit);
                traces.Add(new LoadingCalculationTrace
                {
                    Output = "InfiltrationTotal",
                    Operation = "Sum",
                    Formula = string.Join(" + ", infilItems.Select(c => c.Flow.Value.ToString("R", CultureInfo.InvariantCulture))),
                    CalculatedValue = result.InfiltrationTotal
                });
            }

            var inflowItems = orderedContributions.Where(c => c.Kind == GravityLoadingContributionKind.Inflow).ToList();
            if (inflowItems.Count > 0)
            {
                double sum = inflowItems.Sum(c => c.Flow.Value);
                result.InflowTotal = new StandardQuantity(sum, canonicalFlowUnit);
                traces.Add(new LoadingCalculationTrace
                {
                    Output = "InflowTotal",
                    Operation = "Sum",
                    Formula = string.Join(" + ", inflowItems.Select(c => c.Flow.Value.ToString("R", CultureInfo.InvariantCulture))),
                    CalculatedValue = result.InflowTotal
                });
            }

            var indItems = orderedContributions.Where(c => c.Kind == GravityLoadingContributionKind.IndustrialProcess).ToList();
            if (indItems.Count > 0)
            {
                double sum = indItems.Sum(c => c.Flow.Value);
                result.IndustrialProcessTotal = new StandardQuantity(sum, canonicalFlowUnit);
                traces.Add(new LoadingCalculationTrace
                {
                    Output = "IndustrialProcessTotal",
                    Operation = "Sum",
                    Formula = string.Join(" + ", indItems.Select(c => c.Flow.Value.ToString("R", CultureInfo.InvariantCulture))),
                    CalculatedValue = result.IndustrialProcessTotal
                });
            }

            var pointItems = orderedContributions.Where(c => c.Kind == GravityLoadingContributionKind.KnownPointLoad).ToList();
            if (pointItems.Count > 0)
            {
                double sum = pointItems.Sum(c => c.Flow.Value);
                result.KnownPointLoadTotal = new StandardQuantity(sum, canonicalFlowUnit);
                traces.Add(new LoadingCalculationTrace
                {
                    Output = "KnownPointLoadTotal",
                    Operation = "Sum",
                    Formula = string.Join(" + ", pointItems.Select(c => c.Flow.Value.ToString("R", CultureInfo.InvariantCulture))),
                    CalculatedValue = result.KnownPointLoadTotal
                });
            }

            // TotalDesignLoading = PeakedSanitaryFlow + InfiltrationTotal + InflowTotal + IndustrialProcessTotal + KnownPointLoadTotal
            if (result.PeakedSanitaryFlow != null)
            {
                double total = result.PeakedSanitaryFlow.Value;
                var formulaParts = new List<string> { "PeakedSanitaryFlow (" + result.PeakedSanitaryFlow.Value.ToString("R", CultureInfo.InvariantCulture) + ")" };

                if (result.InfiltrationTotal != null)
                {
                    total += result.InfiltrationTotal.Value;
                    formulaParts.Add("Infiltration (" + result.InfiltrationTotal.Value.ToString("R", CultureInfo.InvariantCulture) + ")");
                }
                if (result.InflowTotal != null)
                {
                    total += result.InflowTotal.Value;
                    formulaParts.Add("Inflow (" + result.InflowTotal.Value.ToString("R", CultureInfo.InvariantCulture) + ")");
                }
                if (result.IndustrialProcessTotal != null)
                {
                    total += result.IndustrialProcessTotal.Value;
                    formulaParts.Add("Industrial (" + result.IndustrialProcessTotal.Value.ToString("R", CultureInfo.InvariantCulture) + ")");
                }
                if (result.KnownPointLoadTotal != null)
                {
                    total += result.KnownPointLoadTotal.Value;
                    formulaParts.Add("PointLoads (" + result.KnownPointLoadTotal.Value.ToString("R", CultureInfo.InvariantCulture) + ")");
                }

                result.TotalDesignLoading = new StandardQuantity(total, canonicalFlowUnit);
                traces.Add(new LoadingCalculationTrace
                {
                    Output = "TotalDesignLoading",
                    Operation = "Sum",
                    Formula = string.Join(" + ", formulaParts),
                    CalculatedValue = result.TotalDesignLoading
                });

                result.Status = isComplete ? GravityLoadingCalculationStatus.Complete : GravityLoadingCalculationStatus.Incomplete;
            }
            else
            {
                result.Status = GravityLoadingCalculationStatus.Incomplete;
            }

            result.CalculationFingerprint = ComputeLoadingFingerprint(basis, input.GenerationRate, input.PeakFactor, orderedContributions);

            return result;
        }

        public static string ComputeLoadingFingerprint(
            GravityPopulationBasis basis,
            WastewaterGenerationRate rate,
            PeakFactor peakFactor,
            IEnumerable<GravityLoadingContribution> contributions)
        {
            var sb = new StringBuilder();
            sb.Append("LOADING_INPUTS:\n");

            if (basis != null)
            {
                sb.Append("  BASIS|").Append((int)basis.Kind).Append("|")
                  .Append(basis.Value.ToString("R", CultureInfo.InvariantCulture)).Append("|")
                  .Append(basis.Provenance?.StandardId ?? "").Append("|")
                  .Append(basis.Provenance?.StandardVersion ?? "").Append("\n");
            }
            else
            {
                sb.Append("  BASIS|NONE\n");
            }

            if (rate != null)
            {
                sb.Append("  RATE|").Append((int)rate.BasisKind).Append("|")
                  .Append(rate.Quantity != null ? rate.Quantity.Value.ToString("R", CultureInfo.InvariantCulture) : "").Append("|")
                  .Append(rate.Quantity?.Unit ?? "").Append("|")
                  .Append(rate.Provenance?.StandardId ?? "").Append("\n");
            }
            else
            {
                sb.Append("  RATE|NONE\n");
            }

            if (peakFactor != null)
            {
                sb.Append("  PEAK|").Append(peakFactor.Value.ToString("R", CultureInfo.InvariantCulture)).Append("|")
                  .Append(peakFactor.Provenance?.StandardId ?? "").Append("\n");
            }
            else
            {
                sb.Append("  PEAK|NONE\n");
            }

            sb.Append("CONTRIBUTIONS:\n");
            if (contributions != null)
            {
                var sorted = contributions.OrderBy(c => c.ContributionId, StringComparer.Ordinal);
                foreach (var c in sorted)
                {
                    sb.Append("  CONTRIB|").Append(c.ContributionId).Append("|")
                      .Append((int)c.Kind).Append("|")
                      .Append(c.Flow != null ? c.Flow.Value.ToString("R", CultureInfo.InvariantCulture) : "").Append("|")
                      .Append(c.Flow?.Unit ?? "").Append("|")
                      .Append(c.Provenance?.StandardId ?? "").Append("\n");
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static GravityLoadingCalculationInput ProjectFromLoadingSection(GravityLoadingSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));

            var input = new GravityLoadingCalculationInput();
            if (section.Criteria == null) return input;

            foreach (var c in section.Criteria)
            {
                if (c == null || c.Status != GravityCriterionStatus.Defined || c.Value == null) continue;

                string key = c.Key?.Trim()?.ToLowerInvariant() ?? "";
                if (key == "loading.population" && c.Value.Kind == StandardValueKind.Number)
                {
                    input.PopulationBasis = new GravityPopulationBasis(GravityPopulationBasisKind.Population, c.Value.NumberValue.GetValueOrDefault(), c.Provenance);
                }
                else if (key == "loading.pe" && c.Value.Kind == StandardValueKind.Number)
                {
                    input.PopulationEquivalentBasis = new GravityPopulationBasis(GravityPopulationBasisKind.PopulationEquivalent, c.Value.NumberValue.GetValueOrDefault(), c.Provenance);
                }
                else if ((key == "loading.generation_rate" || key == "loading.generation_rate_population") && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.GenerationRate = new WastewaterGenerationRate(c.Value.QuantityValue, GravityPopulationBasisKind.Population, c.Provenance);
                }
                else if (key == "loading.generation_rate_pe" && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.GenerationRate = new WastewaterGenerationRate(c.Value.QuantityValue, GravityPopulationBasisKind.PopulationEquivalent, c.Provenance);
                }
                else if (key == "loading.peak_factor" && c.Value.Kind == StandardValueKind.Number)
                {
                    input.PeakFactor = new PeakFactor(c.Value.NumberValue.GetValueOrDefault(), c.Provenance);
                }
                else if (key.StartsWith("loading.infiltration.", StringComparison.Ordinal) && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.Contributions.Add(new GravityLoadingContribution(c.Key, GravityLoadingContributionKind.Infiltration, c.Value.QuantityValue, c.Provenance));
                }
                else if (key.StartsWith("loading.inflow.", StringComparison.Ordinal) && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.Contributions.Add(new GravityLoadingContribution(c.Key, GravityLoadingContributionKind.Inflow, c.Value.QuantityValue, c.Provenance));
                }
                else if (key.StartsWith("loading.industrial.", StringComparison.Ordinal) && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.Contributions.Add(new GravityLoadingContribution(c.Key, GravityLoadingContributionKind.IndustrialProcess, c.Value.QuantityValue, c.Provenance));
                }
                else if (key.StartsWith("loading.point_load.", StringComparison.Ordinal) && c.Value.Kind == StandardValueKind.Quantity)
                {
                    input.KnownPointLoads.Add(new KnownPointLoad(c.Key, c.Value.QuantityValue, c.Provenance));
                }
            }

            if (input.PopulationEquivalentBasis != null && input.PopulationBasis == null && input.GenerationRate != null && input.GenerationRate.BasisKind == GravityPopulationBasisKind.Population)
            {
                input.GenerationRate.BasisKind = GravityPopulationBasisKind.PopulationEquivalent;
            }

            return input;
        }
    }
}
