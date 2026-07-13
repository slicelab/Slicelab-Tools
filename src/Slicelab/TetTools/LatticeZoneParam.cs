using System;
using Grasshopper.Kernel;

namespace Slicelab.TetTools
{
    public class LatticeZoneParam : GH_Param<GH_LatticeZone>
    {
        public LatticeZoneParam()
            : base("Lattice Zone", "Z", "Region with assigned lattice type and falloff",
                   "Slicelab Tools", "Tet Tools", GH_ParamAccess.item)
        { }

        public override Guid ComponentGuid => new Guid("D4E5F6A7-1B2C-3D4E-5F6A-7B8C9D0E1F2A");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }
}
