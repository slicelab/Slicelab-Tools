using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class FaceCenterToFaceCenterComponent : LatticeComponentBase
    {
        public FaceCenterToFaceCenterComponent()
            : base("FaceCenter to FaceCenter", "SLLat5",
                "Connect all pairs of face centers within each tetrahedron (6 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("3E4FE9C9-65D2-42DC-B73B-BBE2EB2E200A");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat5.png");

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            var nodeSet = new HashSet<Point3d>();

            int[][] faceTemplate = { new[]{0,1,2}, new[]{0,1,3}, new[]{0,2,3}, new[]{1,2,3} };

            // Precompute centroids for boundary Voronoi dual
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

            innerStruts = new List<Line>(tetCount * 6);
            surfaceStruts = new List<Line>();

            // Inner struts: all 6 face center pairs within each tet
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d[] v = {
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]
                };

                Point3d[] fc = new Point3d[4];
                for (int i = 0; i < 4; i++)
                {
                    fc[i] = LatticeHelper.FaceCenter(v[faceTemplate[i][0]], v[faceTemplate[i][1]], v[faceTemplate[i][2]]);
                    nodeSet.Add(fc[i]);
                }

                // All 6 pairs: (0,1), (0,2), (0,3), (1,2), (1,3), (2,3)
                for (int i = 0; i < 4; i++)
                    for (int j = i + 1; j < 4; j++)
                        innerStruts.Add(new Line(fc[i], fc[j]));
            }

            // Surface: boundary Voronoi dual only (no connecting struts —
            // within-tet face center pairs already connect boundary face centers to interior)
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            foreach (var kvp in boundary.EdgeAdj)
            {
                var faces = kvp.Value;
                if (faces.Count >= 2)
                {
                    Point3d fc1 = boundary.FaceCenters[faces[0]];
                    Point3d fc2 = boundary.FaceCenters[faces[1]];
                    surfaceStruts.Add(new Line(fc1, fc2));
                    nodeSet.Add(fc1);
                    nodeSet.Add(fc2);
                }
            }

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
