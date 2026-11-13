using System;
using System.Drawing;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    // General design-plan Execute result surface. This panel does not invoke Bentley or infer results:
    // it accepts only the same trusted production presentation constructed by the existing
    // SingleOperatorOutcomeView and its live result-content authority checks.
    internal sealed class VerifiedHydraulicResultsPanel : UserControl
    {
        internal readonly Label State = new Label
        {
            Name = "GeneralHydraulicResultState", Dock = DockStyle.Top, Height = 34,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.LightSkyBlue,
            Text = "  No accepted hydraulic result for the current execution."
        };
        internal readonly TextBox Details = new TextBox
        {
            Name = "GeneralHydraulicResultDetails", Dock = DockStyle.Fill,
            ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10), BackColor = Color.FromArgb(20, 25, 34),
            ForeColor = Color.WhiteSmoke
        };
        internal VerifiedHydraulicResultsPanel()
        {
            Dock = DockStyle.Fill;
            Name = "VerifiedHydraulicResultsPanel";
            Controls.Add(Details);
            Controls.Add(State);
            ClearForModelChange();
        }
        internal void ClearForModelChange()
        {
            Details.Text = "No hydraulic observations from a current verified execution.";
            State.Text = "  RESULT NOT ESTABLISHED";
            State.ForeColor = Color.LightSkyBlue;
        }
        internal bool ShowOutcome(SingleOperatorOutcomeView execution)
        {
            ClearForModelChange(); // A failed/new result must never leave stale accepted flow visible.
            var data = execution?.HydraulicPresentation;
            if (execution == null || execution.Classification != "PASS" ||
                data == null || !data.TrustedProvenanceEstablished ||
                data.StatusToken != GravityHydraulicResultPresentation.GenuineToken ||
                data.Lineage == null || string.IsNullOrWhiteSpace(data.ResultFingerprint) ||
                string.IsNullOrWhiteSpace(execution.ExistingEvidenceDirectory))
            {
                State.Text = "  BLOCKED / UNAVAILABLE — no trusted genuine hydraulic result.";
                State.ForeColor = Color.Salmon;
                Details.Text = "Execution did not establish a genuine trusted result for display.\r\n" +
                    (data?.Describe() ?? "No accepted hydraulic result was returned.");
                return false;
            }
            // The existing projection contains all genuine conduit/structure metrics, units,
            // verification findings, and exact model/source/plan provenance. No fixed P16 values.
            Details.Text = execution.Text;
            State.Text = "  VERIFIED EXECUTION RESULT — " + data.StatusToken +
                " (engineering approval is separate)";
            State.ForeColor = Color.LightGreen;
            return true;
        }
    }
}
