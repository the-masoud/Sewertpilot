using System;
using System.Collections.Generic;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class StandardsResolutionOptions
    {
        public string SelectedPublishedStandardId { get; set; }
        public string SelectedCompanyStandardId { get; set; }
        public string SelectedProjectOverrideId { get; set; }
    }

    public static class StandardsResolver
    {
        public static ResolvedStandardValue Resolve(
            string parameterKey,
            IEnumerable<StandardDefinition> standards,
            StandardsResolutionOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(parameterKey))
                throw new ArgumentException("Parameter key must be specified.", nameof(parameterKey));

            var key = new StandardParameterKey(parameterKey);
            options = options ?? new StandardsResolutionOptions();

            var layerMap = new Dictionary<StandardSourceLayer, List<StandardDefinition>>();
            foreach (StandardSourceLayer layer in Enum.GetValues(typeof(StandardSourceLayer)))
                layerMap[layer] = new List<StandardDefinition>();

            if (standards != null)
            {
                foreach (var std in standards)
                {
                    if (std == null) continue;
                    std.Validate();
                    layerMap[std.SourceLayer].Add(std);
                }
            }

            var selectedPerLayer = new Dictionary<StandardSourceLayer, StandardDefinition>();

            foreach (var kvp in layerMap)
            {
                var layer = kvp.Key;
                var list = kvp.Value;
                if (list.Count == 0) continue;

                string selectedId = null;
                if (layer == StandardSourceLayer.PublishedStandard) selectedId = options.SelectedPublishedStandardId;
                else if (layer == StandardSourceLayer.CompanyStandard) selectedId = options.SelectedCompanyStandardId;
                else if (layer == StandardSourceLayer.ProjectOverride) selectedId = options.SelectedProjectOverrideId;

                var withParam = list.FindAll(s => s.Parameters != null &&
                    s.Parameters.Exists(p => string.Equals(new StandardParameterKey(p.Key).CanonicalKey, key.CanonicalKey, StringComparison.Ordinal)));

                if (withParam.Count == 0) continue;

                if (!string.IsNullOrWhiteSpace(selectedId))
                {
                    var matched = withParam.Find(s => string.Equals(s.StandardId, selectedId, StringComparison.Ordinal));
                    if (matched != null)
                    {
                        selectedPerLayer[layer] = matched;
                        continue;
                    }
                }

                if (withParam.Count == 1)
                {
                    selectedPerLayer[layer] = withParam[0];
                }
                else
                {
                    throw new StandardsResolutionException("AMBIGUOUS_STANDARD_SOURCE",
                        string.Format(CultureInfo.InvariantCulture,
                            "Two eligible standards in layer '{0}' define parameter '{1}' without explicit selection.",
                            layer, parameterKey));
                }
            }

            var candidates = new List<StandardCandidate>();
            var layersDescending = new[]
            {
                StandardSourceLayer.ProjectOverride,
                StandardSourceLayer.CompanyStandard,
                StandardSourceLayer.PublishedStandard,
                StandardSourceLayer.ApplicationDefault
            };

            StandardCandidate winningCandidate = null;

            foreach (var layer in layersDescending)
            {
                if (selectedPerLayer.TryGetValue(layer, out var std))
                {
                    var param = std.Parameters.Find(p => string.Equals(new StandardParameterKey(p.Key).CanonicalKey, key.CanonicalKey, StringComparison.Ordinal));
                    if (param != null)
                    {
                        var prov = param.Provenance != null ? new StandardProvenance
                        {
                            SourceLayer = layer,
                            StandardId = std.StandardId,
                            StandardName = std.Name,
                            StandardVersion = std.Version,
                            ParameterKey = param.Key,
                            DocumentReference = param.Provenance.DocumentReference,
                            SectionReference = param.Provenance.SectionReference,
                            Notes = param.Provenance.Notes
                        } : new StandardProvenance
                        {
                            SourceLayer = layer,
                            StandardId = std.StandardId,
                            StandardName = std.Name,
                            StandardVersion = std.Version,
                            ParameterKey = param.Key
                        };

                        bool isWinner = winningCandidate == null;
                        var candidate = new StandardCandidate
                        {
                            Value = param.Value,
                            Provenance = prov,
                            IsWinner = isWinner
                        };
                        candidates.Add(candidate);
                        if (isWinner)
                            winningCandidate = candidate;
                    }
                }
            }

            if (winningCandidate == null)
            {
                return ResolvedStandardValue.NotDefined(parameterKey);
            }

            return new ResolvedStandardValue
            {
                ParameterKey = parameterKey,
                Status = StandardResolutionStatus.Defined,
                EffectiveValue = winningCandidate.Value,
                EffectiveSource = winningCandidate.Provenance.SourceLayer,
                EffectiveStandardId = winningCandidate.Provenance.StandardId,
                EffectiveVersion = winningCandidate.Provenance.StandardVersion,
                EffectiveProvenance = winningCandidate.Provenance,
                Candidates = candidates
            };
        }
    }
}
