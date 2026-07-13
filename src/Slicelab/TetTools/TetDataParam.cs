using System;
using Grasshopper.Kernel;

namespace Slicelab.TetTools
{
    public class TetDataParam : GH_Param<GH_TetData>
    {
        public TetDataParam()
            : base("Tet Data", "TD", "Combined tetrahedral points and indices",
                   "Slicelab Tools", "Tet Tools", GH_ParamAccess.item)
        { }

        public override Guid ComponentGuid => new Guid("C8D9E0F1-2A3B-4C5D-6E7F-8A9B0C1D2E3F");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-TetData.png");
    }
}
