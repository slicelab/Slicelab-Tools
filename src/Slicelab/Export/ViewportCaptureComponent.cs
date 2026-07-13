using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;

namespace Slicelab.Export
{
    public class ViewportCaptureComponent : GH_Component
    {
        public ViewportCaptureComponent()
            : base("Viewport Capture", "SLCap",
                "Capture a Rhino viewport to an image file.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A6B7C8D9-E0F1-4A2B-3C4D-5E6F7A8B9C24");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Cap.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Viewport", "V", "Name of the viewport to capture", GH_ParamAccess.item, "Perspective");
            pManager.AddTextParameter("Folder Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddTextParameter("Format", "Fmt", "Image format: png, jpg, bmp, tiff", GH_ParamAccess.item, "png");
            pManager.AddIntegerParameter("Width", "W", "Image width in pixels", GH_ParamAccess.item, 1920);
            pManager.AddIntegerParameter("Height", "H", "Image height in pixels", GH_ParamAccess.item, 1080);
            pManager.AddNumberParameter("Scale", "S", "Scale multiplier (1.0 = no scaling)", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Transparent", "T", "Transparent background", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Timestamp", "Ts", "Append timestamp to file name", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Run", "R", "Set to true to capture", GH_ParamAccess.item, false);

            pManager[8].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Full path to saved image", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string viewport = "Perspective";
            string folderPath = "";
            string fileName = "";
            string format = "png";
            int width = 1920;
            int height = 1080;
            double scale = 1.0;
            bool transparent = false;
            bool timestamp = false;
            bool run = false;

            DA.GetData(0, ref viewport);
            if (!DA.GetData(1, ref folderPath)) return;
            if (!DA.GetData(2, ref fileName)) return;
            DA.GetData(3, ref format);
            DA.GetData(4, ref width);
            DA.GetData(5, ref height);
            DA.GetData(6, ref scale);
            DA.GetData(7, ref transparent);
            DA.GetData(8, ref timestamp);
            DA.GetData(9, ref run);

            if (!run) return;

            RhinoView view = FindView(viewport);
            if (view == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Viewport '{viewport}' not found.");
                return;
            }

            Directory.CreateDirectory(folderPath);

            if (timestamp)
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_hh-mmtt").ToLower();
                fileName = $"{fileName}_{stamp}";
            }

            string fullPath = Path.Combine(folderPath, $"{fileName}.{format}");

            Bitmap bitmap = CaptureView(view, width, height, transparent);
            if (bitmap == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to capture viewport.");
                return;
            }

            if (Math.Abs(scale - 1.0) > 1e-6)
                bitmap = ScaleBitmap(bitmap, (int)(width * scale), (int)(height * scale));

            SaveBitmap(bitmap, fullPath, format);
            bitmap.Dispose();

            DA.SetData(0, fullPath);
        }

        internal static RhinoView FindView(string name)
        {
            foreach (var v in RhinoDoc.ActiveDoc.Views)
            {
                if (v.ActiveViewport.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return v;
            }
            return null;
        }

        internal static Bitmap CaptureView(RhinoView view, int width, int height, bool transparent, int passes = -1)
        {
            var capture = new ViewCapture
            {
                Width = width,
                Height = height,
                ScaleScreenItems = false,
                DrawAxes = false,
                DrawGrid = false,
                DrawGridAxes = false,
                TransparentBackground = transparent
            };
            if (passes > 0)
                capture.RealtimeRenderPasses = passes;

            return capture.CaptureToBitmap(view);
        }

        internal static Bitmap ScaleBitmap(Bitmap source, int newWidth, int newHeight)
        {
            var scaled = new Bitmap(newWidth, newHeight);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newWidth, newHeight);
            }
            source.Dispose();
            return scaled;
        }

        internal static void SaveBitmap(Bitmap bitmap, string fullPath, string format)
        {
            switch (format.ToLower())
            {
                case "jpg":
                case "jpeg":
                    bitmap.Save(fullPath, ImageFormat.Jpeg);
                    break;
                case "bmp":
                    bitmap.Save(fullPath, ImageFormat.Bmp);
                    break;
                case "tiff":
                    bitmap.Save(fullPath, ImageFormat.Tiff);
                    break;
                default:
                    bitmap.Save(fullPath, ImageFormat.Png);
                    break;
            }
        }
    }
}
