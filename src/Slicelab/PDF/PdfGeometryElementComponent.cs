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
                "Create a geometry element for PDF layout from flat 2D artwork. Accepts closed planar curves, surfaces, and breps. Use PDF Make2D for 3D geometry.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C04");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PGeo.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Closed planar curves, surfaces, or breps (DataTree: one branch = one shape group). Must be flat in XY. Use PDF Make2D for 3D geometry.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Height", "H", "Fixed height in points (0 = auto-scale to column width)", GH_ParamAccess.item, 0);
            pManager.AddColourParameter("Fill Color", "FC", "Fill color per branch", GH_ParamAccess.tree);
            pManager.AddColourParameter("Stroke Color", "SC", "Stroke color per branch (optional)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Stroke Weight", "SW", "Stroke weight per branch in points (optional)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);

            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF geometry element", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid curves found. Only closed planar curves, surfaces, and breps are accepted. Use PDF Make2D for 3D geometry.");
                return;
            }

            // Compute tight bbox
            var bbox = BoundingBox.Empty;
            foreach (var branch in curveTree.Branches)
            {
                foreach (var ghCrv in branch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsClosed || !crv.IsPlanar()) continue;
                    bbox.Union(crv.GetBoundingBox(true));
                }
            }

            if (!bbox.IsValid || bbox.Diagonal.X < 1e-10 || bbox.Diagonal.Y < 1e-10)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curves have zero or invalid bounding box.");
                return;
            }

            // Build GeoBranches with pre-converted polylines
            var branches = new List<GeoBranch>();
            for (int i = 0; i < curveTree.Branches.Count; i++)
            {
                var curveBranch = curveTree.Branches[i];
                var curvePath = curveTree.Paths[i];
                var polylines = new List<XPoint[]>();

                foreach (var ghCrv in curveBranch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsClosed || !crv.IsPlanar()) continue;

                    var pl = PdfLayoutHelper.CurveToPolyline(crv, tolerance);
                    if (pl == null) continue;
                    polylines.Add(PdfLayoutHelper.PolylineToXPoints(pl, bbox, unitScale));
                }

                if (polylines.Count == 0) continue;

                Color fill = GetColorFromTree(fillTree, curvePath, i, Color.Black);
                Color? stroke = null;
                if (strokeTree != null && strokeTree.DataCount > 0)
                    stroke = GetColorFromTree(strokeTree, curvePath, i, Color.Black);
                double weight = 0;
                if (weightTree != null && weightTree.DataCount > 0)
                    weight = GetNumberFromTree(weightTree, curvePath, i, 0);

                var gb = new GeoBranch
                {
                    Polylines = polylines.ToArray(),
                    FillColor = XColor.FromArgb(fill.A, fill.R, fill.G, fill.B),
                    StrokeColor = stroke.HasValue ? (XColor?)XColor.FromArgb(stroke.Value.A, stroke.Value.R, stroke.Value.G, stroke.Value.B) : null,
                    StrokeWeight = weight
                };
                branches.Add(gb);
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

        private static Color GetColorFromTree(GH_Structure<GH_Colour> tree, GH_Path targetPath, int branchIndex, Color fallback)
        {
            if (tree == null || tree.DataCount == 0) return fallback;
            var list = tree[targetPath];
            if (list != null && list.Count > 0) return list[0].Value;
            if (branchIndex < tree.Branches.Count && tree.Branches[branchIndex].Count > 0)
                return tree.Branches[branchIndex][0].Value;
            if (tree.Branches.Count > 0 && tree.Branches[0].Count > 0)
                return tree.Branches[0][0].Value;
            return fallback;
        }

        private static double GetNumberFromTree(GH_Structure<GH_Number> tree, GH_Path targetPath, int branchIndex, double fallback)
        {
            if (tree == null || tree.DataCount == 0) return fallback;
            var list = tree[targetPath];
            if (list != null && list.Count > 0) return list[0].Value;
            if (branchIndex < tree.Branches.Count && tree.Branches[branchIndex].Count > 0)
                return tree.Branches[branchIndex][0].Value;
            if (tree.Branches.Count > 0 && tree.Branches[0].Count > 0)
                return tree.Branches[0][0].Value;
            return fallback;
        }
    }
}
