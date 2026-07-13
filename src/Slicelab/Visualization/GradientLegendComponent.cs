using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;

namespace Slicelab.Visualization
{
    public class GradientLegendComponent : GH_Component
    {
        private double _minVal;
        private double _maxVal;
        private string _title = "Value";
        private Color _colorA;
        private Color _colorB;
        private Color _textColor = Color.Black;
        private int _barWidth = 200;
        private int _decimals = 2;
        private int _titleSize = 16;
        private int _labelSize = 12;
        private int _position = 1; // 0=TL, 1=TC, 2=TR, 3=BL, 4=BC, 5=BR
        private bool _show = true;

        private static readonly Color DefaultColorA = Color.FromArgb(255, 30, 80, 200);
        private static readonly Color DefaultColorB = Color.FromArgb(255, 255, 100, 20);

        public GradientLegendComponent()
            : base("Gradient Legend", "SLGLgd",
                "Draws a 2D gradient legend in the viewport. Choose placement position (top/bottom, left/center/right).",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("F1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B02");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-GradLegend.png");
        public override bool IsPreviewCapable => true;
        public override BoundingBox ClippingBox => BoundingBox.Empty;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Min Value", "Min", "Minimum value label", GH_ParamAccess.item);               // 0
            pManager.AddNumberParameter("Max Value", "Max", "Maximum value label", GH_ParamAccess.item);               // 1
            pManager.AddTextParameter("Title", "Title", "Legend title", GH_ParamAccess.item, "Value");                 // 2
            pManager.AddColourParameter("Color A", "CA", "Gradient start color", GH_ParamAccess.item, DefaultColorA); // 3
            pManager.AddColourParameter("Color B", "CB", "Gradient end color", GH_ParamAccess.item, DefaultColorB);   // 4
            pManager.AddIntegerParameter("Position", "Pos", "Viewport placement (0=Top Left, 1=Top Center, 2=Top Right, 3=Bottom Left, 4=Bottom Center, 5=Bottom Right)", GH_ParamAccess.item, 1); // 5
            pManager.AddIntegerParameter("Width", "W", "Legend bar width in pixels", GH_ParamAccess.item, 200);        // 6
            pManager.AddIntegerParameter("Decimals", "Dec", "Decimal places on labels", GH_ParamAccess.item, 2);      // 7
            pManager.AddIntegerParameter("Title Size", "TS", "Title text size in pixels", GH_ParamAccess.item, 16);   // 8
            pManager.AddIntegerParameter("Label Size", "LS", "Label text size in pixels", GH_ParamAccess.item, 12);   // 9
            pManager.AddColourParameter("Text Color", "TC", "Color for title and labels", GH_ParamAccess.item, Color.Black); // 10
            pManager.AddBooleanParameter("Show", "Show", "Toggle display", GH_ParamAccess.item, true);                // 11
            for (int i = 2; i <= 11; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Purely visual — no outputs
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            // Auto-attach Position ValueList
            if (Params.Input[5].SourceCount == 0)
            {
                var vl = new GH_ValueList();
                vl.CreateAttributes();
                vl.ListMode = GH_ValueListMode.DropDown;
                vl.ListItems.Clear();
                vl.ListItems.Add(new GH_ValueListItem("Top Left", "0"));
                vl.ListItems.Add(new GH_ValueListItem("Top Center", "1"));
                vl.ListItems.Add(new GH_ValueListItem("Top Right", "2"));
                vl.ListItems.Add(new GH_ValueListItem("Bottom Left", "3"));
                vl.ListItems.Add(new GH_ValueListItem("Bottom Center", "4"));
                vl.ListItems.Add(new GH_ValueListItem("Bottom Right", "5"));
                vl.SelectItem(1); // Default: Top Center
                vl.Attributes.PerformLayout();

                var grip = Params.Input[5].Attributes.InputGrip;
                IconHelper.AlignWidget(vl, grip);

                document.AddObject(vl, false);
                Params.Input[5].AddSource(vl);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double minVal = 0;
            double maxVal = 1;
            string title = "Value";
            var colorA = DefaultColorA;
            var colorB = DefaultColorB;
            int position = 1;
            int barWidth = 200;
            int decimals = 2;
            int titleSize = 16;
            int labelSize = 12;
            var textColor = Color.Black;
            bool show = true;

            if (!DA.GetData(0, ref minVal)) return;
            if (!DA.GetData(1, ref maxVal)) return;
            DA.GetData(2, ref title);
            DA.GetData(3, ref colorA);
            DA.GetData(4, ref colorB);
            DA.GetData(5, ref position);
            DA.GetData(6, ref barWidth);
            DA.GetData(7, ref decimals);
            DA.GetData(8, ref titleSize);
            DA.GetData(9, ref labelSize);
            DA.GetData(10, ref textColor);
            DA.GetData(11, ref show);

            _minVal = minVal;
            _maxVal = maxVal;
            _title = title ?? "Value";
            _colorA = colorA;
            _colorB = colorB;
            _textColor = textColor;
            _barWidth = Math.Max(50, barWidth);
            _decimals = Math.Max(0, Math.Min(6, decimals));
            _titleSize = Math.Max(8, Math.Min(48, titleSize));
            _labelSize = Math.Max(8, Math.Min(48, labelSize));
            _position = Math.Max(0, Math.Min(5, position));
            _show = show;

            Message = $"{_minVal:F2} → {_maxVal:F2}";
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if (!_show) return;

            var viewport = args.Viewport;
            var display = args.Display;

            int barHeight = (int)(_labelSize * 1.4);
            int titleGap = _titleSize + 8;
            int labelGap = _labelSize + 4;
            int margin = 30;

            // Total legend height: title + bar + labels
            int totalHeight = titleGap + barHeight + labelGap;

            // Determine screen anchor (bx = bar left edge X, by = bar top edge Y)
            int vpW = viewport.Size.Width;
            int vpH = viewport.Size.Height;
            int bx, by;

            switch (_position)
            {
                case 0: // Top Left
                    bx = margin;
                    by = titleGap + margin;
                    break;
                default: // Top Center
                case 1:
                    bx = vpW / 2 - _barWidth / 2;
                    by = titleGap + margin;
                    break;
                case 2: // Top Right
                    bx = vpW - _barWidth - margin;
                    by = titleGap + margin;
                    break;
                case 3: // Bottom Left
                    bx = margin;
                    by = vpH - totalHeight - margin + titleGap;
                    break;
                case 4: // Bottom Center
                    bx = vpW / 2 - _barWidth / 2;
                    by = vpH - totalHeight - margin + titleGap;
                    break;
                case 5: // Bottom Right
                    bx = vpW - _barWidth - margin;
                    by = vpH - totalHeight - margin + titleGap;
                    break;
            }

            // Draw gradient bar
            for (int i = 0; i < _barWidth; i++)
            {
                double t = (double)i / Math.Max(1, _barWidth - 1);
                var col = LerpColor(_colorA, _colorB, t);

                display.Draw2dLine(
                    new System.Drawing.Point(bx + i, by),
                    new System.Drawing.Point(bx + i, by + barHeight),
                    col, 1);
            }

            // Draw border
            var borderColor = Color.FromArgb(140, _textColor.R, _textColor.G, _textColor.B);
            display.Draw2dLine(new System.Drawing.Point(bx, by), new System.Drawing.Point(bx + _barWidth, by), borderColor, 1);
            display.Draw2dLine(new System.Drawing.Point(bx, by + barHeight), new System.Drawing.Point(bx + _barWidth, by + barHeight), borderColor, 1);
            display.Draw2dLine(new System.Drawing.Point(bx, by), new System.Drawing.Point(bx, by + barHeight), borderColor, 1);
            display.Draw2dLine(new System.Drawing.Point(bx + _barWidth, by), new System.Drawing.Point(bx + _barWidth, by + barHeight), borderColor, 1);

            // Draw title above bar — align left/center/right based on position
            bool isLeft = _position == 0 || _position == 3;
            bool isRight = _position == 2 || _position == 5;
            double titleCharW = _titleSize * 0.55;

            if (isLeft)
            {
                display.Draw2dText(_title, _textColor,
                    new Point2d(bx, by - titleGap), false, _titleSize);
            }
            else if (isRight)
            {
                double titleW = _title.Length * titleCharW;
                display.Draw2dText(_title, _textColor,
                    new Point2d(bx + _barWidth - titleW, by - titleGap), false, _titleSize);
            }
            else
            {
                double titleW = _title.Length * titleCharW;
                display.Draw2dText(_title, _textColor,
                    new Point2d(bx + _barWidth / 2.0 - titleW / 2.0, by - titleGap), false, _titleSize);
            }

            // Draw labels below bar (all use false to avoid vertical centering into bar)
            string fmt = $"F{_decimals}";
            int labelY = by + barHeight + 4;
            double charWidth = _labelSize * 0.55;

            // Min label (left-aligned)
            display.Draw2dText(
                _minVal.ToString(fmt),
                _textColor,
                new Point2d(bx, labelY),
                false, _labelSize);

            // Mid label (manually centered)
            string midText = ((_minVal + _maxVal) / 2.0).ToString(fmt);
            double midTextW = midText.Length * charWidth;
            display.Draw2dText(
                midText,
                _textColor,
                new Point2d(bx + _barWidth / 2.0 - midTextW / 2.0, labelY),
                false, _labelSize);

            // Max label (manually right-aligned)
            string maxText = _maxVal.ToString(fmt);
            double maxTextW = maxText.Length * charWidth;
            display.Draw2dText(
                maxText,
                _textColor,
                new Point2d(bx + _barWidth - maxTextW, labelY),
                false, _labelSize);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // Intentionally empty
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
