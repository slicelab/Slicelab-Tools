using System.Runtime.InteropServices;

namespace Slicelab.Geometry.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RemeshInputNative
    {
        public double* vertices;
        public int* triangles;
        public int numVertices;
        public int numTriangles;
        public double targetEdgeLength;
        public double hausd;
        public double hgrad;
        public int noRidge;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RemeshMetricNative
    {
        public double* sizes;
        public int numValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RemeshOutputNative
    {
        public double* vertices;
        public int* triangles;
        public int numVertices;
        public int numTriangles;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RemeshConstraintsNative
    {
        public int* edges;       // numEdges * 2 (vertex index pairs, 0-based)
        public int numEdges;
        public int* vertices;    // numRequiredVertices
        public int numVertices;
    }

    public class RemeshResult
    {
        public double[] Vertices;    // numVertices * 3
        public int[] Triangles;      // numTriangles * 3
    }
}
