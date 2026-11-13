using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Optional internal diagnostics. A shared mutex orders entries across AppDomains
    // and threads; every entry is flushed to disk before a boundary is invoked.
    internal static class RuntimeStageJournal
    {
        internal const string EnvironmentName = "SEWERPILOT_P4_STAGE_JOURNAL";
        internal static T Call<T>(string name, Func<T> action, Func<T, object> detail = null)
        {
            Record(name, "ENTER");
            try
            {
                T result = action();
                Record(name, "EXIT", detail == null ? null : detail(result));
                return result;
            }
            catch (Exception error) { Record(name, "EXCEPTION", null, error.ToString()); throw; }
        }
        internal static void Call(string name, Action action)
        { Call(name, () => { action(); return true; }); }
        internal static void Record(string name, string state, object detail = null, string exception = null)
        {
            string path = Environment.GetEnvironmentVariable(EnvironmentName);
            if (string.IsNullOrWhiteSpace(path)) return;
            int pid = Process.GetCurrentProcess().Id;
            using (var mutex = new Mutex(false, "Local\\SewerPilot.P4.Journal." + pid))
            {
                try { mutex.WaitOne(); } catch (AbandonedMutexException suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in RuntimeStageJournal.cs: " + suppressedEx.Message); }
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                    int number = 1;
                    if (File.Exists(path))
                    {
                        string last = File.ReadLines(path).LastOrDefault();
                        if (last != null)
                        {
                            var entry = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(last);
                            number = Convert.ToInt32(entry["stage_number"]) + 1;
                        }
                    }
                    string json = serializer.Serialize(new {
                        stage_number = number, stage_name = name, utc = DateTime.UtcNow.ToString("o"),
                        process_id = pid, managed_thread_id = Thread.CurrentThread.ManagedThreadId,
                        appdomain_id = AppDomain.CurrentDomain.Id,
                        apartment_state = Thread.CurrentThread.GetApartmentState().ToString(),
                        message_loop = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && Application.MessageLoop,
                        synchronization_context = SynchronizationContext.Current?.GetType().FullName,
                        state, detail, exception
                    });
                    using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(json + "\n");
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
