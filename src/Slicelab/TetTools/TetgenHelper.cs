using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Slicelab.TetTools.Native;

namespace Slicelab.TetTools
{
    /// <summary>
    /// Helper methods for converting between Rhino/Grasshopper types and TetGen interop data.
    /// </summary>
    public static class TetgenHelper
    {
        /// <summary>
        /// Extracts vertex and face data from a Rhino Mesh into flat arrays suitable for TetGen.
        /// </summary>
        public static void MeshToArrays(Mesh mesh, out double[] vertices, out int[] faceIndices, out int[] faceSizes)
        {
            Mesh m = mesh.DuplicateMesh();
            m.UnifyNormals();

            // Vertices as flat double array
            vertices = new double[m.Vertices.Count * 3];
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3f v = m.Vertices[i];
                vertices[i * 3] = v.X;
                vertices[i * 3 + 1] = v.Y;
                vertices[i * 3 + 2] = v.Z;
            }

            // Count total face indices and face sizes
            var faceIdxList = new List<int>();
            var faceSizeList = new List<int>();

            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces[i];
                if (f.IsQuad)
                {
                    faceIdxList.Add(f.A);
                    faceIdxList.Add(f.B);
                    faceIdxList.Add(f.C);
                    faceIdxList.Add(f.D);
                    faceSizeList.Add(4);
                }
                else
                {
                    faceIdxList.Add(f.A);
                    faceIdxList.Add(f.B);
                    faceIdxList.Add(f.C);
                    faceSizeList.Add(3);
                }
            }

