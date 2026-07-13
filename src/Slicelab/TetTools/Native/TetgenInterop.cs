using System;
using System.Runtime.InteropServices;

namespace Slicelab.TetTools.Native
{
    /// <summary>
    /// P/Invoke declarations and managed wrapper for the TetgenWrapper native library.
    /// </summary>
    public static class TetgenInterop
    {
        [DllImport("TetgenWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe InteropMeshNative* performTetgen(
            InteropMeshNative* input,
            TetgenBehaviourNative* behaviour);

        [DllImport("TetgenWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe InteropMeshNative* performTetgenWithAddin(
            InteropMeshNative* input,
            TetgenBehaviourNative* behaviour,
            double* addPoints,
            int numAddPoints);

        [DllImport("TetgenWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void freeMesh(InteropMeshNative* mesh);

        /// <summary>
        /// Tetrahedralizes an input mesh using TetGen via native interop.
        /// </summary>
        /// <param name="vertices">Flat array of vertex coordinates (x0,y0,z0, x1,y1,z1, ...).</param>
        /// <param name="faceIndices">Flat array of face vertex indices.</param>
        /// <param name="faceSizes">Number of vertices per face.</param>
        /// <param name="minRatio">Minimum radius-edge ratio for quality meshing.</param>
        /// <param name="maxVolume">Maximum tetrahedron volume constraint (0 = no constraint).</param>
        /// <param name="coarsen">Whether to coarsen the mesh (0 or 1).</param>
        /// <param name="steinerLevel">Steiner point suppression level.</param>
        /// <returns>A TetgenResult containing the output mesh data.</returns>
        public static unsafe TetgenResult Tetrahedralize(
            double[] vertices,
            int[] faceIndices,
            int[] faceSizes,
            double minRatio,
            double maxVolume,
            int coarsen,
            int steinerLevel)
        {
            fixed (double* pVertices = vertices)
            fixed (int* pFaceIndices = faceIndices)
            fixed (int* pFaceSizes = faceSizes)
            {
                InteropMeshNative input = new InteropMeshNative
                {
                    vertices = pVertices,
                    faceIndices = pFaceIndices,
                    faceSizes = pFaceSizes,
                    tetra = null,
                    edges = null,
                    numVertices = vertices.Length / 3,
                    numFaces = faceSizes.Length,
                    numFaceIndices = faceIndices.Length,
                    numTetra = 0,
                    numEdges = 0
                };

                TetgenBehaviourNative behaviour = new TetgenBehaviourNative
                {
                    plc = 1,
                    refine = 0,
                    quality = 1,
                    coarsen = coarsen,
                    insertaddpoints = 0,
                    supsteiner_level = steinerLevel,
                    optlevel = 2,
                    maxvolume = maxVolume,
                    minratio = minRatio,
                    mindihedral = 0.0,
                    optmaxdihedral = 165.0,
                    optminslidihed = 179.0,
                    optminsmtdihed = 179.0
                };

                InteropMeshNative* result = performTetgen(&input, &behaviour);

                if (result == null || result->numVertices == 0)
                {
                    if (result != null)
                    {
                        freeMesh(result);
                    }
                    throw new InvalidOperationException("TetGen tetrahedralization failed — no output produced.");
                }

                TetgenResult managed = new TetgenResult();

                // Copy vertices
                managed.Vertices = new double[result->numVertices * 3];
                Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);

                // Copy face indices
                managed.FaceIndices = new int[result->numFaceIndices];
                Marshal.Copy((IntPtr)result->faceIndices, managed.FaceIndices, 0, managed.FaceIndices.Length);

                // Copy face sizes
                managed.FaceSizes = new int[result->numFaces];
                Marshal.Copy((IntPtr)result->faceSizes, managed.FaceSizes, 0, managed.FaceSizes.Length);

                // Copy tetrahedra indices
                managed.TetraIndices = new int[result->numTetra * 4];
                Marshal.Copy((IntPtr)result->tetra, managed.TetraIndices, 0, managed.TetraIndices.Length);

                // Copy edge indices
                managed.EdgeIndices = new int[result->numEdges * 2];
                Marshal.Copy((IntPtr)result->edges, managed.EdgeIndices, 0, managed.EdgeIndices.Length);

                freeMesh(result);

                return managed;
            }
        }

        /// <summary>
        /// Inserts additional points into an existing tet mesh using TetGen's refine + insertaddpoints mode.
        /// </summary>
        public static unsafe TetgenResult TetrahedralizeWithAdditionalPoints(
            double[] vertices,
            int[] faceIndices,
            int[] faceSizes,
            int[] tetraIndices,
            double[] additionalPoints)
        {
            fixed (double* pVertices = vertices)
            fixed (int* pFaceIndices = faceIndices)
            fixed (int* pFaceSizes = faceSizes)
            fixed (int* pTetra = tetraIndices)
            fixed (double* pAddPoints = additionalPoints)
            {
                InteropMeshNative input = new InteropMeshNative
                {
                    vertices = pVertices,
                    faceIndices = pFaceIndices,
                    faceSizes = pFaceSizes,
                    tetra = pTetra,
                    edges = null,
                    numVertices = vertices.Length / 3,
                    numFaces = faceSizes.Length,
                    numFaceIndices = faceIndices.Length,
                    numTetra = tetraIndices.Length / 4,
                    numEdges = 0
                };

                TetgenBehaviourNative behaviour = new TetgenBehaviourNative
                {
                    plc = 0,
                    refine = 1,
                    quality = 0,
                    coarsen = 0,
                    insertaddpoints = 1,
                    supsteiner_level = 0,
                    optlevel = 0,
                    maxvolume = 0,
                    minratio = 0,
                    mindihedral = 0,
                    optmaxdihedral = 165.0,
                    optminslidihed = 179.0,
                    optminsmtdihed = 179.0
                };

                int numAddPoints = additionalPoints.Length / 3;

                InteropMeshNative* result = performTetgenWithAddin(
                    &input, &behaviour, pAddPoints, numAddPoints);

                if (result == null || result->numVertices == 0)
                {
                    if (result != null)
                        freeMesh(result);
                    throw new InvalidOperationException(
                        "TetGen insertaddpoints failed — no output produced.");
                }

                TetgenResult managed = new TetgenResult();

                managed.Vertices = new double[result->numVertices * 3];
                Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);

                managed.FaceIndices = new int[result->numFaceIndices];
                Marshal.Copy((IntPtr)result->faceIndices, managed.FaceIndices, 0, managed.FaceIndices.Length);

                managed.FaceSizes = new int[result->numFaces];
                Marshal.Copy((IntPtr)result->faceSizes, managed.FaceSizes, 0, managed.FaceSizes.Length);

                managed.TetraIndices = new int[result->numTetra * 4];
                Marshal.Copy((IntPtr)result->tetra, managed.TetraIndices, 0, managed.TetraIndices.Length);

                managed.EdgeIndices = new int[result->numEdges * 2];
                Marshal.Copy((IntPtr)result->edges, managed.EdgeIndices, 0, managed.EdgeIndices.Length);

                freeMesh(result);

                return managed;
            }
        }
    }
}
