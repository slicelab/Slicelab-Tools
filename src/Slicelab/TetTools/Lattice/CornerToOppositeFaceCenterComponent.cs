using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class CornerToOppositeFaceCenterComponent : LatticeComponentBase
    {
        public CornerToOppositeFaceCenterComponent()
            : base("Corner to Opp FaceCenter", "SLLat7",
                "Connect each vertex to the center of the opposite face (4 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("35071EE3-05C0-4A36-B183-4C26AB13297F");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat7.png");

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Detail", "SD", "Vertex-to-face-center struts on boundary faces", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            // Compute surface detail: vertex→face center on boundary faces
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var surfaceDetail = new List<Line>();
            var nodeSet = new HashSet<Point3d>(nodes);

            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                Point3d pA = points[va], pB = points[vb], pC = points[vc];
                Point3d fc = LatticeHelper.FaceCenter(pA, pB, pC);

                surfaceDetail.Add(new Line(pA, fc));
                surfaceDetail.Add(new Line(pB, fc));
                surfaceDetail.Add(new Line(pC, fc));

                nodeSet.Add(pA); nodeSet.Add(pB); nodeSet.Add(pC);
                nodeSet.Add(fc);
            }

            nodes = new List<Point3d>(nodeSet);

            DA.SetDataList(0, innerStruts);
            DA.SetDataList(1, surfaceStruts);
            DA.SetDataList(2, surfaceDetail);
            DA.SetDataList(3, nodes);
            DA.SetData(4, $"Inner struts: {innerStruts.Count}\nSurface struts: {surfaceStruts.Count}\nSurface detail: {surfaceDetail.Count}\nNodes: {nodes.Count}");
        }

        protected override void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes)
        {
            var nodeSet = new HashSet<Point3d>();

            // Build boundary info to skip boundary faces from inner struts
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var boundaryFaceSet = new HashSet<(int, int, int)>();
            foreach (var kvp in faceAdj)
            {
                if (kvp.Value.Count == 1)
                    boundaryFaceSet.Add(kvp.Key);
            }

            // v0 <-> face(1,2,3), v1 <-> face(0,2,3), v2 <-> face(0,1,3), v3 <-> face(0,1,2)
            int[][] oppFaces = { new[]{1,2,3}, new[]{0,2,3}, new[]{0,1,3}, new[]{0,1,2} };

            innerStruts = new List<Line>(tetCount * 4);
            surfaceStruts = new List<Line>();

            // Inner struts: ALL vertex→opposite face center struts (including boundary faces).
            // The opposite vertex is always interior to the boundary face, so this strut
            // is the connector from interior to the boundary face center.
            // Surface Detail adds the ADDITIONAL on-face star pattern separately.
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d[] v = {
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]
                };

                for (int i = 0; i < 4; i++)
                {
                    var f = oppFaces[i];
                    Point3d fc = LatticeHelper.FaceCenter(v[f[0]], v[f[1]], v[f[2]]);
                    innerStruts.Add(new Line(v[i], fc));
                    nodeSet.Add(v[i]);
                    nodeSet.Add(fc);
                }
            }

            // Surface struts: boundary face triangles at original vertex positions
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