            faceIndices = faceIdxList.ToArray();
            faceSizes = faceSizeList.ToArray();
        }

        /// <summary>
        /// Computes the average edge length of a mesh's topology edges.
        /// Used to auto-derive a target tet size when no explicit constraint is given.
        /// </summary>
        public static double AverageEdgeLength(Mesh mesh)
        {
            var edges = mesh.TopologyEdges;
            if (edges.Count == 0) return 1.0;

            double total = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                total += edges.EdgeLine(i).Length;
            }
            return total / edges.Count;
        }

        /// <summary>
        /// Converts TetGen result vertices to a list of Point3d.
        /// </summary>
        public static List<Point3d> ResultToPoints(TetgenResult result)
        {
            int count = result.Vertices.Length / 3;
            var points = new List<Point3d>(count);
            for (int i = 0; i < count; i++)
            {
                points.Add(new Point3d(
                    result.Vertices[i * 3],
                    result.Vertices[i * 3 + 1],
                    result.Vertices[i * 3 + 2]));
            }
            return points;
        }

        /// <summary>
        /// Converts TetGen result tetrahedra to a DataTree with one branch per tet (4 indices each).
        /// </summary>
        public static DataTree<int> ResultToTetraTree(TetgenResult result)
        {
            var tree = new DataTree<int>();
            int tetCount = result.TetraIndices.Length / 4;
            for (int i = 0; i < tetCount; i++)
            {
                var path = new GH_Path(i);
                tree.Add(result.TetraIndices[i * 4], path);
                tree.Add(result.TetraIndices[i * 4 + 1], path);
                tree.Add(result.TetraIndices[i * 4 + 2], path);
                tree.Add(result.TetraIndices[i * 4 + 3], path);
            }
            return tree;
        }

        /// <summary>
        /// Creates individual 4-face meshes for each tetrahedron.
        /// </summary>
        public static List<Mesh> ResultToTetraMeshes(TetgenResult result, List<Point3d> points)
        {
            int tetCount = result.TetraIndices.Length / 4;
            var meshes = new List<Mesh>(tetCount);

            for (int i = 0; i < tetCount; i++)
            {
                int i0 = result.TetraIndices[i * 4];
                int i1 = result.TetraIndices[i * 4 + 1];
                int i2 = result.TetraIndices[i * 4 + 2];
                int i3 = result.TetraIndices[i * 4 + 3];

                var m = new Mesh();
                m.Vertices.Add(points[i0]);
                m.Vertices.Add(points[i1]);
                m.Vertices.Add(points[i2]);
                m.Vertices.Add(points[i3]);

                // 4 triangular faces of a tetrahedron
                m.Faces.AddFace(0, 1, 2);
                m.Faces.AddFace(0, 1, 3);
                m.Faces.AddFace(0, 2, 3);
                m.Faces.AddFace(1, 2, 3);

                m.Normals.ComputeNormals();
                m.UnifyNormals();
                meshes.Add(m);
            }

            return meshes;
        }

        /// <summary>
        /// Computes the centroid of each tetrahedron (average of its 4 vertices).
        /// </summary>
        public static List<Point3d> ResultToCentroids(TetgenResult result, List<Point3d> points)
        {
            int tetCount = result.TetraIndices.Length / 4;
            var centroids = new List<Point3d>(tetCount);

            for (int i = 0; i < tetCount; i++)
            {
                Point3d p0 = points[result.TetraIndices[i * 4]];
                Point3d p1 = points[result.TetraIndices[i * 4 + 1]];
                Point3d p2 = points[result.TetraIndices[i * 4 + 2]];
                Point3d p3 = points[result.TetraIndices[i * 4 + 3]];

                centroids.Add(new Point3d(
                    (p0.X + p1.X + p2.X + p3.X) * 0.25,
                    (p0.Y + p1.Y + p2.Y + p3.Y) * 0.25,
                    (p0.Z + p1.Z + p2.Z + p3.Z) * 0.25));
            }

            return centroids;
        }

        /// <summary>
        /// Extracts unique edges from the TetGen result as Line segments.
        /// </summary>
        public static List<Line> ResultToUniqueEdges(TetgenResult result, List<Point3d> points)
        {
            var seen = new HashSet<(int, int)>();
            var lines = new List<Line>();

            int edgeCount = result.EdgeIndices.Length / 2;
            for (int i = 0; i < edgeCount; i++)
            {
                int a = result.EdgeIndices[i * 2];
                int b = result.EdgeIndices[i * 2 + 1];

                // Sort the tuple so (a,b) and (b,a) are treated as the same edge
                var key = a < b ? (a, b) : (b, a);
                if (seen.Add(key))
                {
                    lines.Add(new Line(points[a], points[b]));
                }
            }

            return lines;
        }

        /// <summary>
        /// Splits unique edges into inner (fully interior) and surface (on boundary) edges.
        /// Surface edges are those that appear in at least one boundary face from TetGen output.
        /// </summary>
        public static (List<Line> innerEdges, List<Line> surfaceEdges) ResultToSplitEdges(
            TetgenResult result, List<Point3d> points)
        {
            // Build surface edge set from TetGen face output
            var surfaceEdgeSet = new HashSet<(int, int)>();
            int idx = 0;
            for (int i = 0; i < result.FaceSizes.Length; i++)
            {
                int size = result.FaceSizes[i];
                for (int j = 0; j < size; j++)
                {
                    int a = result.FaceIndices[idx + j];
                    int b = result.FaceIndices[idx + (j + 1) % size];
                    surfaceEdgeSet.Add(a < b ? (a, b) : (b, a));
                }
                idx += size;
            }

            // Classify each unique edge as inner or surface
            var seen = new HashSet<(int, int)>();
            var inner = new List<Line>();
            var surface = new List<Line>();

            int edgeCount = result.EdgeIndices.Length / 2;
            for (int i = 0; i < edgeCount; i++)
            {
                int a = result.EdgeIndices[i * 2];
                int b = result.EdgeIndices[i * 2 + 1];

                var key = a < b ? (a, b) : (b, a);
                if (seen.Add(key))
                {
                    var line = new Line(points[a], points[b]);
                    if (surfaceEdgeSet.Contains(key))
                        surface.Add(line);
                    else
                        inner.Add(line);
                }
            }

            return (inner, surface);
        }

        /// <summary>
        /// Builds a single surface mesh from the TetGen result face data.
        /// </summary>
        public static Mesh ResultToSurfaceMesh(TetgenResult result)
        {
            var mesh = new Mesh();

            // Add vertices
            int vertCount = result.Vertices.Length / 3;
            for (int i = 0; i < vertCount; i++)
            {
                mesh.Vertices.Add(
                    result.Vertices[i * 3],
                    result.Vertices[i * 3 + 1],
                    result.Vertices[i * 3 + 2]);
            }

            // Add faces
            int idx = 0;
            for (int i = 0; i < result.FaceSizes.Length; i++)
            {
                int size = result.FaceSizes[i];
                if (size == 3)
                {
                    mesh.Faces.AddFace(
                        result.FaceIndices[idx],
                        result.FaceIndices[idx + 1],
                        result.FaceIndices[idx + 2]);
                }
                else if (size == 4)
                {
                    mesh.Faces.AddFace(
                        result.FaceIndices[idx],
                        result.FaceIndices[idx + 1],
                        result.FaceIndices[idx + 2],
                        result.FaceIndices[idx + 3]);
                }
                idx += size;
            }

            mesh.Vertices.CullUnused();
            mesh.Normals.ComputeNormals();
            mesh.UnifyNormals();

            if (mesh.SolidOrientation() <= 0)
            {
                mesh.Flip(true, true, true);
            }

            return mesh;
        }

        /// <summary>
        /// Returns an info string summarizing the TetGen result.
        /// </summary>
        public static string ResultToInfo(TetgenResult result)
        {
            int tetCount = result.TetraIndices.Length / 4;
            int vertCount = result.Vertices.Length / 3;
            int edgeCount = result.EdgeIndices.Length / 2;
            int faceCount = result.FaceSizes.Length;

            return string.Format(
                "Tetrahedra: {0}\nVertices: {1}\nEdges: {2}\nFaces: {3}",
                tetCount, vertCount, edgeCount, faceCount);
        }
    }
}
