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
                "Export planar curves to a PDF with fill and stroke colors. Closed curves can be filled; open curves are stroked. Page size is auto-computed from curve bounding box. Even-odd fill rule handles nested shapes.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("F5A6B7C8-D9E0-4F1A-2B3C-4D5E6F7A8B23");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Pdf.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Planar curves (open or closed), surfaces, or breps (DataTree: one branch = one shape group). Breps/surfaces are converted to boundary curves automatically.", GH_ParamAccess.tree);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddColourParameter("Fill Color", "FC", "Fill color per branch (optional if a stroke is supplied, matched by tree path)", GH_ParamAccess.tree);
            pManager.AddColourParameter("Stroke Color", "SC", "Stroke color per branch (optional, matched by tree path). Supplied on its own, defaults to a 1pt weight.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Stroke Weight", "SW", "Stroke weight per branch in points (optional, matched by tree path). Supplied on its own, defaults to a black stroke.", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Save", "Sa", "Set to true to save PDF", GH_ParamAccess.item, false);

            pManager[3].Optional = true;
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
            Message = null;

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

            // Report the styling default before anything can return early, so the note under the
            // component reflects the wiring rather than whether an export just ran.
            if (PdfLayoutHelper.NoStyleSupplied(fillTree, strokeTree, weightTree))
                Message = "default: 1pt black line";

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
                    if (crv == null || !crv.IsPlanar()) continue;
                    bbox.Union(crv.GetBoundingBox(true));
                }
            }

            // The page is sized from the bounding box, so an extent of zero in either axis would
            // produce a degenerate page. Name the axis — "invalid bounding box" leaves the user guessing.
            bool flatX = bbox.Diagonal.X < 1e-10;
            bool flatY = bbox.Diagonal.Y < 1e-10;

            if (!bbox.IsValid || (flatX && flatY))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "All geometry collapses to a single point — there is nothing to lay out on a page.");
                return;
            }
            if (flatY)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "All geometry lies on a single horizontal line, so the page would have no height. Add geometry with vertical extent, or use PDF Flat Geometry to place this line in a page layout.");
                return;
            }
            if (flatX)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "All geometry lies on a single vertical line, so the page would have no width. Add geometry with horizontal extent, or use PDF Flat Geometry to place this line in a page layout.");
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

            bool usedDefaultStyle = false;
            int skipped = 0;
            double curveTolerance = doc != null ? doc.ModelAbsoluteTolerance : 0.001;

            for (int i = 0; i < curveTree.Branches.Count; i++)
            {
                var branch = curveTree.Branches[i];
                if (branch == null || branch.Count == 0) continue;

                GH_Path curvePath = curveTree.Paths[i];

                // Fill, stroke, or both — resolved by the shared rule in PdfLayoutHelper
                var style = PdfLayoutHelper.ResolveBranchStyle(fillTree, strokeTree, weightTree, curvePath, i);
                if (style.UsedDefault) usedDefaultStyle = true;

                // Build combined XGraphicsPath with StartFigure/CloseFigure for even-odd fill
                var path = new XGraphicsPath();
                path.FillMode = XFillMode.Alternate;

                foreach (var ghCrv in branch)
                {
                    Curve crv = ghCrv?.Value;
                    if (crv == null || !crv.IsPlanar()) { skipped++; continue; }

                    var polyline = PdfLayoutHelper.CurveToPolyline(crv, curveTolerance, out bool closed);
                    if (polyline == null) { skipped++; continue; }

                    path.StartFigure();
                    path.AddLines(PdfLayoutHelper.PolylineToXPoints(polyline, bbox, unitScale));
                    // Only seal closed shapes — an open curve must stay open so its stroke
                    // does not draw a phantom segment back to the start point.
                    if (closed) path.CloseFigure();
                }

                // Draw the path — fill, stroke, or both
                bool hasFill = style.Fill.HasValue;
                bool hasStroke = style.Stroke.HasValue && style.StrokeWeight > 0;

                if (hasFill && hasStroke)
                    gfx.DrawPath(new XPen(style.Stroke.Value, style.StrokeWeight), new XSolidBrush(style.Fill.Value), path);
                else if (hasFill)
                    gfx.DrawPath(new XSolidBrush(style.Fill.Value), path);
                else if (hasStroke)
                    gfx.DrawPath(new XPen(style.Stroke.Value, style.StrokeWeight), path);
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

            // Save
            Directory.CreateDirectory(filePath);
            string fullPath = Path.Combine(filePath, fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? fileName : fileName + ".pdf");

            document.Save(fullPath);
            gfx.Dispose();

            DA.SetData(0, fullPath);
            DA.SetData(1, $"Saved PDF: {pageWidth:F1} x {pageHeight:F1} pt ({curveTree.Branches.Count} branches, {curveTree.DataCount} curves)");
        }

    }
}
