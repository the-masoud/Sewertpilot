using System;
using System.IO;
using System.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    internal static partial class Program
    {
        private static void Remediation_LandXmlGeometryAndMissingInput()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SewerPilot.sln"))) dir = dir.Parent;
            string xml = File.ReadAllText(Path.Combine(dir.FullName, "docs/evidence/landxml/20261019-phase2-landxml-exchange/sample-civil3d-network.xml"));
            var read = LandXmlPipeNetworkAdapter.ReadLandXml(xml, new LandXmlImportOptions { DefaultManningsN = double.NaN });
            var inputs = new LandXmlPipelineHydraulics();
            inputs.OutfallTypes.Add("OF-103", "FREE");
            inputs.InflowsCfs.Add("MH-101", 1.0); // Explicit test loading, not a project criterion.
            var converted = ProductionPipelineOrchestrator.ConvertNetworkReadResultToSwmmInp(read, "unit-regression", inputs);
            Check(Math.Abs(converted.Conduits["PIPE-201"].Geom1 - 1.5) < 1e-12, "18 inches normalizes once to 1.5 feet");
            Check(Math.Abs(converted.Conduits["PIPE-202"].Geom1 - 2.0) < 1e-12, "24 inches normalizes once to 2 feet");
            var roundTrip = SwmmInpNetworkAdapter.ParseInpText(SwmmInpNetworkAdapter.ExportToInpText(converted));
            Check(ProductionPipelineOrchestrator.MeasureConversionDrift(converted, roundTrip) < 1e-6, "Geometry, topology and loading survive export");
            roundTrip.Conduits["PIPE-202"].Geom1 /= 12;
            Check(ProductionPipelineOrchestrator.MeasureConversionDrift(converted, roundTrip) > 1, "Fidelity check detects diameter corruption");
            bool rejected = false;
            try { ProductionPipelineOrchestrator.ConvertNetworkReadResultToSwmmInp(read, "missing-input", null); }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains("ENGINEERING_INPUT_REQUIRED"); }
            Check(rejected, "Geometry alone cannot invent loading or outfall boundary");
            var feetDeclared = LandXmlPipeNetworkAdapter.ReadLandXml(xml.Replace("diameterUnit=\"inch\"", "diameterUnit=\"foot\"")
                .Replace("diameter=\"18.00\"", "diameter=\"12.00\""));
            Check(feetDeclared.Conduits.Single(c => c.Label == "PIPE-201").Diameter == 12.0,
                "An explicitly declared 12-foot diameter cannot be guessed to be inches");
            var metersDeclared = LandXmlPipeNetworkAdapter.ReadLandXml(xml.Replace("<Imperial ", "<Metric ")
                .Replace("linearUnit=\"foot\"", "linearUnit=\"meter\"").Replace("diameterUnit=\"inch\"", "diameterUnit=\"meter\"")
                .Replace("diameter=\"18.00\"", "diameter=\"60.00\""));
            Check(metersDeclared.Conduits.Single(c => c.Label == "PIPE-201").Diameter == 60.0,
                "An explicitly declared 60-metre diameter cannot be guessed to be millimetres");
            var millimetersDeclared = LandXmlPipeNetworkAdapter.ReadLandXml(xml.Replace("<Imperial ", "<Metric ")
                .Replace("linearUnit=\"foot\"", "linearUnit=\"meter\"").Replace("diameterUnit=\"inch\"", "diameterUnit=\"millimeter\"")
                .Replace("diameter=\"18.00\"", "diameter=\"1200.00\""));
            Check(Math.Abs(millimetersDeclared.Conduits.Single(c => c.Label == "PIPE-201").Diameter.Value - 1.2) < 1e-12,
                "Explicit millimetres normalize once to metres, including full unit spelling");
        }

        private static void Remediation_UnsupportedNodeCannotClaimHgl()
        {
            var result = new WaterObjectsResultReader().ReadNodeHgl(null, null, 81, "fixture", 123, "UnknownStructure");
            Check(result.Status == "NODE_CATEGORY_UNSUPPORTED" && result.ElementId == 123, "Unsupported node preserves requested identity and unavailable result");
        }

        private static void Remediation_NativeControlFlowIsNotDropped()
        {
            var result = new SwmmExecutionResult();
            SwmmSolverRunner.ParseReportText("Link Flow Summary\n-------------\nORF-1 ORIFICE 12.00 0 00:21\n\n", result);
            Check(result.Links.ContainsKey("ORF-1") && result.Links["ORF-1"].MaximumFlowCfs == 12,
                "Five-column native control rows retain measured flow");
            Check(double.IsNaN(result.Links["ORF-1"].MaximumVelocityFps), "Absent control velocity cannot become a calculated zero");
            bool rejected = false;
            try { SwmmSolverRunner.ParseReportText("Link Flow Summary\n-------------\nORF-1 ORIFICE broken 0 00:21\n\n", new SwmmExecutionResult()); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed native numbers cannot become zero parity references");
        }

        private static void Remediation_SwmmControlSemanticsAndLosslessPrecision()
        {
            var model = new SwmmModel();
            model.Junctions["J-1"] = new SwmmJunctionDto { Name = "J-1", Elevation = 100.0, MaxDepth = 8.0 };
            model.Junctions["J-2"] = new SwmmJunctionDto { Name = "J-2", Elevation = 95.0, MaxDepth = 8.0 };
            model.Pumps["P-1"] = new SwmmPumpDto { Name = "P-1", FromNode = "J-1", ToNode = "J-2", PumpCurve = "CRV-1", Status = "ON" };
            model.Orifices["O-1"] = new SwmmOrificeDto { Name = "O-1", FromNode = "J-1", ToNode = "J-2", OrificeType = "SIDE", Offset = 0.5, DischargeCoeff = 0.65 };
            model.Inflows["J-1"] = 0.00004;

            string exported = SwmmInpNetworkAdapter.ExportToInpText(model);
            Check(exported.Contains("[PUMPS]") && exported.Contains("P-1"), "Authentic [PUMPS] exported without conduit flattening.");
            Check(exported.Contains("[ORIFICES]") && exported.Contains("O-1"), "Authentic [ORIFICES] exported without conduit flattening.");
            Check(exported.Contains("0.000040") || exported.Contains("0.00004"), "Small non-zero flow 0.00004 CFS survives export without zero truncation.");
            Check(!exported.Contains("[CONDUITS]\nP-1") && !exported.Contains("[CONDUITS]\n;;Name FromNode\nP-1"), "Pumps are not flattened to [CONDUITS].");

            var reimported = SwmmInpNetworkAdapter.ParseInpText(exported);
            Check(reimported.Pumps.ContainsKey("P-1"), "P-1 reimported as pump.");
            Check(reimported.Orifices.ContainsKey("O-1"), "O-1 reimported as orifice.");
            Check(Math.Abs(reimported.Inflows["J-1"] - 0.00004) < 1e-9, "Small flow 0.00004 CFS survives roundtrip parsing.");
        }
    }
}
