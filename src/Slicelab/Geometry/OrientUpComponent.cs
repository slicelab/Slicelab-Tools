using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Slicelab.Utilities
{
    public class OrientUpComponent : GH_Component
    {
        public OrientUpComponent()
            : base("Orient Up", "SLUp",
                "Ensure planes have Z-axis pointing up and vectors have positive Z component.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("B1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D5E");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-OrientUp.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "Plane or Vector3d to orient upward", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "Corrected plane or vector", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Flipped", "F", "Whether the input was flipped", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object obj = null;
            if (!DA.GetData(0, ref obj)) return;

            // Unwrap GH_Goo wrappers
            if (obj is IGH_Goo goo)
                goo.CastTo(out obj);

            if (obj is Plane plane)
            {
                if (plane.ZAxis.Z < 0)
                {
                    plane.Flip();
                    DA.SetData(0, plane);
                    DA.SetData(1, true);
                }
                else
                {
                    DA.SetData(0, plane);
                    DA.SetData(1, false);
                }
            }
            else if (obj is Vector3d vec)
            {
                if (vec.Z < 0)
                {
                    DA.SetData(0, new Vector3d(-vec.X, -vec.Y, -vec.Z));
                    DA.SetData(1, true);
                }
                else
                {
                    DA.SetData(0, vec);
                    DA.SetData(1, false);
                }
            }
            else
            {
                DA.SetData(0, obj);
                DA.SetData(1, false);
            }
        }
    }
}
