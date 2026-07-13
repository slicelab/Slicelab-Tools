using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Special;

namespace Slicelab.Marketing
{
    public class SlicelabCardComponent : GH_Component
    {
        public SlicelabCardComponent()
            : base("Slicelab", "SL",
                "Slicelab — computational design & AM consulting.",
                "Slicelab Tools", "!SL")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("5A6B7C8D-9E0F-4A1B-2C3D-4E5F6A7B8C01");

        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Default.png");

        public override void CreateAttributes()
        {
            m_attributes = new SlicelabCardAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // No inputs
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Website", "Web", "Studio URL", GH_ParamAccess.item);
            pManager.AddTextParameter("Author", "Author", "Author credit", GH_ParamAccess.item);
            pManager.AddTextParameter("Collab", "Collab", "CTA string", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            for (int i = 0; i < Params.Output.Count; i++)
            {
                if (Params.Output[i].Recipients.Count > 0) continue;

                var panel = new GH_Panel();
                panel.CreateAttributes();
                panel.Properties.DrawIndices = false;
                panel.Properties.DrawPaths = false;
                // Gradient: top=blue, middle=half-blue, bottom=white
                Color[] panelColors =
                {
                    Color.FromArgb(255, 0, 120, 200),
                    Color.FromArgb(255, 128, 188, 228),
                    Color.FromArgb(255, 240, 245, 255)
                };
                panel.Properties.Colour = panelColors[i];
                panel.Properties.Font = GH_FontServer.NewFont(GH_FontServer.FamilyScript, 12f);
                // Center panel group on middle output grip, spaced evenly
                var midGrip = Params.Output[1].Attributes.Pivot;
                float panelW = 360f;
                float panelH = 60f;
                float panelSpacing = 70f;
                float panelX = midGrip.X + 60;
                float panelY = midGrip.Y - panelH / 2f + (i - 1) * panelSpacing;
                panel.Attributes.Pivot = new PointF(panelX, panelY);
                panel.Attributes.Bounds = new RectangleF(panelX, panelY, panelW, panelH);
                document.AddObject(panel, false);
                panel.AddSource(Params.Output[i]);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var v = typeof(SlicelabCardComponent).Assembly.GetName().Version;
            DA.SetData(0, $" Slicelab Tools V{v.Major}.{v.Minor}.{v.Build} \n Developed by Arthur Azoulai — Co-Founder");
            DA.SetData(1, " This is a combination of tools we have been \n developping over the past 15 years geared \n towards advanced 3D printing applications.");
            DA.SetData(2, " Want to collaborate? send us an email: \n info@slicelab.com");
        }
    }

    public class SlicelabCardAttributes : GH_ComponentAttributes
    {
        private static GraphicsPath _cachedSPath;
        private static readonly object _lock = new object();

        // Original SVG viewbox: roughly 17 x 19
        private const float SvgWidth = 16.71f;
        private const float SvgHeight = 18.67f;

        // Component size (190 = 140 for S + 50 for button area)
        private const float CompWidth = 120f;
        private const float CompHeight = 190f;

        // Output grip positions along the S right contour (fractions of bounds)
        // Y values recalculated for 190px height to keep absolute positions same as 140px
        private static readonly PointF[] GripFractions =
        {
            new PointF(1.15f, 0.368f),  // Website — 70/190
            new PointF(1.19f, 0.516f),  // Author — 98/190
            new PointF(1.07f, 0.649f),  // Collab — 123.2/190
        };

        // LinkedIn button state
        private RectangleF _btnSlicelab;
        private RectangleF _btnArthur;
        private int _hoverBtn; // 0=none, 1=slicelab, 2=arthur
        private int _pressBtn; // 0=none, 1=slicelab, 2=arthur

        private const string UrlSlicelab = "https://www.linkedin.com/company/slicelab/";
        private const string UrlArthur = "https://www.linkedin.com/in/arthurazoulai/";

        public SlicelabCardAttributes(SlicelabCardComponent owner) : base(owner) { }

        protected override void Layout()
        {
            Pivot = GH_Convert.ToPoint(Pivot);
            Bounds = new RectangleF(
                Pivot.X - CompWidth / 2f,
                Pivot.Y - CompHeight / 2f,
                CompWidth,
                CompHeight);

            // Layout output grips along the S shape's right contour
            int count = Owner.Params.Output.Count;
            for (int i = 0; i < count && i < GripFractions.Length; i++)
            {
                var param = Owner.Params.Output[i];
                float gripX = Bounds.X + GripFractions[i].X * Bounds.Width;
                float gripY = Bounds.Y + GripFractions[i].Y * Bounds.Height;
                param.Attributes.Pivot = new PointF(gripX, gripY);
                param.Attributes.Bounds = new RectangleF(gripX - 4, gripY - 4, 8, 8);
            }

            // LinkedIn buttons below S shape
            float btnMargin = 4f;
            float btnW = CompWidth - btnMargin * 2;
            float btnH = 22f;
            float btnGap = 3f;
            float btnTop = Bounds.Y + 138f; // just below S bottom edge
            _btnSlicelab = new RectangleF(Bounds.X + btnMargin, btnTop, btnW, btnH);
            _btnArthur = new RectangleF(Bounds.X + btnMargin, btnTop + btnH + btnGap, btnW, btnH);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                // Draw output grip dots FIRST so they sit behind the S shape
                const float dotR = 4.69f;
                int count = Owner.Params.Output.Count;
                for (int i = 0; i < count && i < GripFractions.Length; i++)
                {
                    float gx = Bounds.X + GripFractions[i].X * Bounds.Width;
                    float gy = Bounds.Y + GripFractions[i].Y * Bounds.Height;
                    var gripRect = new RectangleF(gx - dotR, gy - dotR, dotR * 2, dotR * 2);
                    graphics.FillEllipse(Brushes.White, gripRect);
                    using (var pen = new Pen(Color.Black, 2f))
                        graphics.DrawEllipse(pen, gripRect);
                }

                // Draw the S shape on top — pinned to top of bounds
                var sPath = GetSPath();
                if (sPath != null)
                {
                    // Scale to fit width, use actual S height (not full CompHeight)
                    float scale = CompWidth / SvgWidth;
                    float pathW = SvgWidth * scale;
                    float pathH = SvgHeight * scale;
                    float offsetX = Bounds.X + (Bounds.Width - pathW) / 2f;
                    float offsetY = Bounds.Y + 3f; // pin to top with small margin

                    var state = graphics.Save();
                    graphics.TranslateTransform(offsetX, offsetY);
                    graphics.ScaleTransform(scale, scale);

                    var pathBounds = sPath.GetBounds();
                    if (pathBounds.Width > 0 && pathBounds.Height > 0)
                    {
                        // White-to-blue gradient fill (top=blue, bottom=white)
                        using (var fillBrush = new LinearGradientBrush(
                            new RectangleF(pathBounds.X - 1, pathBounds.Y - 1,
                                pathBounds.Width + 2, pathBounds.Height + 2),
                            Color.FromArgb(255, 0, 120, 200),
                            Color.FromArgb(255, 240, 245, 255),
                            LinearGradientMode.Vertical))
                        {
                            graphics.FillPath(fillBrush, sPath);
                        }

                        // Gray outline #414042
                        bool selected = Owner.Attributes.Selected;
                        using (var strokePen = new Pen(
                            selected ? Color.FromArgb(255, 120, 200, 255) : Color.FromArgb(255, 65, 64, 66),
                            0.15f))
                        {
                            graphics.DrawPath(strokePen, sPath);
                        }
                    }

                    graphics.Restore(state);
                }

                // Draw LinkedIn buttons below S
                RenderButton(graphics, _btnSlicelab, "in   Slicelab", _hoverBtn == 1, _pressBtn == 1);
                RenderButton(graphics, _btnArthur, "in   Arthur Azoulai", _hoverBtn == 2, _pressBtn == 2);
            }
        }

        private void RenderButton(Graphics graphics, RectangleF rect, string text, bool hover, bool pressed)
        {
            if (rect.Width <= 0) return;

            float radius = 4f;
            using (var rrPath = RoundedRect(rect, radius))
            {
                // Fill: white 30% normally, blue tint on hover/press
                if (pressed)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(100, 0, 119, 181)))
                        graphics.FillPath(fill, rrPath);
                }
                else if (hover)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(55, 0, 119, 181)))
                        graphics.FillPath(fill, rrPath);
                }
                else
                {
                    using (var fill = new SolidBrush(Color.FromArgb(77, 255, 255, 255)))
                        graphics.FillPath(fill, rrPath);
                }

                // Border
                using (var pen = new Pen(Color.FromArgb(140, 65, 64, 66), 1f))
                    graphics.DrawPath(pen, rrPath);
            }

            // Text — "in" in LinkedIn blue bold, rest in gray
            var linkedInBlue = Color.FromArgb(255, 0, 119, 181);
            var grayColor = Color.FromArgb(255, 40, 40, 40);
            float fontSize = rect.Height * 0.50f;

            // Split at first double-space to separate "in" prefix from label
            int sep = text.IndexOf("  ", StringComparison.Ordinal);
            string prefix = sep >= 0 ? text.Substring(0, sep) : text;
            string label = sep >= 0 ? text.Substring(sep + 2) : "";

            using (var boldFont = GH_FontServer.NewFont(GH_FontServer.Standard, fontSize, FontStyle.Bold))
            using (var normalFont = GH_FontServer.NewFont(GH_FontServer.Standard, fontSize))
            using (var blueBrush = new SolidBrush(linkedInBlue))
            using (var grayBrush = new SolidBrush(grayColor))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };

                // Measure "in" to know where the label starts; clamp so wide
                // platform fonts can't push the prefix out of the button
                float pad = 5f;
                float prefixW = graphics.MeasureString(prefix, boldFont).Width;
                float totalW = prefixW + graphics.MeasureString(label, normalFont).Width;
                float startX = rect.X + Math.Max((rect.Width - totalW) / 2f, pad);

                var prefixRect = new RectangleF(startX, rect.Y, prefixW, rect.Height);
                var labelRect = new RectangleF(startX + prefixW, rect.Y,
                    Math.Max(rect.Right - pad - (startX + prefixW), 0f), rect.Height);

                graphics.DrawString(prefix, boldFont, blueBrush, prefixRect, sf);
                graphics.DrawString(label, normalFont, grayBrush, labelRect, sf);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_btnSlicelab.Contains(e.CanvasLocation))
            {
                _pressBtn = 1;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            if (_btnArthur.Contains(e.CanvasLocation))
            {
                _pressBtn = 2;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_pressBtn > 0)
            {
                string url = _pressBtn == 1 ? UrlSlicelab : UrlArthur;
                RectangleF btnRect = _pressBtn == 1 ? _btnSlicelab : _btnArthur;
                _pressBtn = 0;
                Owner.OnDisplayExpired(false);

                if (btnRect.Contains(e.CanvasLocation))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch { /* ignore if browser fails to open */ }
                }
                return GH_ObjectResponse.Release;
            }
            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            int newHover = 0;
            if (_btnSlicelab.Contains(e.CanvasLocation))
                newHover = 1;
            else if (_btnArthur.Contains(e.CanvasLocation))
                newHover = 2;

            if (newHover != _hoverBtn)
            {
                _hoverBtn = newHover;
                Owner.OnDisplayExpired(false);
            }
            return base.RespondToMouseMove(sender, e);
        }

        private static GraphicsPath GetSPath()
        {
            lock (_lock)
            {
                if (_cachedSPath != null) return _cachedSPath;
                _cachedSPath = ParseSvgPath(SvgPathData);
                return _cachedSPath;
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // SVG path data for the Slicelab S letterform
        private const string SvgPathData =
            "M12.27,14.96c-.07.08-.15.16-.24.23-1.27,1.05-3.42.73-4.92,0-.17-.08-.33-.17-.48-.26" +
            "-.18-.11-.35-.23-.51-.36-.11-.09-.22-.18-.33-.27-1.93,1.24-3.86,2.47-5.78,3.71" +
            "h16.71c.37-.3.71-.63,1.04-.98.13-.14.26-.29.38-.43.12-.14.23-.29.35-.44" +
            ".23-.31.45-.63.64-.97.09-.16.18-.32.26-.49.1-.19.19-.38.27-.58.11-.25.2-.51.29-.77" +
            ".03-.1.06-.2.09-.3.05-.16.09-.33.12-.5.03-.15.06-.31.08-.46.02-.19.03-.39.03-.58" +
            ",0-.13,0-.26-.02-.39-.02-.18-.05-.36-.1-.54-.05-.18-.12-.36-.21-.53" +
            "-.07-.14-.15-.26-.24-.39-.15-.2-.33-.39-.53-.55-.24-.19-.5-.36-.77-.49" +
            "-.28-.14-.57-.25-.86-.34-.32-.1-.64-.18-.96-.27-.24-.06-.47-.13-.7-.19" +
            ",0,0,0,0,0,0-.92-.25-1.83-.5-2.75-.76-1.77-.47-1.98-.62-1.76-1.24" +
            ".27-.74,1.12-1.16,2.25-1.16,1.43,0,2.51.47,3.83,1.63.12.1.23.21.35.31" +
            ",1.85-1.19,3.71-2.38,5.56-3.57-.86-.93-1.24-1.28-2.16-1.74" +
            "-1.55-.85-3.49-1.28-5.74-1.28C9.91,0,5.51,2.6,4,6.75c-.13.37-.23.72-.29,1.07" +
            "h0c-.01.06-.02.13-.03.19-.02.11-.03.22-.04.32-.02.21-.02.43,0,.64" +
            ".02.23.06.46.12.68.04.13.08.26.14.39.05.13.12.25.19.37" +
            ".18.28.41.53.66.74.16.13.32.24.49.34.41.24.85.43,1.31.58" +
            ".27.09.54.17.81.25.4.11.8.22,1.2.31.29.07.58.14.88.2" +
            ".52.12,1.05.23,1.54.34.2.04.4.09.63.15.45.14,1,.38,1.03.77" +
            ",0,.13-.04.28-.1.41-.07.16-.16.3-.26.43Z";

        // ─── SVG Path Parser ───────────────────────────────

        private static GraphicsPath ParseSvgPath(string d)
        {
            var path = new GraphicsPath();
            var tokens = Tokenize(d);
            int idx = 0;
            float cx = 0, cy = 0; // current point
            float sx = 0, sy = 0; // start of current subpath
            char cmd = ' ';

            while (idx < tokens.Count)
            {
                // Check if current token is a command letter
                if (tokens[idx].Length == 1 && char.IsLetter(tokens[idx][0]))
                {
                    cmd = tokens[idx][0];
                    idx++;
                }

                switch (cmd)
                {
                    case 'M': // absolute moveto
                    {
                        float x = Float(tokens, ref idx);
                        float y = Float(tokens, ref idx);
                        cx = x; cy = y;
                        sx = cx; sy = cy;
                        cmd = 'L'; // subsequent coords are lineto
                        break;
                    }
                    case 'm': // relative moveto
                    {
                        float dx = Float(tokens, ref idx);
                        float dy = Float(tokens, ref idx);
                        cx += dx; cy += dy;
                        sx = cx; sy = cy;
                        cmd = 'l';
                        break;
                    }
                    case 'L': // absolute lineto
                    {
                        float x = Float(tokens, ref idx);
                        float y = Float(tokens, ref idx);
                        path.AddLine(cx, cy, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'l': // relative lineto
                    {
                        float dx = Float(tokens, ref idx);
                        float dy = Float(tokens, ref idx);
                        float x = cx + dx, y = cy + dy;
                        path.AddLine(cx, cy, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'H': // absolute horizontal
                    {
                        float x = Float(tokens, ref idx);
                        path.AddLine(cx, cy, x, cy);
                        cx = x;
                        break;
                    }
                    case 'h': // relative horizontal
                    {
                        float dx = Float(tokens, ref idx);
                        float x = cx + dx;
                        path.AddLine(cx, cy, x, cy);
                        cx = x;
                        break;
                    }
                    case 'V': // absolute vertical
                    {
                        float y = Float(tokens, ref idx);
                        path.AddLine(cx, cy, cx, y);
                        cy = y;
                        break;
                    }
                    case 'v': // relative vertical
                    {
                        float dy = Float(tokens, ref idx);
                        float y = cy + dy;
                        path.AddLine(cx, cy, cx, y);
                        cy = y;
                        break;
                    }
                    case 'C': // absolute cubic bezier
                    {
                        float x1 = Float(tokens, ref idx), y1 = Float(tokens, ref idx);
                        float x2 = Float(tokens, ref idx), y2 = Float(tokens, ref idx);
                        float x = Float(tokens, ref idx), y = Float(tokens, ref idx);
                        path.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'c': // relative cubic bezier
                    {
                        float dx1 = Float(tokens, ref idx), dy1 = Float(tokens, ref idx);
                        float dx2 = Float(tokens, ref idx), dy2 = Float(tokens, ref idx);
                        float dx = Float(tokens, ref idx), dy = Float(tokens, ref idx);
                        float x1 = cx + dx1, y1 = cy + dy1;
                        float x2 = cx + dx2, y2 = cy + dy2;
                        float x = cx + dx, y = cy + dy;
                        path.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                        cx = x; cy = y;
                        break;
                    }
                    case 'Z':
                    case 'z':
                    {
                        path.CloseFigure();
                        cx = sx; cy = sy;
                        cmd = ' ';
                        break;
                    }
                    default:
                        idx++; // skip unknown
                        break;
                }
            }

            return path;
        }

        private static float Float(List<string> tokens, ref int idx)
        {
            if (idx >= tokens.Count) return 0;
            float.TryParse(tokens[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float val);
            idx++;
            return val;
        }

        private static List<string> Tokenize(string d)
        {
            var tokens = new List<string>();
            // Match: command letters OR numbers (including negative, decimal)
            var matches = Regex.Matches(d, @"[A-Za-z]|[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
            foreach (Match m in matches)
                tokens.Add(m.Value);
            return tokens;
        }
    }
}
