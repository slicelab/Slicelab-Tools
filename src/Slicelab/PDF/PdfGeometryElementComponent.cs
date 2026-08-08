using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using PdfSharpCore.Drawing;
using Rhino.Geometry;

namespace Slicelab.PDF
{
    public class PdfGeometryElementComponent : GH_Component
    {
        public PdfGeometryElementComponent()
            : base("PDF Flat Geometry", "SLPGeo",
                "Create a geometry element for PDF layout from flat 2D artwork. Accepts planar curves (open or closed), surfaces, and breps. Use PDF Make2D for 3D geometry.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C04");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PGeo.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Planar curves (open or closed), surfaces, or breps (DataTree: one branch = one shape group). Must be flat in XY. Use PDF Make2D for 3D geometry.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Height", "H", "Fixed height in points (0 = auto-scale to column width)", GH_ParamAccess.item, 0);
            pManager.AddColourParameter("Fill Color", "FC", "Fill color per branch (optional if a stroke is supplied)", GH_ParamAccess.tree);
            pManager.AddColourParameter("Stroke Color", "SC", "Stroke color per branch (optional). Supplied on its own, defaults to a 1pt weight.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Stroke Weight", "SW", "Stroke weight per branch in points (optional). Supplied on its own, defaults to a black stroke.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF geometry element", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = null;

            GH_Structure<IGH_GeometricGoo> geoTree = null;
            double fixedHeight = 0;
            GH_Structure<GH_Colour> fillTree = null;
            GH_Structure<GH_Colour> strokeTree = null;
            GH_Structure<GH_Number> weightTree = null;
            double spaceAfter = 6;

            if (!DA.GetDataTree(0, out geoTree)) return;
            DA.GetData(1, ref fixedHeight);
            DA.GetDataTree(2, out fillTree);
            DA.GetDataTree(3, out strokeTree);
            DA.GetDataTree(4, out weightTree);
            DA.GetData(5, ref spaceAfter);

            // Report the styling default before anything can return early, so the note under the
            // component reflects the wiring rather than whether the solve got as far as drawing.
            if (PdfLayoutHelper.NoStyleSupplied(fillTree, strokeTree, weightTree))
                Message = "default: 1pt black line";

            if (geoTree == null || geoTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No geometry provided.");
                return;
            }

            double unitScale = PdfLayoutHelper.GetUnitScale();
            double tolerance = PdfLayoutHelper.GetModelTolerance();

            // Check Z-height of all input geometry — reject 3D shapes
            var inputBBox = BoundingBox.Empty;
            foreach (var path in geoTree.Paths)
            {
                foreach (var goo in geoTree[path])
                {
                    if (goo == null) continue;
                    var geomBBox = goo.Boundingbox;
                    if (geomBBox.IsValid) inputBBox.Union(geomBBox);
                }
            }
            if (inputBBox.IsValid && inputBBox.Diagonal.Z > tolerance * 10)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"3D geometry detected (Z height: {inputBBox.Diagonal.Z:F3}). This component only accepts flat 2D geometry. Use PDF Make2D for 3D geometry.");
                return;
            }

            // Extract curves from flat geometry
            var curveTree = new GH_Structure<GH_Curve>();
            foreach (var path in geoTree.Paths)
            {
                var branch = geoTree[path];
                foreach (var goo in branch)
                {
                    if (goo == null) continue;
                    if (goo is GH_Curve ghCrv)
                        curveTree.Append(ghCrv, path);
                    else if (goo is GH_Surface ghSrf && ghSrf.Value != null)
                        PdfLayoutHelper.AppendBrepLoops(ghSrf.Value, curveTree, path);
                    else if (goo is GH_Brep ghBrep && ghBrep.Value != null)
                        PdfLayoutHelper.AppendBrepLoops(ghBrep.Value, curveTree, path);
                }
            }

            if (curveTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid curves found. Only planar curves, surfaces, and breps are accepted. Use PDF Make2D for 3D geometry.");
                return;
            }

            // Compute tight bbox
            var bbox = BoundingBox.Empty;
            foreach (var branch in curveTree.Branches)
            {
                foreach (var ghCrv in branch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsPlanar()) continue;
                    bbox.Union(crv.GetBoundingBox(true));
                }
            }

            bool flatX = bbox.Diagonal.X < 1e-10;
            bool flatY = bbox.Diagonal.Y < 1e-10;

            if (!bbox.IsValid || (flatX && flatY))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "All geometry collapses to a single point — there is nothing to draw.");
                return;
            }
            if (flatX)
            {
                // The element is scaled to fill the column width, so zero width has no meaningful
                // scale factor — a vertical line would be stretched to an unusable height.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "All geometry lies on a single vertical line, which has no width to scale to the column. Combine it with other geometry, or rotate it.");
                return;
            }
            if (flatY)
            {
                // A horizontal line is a legitimate element here — a rule. It has no geometric
                // height, but its stroke does, so pad the box by the heaviest stroke on offer.
                double padPoints = PdfLayoutHelper.MaxStrokeWeight(weightTree);
                double padModel = padPoints / unitScale;
                var min = bbox.Min;
                var max = bbox.Max;
                min.Y -= padModel / 2;
                max.Y += padModel / 2;
                bbox = new BoundingBox(min, max);
            }

            // Build GeoBranches with pre-converted polylines
            var branches = new List<GeoBranch>();
            bool usedDefaultStyle = false;
            int skipped = 0;
            for (int i = 0; i < curveTree.Branches.Count; i++)
            {
                var curveBranch = curveTree.Branches[i];
                var curvePath = curveTree.Paths[i];
                var polylines = new List<GeoPolyline>();

                foreach (var ghCrv in curveBranch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsPlanar()) { skipped++; continue; }

                    var pl = PdfLayoutHelper.CurveToPolyline(crv, tolerance, out bool closed);
                    if (pl == null) { skipped++; continue; }

                    polylines.Add(new GeoPolyline
                    {
                        Points = PdfLayoutHelper.PolylineToXPoints(pl, bbox, unitScale),
                        Closed = closed
                    });
                }

                if (polylines.Count == 0) continue;

                // Fill, stroke, or both — resolved by the shared rule in PdfLayoutHelper
                var style = PdfLayoutHelper.ResolveBranchStyle(fillTree, strokeTree, weightTree, curvePath, i);
                if (style.UsedDefault) usedDefaultStyle = true;

                branches.Add(new GeoBranch
                {
                    Polylines = polylines.ToArray(),
                    FillColor = style.Fill,
                    StrokeColor = style.Stroke,
                    StrokeWeight = style.StrokeWeight
                });
            }

            if (usedDefaultStyle)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No Fill Color, Stroke Color or Stroke Weight supplied — defaulted to a 1pt black line.");
            }

            if (skipped > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{skipped} curve(s) skipped — not planar, or too few points to draw.");
            }

            var element = new PdfGeometryElement
            {
                Branches = branches,
                BBoxWidth = bbox.Diagonal.X * unitScale,
                BBoxHeight = bbox.Diagonal.Y * unitScale,
                FixedHeight = fixedHeight,
                SpaceAfter = spaceAfter
            };

            DA.SetData(0, new GH_PdfElement(element));
        }
    }
}
