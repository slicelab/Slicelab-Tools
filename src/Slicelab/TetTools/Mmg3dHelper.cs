using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Slicelab.TetTools.Native;

namespace Slicelab.TetTools
{
    public static class Mmg3dHelper
    {
        // 6 edge pairs per tetrahedron (indices into 4-vertex tet)
        private static readonly int[,] TetEdges = { {0,1}, {0,2}, {0,3}, {1,2}, {1,3}, {2,3} };

        // 4 faces per tetrahedron (indices into 4-vertex tet, ordered so normals point outward)
        private static readonly int[,] TetFaces = { {0,2,1}, {0,1,3}, {0,3,2}, {1,2,3} };

        /// <summary>
        /// Extracts boundary (surface) triangles from a tet mesh.
        /// A boundary face appears in exactly one tetrahedron.
        /// </summary>
        public static int[] ExtractSurfaceTriangles(int[] tetraIndices)
        {
            int tetCount = tetraIndices.Length / 4;
            var faceCounts = new Dictionary<(int, int, int), (int, int, int)>();

            for (int t = 0; t < tetCount; t++)
            {
                int b = t * 4;
                for (int f = 0; f < 4; f++)
                {
                    int v0 = tetraIndices[b + TetFaces[f, 0]];
                    int v1 = tetraIndices[b + TetFaces[f, 1]];
                    int v2 = tetraIndices[b + TetFaces[f, 2]];

                    // Sort indices for canonical key (order-independent lookup)
                    int a = v0, bb = v1, c = v2;
                    if (a > bb) { int tmp = a; a = bb; bb = tmp; }
                    if (bb > c) { int tmp = bb; bb = c; c = tmp; }
                    if (a > bb) { int tmp = a; a = bb; bb = tmp; }
                    var key = (a, bb, c);

                    if (faceCounts.ContainsKey(key))
                        faceCounts.Remove(key); // shared face → interior, remove
                    else
                        faceCounts[key] = (v0, v1, v2); // first occurrence → keep original winding
                }
            }

            var tris = new int[faceCounts.Count * 3];
            int idx = 0;
            foreach (var face in faceCounts.Values)
            {
                tris[idx++] = face.Item1;
                tris[idx++] = face.Item2;
                tris[idx++] = face.Item3;
            }
            return tris;
        }

        public static void MeshToTriangleArrays(Mesh mesh, out double[] vertices, out int[] triangles)
        {
            Mesh m = mesh.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.UnifyNormals();

            vertices = new double[m.Vertices.Count * 3];
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Point3f v = m.Vertices[i];
                vertices[i * 3] = v.X;
                vertices[i * 3 + 1] = v.Y;
                vertices[i * 3 + 2] = v.Z;
            }

            triangles = new int[m.Faces.Count * 3];
            for (int i = 0; i < m.Faces.Count; i++)
            {
                MeshFace f = m.Faces[i];
                triangles[i * 3] = f.A;
                triangles[i * 3 + 1] = f.B;
                triangles[i * 3 + 2] = f.C;
            }
        }

        public static List<Point3d> ResultToPoints(Mmg3dResult result)
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

        public static DataTree<int> ResultToTetraTree(Mmg3dResult result)
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

        public static List<Mesh> ResultToTetraMeshes(Mmg3dResult result, List<Point3d> points)
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

        public static List<Point3d> ResultToCentroids(Mmg3dResult result, List<Point3d> points)
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

