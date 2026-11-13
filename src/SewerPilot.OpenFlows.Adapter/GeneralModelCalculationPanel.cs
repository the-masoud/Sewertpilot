using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // Engineer-facing panel for calculating the currently selected model's own saved scenario.
    // It never displays a value unless the calculation and the independent readback both succeeded.
    internal sealed class GeneralModelCalculationPanel : UserControl
    {
        internal readonly Button CalculateExistingModel = new Button
        {
            Name = "CalculateExistingModel",
            Text = "Calculate Existing Model",
            Width = 196,
            Height = 28,
            Enabled = false
        };
        internal readonly Button OpenEvidenceFolder = new Button
        {
            Name = "OpenCalculationEvidence",
            Text = "Open evidence folder",
            Width = 160,
            Height = 28,
            Enabled = false
        };
        internal readonly Label State = new Label
        {
            Name = "CalculationState",
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = Color.LightSkyBlue,
            BackColor = Color.FromArgb(25, 32, 45),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Open a SewerGEMS model to enable calculation."
        };
        internal readonly DataGridView Results = new DataGridView
        {
            Name = "HydraulicResults",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            BackgroundColor = Color.FromArgb(20, 25, 34),
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false
        };
        internal readonly TextBox Details = new TextBox
        {
            Name = "CalculationDetails",
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Bottom,
            Height = 132,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(20, 25, 34),
            ForeColor = Color.WhiteSmoke,
            Text = "No calculation has been run for the selected model."
        };

        private GeneralCalculationReceipt lastReceipt;

        internal GeneralModelCalculationPanel()
        {
            Name = "GeneralModelCalculationPanel";
            AccessibleName = "Calculate existing model hydraulic results";
            Dock = DockStyle.Fill;

            Results.Columns.Add(Column("Category", 12));
            Results.Columns.Add(Column("Element", 22));
            Results.Columns.Add(Column("ID", 8));
            Results.Columns.Add(Column("Metric", 14));
            Results.Columns.Add(Column("Value", 16));
            Results.Columns.Add(Column("Unit", 10));
            Results.Columns.Add(Column("Availability", 18));
            Results.DefaultCellStyle.BackColor = Color.FromArgb(26, 32, 44);
            Results.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
            Results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 78, 110);
            Results.DefaultCellStyle.SelectionForeColor = Color.White;
            Results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 46, 60);
            Results.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                ColumnCount = 3,
                BackColor = Color.FromArgb(36, 43, 57)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.Controls.Add(CalculateExistingModel, 0, 0);
            actions.Controls.Add(OpenEvidenceFolder, 1, 0);

            Controls.Add(Results);
            Controls.Add(Details);
            Controls.Add(State);
            Controls.Add(actions);

            OpenEvidenceFolder.Click += (s, e) =>
            {
                if (lastReceipt == null || !Directory.Exists(lastReceipt.EvidenceDirectory)) return;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lastReceipt.EvidenceDirectory)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception) { /* Opening the folder is a convenience only. */ }
            };
        }

        private static DataGridViewTextBoxColumn Column(string header, int fillWeight) =>
            new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = fillWeight, SortMode = DataGridViewColumnSortMode.NotSortable };

        internal void SetModelAvailable(bool available)
        {
            CalculateExistingModel.Enabled = available;
            if (!available)
            {
                State.Text = "  Open a SewerGEMS model to enable calculation.";
                State.ForeColor = Color.LightSkyBlue;
            }
            else if (lastReceipt == null)
            {
                State.Text = "  Ready — the selected model can be calculated on a disposable copy.";
                State.ForeColor = Color.LightSkyBlue;
            }
        }

        internal void BeginCalculation()
        {
            CalculateExistingModel.Enabled = false;
            Results.Rows.Clear();
            lastReceipt = null;
            OpenEvidenceFolder.Enabled = false;
            Details.Text = "The Bentley engine is running once on a disposable copy of the selected model.\r\n" +
                           "The original model file is not modified. Please wait.";
            State.Text = "  CALCULATING — this may take several minutes for a large network.";
            State.ForeColor = Color.LightSkyBlue;
        }

        internal void RejectCalculation(string message)
        {
            Results.Rows.Clear();
            lastReceipt = null;
            OpenEvidenceFolder.Enabled = false;
            Details.Text = "No hydraulic result is shown, because the calculation or its readback did not succeed.\r\n\r\n" + message;
            State.Text = "  NOT CALCULATED — no accepted hydraulic result for this model.";
            State.ForeColor = Color.Salmon;
        }

        internal void ShowReceipt(GeneralCalculationReceipt receipt)
        {
            if (receipt == null || receipt.Rows == null || receipt.Rows.Length == 0)
            {
                RejectCalculation("The calculation returned no result rows.");
                return;
            }
            lastReceipt = receipt;
            OpenEvidenceFolder.Enabled = true;
            Results.SuspendLayout();
            Results.Rows.Clear();
            foreach (var row in receipt.Rows)
            {
                Results.Rows.Add(
                    row.Category,
                    string.IsNullOrEmpty(row.ElementLabel) ? "(no label)" : row.ElementLabel,
                    row.ElementId.ToString(CultureInfo.InvariantCulture),
                    row.Metric,
                    Format(row),
                    row.Unit,
                    string.Equals(row.Status, "AVAILABLE", StringComparison.Ordinal) ? "available" : "not available");
            }
            Results.ResumeLayout();
            Details.Text = receipt.Summary;
            State.Text = "  CALCULATED — scenario " + receipt.ScenarioId + ", engine " + receipt.Engine + ", " +
                         receipt.Rows.Count(row => string.Equals(row.Status, "AVAILABLE", StringComparison.Ordinal)) +
                         " values available.";
            State.ForeColor = Color.LightGreen;
        }

        private static string Format(GeneralHydraulicResultRow row)
        {
            if (!string.Equals(row.Status, "AVAILABLE", StringComparison.Ordinal)) return "—";
            if (row.Value.HasValue)
                return row.Value.Value.ToString("0.####", CultureInfo.InvariantCulture);
            return row.RawValue ?? "—";
        }
    }
}
