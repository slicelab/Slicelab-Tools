using System.Collections.Generic;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Rhino;
using Rhino.Geometry;

namespace Slicelab.PDF
{
    public static class PdfLayoutHelper
    {
        // ─── Layout engine ───────────────────────────────────────

        public static PdfDocument Compose(PageSettings settings, List<PdfElement> elements, out string info)
        {
            var doc = new PdfDocument();
            var page = AddPage(doc, settings);
            var gfx = XGraphics.FromPdfPage(page);
            int col = 0;
            double cursorY = settings.MarginTop;
            int pageCount = 1;
            int warnings = 0;

            foreach (var element in elements)
            {
                double colW = settings.ColumnWidth;
                double drawWidth = colW;
                double height = element.MeasureHeight(gfx, colW);

                // Scale down proportional elements that are taller than the page
                if (height > settings.ContentHeight && element.IsProportional)
                {
                    double ratio = settings.ContentHeight / height;
                    drawWidth = colW * ratio;
                    height = settings.ContentHeight;
                }

                double available = settings.PageHeight - settings.MarginBottom - cursorY;

                if (height > available)
                {
                    // Move to next column
                    col++;
                    if (col >= settings.Columns)
                    {
                        // Move to next page
                        gfx.Dispose();
                        page = AddPage(doc, settings);
                        gfx = XGraphics.FromPdfPage(page);
                        col = 0;
                        pageCount++;
                    }
                    cursorY = settings.MarginTop;
                }

                double x = settings.MarginLeft + col * (settings.ColumnWidth + settings.GutterWidth);
                element.Draw(gfx, x, cursorY, drawWidth);
                cursorY += height + element.SpaceAfter;
            }

            gfx.Dispose();

            string warnText = warnings > 0 ? $" ({warnings} element(s) taller than column)" : "";
            info = $"{elements.Count} elements, {pageCount} page(s), {settings.Columns} column(s){warnText}";
            return doc;
        }

        private static PdfPage AddPage(PdfDocument doc, PageSettings settings)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(settings.PageWidth);
            page.Height = XUnit.FromPoint(settings.PageHeight);
            return page;
        }

        // ─── Shared geometry helpers ─────────────────────────────

        public static double GetUnitScale()
        {
            var rhinoDoc = RhinoDoc.ActiveDoc;
            double modelToMm = RhinoMath.UnitScale(
                rhinoDoc != null ? rhinoDoc.ModelUnitSystem : UnitSystem.Millimeters,
                UnitSystem.Millimeters);
            double mmToPoints = 2.8346456693;
            return modelToMm * mmToPoints;
        }

        public static double GetModelTolerance()
        {
            var rhinoDoc = RhinoDoc.ActiveDoc;
            return rhinoDoc != null ? rhinoDoc.ModelAbsoluteTolerance : 0.001;
        }

        public static void AppendBrepLoops(Brep brep, GH_Structure<GH_Curve> tree, GH_Path path)
        {
            foreach (var face in brep.Faces)
            {
                var outer = face.OuterLoop?.To3dCurve();
                if (outer != null)
                    tree.Append(new GH_Curve(outer), path);

                foreach (var loop in face.Loops)
                {
                    if (loop.LoopType == BrepLoopType.Inner)
                    {
                        var inner = loop.To3dCurve();
                        if (inner != null)
                            tree.Append(new GH_Curve(inner), path);
                    }
                }
            }
        }

        /// <summary>
        /// Convert a curve to a polyline. Closed curves are sealed and need at least 3 points;
        /// open curves are left open and need only 2, so a single straight segment survives.
        /// </summary>
        public static Polyline CurveToPolyline(Curve crv, double tolerance, out bool closed)
        {
            closed = crv.IsClosed;
            int minPoints = closed ? 3 : 2;

            if (crv.TryGetPolyline(out Polyline polyline) && polyline.Count >= minPoints)
            {
                if (closed) ClosePolyline(ref polyline);
                return polyline;
            }

            var plc = crv.ToPolyline(tolerance, RhinoMath.ToRadians(1.0), 0, 0);
            if (plc != null && plc.TryGetPolyline(out polyline) && polyline.Count >= minPoints)
            {
                if (closed) ClosePolyline(ref polyline);
                return polyline;
            }

            return null;
        }

        private static void ClosePolyline(ref Polyline pl)
        {
            if (pl[0].DistanceTo(pl[pl.Count - 1]) > 1e-6)
                pl.Add(pl[0]);
        }

        public static XPoint[] PolylineToXPoints(Polyline polyline, BoundingBox bbox, double unitScale)
        {
            var pts = new XPoint[polyline.Count];
            for (int i = 0; i < polyline.Count; i++)
            {
                pts[i] = new XPoint(
                    (polyline[i].X - bbox.Min.X) * unitScale,
                    (bbox.Max.Y - polyline[i].Y) * unitScale);
            }
            return pts;
        }

        // ─── Branch styling ──────────────────────────────────────

