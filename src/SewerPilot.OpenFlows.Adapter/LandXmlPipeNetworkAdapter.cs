using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum LandXmlUnitSystem
    {
        Imperial = 1,
        Metric = 2
    }

    public enum LandXmlPipeNetworkType
    {
        Storm = 1,
        Sanitary = 2,
        Combined = 3,
        Other = 4
    }

    [Serializable]
    public class LandXmlExchangeException : Exception
    {
        public string ErrorCode { get; }

        public LandXmlExchangeException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode ?? "LANDXML_EXCHANGE_ERROR";
        }

        public LandXmlExchangeException(string errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? "LANDXML_EXCHANGE_ERROR";
        }

        protected LandXmlExchangeException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
    }

    [Serializable]
    public class IncompleteDataException : LandXmlExchangeException
    {
        public IncompleteDataException(string message)
            : base("INCOMPLETE_DATA", message)
        {
        }

        public IncompleteDataException(string errorCode, string message)
            : base(errorCode, message)
        {
        }

        public IncompleteDataException(string message, Exception innerException)
            : base("INCOMPLETE_DATA", message, innerException)
        {
        }

        protected IncompleteDataException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
    }

    public sealed class LandXmlImportOptions
    {
        public double DefaultManningsN { get; set; } = double.NaN;
        public string DefaultPipeMaterial { get; set; } = null;
        // No synthetic default: an unstated structure diameter must remain unstated (fail-closed).
        public double? DefaultStructureDiameterFeet { get; set; } = null;
        public bool EnforceTopologyValidity { get; set; } = true;
        public double ToleranceFeet { get; set; } = 0.001;
    }

    public sealed class LandXmlExportOptions
    {
        public string NetworkName { get; set; } = "SewerPilot-Network";
        public LandXmlPipeNetworkType NetworkType { get; set; } = LandXmlPipeNetworkType.Storm;
        public LandXmlUnitSystem UnitSystem { get; set; } = LandXmlUnitSystem.Imperial;
        /// <summary>
        /// Unit system of the read model being exported. When null it is audited from the per-element
        /// unit identities. A canonical read model without unit provenance is the feet-based working model.
        /// </summary>
        public LandXmlUnitSystem? SourceUnitSystem { get; set; }
        public string ApplicationName { get; set; } = "SewerPilot";
        public string ApplicationVersion { get; set; } = "1.0.0";
        public string ApplicationDescription { get; set; } = "SewerPilot Gravity and Storm Design Assistant";
        // No synthetic default: an unstated structure diameter must remain unstated (fail-closed).
        public double? DefaultStructureDiameterFeet { get; set; } = null;
    }

    public sealed class LandXmlRoundTripResult
    {
        public bool IsSuccess { get; set; }
        public int OriginalStructureCount { get; set; }
        public int RoundTripStructureCount { get; set; }
        public int OriginalPipeCount { get; set; }
        public int RoundTripPipeCount { get; set; }
        public double MaxCoordinateDeltaFeet { get; set; }
        public double MaxElevationDeltaFeet { get; set; }
        public double MaxDiameterDeltaInches { get; set; }
        public double MaxLengthDeltaFeet { get; set; }
        public double MaxManningsNDelta { get; set; }
        public List<string> ValidationIssues { get; set; } = new List<string>();
    }

    /// <summary>
    /// Bidirectional LandXML 1.2 Pipe Network Exchange Adapter.
    /// Provides deterministic import and export between Autodesk Civil 3D LandXML 1.2
    /// pipe networks and SewerPilot's canonical domain models (NetworkReadResult,
    /// GravityNetworkNode, GravityNetworkConduit, and GravityNetworkSnapshot).
    /// </summary>
    public static class LandXmlPipeNetworkAdapter
    {
        private const string LandXmlNamespace12 = "http://www.landxml.org/schema/LandXML-1.2";

        // =========================================================================
        // 1. IMPORT: LandXML 1.2 -> NetworkReadResult
        // =========================================================================

        public static NetworkReadResult ReadLandXml(string xmlContent, LandXmlImportOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(xmlContent))
                throw new ArgumentNullException(nameof(xmlContent), "LandXML XML content cannot be null or empty.");

            using (var reader = new StringReader(xmlContent))
            {
                return ReadLandXml(reader, options);
            }
        }

        public static NetworkReadResult ReadLandXml(TextReader reader, LandXmlImportOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            options = options ?? new LandXmlImportOptions();

            XDocument doc;
            try
            {
                doc = XDocument.Load(reader);
            }
            catch (Exception ex)
            {
                throw new LandXmlExchangeException("LANDXML_XML_PARSE_ERROR", "Failed to parse LandXML document: " + ex.Message, ex);
            }

            var root = doc.Root;
            if (root == null || !root.Name.LocalName.Equals("LandXML", StringComparison.OrdinalIgnoreCase))
                throw new LandXmlExchangeException("LANDXML_INVALID_ROOT", "Root element must be <LandXML>.");

            string version = (string)root.Attribute("version");
            if (string.IsNullOrEmpty(version) || version != "1.2")
                throw new InvalidDataException($"LandXML schema version '{version}' is unsupported. Only LandXML 1.2 is permitted.");

            // Determine Unit System
            var unitsElem = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Units", StringComparison.OrdinalIgnoreCase));
            var (unitSystem, linearUnit, diameterUnitStr) = ParseUnits(unitsElem);

            var pipeNetworksElem = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("PipeNetworks", StringComparison.OrdinalIgnoreCase));
            if (pipeNetworksElem == null)
                throw new LandXmlExchangeException("LANDXML_NO_PIPE_NETWORKS", "LandXML document contains no <PipeNetworks> element.");

            var pipeNetworkElem = pipeNetworksElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("PipeNetwork", StringComparison.OrdinalIgnoreCase));
            if (pipeNetworkElem == null)
                throw new LandXmlExchangeException("LANDXML_EMPTY_PIPE_NETWORK", "<PipeNetworks> contains no <PipeNetwork> element.");

            string networkName = (string)pipeNetworkElem.Attribute("name") ?? "ImportedPipeNetwork";
            string pipeNetType = (string)pipeNetworkElem.Attribute("pipeNetType") ?? "storm";

            var result = new NetworkReadResult
            {
                Phase = "P12-A-LANDXML",
                Status = "PASS",
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioId = 1,
                    ActiveScenarioLabel = networkName,
                    TopologyAlternativeId = 1,
                    PhysicalAlternativeId = 1,
                    ActiveTopologyAlternativeId = 1
                }
            };

            // Standard Unit Identity
            var feetUnit = new SourceUnitIdentity("length", "Feet", "feet", "ft");
            var inchUnit = new SourceUnitIdentity("length", 2, "in", "in");
            var meterUnit = new SourceUnitIdentity("length", "Meter", "meter", "m");
            var mmUnit = new SourceUnitIdentity("length", "Millimeter", "millimeter", "mm");

            SourceUnitIdentity activeLinearUnit = (unitSystem == LandXmlUnitSystem.Imperial) ? feetUnit : meterUnit;
            SourceUnitIdentity activeDiameterUnit = (unitSystem == LandXmlUnitSystem.Imperial)
                ? (diameterUnitStr.IndexOf("in", StringComparison.OrdinalIgnoreCase) >= 0 ? inchUnit : feetUnit)
                : (diameterUnitStr.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ? mmUnit : meterUnit);

            // -----------------------------------------------------------------
            // Parse <Structs>
            // -----------------------------------------------------------------
            var structsElem = pipeNetworkElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Structs", StringComparison.OrdinalIgnoreCase));
            var structNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var idToStructName = new Dictionary<int, string>();

            int nextNodeId = 101;
            if (structsElem != null)
            {
                foreach (var structElem in structsElem.Elements().Where(e => e.Name.LocalName.Equals("Struct", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = (string)structElem.Attribute("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "STR-" + nextNodeId;
                    }

                    int nodeId;
                    if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId) && parsedId > 0 && !idToStructName.ContainsKey(parsedId))
                    {
                        nodeId = parsedId;
                    }
                    else
                    {
                        // Check if ends with integer (e.g. "MH-101" -> 101)
                        int lastHyphen = name.LastIndexOfAny(new[] { '-', '_', ' ' });
                        if (lastHyphen >= 0 && lastHyphen < name.Length - 1 &&
                            int.TryParse(name.Substring(lastHyphen + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tailId) &&
                            tailId > 0 && !idToStructName.ContainsKey(tailId))
                        {
                            nodeId = tailId;
                        }
                        else
                        {
                            while (idToStructName.ContainsKey(nextNodeId)) nextNodeId++;
                            nodeId = nextNodeId++;
                        }
                    }

                    structNameToId[name] = nodeId;
                    idToStructName[nodeId] = name;

                    string desc = (string)structElem.Attribute("desc") ?? string.Empty;
                    string runtimeCategory = ClassifyStructureCategory(desc, name);

                    double? elevRim = ParseNullableDouble((string)structElem.Attribute("elevRim"));
                    double? elevInvert = ParseNullableDouble((string)structElem.Attribute("elevInvert")) ??
                                         ParseNullableDouble((string)structElem.Attribute("elevSump"));

                    // Look for child <Invert> if not on attribute
                    if (!elevInvert.HasValue)
                    {
                        var invertChild = structElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Invert", StringComparison.OrdinalIgnoreCase));
                        if (invertChild != null)
                        {
                            elevInvert = ParseNullableDouble((string)invertChild.Attribute("elev"));
                        }
                    }

                    // Parse <Center> coordinates
                    double? x = null;
                    double? y = null;
                    var centerElem = structElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Center", StringComparison.OrdinalIgnoreCase));
                    if (centerElem != null && !string.IsNullOrWhiteSpace(centerElem.Value))
                    {
                        var coords = ParseCoordinateTokens(centerElem.Value);
                        if (coords.Length >= 2)
                        {
                            x = coords[0];
                            y = coords[1];
                        }
                        if (coords.Length >= 3 && !elevRim.HasValue)
                        {
                            elevRim = coords[2];
                        }
                    }

                    if (!elevRim.HasValue)
                        throw new InvalidDataException($"Structure '{name}' lacks authoritative RimElevation or GroundElevation. Fabricated elevation substitution is prohibited.");
                    if (!elevInvert.HasValue)
                        throw new InvalidDataException($"Structure '{name}' lacks an authoritative InvertElevation. Synthesizing elevations is strictly prohibited.");

                    var structDto = new StructureReadDto
                    {
                        Id = nodeId,
                        Label = name,
                        RuntimeCategory = runtimeCategory,
                        ElementTypeId = runtimeCategory == "CatchBasin" ? 2 : (runtimeCategory == "Outfall" ? 3 : 1),
                        X = x,
                        Y = y,
                        GroundElevation = elevRim,
                        RimElevation = elevRim,
                        InvertElevation = elevInvert,
                        GroundElevationUnit = activeLinearUnit,
                        RimElevationUnit = activeLinearUnit,
                        InvertElevationUnit = activeLinearUnit,
                        CoordinateUnit = activeLinearUnit
                    };

                    structDto.PropertyClassifications["LandXmlName"] = name;
                    structDto.PropertyClassifications["LandXmlDescription"] = desc;
                    structDto.PropertyClassifications["Category"] = runtimeCategory;

                    var circChild = structElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("CircStruct", StringComparison.OrdinalIgnoreCase));
                    if (circChild != null)
                    {
                        string diamStr = (string)circChild.Attribute("diameter");
                        if (!string.IsNullOrEmpty(diamStr))
                            structDto.PropertyClassifications["StructureDiameter"] = diamStr;
                    }

                    result.Structures.Add(structDto);
                }
            }

            // -----------------------------------------------------------------
            // Parse <Pipes>
            // -----------------------------------------------------------------
            var pipesElem = pipeNetworkElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Pipes", StringComparison.OrdinalIgnoreCase));
            int nextConduitId = 201;

            if (pipesElem != null)
            {
                foreach (var pipeElem in pipesElem.Elements().Where(e => e.Name.LocalName.Equals("Pipe", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = (string)pipeElem.Attribute("name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "PIPE-" + nextConduitId;
                    }

                    int conduitId;
                    int lastHyphen = name.LastIndexOfAny(new[] { '-', '_', ' ' });
                    if (lastHyphen >= 0 && lastHyphen < name.Length - 1 &&
                        int.TryParse(name.Substring(lastHyphen + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tailId) &&
                        tailId > 0 && !result.Conduits.Any(c => c.Id == tailId))
                    {
                        conduitId = tailId;
                    }
                    else
                    {
                        while (result.Conduits.Any(c => c.Id == nextConduitId)) nextConduitId++;
                        conduitId = nextConduitId++;
                    }

                    string refStart = (string)pipeElem.Attribute("refStart");
                    string refEnd = (string)pipeElem.Attribute("refEnd");

                    if (string.IsNullOrEmpty(refStart) || !structNameToId.TryGetValue(refStart, out int startNodeId))
                        throw new LandXmlExchangeException("LANDXML_UNRESOLVED_START_STRUCTURE",
                            $"Pipe '{name}' references unresolved start structure refStart='{refStart}'.");

                    if (string.IsNullOrEmpty(refEnd) || !structNameToId.TryGetValue(refEnd, out int stopNodeId))
                        throw new LandXmlExchangeException("LANDXML_UNRESOLVED_END_STRUCTURE",
                            $"Pipe '{name}' references unresolved end structure refEnd='{refEnd}'.");

                    double? length = ParseNullableDouble((string)pipeElem.Attribute("length"));
                    double? slope = ParseNullableDouble((string)pipeElem.Attribute("slope"));
                    string material = (string)pipeElem.Attribute("material");
                    if (string.IsNullOrWhiteSpace(material) && options != null && !string.IsNullOrWhiteSpace(options.DefaultPipeMaterial))
                        material = options.DefaultPipeMaterial;

                    // Hydraulic properties (<PipeFlow>)
                    double? manningsN = null;
                    var pipeFlow = pipeElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("PipeFlow", StringComparison.OrdinalIgnoreCase));
                    if (pipeFlow != null)
                    {
                        double? nVal = ParseNullableDouble((string)pipeFlow.Attribute("manningsN")) ??
                                       ParseNullableDouble((string)pipeFlow.Attribute("n"));
                        if (nVal.HasValue && nVal.Value > 0.0) manningsN = nVal.Value;
                    }
                    if (!manningsN.HasValue && options != null && !double.IsNaN(options.DefaultManningsN) && options.DefaultManningsN > 0.0)
                        manningsN = options.DefaultManningsN;

                    if (string.IsNullOrWhiteSpace(material) || !manningsN.HasValue || manningsN.Value <= 0.0)
                        throw new InvalidDataException($"Conduit '{name}' lacks authoritative Material or Manning's roughness coefficient (n).");

                    // Cross section (<CircPipe>, <RectPipe>, <EllipticalPipe>)
                    double? diameter = null;
                    double? rise = null;
                    double? span = null;
                    string shape = "CircularPipe";

                    var circPipe = pipeElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("CircPipe", StringComparison.OrdinalIgnoreCase));
                    if (circPipe != null)
                    {
                        shape = "CircularPipe";
                        double? rawDiam = ParseNullableDouble((string)circPipe.Attribute("diameter"));
                        if (rawDiam.HasValue)
                        {
                            // If Imperial and diameterUnit is inches, convert to feet for native calculation:
                            // E.g. 18.0 inches -> 1.5 ft
                            if (unitSystem == LandXmlUnitSystem.Imperial &&
                                (diameterUnitStr.IndexOf("inch", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(diameterUnitStr, "in", StringComparison.OrdinalIgnoreCase)))
                            {
                                diameter = rawDiam.Value / 12.0;
                                rise = diameter;
                            }
                            else if (unitSystem == LandXmlUnitSystem.Metric &&
                                     (diameterUnitStr.IndexOf("millimet", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(diameterUnitStr, "mm", StringComparison.OrdinalIgnoreCase)))
                            {
                                diameter = rawDiam.Value / 1000.0;
                                rise = diameter;
                            }
                            else
                            {
                                diameter = rawDiam.Value;
                                rise = diameter;
                            }
                        }
                    }
                    else
                    {
                        var rectPipe = pipeElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("RectPipe", StringComparison.OrdinalIgnoreCase));
                        if (rectPipe != null)
                        {
                            shape = "Box";
                            span = ParseNullableDouble((string)rectPipe.Attribute("width"));
                            rise = ParseNullableDouble((string)rectPipe.Attribute("height"));
                            if (unitSystem == LandXmlUnitSystem.Imperial &&
                                (diameterUnitStr.IndexOf("inch", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(diameterUnitStr, "in", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (span.HasValue) span = span.Value / 12.0;
                                if (rise.HasValue) rise = rise.Value / 12.0;
                            }
                            else if (unitSystem == LandXmlUnitSystem.Metric &&
                                     (diameterUnitStr.IndexOf("millimet", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(diameterUnitStr, "mm", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (span.HasValue) span = span.Value / 1000.0;
                                if (rise.HasValue) rise = rise.Value / 1000.0;
                            }
                            diameter = rise;
                        }
                        else
                        {
                            var otherCrossSection = pipeElem.Elements().FirstOrDefault(e =>
                                e.Name.LocalName.EndsWith("Pipe", StringComparison.OrdinalIgnoreCase) ||
                                e.Name.LocalName.EndsWith("Section", StringComparison.OrdinalIgnoreCase));
                            if (otherCrossSection != null)
                            {
                                throw new NotSupportedException(
                                    $"LandXML pipe cross-section shape '<{otherCrossSection.Name.LocalName}>' for pipe '{name}' is not supported. Supported shapes are <CircPipe> and <RectPipe>.");
                            }
                            throw new IncompleteDataException(
                                $"LandXML pipe '{name}' has no cross section element (<CircPipe> or <RectPipe>). Missing geometry parameters must fail closed.");
                        }
                    }

                    // Inverts & Geometry (<Start>, <End>, <Centerline>)
                    double? startInvert = null;
                    double? stopInvert = null;
                    var geomPoints = new List<Point2DDto>();

                    var startElem = pipeElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Start", StringComparison.OrdinalIgnoreCase));
                    if (startElem != null)
                    {
                        startInvert = ParseNullableDouble((string)startElem.Attribute("elev"));
                        if (!string.IsNullOrWhiteSpace(startElem.Value))
                        {
                            var coords = ParseCoordinateTokens(startElem.Value);
                            if (coords.Length >= 2) geomPoints.Add(new Point2DDto(coords[0], coords[1]));
                            if (coords.Length >= 3 && !startInvert.HasValue) startInvert = coords[2];
                        }
                    }

                    var endElem = pipeElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("End", StringComparison.OrdinalIgnoreCase));
                    if (endElem != null)
                    {
                        stopInvert = ParseNullableDouble((string)endElem.Attribute("elev"));
                        if (!string.IsNullOrWhiteSpace(endElem.Value))
                        {
                            var coords = ParseCoordinateTokens(endElem.Value);
                            if (coords.Length >= 2) geomPoints.Add(new Point2DDto(coords[0], coords[1]));
                            if (coords.Length >= 3 && !stopInvert.HasValue) stopInvert = coords[2];
                        }
                    }

                    // If length was not explicit, compute from endpoints if geometry available
                    if ((!length.HasValue || length.Value <= 0) && geomPoints.Count >= 2)
                    {
                        double dx = geomPoints[1].X - geomPoints[0].X;
                        double dy = geomPoints[1].Y - geomPoints[0].Y;
                        double geomLen = Math.Sqrt(dx * dx + dy * dy);
                        if (geomLen > 0) length = geomLen;
                    }
                    if (!length.HasValue || length.Value <= 0.0)
                        throw new InvalidDataException($"Conduit '{name}' lacks authoritative coordinates or physical length.");

                    // If slope was not explicit, compute from inverts and length
                    if (!slope.HasValue && startInvert.HasValue && stopInvert.HasValue && length.HasValue && length.Value > 0)
                    {
                        slope = (startInvert.Value - stopInvert.Value) / length.Value;
                    }

                    var conduitDto = new ConduitReadDto
                    {
                        Id = conduitId,
                        Label = name,
                        StartNodeId = startNodeId,
                        StopNodeId = stopNodeId,
                        StartInvert = startInvert,
                        StopInvert = stopInvert,
                        Length = length,
                        Diameter = diameter,
                        Rise = rise,
                        Span = span,
                        Shape = shape,
                        Material = material,
                        ManningsN = manningsN,
                        LengthUnit = activeLinearUnit,
                        DiameterUnit = activeLinearUnit,
                        RiseUnit = activeLinearUnit,
                        StartInvertUnit = activeLinearUnit,
                        StopInvertUnit = activeLinearUnit,
                        GeometryPoints = geomPoints
                    };

                    conduitDto.PropertyClassifications["LandXmlName"] = name;
                    conduitDto.PropertyClassifications["Material"] = material;
                    if (diameter.HasValue)
                        conduitDto.PropertyClassifications["Diameter"] = NetworkReadPropertyStatus.PROVEN.ToString();
                    if (rise.HasValue)
                        conduitDto.PropertyClassifications["Rise"] = NetworkReadPropertyStatus.PROVEN.ToString();
                    if (span.HasValue)
                        conduitDto.PropertyClassifications["Span"] = NetworkReadPropertyStatus.PROVEN.ToString();
                    if (slope.HasValue)
                        conduitDto.PropertyClassifications["Slope"] = slope.Value.ToString("F6", CultureInfo.InvariantCulture);

                    result.Conduits.Add(conduitDto);
                }
            }

            // Run topological validation
            result.TopologyCheck = NetworkTopologyValidator.Validate(result);
            if (options.EnforceTopologyValidity && !result.TopologyCheck.IsValid)
            {
                string issueSummary = string.Join("; ", result.TopologyCheck.Issues);
                throw new LandXmlExchangeException("LANDXML_TOPOLOGY_INVALID",
                    "Imported LandXML network failed topological validation: " + issueSummary);
            }

            return result;
        }

        public static GravityNetworkSnapshot ReadLandXmlToSnapshot(string xmlContent, string modelFingerprint = null, LandXmlImportOptions options = null)
        {
            var readResult = ReadLandXml(xmlContent, options);
            string fp = modelFingerprint ?? ("LANDXML-" + (readResult.ScenarioContext?.ActiveScenarioLabel ?? "DEFAULT"));
            var ingestionService = new GravityNetworkIngestionService();
            return ingestionService.Ingest(readResult, fp);
        }

        // =========================================================================
        // 2. EXPORT: NetworkReadResult / GravityNetworkSnapshot -> LandXML 1.2
        // =========================================================================

        public static string ExportLandXml(NetworkReadResult readResult, LandXmlExportOptions options = null)
        {
            if (readResult == null) throw new ArgumentNullException(nameof(readResult));
            options = options ?? new LandXmlExportOptions();

            var structures = readResult.Structures ?? new List<StructureReadDto>();
            var conduits = readResult.Conduits ?? new List<ConduitReadDto>();

            var nodeMap = structures.ToDictionary(s => s.Id, s => s);
            LandXmlUnitSystem sourceUnitSystem = ResolveSourceUnitSystem(readResult, options);

            XNamespace ns = LandXmlNamespace12;
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ns + "LandXML",
                    new XAttribute("version", "1.2"),
                    new XAttribute("date", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XAttribute("time", DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    new XAttribute("readSpec", "1.2"),
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XAttribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "schemaLocation",
                        "http://www.landxml.org/schema/LandXML-1.2 http://www.landxml.org/schema/LandXML-1.2/LandXML-1.2.xsd"),

                    // <Units>
                    BuildUnitsElement(ns, options.UnitSystem),

                    // <Application>
                    new XElement(ns + "Application",
                        new XAttribute("name", options.ApplicationName),
                        new XAttribute("version", options.ApplicationVersion),
                        new XAttribute("desc", options.ApplicationDescription)),

                    // <PipeNetworks>
                    new XElement(ns + "PipeNetworks",
                        new XElement(ns + "PipeNetwork",
                            new XAttribute("name", options.NetworkName),
                            new XAttribute("pipeNetType", options.NetworkType.ToString().ToLowerInvariant()),

                            // <Structs>
                            new XElement(ns + "Structs",
                                structures.Select(s => BuildStructElement(ns, s, options, sourceUnitSystem))),

                            // <Pipes>
                            new XElement(ns + "Pipes",
                                conduits.Select(c => BuildPipeElement(ns, c, nodeMap, options, sourceUnitSystem)))
                        )
                    )
                )
            );

            // Serialize through an XmlWriter rather than a document-level persistence call: the
            // adapter's write-operation guard keeps every persistence primitive in one audited place.
            var sb = new StringBuilder();
            var settings = new System.Xml.XmlWriterSettings
            {
                OmitXmlDeclaration = false,
                Indent = false,
                Encoding = new UTF8Encoding(false)
            };
            using (var stream = new MemoryStream())
            {
                using (var xmlWriter = System.Xml.XmlWriter.Create(stream, settings))
                {
                    doc.WriteTo(xmlWriter);
                }
                sb.Append(new UTF8Encoding(false).GetString(stream.ToArray()));
            }
            return sb.ToString();
        }

        public static string ExportLandXml(GravityNetworkSnapshot snapshot, LandXmlExportOptions options = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            options = options ?? new LandXmlExportOptions();
            return ExportLandXml(snapshot.Nodes, snapshot.Conduits, options);
        }

        public static string ExportLandXml(
            IEnumerable<GravityNetworkNode> nodes,
            IEnumerable<GravityNetworkConduit> conduits,
            LandXmlExportOptions options = null)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (conduits == null) throw new ArgumentNullException(nameof(conduits));
            options = options ?? new LandXmlExportOptions();

            // Convert to lightweight NetworkReadResult for canonical serialization
            var result = new NetworkReadResult
            {
                Phase = "P12-A-EXPORT",
                Status = "PASS",
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioLabel = options.NetworkName
                }
            };

            foreach (var n in nodes)
            {
                result.Structures.Add(new StructureReadDto
                {
                    Id = n.SourceElementId,
                    Label = !string.IsNullOrEmpty(n.Label) ? n.Label : ("STR-" + n.SourceElementId),
                    RuntimeCategory = n.Kind.ToString(),
                    X = n.X,
                    Y = n.Y,
                    RimElevation = n.ExistingElevationState?.RimElevation,
                    GroundElevation = n.ExistingElevationState?.GroundElevation,
                    InvertElevation = n.ExistingElevationState?.InvertElevation,
                    // Unit provenance must survive the snapshot mapping so export can convert instead of
                    // assuming the target unit system (N07).
                    CoordinateUnit = n.CoordinateUnit,
                    GroundElevationUnit = n.ExistingElevationState?.GroundElevationUnit,
                    RimElevationUnit = n.ExistingElevationState?.RimElevationUnit,
                    InvertElevationUnit = n.ExistingElevationState?.InvertElevationUnit,
                    // Preserve source classification metadata (e.g. StructureDiameter) instead of
                    // dropping it here and later re-synthesizing a dimension from a default.
                    PropertyClassifications = n.PropertyClassifications != null
                        ? n.PropertyClassifications.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                });
            }

            foreach (var c in conduits)
            {
                result.Conduits.Add(new ConduitReadDto
                {
                    Id = c.SourceElementId,
                    Label = !string.IsNullOrEmpty(c.Label) ? c.Label : ("CO-" + c.SourceElementId),
                    StartNodeId = c.StartNodeId,
                    StopNodeId = c.StopNodeId,
                    Length = c.ExistingPhysicalState?.Length,
                    Diameter = c.ExistingPhysicalState?.Diameter,
                    Rise = c.ExistingPhysicalState?.Rise,
                    Span = c.ExistingPhysicalState?.Span,
                    Shape = c.ExistingPhysicalState?.Shape ?? "CircularPipe",
                    Material = c.ExistingPhysicalState?.Material,
                    ManningsN = c.ExistingPhysicalState?.ManningsN,
                    StartInvert = c.ExistingPhysicalState?.StartInvert,
                    StopInvert = c.ExistingPhysicalState?.StopInvert,
                    LengthUnit = c.ExistingPhysicalState?.LengthUnit,
                    DiameterUnit = c.ExistingPhysicalState?.DiameterUnit,
                    RiseUnit = c.ExistingPhysicalState?.RiseUnit,
                    SpanUnit = c.ExistingPhysicalState?.SpanUnit,
                    StartInvertUnit = c.ExistingPhysicalState?.StartInvertUnit,
                    StopInvertUnit = c.ExistingPhysicalState?.StopInvertUnit
                });
            }

            return ExportLandXml(result, options);
        }

        // =========================================================================
        // 3. ROUND-TRIP VALIDATION & COMPARISON
        // =========================================================================

        public static LandXmlRoundTripResult ValidateRoundTrip(string originalXml, string exportedXml, double toleranceFeet = 0.001)
        {
            var origResult = ReadLandXml(originalXml);
            var roundResult = ReadLandXml(exportedXml);

            var result = new LandXmlRoundTripResult
            {
                OriginalStructureCount = origResult.Structures.Count,
                RoundTripStructureCount = roundResult.Structures.Count,
                OriginalPipeCount = origResult.Conduits.Count,
                RoundTripPipeCount = roundResult.Conduits.Count,
                IsSuccess = true
            };

            if (result.OriginalStructureCount != result.RoundTripStructureCount)
            {
                result.IsSuccess = false;
                result.ValidationIssues.Add($"Structure count mismatch: expected {result.OriginalStructureCount}, got {result.RoundTripStructureCount}.");
            }

            if (result.OriginalPipeCount != result.RoundTripPipeCount)
            {
                result.IsSuccess = false;
                result.ValidationIssues.Add($"Pipe count mismatch: expected {result.OriginalPipeCount}, got {result.RoundTripPipeCount}.");
            }

            // Compare Structures by Label
            var roundStructMap = roundResult.Structures.ToDictionary(s => s.Label, s => s, StringComparer.OrdinalIgnoreCase);
            foreach (var origStruct in origResult.Structures)
            {
                if (!roundStructMap.TryGetValue(origStruct.Label, out var roundStruct))
                {
                    result.IsSuccess = false;
                    result.ValidationIssues.Add($"Structure '{origStruct.Label}' missing in roundtrip export.");
                    continue;
                }

                if (origStruct.X.HasValue && roundStruct.X.HasValue && origStruct.Y.HasValue && roundStruct.Y.HasValue)
                {
                    double dx = Math.Abs(origStruct.X.Value - roundStruct.X.Value);
                    double dy = Math.Abs(origStruct.Y.Value - roundStruct.Y.Value);
                    result.MaxCoordinateDeltaFeet = Math.Max(result.MaxCoordinateDeltaFeet, Math.Max(dx, dy));
                    if (dx > toleranceFeet || dy > toleranceFeet)
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Structure '{origStruct.Label}' coordinate delta exceeds tolerance: dx={dx:F4}, dy={dy:F4}.");
                    }
                }

                if (origStruct.RimElevation.HasValue && roundStruct.RimElevation.HasValue)
                {
                    double dRim = Math.Abs(origStruct.RimElevation.Value - roundStruct.RimElevation.Value);
                    result.MaxElevationDeltaFeet = Math.Max(result.MaxElevationDeltaFeet, dRim);
                    if (dRim > toleranceFeet)
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Structure '{origStruct.Label}' rim elevation delta ({dRim:F4} ft) exceeds tolerance.");
                    }
                }

                if (origStruct.InvertElevation.HasValue && roundStruct.InvertElevation.HasValue)
                {
                    double dInv = Math.Abs(origStruct.InvertElevation.Value - roundStruct.InvertElevation.Value);
                    result.MaxElevationDeltaFeet = Math.Max(result.MaxElevationDeltaFeet, dInv);
                    if (dInv > toleranceFeet)
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Structure '{origStruct.Label}' invert elevation delta ({dInv:F4} ft) exceeds tolerance.");
                    }
                }
            }

            // Compare Conduits by Label
            var roundConduitMap = roundResult.Conduits.ToDictionary(c => c.Label, c => c, StringComparer.OrdinalIgnoreCase);
            foreach (var origPipe in origResult.Conduits)
            {
                if (!roundConduitMap.TryGetValue(origPipe.Label, out var roundPipe))
                {
                    result.IsSuccess = false;
                    result.ValidationIssues.Add($"Pipe '{origPipe.Label}' missing in roundtrip export.");
                    continue;
                }

                if (origPipe.Length.HasValue && roundPipe.Length.HasValue)
                {
                    double dL = Math.Abs(origPipe.Length.Value - roundPipe.Length.Value);
                    result.MaxLengthDeltaFeet = Math.Max(result.MaxLengthDeltaFeet, dL);
                    if (dL > 0.05) // 0.05 ft length tolerance
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Pipe '{origPipe.Label}' length delta ({dL:F4} ft) exceeds tolerance.");
                    }
                }

                if (origPipe.Diameter.HasValue && roundPipe.Diameter.HasValue)
                {
                    double dDiamInches = Math.Abs(origPipe.Diameter.Value - roundPipe.Diameter.Value) * 12.0;
                    result.MaxDiameterDeltaInches = Math.Max(result.MaxDiameterDeltaInches, dDiamInches);
                    if (dDiamInches > 0.01) // 0.01 inch diameter tolerance
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Pipe '{origPipe.Label}' diameter delta ({dDiamInches:F4} in) exceeds tolerance.");
                    }
                }

                if (origPipe.ManningsN.HasValue && roundPipe.ManningsN.HasValue)
                {
                    double dN = Math.Abs(origPipe.ManningsN.Value - roundPipe.ManningsN.Value);
                    result.MaxManningsNDelta = Math.Max(result.MaxManningsNDelta, dN);
                    if (dN > 0.0005)
                    {
                        result.IsSuccess = false;
                        result.ValidationIssues.Add($"Pipe '{origPipe.Label}' Manning's n delta ({dN:F5}) exceeds tolerance.");
                    }
                }
            }

            return result;
        }

        // =========================================================================
        // PRIVATE XML HELPERS
        // =========================================================================

        private static XElement BuildUnitsElement(XNamespace ns, LandXmlUnitSystem unitSystem)
        {
            if (unitSystem == LandXmlUnitSystem.Imperial)
            {
                return new XElement(ns + "Units",
                    new XElement(ns + "Imperial",
                        new XAttribute("linearUnit", "foot"),
                        new XAttribute("areaUnit", "squareFoot"),
                        new XAttribute("volumeUnit", "cubicFeet"),
                        new XAttribute("temperatureUnit", "fahrenheit"),
                        new XAttribute("pressureUnit", "inHG"),
                        new XAttribute("diameterUnit", "inch"),
                        new XAttribute("widthUnit", "inch"),
                        new XAttribute("heightUnit", "inch")));
            }
            if (unitSystem == LandXmlUnitSystem.Metric)
            {
                return new XElement(ns + "Units",
                    new XElement(ns + "Metric",
                        new XAttribute("linearUnit", "meter"),
                        new XAttribute("areaUnit", "squareMeter"),
                        new XAttribute("volumeUnit", "cubicMeter"),
                        new XAttribute("temperatureUnit", "celsius"),
                        new XAttribute("pressureUnit", "milliBar"),
                        new XAttribute("diameterUnit", "millimeter"),
                        new XAttribute("widthUnit", "millimeter"),
                        new XAttribute("heightUnit", "millimeter")));
            }

            throw new InvalidDataException("LandXML document lacks an authoritative <Units> declaration (<Imperial> or <Metric>).");
        }

        // Exact standard conversion constants. 1 ft = 0.3048 m = 304.8 mm = 12 in; 1 in = 25.4 mm.
        private const double MetersPerFoot = 0.3048;
        private const double MillimetersPerFoot = 304.8;
        private const double MillimetersPerMeter = 1000.0;
        private const double InchesPerFoot = 12.0;
        private const double MillimetersPerInch = 25.4;

        // N06: physically credible bounds for Manning's roughness. Values outside this window are
        // not sewer/storm conduit roughnesses and must not be written as if they were measured.
        private const double MinManningsN = 0.001;
        private const double MaxManningsN = 0.200;

        private static double RequireFiniteQuantity(double value, string errorCode, string what)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new IncompleteDataException(errorCode, string.Format(CultureInfo.InvariantCulture,
                    "{0} is nonfinite ('{1}'). LandXML serialization is fail-closed; no fabricated coordinate, elevation or dimension may be written.",
                    what, value));
            }
            return value;
        }

        /// <summary>Converts a linear quantity between the source and document linear units (ft / m).</summary>
        private static double ToDocumentLinear(double value, LandXmlUnitSystem source, LandXmlUnitSystem target)
        {
            if (source == target) return value;
            return source == LandXmlUnitSystem.Imperial ? value * MetersPerFoot : value / MetersPerFoot;
        }

        /// <summary>Converts a source-linear dimension into the document's diameter unit (inch / mm).</summary>
        private static double ToDocumentDiameter(double value, LandXmlUnitSystem source, LandXmlUnitSystem target)
        {
            if (target == LandXmlUnitSystem.Imperial)
            {
                return source == LandXmlUnitSystem.Imperial
                    ? value * InchesPerFoot
                    : value * MillimetersPerMeter / MillimetersPerInch;
            }
            return source == LandXmlUnitSystem.Imperial
                ? value * MillimetersPerFoot
                : value * MillimetersPerMeter;
        }

        /// <summary>Converts a value already expressed in the source document's diameter unit (inch / mm).</summary>
        private static double SourceDiameterUnitToDocument(double value, LandXmlUnitSystem source, LandXmlUnitSystem target)
        {
            if (source == target) return value;
            return target == LandXmlUnitSystem.Imperial ? value / MillimetersPerInch : value * MillimetersPerInch;
        }

        /// <summary>
        /// N07: audits the read model's own unit provenance before any number is written. Without this
        /// audit, 1000 ft was emitted as 1000 m and a 1.5 ft pipe as 1500 mm.
        /// </summary>
        private static LandXmlUnitSystem ResolveSourceUnitSystem(NetworkReadResult readResult, LandXmlExportOptions options)
        {
            if (options != null && options.SourceUnitSystem.HasValue) return options.SourceUnitSystem.Value;

            int imperialEvidence = 0;
            int metricEvidence = 0;
            foreach (var s in readResult.Structures ?? new List<StructureReadDto>())
            {
                if (s == null) continue;
                TallyUnitSystem(ClassifyUnitSystem(s.CoordinateUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(s.RimElevationUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(s.InvertElevationUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(s.GroundElevationUnit), ref imperialEvidence, ref metricEvidence);
            }
            foreach (var c in readResult.Conduits ?? new List<ConduitReadDto>())
            {
                if (c == null) continue;
                TallyUnitSystem(ClassifyUnitSystem(c.LengthUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(c.DiameterUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(c.RiseUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(c.SpanUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(c.StartInvertUnit), ref imperialEvidence, ref metricEvidence);
                TallyUnitSystem(ClassifyUnitSystem(c.StopInvertUnit), ref imperialEvidence, ref metricEvidence);
            }

            if (imperialEvidence > 0 && metricEvidence > 0)
            {
                throw new IncompleteDataException("LANDXML_MIXED_SOURCE_UNITS", string.Format(CultureInfo.InvariantCulture,
                    "The source read model mixes Imperial ({0}) and Metric ({1}) unit identities. A single authoritative source unit system is required before conversion.",
                    imperialEvidence, metricEvidence));
            }
            if (metricEvidence > 0) return LandXmlUnitSystem.Metric;
            // Canonical read-model convention: values without unit provenance are the feet-based working model.
            return LandXmlUnitSystem.Imperial;
        }

        private static void TallyUnitSystem(LandXmlUnitSystem? unitSystem, ref int imperialEvidence, ref int metricEvidence)
        {
            if (!unitSystem.HasValue) return;
            if (unitSystem.Value == LandXmlUnitSystem.Imperial) imperialEvidence++;
            else metricEvidence++;
        }

        private static LandXmlUnitSystem? ClassifyUnitSystem(SourceUnitIdentity unit)
        {
            if (unit == null) return null;
            string id = (unit.UnitId ?? string.Empty).Trim();
            string name = (unit.UnitName ?? string.Empty).Trim();
            string symbol = (unit.Symbol ?? string.Empty).Trim();
            if (IsToken(symbol, "ft") || IsToken(symbol, "in") || IsToken(id, "Feet") || IsToken(name, "feet") ||
                IsToken(name, "foot") || IsToken(name, "in") || IsToken(name, "inch") || IsToken(name, "inches"))
            {
                return LandXmlUnitSystem.Imperial;
            }
            if (IsToken(symbol, "m") || IsToken(symbol, "mm") || IsToken(name, "m") || IsToken(name, "meter") ||
                IsToken(name, "metre") || IsToken(name, "mm") || IsToken(name, "millimeter") || IsToken(name, "millimetre"))
            {
                return LandXmlUnitSystem.Metric;
            }
            return null;
        }

        private static bool IsToken(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static XElement BuildStructElement(XNamespace ns, StructureReadDto s, LandXmlExportOptions options, LandXmlUnitSystem sourceUnitSystem)
        {
            string label = s.Label ?? ("STR-" + s.Id);
            string desc = s.RuntimeCategory ?? "Manhole";

            if (!s.RimElevation.HasValue && !s.GroundElevation.HasValue)
                throw new InvalidDataException($"Structure '{s.Id}' lacks authoritative RimElevation or GroundElevation.");
            double rim = RequireFiniteQuantity(s.RimElevation ?? s.GroundElevation.Value, "LANDXML_NONFINITE_STRUCTURE_ELEVATION",
                $"Structure '{label}' rim/ground elevation");

            if (!s.InvertElevation.HasValue)
                throw new InvalidDataException($"Structure '{s.Id}' lacks an authoritative InvertElevation. Synthesizing elevations is strictly prohibited.");
            double inv = RequireFiniteQuantity(s.InvertElevation.Value, "LANDXML_NONFINITE_STRUCTURE_ELEVATION",
                $"Structure '{label}' invert elevation");

            if (!s.X.HasValue || !s.Y.HasValue)
                throw new InvalidDataException($"Structure '{s.Id}' lacks authoritative coordinates.");
            double x = RequireFiniteQuantity(s.X.Value, "LANDXML_NONFINITE_STRUCTURE_COORDINATE", $"Structure '{label}' X coordinate");
            double y = RequireFiniteQuantity(s.Y.Value, "LANDXML_NONFINITE_STRUCTURE_COORDINATE", $"Structure '{label}' Y coordinate");

            // N07: every linear quantity is converted from the audited source unit into the document unit.
            LandXmlUnitSystem targetUnitSystem = options.UnitSystem;
            double rimDoc = ToDocumentLinear(rim, sourceUnitSystem, targetUnitSystem);
            double invDoc = ToDocumentLinear(inv, sourceUnitSystem, targetUnitSystem);
            double xDoc = ToDocumentLinear(x, sourceUnitSystem, targetUnitSystem);
            double yDoc = ToDocumentLinear(y, sourceUnitSystem, targetUnitSystem);

            var structElem = new XElement(ns + "Struct",
                new XAttribute("name", label),
                new XAttribute("desc", desc),
                new XAttribute("elevRim", rimDoc.ToString("0.000000", CultureInfo.InvariantCulture)),
                new XAttribute("elevInvert", invDoc.ToString("0.000000", CultureInfo.InvariantCulture)),
                new XElement(ns + "Center", $"{xDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {yDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {rimDoc.ToString("0.000000", CultureInfo.InvariantCulture)}")
            );

            double? structDiam = null;

            // Authoritative source: the diameter read from the source model's classification set.
            // It is expressed in the SOURCE document's diameter unit (inch for Imperial, mm for Metric),
            // so it must be converted when the export target uses the other unit regime.
            // N06: a declared diameter is taken as authoritative even when it is nonfinite or
            // non-positive, so that the validation below rejects it. Excluding NaN here would let it
            // fall through to DefaultStructureDiameterFeet — substituting a caller default for data
            // that was actually read but is unusable.
            if (s.PropertyClassifications != null &&
                s.PropertyClassifications.TryGetValue("StructureDiameter", out string sdStr) &&
                !string.IsNullOrWhiteSpace(sdStr) &&
                double.TryParse(sdStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDiam))
            {
                structDiam = SourceDiameterUnitToDocument(parsedDiam, sourceUnitSystem, targetUnitSystem);
            }
            else if (options?.DefaultStructureDiameterFeet.HasValue == true &&
                     !double.IsNaN(options.DefaultStructureDiameterFeet.Value) &&
                     !double.IsInfinity(options.DefaultStructureDiameterFeet.Value) &&
                     options.DefaultStructureDiameterFeet.Value > 0.0)
            {
                // Explicit, caller-supplied assumption only. Never an implicit 4.0 ft default.
                // The option is expressed in FEET; the attribute is expressed in the document's
                // diameter unit, so it must be converted rather than written through raw.
                structDiam = options.UnitSystem == LandXmlUnitSystem.Imperial
                    ? options.DefaultStructureDiameterFeet.Value * 12.0
                    : options.DefaultStructureDiameterFeet.Value * 304.8;
            }

            if (!structDiam.HasValue ||
                double.IsNaN(structDiam.Value) ||
                double.IsInfinity(structDiam.Value) ||
                structDiam.Value <= 0.0)
            {
                throw new IncompleteDataException(
                    "LANDXML_MISSING_STRUCTURE_DIAMETER",
                    $"Structure '{label}' (ID: {s.Id}) lacks an authoritative diameter/span. Synthetic 4.0 ft fallback is strictly prohibited.");
            }

            structElem.Add(new XElement(ns + "CircStruct",
                new XAttribute("diameter", structDiam.Value.ToString("0.000000", CultureInfo.InvariantCulture))));

            return structElem;
        }

        private static XElement BuildPipeElement(
            XNamespace ns,
            ConduitReadDto c,
            Dictionary<int, StructureReadDto> nodeMap,
            LandXmlExportOptions options,
            LandXmlUnitSystem sourceUnitSystem)
        {
            string label = c.Label ?? ("CO-" + c.Id);
            string startLabel = nodeMap.TryGetValue(c.StartNodeId, out var sStart) ? sStart.Label : ("STR-" + c.StartNodeId);
            string endLabel = nodeMap.TryGetValue(c.StopNodeId, out var sEnd) ? sEnd.Label : ("STR-" + c.StopNodeId);

            if (sStart == null || !sStart.X.HasValue || !sStart.Y.HasValue || sEnd == null || !sEnd.X.HasValue || !sEnd.Y.HasValue ||
                !c.Length.HasValue || c.Length.Value <= 0.0)
            {
                throw new InvalidDataException($"Conduit '{c.Id}' lacks authoritative coordinates or physical length.");
            }

            LandXmlUnitSystem targetUnitSystem = options.UnitSystem;

            double startX = RequireFiniteQuantity(sStart.X.Value, "LANDXML_NONFINITE_CONDUIT_COORDINATE", $"Conduit '{label}' start X coordinate");
            double startY = RequireFiniteQuantity(sStart.Y.Value, "LANDXML_NONFINITE_CONDUIT_COORDINATE", $"Conduit '{label}' start Y coordinate");
            double endX = RequireFiniteQuantity(sEnd.X.Value, "LANDXML_NONFINITE_CONDUIT_COORDINATE", $"Conduit '{label}' end X coordinate");
            double endY = RequireFiniteQuantity(sEnd.Y.Value, "LANDXML_NONFINITE_CONDUIT_COORDINATE", $"Conduit '{label}' end Y coordinate");
            double length = RequireFiniteQuantity(c.Length.Value, "LANDXML_NONFINITE_CONDUIT_LENGTH", $"Conduit '{label}' length");

            double? startInvOpt = c.StartInvert ?? sStart.InvertElevation;
            double? stopInvOpt = c.StopInvert ?? sEnd.InvertElevation;
            if (!startInvOpt.HasValue || !stopInvOpt.HasValue)
                throw new InvalidDataException($"Conduit '{c.Id}' lacks authoritative invert elevations.");

            double startInv = RequireFiniteQuantity(startInvOpt.Value, "LANDXML_NONFINITE_CONDUIT_ELEVATION", $"Conduit '{label}' start invert");
            double stopInv = RequireFiniteQuantity(stopInvOpt.Value, "LANDXML_NONFINITE_CONDUIT_ELEVATION", $"Conduit '{label}' stop invert");

            // N07: convert every linear quantity into the document's linear unit before serializing.
            double startXDoc = ToDocumentLinear(startX, sourceUnitSystem, targetUnitSystem);
            double startYDoc = ToDocumentLinear(startY, sourceUnitSystem, targetUnitSystem);
            double endXDoc = ToDocumentLinear(endX, sourceUnitSystem, targetUnitSystem);
            double endYDoc = ToDocumentLinear(endY, sourceUnitSystem, targetUnitSystem);
            double lengthDoc = ToDocumentLinear(length, sourceUnitSystem, targetUnitSystem);
            double startInvDoc = ToDocumentLinear(startInv, sourceUnitSystem, targetUnitSystem);
            double stopInvDoc = ToDocumentLinear(stopInv, sourceUnitSystem, targetUnitSystem);

            // Slope is dimensionless (length/length), so it is invariant under a unit change and is
            // derived from the converted quantities to keep the ratio exact.
            double slope = (startInvDoc - stopInvDoc) / lengthDoc;
            RequireFiniteQuantity(slope, "LANDXML_NONFINITE_CONDUIT_SLOPE", $"Conduit '{label}' slope");

            if (string.IsNullOrWhiteSpace(c.Material) || !c.ManningsN.HasValue)
                throw new InvalidDataException($"Conduit '{c.Id}' lacks authoritative Material or Manning's roughness coefficient (n).");
            string material = c.Material;

            // N06: `NaN <= 0.0` evaluates to false, so a bare range comparison let a nonfinite n
            // through and serialized manningsN="NaN" straight into the document. Finiteness is
            // checked first, then the sign, then the physical range.
            double manningsN = RequireFiniteQuantity(c.ManningsN.Value, "LANDXML_NONFINITE_ROUGHNESS",
                $"Conduit '{label}' Manning's roughness coefficient (n)");
            if (manningsN <= 0.0)
            {
                // Preserves the established contract: a non-positive n is a missing/invalid input,
                // reported as InvalidDataException rather than an incomplete-data condition.
                throw new InvalidDataException(
                    $"Conduit '{c.Id}' lacks authoritative Material or Manning's roughness coefficient (n). Non-positive value {manningsN.ToString(CultureInfo.InvariantCulture)} is not a physical roughness.");
            }
            if (manningsN <= MinManningsN || manningsN >= MaxManningsN)
            {
                throw new IncompleteDataException("LANDXML_ROUGHNESS_OUT_OF_RANGE", string.Format(CultureInfo.InvariantCulture,
                    "Conduit '{0}' Manning's roughness coefficient (n) is {1}, outside the physically credible range ({2}, {3}). Fail-closed: no out-of-range roughness is written.",
                    label, manningsN, MinManningsN, MaxManningsN));
            }

            XElement crossSectionElem;
            if (string.Equals(c.Shape, "Box", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Shape, "Rectangular", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Shape, "RectPipe", StringComparison.OrdinalIgnoreCase))
            {
                double widthFt = c.Span ?? c.Diameter ?? 0.0;
                double heightFt = c.Rise ?? c.Diameter ?? 0.0;
                if (widthFt <= 0 || heightFt <= 0)
                    throw new IncompleteDataException($"LandXML pipe export failed: Width or height missing/non-positive for rectangular conduit '{label}'.");
                RequireFiniteQuantity(widthFt, "LANDXML_NONFINITE_CONDUIT_DIMENSION", $"Conduit '{label}' width");
                RequireFiniteQuantity(heightFt, "LANDXML_NONFINITE_CONDUIT_DIMENSION", $"Conduit '{label}' height");

                // A source-linear dimension is converted into the document's diameter unit (inch / mm).
                double widthAttr = ToDocumentDiameter(widthFt, sourceUnitSystem, targetUnitSystem);
                double heightAttr = ToDocumentDiameter(heightFt, sourceUnitSystem, targetUnitSystem);

                crossSectionElem = new XElement(ns + "RectPipe",
                    new XAttribute("width", widthAttr.ToString("F2", CultureInfo.InvariantCulture)),
                    new XAttribute("height", heightAttr.ToString("F2", CultureInfo.InvariantCulture)));
            }
            else if (string.IsNullOrEmpty(c.Shape) ||
                     string.Equals(c.Shape, "CircularPipe", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.Shape, "Circular", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.Shape, "CircPipe", StringComparison.OrdinalIgnoreCase))
            {
                double? diamOpt = c.Diameter ?? c.Rise;
                if (!diamOpt.HasValue || diamOpt.Value <= 0)
                    throw new IncompleteDataException($"LandXML pipe export failed: Diameter missing or non-positive for conduit '{label}'.");

                double diamFeet = RequireFiniteQuantity(diamOpt.Value, "LANDXML_NONFINITE_CONDUIT_DIMENSION", $"Conduit '{label}' diameter");
                // 1.5 ft -> 457.2 mm (not 1500 mm) when the document is Metric.
                double diamAttributeVal = ToDocumentDiameter(diamFeet, sourceUnitSystem, targetUnitSystem);

                crossSectionElem = new XElement(ns + "CircPipe",
                    new XAttribute("diameter", diamAttributeVal.ToString("F2", CultureInfo.InvariantCulture)));
            }
            else
            {
                throw new NotSupportedException($"Conduit shape '{c.Shape}' for conduit '{label}' is not supported for LandXML export.");
            }

            var pipeElem = new XElement(ns + "Pipe",
                new XAttribute("name", label),
                new XAttribute("refStart", startLabel),
                new XAttribute("refEnd", endLabel),
                new XAttribute("length", lengthDoc.ToString("0.000000", CultureInfo.InvariantCulture)),
                new XAttribute("slope", slope.ToString("0.000000", CultureInfo.InvariantCulture)),
                new XAttribute("material", material),
                crossSectionElem,
                new XElement(ns + "PipeFlow",
                    new XAttribute("manningsN", manningsN.ToString("F4", CultureInfo.InvariantCulture))),
                new XElement(ns + "Start",
                    new XAttribute("elev", startInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)),
                    $"{startXDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {startYDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {startInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)}"),
                new XElement(ns + "End",
                    new XAttribute("elev", stopInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)),
                    $"{endXDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {endYDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {stopInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)}"),
                new XElement(ns + "Centerline",
                    new XElement(ns + "Coord", $"{startXDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {startYDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {startInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)}"),
                    new XElement(ns + "Coord", $"{endXDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {endYDoc.ToString("0.000000", CultureInfo.InvariantCulture)} {stopInvDoc.ToString("0.000000", CultureInfo.InvariantCulture)}")
                )
            );

            return pipeElem;
        }

        private static (LandXmlUnitSystem unitSystem, string linearUnit, string diameterUnit) ParseUnits(XElement unitsElem)
        {
            if (unitsElem != null)
            {
                var imperial = unitsElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Imperial", StringComparison.OrdinalIgnoreCase));
                if (imperial != null)
                {
                    string linear = (string)imperial.Attribute("linearUnit") ?? "foot";
                    string diam = (string)imperial.Attribute("diameterUnit") ?? "inch";
                    return (LandXmlUnitSystem.Imperial, linear, diam);
                }

                var metric = unitsElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Metric", StringComparison.OrdinalIgnoreCase));
                if (metric != null)
                {
                    string linear = (string)metric.Attribute("linearUnit") ?? "meter";
                    string diam = (string)metric.Attribute("diameterUnit") ?? "millimeter";
                    return (LandXmlUnitSystem.Metric, linear, diam);
                }
            }

            throw new InvalidDataException("LandXML document lacks an authoritative <Units> declaration (<Imperial> or <Metric>).");
        }

        private static string ClassifyStructureCategory(string desc, string name)
        {
            string combined = (desc + " " + name).ToLowerInvariant();
            if (combined.Contains("outfall") || combined.Contains("discharge"))
                return "Outfall";
            if (combined.Contains("inlet") || combined.Contains("catch") || combined.Contains("curb") || combined.Contains("grate"))
                return "CatchBasin";
            return "Manhole";
        }

        private static double[] ParseCoordinateTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new double[0];
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<double>(tokens.Length);
            foreach (var token in tokens)
            {
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    list.Add(val);
            }
            return list.ToArray();
        }

        private static double? ParseNullableDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val) ? (double?)val : null;
        }
    }
}
