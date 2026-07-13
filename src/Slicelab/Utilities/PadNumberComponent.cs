using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Utilities
{
    public class PadNumberComponent : GH_Component
    {
        public PadNumberComponent()
            : base("Pad Number", "SLPad",
                "Format an integer with leading zeros to a specified total number of digits.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("F5E6A7B8-C9D0-4E1F-2A3B-4C5D6E7F8091");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Pad.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Number", "N", "Integer to format", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Total Digits", "D", "Total number of digits in output", GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Formatted", "F", "Zero-padded number string", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int number = 0;
            int totalDigits = 3;
            if (!DA.GetData(0, ref number)) return;
            DA.GetData(1, ref totalDigits);

            if (totalDigits < 1) totalDigits = 1;
            DA.SetData(0, number.ToString($"D{totalDigits}"));
        }
    }
}
