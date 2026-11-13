using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SewerPilot.OpenFlows.Adapter;

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    /// <summary>
    /// Contract tests for Findings N07 and N06.
    ///
    /// N07 was a unit-label substitution bug: an imperial network exported to a metric document wrote
    /// 1000 ft as 1000 m and turned a 1.5 ft pipe into 1500 mm. N06 was the companion bypass: NaN and
    /// Infinity slipped past nullable/range checks and serialized into XML attributes.
    ///
    /// These tests drive the real export path with a read model that carries unit provenance, then
    /// assert exact physical values. No mocks, no stubs, no tolerance-widening.
    /// </summary>
    internal static partial class Program
    {
        private static readonly SourceUnitIdentity FeetUnit =
            new SourceUnitIdentity("length", "Feet", "feet", "ft");

        private static readonly SourceUnitIdentity InchUnit =
            new SourceUnitIdentity("diameter", "Inches", "inches", "in");

        private static readonly SourceUnitIdentity MeterUnit =
            new SourceUnitIdentity("length", "Meters", "meter", "m");

        private static readonly SourceUnitIdentity MillimeterUnit =
            new SourceUnitIdentity("diameter", "Millimeters", "millimeter", "mm");

        /// <summary>
        /// Test 1 — imperial source exported to a metric document must apply exact physical scaling.
        /// </summary>
        private static void LandXml_MetricExport_ConvertsImperialDimensionsWithExactScaling()
        {
            var model = BuildFeetNetwork();
            string xml = LandXmlPipeNetworkAdapter.ExportLandXml(model, new LandXmlExportOptions
            {
                NetworkName = "N07-Metric-Export",
                UnitSystem = LandXmlUnitSystem.Metric
            });

            Check(!string.IsNullOrWhiteSpace(xml), "Metric export must produce a document.");
            var doc = XDocument.Parse(xml);

            // <Units> must declare the metric regime, not merely be labelled as such.
            var units = doc.Descendants().First(e => e.Name.LocalName == "Units");
            var metricUnits = units.Elements().FirstOrDefault(e => e.Name.LocalName == "Metric");
            Check(metricUnits != null, "Export must declare <Metric> units.");
            Check((string)metricUnits.Attribute("linearUnit") == "meter", "Metric linear unit must be 'meter'.");
            Check((string)metricUnits.Attribute("diameterUnit") == "millimeter", "Metric diameter unit must be 'millimeter'.");

            // --- Structure: 1000 ft / 2000 ft / rim 105.5 ft, 4.0 ft (48 in) diameter ---
            var structElem = doc.Descendants().First(e => e.Name.LocalName == "Struct" && (string)e.Attribute("name") == "MH-1");
            string center = structElem.Elements().First(e => e.Name.LocalName == "Center").Value;
            Check(center == "304.800000 609.600000 32.156400",
                "Structure Center must be '304.800000 609.600000 32.156400' (1000 ft / 2000 ft / 105.5 ft in metres); was '" + center + "'.");
            Check(center != "1000.000000 2000.000000 105.500000",
                "Structure Center must not be written raw with a substituted unit label.");

            string elevRim = (string)structElem.Attribute("elevRim");
            Check(elevRim == "32.156400", "Structure elevRim must be 32.156400 m; was '" + elevRim + "'.");
            string elevInvert = (string)structElem.Attribute("elevInvert");
            Check(elevInvert == "30.480000", "Structure elevInvert must be 30.480000 m (100 ft); was '" + elevInvert + "'.");

            var circStruct = structElem.Elements().First(e => e.Name.LocalName == "CircStruct");
            string structDiam = (string)circStruct.Attribute("diameter");
            Check(structDiam == "1219.200000",
                "CircStruct diameter must be 1219.200000 mm (4.0 ft = 48 in); was '" + structDiam + "'.");

            // --- Pipe: 200 ft long, 1.5 ft diameter ---
            var pipeElem = doc.Descendants().First(e => e.Name.LocalName == "Pipe" && (string)e.Attribute("name") == "PIPE-1");
            string length = (string)pipeElem.Attribute("length");
            Check(length == "60.960000", "Pipe length must be 60.960000 m (200 ft); was '" + length + "'.");
            Check(length != "200.000000", "Pipe length must not be written raw with a substituted unit label.");

            var circPipe = pipeElem.Elements().First(e => e.Name.LocalName == "CircPipe");
            string pipeDiam = (string)circPipe.Attribute("diameter");
            Check(pipeDiam == "457.20",
                "CircPipe diameter must be 457.20 mm (1.5 ft x 304.8); was '" + pipeDiam + "'.");
            Check(pipeDiam != "1500.00",
                "CircPipe diameter must NOT be 1500.00 mm (the N07 defect: multiplying by 1000 as if the source were metres).");

            var startCoord = pipeElem.Elements().First(e => e.Name.LocalName == "Start");
            Check(startCoord.Value == "304.800000 609.600000 30.480000",
                "Pipe Start coordinate must be converted to metres; was '" + startCoord.Value + "'.");

            // Manning's n is dimensionless and must pass through untouched.
            var pipeFlow = pipeElem.Elements().First(e => e.Name.LocalName == "PipeFlow");
            Check((string)pipeFlow.Attribute("manningsN") == "0.0130", "Manning's n is dimensionless and must not be rescaled.");

            Console.WriteLine("N07_PROBE: imperial->metric Center=\"" + center + "\" CircStruct=" + structDiam +
                "mm Length=" + length + "m CircPipe=" + pipeDiam + "mm");

            // --- Reverse direction: metric source exported to an imperial document ---
            var metricModel = BuildMeterNetwork();
            string imperialXml = LandXmlPipeNetworkAdapter.ExportLandXml(metricModel, new LandXmlExportOptions
            {
                NetworkName = "N07-Imperial-Export",
                UnitSystem = LandXmlUnitSystem.Imperial
            });
            var imperialDoc = XDocument.Parse(imperialXml);

            var imperialPipe = imperialDoc.Descendants().First(e => e.Name.LocalName == "Pipe" && (string)e.Attribute("name") == "PIPE-1");
            string imperialLength = (string)imperialPipe.Attribute("length");
            Check(imperialLength == "200.000000",
                "60.96 m must convert back to 200.000000 ft (divide by 0.3048); was '" + imperialLength + "'.");
            string imperialDiam = (string)imperialPipe.Elements().First(e => e.Name.LocalName == "CircPipe").Attribute("diameter");
            Check(imperialDiam == "18.00",
                "457.2 mm must convert to 18.00 in (divide by 25.4); was '" + imperialDiam + "'.");

            var imperialStruct = imperialDoc.Descendants().First(e => e.Name.LocalName == "Struct" && (string)e.Attribute("name") == "MH-1");
            string imperialCenter = imperialStruct.Elements().First(e => e.Name.LocalName == "Center").Value;
            // 304.8 m -> 1000 ft, 609.6 m -> 2000 ft, 32.1564 m -> 105.5 ft.
            Check(imperialCenter == "1000.000000 2000.000000 105.500000",
                "304.8 m / 609.6 m / 32.1564 m must convert back to 1000 ft / 2000 ft / 105.5 ft; was '" + imperialCenter + "'.");

            Console.WriteLine("N07_PROBE: metric->imperial Length=" + imperialLength + "ft CircPipe=" + imperialDiam +
                "in Center=\"" + imperialCenter + "\"");
        }

        /// <summary>
        /// Test 2 — every nonfinite geometry value must fail closed, with no partial document emitted.
        /// </summary>
        private static void LandXml_Export_RejectsNonfiniteCoordinatesAndRoughness()
        {
            // Control: the unmodified fixture exports successfully, so each rejection below is caused
            // by the injected nonfinite value alone.
            string control = LandXmlPipeNetworkAdapter.ExportLandXml(BuildFeetNetwork(), new LandXmlExportOptions
            {
                UnitSystem = LandXmlUnitSystem.Metric
            });
            Check(!string.IsNullOrWhiteSpace(control) && control.Contains("LandXML"),
                "Control fixture with finite geometry must export successfully.");

            var tempDir = Path.Combine(Path.GetTempPath(), "sp-n07-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var nanLength = BuildFeetNetwork();
                nanLength.Conduits[0].Length = double.NaN;
                AssertLandXmlExportRejects(nanLength, "LANDXML_NONFINITE_CONDUIT_LENGTH", "NaN conduit length",
                    Path.Combine(tempDir, "nan-length.xml"));

                var infDiameter = BuildFeetNetwork();
                infDiameter.Conduits[0].Diameter = double.PositiveInfinity;
                AssertLandXmlExportRejects(infDiameter, "LANDXML_NONFINITE_CONDUIT_DIMENSION", "+Infinity conduit diameter",
                    Path.Combine(tempDir, "inf-diameter.xml"));

                var nanInvert = BuildFeetNetwork();
                nanInvert.Structures[1].InvertElevation = double.NaN;
                AssertLandXmlExportRejects(nanInvert, "LANDXML_NONFINITE_STRUCTURE_ELEVATION", "NaN structure invert elevation",
                    Path.Combine(tempDir, "nan-invert.xml"));

                var nanCoordinate = BuildFeetNetwork();
                nanCoordinate.Structures[0].X = double.NaN;
                AssertLandXmlExportRejects(nanCoordinate, "LANDXML_NONFINITE_STRUCTURE_COORDINATE", "NaN structure X coordinate",
                    Path.Combine(tempDir, "nan-coordinate.xml"));

                var nanRoughness = BuildFeetNetwork();
                nanRoughness.Conduits[0].ManningsN = double.NaN;
                AssertLandXmlExportRejects(nanRoughness, "LANDXML_NONFINITE_ROUGHNESS", "NaN Manning's n",
                    Path.Combine(tempDir, "nan-roughness.xml"));

                var infRoughness = BuildFeetNetwork();
                infRoughness.Conduits[0].ManningsN = double.NegativeInfinity;
                AssertLandXmlExportRejects(infRoughness, "LANDXML_NONFINITE_ROUGHNESS", "-Infinity Manning's n",
                    Path.Combine(tempDir, "inf-roughness.xml"));

                var nanStructDiam = BuildFeetNetwork();
                nanStructDiam.Structures[0].PropertyClassifications["StructureDiameter"] = "NaN";
                // A declared-but-nonfinite diameter is treated as non-authoritative, so it reports the
                // established missing-diameter code rather than being substituted by a default.
                AssertLandXmlExportRejects(nanStructDiam, "LANDXML_MISSING_STRUCTURE_DIAMETER", "NaN declared structure diameter",
                    Path.Combine(tempDir, "nan-struct-diameter.xml"));

                var outOfRangeN = BuildFeetNetwork();
                outOfRangeN.Conduits[0].ManningsN = 0.5;
                AssertLandXmlExportRejects(outOfRangeN, "LANDXML_ROUGHNESS_OUT_OF_RANGE", "out-of-range Manning's n (0.5)",
                    Path.Combine(tempDir, "range-roughness.xml"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Asserts the export refuses the model, reports the expected error code, returns no document,
        /// and leaves no file on disk.
        /// </summary>
        private static void AssertLandXmlExportRejects(NetworkReadResult model, string expectedCode, string label, string outputPath)
        {
            string xml = null;
            Exception captured = null;
            try
            {
                xml = LandXmlPipeNetworkAdapter.ExportLandXml(model, new LandXmlExportOptions
                {
                    UnitSystem = LandXmlUnitSystem.Metric
                });
                // Mirror the production pipeline: write only after a successful export.
                File.WriteAllText(outputPath, xml);
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            Check(captured is IncompleteDataException || captured is InvalidDataException,
                "Export with " + label + " must throw IncompleteDataException or InvalidDataException (observed " +
                (captured == null ? "no exception" : captured.GetType().Name) + ").");
            Check(xml == null, "Export with " + label + " must not return a partial XML document.");
            Check(!File.Exists(outputPath), "Export with " + label + " must not leave an XML file on disk.");

            var coded = captured as LandXmlExchangeException;
            Check(coded != null && coded.ErrorCode == expectedCode,
                "Export with " + label + " must report " + expectedCode + " (observed " +
                (coded == null ? "<no error code>" : coded.ErrorCode) + ").");

            Console.WriteLine("N07_PROBE: " + label + " -> THROW " +
                (captured == null ? "<none>" : captured.GetType().Name + " [" + (coded == null ? "-" : coded.ErrorCode) + "]") +
                " | xml_null=" + (xml == null) + " | file_written=" + File.Exists(outputPath));
        }

        /// <summary>
        /// Feet-based source network with explicit unit provenance: the model the N07 defect mis-scaled.
        /// </summary>
        private static NetworkReadResult BuildFeetNetwork()
        {
            var result = new NetworkReadResult();

            result.Structures.Add(new StructureReadDto
            {
                Id = 1,
                Label = "MH-1",
                RuntimeCategory = "Manhole",
                X = 1000.0,
                Y = 2000.0,
                GroundElevation = 105.5,
                RimElevation = 105.5,
                InvertElevation = 100.0,
                CoordinateUnit = FeetUnit,
                GroundElevationUnit = FeetUnit,
                RimElevationUnit = FeetUnit,
                InvertElevationUnit = FeetUnit,
                // Declared in the SOURCE document's diameter unit: inches for an imperial document.
                // 48.00 in == 4.0 ft.
                PropertyClassifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "StructureDiameter", "48.00" }
                }
            });

            result.Structures.Add(new StructureReadDto
            {
                Id = 2,
                Label = "MH-2",
                RuntimeCategory = "Manhole",
                X = 1200.0,
                Y = 2000.0,
                GroundElevation = 104.0,
                RimElevation = 104.0,
                InvertElevation = 99.0,
                CoordinateUnit = FeetUnit,
                GroundElevationUnit = FeetUnit,
                RimElevationUnit = FeetUnit,
                InvertElevationUnit = FeetUnit,
                PropertyClassifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "StructureDiameter", "48.00" }
                }
            });

            result.Conduits.Add(new ConduitReadDto
            {
                Id = 1,
                Label = "PIPE-1",
                StartNodeId = 1,
                StopNodeId = 2,
                Length = 200.0,
                Diameter = 1.5,
                Shape = "Circular",
                Material = "RCP",
                ManningsN = 0.013,
                StartInvert = 100.0,
                StopInvert = 99.0,
                LengthUnit = FeetUnit,
                DiameterUnit = FeetUnit,
                StartInvertUnit = FeetUnit,
                StopInvertUnit = FeetUnit
            });

            return result;
        }

        /// <summary>
        /// Metre-based source network used to prove the reverse conversions (divide by 0.3048 / 25.4).
        /// </summary>
        private static NetworkReadResult BuildMeterNetwork()
        {
            var result = new NetworkReadResult();

            result.Structures.Add(new StructureReadDto
            {
                Id = 1,
                Label = "MH-1",
                RuntimeCategory = "Manhole",
                X = 304.8,
                Y = 609.6,
                GroundElevation = 105.5 * 0.3048,
                RimElevation = 105.5 * 0.3048,
                InvertElevation = 30.48,
                CoordinateUnit = MeterUnit,
                GroundElevationUnit = MeterUnit,
                RimElevationUnit = MeterUnit,
                InvertElevationUnit = MeterUnit,
                PropertyClassifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Metric documents declare diameters in millimetres.
                    { "StructureDiameter", "457.20" }
                }
            });

            result.Structures.Add(new StructureReadDto
            {
                Id = 2,
                Label = "MH-2",
                RuntimeCategory = "Manhole",
                X = 365.76,
                Y = 609.6,
                GroundElevation = 104.0 * 0.3048,
                RimElevation = 104.0 * 0.3048,
                InvertElevation = 30.1752,
                CoordinateUnit = MeterUnit,
                GroundElevationUnit = MeterUnit,
                RimElevationUnit = MeterUnit,
                InvertElevationUnit = MeterUnit,
                PropertyClassifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "StructureDiameter", "457.20" }
                }
            });

            result.Conduits.Add(new ConduitReadDto
            {
                Id = 1,
                Label = "PIPE-1",
                StartNodeId = 1,
                StopNodeId = 2,
                Length = 60.96,
                Diameter = 0.4572,
                Shape = "Circular",
                Material = "RCP",
                ManningsN = 0.013,
                StartInvert = 30.48,
                StopInvert = 30.1752,
                LengthUnit = MeterUnit,
                DiameterUnit = MeterUnit,
                StartInvertUnit = MeterUnit,
                StopInvertUnit = MeterUnit
            });

            return result;
        }
    }
}
