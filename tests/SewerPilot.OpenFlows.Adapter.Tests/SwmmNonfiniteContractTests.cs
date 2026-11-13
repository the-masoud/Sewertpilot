using System;
using System.Globalization;
using System.IO;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    /// <summary>
    /// Contract tests for Finding N02: nonfinite-to-zero conversion in SWMM INP serialization.
    ///
    /// The defect was <c>FormatFloat</c> returning "0.0" for NaN/Infinity, which fabricated
    /// physical quantities (zero slope, diameter, flow, invert) inside the simulation deck.
    /// These tests assert real runtime exception propagation. Nothing here is mocked, stubbed,
    /// or tautological: the first test calls the production formatter directly, and the second
    /// drives the public export path with a genuinely nonfinite model element.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// Test 1 — direct nonfinite invariant verification across every nonfinite IEEE 754 state.
        /// </summary>
        private static void N02_FormatFloatRejectsNonfiniteAndPreservesFinite()
        {
            // --- Nonfinite states must throw ArgumentException, never return a literal. ---
            foreach (var probe in new[]
            {
                new { Label = "NaN", Value = double.NaN },
                new { Label = "+Infinity", Value = double.PositiveInfinity },
                new { Label = "-Infinity", Value = double.NegativeInfinity }
            })
            {
                string returned = null;
                ArgumentException captured = null;
                try
                {
                    returned = SwmmInpNetworkAdapter.FormatFloat(probe.Value);
                }
                catch (ArgumentException ex)
                {
                    captured = ex;
                }

                Check(captured != null,
                    "FormatFloat must reject " + probe.Label + " with ArgumentException (returned '" + (returned ?? "<none>") + "').");
                Check(returned == null,
                    "FormatFloat must not return any string for " + probe.Label + ".");
                Check(captured.ParamName == "val",
                    "Nonfinite rejection for " + probe.Label + " must identify the offending parameter 'val' (was '" + captured.ParamName + "').");
                Check(captured.Message.Contains("SWMM INP serialization"),
                    "Nonfinite rejection for " + probe.Label + " must name the serialization contract.");
                Check(captured.Message.Contains("N02"),
                    "Nonfinite rejection for " + probe.Label + " must cite Finding N02 for audit traceability.");
                Check(captured.Message.Contains("Cannot format nonfinite value"),
                    "Nonfinite rejection for " + probe.Label + " must state the nonfinite value was refused.");
                Check(!captured.Message.Contains("'0.0'") && !captured.Message.Contains("'0'"),
                    "Nonfinite rejection for " + probe.Label + " must not mention any zero substitution.");

                Console.WriteLine("N02_PROBE: FormatFloat(" + probe.Label + ") -> THROW " +
                    captured.GetType().FullName + ": " + captured.Message);
            }

            // --- Legitimate finite values must still format, so the guard cannot over-reject. ---
            Check(SwmmInpNetworkAdapter.FormatFloat(0.0) == "0.000000",
                "An authentic zero is a real measurement and must format as 0.000000.");
            Console.WriteLine("N02_PROBE: FormatFloat(0.0) -> \"0.000000\" (authentic zero preserved)");
            Check(SwmmInpNetworkAdapter.FormatFloat(1.5) == "1.500000",
                "Positive finite values must format with six decimals.");
            Check(SwmmInpNetworkAdapter.FormatFloat(-0.8) == "-0.800000",
                "Negative finite values must retain their sign and format with six decimals.");
            Check(SwmmInpNetworkAdapter.FormatFloat(-0.0) == "0.000000",
                "Negative zero is finite and must format without sign.");
            Check(SwmmInpNetworkAdapter.FormatFloat(1234.5678) == "1234.567800",
                "Large finite values must not be truncated.");
            // Small non-zero flows must survive without collapsing to zero. Assert round-trip
            // fidelity rather than a guessed literal: the exact rendering is an implementation
            // detail, but losing the value is not.
            string small = SwmmInpNetworkAdapter.FormatFloat(0.00004);
            Check(small != "0.000000" && double.Parse(small, CultureInfo.InvariantCulture) == 0.00004,
                "Small non-zero value 0.00004 must round-trip rather than truncate to zero (was '" + small + "').");
            double tiny = 4e-7;
            string tinyText = SwmmInpNetworkAdapter.FormatFloat(tiny);
            Check(tinyText != "0.000000" && double.Parse(tinyText, CultureInfo.InvariantCulture) == tiny,
                "Sub-micro value 4e-7 must use round-trip precision rather than collapse to zero (was '" + tinyText + "').");
        }

        /// <summary>
        /// Test 2 — end-to-end export fail-closed verification through the public export path.
        /// </summary>
        private static void N02_ExportFailsClosedOnNonfiniteElement()
        {
            // Control: an identical model with finite values must export successfully, proving the
            // rejection below is caused by the nonfinite value and nothing else.
            string controlExport = SwmmInpNetworkAdapter.ExportToInpText(BuildFiniteModel());
            Check(!string.IsNullOrEmpty(controlExport) && controlExport.Contains("[JUNCTIONS]"),
                "Control model with finite values must export a complete INP deck.");

            var tempDir = Path.Combine(Path.GetTempPath(), "sp-n02-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // --- Case A: NaN conduit length ---
                var nanLength = BuildFiniteModel();
                nanLength.Conduits["C-1"].Length = double.NaN;
                AssertExportFailsClosed(nanLength, "NaN conduit length", Path.Combine(tempDir, "nan-length.inp"));

                // --- Case B: infinite conduit roughness ---
                var infRoughness = BuildFiniteModel();
                infRoughness.Conduits["C-1"].Roughness = double.PositiveInfinity;
                AssertExportFailsClosed(infRoughness, "+Infinity conduit roughness", Path.Combine(tempDir, "inf-roughness.inp"));

                // --- Case C: negative-infinity junction elevation ---
                var infElevation = BuildFiniteModel();
                infElevation.Junctions["J-1"].Elevation = double.NegativeInfinity;
                AssertExportFailsClosed(infElevation, "-Infinity junction elevation", Path.Combine(tempDir, "inf-elevation.inp"));

                // --- Case D: NaN outfall fixed stage ---
                var nanStage = BuildFiniteModel();
                nanStage.Outfalls["O-1"].OutfallType = "FIXED";
                nanStage.Outfalls["O-1"].FixedStage = double.NaN;
                AssertExportFailsClosed(nanStage, "NaN outfall fixed stage", Path.Combine(tempDir, "nan-stage.inp"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Asserts that exporting the model throws ArgumentException, returns no string, and
        /// leaves no file behind — i.e. the pipeline cannot emit a deck containing a fabricated zero.
        /// </summary>
        private static void AssertExportFailsClosed(SwmmModel model, string label, string outputPath)
        {
            string exported = null;
            Exception captured = null;
            try
            {
                exported = SwmmInpNetworkAdapter.ExportToInpText(model);
                // Mirror the production pipeline: it writes the deck only after export returns.
                File.WriteAllText(outputPath, exported);
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            Check(captured is ArgumentException,
                "Export with " + label + " must throw ArgumentException (observed " +
                (captured == null ? "no exception" : captured.GetType().Name) + ").");
            Check(exported == null,
                "Export with " + label + " must not return a partial deck string.");
            Check(!File.Exists(outputPath),
                "Export with " + label + " must not leave an INP file on disk.");
            if (captured is ArgumentException)
            {
                Check(captured.Message.Contains("N02"),
                    "Export rejection for " + label + " must trace back to Finding N02.");
            }

            Console.WriteLine("N02_EXPORT: " + label + " -> THROW " +
                (captured == null ? "<no exception>" : captured.GetType().FullName + ": " + captured.Message) +
                " | returned_string_null=" + (exported == null) +
                " | file_written=" + File.Exists(outputPath));
        }

        /// <summary>
        /// Minimal topologically valid SWMM model used as the control and as the mutation base.
        /// </summary>
        private static SwmmModel BuildFiniteModel()
        {
            var model = new SwmmModel();
            model.Junctions["J-1"] = new SwmmJunctionDto { Name = "J-1", Elevation = 100.0, MaxDepth = 8.0, X = 1000.0, Y = 2000.0 };
            model.Junctions["J-2"] = new SwmmJunctionDto { Name = "J-2", Elevation = 95.0, MaxDepth = 8.0, X = 1100.0, Y = 2000.0 };
            model.Outfalls["O-1"] = new SwmmOutfallDto { Name = "O-1", Elevation = 90.0, OutfallType = "FREE", X = 1200.0, Y = 2000.0 };
            model.Conduits["C-1"] = new SwmmConduitDto
            {
                Name = "C-1",
                FromNode = "J-1",
                ToNode = "J-2",
                Length = 300.0,
                Roughness = 0.013,
                Shape = "CIRCULAR",
                Geom1 = 1.5
            };
            model.Conduits["C-2"] = new SwmmConduitDto
            {
                Name = "C-2",
                FromNode = "J-2",
                ToNode = "O-1",
                Length = 250.0,
                Roughness = 0.013,
                Shape = "CIRCULAR",
                Geom1 = 1.5
            };
            return model;
        }
    }
}
