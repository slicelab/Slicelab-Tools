using System;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;

namespace Slicelab.Visualization
{
    public class ImageInfoComponent : GH_Component
    {
        public ImageInfoComponent()
            : base("Image Info", "SLImgInfo",
                "Read the dimensions of an image file.",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("D3E4F5A6-B7C8-4D9E-0F1A-2B3C4D5E6F33");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-ImgInfo.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Image Path", "P", "File path to the image", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Width", "W", "Image width in pixels", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Height", "H", "Image height in pixels", GH_ParamAccess.item);
            pManager.AddNumberParameter("Aspect Ratio", "AR", "Width / Height", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string path = "";
            if (!DA.GetData(0, ref path)) return;

            path = path.Trim();
            if (!File.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {path}");
                return;
            }

            try
            {
                using (var img = Image.FromFile(path))
                {
                    int w = img.Width;
                    int h = img.Height;
                    DA.SetData(0, w);
                    DA.SetData(1, h);
                    DA.SetData(2, (double)w / h);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to read image: " + ex.Message);
            }
        }
    }
}
