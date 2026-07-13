using System;
using System.Runtime.InteropServices;

namespace Slicelab.Geometry.Native
{
    public static class ManifoldInterop
    {
        [DllImport("ManifoldWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe MeshOutputNative* manifold_boolean_op(
            MeshInputNative* meshA, MeshInputNative* meshB, int operation);

        [DllImport("ManifoldWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe MeshOutputNative* manifold_simplify(
            MeshInputNative* mesh, double tolerance);

        [DllImport("ManifoldWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe MeshOutputNative* manifold_smooth(
            MeshInputNative* mesh, double minSharpAngle, double targetEdgeLength);

        [DllImport("ManifoldWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void manifold_free_output(MeshOutputNative* output);

        /// <summary>
        /// Perform a mesh boolean operation.
        /// operation: 0 = Union, 1 = Difference, 2 = Intersection
        /// </summary>
        public static unsafe MeshResult MeshBoolean(
            double[] vertsA, int[] trisA,
            double[] vertsB, int[] trisB,
            int operation)
        {
            fixed (double* pVertsA = vertsA)
            fixed (int* pTrisA = trisA)
            fixed (double* pVertsB = vertsB)
            fixed (int* pTrisB = trisB)
            {
                MeshInputNative inputA = new MeshInputNative
                {
                    vertices = pVertsA,
                    triangles = pTrisA,
                    numVerts = vertsA.Length / 3,
                    numTris = trisA.Length / 3
                };

                MeshInputNative inputB = new MeshInputNative
                {
                    vertices = pVertsB,
                    triangles = pTrisB,
                    numVerts = vertsB.Length / 3,
                    numTris = trisB.Length / 3
                };

                MeshOutputNative* result = manifold_boolean_op(&inputA, &inputB, operation);
                return CopyAndFree(result, "boolean");
            }
        }

        /// <summary>
        /// Simplify a mesh by reducing triangle count.
        /// tolerance: maximum surface deviation allowed.
        /// </summary>
        public static unsafe MeshResult Simplify(
            double[] verts, int[] tris, double tolerance)
        {
            fixed (double* pVerts = verts)
            fixed (int* pTris = tris)
            {
                MeshInputNative input = new MeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVerts = verts.Length / 3,
                    numTris = tris.Length / 3
                };

                MeshOutputNative* result = manifold_simplify(&input, tolerance);
                return CopyAndFree(result, "simplify");
            }
        }

        /// <summary>
        /// Smooth a mesh then refine to target edge length.
        /// minSharpAngle: edges sharper than this (degrees) stay sharp.
        /// targetEdgeLength: target edge length for refinement.
        /// </summary>
        public static unsafe MeshResult Smooth(
            double[] verts, int[] tris,
            double minSharpAngle, double targetEdgeLength)
        {
            fixed (double* pVerts = verts)
            fixed (int* pTris = tris)
            {
                MeshInputNative input = new MeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVerts = verts.Length / 3,
                    numTris = tris.Length / 3
                };

                MeshOutputNative* result = manifold_smooth(&input, minSharpAngle, targetEdgeLength);
                return CopyAndFree(result, "smooth");
            }
        }

        private static unsafe MeshResult CopyAndFree(MeshOutputNative* result, string opName)
        {
            if (result == null)
                throw new InvalidOperationException("Manifold " + opName + " failed — null result.");

            int errorCode = result->errorCode;
            if (errorCode != 0)
            {
                manifold_free_output(result);
                string msg;
                switch (errorCode)
                {
                    case -1: msg = "Input mesh is not manifold (not closed or has self-intersections)."; break;
                    case -2: msg = "Mesh B is not manifold (not closed or has self-intersections)."; break;
                    case -3: msg = "Operation failed."; break;
                    case -4: msg = "Result is empty."; break;
                    default: msg = "Unknown error (code " + errorCode + ")."; break;
                }
                throw new InvalidOperationException(msg);
            }

            var managed = new MeshResult();
            managed.Vertices = new double[result->numVerts * 3];
            Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);
            managed.Triangles = new int[result->numTris * 3];
            Marshal.Copy((IntPtr)result->triangles, managed.Triangles, 0, managed.Triangles.Length);

            manifold_free_output(result);
            return managed;
        }
    }
}
