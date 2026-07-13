using System;
using System.Drawing;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Slicelab.TetTools
{
    /// <summary>
    /// Interface for Tet Tools components that have a Run button.
    /// </summary>
    public interface ITetButtonComponent
    {
        bool ShouldRun { get; set; }
    }

    /// <summary>
    /// Custom GH component attributes that add a "Run" button below the component.
    /// Uses GH_Capsule for cross-platform rendering (macOS + Windows).
    /// </summary>
    public class TetButtonAttributes : GH_ComponentAttributes
    {
        private RectangleF _buttonBounds;
        private bool _mouseDown;
        private const int ButtonHeight = 22;
        private const int ButtonPadding = 3;

        public TetButtonAttributes(GH_Component owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();

            RectangleF bounds = GH_Convert.ToRectangle(Bounds);
            bounds.Height += ButtonHeight + ButtonPadding * 2;

            _buttonBounds = new RectangleF(
                bounds.Left + ButtonPadding,
                bounds.Bottom - ButtonHeight - ButtonPadding,
                bounds.Width - ButtonPadding * 2,
                ButtonHeight);

            Bounds = bounds;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects) return;

            // Use GH_Capsule — the native Grasshopper rendering primitive
            // GH_Palette.Black gives a dark button that stands out on any component state
            GH_Palette palette = _mouseDown ? GH_Palette.Grey : GH_Palette.Black;
            GH_Capsule button = GH_Capsule.CreateTextCapsule(
                _buttonBounds, _buttonBounds, palette, "Run", 2, 0);
            button.Render(graphics, Selected, Owner.Locked, false);
            button.Dispose();
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_buttonBounds.Contains(e.CanvasLocation))
            {
                _mouseDown = true;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_mouseDown)
            {
                _mouseDown = false;
                if (_buttonBounds.Contains(e.CanvasLocation))
                {
                    if (Owner is ITetButtonComponent comp)
                    {
                        comp.ShouldRun = true;
                        Owner.ExpireSolution(true);
                    }
                }
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Release;
            }
            return base.RespondToMouseUp(sender, e);
        }
    }
}
