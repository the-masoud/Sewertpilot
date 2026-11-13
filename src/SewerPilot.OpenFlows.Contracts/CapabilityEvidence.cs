using System;

namespace SewerPilot.OpenFlows.Contracts
{
    public sealed class CapabilityEvidence
    {
        public CapabilityEvidence(CapabilityIdentifier capability, CapabilityState state,
            CapabilityEvidenceKind kind, string reason, string provenance, DateTimeOffset? observedAt = null)
        {
            Capability = capability ?? throw new ArgumentNullException(nameof(capability));
            if (!Enum.IsDefined(typeof(CapabilityState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (!Enum.IsDefined(typeof(CapabilityEvidenceKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if ((kind == CapabilityEvidenceKind.NotTested && state != CapabilityState.NotTested) ||
                (kind == CapabilityEvidenceKind.ExplicitUnsupported && state != CapabilityState.NotSupported) ||
                (kind == CapabilityEvidenceKind.NotEstablished && state != CapabilityState.NotEstablished))
                throw new ArgumentException("The evidence kind contradicts the capability state.", nameof(kind));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance is required.", nameof(provenance));
            State = state;
            Kind = kind;
            Reason = reason;
            Provenance = provenance;
            ObservedAt = observedAt;
        }

        public CapabilityIdentifier Capability { get; }
        public CapabilityState State { get; }
        public CapabilityEvidenceKind Kind { get; }
        public string Reason { get; }
        public string Provenance { get; }
        // Optional observation metadata; not part of capability identity.
        public DateTimeOffset? ObservedAt { get; }
    }
}
