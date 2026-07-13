using System;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Slicelab.PDF
{
    public class PdfImageElementComponent : GH_Component
    {
        public PdfImageElementComponent()
            : base("PDF Image", "SLPImg",
                "Create an image element for PDF layout.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C03");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PImg.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Image Path", "P", "File path to the image", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height", "H", "Fixed height in points (0 = auto-scale to column width)", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Alignment", "A", "Left=0, Center=1, Right=2", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF image element", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();
            if (Params.Input[2].SourceCount > 0) return;
            var grip = Params.Input[2].Attributes.InputGrip;
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
            Params.Input[2].AddSource(vl);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string imagePath = "";
            double height = 0;
            int alignment = 0;
            double spaceAfter = 6;

            if (!DA.GetData(0, ref imagePath)) return;
            DA.GetData(1, ref height);
            DA.GetData(2, ref alignment);
            DA.GetData(3, ref spaceAfter);

            imagePath = imagePath.Trim();
            if (!File.Exists(imagePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {imagePath}");
                return;
            }

            var element = new PdfImageElement
            {
                ImagePath = imagePath,
                FixedHeight = height,
                Alignment = alignment,
                SpaceAfter = spaceAfter
            };

            DA.SetData(0, new GH_PdfElement(element));
        }
    }
}
