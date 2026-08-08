using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;

namespace Slicelab.PDF
{
    public abstract class PdfElement
    {
        public double SpaceAfter { get; set; }

        /// <summary>True if this element scales proportionally with column width (images, geometry, etc.).</summary>
        public virtual bool IsProportional => false;

        public abstract double MeasureHeight(XGraphics gfx, double columnWidth);
        public abstract void Draw(XGraphics gfx, double x, double y, double columnWidth);
    }

    // ─── Text ────────────────────────────────────────────────────

    public class PdfTextElement : PdfElement
    {
        public string Text { get; set; }
        public string FontFamily { get; set; }
        public double FontSize { get; set; }
        public XFontStyle FontStyle { get; set; }
        public XColor Color { get; set; }
        public int Alignment { get; set; } // 0=Left, 1=Center, 2=Right

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            var font = new XFont(FontFamily, FontSize, FontStyle);
            double lineHeight = FontSize * 1.2;
            int lineCount = CountWrappedLines(gfx, font, Text, columnWidth);
            return lineCount * lineHeight;
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            if (string.IsNullOrEmpty(Text)) return;
            var font = new XFont(FontFamily, FontSize, FontStyle);
            var brush = new XSolidBrush(Color);
            double height = MeasureHeight(gfx, columnWidth);
            var rect = new XRect(x, y, columnWidth, height);

            var tf = new XTextFormatter(gfx);
            var align = XParagraphAlignment.Left;
            switch (Alignment)
            {
                case 1: align = XParagraphAlignment.Center; break;
                case 2: align = XParagraphAlignment.Right; break;
            }
            tf.DrawString(Text, font, brush, rect,
                new TextFormatAlignment { Horizontal = align });
        }

