using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Slicelab.Export;

namespace Slicelab.PDF
{
    public class PdfViewCaptureComponent : GH_Component
    {
        private PdfViewCaptureElement _cached;

        public PdfViewCaptureComponent()
            : base("PDF Viewport Capture", "SLPVCap",
                "Capture a Rhino viewport as a raster image element for PDF layout.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C08");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PVCap.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Viewport", "V", "Viewport name", GH_ParamAccess.item, "Perspective");
            pManager.AddIntegerParameter("Image Width (px)", "IW", "Image width in pixels", GH_ParamAccess.item, 1920);
            pManager.AddIntegerParameter("Image Height (px)", "IH", "Image height in pixels", GH_ParamAccess.item, 1080);
            pManager.AddBooleanParameter("Transparent", "T", "Transparent background", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Alignment", "A", "Left=0, Center=1, Right=2", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Height", "H", "Fixed height in PDF points (0 = auto-scale to column width)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);
            pManager.AddBooleanParameter("Run", "R", "Trigger capture", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF viewport capture element", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();
            if (Params.Input[4].SourceCount > 0) return;
            var grip = Params.Input[4].Attributes.InputGrip;
            var vl = new GH_ValueList();
            vl.CreateAttributes();
            vl.ListMode = GH_ValueListMode.DropDown;
            vl.NickName = "Alignment";
            vl.ListItems.Clear();
            vl.ListItems.Add(new GH_ValueListItem("Left", "0"));
            vl.ListItems.Add(new GH_ValueListItem("Center", "1"));
            vl.ListItems.Add(new GH_ValueListItem("Right", "2"));
            vl.Attributes.PerformLayout();
            IconHelper.AlignWidget(vl, grip);
            document.AddObject(vl, false);
            Params.Input[4].AddSource(vl);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string viewport = "Perspective";
            int imgWidth = 1920;
            int imgHeight = 1080;
            bool transparent = false;
            int alignment = 0;
            double fixedHeight = 0;
            double spaceAfter = 6;
            bool run = false;

            DA.GetData(0, ref viewport);
            DA.GetData(1, ref imgWidth);
            DA.GetData(2, ref imgHeight);
            DA.GetData(3, ref transparent);
            DA.GetData(4, ref alignment);
            DA.GetData(5, ref fixedHeight);
            DA.GetData(6, ref spaceAfter);
            DA.GetData(7, ref run);

            if (!run)
            {
                if (_cached != null)
                    DA.SetData(0, new GH_PdfElement(_cached));
                return;
            }

            var view = ViewportCaptureComponent.FindView(viewport);
            if (view == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Viewport '{viewport}' not found.");
                return;
            }

            Bitmap bitmap = ViewportCaptureComponent.CaptureView(view, imgWidth, imgHeight, transparent);
            if (bitmap == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to capture viewport.");
                return;
            }

            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                pngBytes = ms.ToArray();
            }
            bitmap.Dispose();

            var element = new PdfViewCaptureElement
            {
                ImageData = pngBytes,
                PixelWidth = imgWidth,
                PixelHeight = imgHeight,
                FixedHeight = fixedHeight,
                Alignment = alignment,
                SpaceAfter = spaceAfter
            };

            _cached = element;
            DA.SetData(0, new GH_PdfElement(element));
        }
    }
}
