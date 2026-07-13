using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class EdgeLatticeComponent : LatticeComponentBase
    {
        public EdgeLatticeComponent()
            : base("Edge Lattice", "SLLat10",
                "Tetrahedral edges as lattice struts (6 per tet, deduplicated)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("6C1E2BE3-5710-4516-AB24-1ABC67553E79");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat10.png");

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            innerStruts = new List<Line>();
            surfaceStruts = new List<Line>();
            var nodeSet = new HashSet<Point3d>();

            // Build boundary info to classify edges
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);

            // Deduplicate edges using sorted index pairs
            var edgeSet = new HashSet<(int, int)>();

            // 6 edges per tet: (0,1), (0,2), (0,3), (1,2), (1,3), (2,3)
            int[][] edgeTemplate = { new[]{0,1}, new[]{0,2}, new[]{0,3}, new[]{1,2}, new[]{1,3}, new[]{2,3} };

            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                int i0 = tetraIndices[b], i1 = tetraIndices[b + 1];
                int i2 = tetraIndices[b + 2], i3 = tetraIndices[b + 3];
                int[] idx = { i0, i1, i2, i3 };

                foreach (var e in edgeTemplate)
                {
                    int a = idx[e[0]], bv = idx[e[1]];
                    var key = a < bv ? (a, bv) : (bv, a);
                    edgeSet.Add(key);
                }

                nodeSet.Add(points[i0]); nodeSet.Add(points[i1]);
                nodeSet.Add(points[i2]); nodeSet.Add(points[i3]);
            }

            // Classify edges: boundary edges (both vertices on boundary AND edge in BoundaryInfo.Edges) go to surface
            foreach (var (a, b) in edgeSet)
            {
                var sortedKey = a < b ? (a, b) : (b, a);
                if (boundary.Edges.Contains(sortedKey))
                    surfaceStruts.Add(new Line(points[a], points[b]));
                else
                    innerStruts.Add(new Line(points[a], points[b]));
            }

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
