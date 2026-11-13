using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SewerPilot.OpenFlows.Compatibility
{
    public sealed class WindowsRuntimeLocationSource : IRuntimeLocationSource
    {
        public IEnumerable<RuntimeLocation> Read(OpenFlowsDiscoveryOptions options, DiscoveryDiagnostics diagnostics)
        {
            var locations = new List<RuntimeLocation>();
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(root))
                    foreach (string vendor in new[] { "Bentley", "Bentley Systems" })
                        locations.Add(new RuntimeLocation(Path.Combine(root, vendor), "PlatformProgramFilesVendorRoot"));
            }
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var registry = RegistryKey.OpenBaseKey(hive, view))
                        {
                            using (var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false))
                            {
                                if (uninstall != null)
                                {
                                    var names = uninstall.GetSubKeyNames().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                                    if (names.Length > options.MaximumRegistryEntries) diagnostics.Incomplete("Uninstall registry entry limit reached: " + hive + "/" + view);
                                    foreach (string name in names.Take(options.MaximumRegistryEntries))
                                        using (var product = uninstall.OpenSubKey(name, false))
                                        {
                                            string display = product == null ? null : product.GetValue("DisplayName") as string;
                                            if (!IsProduct(display)) continue;
                                            string source = "UninstallMetadata:" + hive + "/" + view + "/" + name;
                                            Add(locations, product.GetValue("InstallLocation") as string, source);
                                            string icon = product.GetValue("DisplayIcon") as string;
                                            if (!string.IsNullOrWhiteSpace(icon))
                                            {
                                                string path = icon.Trim();
                                                if (path.StartsWith("\"", StringComparison.Ordinal))
                                                {
                                                    int end = path.IndexOf('"', 1);
                                                    path = end > 1 ? path.Substring(1, end - 1) : null;
                                                }
                                                else { int comma = path.LastIndexOf(','); if (comma >= 0) path = path.Substring(0, comma); }
                                                if (path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) Add(locations, Path.GetDirectoryName(path), source);
                                            }
                                        }
                                }
                            }
                            foreach (string vendorKey in new[] { @"SOFTWARE\Bentley", @"SOFTWARE\Bentley Systems", @"SOFTWARE\Haestad" })
                            {
                                int inspected = 0;
                                ReadVendor(registry, vendorKey, 0, options, diagnostics, locations, ref inspected);
                            }
                        }
                    }
                    catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException || error is IOException || error is ArgumentException)
                    { diagnostics.Incomplete("Registry source unavailable: " + hive + "/" + view + ": " + error.GetType().Name); }
                }
            return locations.GroupBy(x => x.Root + "|" + x.Source, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
        }

        private static bool IsProduct(string display) => !string.IsNullOrWhiteSpace(display) &&
            (display.IndexOf("SewerGEMS", StringComparison.OrdinalIgnoreCase) >= 0 ||
             display.IndexOf("OpenFlows Sewer", StringComparison.OrdinalIgnoreCase) >= 0);

        private static void Add(List<RuntimeLocation> locations, string root, string source)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            try { locations.Add(new RuntimeLocation(RuntimePaths.NormalizeRoot(root.Trim().Trim('"')), source)); }
            catch (ArgumentException) { /* Malformed product metadata cannot establish a candidate. */ }
        }

        private static void ReadVendor(RegistryKey registry, string keyPath, int depth, OpenFlowsDiscoveryOptions options,
            DiscoveryDiagnostics diagnostics, List<RuntimeLocation> locations, ref int inspected)
        {
            using (var key = registry.OpenSubKey(keyPath, false))
            {
                if (key == null) return;
                if (++inspected > options.MaximumRegistryEntries) { diagnostics.Incomplete("Vendor registry entry limit reached."); return; }
                // Whitelist path metadata only; do not read license, activation or credential values.
                foreach (string name in new[] { "InstallLocation", "InstallPath", "InstallDir", "Path" })
                    Add(locations, key.GetValue(name) as string, "VendorRegistry:" + registry.Name + "/" + keyPath);
                if (depth >= options.MaximumDepth) return;
                foreach (string child in key.GetSubKeyNames().OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (inspected >= options.MaximumRegistryEntries) { diagnostics.Incomplete("Vendor registry entry limit reached."); break; }
                    if (new[] { "license", "credential", "token", "activation", "secret" }.Any(word => child.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    ReadVendor(registry, keyPath + "\\" + child, depth + 1, options, diagnostics, locations, ref inspected);
                }
            }
        }
    }
}
