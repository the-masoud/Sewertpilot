using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static NetworkReadResult CreateTestPlanNetwork()
        {
            var network = new NetworkReadResult
            {
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioId = 1,
                    ActiveScenarioLabel = "Base Scenario",
                    PhysicalAlternativeId = 2
                }
            };

            // Structures: Inlet-1 (100, 200), MH-2 (350, 200), Outfall-3 (600, 200)
            network.Structures.Add(new StructureReadDto
            {
                Id = 1,
                Label = "INLET-1",
                RuntimeCategory = "CatchBasin",
                X = 100.0,
                Y = 200.0,
                RimElevation = 120.0,
                InvertElevation = 114.0,
                GroundElevation = 120.0
            });

            network.Structures.Add(new StructureReadDto
            {
                Id = 2,
                Label = "MH-2",
                RuntimeCategory = "Manhole",
                X = 350.0,
                Y = 200.0,
                RimElevation = 116.0,
                InvertElevation = 110.0,
                GroundElevation = 116.0
            });

            network.Structures.Add(new StructureReadDto
            {
                Id = 3,
                Label = "OUTFALL-3",
                RuntimeCategory = "Outfall",
                X = 600.0,
                Y = 200.0,
                RimElevation = 112.0,
                InvertElevation = 105.0,
                GroundElevation = 112.0
            });

            // Conduits: Pipe-1 (1 -> 2), Pipe-2 (2 -> 3)
            network.Conduits.Add(new ConduitReadDto
            {
                Id = 10,
                Label = "PIPE-1",
                StartNodeId = 1,
                StopNodeId = 2,
                Length = 250.0,
                Diameter = 18.0,
                StartInvert = 114.0,
                StopInvert = 110.0,
                Shape = "Circular",
                Material = "Concrete",
                ManningsN = 0.013
            });

            network.Conduits.Add(new ConduitReadDto
            {
                Id = 20,
                Label = "PIPE-2",
                StartNodeId = 2,
                StopNodeId = 3,
                Length = 250.0,
                Diameter = 24.0,
                StartInvert = 110.0,
                StopInvert = 105.0,
                Shape = "Circular",
                Material = "Concrete",
                ManningsN = 0.013
            });

            return network;
        }

        private static StormExecutiveReportContext CreateTestHydraulicContext()
        {
            var junctions = new[]
            {
                new StormJunctionSpecification { NodeId = 1, Label = "INLET-1", RimElevationFeet = 120.0, InvertElevationFeet = 114.0 },
                new StormJunctionSpecification { NodeId = 2, Label = "MH-2", RimElevationFeet = 116.0, InvertElevationFeet = 110.0, JunctionLossCoefficient = 0.25 },
                new StormJunctionSpecification { NodeId = 3, Label = "OUTFALL-3", RimElevationFeet = 112.0, InvertElevationFeet = 105.0, IsOutfall = true, TailwaterElevationFeet = 107.0 }
            };

            var conduits = new[]
            {
                new HydraulicSizingResult
                {
                    ConduitId = "PIPE-1",
                    StartNodeId = 1,
                    StopNodeId = 2,
                    LengthFeet = 250.0,
                    SelectedDiameterInches = 18,
                    DesignFlowCfs = 4.5,
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
                    DesignFlowCfs = 9.0,
                    UpstreamInvertFeet = 110.0,
                    DownstreamInvertFeet = 105.0
                }
            };

            var ver = StormFlowVerificationEngine.VerifyNetwork(junctions, conduits);
            return new StormExecutiveReportContext
            {
                ProjectName = "Capital Drainage P26 UX Validation",
                ProjectId = "PRJ-P26-UX",
                VerificationResult = ver
            };
        }

        private static List<EngineeringDecision> CreateTestDecisions()
        {
            var decision = new EngineeringDecision
            {
                DecisionId = "DEC-P26-001",
                ProjectId = "PRJ-P26-UX",
                Module = "StormFlow",
                Category = EngineeringDecisionCategory.BottleneckCapacityRemediation,
                ProblemStatement = "Capacity remediation for PIPE-1 under 25-year design storm",
                Rationale = "Peak flow requires minimum 18\" barrel to prevent upstream surcharge",
                Status = EngineeringDecisionStatus.ApprovedAndActive,
                CreatedUtc = DateTime.UtcNow.ToString("o")
            };
            decision.SubjectElements.Add("PIPE-1");
            decision.SubjectElements.Add("10");
            decision.SupportingEvidence["PeakFlowCfs"] = "4.50";

            var alt1 = new EngineeringDecisionAlternative { AlternativeId = "ALT-1", Title = "Do nothing (15-inch pipe)", Description = "Maintain 15-inch pipe", IsSelected = false };
            var alt2 = new EngineeringDecisionAlternative { AlternativeId = "ALT-2", Title = "Upsize to 18-inch", Description = "Install 18-inch reinforced concrete pipe", IsSelected = true };
            decision.Alternatives.Add(alt1);
            decision.Alternatives.Add(alt2);

            decision.Resolution = new EngineeringDecisionResolution
            {
                SelectedAlternativeId = "ALT-2",
                EngineeringJustification = "Resolves hydraulic gradient surcharge and satisfies minimum velocity requirements.",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                DecisionMaker = new EngineeringDecisionMaker { Name = "Chief Drainage Engineer", ProfessionalTitle = "PE, CFM", IdentityIdentifier = "ENG-001" },
                ResultingImpact = new EngineeringDecisionImpact { HydraulicImpact = "Surcharge eliminated", PhysicalImpact = "18-inch RCP installed" }
            };

            return new List<EngineeringDecision> { decision };
        }

        private static void P26_NetworkPlanCanvas_InitializationAndDataLoading()
        {
            using (var canvas = new NetworkPlanCanvasControl())
            {
                canvas.Size = new Size(800, 600);
                Check(canvas.StructureCount == 0, "Canvas initializes with 0 structures.");
                Check(canvas.ConduitCount == 0, "Canvas initializes with 0 conduits.");
                Check(canvas.SelectedKind == PlanCanvasElementKind.None, "Initial selection kind is None.");

                var network = CreateTestPlanNetwork();
                canvas.LoadNetwork(network);

                Check(canvas.StructureCount == 3, "Canvas loaded 3 structures.");
                Check(canvas.ConduitCount == 2, "Canvas loaded 2 conduits.");
                Check(!canvas.WorldBounds.IsEmpty, "World bounds computed successfully.");
                Check(canvas.WorldBounds.Width >= 500f, "World bounds width reflects coordinate span.");
                Check(canvas.ZoomFactor > 0.0f, "Zoom factor calculated for fit-to-window.");

                var c1 = canvas.GetConduit(10);
                Check(c1 != null && c1.Label == "PIPE-1", "Conduit 10 retrieved by ID with correct label.");
                Check(c1.DiameterInches == 18.0, "Conduit diameter populated as 18 inches.");
                Check(c1.LengthFeet == 250.0, "Conduit length populated as 250 ft.");

                var s1 = canvas.GetStructure(1);
                Check(s1 != null && s1.Label == "INLET-1", "Structure 1 retrieved by ID.");
                Check(s1.RimElevationFeet == 120.0, "Structure 1 rim elevation matches input.");
                Check(s1.Category == "CatchBasin", "Structure 1 category preserved.");

                var s3 = canvas.GetStructure(3);
                Check(s3 != null && s3.IsOutfall, "Structure 3 recognized as terminal outfall.");
            }
        }

        private static void P26_NetworkPlanCanvas_HitTestingAndSelection()
        {
            using (var canvas = new NetworkPlanCanvasControl())
            {
                canvas.Size = new Size(800, 600);
                var network = CreateTestPlanNetwork();
                canvas.LoadNetwork(network);

                // Test Coordinate Transform
                var s2 = canvas.GetStructure(2);
                var screenPt = canvas.WorldToScreen(s2.Location);
                var worldBack = canvas.ScreenToWorld(new Point((int)screenPt.X, (int)screenPt.Y));
                Check(Math.Abs(worldBack.X - s2.Location.X) < 1.0, "World to Screen and back preserves X coordinate.");
                Check(Math.Abs(worldBack.Y - s2.Location.Y) < 1.0, "World to Screen and back preserves Y coordinate.");

                // Hit test structure 2
                bool hit = canvas.FindElementAt(new Point((int)screenPt.X, (int)screenPt.Y), out var kind, out int id);
                Check(hit && kind == PlanCanvasElementKind.Structure && id == 2, "Hit-testing identifies Structure 2 at its screen location.");

                // Select Structure
                canvas.SelectStructure(2);
                Check(canvas.SelectedKind == PlanCanvasElementKind.Structure, "Selected kind is Structure.");
                Check(canvas.SelectedId == 2, "Selected ID is 2.");

                // Select Conduit
                canvas.SelectConduit(10);
                Check(canvas.SelectedKind == PlanCanvasElementKind.Conduit, "Selected kind is Conduit.");
                Check(canvas.SelectedId == 10, "Selected ID is 10.");

                // Clear Selection
                canvas.ClearSelection();
                Check(canvas.SelectedKind == PlanCanvasElementKind.None, "ClearSelection resets selection to None.");
                Check(canvas.SelectedId == 0, "ClearSelection resets selected ID to 0.");
            }
        }

        private static void P26_NetworkPlanCanvas_UpstreamAndDownstreamTracing()
        {
            using (var canvas = new NetworkPlanCanvasControl())
            {
                canvas.Size = new Size(800, 600);
                var network = CreateTestPlanNetwork();
                canvas.LoadNetwork(network);

                // 1. Trace Upstream from Outfall-3 (ID 3)
                canvas.TraceUpstream(3, isConduit: false);
                Check(canvas.TracedConduitCount == 2, "Upstream trace from outfall includes both conduits.");
                Check(canvas.TracedStructureCount == 3, "Upstream trace includes all 3 structures.");

                // 2. Clear Trace
                canvas.ClearTrace();
                Check(canvas.TracedConduitCount == 0, "ClearTrace resets traced conduits.");
                Check(canvas.TracedStructureCount == 0, "ClearTrace resets traced structures.");

                // 3. Trace Downstream from Inlet-1 (ID 1)
                canvas.TraceDownstream(1, isConduit: false);
                Check(canvas.TracedConduitCount == 2, "Downstream trace from inlet includes all downstream conduits to outfall.");
                Check(canvas.TracedStructureCount == 3, "Downstream trace includes all 3 structures.");

                // 4. Trace Upstream from Pipe-1 (ID 10)
                canvas.ClearTrace();
                canvas.TraceUpstream(10, isConduit: true);
                Check(canvas.TracedConduitCount == 1, "Trace upstream from Pipe-1 includes only Pipe-1.");
                Check(canvas.TracedStructureCount >= 2, "Trace upstream from Pipe-1 includes start and stop nodes.");
            }
        }

        private static void P26_NetworkPlanCanvas_ViolationsOverlayAndHydraulicBinding()
        {
            using (var canvas = new NetworkPlanCanvasControl())
            {
                canvas.Size = new Size(800, 600);
                var network = CreateTestPlanNetwork();
                var hydraulicCtx = CreateTestHydraulicContext();
                var decisions = CreateTestDecisions();

                canvas.LoadNetwork(network, hydraulicCtx, decisions);

                var c1 = canvas.GetConduit(10);
                Check(c1.FlowCfs.HasValue && c1.FlowCfs.Value > 0.0, "Conduit 10 flow bound from hydraulic results.");
                Check(c1.VelocityFps.HasValue && c1.VelocityFps.Value > 0.0, "Conduit 10 velocity bound from hydraulic results.");
                Check(c1.P25DecisionCount == 1, "Conduit 10 linked to 1 P25 engineering decision.");

                var s1 = canvas.GetStructure(1);
                Check(s1.HglFeet.HasValue && s1.HglFeet.Value > 0.0, "Structure 1 HGL bound from hydraulic results.");
                Check(s1.FreeboardFeet.HasValue, "Structure 1 freeboard margin bound.");

                // Violations Toggle
                Check(canvas.ShowViolations, "Violations overlay is enabled by default.");
                canvas.ShowViolations = false;
                Check(!canvas.ShowViolations, "Violations overlay can be toggled off.");
                canvas.ShowViolations = true;
            }
        }

        private static void P26_NetworkElementInspector_PropertiesAndViolationsDisplay()
        {
            using (var inspector = new NetworkElementInspectorControl())
            {
                inspector.Size = new Size(400, 700);
                Check(inspector.CurrentKind == PlanCanvasElementKind.None, "Inspector starts with no selection.");
                Check(inspector.PropertyRowCount == 0, "Initial property grid is empty.");

                var network = CreateTestPlanNetwork();
                var hydraulicCtx = CreateTestHydraulicContext();
                var decisions = CreateTestDecisions();

                using (var canvas = new NetworkPlanCanvasControl())
                {
                    canvas.LoadNetwork(network, hydraulicCtx, decisions);

                    // Inspect Conduit
                    var c1 = canvas.GetConduit(10);
                    inspector.InspectConduit(c1);

                    Check(inspector.CurrentKind == PlanCanvasElementKind.Conduit, "Inspector reports Conduit kind.");
                    Check(inspector.CurrentId == 10, "Inspector reports conduit ID 10.");
                    Check(inspector.CurrentLabel == "PIPE-1", "Inspector reports conduit label PIPE-1.");
                    Check(inspector.PropertyRowCount >= 10, "Inspector populated at least 10 conduit properties.");
                    Check(inspector.TitleText.Contains("CONDUIT: PIPE-1"), "Title banner updated for Conduit PIPE-1.");

                    // Inspect Structure
                    var s2 = canvas.GetStructure(2);
                    inspector.InspectStructure(s2);

                    Check(inspector.CurrentKind == PlanCanvasElementKind.Structure, "Inspector reports Structure kind.");
                    Check(inspector.CurrentId == 2, "Inspector reports structure ID 2.");
                    Check(inspector.CurrentLabel == "MH-2", "Inspector reports structure label MH-2.");
                    Check(inspector.PropertyRowCount >= 8, "Inspector populated at least 8 structure properties.");
                    Check(inspector.TitleText.Contains("STRUCTURE: MH-2"), "Title banner updated for Structure MH-2.");
                }
            }
        }

        private static void P26_NetworkElementInspector_P25DecisionLinkage()
        {
            using (var inspector = new NetworkElementInspectorControl())
            {
                var network = CreateTestPlanNetwork();
                var hydraulicCtx = CreateTestHydraulicContext();
                var decisions = CreateTestDecisions();

                inspector.BindDecisions(decisions);

                using (var canvas = new NetworkPlanCanvasControl())
                {
                    canvas.LoadNetwork(network, hydraulicCtx, decisions);
                    var c1 = canvas.GetConduit(10);
                    inspector.InspectConduit(c1);

                    Check(inspector.DecisionItemCount > 0, "Inspector lists associated P25 decision.");
                }
            }
        }

        private static void P26_BidirectionalSelectionSynchronization_WorkspaceIntegration()
        {
            using (var form = new ProjectWorkspaceForm(GravityOperatingMode.SingleOperator))
            {
                Check(form.PlanCanvas != null, "PlanCanvas initialized in ProjectWorkspaceForm.");
                Check(form.ElementInspector != null, "ElementInspector initialized in ProjectWorkspaceForm.");

                // Check tabs
                var tabs = form.Controls.Find("WorkspaceTabs", true).FirstOrDefault() as TabControl;
                Check(tabs != null, "WorkspaceTabs found.");
                Check(tabs.TabPages[0].Text == "Network Plan & Inspector", "Network Plan & Inspector is first tab per Section 20 canvas-centric UX.");

                var network = CreateTestPlanNetwork();
                form.ShowNetwork(network);

                Check(form.PlanCanvas.ConduitCount == 2, "Plan canvas received 2 conduits from ShowNetwork.");
                Check(form.PlanCanvas.StructureCount == 3, "Plan canvas received 3 structures from ShowNetwork.");

                // 1. Select Conduit on PlanCanvas -> triggers Inspector, DataGridView and Profile synchronization
                form.PlanCanvas.SelectConduit(10);

                Check(form.ElementInspector.CurrentKind == PlanCanvasElementKind.Conduit, "Selection on PlanCanvas synchronized to ElementInspector.");
                Check(form.ElementInspector.CurrentId == 10, "ElementInspector displays Conduit 10.");

                // 2. Select Structure on PlanCanvas
                form.PlanCanvas.SelectStructure(1);
                Check(form.ElementInspector.CurrentKind == PlanCanvasElementKind.Structure, "Structure selection on PlanCanvas synchronized to ElementInspector.");
                Check(form.ElementInspector.CurrentId == 1, "ElementInspector displays Structure 1.");
            }
        }

        private static void P26_NetworkPlanCanvas_OffscreenRendering()
        {
            using (var canvas = new NetworkPlanCanvasControl())
            {
                var network = CreateTestPlanNetwork();
                var hydraulicCtx = CreateTestHydraulicContext();
                canvas.LoadNetwork(network, hydraulicCtx);

                using (var bmp = canvas.RenderToBitmap(1024, 768))
                {
                    Check(bmp != null, "Offscreen render returned non-null bitmap.");
                    Check(bmp.Width == 1024 && bmp.Height == 768, "Bitmap dimensions match requested 1024x768.");
                    Check(bmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb, "Bitmap pixel format is 32bpp ARGB.");
                }
            }
        }

        internal static void RunP26CanvasTests()
        {
            Console.WriteLine("[Phase P26] Executing Canvas / Inspector / Profile UX Suite...");
            P26_NetworkPlanCanvas_InitializationAndDataLoading();
            P26_NetworkPlanCanvas_HitTestingAndSelection();
            P26_NetworkPlanCanvas_UpstreamAndDownstreamTracing();
            P26_NetworkPlanCanvas_ViolationsOverlayAndHydraulicBinding();
            P26_NetworkElementInspector_PropertiesAndViolationsDisplay();
            P26_NetworkElementInspector_P25DecisionLinkage();
            P26_BidirectionalSelectionSynchronization_WorkspaceIntegration();
            P26_NetworkPlanCanvas_OffscreenRendering();
            Console.WriteLine("[Phase P26] All Canvas / Inspector / Profile UX Tests PASSED.");
        }
    }
}
