namespace SewerPilot.Mcp.Server
{
    // Deliberately excludes paths, runtime details, audit payloads and engineering results.
    public sealed class ProjectInfo
    {
        public string Kind { get; set; } = "STORED_PROJECT_METADATA";
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public int SchemaVersion { get; set; }
        public int StateRevision { get; set; }
        public string SelectedModule { get; set; }
        public string StoredModelFingerprint { get; set; }
        public BasisReference GravityFlowDesignBasisReference { get; set; }
        public BasisReference StormFlowDesignBasisReference { get; set; }
    }

    public sealed class BasisReference
    {
        public string ReferenceIdentifier { get; set; }
        public string DesignBasisVersion { get; set; }
    }
}
