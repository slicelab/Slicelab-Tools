using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;

namespace Slicelab.Utilities
{
    public class DataConcatenatorComponent : GH_Component
    {
        public DataConcatenatorComponent()
            : base("Data Concatenator", "SLConcat",
                "Collect values from connected sources and format as 'NickName: Value' pairs.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("3C4D5E6F-7A8B-4C9D-0E1F-2A3B4C5D6E03");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Concat.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Connect multiple sources (sliders, panels, etc.)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "I", "Formatted 'NickName: Value' per source", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var dataList = new List<object>();
            if (!DA.GetDataList(0, dataList)) return;

            var sources = Params.Input[0].Sources;
            var sb = new StringBuilder();

            for (int i = 0; i < sources.Count && i < dataList.Count; i++)
            {
                string name = sources[i].NickName;
                string value = dataList[i]?.ToString() ?? "null";
                sb.AppendFormat("{0}: {1}\n", name, value);
            }

            DA.SetData(0, sb.ToString().TrimEnd('\n'));
        }
    }
}
