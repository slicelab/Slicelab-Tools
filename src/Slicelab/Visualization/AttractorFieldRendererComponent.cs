using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.Visualization
{
    public class AttractorFieldRendererComponent : GH_Component
    {
        private List<Line> _previewSegments = new List<Line>();
        private List<Color> _segmentColors = new List<Color>();
        private List<int> _segmentThicknesses = new List<int>();
        private List<Point3d> _outputPoints = new List<Point3d>();
        private List<double> _outputRadii = new List<double>();
        private List<Color> _outputColors = new List<Color>();
        private BoundingBox _bbox = BoundingBox.Empty;
        private bool _isSolved;
        private bool _show = true;

        private static readonly Color DefaultColorA = Color.FromArgb(255, 30, 80, 200);
        private static readonly Color DefaultColorB = Color.FromArgb(255, 255, 100, 20);

        private const int SoftWarningCount = 50000;
        private const int HardCapCount = 300000;

        public AttractorFieldRendererComponent()
            : base("Attractor Field Renderer", "SLAFR",
                "Renders an attractor-driven radius field across a curve network. Outputs adaptive points and radii.",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("F1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B03");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-AttractorField.png");
        public override bool IsPreviewCapable => true;
        public override BoundingBox ClippingBox => _bbox.IsValid ? _bbox : BoundingBox.Empty;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Strut network curves", GH_ParamAccess.list);
            pManager.AddPointParameter("Attractors", "A", "One or more attractor points", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Radius", "MinR", "Minimum radius at far field", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Max Radius", "MaxR", "Maximum radius at attractor", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Falloff", "Fall", "Distance falloff exponent. Higher = sharper transition", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Spacing", "Sp", "Point spacing as multiple of local radius (0.8–1.4)", GH_ParamAccess.item, 1.2);
            pManager.AddBooleanParameter("Mode", "Mode", "True = Attract (thick near attractor). False = Repel (thick far from attractor)", GH_ParamAccess.item, true);
            pManager.AddColourParameter("Color A", "CA", "Color at minimum radius", GH_ParamAccess.item, DefaultColorA);
            pManager.AddColourParameter("Color B", "CB", "Color at maximum radius", GH_ParamAccess.item, DefaultColorB);
            pManager.AddNumberParameter("Display Scale", "DS", "Multiplier to convert radius to pixel thickness (e.g. 10 makes 1.2mm show as 12px)", GH_ParamAccess.item, 10.0);
            pManager.AddBooleanParameter("Show", "Show", "Toggle viewport preview", GH_ParamAccess.item, true);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "Pts", "Adaptive point list for Dendro point-to-volume", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radii", "R", "Matching radius per point", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "Col", "Matching color per point", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Point Count", "N", "Total point count", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Radius Out", "MinR", "Actual minimum radius in field", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Radius Out", "MaxR", "Actual maximum radius in field", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _isSolved = false;
            _previewSegments.Clear();
            _segmentColors.Clear();
            _segmentThicknesses.Clear();
            _outputPoints.Clear();
            _outputRadii.Clear();
            _outputColors.Clear();
            _bbox = BoundingBox.Empty;

            var curves = new List<Curve>();
            var attractors = new List<Point3d>();
            double minRadius = 0.5;
            double maxRadius = 2.0;
            double falloff = 1.0;
            double spacing = 1.2;
            bool attractMode = true;
            var colorA = DefaultColorA;
            var colorB = DefaultColorB;
            double displayScale = 10.0;
            bool show = true;

            if (!DA.GetDataList(0, curves)) return;
            if (!DA.GetDataList(1, attractors)) return;
            DA.GetData(2, ref minRadius);
            DA.GetData(3, ref maxRadius);
            DA.GetData(4, ref falloff);
            DA.GetData(5, ref spacing);
            DA.GetData(6, ref attractMode);
            DA.GetData(7, ref colorA);
            DA.GetData(8, ref colorB);
            DA.GetData(9, ref displayScale);
            DA.GetData(10, ref show);

            if (displayScale < 0.1) displayScale = 0.1;

            _show = show;

            if (curves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No curves connected");
                return;
            }
            if (attractors.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No attractor points connected");
                return;
            }
            if (minRadius <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Min radius must be greater than 0");
                return;
            }
            if (minRadius >= maxRadius)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Min radius must be less than max radius");
                return;
            }

            // Clamp spacing
            spacing = Math.Max(0.5, Math.Min(1.5, spacing));
            if (spacing < 0.8)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Very dense point sampling — will be slow");
            else if (spacing > 1.4)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Spacing too large — Dendro may show gaps");

            double radiusRange = maxRadius - minRadius;
            bool hitCap = false;

            Message = "Computing field...";

            // Pre-scan: find max nearest-attractor distance across all curves
            // so we can normalize distances to span the full 0→1 range
            double maxNearestDist = 0;
            foreach (var curve in curves)
            {
                if (curve == null) continue;
                // Sample start, mid, end of each curve
                var pts = new[] { curve.PointAtStart, curve.PointAt(curve.Domain.Mid), curve.PointAtEnd };
                foreach (var pt in pts)
                {
                    double nearest = double.MaxValue;
                    foreach (var att in attractors)
                        nearest = Math.Min(nearest, pt.DistanceTo(att));
                    if (nearest < double.MaxValue)
                        maxNearestDist = Math.Max(maxNearestDist, nearest);
                }
            }
            if (maxNearestDist < 1e-10) maxNearestDist = 1.0; // avoid div-by-zero

            for (int ci = 0; ci < curves.Count; ci++)
            {
                if (hitCap) break;

                var curve = curves[ci];
                if (curve == null) continue;

                // Check escape key
                if (GH_Document.IsEscapeKeyDown())
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cancelled by user");
                    Message = "Cancelled";
                    return;
                }

                _bbox.Union(curve.GetBoundingBox(false));

                double curveLength = curve.GetLength();
                if (curveLength < 1e-10) continue;

                double tParam = curve.Domain.Min;
                var prevPoint = curve.PointAt(tParam);

                // Emit start point
                double startRadius = GetFieldRadius(prevPoint, attractors, falloff, minRadius, maxRadius, attractMode, maxNearestDist);
                double startT = (startRadius - minRadius) / radiusRange;
                _outputPoints.Add(prevPoint);
                _outputRadii.Add(startRadius);
                _outputColors.Add(LerpColor(colorA, colorB, startT));

                // Walk along curve with adaptive arc-length steps
                double walkedLength = 0;

                while (tParam < curve.Domain.Max)
                {
                    var worldPos = curve.PointAt(tParam);
                    double radius = GetFieldRadius(worldPos, attractors, falloff, minRadius, maxRadius, attractMode, maxNearestDist);
                    double stepDistance = radius * spacing;

                    walkedLength += stepDistance;

                    if (!curve.LengthParameter(walkedLength, out double nextT))
                        break;

                    if (nextT >= curve.Domain.Max)
                        break;

                    var nextPoint = curve.PointAt(nextT);
                    double nextRadius = GetFieldRadius(nextPoint, attractors, falloff, minRadius, maxRadius, attractMode, maxNearestDist);
                    double colorT = (nextRadius - minRadius) / radiusRange;
                    var color = LerpColor(colorA, colorB, colorT);
                    int thickness = (int)Math.Round(nextRadius * displayScale);

                    // Store output point
                    _outputPoints.Add(nextPoint);
                    _outputRadii.Add(nextRadius);
                    _outputColors.Add(color);

                    // Store preview segment
                    _previewSegments.Add(new Line(prevPoint, nextPoint));
                    _segmentColors.Add(color);
                    _segmentThicknesses.Add(Math.Max(1, thickness));

                    prevPoint = nextPoint;
                    tParam = nextT;

                    // Hard cap check
                    if (_outputPoints.Count >= HardCapCount)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Point cap reached at {HardCapCount}. Increase spacing factor.");
                        hitCap = true;
                        break;
                    }
                }

                // Always emit curve endpoint
                if (!hitCap)
                {
                    var endPoint = curve.PointAtEnd;
                    double endRadius = GetFieldRadius(endPoint, attractors, falloff, minRadius, maxRadius, attractMode, maxNearestDist);
                    double endT = (endRadius - minRadius) / radiusRange;

                    _outputPoints.Add(endPoint);
                    _outputRadii.Add(endRadius);
                    _outputColors.Add(LerpColor(colorA, colorB, endT));

                    // Final segment
                    _previewSegments.Add(new Line(prevPoint, endPoint));
                    _segmentColors.Add(LerpColor(colorA, colorB, endT));
                    _segmentThicknesses.Add(Math.Max(1, (int)Math.Round(endRadius * displayScale)));
                }
            }

            // Soft warning
            if (_outputPoints.Count > SoftWarningCount)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{_outputPoints.Count} points — Dendro solve will be heavy. Consider increasing spacing factor.");

            // Set outputs
            DA.SetDataList(0, _outputPoints);
            DA.SetDataList(1, _outputRadii);
            DA.SetDataList(2, _outputColors);
            DA.SetData(3, _outputPoints.Count);

            if (_outputRadii.Count > 0)
            {
                DA.SetData(4, _outputRadii.Min());
                DA.SetData(5, _outputRadii.Max());
            }

            _isSolved = true;
            Message = _outputRadii.Count > 0
                ? $"{_outputPoints.Count} pts | {_outputRadii.Min():F2}→{_outputRadii.Max():F2}"
                : $"{_outputPoints.Count} pts";
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!_isSolved || !_show || _previewSegments.Count == 0) return;

            for (int i = 0; i < _previewSegments.Count; i++)
            {
                args.Display.DrawLine(
                    _previewSegments[i],
                    _segmentColors[i],
                    _segmentThicknesses[i]);
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // Intentionally empty — suppress default GH mesh preview
        }

        private static double GetFieldRadius(
            Point3d worldPoint,
            List<Point3d> attractors,
            double falloff,
            double minRadius,
            double maxRadius,
            bool attractMode,
            double maxNearestDist)
        {
            double minDist = double.MaxValue;
            foreach (var attractor in attractors)
                minDist = Math.Min(minDist, worldPoint.DistanceTo(attractor));

            // Normalize distance to 0→1 range based on geometry extent,
            // then apply falloff exponent for curve shape control
            double normalized = minDist / maxNearestDist;
            double t = 1.0 - Math.Pow(Math.Min(1.0, normalized), falloff);
            if (!attractMode) t = 1.0 - t;

            return minRadius + (maxRadius - minRadius) * t;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            return Color.FromArgb(
                255,
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
