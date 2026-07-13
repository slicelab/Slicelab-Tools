using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class MidpointToCentroidComponent : LatticeComponentBase
    {
        public MidpointToCentroidComponent()
            : base("Midpoint to Centroid", "SLLat2",
                "Connect each edge midpoint to its tetrahedron centroid (6 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("610C5DD7-0534-4F50-97E5-BBA787D8FE4B");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat2.png");

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Detail", "SD", "Midpoint triangles on boundary faces", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            // Compute surface detail: midpoint→midpoint triangles on boundary faces
            var surfaceDetail = new List<Line>();
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var nodeSet = new HashSet<Point3d>(nodes);

            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                Point3d mab = LatticeHelper.Midpoint(points[va], points[vb]);
                Point3d mbc = LatticeHelper.Midpoint(points[vb], points[vc]);
                Point3d mac = LatticeHelper.Midpoint(points[va], points[vc]);

                surfaceDetail.Add(new Line(mab, mbc));
                surfaceDetail.Add(new Line(mbc, mac));
                surfaceDetail.Add(new Line(mac, mab));

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
            innerStruts = new List<Line>(tetCount * 6);
            surfaceStruts = new List<Line>();
            var nodeSet = new HashSet<Point3d>();

            // 6 edges of a tetrahedron: (0,1), (0,2), (0,3), (1,2), (1,3), (2,3)
            int[][] edges = { new[]{0,1}, new[]{0,2}, new[]{0,3}, new[]{1,2}, new[]{1,3}, new[]{2,3} };

            // Inner struts: all midpoint→centroid
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d p0 = points[tetraIndices[b]];
                Point3d p1 = points[tetraIndices[b + 1]];
                Point3d p2 = points[tetraIndices[b + 2]];
                Point3d p3 = points[tetraIndices[b + 3]];
                Point3d[] verts = { p0, p1, p2, p3 };
                Point3d c = LatticeHelper.Centroid(p0, p1, p2, p3);

                nodeSet.Add(c);
                foreach (var e in edges)
                {
                    Point3d mid = LatticeHelper.Midpoint(verts[e[0]], verts[e[1]]);
                    innerStruts.Add(new Line(mid, c));
                    nodeSet.Add(mid);
                }
            }

            // Surface struts: boundary face triangles at original vertex positions
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
