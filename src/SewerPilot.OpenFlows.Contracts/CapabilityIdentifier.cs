using System;

namespace SewerPilot.OpenFlows.Contracts
{
    // An open identifier set lets later capabilities travel through existing consumers.
    public sealed class CapabilityIdentifier : IEquatable<CapabilityIdentifier>
    {
        public CapabilityIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
                throw new ArgumentException("A capability identifier must be nonempty without outer whitespace.", nameof(value));
            Value = value;
        }

        public string Value { get; }
        public bool Equals(CapabilityIdentifier other) =>
            other != null && StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => Equals(obj as CapabilityIdentifier);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;

        public static CapabilityIdentifier ProductDetected { get; } = new CapabilityIdentifier("ProductDetected");
        public static CapabilityIdentifier WaterObjectsPresent { get; } = new CapabilityIdentifier("WaterObjectsPresent");
        public static CapabilityIdentifier ManagedAssembliesLoad { get; } = new CapabilityIdentifier("ManagedAssembliesLoad");
        public static CapabilityIdentifier ModelRead { get; } = new CapabilityIdentifier("ModelRead");
        public static CapabilityIdentifier ScenarioRead { get; } = new CapabilityIdentifier("ScenarioRead");
        public static CapabilityIdentifier ScenarioCreate { get; } = new CapabilityIdentifier("ScenarioCreate");
        public static CapabilityIdentifier AlternativeRead { get; } = new CapabilityIdentifier("AlternativeRead");
        public static CapabilityIdentifier AlternativeCreate { get; } = new CapabilityIdentifier("AlternativeCreate");
        public static CapabilityIdentifier SolverRun { get; } = new CapabilityIdentifier("SolverRun");
        public static CapabilityIdentifier ResultRead { get; } = new CapabilityIdentifier("ResultRead");
        public static CapabilityIdentifier ModelWrite { get; } = new CapabilityIdentifier("ModelWrite");
    }
}
