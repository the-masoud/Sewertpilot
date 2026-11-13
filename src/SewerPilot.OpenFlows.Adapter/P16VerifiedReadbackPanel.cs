using System;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Evidence import into the real SewerPilot workspace. It never runs a solver, opens a
    // Bentley model, or confuses a previously accepted receipt with the active project.
    internal static class P16VerifiedReadbackReceipt
    {
        internal sealed class View
        {
            internal string Summary;
            internal string Status;
            internal string Flow;
            internal string EventId;
        }

        internal static View Read(string evidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
                throw new InvalidDataException("Select a P16 readback evidence directory.");
            string directory = Path.GetFullPath(evidenceDirectory);
            WorkingCopyIntegrity.RejectLinks(directory);
            string requestPath = Path.Combine(directory, "request.json");
            string resultPath = Path.Combine(directory, "result.json");
            WorkingCopyIntegrity.RejectLinks(requestPath);
            WorkingCopyIntegrity.RejectLinks(resultPath);
            if (!File.Exists(requestPath) || !File.Exists(resultPath) ||
                new FileInfo(requestPath).Length > 256 * 1024 ||
                new FileInfo(resultPath).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("P16 receipt is missing or exceeds its size bound.");

            var request = RuntimeHostProtocol.Serializer().Deserialize<HostRequest>(File.ReadAllText(requestPath));
            if (request != null && request.Operation == P16FarzaneganNativeReadHost.Operation)
                return P16FarzaneganNativeDesktopExecution.ReadReceipt(directory);
            if (request == null || request.Operation != P16AzadeganCalculationHost.Operation ||
                request.Diagnostics == null ||
                !request.Diagnostics.TryGetValue(P16AzadeganCalculationHost.PhaseKey, out string phase) ||
                phase != P16AzadeganCalculationHost.PhaseRead)
                throw new InvalidDataException("This is not a native P16 READ receipt.");

            // The same strict result/observation validators as the RuntimeHost readback.
            P16AzadeganCalculationHost.ValidateRequest(request);
            HostResult result = RuntimeHostProtocol.ParseResult(resultPath, request);
            if (!result.Success)
                throw new InvalidDataException("The P16 RuntimeHost did not accept this readback.");
            P16AzadeganCalculationHost.ValidateResult(result);
            if (!result.Diagnostics.TryGetValue(P16AzadeganCalculationHost.EvidenceKey, out string json))
                throw new InvalidDataException("Hydraulic observation is absent.");
            var observed = RuntimeHostProtocol.Serializer().Deserialize<P16AzadeganCalculationObservation>(json);
            if (observed == null || !observed.FreshnessVerified || observed.FlowReadInvocations != 1 ||
                observed.Status != P16AzadeganCalculationHost.PassStatus &&
                observed.Status != P16AzadeganCalculationHost.PassWithWarningsStatus)
                throw new InvalidDataException("Hydraulic readback is not verified.");

            // Do not display a previous receipt as current if the persisted working files
            // have been replaced or modified since that run.
            string workingDirectory = Path.GetDirectoryName(request.WorkingModelPath);
            string currentInventory = P16AzadeganCalculationHost.InventoryJson(
                P16AzadeganCalculationHost.Inventory(workingDirectory));
            if (!string.Equals(currentInventory, observed.OutputInventoryJson, StringComparison.Ordinal))
                throw new InvalidDataException("Saved working-copy hashes differ from the accepted readback.");
            if (!result.Diagnostics.TryGetValue("P16OpenQuestionPolicyClean", out string clean) || clean != "true" ||
                !result.Diagnostics.TryGetValue("P16ReopenedInventoryMatchesPersistedOutput", out string matched) || matched != "true")
                throw new InvalidDataException("P16 open-prompt or persisted-file proof is not complete.");

            var lines = new StringBuilder();
            lines.AppendLine("ACCEPTED NATIVE P16 READBACK  |  Imported evidence (not a new calculation)");
            lines.AppendLine("Status: " + observed.Status);
            lines.AppendLine();
            lines.AppendLine("Conduit: " + observed.ObservedElementLabel + " (Element ID " + observed.ObservedElementId + ")");
            lines.AppendLine("FLOW: " + observed.RawValue + " " + observed.WorkingUnitSymbol);
            lines.AppendLine("Time: " + observed.TimeSeconds.Value.ToString("R", CultureInfo.InvariantCulture) + " s");
            lines.AppendLine("Scenario: " + observed.ActiveScenarioId + "  |  Physical alternative: " + observed.PhysicalAlternativeId +
                "  |  Calculation options: " + observed.CalculationOptionsId);
            lines.AppendLine("Engine: " + observed.ResultManagerEngineName);
            lines.AppendLine("Field / record: " + observed.FieldName + " / " + observed.RecordName);
            lines.AppendLine();
            lines.AppendLine("Freshness: VERIFIED  |  Persisted calculation event: " + observed.CalculationEventId);
            lines.AppendLine("Solver status: " + observed.ScenarioComputeStatus + "  |  Saved: " + observed.SaveSucceeded +
                "  |  Reopened: " + observed.ReopenOccurred + "  |  HasResults: " + observed.HasResults);
            lines.AppendLine("Saved working-copy hashes: VERIFIED AGAINST READ RECEIPT");
            lines.AppendLine("Result read: " + observed.ReadTimestamp);
            lines.AppendLine("Working copy: " + observed.OpenedModelPath);
            lines.AppendLine();
            lines.AppendLine("BENTLEY WARNINGS (" + observed.Notifications.Length + ") — not engineering approval:");
            foreach (var notification in observed.Notifications)
                lines.AppendLine("  [" + notification.Level + " #" + notification.MessageId + "] " +
                    (string.IsNullOrEmpty(notification.Message) ? notification.MessageKey : notification.Message));
            if (observed.Notifications.Length == 0) lines.AppendLine("  None captured");
            lines.AppendLine();
            lines.AppendLine("PROVENANCE");
            lines.AppendLine(observed.ProvenanceChain);
            lines.AppendLine();
            lines.AppendLine("Scope: evidence imported from an existing disposable model run. This tab does NOT calculate,");
            lines.AppendLine("alter the current project, certify an engineering design, or imply results for another model.");
            return new View
            {
                Summary = lines.ToString(),
                Status = observed.Status,
                Flow = observed.RawValue + " " + observed.WorkingUnitSymbol,
                EventId = observed.CalculationEventId
            };
        }
    }

    internal sealed class P16VerifiedReadbackPanel : UserControl
    {
        internal readonly TextBox ReceiptPath = new TextBox {
            Name = "P16ReceiptPath", ReadOnly = true, Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 35, 47), ForeColor = Color.WhiteSmoke
        };
        internal readonly Label State = new Label {
            Name = "P16VerificationState", Dock = DockStyle.Top, Height = 30,
            ForeColor = Color.LightSkyBlue, BackColor = Color.FromArgb(25, 32, 45),
            TextAlign = ContentAlignment.MiddleLeft, Text = "  No verified P16 evidence imported."
        };
        internal readonly TextBox Details = new TextBox {
            Name = "P16HydraulicResult", Multiline = true, ReadOnly = true, WordWrap = false,
            ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10), BackColor = Color.FromArgb(20, 25, 34),
            ForeColor = Color.WhiteSmoke, Text = "Select an existing P16 READ evidence folder, then validate the receipt."
        };
        private readonly Button browse = new Button { Text = "Choose evidence folder", Width = 168, Height = 27 };
        private readonly Button verify = new Button { Text = "Verify & show", Width = 116, Height = 27 };
        internal readonly Button CalculateNative = new Button {
            Name = "P16NativeCalculateAndShow", Text = "Calculate native + show", Width = 188, Height = 27,
            Enabled = false
        };

        internal P16VerifiedReadbackPanel()
        {
            Name = "P16NativeReadbackPanel";
            AccessibleName = "Verified native P16 calculation results";
            Dock = DockStyle.Fill;
            var actions = new TableLayoutPanel { Dock = DockStyle.Top, Height = 37, ColumnCount = 4,
                BackColor = Color.FromArgb(36, 43, 57) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 173));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 121));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
            actions.Controls.Add(browse, 0, 0);
            actions.Controls.Add(ReceiptPath, 1, 0);
            actions.Controls.Add(verify, 2, 0);
            actions.Controls.Add(CalculateNative, 3, 0);
            Controls.Add(Details);
            Controls.Add(State);
            Controls.Add(actions);
            browse.Click += (s, e) => {
                using (var chooser = new FolderBrowserDialog { Description = "Select an existing P16 READ receipt folder" })
                    if (chooser.ShowDialog(FindForm()) == DialogResult.OK) ReceiptPath.Text = chooser.SelectedPath;
            };
            verify.Click += (s, e) => {
                try { LoadReceipt(ReceiptPath.Text); }
                catch (Exception error) {
                    State.Text = "  UNVERIFIED: " + error.GetType().Name;
                    State.ForeColor = Color.Salmon;
                    Details.Text = "Evidence not accepted. No hydraulic value is shown.\r\n" + error.Message;
                }
            };
        }

        internal void ClearForModelChange()
        {
            CalculateNative.Enabled = false;
            ReceiptPath.Clear();
            Details.Text = "No P16 calculation is associated with the currently selected model.";
            State.Text = "  No result loaded for the selected model.";
            State.ForeColor = Color.LightSkyBlue;
        }

        internal void BeginNativeCalculation()
        {
            CalculateNative.Enabled = false;
            ReceiptPath.Clear();
            Details.Text = "Bentley calculation in progress on a disposable copy; no result accepted yet.";
            State.Text = "  CALCULATING — do not close this window.";
            State.ForeColor = Color.LightSkyBlue;
        }

        internal void RejectNativeCalculation(string message)
        {
            ReceiptPath.Clear();
            Details.Text = "Native P16 calculation was not accepted. No flow value is displayed.\r\n" + message;
            State.Text = "  BLOCKED — no accepted hydraulic result.";
            State.ForeColor = Color.Salmon;
        }

        internal P16VerifiedReadbackReceipt.View LoadReceipt(string directory)
        {
            // Clear any previous value before running the proof; failures cannot retain stale flow.
            Details.Clear();
            State.Text = "  Verifying current saved model bytes and readback evidence...";
            State.ForeColor = Color.LightSkyBlue;
            var view = P16VerifiedReadbackReceipt.Read(directory);
            ReceiptPath.Text = Path.GetFullPath(directory);
            Details.Text = view.Summary;
            State.Text = "  VERIFIED: " + view.Flow + "  |  " + view.Status;
            State.ForeColor = Color.LightGreen;
            return view;
        }
    }
}
