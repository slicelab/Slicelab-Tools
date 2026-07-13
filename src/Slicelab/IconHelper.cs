using System.Drawing;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;

namespace Slicelab
{
    internal static class IconHelper
    {
        /// <summary>
        /// Position a widget (ValueList, BooleanToggle, etc.) aligned to a component's input grip.
        /// Call comp.Attributes.PerformLayout() first, then create and configure the widget,
        /// then call widget.Attributes.PerformLayout(), then call this to set its Pivot.
        /// </summary>
        internal static void AlignWidget(IGH_DocumentObject widget, PointF grip, float gap = 20f)
        {
            widget.Attributes.Pivot = new PointF(
                grip.X - widget.Attributes.Bounds.Width - gap,
                grip.Y - widget.Attributes.Bounds.Height / 2f);
        }

        /// <summary>
        /// Load an embedded PNG at original size (no scaling).
        /// Used for tab icons and plugin manager where GH handles sizing.
        /// </summary>
        internal static Bitmap LoadIconRaw(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string fullName = "Slicelab.Resources." + resourceName;

            using (Stream stream = assembly.GetManifestResourceStream(fullName))
            {
                if (stream == null) return null;
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Load an embedded PNG icon scaled to 24x24 for component icons.
        /// resourceName is the filename without path, e.g. "SL-Union.png"
        /// </summary>
        internal static Bitmap LoadIcon(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string fullName = "Slicelab.Resources." + resourceName;

            using (Stream stream = assembly.GetManifestResourceStream(fullName)
                ?? assembly.GetManifestResourceStream("Slicelab.Resources.SL-Default.png"))
            {
                if (stream == null) return null;
                var original = new Bitmap(stream);
                if (original.Width == 24 && original.Height == 24)
                    return original;

                var scaled = new Bitmap(24, 24);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(original, 0, 0, 24, 24);
                }
                original.Dispose();
                return scaled;
            }
        }
    }
}
