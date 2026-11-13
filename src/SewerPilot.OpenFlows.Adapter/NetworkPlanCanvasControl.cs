using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum PlanCanvasElementKind
    {
        None = 0,
        Conduit = 1,
        Structure = 2
    }

    public enum PlanTraceDirection
    {
        None = 0,
        Upstream = 1,
        Downstream = 2
    }

    public sealed class PlanCanvasSelectionEventArgs : EventArgs
    {
        public PlanCanvasElementKind ElementKind { get; }
        public int ElementId { get; }
        public string Label { get; }
        public object RawItem { get; }

        public PlanCanvasSelectionEventArgs(PlanCanvasElementKind kind, int id, string label, object rawItem)
        {
            ElementKind = kind;
            ElementId = id;
            Label = label;
            RawItem = rawItem;
        }
    }

    public sealed class PlanConduitItem
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public int StartNodeId { get; set; }
        public int StopNodeId { get; set; }
        public PointF StartPoint { get; set; }
        public PointF StopPoint { get; set; }
        public List<PointF> IntermediatePoints { get; set; } = new List<PointF>();
        public double DiameterInches { get; set; }
        public double LengthFeet { get; set; }
        public double Slope { get; set; }
        public double? StartInvertFeet { get; set; }
        public double? StopInvertFeet { get; set; }
        public string Shape { get; set; } = "Circular";
        public string Material { get; set; } = "Concrete";
        public double? ManningsN { get; set; } = 0.013;
        public double? FlowCfs { get; set; }
        public double? VelocityFps { get; set; }
        public double? TotalHeadLossFeet { get; set; }
        public double? UpstreamHglFeet { get; set; }
        public double? DownstreamHglFeet { get; set; }
        public bool IsSurcharged { get; set; }
        public bool HasViolations { get; set; }
        public List<string> Violations { get; set; } = new List<string>();
        public int P25DecisionCount { get; set; }
        public ConduitReadDto RawDto { get; set; }

        public IEnumerable<PointF> AllPoints()
        {
            yield return StartPoint;
            if (IntermediatePoints != null)
            {
                foreach (var p in IntermediatePoints) yield return p;
            }
            yield return StopPoint;
        }
    }

    public sealed class PlanStructureItem
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Category { get; set; } = "Manhole";
        public PointF Location { get; set; }
        public double? RimElevationFeet { get; set; }
        public double? InvertElevationFeet { get; set; }
        public double? GroundElevationFeet { get; set; }
        public bool IsOutfall { get; set; }
        public double? HglFeet { get; set; }
        public double? FreeboardFeet { get; set; }
        public bool HasViolations { get; set; }
        public List<string> Violations { get; set; } = new List<string>();
        public int P25DecisionCount { get; set; }
        public StructureReadDto RawDto { get; set; }
    }

    // Interactive 2D Plan View Canvas with Pan/Zoom, Selection, Tracing and Violations Overlay.
    // Offline / headless testable: renders to memory bitmap without active display dependencies.
    internal sealed class NetworkPlanCanvasControl : Control
    {
        private readonly Dictionary<int, PlanStructureItem> structures = new Dictionary<int, PlanStructureItem>();
        private readonly Dictionary<int, PlanConduitItem> conduits = new Dictionary<int, PlanConduitItem>();
        private readonly HashSet<int> tracedConduits = new HashSet<int>();
        private readonly HashSet<int> tracedStructures = new HashSet<int>();

        private RectangleF worldBounds = RectangleF.Empty;
        private float zoomFactor = 1.0f;
        private PointF panOffset = PointF.Empty;
        private bool isPanning;
        private Point panStart;
        private Point lastMousePos = new Point(-1, -1);

        private PlanCanvasElementKind selectedKind = PlanCanvasElementKind.None;
        private int selectedId = 0;
        private PlanCanvasElementKind hoveredKind = PlanCanvasElementKind.None;
        private int hoveredId = 0;

        public event EventHandler<PlanCanvasSelectionEventArgs> ElementSelected;
        public event EventHandler TraceChanged;

        public bool ShowViolations { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowFlowArrows { get; set; } = true;
        public bool ShowGrid { get; set; } = true;

        public PlanCanvasElementKind SelectedKind => selectedKind;
        public int SelectedId => selectedId;
        public int StructureCount => structures.Count;
        public int ConduitCount => conduits.Count;
        public IReadOnlyDictionary<int, PlanConduitItem> Conduits => conduits;
        public IReadOnlyDictionary<int, PlanStructureItem> Structures => structures;
        public PlanConduitItem GetConduit(int id) { conduits.TryGetValue(id, out var c); return c; }
        public PlanStructureItem GetStructure(int id) { structures.TryGetValue(id, out var s); return s; }
        public int TracedConduitCount => tracedConduits.Count;
        public int TracedStructureCount => tracedStructures.Count;
        public RectangleF WorldBounds => worldBounds;
        public float ZoomFactor => zoomFactor;
        public PointF PanOffset => panOffset;

        public NetworkPlanCanvasControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 28, 36);
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
        }

        public void ClearNetwork()
        {
            structures.Clear();
            conduits.Clear();
            tracedConduits.Clear();
            tracedStructures.Clear();
            selectedKind = PlanCanvasElementKind.None;
            selectedId = 0;
            hoveredKind = PlanCanvasElementKind.None;
            hoveredId = 0;
            worldBounds = RectangleF.Empty;
            zoomFactor = 1.0f;
            panOffset = PointF.Empty;
            Invalidate();
        }

        public void LoadNetwork(
            NetworkReadResult network,
            StormExecutiveReportContext hydraulicContext = null,
            IEnumerable<EngineeringDecision> decisions = null)
        {
            ClearNetwork();
            if (network == null) return;

            var decisionList = decisions != null ? decisions.ToList() : new List<EngineeringDecision>();

            // 1. Ingest Structures
            bool hasValidCoords = false;
            if (network.Structures != null)
            {
                foreach (var s in network.Structures)
                {
                    double x = s.X.GetValueOrDefault(0.0);
                    double y = s.Y.GetValueOrDefault(0.0);
                    if (Math.Abs(x) > 0.001 || Math.Abs(y) > 0.001) hasValidCoords = true;

                    bool isOutfall = string.Equals(s.RuntimeCategory, "Outfall", StringComparison.OrdinalIgnoreCase) ||
                                     (s.Label != null && s.Label.IndexOf("Outfall", StringComparison.OrdinalIgnoreCase) >= 0);

                    var item = new PlanStructureItem
                    {
                        Id = s.Id,
                        Label = string.IsNullOrEmpty(s.Label) ? "STR-" + s.Id : s.Label,
                        Category = string.IsNullOrEmpty(s.RuntimeCategory) ? "Structure" : s.RuntimeCategory,
                        Location = new PointF((float)x, (float)y),
                        RimElevationFeet = s.RimElevation,
                        InvertElevationFeet = s.InvertElevation,
                        GroundElevationFeet = s.GroundElevation,
                        IsOutfall = isOutfall,
                        RawDto = s
                    };

                    // Check P25 decisions
                    item.P25DecisionCount = decisionList.Count(d =>
                        d.SubjectElements != null &&
                        (d.SubjectElements.Contains(s.Id.ToString(CultureInfo.InvariantCulture)) ||
                         (!string.IsNullOrEmpty(s.Label) && d.SubjectElements.Contains(s.Label))));

                    structures[s.Id] = item;
                }
            }

            // 2. Ingest Conduits
            if (network.Conduits != null)
            {
                foreach (var c in network.Conduits)
                {
                    double len = c.Length.GetValueOrDefault(100.0);
                    double diam = c.Diameter.GetValueOrDefault(18.0);
                    if (diam < 1.0 && diam > 0.0) diam *= 12.0; // convert feet to inches if fractional

                    double slope = 0.005;
                    if (c.StartInvert.HasValue && c.StopInvert.HasValue && len > 0.001)
                    {
                        slope = (c.StartInvert.Value - c.StopInvert.Value) / len;
                    }

                    var item = new PlanConduitItem
                    {
                        Id = c.Id,
                        Label = string.IsNullOrEmpty(c.Label) ? "P-" + c.Id : c.Label,
                        StartNodeId = c.StartNodeId,
                        StopNodeId = c.StopNodeId,
                        DiameterInches = diam,
                        LengthFeet = len,
                        Slope = slope,
                        StartInvertFeet = c.StartInvert,
                        StopInvertFeet = c.StopInvert,
                        Shape = string.IsNullOrEmpty(c.Shape) ? "Circular" : c.Shape,
                        Material = string.IsNullOrEmpty(c.Material) ? "Concrete" : c.Material,
                        ManningsN = c.ManningsN.GetValueOrDefault(0.013),
                        RawDto = c
                    };

                    if (c.GeometryPoints != null && c.GeometryPoints.Count > 0)
                    {
                        foreach (var gp in c.GeometryPoints)
                        {
                            item.IntermediatePoints.Add(new PointF((float)gp.X, (float)gp.Y));
                        }
                    }

                    // Check P25 decisions
                    item.P25DecisionCount = decisionList.Count(d =>
                        d.SubjectElements != null &&
                        (d.SubjectElements.Contains(c.Id.ToString(CultureInfo.InvariantCulture)) ||
                         (!string.IsNullOrEmpty(c.Label) && d.SubjectElements.Contains(c.Label))));

                    conduits[c.Id] = item;
                }
            }

            // 3. Fallback Layout if no valid coordinates
            if (!hasValidCoords && structures.Count > 0)
            {
                ApplyTopologicalSchematicLayout();
            }
            else
            {
                // Connect conduit endpoints to structure locations
                foreach (var kvp in conduits)
                {
                    var c = kvp.Value;
                    if (structures.TryGetValue(c.StartNodeId, out var sStart))
                        c.StartPoint = sStart.Location;
                    if (structures.TryGetValue(c.StopNodeId, out var sStop))
                        c.StopPoint = sStop.Location;
                }
            }

            // 4. Bind Hydraulic Metrics & Violations if provided
            if (hydraulicContext != null && hydraulicContext.VerificationResult != null)
            {
                BindHydraulicResults(hydraulicContext.VerificationResult);
            }

            // 5. Compute World Bounds & Auto-Fit
            ComputeWorldBounds();
            ZoomToFit();
            Invalidate();
        }

        public void BindHydraulicResults(StormNetworkVerificationResult ver)
        {
            if (ver == null) return;

            if (ver.NodeStates != null)
            {
                foreach (var n in ver.NodeStates)
                {
                    if (structures.TryGetValue(n.NodeId, out var s))
                    {
                        s.HglFeet = n.HglFeet;
                        s.FreeboardFeet = n.FreeboardFeet;
                        s.IsOutfall = n.IsOutfall;
                        if (n.FloodingState == NodeFloodingState.SurfaceFlooding)
                        {
                            s.HasViolations = true;
                            s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "SURFACE FLOODING: HGL ({0:F2} ft) exceeds Rim ({1:F2} ft)", n.HglFeet, n.RimElevationFeet));
                        }
                        else if (n.FloodingState == NodeFloodingState.FreeboardExceeded)
                        {
                            s.HasViolations = true;
                            s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "FREEBOARD VIOLATION: Margin ({0:F2} ft) < 1.0 ft", n.FreeboardFeet));
                        }
                    }
                }
            }

            if (ver.ConduitStates != null)
            {
                foreach (var cs in ver.ConduitStates)
                {
                    // Match by Id or Label
                    PlanConduitItem c = null;
                    if (int.TryParse(cs.ConduitId, out int cid) && conduits.TryGetValue(cid, out var foundById))
                    {
                        c = foundById;
                    }
                    else
                    {
                        c = conduits.Values.FirstOrDefault(item => string.Equals(item.Label, cs.ConduitId, StringComparison.OrdinalIgnoreCase));
                    }

                    if (c != null)
                    {
                        c.FlowCfs = cs.FlowCfs;
                        c.VelocityFps = cs.VelocityFps;
                        c.TotalHeadLossFeet = cs.TotalHeadLossFeet;
                        c.UpstreamHglFeet = cs.UpstreamHglFeet;
                        c.DownstreamHglFeet = cs.DownstreamHglFeet;
                        c.IsSurcharged = cs.IsSurcharged;

                        if (cs.IsSurcharged)
                        {
                            c.HasViolations = true;
                            c.Violations.Add(string.Format(CultureInfo.InvariantCulture, "SURCHARGED: HGL ({0:F2} ft) exceeds Crown ({1:F2} ft)", cs.UpstreamHglFeet, cs.UpstreamCrownFeet));
                        }
                        if (cs.VelocityFps < 2.5)
                        {
                            c.HasViolations = true;
                            c.Violations.Add(string.Format(CultureInfo.InvariantCulture, "LOW VELOCITY: {0:F2} fps < Minimum 2.50 fps self-cleansing", cs.VelocityFps));
                        }
                        else if (cs.VelocityFps > 10.0)
                        {
                            c.HasViolations = true;
                            c.Violations.Add(string.Format(CultureInfo.InvariantCulture, "HIGH VELOCITY: {0:F2} fps > Maximum 10.00 fps scouring limit", cs.VelocityFps));
                        }
                    }
                }
            }

            Invalidate();
        }

        private void ApplyTopologicalSchematicLayout()
        {
            // Clean deterministic 2D layout for models without explicit coordinates:
            // Organizes conduits along a progressive horizontal/vertical flow network.
            var orderedNodes = new List<int>();
            var inDegree = new Dictionary<int, int>();
            foreach (var kvp in structures) inDegree[kvp.Key] = 0;
            foreach (var kvp in conduits)
            {
                if (inDegree.ContainsKey(kvp.Value.StopNodeId))
                    inDegree[kvp.Value.StopNodeId]++;
            }

            var queue = new Queue<int>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
            if (queue.Count == 0 && structures.Count > 0)
                queue.Enqueue(structures.Keys.First());

            var visited = new HashSet<int>();
            float curX = 100f;
            float curY = 300f;
            float stepX = 220f;

            while (queue.Count > 0)
            {
                int nid = queue.Dequeue();
                if (!visited.Add(nid)) continue;

                if (structures.TryGetValue(nid, out var str))
                {
                    str.Location = new PointF(curX, curY);
                    curX += stepX;
                }

                // Enqueue downstream structures
                foreach (var c in conduits.Values.Where(cd => cd.StartNodeId == nid))
                {
                    if (!visited.Contains(c.StopNodeId))
                        queue.Enqueue(c.StopNodeId);
                }
            }

            // Assign remaining unvisited nodes
            foreach (var str in structures.Values.Where(s => !visited.Contains(s.Id)))
            {
                str.Location = new PointF(curX, curY);
                curX += stepX;
            }

            foreach (var c in conduits.Values)
            {
                if (structures.TryGetValue(c.StartNodeId, out var sStart))
                    c.StartPoint = sStart.Location;
                if (structures.TryGetValue(c.StopNodeId, out var sStop))
                    c.StopPoint = sStop.Location;
            }
        }

        private void ComputeWorldBounds()
        {
            if (structures.Count == 0 && conduits.Count == 0)
            {
                worldBounds = RectangleF.Empty;
                return;
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var s in structures.Values)
            {
                if (s.Location.X < minX) minX = s.Location.X;
                if (s.Location.X > maxX) maxX = s.Location.X;
                if (s.Location.Y < minY) minY = s.Location.Y;
                if (s.Location.Y > maxY) maxY = s.Location.Y;
            }

            foreach (var c in conduits.Values)
            {
                foreach (var p in c.AllPoints())
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }

            float width = Math.Max(20f, maxX - minX);
            float height = Math.Max(20f, maxY - minY);
            float padX = Math.Max(30f, width * 0.1f);
            float padY = Math.Max(30f, height * 0.1f);

            worldBounds = new RectangleF(minX - padX, minY - padY, width + padX * 2, height + padY * 2);
        }

        public void ZoomToFit()
        {
            if (worldBounds.IsEmpty || ClientRectangle.Width <= 10 || ClientRectangle.Height <= 10)
            {
                zoomFactor = 1.0f;
                panOffset = PointF.Empty;
                Invalidate();
                return;
            }

            float sx = (ClientRectangle.Width - 40f) / worldBounds.Width;
            float sy = (ClientRectangle.Height - 40f) / worldBounds.Height;
            zoomFactor = Math.Max(0.01f, Math.Min(sx, sy));

            float centerX = (worldBounds.Left + worldBounds.Right) / 2f;
            float centerY = (worldBounds.Top + worldBounds.Bottom) / 2f;

            panOffset = new PointF(
                ClientRectangle.Width / 2f - centerX * zoomFactor,
                ClientRectangle.Height / 2f - centerY * zoomFactor
            );

            Invalidate();
        }

        public void Zoom(float factor)
        {
            zoomFactor = Math.Max(0.05f, Math.Min(50.0f, zoomFactor * factor));
            Invalidate();
        }

        public void ResetView()
        {
            ZoomToFit();
        }

        public PointF WorldToScreen(PointF worldPt)
        {
            return new PointF(
                worldPt.X * zoomFactor + panOffset.X,
                worldPt.Y * zoomFactor + panOffset.Y
            );
        }

        public PointF ScreenToWorld(Point screenPt)
        {
            if (Math.Abs(zoomFactor) < 0.00001f) return PointF.Empty;
            return new PointF(
                (screenPt.X - panOffset.X) / zoomFactor,
                (screenPt.Y - panOffset.Y) / zoomFactor
            );
        }

        public void SelectElement(PlanCanvasElementKind kind, int id)
        {
            selectedKind = kind;
            selectedId = id;
            Invalidate();

            string label = "";
            object raw = null;
            if (kind == PlanCanvasElementKind.Conduit && conduits.TryGetValue(id, out var c))
            {
                label = c.Label;
                raw = c;
            }
            else if (kind == PlanCanvasElementKind.Structure && structures.TryGetValue(id, out var s))
            {
                label = s.Label;
                raw = s;
            }

            ElementSelected?.Invoke(this, new PlanCanvasSelectionEventArgs(kind, id, label, raw));
        }

        public void SelectConduit(int conduitId)
        {
            SelectElement(PlanCanvasElementKind.Conduit, conduitId);
        }

        public void SelectStructure(int structureId)
        {
            SelectElement(PlanCanvasElementKind.Structure, structureId);
        }

        public void ClearSelection()
        {
            selectedKind = PlanCanvasElementKind.None;
            selectedId = 0;
            Invalidate();
            ElementSelected?.Invoke(this, new PlanCanvasSelectionEventArgs(PlanCanvasElementKind.None, 0, null, null));
        }

        public void TraceUpstream(int startId, bool isConduit)
        {
            tracedConduits.Clear();
            tracedStructures.Clear();

            var nodeQueue = new Queue<int>();

            if (isConduit && conduits.TryGetValue(startId, out var conduit))
            {
                tracedConduits.Add(conduit.Id);
                tracedStructures.Add(conduit.StopNodeId);
                tracedStructures.Add(conduit.StartNodeId);
                nodeQueue.Enqueue(conduit.StartNodeId);
            }
            else if (!isConduit && structures.TryGetValue(startId, out var structure))
            {
                tracedStructures.Add(structure.Id);
                nodeQueue.Enqueue(structure.Id);
            }

            var visitedNodes = new HashSet<int>(nodeQueue);

            while (nodeQueue.Count > 0)
            {
                int nid = nodeQueue.Dequeue();
                // Find all conduits that flow INTO this node (StopNodeId == nid)
                foreach (var c in conduits.Values.Where(cd => cd.StopNodeId == nid))
                {
                    tracedConduits.Add(c.Id);
                    if (visitedNodes.Add(c.StartNodeId))
                    {
                        tracedStructures.Add(c.StartNodeId);
                        nodeQueue.Enqueue(c.StartNodeId);
                    }
                }
            }

            Invalidate();
            TraceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void TraceDownstream(int startId, bool isConduit)
        {
            tracedConduits.Clear();
            tracedStructures.Clear();

            var nodeQueue = new Queue<int>();

            if (isConduit && conduits.TryGetValue(startId, out var conduit))
            {
                tracedConduits.Add(conduit.Id);
                tracedStructures.Add(conduit.StartNodeId);
                tracedStructures.Add(conduit.StopNodeId);
                nodeQueue.Enqueue(conduit.StopNodeId);
            }
            else if (!isConduit && structures.TryGetValue(startId, out var structure))
            {
                tracedStructures.Add(structure.Id);
                nodeQueue.Enqueue(structure.Id);
            }

            var visitedNodes = new HashSet<int>(nodeQueue);

            while (nodeQueue.Count > 0)
            {
                int nid = nodeQueue.Dequeue();
                // Find all conduits that flow OUT OF this node (StartNodeId == nid)
                foreach (var c in conduits.Values.Where(cd => cd.StartNodeId == nid))
                {
                    tracedConduits.Add(c.Id);
                    if (visitedNodes.Add(c.StopNodeId))
                    {
                        tracedStructures.Add(c.StopNodeId);
                        nodeQueue.Enqueue(c.StopNodeId);
                    }
                }
            }

            Invalidate();
            TraceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearTrace()
        {
            tracedConduits.Clear();
            tracedStructures.Clear();
            Invalidate();
            TraceChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool FindElementAt(Point screenPt, out PlanCanvasElementKind kind, out int id)
        {
            kind = PlanCanvasElementKind.None;
            id = 0;

            // 1. Check Structures first (circular hit region, radius 12px)
            foreach (var s in structures.Values)
            {
                var spt = WorldToScreen(s.Location);
                float distSq = (spt.X - screenPt.X) * (spt.X - screenPt.X) + (spt.Y - screenPt.Y) * (spt.Y - screenPt.Y);
                if (distSq <= 144f) // 12px radius
                {
                    kind = PlanCanvasElementKind.Structure;
                    id = s.Id;
                    return true;
                }
            }

            // 2. Check Conduits (distance to segment < 8px)
            foreach (var c in conduits.Values)
            {
                var pts = c.AllPoints().Select(p => WorldToScreen(p)).ToList();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (DistanceToLineSegment(screenPt, pts[i], pts[i + 1]) <= 8.0)
                    {
                        kind = PlanCanvasElementKind.Conduit;
                        id = c.Id;
                        return true;
                    }
                }
            }

            return false;
        }

        private static double DistanceToLineSegment(Point p, PointF v, PointF w)
        {
            double l2 = (w.X - v.X) * (w.X - v.X) + (w.Y - v.Y) * (w.Y - v.Y);
            if (l2 < 0.0001) return Math.Sqrt((p.X - v.X) * (p.X - v.X) + (p.Y - v.Y) * (p.Y - v.Y));
            double t = Math.Max(0.0, Math.Min(1.0, ((p.X - v.X) * (w.X - v.X) + (p.Y - v.Y) * (w.Y - v.Y)) / l2));
            double projX = v.X + t * (w.X - v.X);
            double projY = v.Y + t * (w.Y - v.Y);
            return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && ModifierKeys == Keys.Control))
            {
                isPanning = true;
                panStart = e.Location;
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (FindElementAt(e.Location, out var kind, out int id))
                {
                    SelectElement(kind, id);
                }
                else
                {
                    // Start pan if clicked empty space
                    isPanning = true;
                    panStart = e.Location;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMousePos = e.Location;

            if (isPanning)
            {
                panOffset.X += (e.X - panStart.X);
                panOffset.Y += (e.Y - panStart.Y);
                panStart = e.Location;
                Invalidate();
                return;
            }

            // Hit test for hover HUD
            if (FindElementAt(e.Location, out var hKind, out int hId))
            {
                if (hoveredKind != hKind || hoveredId != hId)
                {
                    hoveredKind = hKind;
                    hoveredId = hId;
                    Invalidate();
                }
            }
            else if (hoveredKind != PlanCanvasElementKind.None)
            {
                hoveredKind = PlanCanvasElementKind.None;
                hoveredId = 0;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isPanning = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float factor = e.Delta > 0 ? 1.15f : 0.85f;
            var mouseWorld = ScreenToWorld(e.Location);

            zoomFactor = Math.Max(0.05f, Math.Min(50.0f, zoomFactor * factor));

            // Keep mouse point invariant
            panOffset.X = e.X - mouseWorld.X * zoomFactor;
            panOffset.Y = e.Y - mouseWorld.Y * zoomFactor;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawPlan(e.Graphics, ClientRectangle);
        }

        public Bitmap RenderToBitmap(int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var viewport = new Rectangle(0, 0, width, height);

                // Auto-frame in bitmap
                float oldZoom = zoomFactor;
                PointF oldPan = panOffset;

                if (!worldBounds.IsEmpty)
                {
                    float sx = (width - 40f) / worldBounds.Width;
                    float sy = (height - 40f) / worldBounds.Height;
                    zoomFactor = Math.Max(0.01f, Math.Min(sx, sy));

                    float centerX = (worldBounds.Left + worldBounds.Right) / 2f;
                    float centerY = (worldBounds.Top + worldBounds.Bottom) / 2f;

                    panOffset = new PointF(
                        width / 2f - centerX * zoomFactor,
                        height / 2f - centerY * zoomFactor
                    );
                }

                DrawPlan(g, viewport);

                zoomFactor = oldZoom;
                panOffset = oldPan;
            }
            return bmp;
        }

        private void DrawPlan(Graphics g, Rectangle viewport)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Background
            using (var bgBrush = new SolidBrush(BackColor))
                g.FillRectangle(bgBrush, viewport);

            // 2. CAD Coordinate Grid
            if (ShowGrid && viewport.Width > 100 && viewport.Height > 100)
            {
                DrawCoordinateGrid(g, viewport);
            }

            // 3. Conduits Layer
            DrawConduitsLayer(g, viewport);

            // 4. Structures Layer
            DrawStructuresLayer(g, viewport);

            // 5. Traced Path Glow Layer
            DrawTracedPathGlow(g, viewport);

            // 6. Selection Halos
            DrawSelectionOverlay(g, viewport);

            // 7. Violations Overlay Badges
            if (ShowViolations)
            {
                DrawViolationsBadges(g, viewport);
            }

            // 8. Hover HUD Inspection Tooltip
            DrawHoverHud(g, viewport);

            // 9. Canvas Chrome: Status, North Arrow & Legend
            DrawCanvasChrome(g, viewport);
        }

        private void DrawCoordinateGrid(Graphics g, Rectangle viewport)
        {
            using (var gridPen = new Pen(Color.FromArgb(34, 40, 52), 1.0f) { DashStyle = DashStyle.Dot })
            using (var font = new Font("Consolas", 7.5f))
            using (var textBrush = new SolidBrush(Color.FromArgb(70, 80, 100)))
            {
                int step = 60;
                for (int x = (int)(panOffset.X % step); x < viewport.Width; x += step)
                {
                    if (x < 0) continue;
                    g.DrawLine(gridPen, x, 0, x, viewport.Height);
                }
                for (int y = (int)(panOffset.Y % step); y < viewport.Height; y += step)
                {
                    if (y < 0) continue;
                    g.DrawLine(gridPen, 0, y, viewport.Width, y);
                }
            }
        }

        private void DrawConduitsLayer(Graphics g, Rectangle viewport)
        {
            using (var labelFont = new Font("Segoe UI", 7.5f))
            using (var textBrush = new SolidBrush(Color.FromArgb(190, 205, 225)))
            {
                foreach (var c in conduits.Values)
                {
                    var pts = c.AllPoints().Select(p => WorldToScreen(p)).ToArray();
                    if (pts.Length < 2) continue;

                    // Stroke width mapped from diameter (min 2.5px, max 8.0px)
                    float penWidth = (float)Math.Max(2.5, Math.Min(8.0, c.DiameterInches / 6.0));

                    Color strokeColor = Color.FromArgb(70, 140, 210); // Standard slate blue
                    if (c.HasViolations && ShowViolations) strokeColor = Color.FromArgb(235, 75, 75); // Warning Red
                    if (tracedConduits.Contains(c.Id)) strokeColor = Color.FromArgb(0, 240, 220); // Traced Cyan

                    using (var pen = new Pen(strokeColor, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLines(pen, pts);
                    }

                    // Flow Arrow
                    if (ShowFlowArrows && pts.Length >= 2)
                    {
                        int midIdx = pts.Length / 2;
                        PointF p1 = pts[midIdx - 1];
                        PointF p2 = pts[midIdx];
                        DrawArrowHead(g, p1, p2, strokeColor);
                    }

                    // Annotations
                    if (ShowLabels && pts.Length >= 2)
                    {
                        PointF midPt = new PointF((pts[0].X + pts[pts.Length - 1].X) / 2f, (pts[0].Y + pts[pts.Length - 1].Y) / 2f);
                        string annotation = string.Format(CultureInfo.InvariantCulture, "{0} [{1:F0}\"]", c.Label, c.DiameterInches);
                        var sz = g.MeasureString(annotation, labelFont);
                        g.DrawString(annotation, labelFont, textBrush, midPt.X - sz.Width / 2f, midPt.Y - sz.Height - 3f);
                    }
                }
            }
        }

        private void DrawArrowHead(Graphics g, PointF from, PointF to, Color color)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 12f) return;

            float midX = (from.X + to.X) / 2f;
            float midY = (from.Y + to.Y) / 2f;

            float uX = dx / len;
            float uY = dy / len;

            float arrowLen = 10f;
            float arrowWidth = 5f;

            PointF tip = new PointF(midX + uX * arrowLen / 2f, midY + uY * arrowLen / 2f);
            PointF left = new PointF(tip.X - uX * arrowLen + uY * arrowWidth, tip.Y - uY * arrowLen - uX * arrowWidth);
            PointF right = new PointF(tip.X - uX * arrowLen - uY * arrowWidth, tip.Y - uY * arrowLen + uX * arrowWidth);

            using (var brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, new[] { tip, left, right });
            }
        }

        private void DrawStructuresLayer(Graphics g, Rectangle viewport)
        {
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 6.8f))
            using (var textBrush = new SolidBrush(Color.FromArgb(230, 235, 245)))
            using (var dimBrush = new SolidBrush(Color.FromArgb(150, 165, 185)))
            {
                foreach (var s in structures.Values)
                {
                    PointF pt = WorldToScreen(s.Location);

                    // Skip offscreen
                    if (pt.X < -50 || pt.X > viewport.Width + 50 || pt.Y < -50 || pt.Y > viewport.Height + 50)
                        continue;

                    if (s.IsOutfall)
                    {
                        // Inverted Triangle for Outfall
                        float side = 18f;
                        PointF p1 = new PointF(pt.X, pt.Y + side * 0.6f);
                        PointF p2 = new PointF(pt.X - side * 0.55f, pt.Y - side * 0.45f);
                        PointF p3 = new PointF(pt.X + side * 0.55f, pt.Y - side * 0.45f);

                        using (var brush = new SolidBrush(Color.FromArgb(25, 110, 75)))
                        using (var pen = new Pen(Color.FromArgb(50, 220, 130), 2.0f))
                        {
                            g.FillPolygon(brush, new[] { p1, p2, p3 });
                            g.DrawPolygon(pen, new[] { p1, p2, p3 });
                        }
                    }
                    else if (s.Category.IndexOf("Inlet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             s.Category.IndexOf("Catch", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Square for Catch Basin / Inlet
                        float r = 7f;
                        var rect = new RectangleF(pt.X - r, pt.Y - r, r * 2, r * 2);

                        using (var brush = new SolidBrush(Color.FromArgb(35, 75, 120)))
                        using (var pen = new Pen(Color.FromArgb(80, 180, 255), 2.0f))
                        {
                            g.FillRectangle(brush, rect);
                            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                        }
                    }
                    else
                    {
                        // Circle for Standard Manhole
                        float r = 7.5f;
                        var rect = new RectangleF(pt.X - r, pt.Y - r, r * 2, r * 2);

                        using (var brush = new SolidBrush(Color.FromArgb(45, 52, 68)))
                        using (var pen = new Pen(Color.FromArgb(200, 210, 225), 2.0f))
                        {
                            g.FillEllipse(brush, rect);
                            g.DrawEllipse(pen, rect);
                        }
                    }

                    // Structure Annotation
                    if (ShowLabels)
                    {
                        g.DrawString(s.Label, font, textBrush, pt.X + 11f, pt.Y - 10f);
                        if (s.RimElevationFeet.HasValue)
                        {
                            string elevStr = string.Format(CultureInfo.InvariantCulture, "Rim: {0:F1} ft", s.RimElevationFeet.Value);
                            g.DrawString(elevStr, subFont, dimBrush, pt.X + 11f, pt.Y + 2f);
                        }
                    }
                }
            }
        }

        private void DrawTracedPathGlow(Graphics g, Rectangle viewport)
        {
            if (tracedConduits.Count == 0 && tracedStructures.Count == 0) return;

            // Glow pen for conduits
            using (var glowPen = new Pen(Color.FromArgb(90, 0, 255, 230), 10.0f) { LineJoin = LineJoin.Round })
            {
                foreach (int cid in tracedConduits)
                {
                    if (conduits.TryGetValue(cid, out var c))
                    {
                        var pts = c.AllPoints().Select(p => WorldToScreen(p)).ToArray();
                        if (pts.Length >= 2) g.DrawLines(glowPen, pts);
                    }
                }
            }

            // Glow for traced structures
            using (var brush = new SolidBrush(Color.FromArgb(80, 0, 255, 230)))
            {
                foreach (int sid in tracedStructures)
                {
                    if (structures.TryGetValue(sid, out var s))
                    {
                        var pt = WorldToScreen(s.Location);
                        g.FillEllipse(brush, pt.X - 14f, pt.Y - 14f, 28f, 28f);
                    }
                }
            }
        }

        private void DrawSelectionOverlay(Graphics g, Rectangle viewport)
        {
            if (selectedKind == PlanCanvasElementKind.Conduit && conduits.TryGetValue(selectedId, out var c))
            {
                var pts = c.AllPoints().Select(p => WorldToScreen(p)).ToArray();
                if (pts.Length >= 2)
                {
                    using (var haloPen = new Pen(Color.FromArgb(200, 255, 215, 0), 9.0f) { LineJoin = LineJoin.Round })
                    using (var corePen = new Pen(Color.White, 3.0f))
                    {
                        g.DrawLines(haloPen, pts);
                        g.DrawLines(corePen, pts);
                    }
                }
            }
            else if (selectedKind == PlanCanvasElementKind.Structure && structures.TryGetValue(selectedId, out var s))
            {
                var pt = WorldToScreen(s.Location);
                using (var haloPen = new Pen(Color.FromArgb(255, 215, 0), 3.0f))
                {
                    g.DrawEllipse(haloPen, pt.X - 13f, pt.Y - 13f, 26f, 26f);
                }
            }
        }

        private void DrawViolationsBadges(Graphics g, Rectangle viewport)
        {
            using (var badgePen = new Pen(Color.FromArgb(255, 60, 60), 2.5f))
            using (var badgeBrush = new SolidBrush(Color.FromArgb(220, 20, 20)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Segoe UI", 7.0f, FontStyle.Bold))
            {
                foreach (var s in structures.Values.Where(item => item.HasViolations))
                {
                    var pt = WorldToScreen(s.Location);
                    // Draw red alert ring
                    g.DrawEllipse(badgePen, pt.X - 11f, pt.Y - 11f, 22f, 22f);
                }

                foreach (var c in conduits.Values.Where(item => item.HasViolations))
                {
                    var pts = c.AllPoints().Select(p => WorldToScreen(p)).ToArray();
                    if (pts.Length >= 2)
                    {
                        PointF midPt = new PointF((pts[0].X + pts[pts.Length - 1].X) / 2f, (pts[0].Y + pts[pts.Length - 1].Y) / 2f);
                        // Warning badge triangle
                        PointF t1 = new PointF(midPt.X, midPt.Y - 8f);
                        PointF t2 = new PointF(midPt.X - 7f, midPt.Y + 6f);
                        PointF t3 = new PointF(midPt.X + 7f, midPt.Y + 6f);
                        g.FillPolygon(badgeBrush, new[] { t1, t2, t3 });
                        g.DrawString("!", font, textBrush, midPt.X - 3.5f, midPt.Y - 6f);
                    }
                }
            }
        }

        private void DrawHoverHud(Graphics g, Rectangle viewport)
        {
            if (hoveredKind == PlanCanvasElementKind.None) return;

            var lines = new List<string>();
            string title = "";

            if (hoveredKind == PlanCanvasElementKind.Conduit && conduits.TryGetValue(hoveredId, out var c))
            {
                title = string.Format(CultureInfo.InvariantCulture, "CONDUIT: {0} (ID {1})", c.Label, c.Id);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Span: {0} -> {1}", c.StartNodeId, c.StopNodeId));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Size: {0:F0}\" | Length: {1:F1} ft | Slope: {2:P2}", c.DiameterInches, c.LengthFeet, c.Slope));
                if (c.FlowCfs.HasValue) lines.Add(string.Format(CultureInfo.InvariantCulture, "Flow: {0:F2} cfs | Vel: {1:F2} fps", c.FlowCfs.Value, c.VelocityFps.GetValueOrDefault(0.0)));
                if (c.HasViolations) lines.Add(string.Format(CultureInfo.InvariantCulture, "VIOLATIONS: {0}", string.Join("; ", c.Violations)));
                if (c.P25DecisionCount > 0) lines.Add(string.Format(CultureInfo.InvariantCulture, "P25 DECISIONS LINKED: {0}", c.P25DecisionCount));
            }
            else if (hoveredKind == PlanCanvasElementKind.Structure && structures.TryGetValue(hoveredId, out var s))
            {
                title = string.Format(CultureInfo.InvariantCulture, "STRUCTURE: {0} (ID {1})", s.Label, s.Id);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Type: {0} | Outfall: {1}", s.Category, s.IsOutfall ? "Yes" : "No"));
                if (s.RimElevationFeet.HasValue) lines.Add(string.Format(CultureInfo.InvariantCulture, "Rim: {0:F2} ft | Invert: {1:F2} ft", s.RimElevationFeet.Value, s.InvertElevationFeet.GetValueOrDefault(0.0)));
                if (s.HglFeet.HasValue) lines.Add(string.Format(CultureInfo.InvariantCulture, "HGL: {0:F2} ft | Freeboard: {1:F2} ft", s.HglFeet.Value, s.FreeboardFeet.GetValueOrDefault(0.0)));
                if (s.HasViolations) lines.Add(string.Format(CultureInfo.InvariantCulture, "VIOLATIONS: {0}", string.Join("; ", s.Violations)));
                if (s.P25DecisionCount > 0) lines.Add(string.Format(CultureInfo.InvariantCulture, "P25 DECISIONS LINKED: {0}", s.P25DecisionCount));
            }

            if (lines.Count == 0) return;

            using (var titleFont = new Font("Segoe UI", 8.2f, FontStyle.Bold))
            using (var bodyFont = new Font("Segoe UI", 7.5f))
            {
                float w = 240f;
                float h = 26f + lines.Count * 16f;

                float x = lastMousePos.X + 16f;
                float y = lastMousePos.Y + 16f;

                if (x + w > viewport.Right - 10) x = lastMousePos.X - w - 10;
                if (y + h > viewport.Bottom - 10) y = lastMousePos.Y - h - 10;

                var cardRect = new RectangleF(x, y, w, h);

                using (var bgBrush = new SolidBrush(Color.FromArgb(235, 18, 22, 30)))
                using (var borderPen = new Pen(Color.FromArgb(65, 150, 230), 1.2f))
                using (var titleBrush = new SolidBrush(Color.FromArgb(100, 200, 255)))
                using (var bodyBrush = new SolidBrush(Color.FromArgb(220, 230, 245)))
                {
                    g.FillRectangle(bgBrush, cardRect);
                    g.DrawRectangle(borderPen, cardRect.X, cardRect.Y, cardRect.Width, cardRect.Height);

                    g.DrawString(title, titleFont, titleBrush, x + 8f, y + 6f);
                    float curY = y + 24f;
                    foreach (var line in lines)
                    {
                        var brush = line.StartsWith("VIOLATIONS", StringComparison.Ordinal) ? Brushes.Tomato : bodyBrush;
                        g.DrawString(line, bodyFont, brush, x + 8f, curY);
                        curY += 16f;
                    }
                }
            }
        }

        private void DrawCanvasChrome(Graphics g, Rectangle viewport)
        {
            // North Arrow & Coordinate Indicator in Bottom-Left
            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(170, 185, 205)))
            using (var pen = new Pen(Color.FromArgb(170, 185, 205), 1.5f))
            {
                float nx = 35f;
                float ny = viewport.Bottom - 45f;

                // North Arrow
                g.DrawLine(pen, nx, ny + 15f, nx, ny - 15f);
                g.DrawLine(pen, nx, ny - 15f, nx - 5f, ny - 7f);
                g.DrawLine(pen, nx, ny - 15f, nx + 5f, ny - 7f);
                g.DrawString("N", font, brush, nx - 5f, ny - 30f);

                // Stats text
                string stats = string.Format(CultureInfo.InvariantCulture,
                    "Elements: {0} conduits / {1} structures | Zoom: {2:P0} | Traced: {3}",
                    conduits.Count, structures.Count, zoomFactor,
                    (tracedConduits.Count > 0 || tracedStructures.Count > 0) ? tracedConduits.Count + " links" : "None");
                g.DrawString(stats, font, brush, 65f, viewport.Bottom - 25f);
            }
        }
    }
}
