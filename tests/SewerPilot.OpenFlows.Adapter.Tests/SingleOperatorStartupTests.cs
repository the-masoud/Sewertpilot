using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static SingleOperatorStartupOptions StartupOptions(string path, bool read = false)
        {
            Check(SingleOperatorStartupOptions.TryParse(read ? new[] { "--open-project", path, "--read-referenced-model" } :
                new[] { "--open-project", path }, out var options), "Explicit startup arguments dispatch to the operator workflow.");
            return options;
        }

        private static ProjectWorkspace StartupProject(bool withModel = true)
        {
            string root = Ui01Directory();
            var workspace = ProjectWorkspace.Create(Path.Combine(root, "project"), "TEST_ONLY startup");
            if (withModel)
            {
                string model = Path.Combine(root, "TEST_ONLY.stsw");
                File.WriteAllText(model, "TEST_ONLY_NOT_A_BENTLEY_MODEL");
                workspace.AttachModel(ProtectedBaseline.Register(model), Ui01Network());
                workspace.State.CurrentDesignRunId = "TEST_ONLY_historical_run";
                workspace.SaveProjectState();
            }
            return workspace;
        }

        private static void StartupReject(Action work, string message)
        {
            bool rejected = false;
            try { work(); }
            catch (Exception error) { rejected = true; Check(error.Message.Contains(message), "Expected precise startup failure: " + message); }
            Check(rejected, "Invalid startup is rejected rather than opening a dialog or falling through to runtime.");
        }

        private static void MvpStartup_ArgumentModesAndLegacyDispatch()
        {
            Check(SingleOperatorStartupOptions.TryParse(new string[0], out var empty) && empty.ProjectJsonPath == null && !empty.ReadReferencedModel,
                "No-argument GUI startup remains the default without any implicit model read.");
            Check(SingleOperatorStartupOptions.TryParse(new[] { "--mvp" }, out var mvp) && mvp.ProjectJsonPath == null && !mvp.ReadReferencedModel,
                "Existing --mvp startup is preserved.");
            foreach (string mode in new[] { "r12", "p5", "p6b", "p12a", "p13a", "p15a", "p15b-r4" })
                Check(!SingleOperatorStartupOptions.TryParse(new[] { "lesson.stsw", "repo", mode }, out var legacy) && legacy == null,
                    "Historical CLI mode dispatch is unchanged: " + mode);
            Check(!SingleOperatorStartupOptions.TryParse(new[] { "lesson.stsw", "repo" }, out var original) && original == null,
                "Original two-positional-argument acceptance dispatch is preserved.");
            string path = Path.Combine(Ui01Directory(), "project.json");
            var open = StartupOptions(path); var read = StartupOptions(path, true);
            Check(open.ProjectJsonPath == path && !open.ReadReferencedModel && read.ProjectJsonPath == path && read.ReadReferencedModel,
                "Optional read is explicit and immutable; parsing never loads files or a vendor runtime.");
        }

        private static void MvpStartup_InvalidArgumentsFailClosed()
        {
            string path = Path.Combine(Ui01Directory(), "project.json");
            var invalid = new[] {
                new[] { "--open-project" }, new[] { "--open-project", "" },
                new[] { "--open-project", "project.json" }, new[] { "--open-project", @"C:project.json" },
                new[] { "--open-project", @"\project.json" }, new[] { "--open-project", Path.Combine(Ui01Directory(), "other.json") },
                new[] { "--read-referenced-model" }, new[] { "--read-referenced-model", "--open-project", path },
                new[] { "--open-project", path, "--unknown" }, new[] { "--open-project", path, "--read-referenced-model", "--read-referenced-model" },
                new[] { "lesson.stsw", "--open-project" }
            };
            foreach (var args in invalid)
            {
                bool rejected = false;
                try { SingleOperatorStartupOptions.TryParse(args, out var ignored); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, "Malformed/relative/misordered startup flags cannot dispatch to the legacy model reader.");
            }
            StartupReject(() => SingleOperatorStartupOptions.TryParse(null, out var ignored), "args");
        }

        private static void MvpStartup_DefaultStartupNeverLoads()
        {
            var result = SingleOperatorProjectStartup.OpenAsync(SingleOperatorStartupOptions.Default,
                (w, s) => { throw new Exception("Unexpected project callback"); },
                (w, p) => { throw new Exception("Unexpected model read"); }).GetAwaiter().GetResult();
            Check(result == null, "Default startup does not open a project, read a model or restore execution state.");
        }

        private static void MvpStartup_OpenOnlyPreservesIdentityWithoutRestoredNetwork()
        {
            var saved = StartupProject(); int opened = 0, reads = 0;
            var result = SingleOperatorProjectStartup.OpenAsync(StartupOptions(Path.Combine(saved.DirectoryPath, "project.json")), (w, status) => {
                opened++;
                Check(status.StartsWith("Saved model identity verified") && w.Network == null && w.Baseline == null && !w.Reading,
                    "Only validated metadata is published; no stale source snapshot or active model operation is restored.");
            }, (w, path) => { reads++; throw new Exception("Real reader must remain unreachable"); }).GetAwaiter().GetResult();
            Check(opened == 1 && reads == 0 && result.State.ProjectId == saved.State.ProjectId && result.State.StateRevision == saved.State.StateRevision,
                "Open-only startup retains exact project/revision and never invokes its reader seam.");
            Check(result.State.CurrentDesignRunId == "TEST_ONLY_historical_run" && !result.Dirty,
                "Historical metadata remains metadata; no candidate execution or automatic save is requested.");
            Check(ProjectStore.ReadAudit(saved.DirectoryPath).Last().ActionType == AuditActionType.ProjectOpened,
                "Startup reuses existing project opening and its audit contract.");
        }

        private static void MvpStartup_InvalidProjectNeverPublishesOrReads()
        {
            int opened = 0, reads = 0;
            Action<string> attempt = path => SingleOperatorProjectStartup.OpenAsync(StartupOptions(path, true),
                (w, s) => opened++, (w, p) => { reads++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            StartupReject(() => attempt(Path.Combine(Ui01Directory(), "project.json")), "Project state file not found");
            var saved = StartupProject(); File.WriteAllText(Path.Combine(saved.DirectoryPath, "project.json"), "{");
            StartupReject(() => attempt(Path.Combine(saved.DirectoryPath, "project.json")), "Malformed project JSON");
            Check(opened == 0 && reads == 0, "Malformed/missing project state cannot publish a workspace or request model loading.");
        }

        private static void MvpStartup_SourceMismatchPreventsOptionalRead()
        {
            var saved = StartupProject(); int opened = 0, reads = 0;
            string project = Path.Combine(saved.DirectoryPath, "project.json"), before = File.ReadAllText(project);
            File.WriteAllText(saved.State.Model.BaselineLocationMetadata, "TEST_ONLY_CHANGED_SOURCE");
            StartupReject(() => SingleOperatorProjectStartup.OpenAsync(StartupOptions(project, true),
                (w, s) => opened++, (w, p) => { reads++; return Task.CompletedTask; }).GetAwaiter().GetResult(), "Source model family changed");
            Check(opened == 0 && reads == 0 && File.ReadAllText(project) == before,
                "Stored identity mismatch blocks publication/read without overwriting project identity or restoring authorization.");
        }

        private static void MvpStartup_OptionalReadOnceAfterValidatedPublication()
        {
            var saved = StartupProject(); int opened = 0, reads = 0;
            string project = Path.Combine(saved.DirectoryPath, "project.json"), before = File.ReadAllText(project);
            var result = SingleOperatorProjectStartup.OpenAsync(StartupOptions(project, true), (w, status) => {
                opened++; Check(w.Network == null && status.StartsWith("Saved model identity verified"), "Fresh project metadata precedes explicit model loading.");
            }, (w, path) => {
                reads++; Check(opened == 1 && path == saved.State.Model.BaselineLocationMetadata && !w.Reading,
                    "One optional request targets only the validated stored reference after project binding.");
                w.AttachModel(ProtectedBaseline.Register(path), Ui01Network());
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Check(reads == 1 && result.Network != null && result.Network.ScenarioContext.ActiveScenarioId == 17 && !result.Reading,
                "Injected TEST_ONLY read demonstrates fresh startup state without using the actual model reader.");
            Check(File.ReadAllText(project) == before && result.State.StateRevision == saved.State.StateRevision,
                "Optional read requests no metadata save or engineering execution.");
        }

        private static void MvpStartup_MissingReferenceAndReadFailure()
        {
            var saved = StartupProject(false); int reads = 0; ProjectWorkspace published = null;
            string project = Path.Combine(saved.DirectoryPath, "project.json");
            var openOnly = SingleOperatorProjectStartup.OpenAsync(StartupOptions(project), (w, s) => {
                Check(s == "No model selected.", "Open-only projects without a model remain valid and unresolved.");
            }, null).GetAwaiter().GetResult();
            Check(openOnly.Network == null, "No missing model is invented.");
            StartupReject(() => SingleOperatorProjectStartup.OpenAsync(StartupOptions(project, true),
                (w, s) => published = w, (w, p) => { reads++; return Task.CompletedTask; }).GetAwaiter().GetResult(), "REFERENCED_MODEL_UNAVAILABLE");
            Check(reads == 0 && published.Network == null, "Explicit read without a saved reference is blocked before the reader seam.");
            saved = StartupProject(); published = null;
            StartupReject(() => SingleOperatorProjectStartup.OpenAsync(StartupOptions(Path.Combine(saved.DirectoryPath, "project.json"), true),
                (w, s) => published = w, (w, p) => { reads++; return Task.FromException(new InvalidOperationException("TEST_ONLY_READ_FAILURE")); })
                .GetAwaiter().GetResult(), "TEST_ONLY_READ_FAILURE");
            Check(reads == 1 && published.Network == null && published.Baseline == null && !published.Reading,
                "Failed injected read is not retried and leaves no fabricated source or authorization state.");
            // Runtime-emitted test doubles are not vendor assemblies. The P15 solver fixtures deliberately
            // define synthetic Haestad.* assemblies in this AppDomain (including two same-named ones, to
            // prove ambiguity rejection) and dynamic assemblies cannot be unloaded. The guard therefore
            // scopes to real, disk-backed assemblies and still fails if headless startup loads any of them.
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic &&
                    (a.GetName().Name.StartsWith("Haestad.") || a.GetName().Name.StartsWith("Bentley."))),
                "Affected headless startup tests load no vendor assembly.");
        }
    }
}
