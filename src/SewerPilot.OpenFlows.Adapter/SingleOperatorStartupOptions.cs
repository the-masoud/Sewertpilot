using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class SingleOperatorStartupOptions
    {
        public string ProjectJsonPath { get; }
        public bool ReadReferencedModel { get; }

        private SingleOperatorStartupOptions(string projectJsonPath, bool readReferencedModel)
        { ProjectJsonPath = projectJsonPath; ReadReferencedModel = readReferencedModel; }

        internal static SingleOperatorStartupOptions Default => new SingleOperatorStartupOptions(null, false);

        // False preserves dispatch to the historical CLI; malformed startup flags never reach it.
        public static bool TryParse(string[] args, out SingleOperatorStartupOptions options)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            options = null;
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--mvp"))
            { options = Default; return true; }
            if (args[0] == "--open-project")
            {
                if ((args.Length != 2 && args.Length != 3) ||
                    (args.Length == 3 && args[2] != "--read-referenced-model"))
                    throw new ArgumentException("Use --open-project <absolute-project.json-path> [--read-referenced-model].");
                options = ForProjectFile(args[1], args.Length == 3);
                return true;
            }
            if (args.Any(a => a == "--open-project" || a == "--read-referenced-model"))
                throw new ArgumentException("--read-referenced-model requires --open-project and must follow its project path.");
            return false;
        }

        internal static SingleOperatorStartupOptions ForProjectFile(string path, bool readReferencedModel)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An absolute project.json path is required.");
            bool drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            bool unc = path.StartsWith(@"\\", StringComparison.Ordinal) &&
                path.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length >= 3;
            if (!drive && !unc) throw new ArgumentException("An absolute project.json path is required.");
            string fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetFileName(fullPath), "project.json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select the project's project.json file.");
            return new SingleOperatorStartupOptions(fullPath, readReferencedModel);
        }
    }

    // The shared startup/dialog coordinator performs no GUI or vendor operations itself.
    internal static class SingleOperatorProjectStartup
    {
        internal static async Task<ProjectWorkspace> OpenAsync(SingleOperatorStartupOptions options,
            Action<ProjectWorkspace, string> opened, Func<ProjectWorkspace, string, Task> readModel)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ProjectJsonPath == null) return null;
            if (options.ReadReferencedModel && readModel == null) throw new ArgumentNullException(nameof(readModel));
            var workspace = await Task.Run(() => ProjectWorkspace.Open(Path.GetDirectoryName(options.ProjectJsonPath)));
            string status = await Task.Run(() => workspace.ModelReferenceStatus());
            opened?.Invoke(workspace, status);
            if (options.ReadReferencedModel)
            {
                string model = workspace.State.Model?.BaselineLocationMetadata;
                if (string.IsNullOrWhiteSpace(model))
                    throw new InvalidOperationException("REFERENCED_MODEL_UNAVAILABLE: The project has no saved model reference.");
                await readModel(workspace, model);
            }
            return workspace;
        }
    }
}