        /// <summary>Default stroke weight in points, used when a stroke is implied but no weight is given.</summary>
        public const double DefaultStrokeWeight = 1.0;

        /// <summary>Resolved fill/stroke styling for one geometry branch. Either may be null, never both.</summary>
        public class BranchStyle
        {
            public XColor? Fill { get; set; }
            public XColor? Stroke { get; set; }
            public double StrokeWeight { get; set; }
            /// <summary>True when no colors were supplied and the black-outline default was applied.</summary>
            public bool UsedDefault { get; set; }
        }

        /// <summary>
        /// Resolve fill and stroke for one branch. Supply a fill, a stroke, or both — with nothing
        /// supplied the branch falls back to a 1pt black outline so geometry always renders.
        /// </summary>
        public static BranchStyle ResolveBranchStyle(
            GH_Structure<GH_Colour> fillTree,
            GH_Structure<GH_Colour> strokeTree,
            GH_Structure<GH_Number> weightTree,
            GH_Path targetPath,
            int branchIndex)
        {
            bool hasFill = fillTree != null && fillTree.DataCount > 0;
            bool hasStrokeColor = strokeTree != null && strokeTree.DataCount > 0;
            bool hasStrokeWeight = weightTree != null && weightTree.DataCount > 0;

            var style = new BranchStyle();

            if (hasFill)
            {
                var fill = GetColorFromTree(fillTree, targetPath, branchIndex, System.Drawing.Color.Black);
                style.Fill = ToXColor(fill);
            }

            if (hasStrokeColor || hasStrokeWeight)
            {
                // A weight was given explicitly: honour it, including a deliberate 0 meaning "no stroke"
                // (that is how v0.27.2 behaved). With no weight given, a supplied color implies 1pt.
                double weight = hasStrokeWeight
                    ? GetNumberFromTree(weightTree, targetPath, branchIndex, DefaultStrokeWeight)
                    : DefaultStrokeWeight;

                if (weight > 0)
                {
                    var stroke = hasStrokeColor
                        ? GetColorFromTree(strokeTree, targetPath, branchIndex, System.Drawing.Color.Black)
                        : System.Drawing.Color.Black;
                    style.Stroke = ToXColor(stroke);
                    style.StrokeWeight = weight;
                }
            }

            // Nothing resolved to anything visible — fall back to a black outline rather than
            // silently dropping the branch.
            if (!style.Fill.HasValue && !style.Stroke.HasValue)
            {
                style.Stroke = XColors.Black;
                style.StrokeWeight = DefaultStrokeWeight;
                style.UsedDefault = true;
            }

            return style;
        }

        /// <summary>
        /// True when none of the three style inputs carry data, i.e. the black-line default applies.
        /// Answerable from the input trees alone, so it can be reported before any geometry work.
        /// </summary>
        public static bool NoStyleSupplied(
            GH_Structure<GH_Colour> fillTree,
            GH_Structure<GH_Colour> strokeTree,
            GH_Structure<GH_Number> weightTree)
        {
            return (fillTree == null || fillTree.DataCount == 0)
                && (strokeTree == null || strokeTree.DataCount == 0)
                && (weightTree == null || weightTree.DataCount == 0);
        }

        /// <summary>
        /// Heaviest stroke that could be drawn, in points — used to pad a bounding box that is
        /// degenerate in one axis, since a stroke occupies width the geometry itself does not.
        /// </summary>
        public static double MaxStrokeWeight(GH_Structure<GH_Number> weightTree)
        {
            double max = 0;
            if (weightTree != null)
            {
                foreach (var branch in weightTree.Branches)
                    foreach (var n in branch)
                        if (n != null && n.Value > max) max = n.Value;
            }
            return max > 0 ? max : DefaultStrokeWeight;
        }

        public static XColor ToXColor(System.Drawing.Color c)
        {
            return XColor.FromArgb(c.A, c.R, c.G, c.B);
        }

        /// <summary>
        /// Get a color from a tree, matching by path first, then falling back to sequential index, then first branch.
        /// </summary>
        public static System.Drawing.Color GetColorFromTree(GH_Structure<GH_Colour> tree, GH_Path targetPath, int branchIndex, System.Drawing.Color fallback)
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

        /// <summary>
        /// Get a number from a tree, matching by path first, then falling back to sequential index, then first branch.
        /// </summary>
        public static double GetNumberFromTree(GH_Structure<GH_Number> tree, GH_Path targetPath, int branchIndex, double fallback)
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

        public static XGraphicsPath BuildGraphicsPath(List<GeoPolyline> polylines)
        {
            var path = new XGraphicsPath();
            path.FillMode = XFillMode.Alternate;

            foreach (var pl in polylines)
            {
                if (pl?.Points == null || pl.Points.Length < 2) continue;
                path.StartFigure();
                path.AddLines(pl.Points);
                // Only seal closed shapes — an open curve must stay open so its stroke
                // does not draw a phantom segment back to the start point.
                if (pl.Closed) path.CloseFigure();
            }

            return path;
        }
    }
}
