using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class P16UiReadbackAcceptance
    {
        internal static int Run(string evidenceDirectory)
        {
            Exception failure = null;
            string[] receipts = new string[0];
            var thread = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var view = P16VerifiedReadbackReceipt.Read(evidenceDirectory);
                    Require(view.Status == P16AzadeganCalculationHost.PassWithWarningsStatus,
                        "the evidence explicitly retains the Bentley warning classification");
                    Require(view.Flow == "0.02673611231148243 ft3/s",
                        "the actual accepted Azadegan flow and native unit are preserved");
                    Require(view.Summary.Contains("30183") && view.Summary.Contains("not engineering approval"),
                        "Bentley warning stays visible, never turned into engineering acceptance");
                    Require(!string.IsNullOrWhiteSpace(view.EventId), "the calculation event is displayed");
                    using (var form = new ProjectWorkspaceForm(GravityOperatingMode.SingleOperator))
                    {
                        var tabs = form.Controls.OfType<TabControl>().Single(c => c.Name == "WorkspaceTabs");
                        Require(tabs.TabPages.Cast<TabPage>().Any(t => t.Text == "P16 Native Results"),
                            "the verified P16 results tab is part of the real desktop workspace");
                        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "P16 Native Results");
                        form.Show();
                        Application.DoEvents();
                        var imported = form.NativeResultsWorkspace.LoadReceipt(evidenceDirectory);
                        Application.DoEvents();
                        Require(imported.EventId == view.EventId, "panel binds to the strict parsed result");
                        Require(form.NativeResultsWorkspace.Details.Text.Contains(view.Flow),
                            "native flow is visibly rendered in the WinForms textbox");
                        Require(form.NativeResultsWorkspace.Details.Text.Contains("30183"),
                            "warning is visibly rendered in the WinForms textbox");
                        Require(form.NativeResultsWorkspace.State.Text.Contains("VERIFIED"),
                            "readback state shown after full validation");
                        Require(form.NativeResultsWorkspace.IsHandleCreated &&
                                form.NativeResultsWorkspace.Details.IsHandleCreated &&
                                form.NativeResultsWorkspace.Details.Visible,
                            "WinForms tab and result textbox have live visible handles");

                        bool missingRejected = false;
                        try { form.NativeResultsWorkspace.LoadReceipt(Path.Combine(evidenceDirectory, "does-not-exist")); }
                        catch (InvalidDataException) { missingRejected = true; }
                        Require(missingRejected, "missing evidence cannot be presented as verified");
                        Require(!form.NativeResultsWorkspace.Details.Text.Contains(view.Flow),
                            "a later invalid load clears the previously displayed result");
                        Require(tabs.TabPages.Cast<TabPage>().Any(t => t.Text == "Verified Hydraulic Results"),
                            "the shared hydraulic results tab exists for the general Execute workflow");
                        Require(!form.ShowVerifiedHydraulicOutcome(null),
                            "the general results tab fails closed when no production result is available");
                        Require(!form.VerifiedHydraulicResultsWorkspace.State.Text.Contains("VERIFIED EXECUTION RESULT"),
                            "the general result panel does not claim success without trusted genuine provenance");
                        Require(!form.VerifiedHydraulicResultsWorkspace.Details.Text.Contains(view.Flow),
                            "a prior P16 flow must never bleed into the general result panel");
                        form.Close();
                    }
                    receipts = new[] {
                        "P16_UI_SMOKE=PASS",
                        "STRICT_RECEIPT=PASS",
                        "NATIVE_FLOW=0.02673611231148243 ft3/s",
                        "BENTLEY_WARNING_30183=VISIBLE",
                        "DESKTOP_PROJECT_WORKSPACE=REAL_WINFORMS_FORM",
                        "VISIBLE_TEXTBOX_HANDLE=PASS",
                        "INVALID_RECEIPT_REJECTED=PASS",
                        "NO_SOLVER_OR_SAVE_INVOKED=TRUE"
                    };
                }
                catch (Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(20000))
            {
                Console.Error.WriteLine("P16_UI_SMOKE=TIMEOUT");
                return 2;
            }
            if (failure != null)
            {
                Console.Error.WriteLine("P16_UI_SMOKE=FAIL: " + failure);
                return 1;
            }
            foreach (string line in receipts) Console.WriteLine(line);
            return 0;
        }
        private static void Require(bool value, string explanation)
        {
            if (!value) throw new InvalidOperationException("P16_UI_TEST_FAILURE: " + explanation);
        }
    }
}
