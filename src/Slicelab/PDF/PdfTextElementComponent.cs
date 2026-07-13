using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using PdfSharpCore.Drawing;

namespace Slicelab.PDF
{
    public class PdfTextElementComponent : GH_Component
    {
        public PdfTextElementComponent()
            : base("PDF Text", "SLPTxt",
                "Create a text element for PDF layout.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C02");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PTxt.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "T", "Text content", GH_ParamAccess.item);
            pManager.AddTextParameter("Font Family", "FF", "Font family name", GH_ParamAccess.item, "Arial");
            pManager.AddNumberParameter("Font Size", "FS", "Font size in points", GH_ParamAccess.item, 12.0);
            pManager.AddIntegerParameter("Font Style", "St", "Regular=0, Bold=1, Italic=2, BoldItalic=3", GH_ParamAccess.item, 0);
            pManager.AddColourParameter("Color", "C", "Text color", GH_ParamAccess.item, Color.Black);
            pManager.AddIntegerParameter("Alignment", "A", "Left=0, Center=1, Right=2", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF text element", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            AttachFontList(document);
            AttachValueList(document, 3, "Font Style", new[] { "Regular", "Bold", "Italic", "BoldItalic" }, new[] { "0", "1", "2", "3" });
            AttachValueList(document, 5, "Alignment", new[] { "Left", "Center", "Right" }, new[] { "0", "1", "2" });
        }

        private void AttachFontList(GH_Document document)
        {
            if (Params.Input[1].SourceCount > 0) return;
            var fonts = PdfFontResolver.GetAvailableFontNames();
            if (fonts.Count == 0) return;
            var grip = Params.Input[1].Attributes.InputGrip;
            var vl = new GH_ValueList();
            vl.CreateAttributes();
            vl.ListMode = GH_ValueListMode.DropDown;
            vl.NickName = "Font";
            vl.ListItems.Clear();
            foreach (var f in fonts)
                vl.ListItems.Add(new GH_ValueListItem(f, $"\"{f}\""));
            for (int i = 0; i < vl.ListItems.Count; i++)
            {
                if (vl.ListItems[i].Name.Equals("Arial", StringComparison.OrdinalIgnoreCase))
                {
                    vl.SelectItem(i);
                    break;
                }
            }
            vl.Attributes.PerformLayout();
            IconHelper.AlignWidget(vl, grip);
            document.AddObject(vl, false);
            Params.Input[1].AddSource(vl);
        }

        private void AttachValueList(GH_Document document, int inputIndex, string name, string[] labels, string[] values)
        {
            if (Params.Input[inputIndex].SourceCount > 0) return;
            var grip = Params.Input[inputIndex].Attributes.InputGrip;
            var vl = new GH_ValueList();
            vl.CreateAttributes();
            vl.ListMode = GH_ValueListMode.DropDown;
            vl.NickName = name;
            vl.ListItems.Clear();
            for (int i = 0; i < labels.Length; i++)
                vl.ListItems.Add(new GH_ValueListItem(labels[i], values[i]));
            vl.Attributes.PerformLayout();
            IconHelper.AlignWidget(vl, grip);
            document.AddObject(vl, false);
            Params.Input[inputIndex].AddSource(vl);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string text = "";
            string fontFamily = "Arial";
            double fontSize = 12;
            int fontStyle = 0;
            Color color = Color.Black;
            int alignment = 0;
            double spaceAfter = 6;

            if (!DA.GetData(0, ref text)) return;
            DA.GetData(1, ref fontFamily);
            DA.GetData(2, ref fontSize);
            DA.GetData(3, ref fontStyle);
            DA.GetData(4, ref color);
            DA.GetData(5, ref alignment);
            DA.GetData(6, ref spaceAfter);

            XFontStyle xStyle;
            switch (fontStyle)
            {
                case 1: xStyle = XFontStyle.Bold; break;
                case 2: xStyle = XFontStyle.Italic; break;
                case 3: xStyle = XFontStyle.BoldItalic; break;
                default: xStyle = XFontStyle.Regular; break;
            }

            var element = new PdfTextElement
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontStyle = xStyle,
                Color = XColor.FromArgb(color.A, color.R, color.G, color.B),
                Alignment = alignment,
                SpaceAfter = spaceAfter
            };

            DA.SetData(0, new GH_PdfElement(element));
        }
    }
}
