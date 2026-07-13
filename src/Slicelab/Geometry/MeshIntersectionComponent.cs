using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Geometry
{
    public class MeshIntersectionComponent : MeshBooleanComponentBase
    {
        public MeshIntersectionComponent()
            : base("Mesh Intersection", "SLIsect",
                "Boolean intersection of two closed meshes.")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("50D76788-4F8D-481B-BA7C-C5E196FDE123");
        protected override int Operation => 2;
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Intersect.png");
    }
}
