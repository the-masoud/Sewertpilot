using System;

namespace SewerPilot.OpenFlows.Contracts
{
    // The default value expresses absence of evidence, never support.
    public enum CapabilityState
    {
        NotEstablished = 0,
        Supported = 1,
        NotSupported = 2,
        NotTested = 3
    }

    public static class CapabilityStateReport
    {
        public static string ToReportValue(this CapabilityState state)
        {
            switch (state)
            {
                case CapabilityState.Supported: return "SUPPORTED";
                case CapabilityState.NotSupported: return "NOT_SUPPORTED";
                case CapabilityState.NotTested: return "NOT_TESTED";
                case CapabilityState.NotEstablished: return "NOT_ESTABLISHED";
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }
    }

    public enum CapabilityEvidenceKind
    {
        NotEstablished = 0,
        StaticMetadata,
        AssemblyLoad,
        RuntimeInitialization,
        ModelRead,
        RuntimeOperation,
        SolverExecution,
        ExplicitUnsupported,
        NotTested
    }

    public enum CompatibilityClassification
    {
        InsufficientData = 0,
        Established,
        Partial,
        Unsupported
    }
}
