using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace Slicelab.Utilities
{
    public class GeometryFilterComponent : GH_Component
    {
        public GeometryFilterComponent()
            : base("Geometry Filter", "SLFilt",
                "Filter a list of mixed geometry by type. Auto-attaches a dropdown for type selection.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("78A7E6AE-ACC2-41F2-91E8-15DA4C040701");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Type.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "List of mixed geometry to filter", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Type", "T", "Type code to filter by: 0=Mesh, 1=Brep, 2=Surface, 3=Curve, 4=Point, 5=SubD, 6=Line, 7=Arc, 8=Circle, -1=Others", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Filtered geometry matching the selected type", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Indices", "I", "Indices of matching items from the input list", GH_ParamAccess.list);
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
            valueList.NickName = "Type";

            valueList.ListItems.Clear();
            valueList.ListItems.Add(new GH_ValueListItem("Mesh", "0"));
            valueList.ListItems.Add(new GH_ValueListItem("Brep", "1"));
            valueList.ListItems.Add(new GH_ValueListItem("Surface", "2"));
            valueList.ListItems.Add(new GH_ValueListItem("Curve", "3"));
            valueList.ListItems.Add(new GH_ValueListItem("Point", "4"));
            valueList.ListItems.Add(new GH_ValueListItem("SubD", "5"));
            valueList.ListItems.Add(new GH_ValueListItem("Line", "6"));
            valueList.ListItems.Add(new GH_ValueListItem("Arc", "7"));
            valueList.ListItems.Add(new GH_ValueListItem("Circle", "8"));
            valueList.ListItems.Add(new GH_ValueListItem("Others", "-1"));
            valueList.Attributes.PerformLayout();

            IconHelper.AlignWidget(valueList, grip);

            document.AddObject(valueList, false);
            Params.Input[1].AddSource(valueList);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<IGH_GeometricGoo>();
            if (!DA.GetDataList(0, items)) return;

            int targetType = 0;
            if (!DA.GetData(1, ref targetType)) return;

            var filtered = new List<IGH_GeometricGoo>();
            var indices = new List<int>();

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                int code = GetTypeCode(items[i]);
                if (code == targetType)
                {
                    filtered.Add(items[i]);
                    indices.Add(i);
                }
            }

            DA.SetDataList(0, filtered);
            DA.SetDataList(1, indices);
        }

        private static int GetTypeCode(IGH_GeometricGoo goo)
        {
            if (goo is GH_Mesh) return 0;
            if (goo is GH_Brep) return 1;
            if (goo is GH_Surface) return 2;
            if (goo is GH_SubD) return 5;
            if (goo is GH_Line) return 6;
            if (goo is GH_Arc) return 7;
            if (goo is GH_Circle) return 8;
            if (goo is GH_Curve) return 3;
            if (goo is GH_Point) return 4;
            return -1;
        }
    }
}
