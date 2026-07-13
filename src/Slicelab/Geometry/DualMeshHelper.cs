using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Slicelab.Geometry
{
    public static class DualMeshHelper
    {
        /// <summary>
        /// Compute the topological dual of a triangle mesh.
        /// Interior vertices become polygonal faces (hexagons at valence-6).
        /// Boundary vertices become half-cells closed along the open edge.
        /// Faces are BFS-ordered from a corner vertex for topological coherence.
        /// Returns a mesh with ngon faces.
        /// </summary>
        public static Mesh ComputeDual(Mesh triMesh)
        {
            triMesh.Faces.ConvertQuadsToTriangles();
            triMesh.Compact();

            MeshTopologyEdgeList topoEdges = triMesh.TopologyEdges;
            MeshTopologyVertexList topoVerts = triMesh.TopologyVertices;
            int faceCount = triMesh.Faces.Count;
            int topoVertCount = topoVerts.Count;
            int edgeCount = topoEdges.Count;

            // Step 1: Compute face centroids → these become dual mesh vertices
            var faceCentroids = new Point3d[faceCount];
            for (int fi = 0; fi < faceCount; fi++)
            {
                MeshFace f = triMesh.Faces[fi];
                Point3d v0 = triMesh.Vertices[f.A];
                Point3d v1 = triMesh.Vertices[f.B];
                Point3d v2 = triMesh.Vertices[f.C];
                faceCentroids[fi] = new Point3d(
                    (v0.X + v1.X + v2.X) / 3.0,
                    (v0.Y + v1.Y + v2.Y) / 3.0,
                    (v0.Z + v1.Z + v2.Z) / 3.0);
            }

            // Step 2: Identify boundary edges and boundary vertices
            var isBoundaryEdge = new bool[edgeCount];
            var isBoundaryTopoVert = new bool[topoVertCount];
            for (int ei = 0; ei < edgeCount; ei++)
            {
                int[] connFaces = topoEdges.GetConnectedFaces(ei);
                if (connFaces.Length == 1)
                {
                    isBoundaryEdge[ei] = true;
                    IndexPair ep = topoEdges.GetTopologyVertices(ei);
                    isBoundaryTopoVert[ep.I] = true;
                    isBoundaryTopoVert[ep.J] = true;
                }
            }

            // Step 3: Build dual vertex pool
            // Layout: [0..faceCount-1] = face centroids,
            //         [faceCount..] = boundary edge midpoints + boundary vertices
            var dualVerts = new List<Point3d>(faceCount + edgeCount);
            for (int fi = 0; fi < faceCount; fi++)
                dualVerts.Add(faceCentroids[fi]);

            var edgeMidpointDualIdx = new Dictionary<int, int>();
            for (int ei = 0; ei < edgeCount; ei++)
            {
                if (!isBoundaryEdge[ei]) continue;
                IndexPair ep = topoEdges.GetTopologyVertices(ei);
                Point3d a = topoVerts[ep.I];
                Point3d b = topoVerts[ep.J];
                edgeMidpointDualIdx[ei] = dualVerts.Count;
                dualVerts.Add(new Point3d(
                    (a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0));
            }

            var boundaryVertDualIdx = new Dictionary<int, int>();
            for (int tvi = 0; tvi < topoVertCount; tvi++)
            {
                if (!isBoundaryTopoVert[tvi]) continue;
                boundaryVertDualIdx[tvi] = dualVerts.Count;
                dualVerts.Add(topoVerts[tvi]);
            }

            // Step 4: Build polygon vertex lists for each topology vertex (unordered)
            var polygons = new List<int>[topoVertCount]; // null = no polygon for this vertex
            for (int tvi = 0; tvi < topoVertCount; tvi++)
            {
                int[] connFaces = topoVerts.ConnectedFaces(tvi);
                if (connFaces.Length == 0) continue;

                int[] connEdges = topoVerts.ConnectedEdges(tvi);
                if (connEdges.Length == 0) continue;

                if (isBoundaryTopoVert[tvi])
                {
                    var boundaryEdges = new List<int>();
                    for (int i = 0; i < connEdges.Length; i++)
                    {
                        if (isBoundaryEdge[connEdges[i]])
                            boundaryEdges.Add(connEdges[i]);
                    }
                    if (boundaryEdges.Count < 2) continue;

                    List<int> orderedFaces = WalkFaces(tvi, boundaryEdges[0], connFaces, connEdges, topoEdges);
                    if (orderedFaces.Count == 0) continue;

                    var polyVerts = new List<int>();
                    polyVerts.Add(edgeMidpointDualIdx[boundaryEdges[0]]);
                    for (int i = 0; i < orderedFaces.Count; i++)
                        polyVerts.Add(orderedFaces[i]);

                    int endBoundaryEdge = FindEndBoundaryEdge(tvi, orderedFaces, connEdges, isBoundaryEdge, topoEdges, boundaryEdges[0]);
                    if (endBoundaryEdge >= 0)
                        polyVerts.Add(edgeMidpointDualIdx[endBoundaryEdge]);

                    polyVerts.Add(boundaryVertDualIdx[tvi]);
                    polygons[tvi] = polyVerts;
                }
                else
                {
                    List<int> orderedFaces = WalkFacesRing(tvi, connFaces, connEdges, topoEdges);
                    if (orderedFaces.Count < 3) continue;
                    polygons[tvi] = orderedFaces;
                }
            }

            // Step 5: BFS ordering of dual faces for topological coherence
            // Two dual faces (topology vertices) are adjacent if they share an edge.
            // Start from a boundary corner vertex (lowest valence boundary vertex) for open meshes,
            // or vertex 0 for closed meshes.
            var bfsOrder = new List<int>(topoVertCount);
            var visited = new bool[topoVertCount];

            // Find start vertex: prefer boundary vertex with lowest valence (corner)
            int startVert = -1;
            int minValence = int.MaxValue;
            for (int tvi = 0; tvi < topoVertCount; tvi++)
            {
                if (polygons[tvi] == null) continue;
                if (isBoundaryTopoVert[tvi])
                {
                    int val = topoVerts.ConnectedTopologyVertices(tvi).Length;
                    if (val < minValence)
                    {
                        minValence = val;
                        startVert = tvi;
                    }
                }
            }
            // Fallback: first vertex with a polygon
            if (startVert < 0)
            {
                for (int tvi = 0; tvi < topoVertCount; tvi++)
                {
                    if (polygons[tvi] != null) { startVert = tvi; break; }
                }
            }
            if (startVert < 0)
                return new Mesh(); // degenerate

            // BFS
            var queue = new Queue<int>();
            queue.Enqueue(startVert);
            visited[startVert] = true;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (polygons[current] != null)
                    bfsOrder.Add(current);

                int[] neighbors = topoVerts.ConnectedTopologyVertices(current);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (!visited[nb])
                    {
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }

            // Step 6: Build dual mesh with faces in BFS order
            var dualMesh = new Mesh();
            for (int i = 0; i < dualVerts.Count; i++)
                dualMesh.Vertices.Add(dualVerts[i]);

            for (int i = 0; i < bfsOrder.Count; i++)
                AddNgonFace(dualMesh, polygons[bfsOrder[i]]);

            dualMesh.UnifyNormals();
            dualMesh.Normals.ComputeNormals();
            dualMesh.Compact();
            return dualMesh;
        }

        /// <summary>
        /// Walk faces around a boundary vertex starting from a boundary edge.
        /// </summary>
        private static List<int> WalkFaces(int topoVert, int startBoundaryEdge,
            int[] connFaces, int[] connEdges, MeshTopologyEdgeList topoEdges)
        {
            var ordered = new List<int>();
            var visited = new HashSet<int>();

            int[] startFaces = topoEdges.GetConnectedFaces(startBoundaryEdge);
            if (startFaces.Length == 0) return ordered;

            int currentFace = startFaces[0];
            ordered.Add(currentFace);
            visited.Add(currentFace);

            for (int safety = 0; safety < connFaces.Length + 1; safety++)
            {
                int nextFace = -1;
                for (int i = 0; i < connEdges.Length; i++)
                {
                    int ei = connEdges[i];
                    int[] edgeFaces = topoEdges.GetConnectedFaces(ei);
                    if (edgeFaces.Length != 2) continue;

                    if (edgeFaces[0] == currentFace && !visited.Contains(edgeFaces[1]))
                        nextFace = edgeFaces[1];
                    else if (edgeFaces[1] == currentFace && !visited.Contains(edgeFaces[0]))
                        nextFace = edgeFaces[0];

                    if (nextFace >= 0) break;
                }

                if (nextFace < 0) break;
                ordered.Add(nextFace);
                visited.Add(nextFace);
                currentFace = nextFace;
            }

            return ordered;
        }

        /// <summary>
        /// Walk faces around an interior vertex in a complete ring.
        /// </summary>
        private static List<int> WalkFacesRing(int topoVert,
            int[] connFaces, int[] connEdges, MeshTopologyEdgeList topoEdges)
        {
            var ordered = new List<int>();
            if (connFaces.Length == 0) return ordered;

            var visited = new HashSet<int>();
            int currentFace = connFaces[0];
            ordered.Add(currentFace);
            visited.Add(currentFace);

            for (int safety = 0; safety < connFaces.Length; safety++)
            {
                int nextFace = -1;
                for (int i = 0; i < connEdges.Length; i++)
                {
                    int ei = connEdges[i];
                    int[] edgeFaces = topoEdges.GetConnectedFaces(ei);
                    if (edgeFaces.Length != 2) continue;

                    if (edgeFaces[0] == currentFace && !visited.Contains(edgeFaces[1]))
                        nextFace = edgeFaces[1];
                    else if (edgeFaces[1] == currentFace && !visited.Contains(edgeFaces[0]))
                        nextFace = edgeFaces[0];

                    if (nextFace >= 0) break;
                }

                if (nextFace < 0) break;
                ordered.Add(nextFace);
                visited.Add(nextFace);
                currentFace = nextFace;
            }

            return ordered;
        }

        /// <summary>
        /// Find the boundary edge at the end of the face walk (not the start edge).
        /// </summary>
        private static int FindEndBoundaryEdge(int topoVert, List<int> orderedFaces,
            int[] connEdges, bool[] isBoundaryEdge, MeshTopologyEdgeList topoEdges, int startEdge)
        {
            if (orderedFaces.Count == 0) return -1;
            int lastFace = orderedFaces[orderedFaces.Count - 1];

            for (int i = 0; i < connEdges.Length; i++)
            {
                int ei = connEdges[i];
                if (!isBoundaryEdge[ei] || ei == startEdge) continue;

                int[] edgeFaces = topoEdges.GetConnectedFaces(ei);
                if (edgeFaces.Length == 1 && edgeFaces[0] == lastFace)
                    return ei;
            }
            return -1;
        }

        /// <summary>
        /// Add a polygon face to the mesh, triangulated from its centroid.
        /// Creates N triangles (one per edge) with uniform aspect ratios.
        /// </summary>
        private static void AddNgonFace(Mesh mesh, List<int> polyVertIndices)
        {
            if (polyVertIndices.Count < 3) return;

            int n = polyVertIndices.Count;

            // Compute polygon centroid and add as a new vertex
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++)
            {
                Point3f v = mesh.Vertices[polyVertIndices[i]];
                cx += v.X;
                cy += v.Y;
                cz += v.Z;
            }
            int centerIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(new Point3d(cx / n, cy / n, cz / n));

            // Create N triangles: each edge → centroid
            int firstTriFace = mesh.Faces.Count;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                mesh.Faces.AddFace(polyVertIndices[i], polyVertIndices[next], centerIdx);
            }

            // Ngon references all N triangles and the original polygon vertices
            var ngonFaces = new List<int>(n);
            for (int i = 0; i < n; i++)
                ngonFaces.Add(firstTriFace + i);

            mesh.Ngons.AddNgon(MeshNgon.Create(polyVertIndices, ngonFaces));
        }
    }
}
