using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class CornerToOppositeEdgeMidpointComponent : LatticeComponentBase
    {
        public CornerToOppositeEdgeMidpointComponent()
            : base("Corner to Opp EdgeMidpoint", "SLLat8",
                "Connect each vertex to the opposite edge midpoint within each face (12 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("8F425A43-7518-4642-B22D-F66BBF60CF7F");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat8.png");

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Detail", "SD", "Vertex-to-midpoint connections on boundary faces", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            // Compute surface detail: vertex->opposite midpoint on boundary faces
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var surfaceDetail = new List<Line>();
            var nodeSet = new HashSet<Point3d>(nodes);

            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                Point3d pA = points[va], pB = points[vb], pC = points[vc];

                // Each vertex connects to the midpoint of the opposite edge in this face
                Point3d mBC = LatticeHelper.Midpoint(pB, pC); // opposite va
                Point3d mAC = LatticeHelper.Midpoint(pA, pC); // opposite vb
                Point3d mAB = LatticeHelper.Midpoint(pA, pB); // opposite vc

                surfaceDetail.Add(new Line(pA, mBC));
                surfaceDetail.Add(new Line(pB, mAC));
                surfaceDetail.Add(new Line(pC, mAB));

                nodeSet.Add(pA); nodeSet.Add(pB); nodeSet.Add(pC);
                nodeSet.Add(mBC); nodeSet.Add(mAC); nodeSet.Add(mAB);
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

            // 4 faces of a tetrahedron
            int[][] faces = { new[]{0,1,2}, new[]{0,1,3}, new[]{0,2,3}, new[]{1,2,3} };

            innerStruts = new List<Line>(tetCount * 12);
            surfaceStruts = new List<Line>();

            // Inner struts: vertex→opposite edge midpoint for non-boundary faces only
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d[] v = {
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]
                };

                foreach (var f in faces)
                {
                    // Skip boundary faces — handled by Surface Detail
                    int ia = tetraIndices[b + f[0]];
                    int ib = tetraIndices[b + f[1]];
                    int ic = tetraIndices[b + f[2]];
                    int sa = ia, sb = ib, sc = ic;
                    if (sa > sb) { int tmp = sa; sa = sb; sb = tmp; }
                    if (sb > sc) { int tmp = sb; sb = sc; sc = tmp; }
                    if (sa > sb) { int tmp = sa; sa = sb; sb = tmp; }
                    if (boundaryFaceSet.Contains((sa, sb, sc)))
                        continue;

                    Point3d v0 = v[f[0]], v1 = v[f[1]], v2 = v[f[2]];

                    Point3d m12 = LatticeHelper.Midpoint(v1, v2);
                    Point3d m02 = LatticeHelper.Midpoint(v0, v2);
                    Point3d m01 = LatticeHelper.Midpoint(v0, v1);

                    innerStruts.Add(new Line(v0, m12));
                    innerStruts.Add(new Line(v1, m02));
                    innerStruts.Add(new Line(v2, m01));

                    nodeSet.Add(v0); nodeSet.Add(v1); nodeSet.Add(v2);
                    nodeSet.Add(m12); nodeSet.Add(m02); nodeSet.Add(m01);
                }
            }

            // Surface struts: boundary face triangles at original vertex positions
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
