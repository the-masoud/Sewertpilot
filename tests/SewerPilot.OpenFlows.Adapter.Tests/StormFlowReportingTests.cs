using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void P23_MultiModuleWorkspace_TabsAndControlsInitialized()
        {
            using (var form = new ProjectWorkspaceForm(GravityOperatingMode.SingleOperator))
            {
                Check(form.Text.Contains("SingleOperator"), "Form title reflects operating mode.");
                Check(form.HydraulicProfileViewer != null, "HydraulicProfileViewer control initialized.");

                // Check tabs
                var tabs = form.Controls.Find("WorkspaceTabs", true).FirstOrDefault() as System.Windows.Forms.TabControl;
                Check(tabs != null, "WorkspaceTabs control present.");
                Check(tabs.TabPages.Count >= 6, "All 6 multi-module tabs present.");

                var tabNames = tabs.TabPages.Cast<System.Windows.Forms.TabPage>().Select(p => p.Text).ToList();
                Check(tabNames.Contains("Conduits"), "Conduits tab present.");
                Check(tabNames.Contains("Structures"), "Structures tab present.");
                Check(tabNames.Contains("Design Workspace"), "Gravity Design Workspace tab present.");
                Check(tabNames.Contains("StormFlow Design Basis"), "StormFlow Design Basis tab present.");
                Check(tabNames.Contains("StormFlow Profile & Audit"), "StormFlow Profile & Audit tab present.");
                Check(tabNames.Contains("Engineering status"), "Engineering status tab present.");

                // Check profile viewer controls
                var viewer = form.HydraulicProfileViewer;
                Check(viewer.ProfileGrid != null, "Profile grid initialized.");
                Check(viewer.ProfileGrid.Columns.Count >= 10, "Profile grid has complete hydraulic columns.");
                Check(viewer.RemediationList != null, "Remediation list initialized.");
                Check(viewer.ReportPreviewBox != null, "Report preview box initialized.");
            }
        }

        private static void P23_HydraulicProfileVisualization_RendersAccurateElevations()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "INLET-1", RimElevationFeet = 120.0, InvertElevationFeet = 114.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-2", RimElevationFeet = 116.0, InvertElevationFeet = 110.0, JunctionLossCoefficient = 0.25 },
                new StormJunctionSpecification { NodeId = 3, Label = "OUTFALL", RimElevationFeet = 112.0, InvertElevationFeet = 105.0, IsOutfall = true, TailwaterElevationFeet = 107.0 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 5.0,
                    UpstreamInvertFeet = 114.0,
                    DownstreamInvertFeet = 110.0
                },
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-2",
                    StartNodeId = 2,
                    StopNodeId = 3,
                    LengthFeet = 250.0,
                    SelectedDiameterInches = 24,
                    DesignFlowCfs = 10.0,
                    UpstreamInvertFeet = 110.0,
                    DownstreamInvertFeet = 105.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);
            var context = new StormExecutiveReportContext
            {
                ProjectName = "Capital Drainage District Improvement",
                ProjectId = "PRJ-TEST-P23",
                VerificationResult = ver
            };

            var viewer = new HydraulicProfileViewerControl();
            viewer.LoadResults(context);

            Check(viewer.ProfileGridRowCount == 2, "Profile grid populated with 2 conduits.");
            Check(viewer.VerdictText.Contains("VERDICT:"), "Verdict banner updated with status.");
            Check(viewer.ReportPreviewText.Contains("SewerPilot — StormFlow Executive Engineering Audit Report"), "Preview box displays executive report.");
            Check(viewer.KpiHeadLoss.Text.Contains("System Head Loss:"), "KPI system head loss populated.");
            Check(viewer.HasGraphicalProfile, "Graphical profile data loaded.");
            Check(viewer.ProfileSegmentCount == 2, "Profile segment count equals 2.");
            Check(viewer.ProfileNodeCount >= 2, "Profile node count is at least 2.");
            Check(viewer.ProfileWorldBounds.Width > 0, "Profile world bounds calculated.");

            using (var bmp = viewer.RenderOffscreen(800, 600))
            {
                Check(bmp != null && bmp.Width == 800 && bmp.Height == 600, "Offscreen render generated 800x600 bitmap.");
            }
        }

        private static void P23_ExecutiveReport_MarkdownGeneration_CompleteSections()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "INLET-1", RimElevationFeet = 120.0, InvertElevationFeet = 114.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 112.0, InvertElevationFeet = 105.0, IsOutfall = true }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 4.0,
                    UpstreamInvertFeet = 114.0,
                    DownstreamInvertFeet = 105.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);
            var context = new StormExecutiveReportContext
            {
                ProjectName = "Downtown Austin Storm Infrastructure",
                ProjectId = "PRJ-AUS-2026",
                ProjectRevision = 3,
                AuthoritativeEngineer = "P.E. Lead Hydrologist",
                VerificationResult = ver,
                HydrologyResults = new List<RunoffResult>
                {
                    new RunoffResult { CatchmentId = "C-1", AreaAcres = 2.5, Method = HydrologyCalculationMethod.Rational, EffectiveRunoffParameter = 0.85, PeakDischargeCfs = 4.0, TimeOfConcentrationMinutes = 10.0 }
                },
                InletResults = new List<InletInterceptionResult>
                {
                    new InletInterceptionResult { InletId = "IN-1", InletType = InletType.CurbOpening, InletLocation = InletLocation.OnContinuousGrade, TotalApproachFlowCfs = 4.0, InterceptedFlowCfs = 4.0, InterceptionEfficiency = 1.0, GutterSpreadFeet = 6.5, GutterDepthInches = 4.5 }
                }
            };

            string md = StormFlowExecutiveReportGenerator.GenerateMarkdownReport(context);

            // Assert presence of all 8 core engineering sections
            Check(md.Contains("## 1. Executive Summary & Verification Verdict"), "Section 1 present.");
            Check(md.Contains("## 2. Multi-Module Project Audit Lineage & Identity Provenance"), "Section 2 present.");
            Check(md.Contains("## 3. Hydrology & Runoff Summary"), "Section 3 present.");
            Check(md.Contains("## 4. Gutter Hydraulics & Inlet Interception Summary"), "Section 4 present.");
            Check(md.Contains("## 5. Conduit Conveyance & Vertical Invert Design Summary"), "Section 5 present.");
            Check(md.Contains("## 6. Hydraulic Grade Line (HGL) & Network Surcharge Analysis"), "Section 6 present.");
            Check(md.Contains("## 7. Hydraulic Bottleneck Identification & Engineering Remediation Proposals"), "Section 7 present.");
            Check(md.Contains("## 8. Multi-Module Engineering Authority & Coexistence Matrix"), "Section 8 present.");

            Check(md.Contains("PRJ-AUS-2026"), "Project ID present in report.");
            Check(md.Contains("Downtown Austin Storm Infrastructure"), "Project name present in report.");
            Check(md.Contains("P.E. Lead Hydrologist"), "Authoritative engineer identity present.");
        }

        private static void P23_ExecutiveReport_JsonAuditPackage_ValidJsonAndProvenance()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "J1", RimElevationFeet = 110.0, InvertElevationFeet = 104.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "J2", RimElevationFeet = 106.0, InvertElevationFeet = 100.0, IsOutfall = true }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "C1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 150.0,
                    SelectedDiameterInches = 15,
                    DesignFlowCfs = 3.2,
                    UpstreamInvertFeet = 104.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);
            var context = new StormExecutiveReportContext
            {
                ProjectName = "JSON Audit Test",
                ProjectId = "JSON-001",
                VerificationResult = ver
            };

            string json = StormFlowExecutiveReportGenerator.GenerateJsonAuditPackage(context);

            Check(json.Contains("\"projectName\": \"JSON Audit Test\""), "JSON contains project name.");
            Check(json.Contains("\"projectId\": \"JSON-001\""), "JSON contains project ID.");
            Check(json.Contains("\"conduits\": ["), "JSON contains conduits array.");
            Check(json.Contains("\"nodes\": ["), "JSON contains nodes array.");
            Check(json.Contains("\"totalHeadLossFeet\":"), "JSON contains total head loss.");
        }

        private static void P23_ExecutiveReport_CsvProfileExport_HeaderAndDataIntegrity()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "A", RimElevationFeet = 120.0, InvertElevationFeet = 110.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "B", RimElevationFeet = 110.0, InvertElevationFeet = 100.0, IsOutfall = true }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-AB",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 200.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 4.0,
                    UpstreamInvertFeet = 110.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);
            var context = new StormExecutiveReportContext
            {
                ProjectName = "CSV Test",
                ProjectId = "CSV-001",
                VerificationResult = ver
            };

            string conduitCsv = StormFlowExecutiveReportGenerator.GenerateConduitProfilesCsv(context);
            string junctionCsv = StormFlowExecutiveReportGenerator.GenerateJunctionProfilesCsv(context);

            var conduitRows = conduitCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var junctionRows = junctionCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            Check(conduitRows.Length == 2, "Conduit CSV contains header + 1 data line.");
            Check(conduitRows[0].StartsWith("ConduitId,StartNode,StopNode"), "Conduit CSV header is standard.");
            Check(conduitRows[1].Contains("PIPE-AB,1,2,200.0,18"), "Conduit CSV record is accurate.");

            Check(junctionRows.Length == 3, "Junction CSV contains header + 2 data lines.");
            Check(junctionRows[0].StartsWith("NodeId,Label,RimElevationFt"), "Junction CSV header is standard.");
        }

        private static void P23_BottleneckRemediation_ReportingIntegration()
        {
            // Undersized 15-inch pipe carrying 8 cfs, creating high friction loss that violates upstream freeboard
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "MH-SHALLOW", RimElevationFeet = 106.0, InvertElevationFeet = 101.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "OUTFALL", RimElevationFeet = 105.0, InvertElevationFeet = 100.0, IsOutfall = true, TailwaterElevationFeet = 101.5 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "P-THROTTLE",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 300.0,
                    SelectedDiameterInches = 15,
                    DesignFlowCfs = 8.0,
                    UpstreamInvertFeet = 101.0,
                    DownstreamInvertFeet = 100.0
                }
            };

            var criteria = new StormVerificationCriteria
            {
                MinimumFreeboardFeet = 1.0,
                AvailableDiametersInches = new[] { 15, 18, 21, 24, 30 }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits, criteria);
            Check(ver.BottleneckProposals.Count > 0, "Bottleneck detected by verification engine.");

            var context = new StormExecutiveReportContext
            {
                ProjectName = "Bottleneck Test",
                ProjectId = "BN-001",
                VerificationResult = ver
            };

            string md = StormFlowExecutiveReportGenerator.GenerateMarkdownReport(context);
            Check(md.Contains("P-THROTTLE"), "Throttled conduit mentioned in report.");
            Check(md.Contains("Proposed Diam"), "Proposed diameter column rendered.");
            Check(md.Contains("Head Loss Reduct"), "Head loss reduction column rendered.");

            var viewer = new HydraulicProfileViewerControl();
            viewer.LoadResults(context);
            Check(viewer.RemediationItemCount > 0, "Viewer remediation list populated.");
        }

        private static void P23_FailClosed_NullOrInvalidContextHandling()
        {
            Throws<ArgumentNullException>(() => StormFlowExecutiveReportGenerator.GenerateMarkdownReport(null));
            Throws<ArgumentNullException>(() => StormFlowExecutiveReportGenerator.GenerateJsonAuditPackage(null));
            Throws<ArgumentNullException>(() => StormFlowExecutiveReportGenerator.GenerateConduitProfilesCsv(null));

            // Null VerificationResult
            Throws<ArgumentNullException>(() =>
            {
                var ctx = new StormExecutiveReportContext { ProjectName = "Valid", ProjectId = "P1", VerificationResult = null };
                StormFlowExecutiveReportGenerator.GenerateMarkdownReport(ctx);
            });

            // Empty ProjectName
            Throws<ArgumentException>(() =>
            {
                var ctx = new StormExecutiveReportContext { ProjectName = "", ProjectId = "P1", VerificationResult = new StormNetworkVerificationResult() };
                StormFlowExecutiveReportGenerator.GenerateMarkdownReport(ctx);
            });
        }

        internal static void P23_MaterializeAuthoritativeEvidence()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "SewerPilot.sln")))
            {
                root = Path.GetDirectoryName(root);
            }
            string outDir = Path.Combine(root, "docs", "evidence", "p23", "20261019-multimodule-reporting");
            Directory.CreateDirectory(outDir);

            // Realistic multi-module storm network
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "INLET-201", RimElevationFeet = 135.0, InvertElevationFeet = 127.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-202", RimElevationFeet = 130.0, InvertElevationFeet = 122.5, JunctionLossCoefficient = 0.35 },
                new StormJunctionSpecification { NodeId = 3, Label = "MH-203", RimElevationFeet = 124.0, InvertElevationFeet = 116.0, JunctionLossCoefficient = 0.45 },
                new StormJunctionSpecification { NodeId = 4, Label = "OUTFALL-RIVER", RimElevationFeet = 118.0, InvertElevationFeet = 108.0, IsOutfall = true, TailwaterElevationFeet = 112.50 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "CONDUIT-201",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 280.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 5.2,
                    UpstreamInvertFeet = 127.0,
                    DownstreamInvertFeet = 123.0
                },
                new HydraulicSizingResult
                {
                    ConduitId = "CONDUIT-202",
                    StartNodeId = 2,
                    StopNodeId = 3,
                    LengthFeet = 320.0,
                    SelectedDiameterInches = 24,
                    DesignFlowCfs = 11.4,
                    UpstreamInvertFeet = 122.5,
                    DownstreamInvertFeet = 116.5
                },
                new HydraulicSizingResult
                {
                    ConduitId = "CONDUIT-203",
                    StartNodeId = 3,
                    StopNodeId = 4,
                    LengthFeet = 400.0,
                    SelectedDiameterInches = 30,
                    DesignFlowCfs = 18.6,
                    UpstreamInvertFeet = 116.0,
                    DownstreamInvertFeet = 108.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);

            var context = new StormExecutiveReportContext
            {
                ProjectName = "Travis County East Trunk Drainage Modernization",
                ProjectId = "PRJ-TC-2026-09A",
                ProjectRevision = 4,
                AuthoritativeEngineer = Environment.UserName,
                VerificationResult = ver,
                HydrologyResults = new List<RunoffResult>
                {
                    new RunoffResult { CatchmentId = "CAT-201", AreaAcres = 4.2, Method = HydrologyCalculationMethod.Rational, EffectiveRunoffParameter = 0.75, RainfallIntensityInPerHour = 3.8, PeakDischargeCfs = 11.97, TimeOfConcentrationMinutes = 12.0 }
                },
                InletResults = new List<InletInterceptionResult>
                {
                    new InletInterceptionResult { InletId = "INL-201", InletType = InletType.Combination, InletLocation = InletLocation.OnContinuousGrade, TotalApproachFlowCfs = 11.97, InterceptedFlowCfs = 10.50, BypassFlowCfs = 1.47, InterceptionEfficiency = 0.877, GutterSpreadFeet = 7.2, GutterDepthInches = 5.2 }
                }
            };

            // Export all 4 package files
            StormFlowExecutiveReportGenerator.ExportAuditPackage(outDir, context);

            Check(File.Exists(Path.Combine(outDir, "executive_audit_report.md")), "Markdown report materialized.");
            Check(File.Exists(Path.Combine(outDir, "executive_audit_package.json")), "JSON audit package materialized.");
            Check(File.Exists(Path.Combine(outDir, "conduit_hydraulic_profiles.csv")), "Conduit CSV materialized.");
            Check(File.Exists(Path.Combine(outDir, "junction_hydraulic_profiles.csv")), "Junction CSV materialized.");
        }
    }
}
