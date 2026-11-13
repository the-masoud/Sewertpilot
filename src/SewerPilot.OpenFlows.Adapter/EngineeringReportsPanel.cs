using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum EngineeringReportKind
    {
        ComprehensivePackage = 0,
        DesignBasis = 1,
        HydraulicDesign = 2,
        Violations = 3,
        DecisionRegister = 4,
        ModelAudit = 5
    }

    // Production WinForms UI Panel for Phase P27 Reporting & Audit Package Export.
    // Offline / headless testable: does not perform direct model writes or unverified solver calls.
    // Adapter public boundary must stay vendor/UI neutral; the WinForms surface is internal
    // to the solution (tests and SewerPilot.Desktop hold internals access).
    internal sealed class EngineeringReportsPanel : UserControl
    {
        private readonly FlowLayoutPanel topSelectorPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(28, 34, 46),
            Padding = new Padding(6, 4, 6, 4)
        };

        private readonly FlowLayoutPanel actionToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(34, 40, 54),
            Padding = new Padding(6, 4, 6, 4)
        };

        private readonly Label statusBanner = new Label
        {
            Name = "ReportStatusBanner",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Consolas", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(140, 205, 255),
            BackColor = Color.FromArgb(22, 26, 36),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "  Report Fingerprint: Unavailable (No active report generated)"
        };

        private readonly TextBox reportPreviewBox = new TextBox
        {
            Name = "ReportPreviewTextBox",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(18, 22, 30),
            ForeColor = Color.FromArgb(225, 235, 245),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None
        };

        private readonly Button btnPkg = CreateTabButton("All (Submission Package)", EngineeringReportKind.ComprehensivePackage);
        private readonly Button btnDb = CreateTabButton("1. Design Basis", EngineeringReportKind.DesignBasis);
        private readonly Button btnHd = CreateTabButton("2. Hydraulic Design", EngineeringReportKind.HydraulicDesign);
        private readonly Button btnVio = CreateTabButton("3. Violations", EngineeringReportKind.Violations);
        private readonly Button btnDec = CreateTabButton("4. Decision Register", EngineeringReportKind.DecisionRegister);
        private readonly Button btnAudit = CreateTabButton("5. Model Audit", EngineeringReportKind.ModelAudit);

        private readonly Button btnExportMd = new Button
        {
            Text = "Export Markdown (*.md)",
            Name = "BtnExportMarkdown",
            Width = 160,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.LightCyan,
            BackColor = Color.FromArgb(45, 60, 85)
        };

        private readonly Button btnExportJson = new Button
        {
            Text = "Export JSON Package (*.json)",
            Name = "BtnExportJson",
            Width = 180,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.LightGreen,
            BackColor = Color.FromArgb(35, 65, 45)
        };

        private readonly Button btnExportAll = new Button
        {
            Text = "Export All Reports to Folder",
            Name = "BtnExportAllReports",
            Width = 185,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Yellow,
            BackColor = Color.FromArgb(65, 55, 30)
        };

        private EngineeringReportPackage activePackage;
        private EngineeringReportKind activeKind = EngineeringReportKind.ComprehensivePackage;

        public EngineeringReportKind ActiveKind => activeKind;
        public string ActiveReportContent => reportPreviewBox.Text;
        public string StatusBannerText => statusBanner.Text;
        public EngineeringReportPackage ActivePackage => activePackage;

        public EngineeringReportsPanel()
        {
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(18, 22, 30);

            // Connect Report Selector Buttons
            btnPkg.Click += (s, e) => SwitchReport(EngineeringReportKind.ComprehensivePackage);
            btnDb.Click += (s, e) => SwitchReport(EngineeringReportKind.DesignBasis);
            btnHd.Click += (s, e) => SwitchReport(EngineeringReportKind.HydraulicDesign);
            btnVio.Click += (s, e) => SwitchReport(EngineeringReportKind.Violations);
            btnDec.Click += (s, e) => SwitchReport(EngineeringReportKind.DecisionRegister);
            btnAudit.Click += (s, e) => SwitchReport(EngineeringReportKind.ModelAudit);

            topSelectorPanel.Controls.AddRange(new Control[] { btnPkg, btnDb, btnHd, btnVio, btnDec, btnAudit });

            // Connect Action Buttons
            btnExportMd.Click += (s, e) => PromptExportMarkdown();
            btnExportJson.Click += (s, e) => PromptExportJson();
            btnExportAll.Click += (s, e) => PromptExportAll();

            actionToolbar.Controls.AddRange(new Control[] { btnExportMd, btnExportJson, btnExportAll });

            Controls.Add(reportPreviewBox);
            Controls.Add(statusBanner);
            Controls.Add(actionToolbar);
            Controls.Add(topSelectorPanel);

            HighlightActiveButton();
        }

        private static Button CreateTabButton(string text, EngineeringReportKind kind)
        {
            return new Button
            {
                Text = text,
                Tag = kind,
                Width = 140,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.WhiteSmoke,
                BackColor = Color.FromArgb(42, 50, 68),
                Font = new Font("Segoe UI", 8f)
            };
        }

        public void LoadPackage(EngineeringReportPackage package)
        {
            activePackage = package;
            RefreshCurrentReportView();
        }

        public void LoadContext(
            StormExecutiveReportContext stormContext,
            EngineeringDecisionRegister decisionRegister = null,
            NetworkReadResult network = null)
        {
            var pkg = EngineeringReportingEngine.BuildPackageFromContext(stormContext, decisionRegister, network);
            LoadPackage(pkg);
        }

        public void SwitchReport(EngineeringReportKind kind)
        {
            activeKind = kind;
            HighlightActiveButton();
            RefreshCurrentReportView();
        }

        private void HighlightActiveButton()
        {
            Button[] buttons = { btnPkg, btnDb, btnHd, btnVio, btnDec, btnAudit };
            foreach (var b in buttons)
            {
                if (b.Tag is EngineeringReportKind k && k == activeKind)
                {
                    b.BackColor = Color.FromArgb(70, 130, 220);
                    b.ForeColor = Color.White;
                    b.Font = new Font("Segoe UI", 8.2f, FontStyle.Bold);
                }
                else
                {
                    b.BackColor = Color.FromArgb(42, 50, 68);
                    b.ForeColor = Color.FromArgb(190, 205, 225);
                    b.Font = new Font("Segoe UI", 8f);
                }
            }
        }

        private void RefreshCurrentReportView()
        {
            if (activePackage == null)
            {
                reportPreviewBox.Text = "No active engineering report package loaded.\r\nLoad a project or run hydraulic verification to generate authoritative reports.";
                statusBanner.Text = "  Report Fingerprint: Unavailable";
                return;
            }

            string content = "";
            switch (activeKind)
            {
                case EngineeringReportKind.ComprehensivePackage:
                    content = EngineeringReportingEngine.GenerateComprehensiveSubmissionReport(activePackage);
                    break;
                case EngineeringReportKind.DesignBasis:
                    content = activePackage.DesignBasis != null
                        ? EngineeringReportingEngine.GenerateDesignBasisReport(activePackage.DesignBasis)
                        : "Design Basis section not available in active package.";
                    break;
                case EngineeringReportKind.HydraulicDesign:
                    content = activePackage.HydraulicDesign != null
                        ? EngineeringReportingEngine.GenerateHydraulicDesignReport(activePackage.HydraulicDesign)
                        : "Hydraulic Design section not available in active package.";
                    break;
                case EngineeringReportKind.Violations:
                    content = activePackage.Violations != null
                        ? EngineeringReportingEngine.GenerateViolationsReport(activePackage.Violations)
                        : "Violations section not available in active package.";
                    break;
                case EngineeringReportKind.DecisionRegister:
                    content = activePackage.DecisionRegister != null
                        ? EngineeringReportingEngine.GenerateDecisionRegisterReport(activePackage.DecisionRegister)
                        : "Decision Register not available in active package.";
                    break;
                case EngineeringReportKind.ModelAudit:
                    content = activePackage.ModelAudit != null
                        ? EngineeringReportingEngine.GenerateModelAuditReport(activePackage.ModelAudit)
                        : "Model Audit section not available in active package.";
                    break;
            }

            reportPreviewBox.Text = content;
            statusBanner.Text = string.Format("  Package Fingerprint (SHA-256): {0} | Items: {1} Conduits",
                activePackage.PackageSha256Fingerprint ?? activePackage.ComputeFingerprint(),
                activePackage.HydraulicDesign?.TotalConduitsCount ?? 0);
        }

        private void PromptExportMarkdown()
        {
            if (activePackage == null)
            {
                MessageBox.Show(this, "No active report package loaded to export.", "Export Notice");
                return;
            }

            string defaultName = GetDefaultMarkdownFilename(activeKind);
            using (var sfd = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|Text file (*.txt)|*.txt", FileName = defaultName })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    ExportActiveReportToPath(sfd.FileName);
                    MessageBox.Show(this, "Markdown report successfully exported to:\r\n" + sfd.FileName, "Export Succeeded");
                }
            }
        }

        private void PromptExportJson()
        {
            if (activePackage == null)
            {
                MessageBox.Show(this, "No active report package loaded to export.", "Export Notice");
                return;
            }

            using (var sfd = new SaveFileDialog { Filter = "JSON Package (*.json)|*.json", FileName = "engineering-report-package.json" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    ExportJsonPackageToPath(sfd.FileName);
                    MessageBox.Show(this, "JSON report package successfully exported to:\r\n" + sfd.FileName, "Export Succeeded");
                }
            }
        }

        private void PromptExportAll()
        {
            if (activePackage == null)
            {
                MessageBox.Show(this, "No active report package loaded to export.", "Export Notice");
                return;
            }

            using (var fbd = new FolderBrowserDialog { Description = "Select folder to export all 5 engineering reports and JSON package" })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    ExportAllReportsToDirectory(fbd.SelectedPath);
                    MessageBox.Show(this, "All 5 engineering reports and JSON package exported to:\r\n" + fbd.SelectedPath, "Export Complete");
                }
            }
        }

        public void ExportActiveReportToPath(string filePath)
        {
            if (activePackage == null) return;
            string content = reportPreviewBox.Text;
            File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
        }

        public void ExportJsonPackageToPath(string filePath)
        {
            if (activePackage == null) return;
            string json = EngineeringReportingEngine.ExportReportPackageToJson(activePackage);
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        }

        public void ExportAllReportsToDirectory(string targetDir)
        {
            if (activePackage == null) return;
            Directory.CreateDirectory(targetDir);

            // 1. Design Basis
            if (activePackage.DesignBasis != null)
                File.WriteAllText(Path.Combine(targetDir, "design-basis-report.md"),
                    EngineeringReportingEngine.GenerateDesignBasisReport(activePackage.DesignBasis), System.Text.Encoding.UTF8);

            // 2. Hydraulic Design
            if (activePackage.HydraulicDesign != null)
                File.WriteAllText(Path.Combine(targetDir, "hydraulic-design-report.md"),
                    EngineeringReportingEngine.GenerateHydraulicDesignReport(activePackage.HydraulicDesign), System.Text.Encoding.UTF8);

            // 3. Violations
            if (activePackage.Violations != null)
                File.WriteAllText(Path.Combine(targetDir, "violations-report.md"),
                    EngineeringReportingEngine.GenerateViolationsReport(activePackage.Violations), System.Text.Encoding.UTF8);

            // 4. Decision Register
            if (activePackage.DecisionRegister != null)
                File.WriteAllText(Path.Combine(targetDir, "engineering-decision-register.md"),
                    EngineeringReportingEngine.GenerateDecisionRegisterReport(activePackage.DecisionRegister), System.Text.Encoding.UTF8);

            // 5. Model Audit
            if (activePackage.ModelAudit != null)
                File.WriteAllText(Path.Combine(targetDir, "model-audit-report.md"),
                    EngineeringReportingEngine.GenerateModelAuditReport(activePackage.ModelAudit), System.Text.Encoding.UTF8);

            // 6. Comprehensive Submission Package
            File.WriteAllText(Path.Combine(targetDir, "comprehensive-engineering-submission.md"),
                EngineeringReportingEngine.GenerateComprehensiveSubmissionReport(activePackage), System.Text.Encoding.UTF8);

            // 7. JSON Package
            File.WriteAllText(Path.Combine(targetDir, "engineering-report-package.json"),
                EngineeringReportingEngine.ExportReportPackageToJson(activePackage), System.Text.Encoding.UTF8);
        }

        private static string GetDefaultMarkdownFilename(EngineeringReportKind kind)
        {
            switch (kind)
            {
                case EngineeringReportKind.DesignBasis: return "design-basis-report.md";
                case EngineeringReportKind.HydraulicDesign: return "hydraulic-design-report.md";
                case EngineeringReportKind.Violations: return "violations-report.md";
                case EngineeringReportKind.DecisionRegister: return "engineering-decision-register.md";
                case EngineeringReportKind.ModelAudit: return "model-audit-report.md";
                default: return "comprehensive-engineering-submission.md";
            }
        }
    }
}
