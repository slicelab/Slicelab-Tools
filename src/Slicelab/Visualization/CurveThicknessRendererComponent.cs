using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Slicelab.Visualization
{
    public class CurveThicknessRendererComponent : GH_Component
    {
        private List<Curve> _curves = new List<Curve>();
        private List<Color> _colors = new List<Color>();
        private List<int> _thicknesses = new List<int>();
        private BoundingBox _bbox = BoundingBox.Empty;
        private bool _show = true;
        private bool _roundCaps = true;

        private static readonly Color DefaultColorA = Color.FromArgb(255, 30, 80, 200);
        private static readonly Color DefaultColorB = Color.FromArgb(255, 255, 100, 20);

        public CurveThicknessRendererComponent()
            : base("Curve Renderer", "SLCViz",
                "Renders curves as colored thick lines with one radius per curve, driven by attractor-point distance at the curve midpoint.",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("F1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B01");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-CurveViz.png");
        public override bool IsPreviewCapable => true;
        public override BoundingBox ClippingBox => _bbox.IsValid ? _bbox : BoundingBox.Empty;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to render", GH_ParamAccess.list);                                          // 0
            pManager.AddPointParameter("Attractors", "A", "Attractor points — radius computed from midpoint distance", GH_ParamAccess.list); // 1
            pManager.AddNumberParameter("Min Radius", "MinR", "Minimum radius at far field", GH_ParamAccess.item, 0.5);                   // 2
            pManager.AddNumberParameter("Max Radius", "MaxR", "Maximum radius at attractor", GH_ParamAccess.item, 2.0);                   // 3
            pManager.AddNumberParameter("Falloff", "Fall", "Distance falloff exponent. Higher = sharper transition", GH_ParamAccess.item, 1.0); // 4
            pManager.AddColourParameter("Color A", "CA", "Gradient start color (min radius)", GH_ParamAccess.item, DefaultColorA);        // 5
            pManager.AddColourParameter("Color B", "CB", "Gradient end color (max radius)", GH_ParamAccess.item, DefaultColorB);          // 6
            pManager.AddNumberParameter("Display Scale", "DS", "Multiplier: radius × scale = pixel thickness", GH_ParamAccess.item, 10.0); // 7
            pManager.AddBooleanParameter("Round Caps", "RC", "Draw round end caps on curves", GH_ParamAccess.item, true);                 // 8
            pManager.AddBooleanParameter("Show", "Show", "Toggle display on/off", GH_ParamAccess.item, true);                             // 9
            for (int i = 2; i <= 9; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Min Radius", "Min", "Actual minimum radius across curves", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Radius", "Max", "Actual maximum radius across curves", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radii", "R", "Computed radius per curve", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "Col", "Computed color per curve", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _curves.Clear();
            _colors.Clear();
            _thicknesses.Clear();
            _bbox = BoundingBox.Empty;

            var curves = new List<Curve>();
            var attractors = new List<Point3d>();
            double minRadius = 0.5;
            double maxRadius = 2.0;
            double falloff = 1.0;
            var colorA = DefaultColorA;
            var colorB = DefaultColorB;
            double displayScale = 10.0;
            bool roundCaps = true;
            bool show = true;

            if (!DA.GetDataList(0, curves)) return;
            if (!DA.GetDataList(1, attractors)) return;
            DA.GetData(2, ref minRadius);
            DA.GetData(3, ref maxRadius);
            DA.GetData(4, ref falloff);
            DA.GetData(5, ref colorA);
            DA.GetData(6, ref colorB);
            DA.GetData(7, ref displayScale);
            DA.GetData(8, ref roundCaps);
            DA.GetData(9, ref show);

            if (displayScale < 0.1) displayScale = 0.1;
            _show = show;
            _roundCaps = roundCaps;

            if (!show) return;

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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Min radius must be > 0");
                return;
            }
            if (minRadius >= maxRadius)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Min radius must be < max radius");
                return;
            }

            double radiusRange = maxRadius - minRadius;

            // Pre-scan: find max nearest-attractor distance for normalization
            double maxNearestDist = 0;
            foreach (var curve in curves)
            {
                if (curve == null) continue;
                var mid = curve.PointAt(curve.Domain.Mid);
                double nearest = double.MaxValue;
                foreach (var att in attractors)
                    nearest = Math.Min(nearest, mid.DistanceTo(att));
                if (nearest < double.MaxValue)
                    maxNearestDist = Math.Max(maxNearestDist, nearest);
            }
            if (maxNearestDist < 1e-10) maxNearestDist = 1.0;

            var radii = new List<double>();

            for (int i = 0; i < curves.Count; i++)
            {
                if (curves[i] == null) continue;

                var mid = curves[i].PointAt(curves[i].Domain.Mid);
                double radius = GetFieldRadius(mid, attractors, falloff, minRadius, maxRadius, maxNearestDist);

                double t = (radius - minRadius) / radiusRange;
                var color = LerpColor(colorA, colorB, t);
                int thickness = Math.Max(1, (int)Math.Round(radius * displayScale));

                _curves.Add(curves[i]);
                _colors.Add(color);
                _thicknesses.Add(thickness);
                radii.Add(radius);
                _bbox.Union(curves[i].GetBoundingBox(false));
            }

            if (radii.Count > 0)
            {
                DA.SetData(0, radii.Min());
                DA.SetData(1, radii.Max());
                Message = $"{_curves.Count} curves | {radii.Min():F2}→{radii.Max():F2}";
            }
            DA.SetDataList(2, radii);
            DA.SetDataList(3, _colors);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!_show || _curves.Count == 0) return;

            for (int i = 0; i < _curves.Count; i++)
            {
                if (_curves[i] == null) continue;
                args.Display.DrawCurve(_curves[i], _colors[i], _thicknesses[i]);

                if (_roundCaps)
                {
                    float capRadius = Math.Max(1f, _thicknesses[i] / 2f);
                    args.Display.DrawPoint(
                        _curves[i].PointAtStart,
                        PointStyle.RoundSimple,
                        capRadius,
                        _colors[i]);
                    args.Display.DrawPoint(
                        _curves[i].PointAtEnd,
                        PointStyle.RoundSimple,
                        capRadius,
                        _colors[i]);
                }
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
            double maxNearestDist)
        {
            double minDist = double.MaxValue;
            foreach (var attractor in attractors)
                minDist = Math.Min(minDist, worldPoint.DistanceTo(attractor));

            double normalized = minDist / maxNearestDist;
            double t = 1.0 - Math.Pow(Math.Min(1.0, normalized), falloff);

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
