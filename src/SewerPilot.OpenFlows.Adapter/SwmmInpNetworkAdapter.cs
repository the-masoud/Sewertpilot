using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class SwmmInpParseException : Exception
    {
        public int LineNumber { get; }
        public string SectionName { get; }

        public SwmmInpParseException(string message, string sectionName = null, int lineNumber = 0)
            : base(lineNumber > 0 ? $"Line {lineNumber} [{sectionName ?? "GENERAL"}]: {message}" : message)
        {
            SectionName = sectionName;
            LineNumber = lineNumber;
        }
    }

    public sealed class SwmmJunctionDto
    {
        public string Name { get; set; }
        public double Elevation { get; set; }
        public double MaxDepth { get; set; }
        public double InitDepth { get; set; }
        public double SurDepth { get; set; }
        public double Aponded { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double BaselineInflow { get; set; }
    }

    public sealed class SwmmOutfallDto
    {
        public string Name { get; set; }
        public double Elevation { get; set; }
        public string OutfallType { get; set; } = "FREE";
        public double FixedStage { get; set; }
        public bool Gated { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class SwmmConduitDto
    {
        public string Name { get; set; }
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public double Length { get; set; }
        public double Roughness { get; set; }
        public double InOffset { get; set; }
        public double OutOffset { get; set; }
        public double InitFlow { get; set; }
        public double MaxFlow { get; set; }

        // Cross-section properties
        public string Shape { get; set; } = "CIRCULAR";
        public double Geom1 { get; set; } // Diameter / Height (ft)
        public double Geom2 { get; set; }
        public double Geom3 { get; set; }
        public double Geom4 { get; set; }
        public int Barrels { get; set; } = 1;
    }

    public sealed class SwmmSubcatchmentDto
    {
        public string Name { get; set; }
        public string RainGage { get; set; }
        public string Outlet { get; set; }
        public double AreaAcres { get; set; }
        public double PercentImpervious { get; set; }
        public double Width { get; set; }
        public double PercentSlope { get; set; }
        public double CurbLength { get; set; }
    }

    public sealed class SwmmPumpDto
    {
        public string Name { get; set; }
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string PumpCurve { get; set; } = "*";
        public string Status { get; set; } = "ON";
        public double StartupDepth { get; set; }
        public double ShutoffDepth { get; set; }
    }

    public sealed class SwmmOrificeDto
    {
        public string Name { get; set; }
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string OrificeType { get; set; } = "SIDE";
        public double Offset { get; set; }
        public double DischargeCoeff { get; set; } = 0.65;
        public bool Gated { get; set; }
        public double CloseTime { get; set; }
        public string Shape { get; set; } = "CIRCULAR";
        public double Geom1 { get; set; }
        public double Geom2 { get; set; }
    }

    public sealed class SwmmWeirDto
    {
        public string Name { get; set; }
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string WeirType { get; set; } = "TRANSVERSE";
        public double CrestHeight { get; set; }
        public double DischargeCoeff { get; set; } = 3.33;
        public bool Gated { get; set; }
        public double EndCon { get; set; }
        public double EndCoeff { get; set; }
        public bool Surcharge { get; set; } = true;
        public double RoadWidth { get; set; }
        public string RoadSurface { get; set; }
        public string Shape { get; set; } = "RECT_OPEN";
        public double Geom1 { get; set; }
        public double Geom2 { get; set; }
    }

    public sealed class SwmmCurvePointDto
    {
        public string Name { get; set; }
        public string CurveType { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class SwmmModel
    {
        public string Title { get; set; } = "SewerPilot SWMM Model";
        public Dictionary<string, string> Options { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmJunctionDto> Junctions { get; } = new Dictionary<string, SwmmJunctionDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmOutfallDto> Outfalls { get; } = new Dictionary<string, SwmmOutfallDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmConduitDto> Conduits { get; } = new Dictionary<string, SwmmConduitDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmPumpDto> Pumps { get; } = new Dictionary<string, SwmmPumpDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmOrificeDto> Orifices { get; } = new Dictionary<string, SwmmOrificeDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SwmmWeirDto> Weirs { get; } = new Dictionary<string, SwmmWeirDto>(StringComparer.OrdinalIgnoreCase);
        public List<SwmmCurvePointDto> Curves { get; } = new List<SwmmCurvePointDto>();
        public Dictionary<string, SwmmSubcatchmentDto> Subcatchments { get; } = new Dictionary<string, SwmmSubcatchmentDto>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Inflows { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public static class SwmmInpNetworkAdapter
    {
        public static SwmmModel ReadInpFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SWMM .inp file not found: " + filePath, filePath);

            string content = File.ReadAllText(filePath, Encoding.UTF8);
            return ParseInpText(content);
        }

        public static SwmmModel ParseInpText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new SwmmInpParseException("EPA-SWMM .inp content is empty or null.");

            var model = new SwmmModel();
            string currentSection = null;

            using (var reader = new StringReader(content))
            {
                string line;
                int lineNum = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
                        continue;

                    // Strip inline comments
                    int commentIndex = trimmed.IndexOf(';');
                    if (commentIndex >= 0)
                        trimmed = trimmed.Substring(0, commentIndex).Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim().ToUpperInvariant();
                        continue;
                    }

                    if (string.IsNullOrEmpty(currentSection))
                        continue;

                    var tokens = TokenizeLine(trimmed);
                    if (tokens.Count == 0)
                        continue;

                    ParseSectionLine(model, currentSection, tokens, lineNum);
                }
            }

            ValidateModelTopology(model);
            return model;
        }

        private static List<string> TokenizeLine(string line)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool hadQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hadQuotes = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (sb.Length > 0 || hadQuotes)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                        hadQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0 || hadQuotes)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static void ParseSectionLine(SwmmModel model, string section, List<string> tokens, int lineNum)
        {
            switch (section)
            {
                case "TITLE":
                    model.Title = string.Join(" ", tokens);
                    break;

                case "OPTIONS":
                    if (tokens.Count >= 2)
                        model.Options[tokens[0]] = tokens[1];
                    break;

                case "JUNCTIONS":
                    // Name Elev [MaxDepth InitDepth SurDepth Aponded]
                    if (tokens.Count >= 2)
                    {
                        var j = new SwmmJunctionDto
                        {
                            Name = tokens[0],
                            Elevation = ParseDouble(tokens[1], lineNum, section, "Elevation"),
                            MaxDepth = tokens.Count > 2 ? ParseDouble(tokens[2], lineNum, section, "MaxDepth") : 0.0,
                            InitDepth = tokens.Count > 3 ? ParseDouble(tokens[3], lineNum, section, "InitDepth") : 0.0,
                            SurDepth = tokens.Count > 4 ? ParseDouble(tokens[4], lineNum, section, "SurDepth") : 0.0,
                            Aponded = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "Aponded") : 0.0
                        };
                        model.Junctions[j.Name] = j;
                    }
                    break;

                case "STORAGE":
                    // Name Elev [MaxDepth InitDepth Shape CurveParams...]
                    if (tokens.Count >= 2)
                    {
                        var j = new SwmmJunctionDto
                        {
                            Name = tokens[0],
                            Elevation = ParseDouble(tokens[1], lineNum, section, "Elevation"),
                            MaxDepth = tokens.Count > 2 ? ParseDouble(tokens[2], lineNum, section, "MaxDepth") : 15.0,
                            InitDepth = tokens.Count > 3 ? ParseDouble(tokens[3], lineNum, section, "InitDepth") : 0.0
                        };
                        model.Junctions[j.Name] = j;
                    }
                    break;

                case "OUTFALLS":
                    // Name Elev Type [StageData Gated RouteTo]
                    if (tokens.Count >= 2)
                    {
                        string oType = tokens.Count > 2 ? tokens[2].ToUpperInvariant() : "FREE";
                        double fixedStage = 0.0;
                        bool gated = false;

                        if (oType == "FIXED")
                        {
                            if (tokens.Count > 3) fixedStage = ParseDouble(tokens[3], lineNum, section, "FixedStage");
                            if (tokens.Count > 4) gated = string.Equals(tokens[4], "YES", StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            if (tokens.Count > 3)
                            {
                                if (string.Equals(tokens[3], "YES", StringComparison.OrdinalIgnoreCase)) gated = true;
                                else if (string.Equals(tokens[3], "NO", StringComparison.OrdinalIgnoreCase)) gated = false;
                                else double.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out fixedStage);
                            }
                            if (tokens.Count > 4 && string.Equals(tokens[4], "YES", StringComparison.OrdinalIgnoreCase))
                                gated = true;
                        }

                        var o = new SwmmOutfallDto
                        {
                            Name = tokens[0],
                            Elevation = ParseDouble(tokens[1], lineNum, section, "Elevation"),
                            OutfallType = oType,
                            FixedStage = fixedStage,
                            Gated = gated
                        };
                        model.Outfalls[o.Name] = o;
                    }
                    break;

                case "CONDUITS":
                    // Name FromNode ToNode Length Roughness [InOffset OutOffset InitFlow MaxFlow]
                    if (tokens.Count >= 5)
                    {
                        var c = new SwmmConduitDto
                        {
                            Name = tokens[0],
                            FromNode = tokens[1],
                            ToNode = tokens[2],
                            Length = ParseDouble(tokens[3], lineNum, section, "Length"),
                            Roughness = ParseDouble(tokens[4], lineNum, section, "Roughness"),
                            InOffset = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "InOffset") : 0.0,
                            OutOffset = tokens.Count > 6 ? ParseDouble(tokens[6], lineNum, section, "OutOffset") : 0.0,
                            InitFlow = tokens.Count > 7 ? ParseDouble(tokens[7], lineNum, section, "InitFlow") : 0.0,
                            MaxFlow = tokens.Count > 8 ? ParseDouble(tokens[8], lineNum, section, "MaxFlow") : 0.0
                        };
                        model.Conduits[c.Name] = c;
                    }
                    break;

                case "PUMPS":
                    // Name FromNode ToNode PumpCurve Status Startup Shutoff
                    if (tokens.Count >= 3)
                    {
                        var p = new SwmmPumpDto
                        {
                            Name = tokens[0],
                            FromNode = tokens[1],
                            ToNode = tokens[2],
                            PumpCurve = tokens.Count > 3 ? tokens[3] : "*",
                            Status = tokens.Count > 4 ? tokens[4].ToUpperInvariant() : "ON",
                            StartupDepth = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "Startup") : 0.0,
                            ShutoffDepth = tokens.Count > 6 ? ParseDouble(tokens[6], lineNum, section, "Shutoff") : 0.0
                        };
                        model.Pumps[p.Name] = p;
                    }
                    break;

                case "WEIRS":
                    // Name FromNode ToNode Type CrestHt Cd Gated ...
                    if (tokens.Count >= 3)
                    {
                        var w = new SwmmWeirDto
                        {
                            Name = tokens[0],
                            FromNode = tokens[1],
                            ToNode = tokens[2],
                            WeirType = tokens.Count > 3 ? tokens[3].ToUpperInvariant() : "TRANSVERSE",
                            CrestHeight = tokens.Count > 4 ? ParseDouble(tokens[4], lineNum, section, "CrestHt") : 0.0,
                            DischargeCoeff = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "Cd") : 3.33,
                            Gated = tokens.Count > 6 && string.Equals(tokens[6], "YES", StringComparison.OrdinalIgnoreCase)
                        };
                        model.Weirs[w.Name] = w;
                    }
                    break;

                case "ORIFICES":
                    // Name FromNode ToNode Type Offset Cd Gated CloseTime
                    if (tokens.Count >= 3)
                    {
                        var o = new SwmmOrificeDto
                        {
                            Name = tokens[0],
                            FromNode = tokens[1],
                            ToNode = tokens[2],
                            OrificeType = tokens.Count > 3 ? tokens[3].ToUpperInvariant() : "SIDE",
                            Offset = tokens.Count > 4 ? ParseDouble(tokens[4], lineNum, section, "Offset") : 0.0,
                            DischargeCoeff = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "Cd") : 0.65,
                            Gated = tokens.Count > 6 && string.Equals(tokens[6], "YES", StringComparison.OrdinalIgnoreCase),
                            CloseTime = tokens.Count > 7 ? ParseDouble(tokens[7], lineNum, section, "CloseTime") : 0.0
                        };
                        model.Orifices[o.Name] = o;
                    }
                    break;

                case "CURVES":
                    // Name Type X-Value Y-Value
                    if (tokens.Count >= 4)
                    {
                        model.Curves.Add(new SwmmCurvePointDto
                        {
                            Name = tokens[0],
                            CurveType = tokens[1].ToUpperInvariant(),
                            X = ParseDouble(tokens[2], lineNum, section, "X-Value"),
                            Y = ParseDouble(tokens[3], lineNum, section, "Y-Value")
                        });
                    }
                    break;

                case "XSECTIONS":
                    // Link Shape Geom1 [Geom2 Geom3 Geom4 Barrels Culvert]
                    if (tokens.Count >= 3)
                    {
                        string linkName = tokens[0];
                        if (model.Conduits.TryGetValue(linkName, out var conduit))
                        {
                            conduit.Shape = tokens[1].ToUpperInvariant();
                            conduit.Geom1 = ParseDouble(tokens[2], lineNum, section, "Geom1");
                            conduit.Geom2 = tokens.Count > 3 ? ParseDouble(tokens[3], lineNum, section, "Geom2") : 0.0;
                            conduit.Geom3 = tokens.Count > 4 ? ParseDouble(tokens[4], lineNum, section, "Geom3") : 0.0;
                            conduit.Geom4 = tokens.Count > 5 ? ParseDouble(tokens[5], lineNum, section, "Geom4") : 0.0;
                            conduit.Barrels = tokens.Count > 6 ? (int)ParseDouble(tokens[6], lineNum, section, "Barrels") : 1;
                        }
                        else if (model.Orifices.TryGetValue(linkName, out var orifice))
                        {
                            orifice.Shape = tokens[1].ToUpperInvariant();
                            orifice.Geom1 = ParseDouble(tokens[2], lineNum, section, "Geom1");
                            orifice.Geom2 = tokens.Count > 3 ? ParseDouble(tokens[3], lineNum, section, "Geom2") : 0.0;
                        }
                        else if (model.Weirs.TryGetValue(linkName, out var weir))
                        {
                            weir.Shape = tokens[1].ToUpperInvariant();
                            weir.Geom1 = ParseDouble(tokens[2], lineNum, section, "Geom1");
                            weir.Geom2 = tokens.Count > 3 ? ParseDouble(tokens[3], lineNum, section, "Geom2") : 0.0;
                        }
                    }
                    break;

                case "INFLOWS":
                    // Node Constituent TimeSeries Type Mfactor Sfactor Baseline Pattern
                    if (tokens.Count >= 7)
                    {
                        string nodeName = tokens[0];
                        double baseline = ParseDouble(tokens[6], lineNum, section, "Baseline");
                        model.Inflows[nodeName] = baseline;
                        if (model.Junctions.TryGetValue(nodeName, out var j))
                            j.BaselineInflow = baseline;
                    }
                    break;

                case "DWF":
                    // Node Constituent Baseline [Patterns...]
                    if (tokens.Count >= 3)
                    {
                        string nodeName = tokens[0];
                        double baseline = ParseDouble(tokens[2], lineNum, section, "Baseline");
                        model.Inflows[nodeName] = baseline;
                        if (model.Junctions.TryGetValue(nodeName, out var j))
                            j.BaselineInflow = baseline;
                    }
                    break;

                case "COORDINATES":
                    // Node X-Coord Y-Coord
                    if (tokens.Count >= 3)
                    {
                        string name = tokens[0];
                        double x = ParseDouble(tokens[1], lineNum, section, "X-Coord");
                        double y = ParseDouble(tokens[2], lineNum, section, "Y-Coord");
                        if (model.Junctions.TryGetValue(name, out var j))
                        {
                            j.X = x;
                            j.Y = y;
                        }
                        else if (model.Outfalls.TryGetValue(name, out var o))
                        {
                            o.X = x;
                            o.Y = y;
                        }
                    }
                    break;

                case "SUBCATCHMENTS":
                    // Name RainGage Outlet Area %Imperv Width %Slope CurbLen [SnowPack]
                    if (tokens.Count >= 8)
                    {
                        var sc = new SwmmSubcatchmentDto
                        {
                            Name = tokens[0],
                            RainGage = tokens[1],
                            Outlet = tokens[2],
                            AreaAcres = ParseDouble(tokens[3], lineNum, section, "Area"),
                            PercentImpervious = ParseDouble(tokens[4], lineNum, section, "%Imperv"),
                            Width = ParseDouble(tokens[5], lineNum, section, "Width"),
                            PercentSlope = ParseDouble(tokens[6], lineNum, section, "%Slope"),
                            CurbLength = ParseDouble(tokens[7], lineNum, section, "CurbLen")
                        };
                        model.Subcatchments[sc.Name] = sc;
                    }
                    break;
            }
        }

        private static double ParseDouble(string str, int line, string section, string field)
        {
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            throw new SwmmInpParseException($"Failed to parse numeric value '{str}' for {field}.", section, line);
        }

        public static void ValidateModelTopology(SwmmModel model)
        {
            if (model.Junctions.Count == 0 && model.Outfalls.Count == 0)
                throw new SwmmInpParseException("SWMM model contains zero junctions and zero outfalls.");

            if (model.Conduits.Count == 0 && model.Pumps.Count == 0 && model.Orifices.Count == 0 && model.Weirs.Count == 0)
                throw new SwmmInpParseException("SWMM model contains zero links (conduits, pumps, orifices, or weirs).");

            // Check conduit node connectivity
            foreach (var kvp in model.Conduits)
            {
                var c = kvp.Value;
                bool fromExists = model.Junctions.ContainsKey(c.FromNode) || model.Outfalls.ContainsKey(c.FromNode);
                bool toExists = model.Junctions.ContainsKey(c.ToNode) || model.Outfalls.ContainsKey(c.ToNode);

                if (!fromExists)
                    throw new SwmmInpParseException($"Conduit '{c.Name}' references undefined FromNode '{c.FromNode}'.", "CONDUITS");
                if (!toExists)
                    throw new SwmmInpParseException($"Conduit '{c.Name}' references undefined ToNode '{c.ToNode}'.", "CONDUITS");
                if (string.Equals(c.FromNode, c.ToNode, StringComparison.OrdinalIgnoreCase))
                    throw new SwmmInpParseException($"Conduit '{c.Name}' has identical FromNode and ToNode '{c.FromNode}'.", "CONDUITS");
                if (c.Length <= 0.0)
                    throw new SwmmInpParseException($"Conduit '{c.Name}' has non-positive length ({c.Length} ft).", "CONDUITS");
                if (c.Geom1 <= 0.0)
                    throw new SwmmInpParseException($"Conduit '{c.Name}' has non-positive diameter/height ({c.Geom1} ft). Check [XSECTIONS].", "XSECTIONS");
            }

            // Check pump connectivity
            foreach (var p in model.Pumps.Values)
            {
                bool fromExists = model.Junctions.ContainsKey(p.FromNode) || model.Outfalls.ContainsKey(p.FromNode);
                bool toExists = model.Junctions.ContainsKey(p.ToNode) || model.Outfalls.ContainsKey(p.ToNode);

                if (!fromExists)
                    throw new SwmmInpParseException($"Pump '{p.Name}' references undefined FromNode '{p.FromNode}'.", "PUMPS");
                if (!toExists)
                    throw new SwmmInpParseException($"Pump '{p.Name}' references undefined ToNode '{p.ToNode}'.", "PUMPS");
                if (string.Equals(p.FromNode, p.ToNode, StringComparison.OrdinalIgnoreCase))
                    throw new SwmmInpParseException($"Pump '{p.Name}' has identical FromNode and ToNode '{p.FromNode}'.", "PUMPS");
            }

            // Check orifice connectivity
            foreach (var o in model.Orifices.Values)
            {
                bool fromExists = model.Junctions.ContainsKey(o.FromNode) || model.Outfalls.ContainsKey(o.FromNode);
                bool toExists = model.Junctions.ContainsKey(o.ToNode) || model.Outfalls.ContainsKey(o.ToNode);

                if (!fromExists)
                    throw new SwmmInpParseException($"Orifice '{o.Name}' references undefined FromNode '{o.FromNode}'.", "ORIFICES");
                if (!toExists)
                    throw new SwmmInpParseException($"Orifice '{o.Name}' references undefined ToNode '{o.ToNode}'.", "ORIFICES");
                if (string.Equals(o.FromNode, o.ToNode, StringComparison.OrdinalIgnoreCase))
                    throw new SwmmInpParseException($"Orifice '{o.Name}' has identical FromNode and ToNode '{o.FromNode}'.", "ORIFICES");
            }

            // Check weir connectivity
            foreach (var w in model.Weirs.Values)
            {
                bool fromExists = model.Junctions.ContainsKey(w.FromNode) || model.Outfalls.ContainsKey(w.FromNode);
                bool toExists = model.Junctions.ContainsKey(w.ToNode) || model.Outfalls.ContainsKey(w.ToNode);

                if (!fromExists)
                    throw new SwmmInpParseException($"Weir '{w.Name}' references undefined FromNode '{w.FromNode}'.", "WEIRS");
                if (!toExists)
                    throw new SwmmInpParseException($"Weir '{w.Name}' references undefined ToNode '{w.ToNode}'.", "WEIRS");
                if (string.Equals(w.FromNode, w.ToNode, StringComparison.OrdinalIgnoreCase))
                    throw new SwmmInpParseException($"Weir '{w.Name}' has identical FromNode and ToNode '{w.FromNode}'.", "WEIRS");
            }

            // Check subcatchments outlet connectivity
            foreach (var kvp in model.Subcatchments)
            {
                var sc = kvp.Value;
                bool outletExists = model.Junctions.ContainsKey(sc.Outlet) || model.Outfalls.ContainsKey(sc.Outlet);
                if (!outletExists)
                    throw new SwmmInpParseException($"Subcatchment '{sc.Name}' references undefined Outlet node '{sc.Outlet}'.", "SUBCATCHMENTS");
            }
        }

        public static NetworkReadResult ConvertToNetworkReadResult(SwmmModel model, string scenarioLabel = "SWMM Ingested Scenario")
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            if (model.Pumps.Count > 0 || model.Orifices.Count > 0 || model.Weirs.Count > 0)
            {
                throw new NotSupportedException(
                    "Conversion of active SWMM controls ([PUMPS], [WEIRS], [ORIFICES]) into canonical gravity network read results is not supported. Fail-closed contract enforced.");
            }

            var result = new NetworkReadResult
            {
                ScenarioContext = new NetworkScenarioContext
                {
                    ActiveScenarioId = 1,
                    ActiveScenarioLabel = scenarioLabel,
                    PhysicalAlternativeId = 1
                }
            };

            var nodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int nextNodeId = 1;

            // Map Junctions
            foreach (var j in model.Junctions.Values.OrderBy(x => x.Name))
            {
                int id = nextNodeId++;
                nodeMap[j.Name] = id;

                if (double.IsNaN(j.Elevation) || double.IsInfinity(j.Elevation))
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "Junction '{0}' has a nonfinite invert elevation '{1}'. Fail-closed contract enforced.", j.Name, j.Elevation));
                }

                // Authoritative elevation derivation:
                double rimElevation;
                if (j.MaxDepth > 0.0 && !double.IsNaN(j.MaxDepth) && !double.IsInfinity(j.MaxDepth))
                {
                    rimElevation = j.Elevation + j.MaxDepth;
                }
                else
                {
                    throw new IncompleteDataException(
                        "SWMM_MISSING_RIM_ELEVATION",
                        string.Format(CultureInfo.InvariantCulture,
                            "Junction '{0}' lacks authoritative rim elevation or positive depth (invert: {1}, maxDepth: {2}). Fail-closed contract enforced (N05).",
                            j.Name, j.Elevation, j.MaxDepth));
                }

                result.Structures.Add(new StructureReadDto
                {
                    Id = id,
                    Label = j.Name,
                    RuntimeCategory = "Manhole",
                    RimElevation = rimElevation,
                    InvertElevation = j.Elevation,
                    GroundElevation = rimElevation,
                    X = j.X != 0.0 ? j.X : (id * 200.0),
                    Y = j.Y != 0.0 ? j.Y : 500.0
                });
            }

            // Map Outfalls
            foreach (var o in model.Outfalls.Values.OrderBy(x => x.Name))
            {
                int id = nextNodeId++;
                nodeMap[o.Name] = id;

                result.Structures.Add(new StructureReadDto
                {
                    Id = id,
                    Label = o.Name,
                    RuntimeCategory = "Outfall",
                    RimElevation = o.Elevation + (o.FixedStage > o.Elevation ? (o.FixedStage - o.Elevation + 3.0) : 5.0),
                    InvertElevation = o.Elevation,
                    GroundElevation = o.Elevation + 5.0,
                    X = o.X != 0.0 ? o.X : (id * 200.0),
                    Y = o.Y != 0.0 ? o.Y : 500.0
                });
            }

            // Map Conduits
            int nextConduitId = 101;
            foreach (var c in model.Conduits.Values.OrderBy(x => x.Name))
            {
                int cId = nextConduitId++;
                int fromId = nodeMap[c.FromNode];
                int toId = nodeMap[c.ToNode];

                double fromInvert = 0.0;
                double toInvert = 0.0;

                if (model.Junctions.TryGetValue(c.FromNode, out var jf)) fromInvert = jf.Elevation + c.InOffset;
                else if (model.Outfalls.TryGetValue(c.FromNode, out var of)) fromInvert = of.Elevation + c.InOffset;

                if (model.Junctions.TryGetValue(c.ToNode, out var jt)) toInvert = jt.Elevation + c.OutOffset;
                else if (model.Outfalls.TryGetValue(c.ToNode, out var ot)) toInvert = ot.Elevation + c.OutOffset;

                double slope = c.Length > 0 ? Math.Max(0.0001, (fromInvert - toInvert) / c.Length) : 0.001;

                result.Conduits.Add(new ConduitReadDto
                {
                    Id = cId,
                    Label = c.Name,
                    StartNodeId = fromId,
                    StopNodeId = toId,
                    Diameter = c.Geom1 * 12.0, // Convert ft to inches
                    Length = c.Length,
                    ManningsN = c.Roughness > 0 ? c.Roughness : 0.013,
                    StartInvert = fromInvert,
                    StopInvert = toInvert,
                    Shape = c.Shape
                });
            }

            return result;
        }

        // Exposed as internal solely so the contract test can probe the formatter directly
        // (InternalsVisibleTo grants the test assembly access). Production callers are all
        // inside this adapter.
        internal static string FormatFloat(double val)
        {
            // Fail-closed contract (Finding N02): a nonfinite value has no SWMM numeric literal.
            // Substituting "0.0" would inject a fabricated quantity (zero slope, zero diameter,
            // zero flow, zero invert) into the simulation deck, corrupting the hydraulic model
            // while evading detection. There is no safe default, so refuse to serialize.
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                // Invariant formatting keeps the diagnostic byte-identical across locales.
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "Cannot format nonfinite value '{0}' for SWMM INP serialization. Fail-closed contract enforced (Finding N02).", val),
                    nameof(val));
            }
            if (val == 0.0) return "0.000000";
            if (Math.Abs(val) < 1e-6 && val != 0.0)
            {
                return val.ToString("G17", CultureInfo.InvariantCulture);
            }
            return val.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        public static string ExportToInpText(SwmmModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var sb = new StringBuilder();
            sb.AppendLine("[TITLE]");
            sb.AppendLine(";;Project Title");
            sb.AppendLine(model.Title);
            sb.AppendLine();

            sb.AppendLine("[OPTIONS]");
            sb.AppendLine(";;Option             Value");
            sb.AppendLine("FLOW_UNITS           " + (model.Options.TryGetValue("FLOW_UNITS", out var fu) ? fu : "CFS"));
            sb.AppendLine("INFILTRATION         " + (model.Options.TryGetValue("INFILTRATION", out var inf) ? inf : "HORTON"));
            sb.AppendLine("FLOW_ROUTING         " + (model.Options.TryGetValue("FLOW_ROUTING", out var fr) ? fr : "KINWAVE"));
            sb.AppendLine("START_DATE           " + (model.Options.TryGetValue("START_DATE", out var sd) ? sd : "10/19/2026"));
            sb.AppendLine("START_TIME           " + (model.Options.TryGetValue("START_TIME", out var st) ? st : "00:00:00"));
            sb.AppendLine("REPORT_START_DATE    " + (model.Options.TryGetValue("REPORT_START_DATE", out var rsd) ? rsd : (model.Options.TryGetValue("START_DATE", out var sd2) ? sd2 : "10/19/2026")));
            sb.AppendLine("REPORT_START_TIME    " + (model.Options.TryGetValue("REPORT_START_TIME", out var rst) ? rst : (model.Options.TryGetValue("START_TIME", out var st2) ? st2 : "00:00:00")));
            sb.AppendLine("END_DATE             " + (model.Options.TryGetValue("END_DATE", out var ed) ? ed : "10/19/2026"));
            sb.AppendLine("END_TIME             " + (model.Options.TryGetValue("END_TIME", out var et) ? et : "06:00:00"));
            sb.AppendLine("REPORT_STEP          " + (model.Options.TryGetValue("REPORT_STEP", out var repStep) ? repStep : "00:15:00"));
            sb.AppendLine("WET_STEP             " + (model.Options.TryGetValue("WET_STEP", out var ws) ? ws : "00:05:00"));
            sb.AppendLine("DRY_STEP             " + (model.Options.TryGetValue("DRY_STEP", out var ds) ? ds : "01:00:00"));
            sb.AppendLine("ROUTING_STEP         " + (model.Options.TryGetValue("ROUTING_STEP", out var rs) ? rs : "00:00:30"));
            var writtenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FLOW_UNITS", "INFILTRATION", "FLOW_ROUTING", "START_DATE", "START_TIME",
                "REPORT_START_DATE", "REPORT_START_TIME", "END_DATE", "END_TIME",
                "REPORT_STEP", "WET_STEP", "DRY_STEP", "ROUTING_STEP"
            };
            foreach (var kvp in model.Options)
            {
                if (!writtenOptions.Contains(kvp.Key))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1}", kvp.Key, kvp.Value));
                }
            }
            sb.AppendLine();

            sb.AppendLine("[JUNCTIONS]");
            sb.AppendLine(";;Name           Elevation    MaxDepth     InitDepth    SurDepth     Aponded");
            foreach (var j in model.Junctions.Values.OrderBy(x => x.Name))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} {1,-12} {2,-12} {3,-12} {4,-12} {5,-12}",
                    j.Name, FormatFloat(j.Elevation), FormatFloat(j.MaxDepth), FormatFloat(j.InitDepth), FormatFloat(j.SurDepth), FormatFloat(j.Aponded)));
            }
            sb.AppendLine();

            sb.AppendLine("[OUTFALLS]");
            sb.AppendLine(";;Name           Elevation    Type       Stage Data       Gated    Route To");
            foreach (var o in model.Outfalls.Values.OrderBy(x => x.Name))
            {
                if (string.Equals(o.OutfallType, "FIXED", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-12} {2,-10} {3,-16} {4,-8}",
                        o.Name, FormatFloat(o.Elevation), o.OutfallType, FormatFloat(o.FixedStage), o.Gated ? "YES" : "NO"));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-12} {2,-10} {3,-8}",
                        o.Name, FormatFloat(o.Elevation), o.OutfallType, o.Gated ? "YES" : "NO"));
                }
            }
            sb.AppendLine();

            if (model.Conduits.Count > 0)
            {
                sb.AppendLine("[CONDUITS]");
                sb.AppendLine(";;Name           From Node        To Node          Length       Roughness    InOffset     OutOffset    InitFlow     MaxFlow");
                foreach (var c in model.Conduits.Values.OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16} {3,-12} {4,-12} {5,-12} {6,-12} {7,-12} {8,-12}",
                        c.Name, c.FromNode, c.ToNode, FormatFloat(c.Length), FormatFloat(c.Roughness), FormatFloat(c.InOffset), FormatFloat(c.OutOffset), FormatFloat(c.InitFlow), FormatFloat(c.MaxFlow)));
                }
                sb.AppendLine();
            }

            if (model.Pumps.Count > 0)
            {
                sb.AppendLine("[PUMPS]");
                sb.AppendLine(";;Name           From Node        To Node          Pump Curve       Status   Startup      Shutoff");
                foreach (var p in model.Pumps.Values.OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16} {3,-16} {4,-8} {5,-12} {6,-12}",
                        p.Name, p.FromNode, p.ToNode, p.PumpCurve ?? "*", p.Status ?? "ON", FormatFloat(p.StartupDepth), FormatFloat(p.ShutoffDepth)));
                }
                sb.AppendLine();
            }

            if (model.Orifices.Count > 0)
            {
                sb.AppendLine("[ORIFICES]");
                sb.AppendLine(";;Name           From Node        To Node          Type         Offset       Cd           Gated    CloseTime");
                foreach (var o in model.Orifices.Values.OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16} {3,-12} {4,-12} {5,-12} {6,-8} {7,-12}",
                        o.Name, o.FromNode, o.ToNode, o.OrificeType ?? "SIDE", FormatFloat(o.Offset), FormatFloat(o.DischargeCoeff), o.Gated ? "YES" : "NO", FormatFloat(o.CloseTime)));
                }
                sb.AppendLine();
            }

            if (model.Weirs.Count > 0)
            {
                sb.AppendLine("[WEIRS]");
                sb.AppendLine(";;Name           From Node        To Node          Type         CrestHt      Cd           Gated");
                foreach (var w in model.Weirs.Values.OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16} {3,-12} {4,-12} {5,-12} {6,-8}",
                        w.Name, w.FromNode, w.ToNode, w.WeirType ?? "TRANSVERSE", FormatFloat(w.CrestHeight), FormatFloat(w.DischargeCoeff), w.Gated ? "YES" : "NO"));
                }
                sb.AppendLine();
            }

            if (model.Curves.Count > 0)
            {
                sb.AppendLine("[CURVES]");
                sb.AppendLine(";;Name           Type       X-Value      Y-Value");
                foreach (var crv in model.Curves)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-10} {2,-12} {3,-12}",
                        crv.Name, crv.CurveType, FormatFloat(crv.X), FormatFloat(crv.Y)));
                }
                sb.AppendLine();
            }

            sb.AppendLine("[XSECTIONS]");
            sb.AppendLine(";;Link           Shape        Geom1        Geom2        Geom3        Geom4        Barrels    Culvert");
            foreach (var c in model.Conduits.Values.OrderBy(x => x.Name))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} {1,-12} {2,-12} {3,-12} {4,-12} {5,-12} {6,-10}",
                    c.Name, c.Shape, FormatFloat(c.Geom1), FormatFloat(c.Geom2), FormatFloat(c.Geom3), FormatFloat(c.Geom4), c.Barrels));
            }
            foreach (var o in model.Orifices.Values.Where(x => x.Geom1 > 0).OrderBy(x => x.Name))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} {1,-12} {2,-12} {3,-12} {4,-12} {5,-12} 1",
                    o.Name, o.Shape, FormatFloat(o.Geom1), FormatFloat(o.Geom2), FormatFloat(0), FormatFloat(0)));
            }
            foreach (var w in model.Weirs.Values.Where(x => x.Geom1 > 0).OrderBy(x => x.Name))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} {1,-12} {2,-12} {3,-12} {4,-12} {5,-12} 1",
                    w.Name, w.Shape, FormatFloat(w.Geom1), FormatFloat(w.Geom2), FormatFloat(0), FormatFloat(0)));
            }
            sb.AppendLine();

            if (model.Inflows.Count > 0)
            {
                sb.AppendLine("[DWF]");
                sb.AppendLine(";;Node           Constituent      Baseline");
                foreach (var kvp in model.Inflows.OrderBy(x => x.Key))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} FLOW             {1}",
                        kvp.Key, FormatFloat(kvp.Value)));
                }
                sb.AppendLine();
            }

            if (model.Junctions.Values.Any(j => j.X != 0.0 || j.Y != 0.0) || model.Outfalls.Values.Any(o => o.X != 0.0 || o.Y != 0.0))
            {
                sb.AppendLine("[COORDINATES]");
                sb.AppendLine(";;Node           X-Coord          Y-Coord");
                foreach (var j in model.Junctions.Values.Where(x => x.X != 0.0 || x.Y != 0.0).OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16}", j.Name, FormatFloat(j.X), FormatFloat(j.Y)));
                }
                foreach (var o in model.Outfalls.Values.Where(x => x.X != 0.0 || x.Y != 0.0).OrderBy(x => x.Name))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} {1,-16} {2,-16}", o.Name, FormatFloat(o.X), FormatFloat(o.Y)));
                }
                sb.AppendLine();
            }

            sb.AppendLine("[REPORT]");
            sb.AppendLine("INPUT      YES");
            sb.AppendLine("CONTROLS   NO");
            sb.AppendLine("SUBCATCHMENTS ALL");
            sb.AppendLine("NODES ALL");
            sb.AppendLine("LINKS ALL");
            sb.AppendLine();

            sb.AppendLine("[TAGS]");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
