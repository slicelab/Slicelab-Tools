using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Rhino;
using Rhino.Geometry;
using Slicelab.PDF;

namespace Slicelab.PDF
{
    public class PdfExportComponent : GH_Component
    {
        public PdfExportComponent()
            : base("PDF Quick Export", "SLPdfQ",
                "Export closed planar curves to a PDF with fill and stroke colors. Page size is auto-computed from curve bounding box. Even-odd fill rule handles nested shapes.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("F5A6B7C8-D9E0-4F1A-2B3C-4D5E6F7A8B23");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Pdf.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Closed planar curves, surfaces, or breps (DataTree: one branch = one shape group). Breps/surfaces are converted to boundary curves automatically.", GH_ParamAccess.tree);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddColourParameter("Fill Color", "FC", "Fill color per branch (matched by tree path)", GH_ParamAccess.tree);
            pManager.AddColourParameter("Stroke Color", "SC", "Stroke color per branch (optional, matched by tree path)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Stroke Weight", "SW", "Stroke weight per branch in points (optional, matched by tree path)", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Save", "Sa", "Set to true to save PDF", GH_ParamAccess.item, false);

            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Full path to saved file", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Export info including page dimensions", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_GeometricGoo> geoTree = null;
            string fileName = "";
            string filePath = "";
            GH_Structure<GH_Colour> fillTree = null;
            GH_Structure<GH_Colour> strokeTree = null;
            GH_Structure<GH_Number> weightTree = null;
            bool save = false;

            if (!DA.GetDataTree(0, out geoTree)) return;
            if (!DA.GetData(1, ref fileName)) return;
            if (!DA.GetData(2, ref filePath)) return;
            DA.GetDataTree(3, out fillTree);
            DA.GetDataTree(4, out strokeTree);
            DA.GetDataTree(5, out weightTree);
            DA.GetData(6, ref save);

            if (!save)
            {
                DA.SetData(1, "Save toggle is false.");
                return;
            }

            if (geoTree == null || geoTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No geometry provided.");
                return;
            }
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File path and file name are required.");
                return;
            }

            // Convert geometry tree to curve tree (extracting boundary loops from Breps/Surfaces)
            var curveTree = new GH_Structure<GH_Curve>();
            foreach (var path in geoTree.Paths)
            {
                var branch = geoTree[path];
                foreach (var goo in branch)
                {
                    if (goo == null) continue;

                    if (goo is GH_Curve ghCrv)
                    {
                        curveTree.Append(ghCrv, path);
                    }
                    else if (goo is GH_Surface ghSrf)
                    {
                        if (ghSrf.Value != null)
                            PdfLayoutHelper.AppendBrepLoops(ghSrf.Value, curveTree, path);
                    }
                    else if (goo is GH_Brep ghBrep)
                    {
                        if (ghBrep.Value != null)
                            PdfLayoutHelper.AppendBrepLoops(ghBrep.Value, curveTree, path);
                    }
                }
            }

            if (curveTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid curves or boundary loops found.");
                return;
            }

            // Compute bounding box of all curves
            var bbox = BoundingBox.Empty;
            foreach (var branch in curveTree.Branches)
            {
                foreach (var ghCrv2 in branch)
                {
                    Curve crv = ghCrv2?.Value;
                    if (crv == null || !crv.IsClosed || !crv.IsPlanar()) continue;
                    bbox.Union(crv.GetBoundingBox(true));
                }
            }

            if (!bbox.IsValid || bbox.Diagonal.X < 1e-10 || bbox.Diagonal.Y < 1e-10)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curves have zero or invalid bounding box extent.");
                return;
            }

            // Convert bbox dimensions from model units to PDF points
            var doc = RhinoDoc.ActiveDoc;
            double modelToMm = RhinoMath.UnitScale(doc != null ? doc.ModelUnitSystem : UnitSystem.Millimeters, UnitSystem.Millimeters);
            double mmToPoints = 2.8346456693;
            double unitScale = modelToMm * mmToPoints;

            double pageWidth = bbox.Diagonal.X * unitScale;
            double pageHeight = bbox.Diagonal.Y * unitScale;