        public static List<Line> ResultToUniqueEdges(Mmg3dResult result, List<Point3d> points)
        {
            int tetCount = result.TetraIndices.Length / 4;
            var seen = new HashSet<(int, int)>();
            var lines = new List<Line>();

            for (int i = 0; i < tetCount; i++)
            {
                int baseIdx = i * 4;
                for (int e = 0; e < 6; e++)
                {
                    int a = result.TetraIndices[baseIdx + TetEdges[e, 0]];
                    int b = result.TetraIndices[baseIdx + TetEdges[e, 1]];

                    var key = a < b ? (a, b) : (b, a);
                    if (seen.Add(key))
                    {
                        lines.Add(new Line(points[a], points[b]));
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// Splits unique edges into inner (fully interior) and surface (on boundary) edges.
        /// Surface edges are those that appear in at least one boundary triangle.
        /// </summary>
        public static (List<Line> innerEdges, List<Line> surfaceEdges) ResultToSplitEdges(
            Mmg3dResult result, List<Point3d> points)
        {
            // Build surface edge set from result triangles (boundary faces)
            var surfaceEdgeSet = new HashSet<(int, int)>();
            int triCount = result.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                int v0 = result.Triangles[i * 3];
                int v1 = result.Triangles[i * 3 + 1];
                int v2 = result.Triangles[i * 3 + 2];

                surfaceEdgeSet.Add(v0 < v1 ? (v0, v1) : (v1, v0));
                surfaceEdgeSet.Add(v1 < v2 ? (v1, v2) : (v2, v1));
                surfaceEdgeSet.Add(v0 < v2 ? (v0, v2) : (v2, v0));
            }

            // Iterate all tet edges, classify as inner or surface
            int tetCount = result.TetraIndices.Length / 4;
            var seen = new HashSet<(int, int)>();
            var inner = new List<Line>();
            var surface = new List<Line>();

            for (int i = 0; i < tetCount; i++)
            {
                int baseIdx = i * 4;
                for (int e = 0; e < 6; e++)
                {
                    int a = result.TetraIndices[baseIdx + TetEdges[e, 0]];
                    int b = result.TetraIndices[baseIdx + TetEdges[e, 1]];

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
            }

            return (inner, surface);
        }

        public static Mesh ResultToSurfaceMesh(Mmg3dResult result)
        {
            var mesh = new Mesh();

            int vertCount = result.Vertices.Length / 3;
            for (int i = 0; i < vertCount; i++)
            {
                mesh.Vertices.Add(
                    result.Vertices[i * 3],
                    result.Vertices[i * 3 + 1],
                    result.Vertices[i * 3 + 2]);
            }

            int triCount = result.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                mesh.Faces.AddFace(
                    result.Triangles[i * 3],
                    result.Triangles[i * 3 + 1],
                    result.Triangles[i * 3 + 2]);
            }

            mesh.Vertices.CullUnused();
            mesh.Normals.ComputeNormals();
            mesh.UnifyNormals();

            if (mesh.SolidOrientation() <= 0)
                mesh.Flip(true, true, true);

            return mesh;
        }

        public static (double min, double max, double avg, double stddev, double pctInRange) ComputeEdgeStats(
            List<Line> edges, double targetLength, double tolerance)
        {
            if (edges.Count == 0)
                return (0, 0, 0, 0, 0);

            double min = double.MaxValue;
            double max = double.MinValue;
            double sum = 0;
            int inRange = 0;
            double lo = targetLength * (1 - tolerance);
            double hi = targetLength * (1 + tolerance);

            for (int i = 0; i < edges.Count; i++)
            {
                double len = edges[i].Length;
                if (len < min) min = len;
                if (len > max) max = len;
                sum += len;
                if (len >= lo && len <= hi) inRange++;
            }

            double avg = sum / edges.Count;

            double sqSum = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                double d = edges[i].Length - avg;
                sqSum += d * d;
            }
            double stddev = Math.Sqrt(sqSum / edges.Count);

            double pctInRange = 100.0 * inRange / edges.Count;

            return (min, max, avg, stddev, pctInRange);
        }

        /// <summary>
        /// Computes per-vertex sizing field from attractor points.
        /// Each attractor has its own target edge length. At each vertex, the closest
        /// attractor's influence determines the local target via linear interpolation
        /// from the attractor's target to the base edge length.
        /// </summary>
        public static double[] ComputeSizingField(
            double[] vertices, int numVertices,
            List<Point3d> attractorPoints, List<double> attractorTargets,
            double baseEdgeLength)
        {
            var sizes = new double[numVertices];

            if (attractorPoints == null || attractorPoints.Count == 0)
            {
                for (int i = 0; i < numVertices; i++)
                    sizes[i] = baseEdgeLength;
                return sizes;
            }

            // Compute bounding box diagonal for falloff radius
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < numVertices; i++)
            {
                double x = vertices[i * 3], y = vertices[i * 3 + 1], z = vertices[i * 3 + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            double falloff = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 0.5;
            if (falloff < 1e-10) falloff = 1.0;

            for (int i = 0; i < numVertices; i++)
            {
                var pt = new Point3d(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
                double bestSize = baseEdgeLength;
                double bestDist = double.MaxValue;

                for (int j = 0; j < attractorPoints.Count; j++)
                {
                    double d = pt.DistanceTo(attractorPoints[j]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        double target = (j < attractorTargets.Count) ? attractorTargets[j] : attractorTargets[attractorTargets.Count - 1];
                        double t = Math.Min(d / falloff, 1.0);
                        bestSize = target + t * (baseEdgeLength - target);
                    }
                }

                sizes[i] = bestSize;
            }

            return sizes;
        }
        /// <summary>
        /// Computes per-vertex sizing field from region containment.
        /// Vertices inside a region adopt that region's target edge length.
        /// Overlapping regions: smallest (densest) wins.
        /// Vertices outside all regions get the base edge length.
        /// </summary>
        public static double[] ComputeRegionSizingField(
            double[] vertices, int numVertices,
            List<Mesh> regionMeshes, List<double> regionLengths,
            double baseEdgeLength)
        {
            var sizes = new double[numVertices];

            // Pre-compute bounding boxes for fast rejection
            var bboxes = new BoundingBox[regionMeshes.Count];
            for (int r = 0; r < regionMeshes.Count; r++)
                bboxes[r] = regionMeshes[r].GetBoundingBox(false);

            for (int i = 0; i < numVertices; i++)
            {
                var pt = new Point3d(
                    vertices[i * 3],
                    vertices[i * 3 + 1],
                    vertices[i * 3 + 2]);

                double targetLen = baseEdgeLength;

                for (int r = 0; r < regionMeshes.Count; r++)
                {
                    // Bounding box pre-filter
                    if (!bboxes[r].Contains(pt))
                        continue;

                    if (regionMeshes[r].IsPointInside(pt, Rhino.RhinoMath.SqrtEpsilon, true))
                    {
                        double regionLen = (r < regionLengths.Count)
                            ? regionLengths[r]
                            : regionLengths[regionLengths.Count - 1];

                        if (regionLen < targetLen)
                            targetLen = regionLen;
                    }
                }

                sizes[i] = targetLen;
            }

            return sizes;
        }
    }
}
