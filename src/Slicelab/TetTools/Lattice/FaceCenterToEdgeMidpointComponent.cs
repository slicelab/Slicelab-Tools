using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class FaceCenterToEdgeMidpointComponent : LatticeComponentBase
    {
        public FaceCenterToEdgeMidpointComponent()
            : base("FaceCenter to EdgeMidpoint", "SLLat4",
                "Connect each face center to its 3 edge midpoints (12 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("43D2D847-2B77-4772-8B1B-7FDB4F94F47E");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat4.png");

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Detail", "SD", "FaceCenter to midpoint struts on boundary faces", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            // Compute surface detail: faceCenter→midpoint struts on boundary faces
            var surfaceDetail = new List<Line>();
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var nodeSet = new HashSet<Point3d>(nodes);

            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                Point3d fc = LatticeHelper.FaceCenter(points[va], points[vb], points[vc]);
                Point3d mab = LatticeHelper.Midpoint(points[va], points[vb]);
                Point3d mbc = LatticeHelper.Midpoint(points[vb], points[vc]);
                Point3d mac = LatticeHelper.Midpoint(points[va], points[vc]);

                surfaceDetail.Add(new Line(fc, mab));
                surfaceDetail.Add(new Line(fc, mbc));
                surfaceDetail.Add(new Line(fc, mac));

                nodeSet.Add(fc);
                nodeSet.Add(mab); nodeSet.Add(mbc); nodeSet.Add(mac);
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

            // Build boundary info to identify boundary faces
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundaryFaceSet = new HashSet<(int, int, int)>();
            foreach (var kvp in faceAdj)
            {
                if (kvp.Value.Count == 1)
                    boundaryFaceSet.Add(kvp.Key);
            }

            // 4 faces of a tetrahedron
            int[][] faceTemplate = { new[]{0,1,2}, new[]{0,1,3}, new[]{0,2,3}, new[]{1,2,3} };

            innerStruts = new List<Line>(tetCount * 12);
            surfaceStruts = new List<Line>();

            // Inner struts: faceCenter→midpoint for interior (non-boundary) faces
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d[] v = {
                    points[tetraIndices[b]],
                    points[tetraIndices[b + 1]],
                    points[tetraIndices[b + 2]],
                    points[tetraIndices[b + 3]]
                };

                foreach (var f in faceTemplate)
                {
                    // Check if this face is a boundary face
                    int ia = tetraIndices[b + f[0]];
                    int ib = tetraIndices[b + f[1]];
                    int ic = tetraIndices[b + f[2]];
                    // Sort to match the key format
                    int sa = ia, sb = ib, sc = ic;
                    if (sa > sb) { int tmp = sa; sa = sb; sb = tmp; }
                    if (sb > sc) { int tmp = sb; sb = sc; sc = tmp; }
                    if (sa > sb) { int tmp = sa; sa = sb; sb = tmp; }

                    if (boundaryFaceSet.Contains((sa, sb, sc)))
                        continue; // Skip boundary faces (handled as surface detail)

                    Point3d fc = LatticeHelper.FaceCenter(v[f[0]], v[f[1]], v[f[2]]);
                    Point3d m01 = LatticeHelper.Midpoint(v[f[0]], v[f[1]]);
                    Point3d m12 = LatticeHelper.Midpoint(v[f[1]], v[f[2]]);
                    Point3d m02 = LatticeHelper.Midpoint(v[f[0]], v[f[2]]);

                    innerStruts.Add(new Line(fc, m01));
                    innerStruts.Add(new Line(fc, m12));
                    innerStruts.Add(new Line(fc, m02));

                    nodeSet.Add(fc);
                    nodeSet.Add(m01); nodeSet.Add(m12); nodeSet.Add(m02);
                }
            }

            // Surface struts: boundary face triangles at original vertex positions
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
