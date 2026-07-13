using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Slicelab.PDF
{
    public class PdfPageSettingsComponent : GH_Component
    {
        public PdfPageSettingsComponent()
            : base("PDF Page Settings", "SLPage",
                "Configure PDF page size, orientation, margins, and column layout.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C01");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Page.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Page Size", "PS", "A4=0, Letter=1, Tabloid=2, Legal=3, A3=4, Custom=5", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Orientation", "O", "Portrait=0, Landscape=1", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Margin Top", "MT", "Top margin in points", GH_ParamAccess.item, 36.0);
            pManager.AddNumberParameter("Margin Right", "MR", "Right margin in points", GH_ParamAccess.item, 36.0);
            pManager.AddNumberParameter("Margin Bottom", "MB", "Bottom margin in points", GH_ParamAccess.item, 36.0);
            pManager.AddNumberParameter("Margin Left", "ML", "Left margin in points", GH_ParamAccess.item, 36.0);
            pManager.AddIntegerParameter("Columns", "C", "Number of columns (1-4)", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Gutter", "Gu", "Space between columns in points", GH_ParamAccess.item, 18.0);
            pManager.AddNumberParameter("Custom Width", "CW", "Custom page width in points (only when PageSize=Custom)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Custom Height", "CH", "Custom page height in points (only when PageSize=Custom)", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Page Settings", "PS", "Page settings for PDF Doc Export", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Page dimensions summary", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();
            AttachValueList(document, 0, "Page Size", new[] { "A4", "Letter", "Tabloid", "Legal", "A3", "Custom" }, new[] { "0", "1", "2", "3", "4", "5" });
            AttachValueList(document, 1, "Orientation", new[] { "Portrait", "Landscape" }, new[] { "0", "1" });
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
            int pageSize = 0, orientation = 0, columns = 1;
            double mt = 36, mr = 36, mb = 36, ml = 36, gutter = 18, cw = 0, ch = 0;

            DA.GetData(0, ref pageSize);
            DA.GetData(1, ref orientation);
            DA.GetData(2, ref mt);
            DA.GetData(3, ref mr);
            DA.GetData(4, ref mb);
            DA.GetData(5, ref ml);
            DA.GetData(6, ref columns);
            DA.GetData(7, ref gutter);
            DA.GetData(8, ref cw);
            DA.GetData(9, ref ch);

            if (pageSize < 0 || pageSize > 5) pageSize = 0;
            if (columns < 1) columns = 1;
            if (columns > 4) columns = 4;

            bool landscape = orientation == 1;

            if (pageSize == 5 && (cw <= 0 || ch <= 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Custom page size requires positive Custom Width and Custom Height.");
                return;
            }

            var settings = new PageSettings(pageSize, landscape, mt, mr, mb, ml, columns, gutter, cw, ch);

            DA.SetData(0, new GH_PageSettings(settings));

            string orient = landscape ? "Landscape" : "Portrait";
            string colInfo = columns > 1 ? $", {columns} columns ({settings.ColumnWidth:F1}pt each)" : "";
            DA.SetData(1, $"{PageSettings.SizeName(pageSize)} {orient}: {settings.PageWidth:F1} x {settings.PageHeight:F1}pt, content area {settings.ContentWidth:F1} x {settings.ContentHeight:F1}pt{colInfo}");
        }
    }
}
