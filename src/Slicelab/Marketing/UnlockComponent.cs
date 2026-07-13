using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Slicelab.Marketing
{
    public class UnlockComponent : GH_Component
    {
        public UnlockComponent()
            : base("Unlock", "🔒",
                "Connect all inputs to unlock something.",
                "Slicelab Tools", "!SL")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("5A6B7C8D-9E0F-4A1B-2C3D-4E5F6A7B8C02");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Unlock.png");

        public override void CreateAttributes()
        {
            m_attributes = new UnlockAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Left side (2)
            pManager.AddGenericParameter("A1", "A1", "Left input 1", GH_ParamAccess.item);
            pManager.AddGenericParameter("A2", "A2", "Left input 2", GH_ParamAccess.item);
            // Right side (2)
            pManager.AddGenericParameter("B1", "B1", "Right input 1", GH_ParamAccess.item);
            pManager.AddGenericParameter("B2", "B2", "Right input 2", GH_ParamAccess.item);
            // Top side (2)
            pManager.AddGenericParameter("C1", "C1", "Top input 1", GH_ParamAccess.item);
            pManager.AddGenericParameter("C2", "C2", "Top input 2", GH_ParamAccess.item);
            // Bottom side (2)
            pManager.AddGenericParameter("D1", "D1", "Bottom input 1", GH_ParamAccess.item);
            pManager.AddGenericParameter("D2", "D2", "Bottom input 2", GH_ParamAccess.item);

            for (int i = 0; i < 8; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // No outputs
        }

        public bool IsUnlocked { get; private set; }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int connected = Params.Input.Count(p => p.SourceCount > 0);
            IsUnlocked = connected == 8;
            Message = $"{connected}/8";
        }
    }

    public class SideInputParamAttributes : GH_LinkedParamAttributes
    {
        private PointF _gripPoint;

        public SideInputParamAttributes(IGH_Param param, IGH_Attributes parent)
            : base(param, parent)
        {
        }

        public void SetGripPoint(PointF grip)
        {
            _gripPoint = grip;
        }

        public override PointF InputGrip => _gripPoint;

        protected override void Layout()
        {
            Pivot = _gripPoint;
            Bounds = new RectangleF(_gripPoint.X - 5, _gripPoint.Y - 5, 10, 10);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires) return;
            base.Render(canvas, graphics, channel);
        }
    }

    public class UnlockAttributes : GH_ComponentAttributes
    {
        private const float LockedWidth = 120f;
        private const float LockedHeight = 120f;
        private const float UnlockedWidth = 200f;
        private const float UnlockedHeight = 240f;
        private const float GripInset = 35f;

        private Bitmap _unlockImage;
        private string _caption;
        private static Bitmap _lockImage;
        private static readonly string[] ImageNames =
        {
            "unlock1.png", "unlock2.png", "unlock3.png", "unlock4.png",
            "unlock5.png", "unlock6.png", "unlock7.png"
        };
        private static readonly string[] Captions =
        {
            "You connected 8 inputs. Was it worth it?",
            "Congratulations. This component does nothing.",
            "I'm not superstitious, but I am a little stitious.",
            "Would I rather be feared or loved? Both.",
            "You miss 100% of the shots you don't take.\n— Wayne Gretzky — Michael Scott",
            "Why are you the way that you are?",
            "Sometimes I'll start a definition\nand I don't even know where it's going.",
            "I declare MESH BOOLEAN!",
            "8 wires for this. You could've remeshed something by now.",
        };
        private static readonly Random _rng = new Random();

        public UnlockAttributes(UnlockComponent owner) : base(owner) { }

        private void LoadRandomImage()
        {
            if (_unlockImage != null) return;
            try
            {
                string pick = ImageNames[_rng.Next(ImageNames.Length)];
                string fullName = "Slicelab.Resources." + pick;
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(fullName))
                {
                    if (stream != null)
                        _unlockImage = new Bitmap(stream);
                }
                _caption = Captions[_rng.Next(Captions.Length)];
            }
            catch
            {
                _unlockImage = null;
            }
        }

        private void DisposeImage()
        {
            _unlockImage?.Dispose();
            _unlockImage = null;
            _caption = null;
        }

        protected override void Layout()
        {
            bool unlocked = (Owner as UnlockComponent)?.IsUnlocked ?? false;
            float w = unlocked ? UnlockedWidth : LockedWidth;
            float h = unlocked ? UnlockedHeight : LockedHeight;

            Pivot = GH_Convert.ToPoint(Pivot);
            Bounds = new RectangleF(
                Pivot.X - w / 2f,
                Pivot.Y - h / 2f,
                w,
                h);

            var inputs = Owner.Params.Input;
            if (inputs.Count < 8) return;

            float leftX = Bounds.Left;
            float[] leftYs = SpaceEvenly(Bounds.Top + GripInset, Bounds.Bottom - GripInset, 2);
            for (int i = 0; i < 2; i++)
                SetParamGrip(inputs[i], new PointF(leftX, leftYs[i]));

            float rightX = Bounds.Right;
            float[] rightYs = SpaceEvenly(Bounds.Top + GripInset, Bounds.Bottom - GripInset, 2);
            for (int i = 0; i < 2; i++)
                SetParamGrip(inputs[2 + i], new PointF(rightX, rightYs[i]));

            float topY = Bounds.Top;
            float[] topXs = SpaceEvenly(Bounds.Left + GripInset, Bounds.Right - GripInset, 2);
            for (int i = 0; i < 2; i++)
                SetParamGrip(inputs[4 + i], new PointF(topXs[i], topY));

            float bottomY = Bounds.Bottom;
            float[] bottomXs = SpaceEvenly(Bounds.Left + GripInset, Bounds.Right - GripInset, 2);
            for (int i = 0; i < 2; i++)
                SetParamGrip(inputs[6 + i], new PointF(bottomXs[i], bottomY));
        }

        private void SetParamGrip(IGH_Param param, PointF grip)
        {
            var attr = param.Attributes as SideInputParamAttributes;
            if (attr == null)
            {
                attr = new SideInputParamAttributes(param, this);
                param.Attributes = attr;
            }
            attr.SetGripPoint(grip);
            attr.ExpireLayout();
            attr.PerformLayout();
        }

        private static float[] SpaceEvenly(float start, float end, int count)
        {
            var positions = new float[count];
            if (count == 1)
            {
                positions[0] = (start + end) / 2f;
                return positions;
            }
            float step = (end - start) / (count - 1);
            for (int i = 0; i < count; i++)
                positions[i] = start + i * step;
            return positions;
        }

        private static int GetSide(int paramIndex)
        {
            if (paramIndex < 2) return 0;  // left
            if (paramIndex < 4) return 1;  // right
            if (paramIndex < 6) return 2;  // top
            return 3;                       // bottom
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires)
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                var inputs = Owner.Params.Input;
                for (int i = 0; i < inputs.Count; i++)
                {
                    var param = inputs[i];
                    if (param.SourceCount == 0) continue;

                    PointF end = param.Attributes.InputGrip;
                    int side = GetSide(i);

                    foreach (var source in param.Sources)
                    {
                        PointF start = source.Attributes.OutputGrip;
                        Color wireColor = Color.FromArgb(150, 0, 0, 0);

                        float dist = (float)Math.Sqrt(
                            (end.X - start.X) * (end.X - start.X) +
                            (end.Y - start.Y) * (end.Y - start.Y));
                        float tangent = Math.Max(dist * 0.4f, 30f);

                        PointF cp1 = new PointF(start.X + tangent, start.Y);
                        PointF cp2;

                        switch (side)
                        {
                            case 0: cp2 = new PointF(end.X - tangent, end.Y); break;
                            case 1: cp2 = new PointF(end.X + tangent, end.Y); break;
                            case 2: cp2 = new PointF(end.X, end.Y - tangent); break;
                            case 3: cp2 = new PointF(end.X, end.Y + tangent); break;
                            default: cp2 = new PointF(end.X - tangent, end.Y); break;
                        }

                        using (var pen = new Pen(wireColor, 3f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            graphics.DrawBezier(pen, start, cp1, cp2, end);
                        }
                    }
                }
                return;
            }

            if (channel == GH_CanvasChannel.Objects)
            {
                // Use GH_Capsule for standard Grasshopper component body (no grips — we draw those manually)
                GH_Capsule capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
                capsule.Render(graphics, Selected, Owner.Locked, false);
                capsule.Dispose();

                graphics.SmoothingMode = SmoothingMode.HighQuality;
                bool unlocked = (Owner as UnlockComponent)?.IsUnlocked ?? false;

                if (unlocked)
                {
                    LoadRandomImage();
                    if (_unlockImage != null)
                    {
                        float inset = GripInset + 5f;
                        float imgSize = Bounds.Width - inset * 2;
                        var imgRect = new RectangleF(
                            Bounds.X + inset, Bounds.Y + inset,
                            imgSize, imgSize);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(_unlockImage, imgRect);
                    }
                    if (_caption != null)
                    {
                        float captionTop = Bounds.Y + GripInset + 5f + (Bounds.Width - (GripInset + 5f) * 2) + 2f;
                        var captionRect = new RectangleF(
                            Bounds.X + GripInset, captionTop,
                            Bounds.Width - GripInset * 2, Bounds.Bottom - captionTop - 16f);
                        using (var font = new Font("Arial", 6.5f, FontStyle.Italic))
                        using (var brush = new SolidBrush(Color.FromArgb(200, 60, 60, 60)))
                        {
                            var sf = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center,
                                Trimming = StringTrimming.EllipsisWord
                            };
                            graphics.DrawString(_caption, font, brush, captionRect, sf);
                        }
                    }
                }
                else
                {
                    DisposeImage();

                    // Draw unlock0.png as lock image
                    if (_lockImage == null)
                    {
                        try
                        {
                            var asm = Assembly.GetExecutingAssembly();
                            using (var stream = asm.GetManifestResourceStream("Slicelab.Resources.unlock0.png"))
                            {
                                if (stream != null)
                                    _lockImage = new Bitmap(stream);
                            }
                        }
                        catch { }
                    }
                    if (_lockImage != null)
                    {
                        float imgSize = Math.Min(Bounds.Width, Bounds.Height) * 0.45f;
                        float imgX = Bounds.X + (Bounds.Width - imgSize) / 2f;
                        float imgY = Bounds.Y + (Bounds.Height - imgSize) / 2f - 8f;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(_lockImage, imgX, imgY, imgSize, imgSize);
                    }

                    // Hint text
                    using (var font = new Font("Arial", 6.5f))
                    using (var brush = new SolidBrush(Color.FromArgb(160, 80, 80, 80)))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center };
                        graphics.DrawString("Connect all 8 inputs to unlock", font, brush,
                            Bounds.X + Bounds.Width / 2f, Bounds.Bottom - 28f, sf);
                    }
                }

                // Connected count
                int connected = Owner.Params.Input.Count(p => p.SourceCount > 0);
                using (var font = new Font("Arial", 7f))
                using (var brush = new SolidBrush(Color.FromArgb(180, 80, 80, 80)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    graphics.DrawString($"{connected}/8", font, brush,
                        Bounds.X + Bounds.Width / 2f, Bounds.Bottom - 16f, sf);
                }

                // Grip dots on all 4 sides — CMYK colors per side
                var inputs = Owner.Params.Input;
                // Side colors: Left=Cyan, Right=K(dark gray), Top=Magenta, Bottom=Yellow
                Color[] sideColors =
                {
                    Color.FromArgb(255, 0, 174, 239),    // Cyan (left)
                    Color.FromArgb(255, 88, 89, 91),     // K dark gray (right)
                    Color.FromArgb(255, 236, 0, 140),    // Magenta (top)
                    Color.FromArgb(255, 255, 242, 0)     // Yellow (bottom)
                };
                for (int i = 0; i < inputs.Count; i++)
                {
                    var grip = inputs[i].Attributes.InputGrip;
                    bool hasSource = inputs[i].SourceCount > 0;
                    int side = GetSide(i);
                    var dotRect = new RectangleF(grip.X - 3.5f, grip.Y - 3.5f, 7, 7);
                    using (var fillBrush = new SolidBrush(hasSource ? sideColors[side] : Color.White))
                        graphics.FillEllipse(fillBrush, dotRect);
                    using (var pen = new Pen(Color.Black, 1.5f))
                        graphics.DrawEllipse(pen, dotRect);
                }
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
    }
}
