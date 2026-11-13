using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // One recorded failed operation, as surfaced by the diagnostics export.
    public sealed class DiagnosticFailureRecord
    {
        public string TimestampUtc { get; set; }
        public string Operation { get; set; }
        public string ExceptionType { get; set; }
        public string Message { get; set; }
        public string Detail { get; set; }
    }

    // Lightweight, local-only application diagnostics for the engineering beta.
    // Nothing is ever uploaded. Records are written under the current user's local
    // application data so an installed beta never writes into its own program files.
    public static class ApplicationDiagnostics
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object Gate = new object();
        private static int attached;

        // Test-only isolation of the log directory. Null in every production path.
        internal static string DirectoryOverride { get; set; }

        public static string LogDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DirectoryOverride)) return DirectoryOverride;
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(root))
                    root = Path.GetTempPath();
                return Path.Combine(root, "SewerPilot", "logs");
            }
        }

        public static string LogFilePath { get { return Path.Combine(LogDirectory, "sewerpilot.log"); } }

        public static string LastFailureFilePath { get { return Path.Combine(LogDirectory, "last-failure.json"); } }

        // Installs process-wide handlers exactly once so an unexpected failure is captured
        // rather than disappearing with the window.
        public static void Attach()
        {
            if (Interlocked.Exchange(ref attached, 1) == 1) return;
            try
            {
                System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                System.Windows.Forms.Application.ThreadException += (s, e) => RecordFailure("Unhandled user-interface error", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => RecordFailure("Unhandled application error", e.ExceptionObject as Exception);
            }
            catch (Exception error)
            {
                RecordFailure("Diagnostics handler installation", error);
            }
        }

        public static void RecordOperationStart(string operation)
        {
            Write("INFO", operation, null);
        }

        public static void RecordMessage(string message)
        {
            Write("INFO", message, null);
        }

        public static void RecordFailure(string operation, Exception error)
        {
            Write("ERROR", operation, error);
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(LastFailureFilePath, BuildFailureRecord(operation, error), new UTF8Encoding(false));
            }
            catch (Exception) { /* Diagnostics must never break the application. */ }
        }

        // Reads the recorded last failure as a plain-text record, or null when none exists.
        public static DiagnosticFailureRecord ReadLastFailure()
        {
            try
            {
                if (!File.Exists(LastFailureFilePath)) return null;
                var lines = File.ReadAllLines(LastFailureFilePath);
                var record = new DiagnosticFailureRecord();
                int index = 0;
                for (; index < lines.Length; index++)
                {
                    string line = lines[index];
                    if (line.StartsWith("detail=", StringComparison.Ordinal))
                    {
                        var detail = new StringBuilder(line.Substring("detail=".Length));
                        for (index++; index < lines.Length; index++)
                            detail.Append("\r\n").Append(lines[index]);
                        record.Detail = detail.ToString();
                        break;
                    }
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    if (key == "timestamp_utc") record.TimestampUtc = value;
                    else if (key == "operation") record.Operation = value;
                    else if (key == "exception_type") record.ExceptionType = value;
                    else if (key == "message") record.Message = value;
                }
                return record.Operation == null && record.Message == null ? null : record;
            }
            catch (Exception) { return null; }
        }

        // Returns the tail of the log, bounded so a diagnostic export stays small.
        public static string ReadLogTail(int maxCharacters)
        {
            try
            {
                if (!File.Exists(LogFilePath)) return string.Empty;
                string text = File.ReadAllText(LogFilePath);
                if (text.Length <= maxCharacters) return text;
                return text.Substring(text.Length - maxCharacters);
            }
            catch (Exception) { return string.Empty; }
        }

        private static string BuildFailureRecord(string operation, Exception error)
        {
            // Plain key/value text so the diagnostics exporter can redact raw values before
            // any JSON escaping is applied. The detail section runs to the end of the file.
            var sb = new StringBuilder();
            sb.Append("timestamp_utc=").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("operation=").Append(SingleLine(operation)).Append("\r\n");
            sb.Append("exception_type=").Append(SingleLine(error == null ? null : error.GetType().FullName)).Append("\r\n");
            sb.Append("message=").Append(SingleLine(error == null ? null : error.Message)).Append("\r\n");
            sb.Append("detail=").Append(error == null ? string.Empty : error.ToString());
            return sb.ToString();
        }

        private static string SingleLine(string value)
        {
            return value == null ? string.Empty : value.Replace("\r", " ").Replace("\n", " ");
        }

        private static void Write(string level, string message, Exception error)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfOversized();
                    var line = new StringBuilder();
                    line.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append(' ').Append(level).Append(' ');
                    line.Append(message ?? string.Empty);
                    if (error != null)
                        line.Append(" | ").Append(error.GetType().FullName).Append(": ").Append(error.Message);
                    File.AppendAllText(LogFilePath, line.ToString() + "\r\n", new UTF8Encoding(false));
                }
            }
            catch (Exception) { /* Diagnostics must never break the application. */ }
        }

        private static void RotateIfOversized()
        {
            var info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length <= MaxLogBytes) return;
            string previous = LogFilePath + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(LogFilePath, previous);
        }
    }
}