        private static int CountWrappedLines(XGraphics gfx, XFont font, string text, double maxWidth)
        {
            if (maxWidth <= 0) return 1;
            string[] paragraphs = text.Split('\n');
            int totalLines = 0;

            foreach (string para in paragraphs)
            {
                if (string.IsNullOrEmpty(para))
                {
                    totalLines++;
                    continue;
                }

                string[] words = para.Split(' ');
                double lineWidth = 0;
                int lines = 1;

                foreach (string word in words)
                {
                    double wordWidth = gfx.MeasureString(word + " ", font).Width;
                    if (lineWidth + wordWidth > maxWidth && lineWidth > 0)
                    {
                        lines++;
                        lineWidth = wordWidth;
                    }
                    else
                    {
                        lineWidth += wordWidth;
                    }
                }
                totalLines += lines;
            }
            return totalLines;
        }
    }

    // ─── Image ───────────────────────────────────────────────────

    public class PdfImageElement : PdfElement
    {
        public string ImagePath { get; set; }
        public double FixedHeight { get; set; } // 0 = auto
        public int Alignment { get; set; }
        public string LastError { get; set; }
        public override bool IsProportional => true;

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (FixedHeight > 0) return FixedHeight;
            try
            {
                using (var img = XImage.FromFile(ImagePath))
                    return columnWidth * ((double)img.PixelHeight / img.PixelWidth);
            }
            catch (Exception ex)
            {
                LastError = $"MeasureHeight failed for '{ImagePath}': {PdfImageSource.Describe(ex)}";
                return 0;
            }
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            try
            {
                using (var img = XImage.FromFile(ImagePath))
                {
                    double drawH = FixedHeight > 0 ? FixedHeight : columnWidth * ((double)img.PixelHeight / img.PixelWidth);
                    double drawW = FixedHeight > 0 ? FixedHeight * ((double)img.PixelWidth / img.PixelHeight) : columnWidth;
                    if (drawW > columnWidth) drawW = columnWidth;

                    double offsetX = 0;
                    if (Alignment == 1) offsetX = (columnWidth - drawW) / 2;
                    else if (Alignment == 2) offsetX = columnWidth - drawW;

                    gfx.DrawImage(img, x + offsetX, y, drawW, drawH);
                }
            }
            catch (Exception ex)
            {
                LastError = $"Draw failed for '{ImagePath}': {PdfImageSource.Describe(ex)}";
                try
                {
                    var font = new XFont("Arial", 8, XFontStyle.Italic);
                    var tf = new XTextFormatter(gfx);
                    var errorRect = new XRect(x, y, columnWidth, 60);
                    tf.DrawString($"[Image error: {PdfImageSource.Describe(ex)}]", font,
                        XBrushes.Red, errorRect,
                        new TextFormatAlignment { Horizontal = XParagraphAlignment.Left });
                }
                catch { }
            }
        }
    }

    // ─── Geometry ────────────────────────────────────────────────

    /// <summary>A polyline destined for the PDF, carrying whether the source curve was closed.</summary>
    public class GeoPolyline
    {
        public XPoint[] Points { get; set; }
        public bool Closed { get; set; }
    }

    public class GeoBranch
    {
        public GeoPolyline[] Polylines { get; set; }
        public XColor? FillColor { get; set; }
        public XColor? StrokeColor { get; set; }
        public double StrokeWeight { get; set; }
    }

    public class PdfGeometryElement : PdfElement
    {
        public List<GeoBranch> Branches { get; set; }
        public double BBoxWidth { get; set; }  // in points
        public double BBoxHeight { get; set; } // in points
        public double FixedHeight { get; set; }
        public override bool IsProportional => true;

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (FixedHeight > 0) return FixedHeight;
            if (BBoxWidth <= 0) return 0;
            return columnWidth * (BBoxHeight / BBoxWidth);
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            if (Branches == null || BBoxWidth <= 0) return;

            double drawH = MeasureHeight(gfx, columnWidth);
            double scale = columnWidth / BBoxWidth;

            foreach (var branch in Branches)
            {
                if (branch.Polylines == null) continue;

                var path = new XGraphicsPath();
                path.FillMode = XFillMode.Alternate;

                foreach (var pl in branch.Polylines)
                {
                    var pts = pl?.Points;
                    if (pts == null || pts.Length < 2) continue;

                    // Scale and translate points into draw rect
                    var scaled = new XPoint[pts.Length];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        scaled[i] = new XPoint(
                            x + pts[i].X * scale,
                            y + pts[i].Y * scale);
                    }

                    path.StartFigure();
                    path.AddLines(scaled);
                    // Only seal closed shapes — an open curve must stay open so its stroke
                    // does not draw a phantom segment back to the start point.
                    if (pl.Closed) path.CloseFigure();
                }

                bool hasFill = branch.FillColor.HasValue;
                bool hasStroke = branch.StrokeColor.HasValue && branch.StrokeWeight > 0;

                if (hasFill && hasStroke)
                {
                    var pen = new XPen(branch.StrokeColor.Value, branch.StrokeWeight);
                    gfx.DrawPath(pen, new XSolidBrush(branch.FillColor.Value), path);
                }
                else if (hasFill)
                {
                    gfx.DrawPath(new XSolidBrush(branch.FillColor.Value), path);
                }
                else if (hasStroke)
                {
                    var pen = new XPen(branch.StrokeColor.Value, branch.StrokeWeight);
                    gfx.DrawPath(pen, path);
                }
            }
        }
    }

    // ─── Group ───────────────────────────────────────────────────

    public class PdfGroupElement : PdfElement
    {
        public List<PdfElement> Children { get; set; }

        public override bool IsProportional
        {
            get
            {
                if (Children == null) return false;
                foreach (var child in Children)
                    if (child.IsProportional) return true;
                return false;
            }
        }

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (Children == null) return 0;
            double total = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                total += Children[i].MeasureHeight(gfx, columnWidth);
                if (i < Children.Count - 1)
                    total += Children[i].SpaceAfter;
            }
            return total;
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            if (Children == null) return;
            double curY = y;
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Draw(gfx, x, curY, columnWidth);
                curY += Children[i].MeasureHeight(gfx, columnWidth);
                if (i < Children.Count - 1)
                    curY += Children[i].SpaceAfter;
            }
        }
    }

    // ─── Make2D ──────────────────────────────────────────────────

    public class PdfMake2DElement : PdfElement
    {
        public List<XPoint[]> VisibleLines { get; set; }
        public List<XPoint[]> HiddenLines { get; set; }
        public XColor StrokeColor { get; set; }
        public XColor HiddenColor { get; set; }
        public double StrokeWeight { get; set; }
        public double HiddenWeight { get; set; }
        public override bool IsProportional => true;
        public double BBoxWidth { get; set; }
        public double BBoxHeight { get; set; }
        public double FixedHeight { get; set; }
        public int Alignment { get; set; }

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (FixedHeight > 0) return FixedHeight;
            if (BBoxWidth <= 0) return 0;
            return columnWidth * (BBoxHeight / BBoxWidth);
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            if (BBoxWidth <= 0) return;

            double drawW, drawH, scale;
            if (FixedHeight > 0)
            {
                drawH = FixedHeight;
                scale = drawH / BBoxHeight;
                drawW = BBoxWidth * scale;
                if (drawW > columnWidth) { drawW = columnWidth; scale = drawW / BBoxWidth; drawH = BBoxHeight * scale; }
            }
            else
            {
                drawW = columnWidth;
                scale = drawW / BBoxWidth;
                drawH = BBoxHeight * scale;
            }

            double offsetX = 0;
            if (Alignment == 1) offsetX = (columnWidth - drawW) / 2;
            else if (Alignment == 2) offsetX = columnWidth - drawW;

            double ox = x + offsetX;
            double oy = y;

            if (HiddenLines != null && HiddenLines.Count > 0)
            {
                var pen = new XPen(HiddenColor, HiddenWeight);
                pen.DashStyle = XDashStyle.Dash;
                pen.DashPattern = new double[] { 4, 2 };
                foreach (var seg in HiddenLines)
                {
                    if (seg == null || seg.Length < 2) continue;
                    gfx.DrawLine(pen,
                        ox + seg[0].X * scale, oy + seg[0].Y * scale,
                        ox + seg[1].X * scale, oy + seg[1].Y * scale);
                }
            }

            if (VisibleLines != null)
            {
                var pen = new XPen(StrokeColor, StrokeWeight);
                foreach (var seg in VisibleLines)
                {
                    if (seg == null || seg.Length < 2) continue;
                    gfx.DrawLine(pen,
                        ox + seg[0].X * scale, oy + seg[0].Y * scale,
                        ox + seg[1].X * scale, oy + seg[1].Y * scale);
                }
            }
        }
    }

    // ─── Viewport Capture ────────────────────────────────────────

    public class PdfViewCaptureElement : PdfElement
    {
        public byte[] ImageData { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public double FixedHeight { get; set; }
        public int Alignment { get; set; }
        public override bool IsProportional => true;

        public override double MeasureHeight(XGraphics gfx, double columnWidth)
        {
            if (FixedHeight > 0) return FixedHeight;
            if (PixelWidth <= 0) return 0;
            return columnWidth * ((double)PixelHeight / PixelWidth);
        }

        public override void Draw(XGraphics gfx, double x, double y, double columnWidth)
        {
            if (ImageData == null || ImageData.Length == 0) return;

            try
            {
                using (var img = XImage.FromStream(() => new System.IO.MemoryStream(ImageData)))
                {
                    double drawH = FixedHeight > 0 ? FixedHeight : columnWidth * ((double)PixelHeight / PixelWidth);
                    double drawW = FixedHeight > 0 ? FixedHeight * ((double)PixelWidth / PixelHeight) : columnWidth;
                    if (drawW > columnWidth) drawW = columnWidth;

                    double offsetX = 0;
                    if (Alignment == 1) offsetX = (columnWidth - drawW) / 2;
                    else if (Alignment == 2) offsetX = columnWidth - drawW;

                    gfx.DrawImage(img, x + offsetX, y, drawW, drawH);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var font = new XFont("Arial", 8, XFontStyle.Italic);
                    var tf = new XTextFormatter(gfx);
                    tf.DrawString($"[Viewport capture error: {PdfImageSource.Describe(ex)}]", font,
                        XBrushes.Red, new XRect(x, y, columnWidth, 60),
                        new TextFormatAlignment { Horizontal = XParagraphAlignment.Left });
                }
                catch { }
            }
        }
    }

    // ─── GH_Goo wrapper ─────────────────────────────────────────

    public class GH_PdfElement : GH_Goo<PdfElement>
    {
        public GH_PdfElement() : base() { }
        public GH_PdfElement(PdfElement element) : base(element) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "PdfElement";
        public override string TypeDescription => "An element for PDF layout (text, image, geometry, or group)";

        public override IGH_Goo Duplicate()
        {
            // Elements are consumed once during rendering, shallow copy is fine
            return new GH_PdfElement(Value);
        }

        public override string ToString()
        {
            if (Value == null) return "PdfElement: (null)";
            if (Value is PdfTextElement t) return $"PdfText: \"{(t.Text?.Length > 30 ? t.Text.Substring(0, 30) + "..." : t.Text)}\"";
            if (Value is PdfImageElement img) return $"PdfImage: {System.IO.Path.GetFileName(img.ImagePath)}";
            if (Value is PdfGeometryElement) return "PdfGeometry";
            if (Value is PdfGroupElement g) return $"PdfGroup: {g.Children?.Count ?? 0} children";
            if (Value is PdfMake2DElement m) return $"PdfMake2D: {(m.VisibleLines?.Count ?? 0)} visible, {(m.HiddenLines?.Count ?? 0)} hidden lines";
            if (Value is PdfViewCaptureElement vc) return $"PdfViewCapture: {vc.PixelWidth}x{vc.PixelHeight}";
            return "PdfElement";
        }
    }
}
