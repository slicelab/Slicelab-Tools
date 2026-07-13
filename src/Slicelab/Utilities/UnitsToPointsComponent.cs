using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;

namespace Slicelab.Utilities
{
    public class UnitsToPointsComponent : GH_Component
    {
        public UnitsToPointsComponent()
            : base("Units to Points", "SLToPt",
                "Convert length units to typographic points. Supports MM, CM, Inches, and Feet. Auto-detects Rhino document units.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("038A527B-5D94-4791-ACCA-0D642B4DEE44");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-InPt.png");

        private static readonly double[] Factors = {
            2.8346456693,  // 0 = MM
            28.346456693,  // 1 = CM
            72.0,          // 2 = Inches
            864.0          // 3 = Feet
        };

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Value", "V", "Value in the selected unit", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Unit", "U", "Unit: 0=MM, 1=CM, 2=Inches, 3=Feet", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Points", "Pt", "Value in typographic points", GH_ParamAccess.list);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            if (Params.Input[1].SourceCount > 0) return;

            var grip = Params.Input[1].Attributes.InputGrip;
            var valueList = new GH_ValueList();
            valueList.CreateAttributes();
            valueList.ListMode = GH_ValueListMode.DropDown;
            valueList.NickName = "Unit";

            valueList.ListItems.Clear();
            valueList.ListItems.Add(new GH_ValueListItem("MM", "0"));
            valueList.ListItems.Add(new GH_ValueListItem("CM", "1"));
            valueList.ListItems.Add(new GH_ValueListItem("Inches", "2"));
            valueList.ListItems.Add(new GH_ValueListItem("Feet", "3"));

            int defaultIndex = GetDefaultUnitIndex();
            valueList.SelectItem(defaultIndex);
            valueList.Attributes.PerformLayout();

            IconHelper.AlignWidget(valueList, grip);

            document.AddObject(valueList, false);
            Params.Input[1].AddSource(valueList);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var values = new List<double>();
            if (!DA.GetDataList(0, values)) return;

            int unit = 0;
            if (!DA.GetData(1, ref unit)) return;

            if (unit < 0 || unit >= Factors.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid unit. Use 0=MM, 1=CM, 2=Inches, 3=Feet.");
                return;
            }

            double factor = Factors[unit];
            var results = new List<double>(values.Count);
            for (int i = 0; i < values.Count; i++)
                results.Add(values[i] * factor);

            DA.SetDataList(0, results);
        }

        private static int GetDefaultUnitIndex()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return 0;

            switch (doc.ModelUnitSystem)
            {
                case UnitSystem.Centimeters: return 1;
                case UnitSystem.Inches: return 2;
                case UnitSystem.Feet: return 3;
                default: return 0; // MM for Millimeters and everything else
            }
        }
    }
}
