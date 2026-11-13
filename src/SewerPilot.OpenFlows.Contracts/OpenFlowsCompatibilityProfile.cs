using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SewerPilot.OpenFlows.Contracts
{
    public sealed class OpenFlowsCompatibilityProfile
    {
        public OpenFlowsCompatibilityProfile(IEnumerable<CapabilityEvidence> capabilities,
            CompatibilityClassification classification, string productDisplayName = null,
            string productVersion = null, string productBuild = null, string architecture = null,
            IEnumerable<string> limitations = null)
        {
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            if (!Enum.IsDefined(typeof(CompatibilityClassification), classification))
                throw new ArgumentOutOfRangeException(nameof(classification));
            var copy = new Dictionary<CapabilityIdentifier, CapabilityEvidence>();
            foreach (var evidence in capabilities)
            {
                if (evidence == null) throw new ArgumentException("Capability evidence cannot be null.", nameof(capabilities));
                if (copy.ContainsKey(evidence.Capability))
                    throw new ArgumentException("Duplicate capability evidence is ambiguous.", nameof(capabilities));
                copy.Add(evidence.Capability, evidence);
            }
            Capabilities = new ReadOnlyDictionary<CapabilityIdentifier, CapabilityEvidence>(copy);
            var warnings = new List<string>();
            if (limitations != null)
                foreach (var limitation in limitations)
                {
                    if (string.IsNullOrWhiteSpace(limitation))
                        throw new ArgumentException("Limitations must be nonempty.", nameof(limitations));
                    warnings.Add(limitation);
                }
            Limitations = new ReadOnlyCollection<string>(warnings);
            Classification = classification;
            ProductDisplayName = productDisplayName;
            ProductVersion = productVersion;
            ProductBuild = productBuild;
            Architecture = architecture;
        }

        public string ProductDisplayName { get; }
        public string ProductVersion { get; }
        public string ProductBuild { get; }
        public string Architecture { get; }
        public CompatibilityClassification Classification { get; }
        public IReadOnlyDictionary<CapabilityIdentifier, CapabilityEvidence> Capabilities { get; }
        public IReadOnlyList<string> Limitations { get; }
        public CapabilityState ProductDetected => GetCapability(CapabilityIdentifier.ProductDetected).State;

        public CapabilityEvidence GetCapability(CapabilityIdentifier capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            CapabilityEvidence evidence;
            if (Capabilities.TryGetValue(capability, out evidence)) return evidence;
            return new CapabilityEvidence(capability, CapabilityState.NotEstablished,
                CapabilityEvidenceKind.NotEstablished, "No evidence was supplied for this capability.",
                "Compatibility profile: capability absent.");
        }
    }
}
