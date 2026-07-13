using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class CentroidToCentroidComponent : LatticeComponentBase
    {
        public CentroidToCentroidComponent()
            : base("Centroid to Centroid", "SLLat9",
                "Connect centroids of adjacent tetrahedra through shared faces (Voronoi dual)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("8CA4C93C-1A6C-4188-95AB-51099D522AAB");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat9.png");

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            innerStruts = new List<Line>();
            surfaceStruts = new List<Line>();
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

            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);

            // Inner struts: centroid->centroid for shared interior faces (2 adjacent tets)
            foreach (var kvp in faceAdj)
            {
                var tets = kvp.Value;
                if (tets.Count == 2)
                {
                    innerStruts.Add(new Line(centroids[tets[0]], centroids[tets[1]]));
                }
            }

            // Surface struts: boundary Voronoi dual + connecting struts
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            LatticeHelper.AddBoundaryVoronoiDual(points, boundary, centroids, surfaceStruts, innerStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
