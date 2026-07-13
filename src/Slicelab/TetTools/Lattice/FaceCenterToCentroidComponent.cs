using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class FaceCenterToCentroidComponent : LatticeComponentBase
    {
        public FaceCenterToCentroidComponent()
            : base("FaceCenter to Centroid", "SLLat3",
                "Connect face centers to adjacent tetrahedron centroids (shared-face topology)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("3D262B28-344B-4280-BE54-B4A4D0484493");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat3.png");

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            var nodeSet = new HashSet<Point3d>();

            // Precompute all tet centroids
            var centroids = new Point3d[tetCount];
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                centroids[t] = LatticeHelper.Centroid(
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]);
                nodeSet.Add(centroids[t]);
            }

            var adjacency = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            innerStruts = new List<Line>();
            surfaceStruts = new List<Line>();

            // Interior faces (shared by 2 tets): centroid→faceCenter→centroid
            foreach (var kvp in adjacency)
            {
                var tets = kvp.Value;
                if (tets.Count != 2) continue;

                var (a, b, c) = kvp.Key;
                Point3d fc = LatticeHelper.FaceCenter(points[a], points[b], points[c]);
                nodeSet.Add(fc);

                innerStruts.Add(new Line(centroids[tets[0]], fc));
                innerStruts.Add(new Line(fc, centroids[tets[1]]));
            }

            // Surface: boundary Voronoi dual + connecting struts
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, adjacency);
            LatticeHelper.AddBoundaryVoronoiDual(points, boundary, centroids, surfaceStruts, innerStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
