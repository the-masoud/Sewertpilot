using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static class P16NativeDesktopLiveAcceptance
    {
        // Executes exactly one real Bentley CALCULATE+SAVE and one fresh READ from the current
        // qualified source model, then renders the resulting receipt in the actual desktop form.
        internal static int Run(string source)
        {
            string[] family = null;
            string[] sourceBefore = null;
            P16NativeDesktopExecution.Receipt receipt = null;
            var sourceDir = Path.GetDirectoryName(Path.GetFullPath(source));
            try
            {
                P16NativeDesktopExecution.ValidateSource(source);
                family = P16AzadeganCalculationHost.Family.Select(n => Path.Combine(sourceDir, n)).ToArray();
                sourceBefore = P16AzadeganCalculationProof.Manifest(family).ToArray();
                Console.WriteLine("P16_LIVE_DESKTOP_START=one solver, one save, one read; zero original writes.");
                receipt = P16NativeDesktopExecution.Execute(source, message => Console.WriteLine("STAGE=" + message));
                if (receipt == null || receipt.View == null)
                    throw new InvalidDataException("P16 same-run receipt missing.");
                if (!sourceBefore.SequenceEqual(P16AzadeganCalculationProof.Manifest(family)))
                    throw new InvalidDataException("Protected source family changed.");
                Exception uiError = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        using (var form = new ProjectWorkspaceForm(GravityOperatingMode.SingleOperator))
                        {
                            var tabs = form.Controls.OfType<TabControl>().Single(t => t.Name == "WorkspaceTabs");
                            var tab = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "P16 Native Results");
                            tabs.SelectedTab = tab;
                            form.Show();
                            Application.DoEvents();
                            var shown = form.NativeResultsWorkspace.LoadReceipt(receipt.EvidenceDirectory);
                            Application.DoEvents();
                            if (shown.EventId != receipt.View.EventId ||
                                shown.Flow != receipt.View.Flow ||
                                !form.NativeResultsWorkspace.Details.Text.Contains(shown.Flow) ||
                                !form.NativeResultsWorkspace.Details.Text.Contains("30183") ||
                                !form.NativeResultsWorkspace.Details.IsHandleCreated ||
                                !form.NativeResultsWorkspace.Details.Visible)
                                throw new InvalidDataException("The current P16 calculation did not render in the live WinForms form.");
                            Console.WriteLine("SAME_RUN_UI=PASS");
                            Console.WriteLine("OBSERVED_FLOW=" + shown.Flow);
                            Console.WriteLine("CALCULATION_EVENT=" + shown.EventId);
                            Console.WriteLine("EVIDENCE_DIRECTORY=" + receipt.EvidenceDirectory);
                            form.Close();
                        }
                    }
                    catch (Exception error) { uiError = error; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!thread.Join(20000)) throw new TimeoutException("P16 WinForms display exceeded 20 seconds.");
                if (uiError != null) throw uiError;
                Console.WriteLine("P16_NATIVE_AUTOMATIC_DESKTOP_E2E=PASS");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("P16_NATIVE_AUTOMATIC_DESKTOP_E2E=BLOCKED");
                Console.Error.WriteLine(error.ToString());
                if (receipt != null) Console.Error.WriteLine("EVIDENCE_DIRECTORY=" + receipt.EvidenceDirectory);
                return 1;
            }
            finally
            {
                if (family != null && sourceBefore != null)
                    Console.WriteLine("SOURCE_FAMILY_INTEGRITY=" +
                        (sourceBefore.SequenceEqual(P16AzadeganCalculationProof.Manifest(family)) ? "PASS" : "FAILED"));
            }
        }
    }
}
