using System.Runtime.InteropServices;

namespace Slicelab.TetTools.Native
{
    /// <summary>
    /// Unsafe struct matching the C++ InteropMesh layout for P/Invoke marshalling.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct InteropMeshNative
    {
        public double* vertices;
        public int* faceIndices;
        public int* faceSizes;
        public int* tetra;
        public int* edges;
        public int numVertices;
        public int numFaces;
        public int numFaceIndices;
        public int numTetra;
        public int numEdges;
    }

    /// <summary>
    /// Struct matching the C++ TetgenBehaviour layout for P/Invoke marshalling.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TetgenBehaviourNative
    {
        public int plc;
        public int refine;
        public int quality;
        public int coarsen;
        public int insertaddpoints;
        public int supsteiner_level;
        public int optlevel;
        public double maxvolume;
        public double minratio;
        public double mindihedral;
        public double optmaxdihedral;
        public double optminslidihed;
        public double optminsmtdihed;
    }

    /// <summary>
    /// Managed result class holding arrays copied from native TetGen output.
    /// </summary>
    public class TetgenResult
    {
        public double[] Vertices;
        public int[] FaceIndices;
        public int[] FaceSizes;
        public int[] TetraIndices;
        public int[] EdgeIndices;
    }
}
