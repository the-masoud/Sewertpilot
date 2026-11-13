using System;

namespace SewerPilot.OpenFlows.Contracts
{
    // Opaque, adapter-assigned values. P2 does not define persistent model identity policy.
    public abstract class OpenFlowsIdentity : IEquatable<OpenFlowsIdentity>
    {
        protected OpenFlowsIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
                throw new ArgumentException("An identity must be nonempty without outer whitespace.", nameof(value));
            Value = value;
        }
        public string Value { get; }
        public bool Equals(OpenFlowsIdentity other) => other != null && GetType() == other.GetType() &&
            StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => Equals(obj as OpenFlowsIdentity);
        public override int GetHashCode() => GetType().GetHashCode() ^ StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    public sealed class ModelIdentity : OpenFlowsIdentity
    {
        public ModelIdentity(string value) : base(value) { }
    }
    public sealed class ScenarioIdentity : OpenFlowsIdentity
    {
        public ScenarioIdentity(string value) : base(value) { }
    }
    public sealed class AlternativeIdentity : OpenFlowsIdentity
    {
        public AlternativeIdentity(string value) : base(value) { }
    }
    public sealed class ElementIdentity : OpenFlowsIdentity
    {
        public int Id { get; }
        public string Label => Value;
        public object Category { get; }

        public ElementIdentity(string value) : base(value)
        {
            if (int.TryParse(value, out int parsedId)) Id = parsedId;
            Category = "General";
        }

        public ElementIdentity(int id, string label, object category) : base(label)
        {
            Id = id;
            Category = category;
        }
    }
    public sealed class CalculationRunIdentity : OpenFlowsIdentity
    {
        public CalculationRunIdentity(string value) : base(value) { }
    }
}
