using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools.Lattice
{
    public class MidpointToMidpointComponent : LatticeComponentBase
    {
        public MidpointToMidpointComponent()
            : base("Midpoint to Midpoint", "SLLat6",
                "Connect midpoints of opposite edges (3 struts per tet)")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("B72BA650-A33B-469B-854F-D5BC98E44DF0");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Lat6.png");

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Detail", "SD", "Boundary midpoint-to-midpoint connections within each boundary face", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            // Compute surface detail: midpoint→midpoint connections on boundary faces
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);
            var surfaceDetail = new List<Line>();
            var nodeSet = new HashSet<Point3d>(nodes);

            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                Point3d pA = points[va], pB = points[vb], pC = points[vc];
                // Midpoints of the 3 boundary face edges
                Point3d mAB = LatticeHelper.Midpoint(pA, pB);
                Point3d mBC = LatticeHelper.Midpoint(pB, pC);
                Point3d mAC = LatticeHelper.Midpoint(pA, pC);

                surfaceDetail.Add(new Line(mAB, mBC));
                surfaceDetail.Add(new Line(mBC, mAC));
                surfaceDetail.Add(new Line(mAC, mAB));

                nodeSet.Add(mAB); nodeSet.Add(mBC); nodeSet.Add(mAC);
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
            innerStruts = new List<Line>(tetCount * 3);
            surfaceStruts = new List<Line>();
            var nodeSet = new HashSet<Point3d>();

            // Build boundary info for surface struts
            var faceAdj = LatticeHelper.BuildFaceAdjacency(tetraIndices, tetCount);
            var boundary = LatticeHelper.BuildBoundaryInfo(points, tetraIndices, tetCount, faceAdj);

            // Inner struts: 3 opposite edge pairs per tet: (0-1, 2-3), (0-2, 1-3), (0-3, 1-2)
            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                Point3d p0 = points[tetraIndices[b]];
                Point3d p1 = points[tetraIndices[b + 1]];
                Point3d p2 = points[tetraIndices[b + 2]];
                Point3d p3 = points[tetraIndices[b + 3]];

                Point3d m01 = LatticeHelper.Midpoint(p0, p1);
                Point3d m23 = LatticeHelper.Midpoint(p2, p3);
                Point3d m02 = LatticeHelper.Midpoint(p0, p2);
                Point3d m13 = LatticeHelper.Midpoint(p1, p3);
                Point3d m03 = LatticeHelper.Midpoint(p0, p3);
                Point3d m12 = LatticeHelper.Midpoint(p1, p2);

                innerStruts.Add(new Line(m01, m23));
                innerStruts.Add(new Line(m02, m13));
                innerStruts.Add(new Line(m03, m12));

                nodeSet.Add(m01); nodeSet.Add(m23);
                nodeSet.Add(m02); nodeSet.Add(m13);
                nodeSet.Add(m03); nodeSet.Add(m12);
            }

            // Surface struts: boundary face triangles at original vertex positions
            LatticeHelper.AddBoundaryFaceTriangles(points, boundary, surfaceStruts, nodeSet);

            nodes = new List<Point3d>(nodeSet);
        }
    }
}
