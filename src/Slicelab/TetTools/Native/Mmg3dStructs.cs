using System.Runtime.InteropServices;

namespace Slicelab.TetTools.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VolMeshInputNative
    {
        public double* vertices;      // numVertices * 3
        public int* triangles;        // numTriangles * 3 (0-based)
        public int* tetrahedra;       // numTetrahedra * 4 (0-based)
        public int numVertices;
        public int numTriangles;
        public int numTetrahedra;
        public double hsiz;           // target (constant) edge length
        public double hmin;           // minimum edge length
        public double hmax;           // maximum edge length
        public double hausd;          // Hausdorff distance
        public double hgrad;          // gradation parameter
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VolMeshOutputNative
    {
        public double* vertices;      // numVertices * 3
        public int* triangles;        // numTriangles * 3 (0-based)
        public int* tetrahedra;       // numTetrahedra * 4 (0-based)
        public int numVertices;
        public int numTriangles;
        public int numTetrahedra;
        public int returnCode;        // 0=strong failure, -1=low failure, 1=success
        public byte* errorLog;        // captured stdout/stderr from mmg3d
        public int errorLogLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VolMeshMetricNative
    {
        public double* sizes;      // numVertices values — target edge length at each vertex
        public int numValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VolMeshConstraintsNative
    {
        public int* requiredVertices;  // 0-based vertex indices to pin
        public int numRequired;
    }

    public class Mmg3dResult
    {
        public double[] Vertices;     // flat, 3 per vertex
        public int[] Triangles;       // flat, 3 per triangle
        public int[] TetraIndices;    // flat, 4 per tetrahedron
    }
}
