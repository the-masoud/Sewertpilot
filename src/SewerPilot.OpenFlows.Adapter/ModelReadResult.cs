using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SewerPilot.OpenFlows.Contracts;

[assembly: InternalsVisibleTo("SewerPilot.OpenFlows.Adapter.Tests")]
[assembly: InternalsVisibleTo("OpenFlowsReadOnlyModelAcceptance")]
[assembly: InternalsVisibleTo("SewerPilot.OpenFlows.RuntimeHost")]
[assembly: InternalsVisibleTo("SewerPilot")]

namespace SewerPilot.OpenFlows.Adapter
{
    [Serializable]
    public sealed class FileFingerprint
    {
        public FileFingerprint(string path, string sha256) { Path = path; SHA256 = sha256; }
        public string Path { get; }
        public string SHA256 { get; }
    }

    // Returned diagnostics contain owned values only; no live model/session escapes.
    [Serializable]
    public sealed class ModelReadEvidence
    {
        public bool RuntimeSelected { get; internal set; }
        public bool AssembliesBound { get; internal set; }
        public bool LifecycleInitialized { get; internal set; }
        public bool OpenSucceeded { get; internal set; }
        public bool CurrentGraphicalProjectAvailable { get; internal set; }
        public bool DomainProjectAvailable { get; internal set; }
        public bool DomainDataSetAvailable { get; internal set; }
        public int? FirstConduitElementID { get; internal set; }
        public bool CloseCompleted { get; internal set; }
        public bool SourceIntegrity { get; internal set; }
        public bool WorkingIntegrity { get; internal set; }
        public string FailureCode { get; internal set; }
        public string Exception { get; internal set; }
        public string WorkingCopyPath { get; internal set; }
        public List<FileFingerprint> SourceBefore { get; internal set; } = new List<FileFingerprint>();
        public List<FileFingerprint> SourceAfter { get; internal set; } = new List<FileFingerprint>();
        public List<FileFingerprint> WorkingBefore { get; internal set; } = new List<FileFingerprint>();
        public List<FileFingerprint> WorkingAfter { get; internal set; } = new List<FileFingerprint>();
        public List<string> TransientFiles { get; internal set; } = new List<string>();
        public List<string> SaveWriteInvocations { get; internal set; } = new List<string>();
        public Dictionary<string, string> Diagnostics { get; internal set; } = new Dictionary<string, string>();
        public List<FileFingerprint> LoadedAssemblies { get; internal set; } = new List<FileFingerprint>();
        public bool Accepted => RuntimeSelected && AssembliesBound && LifecycleInitialized && OpenSucceeded &&
            CurrentGraphicalProjectAvailable && DomainProjectAvailable && DomainDataSetAvailable &&
            FirstConduitElementID.HasValue && CloseCompleted && SourceIntegrity && WorkingIntegrity &&
            SaveWriteInvocations.Count == 0 && FailureCode == null &&
            HostFlag("RuntimeHostResultValid", "true") && HostFlag("RuntimeHostResultCommitted", "true") &&
            HostFlag("RuntimeHostStaCompleted", "true") && HostFlag("RuntimeHostProcessTerminated", "true") &&
            HostFlag("RuntimeHostExitCode", "0") && HostFlag("RuntimeHostWatchdogTermination", "false");
        private bool HostFlag(string key, string value) => Diagnostics.ContainsKey(key) && Diagnostics[key] == value;
    }

    public sealed class ModelReadResult : IOpenFlowsModel
    {
        internal ModelReadResult(ModelReadEvidence evidence, OpenFlowsCompatibilityProfile profile)
        { Evidence = evidence; Profile = profile; Identity = new ModelIdentity(Guid.NewGuid().ToString("N")); }
        public ModelIdentity Identity { get; }
        public bool OpenSucceeded => Evidence.OpenSucceeded;
        public bool DomainProjectAvailable => Evidence.DomainProjectAvailable;
        public bool DomainDataSetAvailable => Evidence.DomainDataSetAvailable;
        public string ObservedDataName => Evidence.FirstConduitElementID.HasValue ? "FirstConduitElementID" : null;
        public int? ObservedDataValue => Evidence.FirstConduitElementID;
        public ModelReadEvidence Evidence { get; }
        public OpenFlowsCompatibilityProfile Profile { get; }
        public bool Succeeded => Evidence.Accepted;
        public void Dispose() { } // Detached observation; the vendor lifecycle is already closed.
    }
}
