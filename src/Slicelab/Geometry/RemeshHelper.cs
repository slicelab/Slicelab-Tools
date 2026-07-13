using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace Slicelab.Geometry
{
    public class EmbedResult
    {
        public Mesh ModifiedMesh;
        public int[] RequiredEdges;    // pairs of vertex indices
        public int[] RequiredVertices; // all snapped vertex indices
    }

    public static class RemeshHelper
    {
        public static EmbedResult EmbedCurvesInMesh(Mesh mesh, List<Curve> featureCurves, double targetEdgeLength)
        {
            Mesh m = mesh.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.RebuildNormals();

            var snappedSet = new HashSet<int>();
            var allEdgePairs = new List<int>();
            var allReqVerts = new List<int>();

            // Build adjacency: for each vertex, the set of vertices sharing a face
            var adjacency = new Dictionary<int, HashSet<int>>();
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                MeshFace f = m.Faces[fi];
                int[] fv = f.IsQuad ? new[] { f.A, f.B, f.C, f.D } : new[] { f.A, f.B, f.C };
                for (int a = 0; a < fv.Length; a++)
                {
                    for (int b = a + 1; b < fv.Length; b++)
                    {
                        if (!adjacency.ContainsKey(fv[a]))
                            adjacency[fv[a]] = new HashSet<int>();
                        if (!adjacency.ContainsKey(fv[b]))
                            adjacency[fv[b]] = new HashSet<int>();
                        adjacency[fv[a]].Add(fv[b]);
                        adjacency[fv[b]].Add(fv[a]);
                    }
                }
            }

            foreach (var curve in featureCurves)
            {
                if (curve == null) continue;

                double curveLen = curve.GetLength();
                if (curveLen <= 0) continue;

                int segCount = Math.Max(1, (int)Math.Round(curveLen / targetEdgeLength));
                var samplePts = new List<Point3d>();
                double[] divParams = curve.DivideByCount(segCount, true);
                if (divParams == null) continue;
                for (int i = 0; i < divParams.Length; i++)
                    samplePts.Add(curve.PointAt(divParams[i]));

                // Snap each sample point to the nearest mesh vertex
                var snappedVerts = new List<int>();
                foreach (var samplePt in samplePts)
                {
                    MeshPoint mp = m.ClosestMeshPoint(samplePt, 0.0);
                    if (mp == null) continue;

                    // Find nearest vertex on the face
                    MeshFace face = m.Faces[mp.FaceIndex];
                    int[] faceVerts = face.IsQuad
                        ? new[] { face.A, face.B, face.C, face.D }
                        : new[] { face.A, face.B, face.C };

                    int bestVert = -1;
                    double bestDist = double.MaxValue;
                    for (int i = 0; i < faceVerts.Length; i++)
                    {
                        double d = samplePt.DistanceTo(new Point3d(m.Vertices[faceVerts[i]]));
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestVert = faceVerts[i];
                        }
                    }

                    if (bestVert < 0) continue;

                    // Degenerate triangle guard: skip if an adjacent vertex is already snapped
                    bool adjacentSnapped = false;
                    if (adjacency.ContainsKey(bestVert))
                    {
                        foreach (int neighbor in adjacency[bestVert])
                        {
                            if (snappedSet.Contains(neighbor))
                            {
                                adjacentSnapped = true;
                                break;
                            }
                        }
                    }

                    if (adjacentSnapped && snappedSet.Contains(bestVert))
                        continue;

                    // Snap the vertex to the projected point on the mesh surface
                    m.Vertices.SetVertex(bestVert, mp.Point);
                    snappedSet.Add(bestVert);
                    snappedVerts.Add(bestVert);
                }

                // Build required edge pairs from consecutive snapped vertices
                for (int i = 0; i < snappedVerts.Count - 1; i++)
                {
                    int v0 = snappedVerts[i];
                    int v1 = snappedVerts[i + 1];
                    if (v0 != v1)
                    {
                        allEdgePairs.Add(v0);
                        allEdgePairs.Add(v1);
                    }
                }
            }

            allReqVerts.AddRange(snappedSet);

            return new EmbedResult
            {
                ModifiedMesh = m,
                RequiredEdges = allEdgePairs.ToArray(),
                RequiredVertices = allReqVerts.ToArray()
            };
        }

        public static int[] EmbedPointsInMesh(Mesh mesh, List<Point3d> featurePoints)
        {
            var reqVerts = new HashSet<int>();

            foreach (var pt in featurePoints)
            {
                MeshPoint mp = mesh.ClosestMeshPoint(pt, 0.0);
                if (mp == null) continue;

                MeshFace face = mesh.Faces[mp.FaceIndex];
                int[] faceVerts = face.IsQuad
                    ? new[] { face.A, face.B, face.C, face.D }
                    : new[] { face.A, face.B, face.C };

                int bestVert = -1;
                double bestDist = double.MaxValue;
                for (int i = 0; i < faceVerts.Length; i++)
                {
                    double d = pt.DistanceTo(new Point3d(mesh.Vertices[faceVerts[i]]));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestVert = faceVerts[i];
                    }
                }

                if (bestVert < 0) continue;

                mesh.Vertices.SetVertex(bestVert, mp.Point);
                reqVerts.Add(bestVert);
            }

            var result = new int[reqVerts.Count];
            reqVerts.CopyTo(result);
            return result;
        }

        public static void MeshToArrays(Mesh mesh, out double[] vertices, out int[] triangles)
        {
            Mesh m = mesh.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();

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

        public static Mesh ArraysToMesh(double[] vertices, int[] triangles)
        {
            var mesh = new Mesh();
            int vertCount = vertices.Length / 3;
            for (int i = 0; i < vertCount; i++)
            {
                mesh.Vertices.Add(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
            }

            int triCount = triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                mesh.Faces.AddFace(triangles[i * 3], triangles[i * 3 + 1], triangles[i * 3 + 2]);
            }

            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
            mesh.Compact();
            mesh.Normals.ComputeNormals();
            mesh.UnifyNormals();
            return mesh;
        }

        public static double[] ComputeSizingField(
            double[] vertices, int numVertices,
            List<Point3d> attractorPoints, List<Curve> attractorCurves,
            double minEdgeLength, double maxEdgeLength, double falloffRadius)
        {
            var sizes = new double[numVertices];
            bool hasAttractors = (attractorPoints != null && attractorPoints.Count > 0)
                              || (attractorCurves != null && attractorCurves.Count > 0);

            if (!hasAttractors)
            {
                for (int i = 0; i < numVertices; i++)
                    sizes[i] = maxEdgeLength;
                return sizes;
            }

            for (int i = 0; i < numVertices; i++)
            {
                var pt = new Point3d(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
                double minDist = double.MaxValue;

                if (attractorPoints != null)
                {
                    for (int j = 0; j < attractorPoints.Count; j++)
                    {
                        double d = pt.DistanceTo(attractorPoints[j]);
                        if (d < minDist) minDist = d;
                    }
                }

                if (attractorCurves != null)
                {
                    for (int j = 0; j < attractorCurves.Count; j++)
                    {
                        if (attractorCurves[j] == null) continue;
                        attractorCurves[j].ClosestPoint(pt, out double t);
                        double d = pt.DistanceTo(attractorCurves[j].PointAt(t));
                        if (d < minDist) minDist = d;
                    }
                }

                // Lerp between min and max edge length based on distance / falloff
                double t_param = Math.Min(minDist / falloffRadius, 1.0);
                sizes[i] = minEdgeLength + t_param * (maxEdgeLength - minEdgeLength);
            }

            return sizes;
        }

        public static Mesh BrepToMesh(Brep brep, double targetEdgeLength)
        {
            var mp = new MeshingParameters();
            mp.MaximumEdgeLength = targetEdgeLength * 2.0;
            mp.MinimumEdgeLength = targetEdgeLength * 0.5;
            mp.GridAspectRatio = 0;

            Mesh[] meshes = Mesh.CreateFromBrep(brep, mp);
            if (meshes == null || meshes.Length == 0)
                return null;

            var combined = new Mesh();
            for (int i = 0; i < meshes.Length; i++)
                combined.Append(meshes[i]);

            // Merge near-coincident seam vertices with tolerance.
            // CreateFromBrep produces per-face patches with vertices that differ
            // by tiny floating-point amounts at shared edges — CombineIdentical
            // and Weld can't bridge that gap since they require exact matches.
            double tol = RhinoDoc.ActiveDoc != null
                ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance
                : 0.001;
            MergeCloseVertices(combined, tol);

            combined.Faces.CullDegenerateFaces();
            combined.Compact();
            combined.Faces.ConvertQuadsToTriangles();
            combined.Normals.ComputeNormals();
            combined.UnifyNormals();
            return combined;
        }

        /// <summary>
        /// Merges vertices that are within tolerance distance of each other.
        /// Unlike CombineIdentical (exact match only), this handles the tiny
        /// floating-point differences at Brep face seams.
        /// </summary>
        private static void MergeCloseVertices(Mesh mesh, double tolerance)
        {
            var pts = mesh.Vertices;
            int n = pts.Count;
            if (n == 0) return;

            double tol2 = tolerance * tolerance;

            // map[i] = canonical index that vertex i should merge into
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = i;

            // For each vertex, find earlier vertices within tolerance
            for (int i = 1; i < n; i++)
            {
                var pi = (Point3d)pts[i];
                for (int j = 0; j < i; j++)
                {
                    if (map[j] != j) continue; // j was already merged into something else
                    if (pi.DistanceToSquared((Point3d)pts[j]) < tol2)
                    {
                        map[i] = j;
                        break;
                    }
                }
            }

            // Check if any merging happened
            bool anyMerged = false;
            for (int i = 0; i < n; i++)
            {
                if (map[i] != i) { anyMerged = true; break; }
            }
            if (!anyMerged) return;

            // Build compacted index mapping (old canonical → new sequential)
            var compact = new int[n];
            int newCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (map[i] == i)
                    compact[i] = newCount++;
            }

            // Final mapping: old vertex index → new compacted index
            var finalMap = new int[n];
            for (int i = 0; i < n; i++)
                finalMap[i] = compact[map[i]];

            // Build new vertex list (only canonical vertices)
            var newVerts = new Point3f[newCount];
            for (int i = 0; i < n; i++)
            {
                if (map[i] == i)
                    newVerts[compact[i]] = pts[i];
            }

            // Remap faces
            int faceCount = mesh.Faces.Count;
            var newFaces = new MeshFace[faceCount];
            for (int i = 0; i < faceCount; i++)
            {
                var f = mesh.Faces[i];
                newFaces[i] = f.IsQuad
                    ? new MeshFace(finalMap[f.A], finalMap[f.B], finalMap[f.C], finalMap[f.D])
                    : new MeshFace(finalMap[f.A], finalMap[f.B], finalMap[f.C]);
            }

            // Replace mesh contents
            mesh.Vertices.Clear();
            mesh.Faces.Clear();
            mesh.Normals.Clear();

            for (int i = 0; i < newCount; i++)
                mesh.Vertices.Add(newVerts[i]);
            for (int i = 0; i < faceCount; i++)
                mesh.Faces.AddFace(newFaces[i]);
        }
    }
}
