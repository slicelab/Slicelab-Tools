using System;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;

namespace Slicelab.Export
{
    public class RenderCaptureComponent : GH_Component
    {
        public RenderCaptureComponent()
            : base("Render Capture", "SLRen",
                "Capture a Rhino viewport with raytraced render passes to an image file. The viewport display mode must be set to 'Raytraced' for passes to take effect.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("B7C8D9E0-F1A2-4B3C-4D5E-6F7A8B9C0D25");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Ren.png");

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
            pManager.AddIntegerParameter("Passes", "Pa", "Number of raytraced render passes", GH_ParamAccess.item, 100);
            pManager.AddBooleanParameter("Timestamp", "Ts", "Append timestamp to file name", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Run", "R", "Set to true to capture", GH_ParamAccess.item, false);

            pManager[9].Optional = true;
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
            int passes = 100;
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
            DA.GetData(8, ref passes);
            DA.GetData(9, ref timestamp);
            DA.GetData(10, ref run);

            if (!run) return;

            var view = ViewportCaptureComponent.FindView(viewport);
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

            Bitmap bitmap = ViewportCaptureComponent.CaptureView(view, width, height, transparent, passes);
            if (bitmap == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to capture viewport.");
                return;
            }

            if (Math.Abs(scale - 1.0) > 1e-6)
                bitmap = ViewportCaptureComponent.ScaleBitmap(bitmap, (int)(width * scale), (int)(height * scale));

            ViewportCaptureComponent.SaveBitmap(bitmap, fullPath, format);
            bitmap.Dispose();

            DA.SetData(0, fullPath);
        }
    }
}
