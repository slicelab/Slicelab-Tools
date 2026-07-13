using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Geometry
{
    public class MeshDifferenceComponent : MeshBooleanComponentBase
    {
        public MeshDifferenceComponent()
            : base("Mesh Difference", "SLDiff",
                "Boolean difference (A minus B) of two closed meshes.")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("DFA731FF-8897-4625-96DA-248B91854830");
        protected override int Operation => 1;
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Diff.png");
    }
}
