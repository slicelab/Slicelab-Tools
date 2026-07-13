using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Utilities
{
    public class TextSplitComponent : GH_Component
    {
        public TextSplitComponent()
            : base("Text Split", "SLSplit",
                "Split text at the first occurrence of a delimiter and return the portion after it.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D02");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Split.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "T", "Input text to split", GH_ParamAccess.item);
            pManager.AddTextParameter("Delimiter", "D", "Delimiter string to split on", GH_ParamAccess.item, ":");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "Text after first delimiter occurrence", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string text = "";
            string delimiter = ":";
            if (!DA.GetData(0, ref text)) return;
            DA.GetData(1, ref delimiter);

            int idx = text.IndexOf(delimiter, StringComparison.Ordinal);
            if (idx >= 0 && idx + delimiter.Length < text.Length)
                DA.SetData(0, text.Substring(idx + delimiter.Length));
            else
                DA.SetData(0, "");
        }
    }
}
