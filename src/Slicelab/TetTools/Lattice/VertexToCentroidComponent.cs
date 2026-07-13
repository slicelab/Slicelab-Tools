using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class VertexToCentroidComponent : LatticeComponentBase
    {
        public VertexToCentroidComponent()
            : base("Vertex to Centroid", "SLLat1",
                "Connect each vertex to its tetrahedron centroid (4 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("15EA022E-70A2-436D-8DF5-383CDBD59224");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat1.png");

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            innerStruts = new List<Line>(tetCount * 4);
            surfaceStruts = new List<Line>();
            var nodeSet = new HashSet<Point3d>();

            // Precompute centroids
            var centroids = new Point3d[tetCount];
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                centroids[t] = LatticeHelper.Centroid(
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]);
            }

            // Inner struts: all vertex→centroid
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d p0 = points[tetraIndices[b]];
                Point3d p1 = points[tetraIndices[b + 1]];
                Point3d p2 = points[tetraIndices[b + 2]];
                Point3d p3 = points[tetraIndices[b + 3]];
                Point3d c = centroids[t];

                innerStruts.Add(new Line(p0, c));
                innerStruts.Add(new Line(p1, c));
                innerStruts.Add(new Line(p2, c));
                innerStruts.Add(new Line(p3, c));

                nodeSet.Add(p0); nodeSet.Add(p1);
                nodeSet.Add(p2); nodeSet.Add(p3);
                nodeSet.Add(c);
            }

            // Surface struts: boundary face triangles at original vertex positions
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
