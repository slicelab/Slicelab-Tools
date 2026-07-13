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

        public static Polyline CurveToPolyline(Curve crv, double tolerance)
        {
            if (crv.TryGetPolyline(out Polyline polyline))
            {
                if (polyline.Count >= 3)
                {
                    ClosePolyline(ref polyline);
                    return polyline;
                }
            }

            var plc = crv.ToPolyline(tolerance, RhinoMath.ToRadians(1.0), 0, 0);
            if (plc != null && plc.TryGetPolyline(out polyline) && polyline.Count >= 3)
            {
                ClosePolyline(ref polyline);
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

        public static XGraphicsPath BuildGraphicsPath(List<XPoint[]> polylines)
        {
            var path = new XGraphicsPath();
            path.FillMode = XFillMode.Alternate;

            foreach (var pts in polylines)
            {
                if (pts == null || pts.Length < 3) continue;
                path.StartFigure();
                path.AddLines(pts);
                path.CloseFigure();
            }

            return path;
        }
    }
}
