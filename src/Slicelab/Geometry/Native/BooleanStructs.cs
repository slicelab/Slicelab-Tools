using System.Runtime.InteropServices;

namespace Slicelab.Geometry.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MeshInputNative
    {
        public double* vertices;     // numVerts * 3 (x, y, z)
        public int* triangles;       // numTris * 3 (0-based)
        public int numVerts;
        public int numTris;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MeshOutputNative
    {
        public double* vertices;     // numVerts * 3
        public int* triangles;       // numTris * 3 (0-based)
        public int numVerts;
        public int numTris;
        public int errorCode;
    }

    public class MeshResult
    {
        public double[] Vertices;    // numVerts * 3
        public int[] Triangles;      // numTris * 3
    }
}
