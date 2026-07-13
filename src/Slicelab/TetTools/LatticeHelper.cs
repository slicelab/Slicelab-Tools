using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools
{
    public static class LatticeHelper
    {
        public static Point3d Midpoint(Point3d a, Point3d b)
        {
            return new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5);
        }

        public static Point3d FaceCenter(Point3d a, Point3d b, Point3d c)
        {
            return new Point3d(
                (a.X + b.X + c.X) / 3.0,
                (a.Y + b.Y + c.Y) / 3.0,
                (a.Z + b.Z + c.Z) / 3.0);
        }

        public static Point3d Centroid(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            return new Point3d(
                (a.X + b.X + c.X + d.X) * 0.25,
                (a.Y + b.Y + c.Y + d.Y) * 0.25,
                (a.Z + b.Z + c.Z + d.Z) * 0.25);
        }

        /// <summary>
        /// Reads TetData from GH param at index 0.
        /// Returns points list, flat int[] of tetra indices (4 per tet), and tet count.
        /// </summary>
        public static bool ReadTetraInputs(IGH_DataAccess DA, GH_Component component,
            out List<Point3d> points, out int[] tetraIndices, out int tetCount)
        {
            return ReadTetraInputs(DA, component, out points, out tetraIndices, out tetCount, out _);
        }

        public static bool ReadTetraInputs(IGH_DataAccess DA, GH_Component component,
            out List<Point3d> points, out int[] tetraIndices, out int tetCount,
            out TetData tetData)
        {
            points = null;
            tetraIndices = null;
            tetCount = 0;
            tetData = null;

            object tetDataObj = null;
            if (!DA.GetData(0, ref tetDataObj) || tetDataObj == null)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No tet data provided.");
                return false;
            }

            var ghTetData = tetDataObj as GH_TetData;
            if (ghTetData?.Value == null)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid tet data.");
                return false;
            }

            var td = ghTetData.Value;
            points = new List<Point3d>(td.Points);
            tetraIndices = td.TetraIndices;
            tetCount = td.TetCount;
            tetData = td;
            return true;
        }

        /// <summary>
        /// Builds a face adjacency map: sorted (min,mid,max) face key -> list of tet indices sharing that face.
        /// </summary>
        public static Dictionary<(int, int, int), List<int>> BuildFaceAdjacency(int[] tetraIndices, int tetCount)
        {
            var map = new Dictionary<(int, int, int), List<int>>();

            int[][] faceTemplate = new int[][]
            {
                new[] { 0, 1, 2 },
                new[] { 0, 1, 3 },
                new[] { 0, 2, 3 },
                new[] { 1, 2, 3 }
            };

            for (int t = 0; t < tetCount; t++)
            {
                int i0 = tetraIndices[t * 4];
                int i1 = tetraIndices[t * 4 + 1];
                int i2 = tetraIndices[t * 4 + 2];
                int i3 = tetraIndices[t * 4 + 3];
                int[] verts = { i0, i1, i2, i3 };

                foreach (var ft in faceTemplate)
                {
                    int a = verts[ft[0]], b = verts[ft[1]], c = verts[ft[2]];
                    // Sort to normalize the face key
                    if (a > b) { int tmp = a; a = b; b = tmp; }
                    if (b > c) { int tmp = b; b = c; c = tmp; }
                    if (a > b) { int tmp = a; a = b; b = tmp; }

                    var key = (a, b, c);
                    if (!map.TryGetValue(key, out var list))
                    {
                        list = new List<int>(2);
                        map[key] = list;
                    }
                    list.Add(t);
                }
            }

            return map;
        }

        /// <summary>
        /// Boundary face information for separating inner/surface struts.
        /// </summary>
        public class BoundaryInfo
        {
            public List<(int va, int vb, int vc, int tetIdx)> Faces;
            public HashSet<int> Vertices;
            public HashSet<(int, int)> Edges; // sorted pairs
            public Point3d[] FaceCenters;
            public Dictionary<(int, int), List<int>> EdgeAdj; // boundary edge → list of boundary face indices
        }

        /// <summary>
        /// Builds boundary face info from face adjacency. Boundary faces have only 1 adjacent tet.
        /// </summary>
        public static BoundaryInfo BuildBoundaryInfo(
            List<Point3d> points, int[] tetraIndices, int tetCount,
            Dictionary<(int, int, int), List<int>> faceAdj)
        {
            var info = new BoundaryInfo
            {
                Faces = new List<(int, int, int, int)>(),
                Vertices = new HashSet<int>(),
                Edges = new HashSet<(int, int)>()
            };

            foreach (var kvp in faceAdj)
            {
                if (kvp.Value.Count != 1) continue;
                int va = kvp.Key.Item1, vb = kvp.Key.Item2, vc = kvp.Key.Item3;
                info.Faces.Add((va, vb, vc, kvp.Value[0]));
                info.Vertices.Add(va);
                info.Vertices.Add(vb);
                info.Vertices.Add(vc);
                info.Edges.Add(va < vb ? (va, vb) : (vb, va));
                info.Edges.Add(vb < vc ? (vb, vc) : (vc, vb));
                info.Edges.Add(va < vc ? (va, vc) : (vc, va));
            }

            // Face centers
            info.FaceCenters = new Point3d[info.Faces.Count];
            for (int fi = 0; fi < info.Faces.Count; fi++)
            {
                var (va, vb, vc, _) = info.Faces[fi];
                info.FaceCenters[fi] = FaceCenter(points[va], points[vb], points[vc]);
            }

            // Boundary edge adjacency (which boundary faces share each boundary edge)
            info.EdgeAdj = new Dictionary<(int, int), List<int>>();
            for (int fi = 0; fi < info.Faces.Count; fi++)
            {
                var (va, vb, vc, _) = info.Faces[fi];
                int[][] edges = { new[]{va,vb}, new[]{vb,vc}, new[]{va,vc} };
                foreach (var e in edges)
                {
                    int ea = Math.Min(e[0], e[1]);
                    int eb = Math.Max(e[0], e[1]);
                    var key = (ea, eb);
                    if (!info.EdgeAdj.TryGetValue(key, out var list))
                    {
                        list = new List<int>(2);
                        info.EdgeAdj[key] = list;
                    }
                    list.Add(fi);
                }
            }

            return info;
        }

        /// <summary>
        /// Generates surface struts as boundary face triangles at original vertex positions.
        /// Used by lattice components where vertices are endpoints (SLLat1, SLLat7, SLLat8, SLLat10).
        /// </summary>
        public static void AddBoundaryFaceTriangles(
            List<Point3d> points, BoundaryInfo boundary,
            List<Line> surfaceStruts, HashSet<Point3d> nodeSet)
        {
            foreach (var (va, vb, vc, _) in boundary.Faces)
            {
                surfaceStruts.Add(new Line(points[va], points[vb]));
                surfaceStruts.Add(new Line(points[vb], points[vc]));
                surfaceStruts.Add(new Line(points[vc], points[va]));
                nodeSet.Add(points[va]);
                nodeSet.Add(points[vb]);
                nodeSet.Add(points[vc]);
            }
        }

        /// <summary>
        /// Generates surface struts as boundary face center Voronoi dual (connecting adjacent face centers
        /// through shared boundary edges) + connecting struts from face centers to boundary tet centroids.
        /// Used by lattice components where no vertices are endpoints (SLLat3, SLLat5, SLLat9).
        /// </summary>
        public static void AddBoundaryVoronoiDual(
            List<Point3d> points, BoundaryInfo boundary, Point3d[] centroids,
            List<Line> surfaceStruts, List<Line> innerStruts, HashSet<Point3d> nodeSet)
        {
            // Voronoi dual on surface: connect face centers of adjacent boundary faces
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

            // Connecting struts: boundary face center → boundary tet centroid
            for (int fi = 0; fi < boundary.Faces.Count; fi++)
            {
                Point3d fc = boundary.FaceCenters[fi];
                Point3d tc = centroids[boundary.Faces[fi].tetIdx];
                innerStruts.Add(new Line(fc, tc));
                nodeSet.Add(fc);
                nodeSet.Add(tc);
            }
        }
    }
}
