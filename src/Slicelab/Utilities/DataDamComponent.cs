using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Slicelab.Utilities
{
    public class DataDamComponent : GH_Component
    {
        public DataDamComponent()
            : base("Data Dam", "SLDam",
                "Pass or block data based on a boolean toggle.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C01");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Dam.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Pass Data", "P", "True to pass data through, false to block", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("Data", "D", "Data to pass or block", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Passed data (null if blocked)", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            if (Params.Input[0].SourceCount == 0)
            {
                var grip = Params.Input[0].Attributes.InputGrip;
                var toggle = new GH_BooleanToggle();
                toggle.CreateAttributes();
                toggle.NickName = "Pass";
                toggle.Value = false;
                toggle.Attributes.PerformLayout();
                IconHelper.AlignWidget(toggle, grip);
                document.AddObject(toggle, false);
                Params.Input[0].AddSource(toggle);
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool pass = false;
            DA.GetData(0, ref pass);

            if (pass)
            {
                object data = null;
                if (DA.GetData(1, ref data))
                    DA.SetData(0, data);
            }
        }
    }
}
