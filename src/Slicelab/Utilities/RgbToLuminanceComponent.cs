using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

namespace Slicelab.Utilities
{
    public class RgbToLuminanceComponent : GH_Component
    {
        // Luminosity weighting schemes: [R, G, B]
        private static readonly double[][] Weights = {
            new[] { 0.2989, 0.5870, 0.1140 }, // 0 = BT.601 (NTSC)
            new[] { 0.2627, 0.6780, 0.0593 }, // 1 = BT.709
            new[] { 0.2126, 0.7152, 0.0722 }, // 2 = BT.709 (sRGB)
            new[] { 0.3333, 0.3333, 0.3333 }  // 3 = Average
        };

        public RgbToLuminanceComponent()
            : base("RGB to Luminance", "SLLum",
                "Convert colors to luminance values (0-1) with selectable weighting scheme. Also outputs per-channel R, G, B, Alpha as 0-1.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("A6F7B8C9-D0E1-4F2A-3B4C-5D6E7F8091A2");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Lum.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddColourParameter("Colors", "C", "Input colors", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Luminosity Type", "L",
                "Weighting scheme:\n0 = BT.601 (R*0.2989, G*0.5870, B*0.1140)\n1 = BT.709 (R*0.2627, G*0.6780, B*0.0593)\n2 = BT.709 sRGB (R*0.2126, G*0.7152, B*0.0722)\n3 = Average (R*0.3333, G*0.3333, B*0.3333)",
                GH_ParamAccess.item, 2);
            pManager.AddBooleanParameter("Invert", "I", "Invert luminance (Black=1, White=0)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Values", "V", "Luminance values (0-1)", GH_ParamAccess.list);
            pManager.AddNumberParameter("R", "R", "Red channel values (0-1)", GH_ParamAccess.list);
            pManager.AddNumberParameter("G", "G", "Green channel values (0-1)", GH_ParamAccess.list);
            pManager.AddNumberParameter("B", "B", "Blue channel values (0-1)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Alpha", "A", "Alpha channel values (0-1)", GH_ParamAccess.list);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            // Auto-attach ValueList for Luminosity Type (input 1)
            if (Params.Input[1].SourceCount == 0)
            {
                var grip = Params.Input[1].Attributes.InputGrip;
                var valueList = new GH_ValueList();
                valueList.CreateAttributes();
                valueList.ListMode = GH_ValueListMode.DropDown;
                valueList.NickName = "Type";

                valueList.ListItems.Clear();
                valueList.ListItems.Add(new GH_ValueListItem("BT.601", "0"));
                valueList.ListItems.Add(new GH_ValueListItem("BT.709", "1"));
                valueList.ListItems.Add(new GH_ValueListItem("BT.709 sRGB", "2"));
                valueList.ListItems.Add(new GH_ValueListItem("Average", "3"));
                valueList.SelectItem(2);
                valueList.Attributes.PerformLayout();
                IconHelper.AlignWidget(valueList, grip);

                document.AddObject(valueList, false);
                Params.Input[1].AddSource(valueList);
            }

            // Auto-attach Boolean Toggle for Invert (input 2)
            if (Params.Input[2].SourceCount == 0)
            {
                var grip = Params.Input[2].Attributes.InputGrip;
                var toggle = new GH_BooleanToggle();
                toggle.CreateAttributes();
                toggle.NickName = "Invert";
                toggle.Value = true;
                toggle.Attributes.PerformLayout();
                IconHelper.AlignWidget(toggle, grip);

                document.AddObject(toggle, false);
                Params.Input[2].AddSource(toggle);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var colors = new List<Color>();
            if (!DA.GetDataList(0, colors)) return;

            int luminosityType = 2;
            if (!DA.GetData(1, ref luminosityType)) return;

            bool invert = true;
            DA.GetData(2, ref invert);

            if (colors.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input color list is empty.");
                return;
            }

            if (luminosityType < 0 || luminosityType >= Weights.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid luminosity type. Use 0-3.");
                return;
            }

            double[] w = Weights[luminosityType];
            var values = new List<double>(colors.Count);
            var reds = new List<double>(colors.Count);
            var greens = new List<double>(colors.Count);
            var blues = new List<double>(colors.Count);
            var alphas = new List<double>(colors.Count);

            foreach (Color c in colors)
            {
                double r = c.R / 255.0;
                double g = c.G / 255.0;
                double b = c.B / 255.0;

                double luminance = w[0] * r + w[1] * g + w[2] * b;
                values.Add(invert ? 1.0 - luminance : luminance);

                reds.Add(r);
                greens.Add(g);
                blues.Add(b);
                alphas.Add(c.A / 255.0);
            }

            DA.SetDataList(0, values);
            DA.SetDataList(1, reds);
            DA.SetDataList(2, greens);
            DA.SetDataList(3, blues);
            DA.SetDataList(4, alphas);
        }
    }
}
