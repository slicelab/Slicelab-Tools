using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Geometry
{
    public class MeshUnionComponent : MeshBooleanComponentBase
    {
        public MeshUnionComponent()
            : base("Mesh Union", "SLUnion",
                "Boolean union of two closed meshes.")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("1AAAFDAE-CAD4-4DDB-A773-1CC42463B36E");
        protected override int Operation => 0;
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Union.png");
    }
}