            // Create PDF
            var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(pageWidth);
            page.Height = XUnit.FromPoint(pageHeight);
            var gfx = XGraphics.FromPdfPage(page);

            for (int i = 0; i < curveTree.Branches.Count; i++)
            {
                var branch = curveTree.Branches[i];
                if (branch == null || branch.Count == 0) continue;

                GH_Path curvePath = curveTree.Paths[i];

                // Match fill color by tree path, fallback to sequential index, then default
                Color fill = GetColorFromTree(fillTree, curvePath, i, Color.Black);

                // Match stroke color by tree path (optional)
                Color? stroke = null;
                if (strokeTree != null && strokeTree.DataCount > 0)
                    stroke = GetColorFromTree(strokeTree, curvePath, i, Color.Black);

                // Match stroke weight by tree path (optional)
                double strokeWeight = 0;
                if (weightTree != null && weightTree.DataCount > 0)
                    strokeWeight = GetNumberFromTree(weightTree, curvePath, i, 0);

                // Build combined XGraphicsPath with StartFigure/CloseFigure for even-odd fill
                var path = new XGraphicsPath();
                path.FillMode = XFillMode.Alternate;

                foreach (var ghCrv in branch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsClosed || !crv.IsPlanar()) continue;

                    Polyline polyline;
                    if (!crv.TryGetPolyline(out polyline))
                    {
                        double tolerance = doc != null ? doc.ModelAbsoluteTolerance : 0.001;
                        PolylineCurve plc = crv.ToPolyline(tolerance, RhinoMath.ToRadians(1.0), 0, 0);
                        if (plc == null || !plc.TryGetPolyline(out polyline)) continue;
                    }

                    if (polyline.Count < 3) continue;

                    // Close if needed
                    if (polyline[0].DistanceTo(polyline[polyline.Count - 1]) > 1e-6)
                        polyline.Add(polyline[0]);

                    // Convert to PDF coordinates
                    var pdfPts = new XPoint[polyline.Count];
                    for (int j = 0; j < polyline.Count; j++)
                    {
                        pdfPts[j] = new XPoint(
                            (polyline[j].X - bbox.Min.X) * unitScale,
                            (bbox.Max.Y - polyline[j].Y) * unitScale);
                    }

                    path.StartFigure();
                    path.AddLines(pdfPts);
                    path.CloseFigure();
                }

                // Draw the path
                var xFill = new XSolidBrush(XColor.FromArgb(fill.A, fill.R, fill.G, fill.B));
                if (stroke.HasValue && strokeWeight > 0)
                {
                    var sc = stroke.Value;
                    var xPen = new XPen(XColor.FromArgb(sc.A, sc.R, sc.G, sc.B), strokeWeight);
                    gfx.DrawPath(xPen, xFill, path);
                }
                else
                {
                    gfx.DrawPath(xFill, path);
                }
            }

            // Save
            Directory.CreateDirectory(filePath);
            string fullPath = Path.Combine(filePath, fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? fileName : fileName + ".pdf");

            document.Save(fullPath);
            gfx.Dispose();

            DA.SetData(0, fullPath);
            DA.SetData(1, $"Saved PDF: {pageWidth:F1} x {pageHeight:F1} pt ({curveTree.Branches.Count} branches, {curveTree.DataCount} curves)");
        }

        /// <summary>
        /// Get a color from a tree, matching by path first, then falling back to sequential index, then first branch.
        /// </summary>
        private static Color GetColorFromTree(GH_Structure<GH_Colour> tree, GH_Path targetPath, int branchIndex, Color fallback)
        {
            if (tree == null || tree.DataCount == 0) return fallback;

            // Try exact path match
            var list = tree[targetPath];
            if (list != null && list.Count > 0) return list[0].Value;

            // Fallback to sequential index
            if (branchIndex < tree.Branches.Count && tree.Branches[branchIndex].Count > 0)
                return tree.Branches[branchIndex][0].Value;

            // Fallback to first branch
            if (tree.Branches.Count > 0 && tree.Branches[0].Count > 0)
                return tree.Branches[0][0].Value;

            return fallback;
        }

        /// <summary>
        /// Get a number from a tree, matching by path first, then falling back to sequential index, then first branch.
        /// </summary>
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
