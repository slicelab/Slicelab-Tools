using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

namespace Slicelab.Utilities
{
    public class SortPointsComponent : GH_Component
    {
        public SortPointsComponent()
            : base("Sort Points", "SLSort",
                "Sort and group points along a direction vector with tolerance-based grouping.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quinary;
        public override Guid ComponentGuid => new Guid("9C0D1E2F-3A4B-4C5D-6E7F-809102030409");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Sort.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Points to sort and group", GH_ParamAccess.list);
            pManager.AddVectorParameter("Direction", "D", "Sort direction vector", GH_ParamAccess.item, Vector3d.XAxis);
            pManager.AddNumberParameter("Tolerance", "T", "Grouping tolerance along the direction", GH_ParamAccess.item, 0.01);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Sorted Points", "P", "Points sorted and grouped into branches", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Group Count", "N", "Number of groups", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var points = new List<Point3d>();
            Vector3d direction = Vector3d.XAxis;
            double tolerance = 0.01;

            if (!DA.GetDataList(0, points)) return;
            DA.GetData(1, ref direction);
            DA.GetData(2, ref tolerance);

            if (points.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No points provided.");
                return;
            }

            double dirLen = direction.Length;
            if (dirLen < 1e-12)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Direction vector is zero-length.");
                return;
            }

            // Project each point onto the direction vector and store index + projection
            Vector3d unitDir = direction / dirLen;
            var projected = new List<(int index, double proj)>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                double proj = points[i].X * unitDir.X + points[i].Y * unitDir.Y + points[i].Z * unitDir.Z;
                projected.Add((i, proj));
            }

            // Sort by projection value
            projected.Sort((a, b) => a.proj.CompareTo(b.proj));

            // Group by tolerance
            var tree = new DataTree<Point3d>();
            int groupIndex = 0;
            int start = 0;

            while (start < projected.Count)
            {
                double groupStart = projected[start].proj;
                int end = start;
                while (end < projected.Count && Math.Abs(projected[end].proj - groupStart) <= tolerance)
                    end++;

                var path = new GH_Path(groupIndex);
                for (int i = start; i < end; i++)
                    tree.Add(points[projected[i].index], path);

                groupIndex++;
                start = end;
            }

            DA.SetDataTree(0, tree);
            DA.SetData(1, groupIndex);
        }
    }
}
