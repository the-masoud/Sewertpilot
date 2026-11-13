using System;
using System.Globalization;
using System.Linq;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class NetworkExplorerRow
    {
        internal string[] Cells { get; set; }
        internal string Details { get; set; }
    }
    internal static class NetworkExplorerProjection
    {
        internal const string Unavailable = "Unavailable";
        internal const string TopologyNotice = "Start/Stop are Bentley topology references. Hydraulic-flow orientation is not approved or inferred.";
        internal static readonly string[] ConduitHeaders = { "ID", "Start node", "Stop node", "Start invert", "Start unit", "Start inherited", "Stop invert", "Stop unit", "Stop inherited" };
        internal static readonly string[] StructureHeaders = { "ID", "Type", "Ground", "Ground unit", "Rim", "Rim unit", "Invert", "Invert unit", "Connected conduits" };
        private static string Number(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value.ToString("R", CultureInfo.InvariantCulture) : Unavailable;
        private static string Id(int value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : Unavailable;
        private static string Flag(bool? value) => value.HasValue ? value.Value.ToString() : Unavailable;
        private static string Unit(SourceUnitIdentity unit) => unit == null || string.IsNullOrWhiteSpace(unit.UnitId) ||
            unit.UnitId.IndexOf("UNRESOLVED", StringComparison.OrdinalIgnoreCase) >= 0 ? Unavailable :
            (!string.IsNullOrWhiteSpace(unit.Symbol) ? unit.Symbol : !string.IsNullOrWhiteSpace(unit.UnitName) ? unit.UnitName : unit.UnitId);
        private static string Text(string value) => string.IsNullOrWhiteSpace(value) ? Unavailable : value;
        private static NetworkExplorerRow Row(string[] headers, string[] values, string extra) => new NetworkExplorerRow {
            Cells = values, Details = string.Join("\r\n", headers.Zip(values, (h, v) => h + ": " + v)) + "\r\n" + extra };
        internal static NetworkExplorerRow Conduit(ConduitReadDto c) => Row(ConduitHeaders, new[] {
            Id(c.Id), Id(c.StartNodeId), Id(c.StopNodeId), Number(c.StartInvert), Unit(c.StartInvertUnit), Flag(c.SetInvertToStart),
            Number(c.StopInvert), Unit(c.StopInvertUnit), Flag(c.SetInvertToStop) },
            "Label: " + Text(c.Label) + "\r\n" + TopologyNotice + "\r\nSource fields: " + Text(c.StartInvertSourceField) + " / " + Text(c.StopInvertSourceField));
        internal static NetworkExplorerRow Structure(StructureReadDto s, NetworkReadResult network) => Row(StructureHeaders, new[] {
            Id(s.Id), Text(s.RuntimeCategory), Number(s.GroundElevation), Unit(s.GroundElevationUnit), Number(s.RimElevation), Unit(s.RimElevationUnit),
            Number(s.InvertElevation), Unit(s.InvertElevationUnit), network?.Conduits == null ? Unavailable :
                string.Join(", ", network.Conduits.Where(c => c.StartNodeId == s.Id || c.StopNodeId == s.Id).Select(c => c.Id).OrderBy(id => id)) },
            "Label: " + Text(s.Label) + "\r\nX: " + Number(s.X) + "\r\nY: " + Number(s.Y) + "\r\nCoordinate unit: " + Unit(s.CoordinateUnit) +
            "\r\nConnected conduit IDs are derived only from source Start/Stop references; no flow direction is assigned.");
    }
}
