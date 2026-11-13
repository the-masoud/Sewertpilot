using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SewerPilot.OpenFlows.Adapter
{
    // Observation and bounded provenance validation only.
    internal sealed class AssemblyProvenanceDiagnostics
    {
        internal const string Unavailable = "UNAVAILABLE";
        internal const string ConnectionClientAnchorName = "Bentley.Connect.Client.API";
        private readonly ModelReadEvidence evidence;
        private readonly List<Dictionary<string, object>> rejected = new List<Dictionary<string, object>>();
        private readonly List<Dictionary<string, object>> resolutions = new List<Dictionary<string, object>>();

        internal AssemblyProvenanceDiagnostics(ModelReadEvidence evidence) { this.evidence = evidence; }

        internal static object Safe(Func<object> read)
        { try { return read(); } catch (Exception) { return Unavailable; } }

        internal static bool ContainsPath(string root, string path) =>
            !string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(path) &&
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        internal static bool IsUnder(string root, string path) => ContainsPath(root, path);

        internal static bool IsUnderAny(IEnumerable<HostCompanionRoot> companionRoots, string path) =>
            companionRoots != null && companionRoots.Any(cr => cr != null && ContainsPath(cr.Root, path));

        internal static bool TryValidateCompanionAnchor(Assembly assembly, out HostCompanionRoot companionRoot, out string failureReason)
        {
            companionRoot = null;
            if (assembly == null) { failureReason = "Assembly is null."; return false; }
            bool dynamicAssembly = false;
            try { dynamicAssembly = assembly.IsDynamic; } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in AssemblyProvenanceDiagnostics.cs: " + suppressedEx.Message); }
            if (dynamicAssembly) { failureReason = "Anchor assembly is dynamic."; return false; }

            string location = null;
            try { location = assembly.Location; } catch (Exception ex) { failureReason = "Cannot read assembly location: " + ex.Message; return false; }
            if (string.IsNullOrEmpty(location)) { failureReason = "Anchor assembly location is empty."; return false; }
            if (!File.Exists(location)) { failureReason = "Anchor assembly file does not exist on disk."; return false; }

            string simpleName = Safe(() => assembly.GetName().Name) as string;
            if (string.IsNullOrEmpty(simpleName) || !(simpleName.StartsWith("Bentley.", StringComparison.Ordinal) || simpleName.StartsWith("Haestad.", StringComparison.Ordinal)))
            {
                failureReason = "Anchor assembly is not classified as vendor code.";
                return false;
            }

            string directory = null;
            try { directory = Path.GetDirectoryName(Path.GetFullPath(location)); } catch (Exception ex) { failureReason = "Cannot derive companion directory: " + ex.Message; return false; }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) { failureReason = "Derived companion directory does not exist."; return false; }

            string fullName = Safe(() => assembly.FullName) as string;
            string version = Safe(() => assembly.GetName().Version?.ToString()) as string;
            string token = Safe(() => {
                byte[] bytes = assembly.GetName().GetPublicKeyToken();
                return bytes == null || bytes.Length == 0 ? null : BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }) as string;

            string fileVersion = null, companyName = null, productName = null;
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(location);
                fileVersion = fvi.FileVersion;
                companyName = fvi.CompanyName;
                productName = fvi.ProductName;
            }
            catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in AssemblyProvenanceDiagnostics.cs: " + suppressedEx.Message); }

            companionRoot = new HostCompanionRoot {
                Root = Path.GetFullPath(directory),
                RootKind = "BentleyCompanion",
                DiscoverySource = "LoadedAnchorAssembly",
                AnchorAssembly = simpleName,
                AnchorFullName = fullName ?? Unavailable,
                AnchorLocation = Path.GetFullPath(location),
                Exists = true,
                ValidationResult = "VALIDATED",
                AnchorVersion = version,
                AnchorPublicKeyToken = token,
                AnchorFileVersion = fileVersion,
                AnchorCompanyName = companyName,
                AnchorProductName = productName
            };
            failureReason = null;
            return true;
        }

        internal static List<HostCompanionRoot> DiscoverCompanionRoots(IEnumerable<Assembly> loadedAssemblies)
        {
            var roots = new List<HostCompanionRoot>();
            if (loadedAssemblies == null) return roots;

            foreach (var assembly in loadedAssemblies)
            {
                if (assembly == null) continue;
                bool dynamicAssembly = false;
                try { dynamicAssembly = assembly.IsDynamic; } catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in AssemblyProvenanceDiagnostics.cs: " + suppressedEx.Message); }
                if (dynamicAssembly) continue;

                string name = Safe(() => assembly.GetName().Name) as string;
                if (string.Equals(name, ConnectionClientAnchorName, StringComparison.Ordinal))
                {
                    HostCompanionRoot companion;
                    string failureReason;
                    if (TryValidateCompanionAnchor(assembly, out companion, out failureReason))
                    {
                        if (!roots.Any(r => string.Equals(r.Root, companion.Root, StringComparison.OrdinalIgnoreCase)))
                        {
                            roots.Add(companion);
                        }
                    }
                }
            }
            return roots;
        }

        internal static Dictionary<string, object> Describe(Assembly assembly, string root) => Describe(assembly, root, null);

        internal static Dictionary<string, object> Describe(Assembly assembly, string root, IEnumerable<HostCompanionRoot> companionRoots)
        {
            var roots = (companionRoots ?? Enumerable.Empty<HostCompanionRoot>()).ToList();
            string location = Safe(() => assembly.Location) as string;
            bool hasLocation = !string.IsNullOrEmpty(location) && !Equals(location, Unavailable);
            bool underProduct = hasLocation && ContainsPath(root, location);
            bool underCompanion = hasLocation && roots.Any(cr => ContainsPath(cr.Root, location));

            return new Dictionary<string, object> {
                { "SimpleName", Safe(() => assembly.GetName().Name) },
                { "FullName", Safe(() => assembly.FullName) },
                { "Version", Safe(() => assembly.GetName().Version?.ToString()) },
                { "Culture", Safe(() => string.IsNullOrEmpty(assembly.GetName().CultureName) ? "neutral" : assembly.GetName().CultureName) },
                { "PublicKeyToken", Safe(() => {
                    byte[] token = assembly.GetName().GetPublicKeyToken();
                    return token == null || token.Length == 0 ? null : BitConverter.ToString(token).Replace("-", "").ToLowerInvariant(); }) },
                { "Location", Safe(() => assembly.Location) },
                { "CodeBaseIfSafelyAvailable", Safe(() => assembly.CodeBase) },
                { "GlobalAssemblyCache", Safe(() => assembly.GlobalAssemblyCache) },
                { "ReflectionOnly", Safe(() => assembly.ReflectionOnly) },
                { "IsDynamicIfAvailable", Safe(() => assembly.IsDynamic) },
                { "ImageRuntimeVersion", Safe(() => assembly.ImageRuntimeVersion) },
                { "SelectedRuntimeRoot", root },
                { "PrimaryProductRoot", root },
                { "SelectedProductRoot", root },
                { "NormalizedSelectedRuntimeRoot", Safe(() => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) },
                { "NormalizedAssemblyPath", Safe(() => !hasLocation ? Unavailable : Path.GetFullPath(location)) },
                { "AssemblyFileExists", Safe(() => !hasLocation ? (object)Unavailable : File.Exists(location)) },
                { "IsUnderSelectedRuntimeRoot", Safe(() => !hasLocation ? (object)Unavailable : underProduct) },
                { "IsUnderProductRoot", underProduct },
                { "IsUnderCompanionRoot", underCompanion },
                { "ValidatedCompanionRoots", roots.Select(r => r.Root).ToArray() },
                { "CurrentGuardClassifiedAsVendor", Safe(() => {
                    string n = assembly.GetName().Name;
                    return n.StartsWith("Haestad.", StringComparison.Ordinal) || n.StartsWith("Bentley.", StringComparison.Ordinal);
                }) }
            };
        }

        private void Observe(Action action)
        {
            try { action(); }
            catch (Exception error) { evidence.Diagnostics["ProvenanceCaptureError"] = error.GetType().FullName; }
        }

        internal Dictionary<string, object> BeginResolve(ResolveEventArgs args)
        {
            var record = new Dictionary<string, object>();
            Observe(() => {
                record["RequestedAssemblyIdentity"] = args.Name;
                record["RequestingAssemblySimpleName"] = Safe(() => args.RequestingAssembly == null ? Unavailable : args.RequestingAssembly.GetName().Name);
                record["RequestingAssemblyFullName"] = Safe(() => args.RequestingAssembly == null ? Unavailable : args.RequestingAssembly.FullName);
                record["RequestingAssemblyLocation"] = Safe(() => args.RequestingAssembly == null ? Unavailable : args.RequestingAssembly.Location);
            });
            return record;
        }

        internal void EndResolve(Dictionary<string, object> record, string candidate, Assembly returned)
        {
            Observe(() => {
                record["CandidatePathConsidered"] = candidate ?? Unavailable;
                record["ReturnedAssemblyFullName"] = returned == null ? Unavailable : Safe(() => returned.FullName);
                record["ReturnedAssemblyLocation"] = returned == null ? Unavailable : Safe(() => returned.Location);
                if (resolutions.Count < 128) resolutions.Add(record);
                else evidence.Diagnostics["ProvenanceResolverTraceTruncated"] = "true";
            });
        }

        internal void RecordCompanionRoots(string root, IEnumerable<HostCompanionRoot> companionRoots)
        {
            Observe(() => {
                var roots = (companionRoots ?? Enumerable.Empty<HostCompanionRoot>()).ToList();
                evidence.Diagnostics["PrimaryProductRoot"] = root;
                evidence.Diagnostics["CompanionRoots"] = RuntimeHostProtocol.Serializer().Serialize(roots);
                evidence.Diagnostics["CompanionRootCount"] = roots.Count.ToString();
                RuntimeStageJournal.Record("CompanionRootsRecorded", "EXIT", roots);
            });
        }

        internal void Snapshot(Assembly[] assemblies, string root) => Snapshot(assemblies, root, null);

        internal void Snapshot(Assembly[] assemblies, string root, IEnumerable<HostCompanionRoot> companionRoots)
        {
            Observe(() => {
                var roots = (companionRoots ?? Enumerable.Empty<HostCompanionRoot>()).ToList();
                var snapshot = assemblies.Take(512).Select(assembly => {
                    var record = Describe(assembly, root, roots);
                    return new Dictionary<string, object> {
                        { "SimpleName", record["SimpleName"] }, { "FullName", record["FullName"] },
                        { "Location", record["Location"] }, { "GlobalAssemblyCache", record["GlobalAssemblyCache"] },
                        { "IsDynamicIfAvailable", record["IsDynamicIfAvailable"] },
                        { "CurrentGuardClassifiedAsVendor", record["CurrentGuardClassifiedAsVendor"] },
                        { "IsUnderSelectedRuntimeRoot", record["IsUnderSelectedRuntimeRoot"] },
                        { "IsUnderProductRoot", record["IsUnderProductRoot"] },
                        { "IsUnderCompanionRoot", record["IsUnderCompanionRoot"] }
                    };
                }).ToArray();
                evidence.Diagnostics["ProvenanceSnapshot"] = RuntimeHostProtocol.Serializer().Serialize(snapshot);
                evidence.Diagnostics["ProvenanceSnapshotTotalCount"] = assemblies.Length.ToString();
                evidence.Diagnostics["ProvenanceSnapshotTruncated"] = (assemblies.Length > 512).ToString().ToLowerInvariant();
                evidence.Diagnostics["ProvenanceResolverTrace"] = RuntimeHostProtocol.Serializer().Serialize(resolutions);
                RuntimeStageJournal.Record("ProvenanceSnapshot", "EXIT", snapshot);
            });
        }

        internal void Reject(Assembly assembly, string root, string path, bool classifiedVendor) =>
            Reject(assembly, root, null, path, classifiedVendor);

        internal void Reject(Assembly assembly, string root, IEnumerable<HostCompanionRoot> companionRoots, string path, bool classifiedVendor)
        {
            Observe(() => {
                var roots = (companionRoots ?? Enumerable.Empty<HostCompanionRoot>()).ToList();
                var record = Describe(assembly, root, roots);
                record["Sequence"] = rejected.Count + 1;
                record["NormalizedAssemblyPath"] = path;
                record["CurrentGuardClassifiedAsVendor"] = classifiedVendor;
                record["CurrentGuardClassificationReason"] = "Simple name starts with Haestad. or Bentley. using Ordinal comparison.";
                record["CurrentGuardRejectionReason"] = "Vendor dependency outside validated product and companion roots.";
                record["RejectionReason"] = "Vendor dependency outside validated product and companion roots.";
                record["PathComparisonRule"] = "Vendor assembly must reside under SelectedProductRoot or a validated runtime companion root.";
                record["OriginalGuardIsUnderSelectedRuntimeRoot"] = path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                record["IsUnderProductRoot"] = ContainsPath(root, path);
                record["IsUnderCompanionRoot"] = roots.Any(r => ContainsPath(r.Root, path));
                record["ValidatedCompanionRoots"] = roots.Select(r => r.Root).ToArray();
                record["RootDiscoveryEvidence"] = roots.Select(r => new Dictionary<string, object> {
                    { "Root", r.Root }, { "RootKind", r.RootKind }, { "DiscoverySource", r.DiscoverySource },
                    { "AnchorAssembly", r.AnchorAssembly }, { "AnchorLocation", r.AnchorLocation },
                    { "ValidationResult", r.ValidationResult }
                }).ToArray();

                var requesting = resolutions.LastOrDefault(item => item.ContainsKey("ReturnedAssemblyFullName") &&
                    Equals(item["ReturnedAssemblyFullName"], record["FullName"]) && Equals(item["ReturnedAssemblyLocation"], record["Location"]));
                foreach (string key in new[] { "RequestingAssemblySimpleName", "RequestingAssemblyFullName", "RequestingAssemblyLocation" })
                    record[key] = requesting != null && requesting.ContainsKey(key) ? requesting[key] : Unavailable;
                record["RequestingAssemblyEvidenceSource"] = requesting == null ? "UNAVAILABLE: no matching existing AssemblyResolve return; CLR loads may not raise this event." : "Existing AssemblyResolve event and exact returned identity/location match.";
                rejected.Add(record);
                evidence.Diagnostics["OffendingAssembly"] = RuntimeHostProtocol.Serializer().Serialize(rejected);
                evidence.Diagnostics["LastDurableStage"] = "ProvenanceOffenderCaptured";
                RuntimeStageJournal.Record("ProvenanceOffenderCaptured", "EXIT", record);
            });
        }
    }
}
