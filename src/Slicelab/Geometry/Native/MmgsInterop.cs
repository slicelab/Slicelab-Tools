using System;
using System.Runtime.InteropServices;

namespace Slicelab.Geometry.Native
{
    public static class MmgsInterop
    {
        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe RemeshOutputNative* mmgs_remesh_uniform(RemeshInputNative* input);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe RemeshOutputNative* mmgs_remesh_adaptive(
            RemeshInputNative* input, RemeshMetricNative* metric);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe RemeshOutputNative* mmgs_remesh_uniform_constrained(
            RemeshInputNative* input, RemeshConstraintsNative* constraints);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe RemeshOutputNative* mmgs_remesh_adaptive_constrained(
            RemeshInputNative* input, RemeshMetricNative* metric, RemeshConstraintsNative* constraints);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void mmgs_free_output(RemeshOutputNative* output);

        public static unsafe RemeshResult RemeshUniform(
            double[] vertices, int[] triangles, double targetEdgeLength, double hausd,
            bool noRidge = false)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            {
                RemeshInputNative input = new RemeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    targetEdgeLength = targetEdgeLength,
                    hausd = hausd,
                    hgrad = 1.1,
                    noRidge = noRidge ? 1 : 0
                };

                RemeshOutputNative* result = mmgs_remesh_uniform(&input);
                return CopyAndFree(result);
            }
        }

        public static unsafe RemeshResult RemeshAdaptive(
            double[] vertices, int[] triangles, double targetEdgeLength, double[] sizingField, double hausd,
            double hgrad = 1.1, bool noRidge = false)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (double* pSizes = sizingField)
            {
                RemeshInputNative input = new RemeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    targetEdgeLength = targetEdgeLength,
                    hausd = hausd,
                    hgrad = hgrad,
                    noRidge = noRidge ? 1 : 0
                };

                RemeshMetricNative metric = new RemeshMetricNative
                {
                    sizes = pSizes,
                    numValues = sizingField.Length
                };

                RemeshOutputNative* result = mmgs_remesh_adaptive(&input, &metric);
                return CopyAndFree(result);
            }
        }

        public static unsafe RemeshResult RemeshUniformConstrained(
            double[] vertices, int[] triangles, double targetEdgeLength, double hausd,
            int[] requiredEdges, int[] requiredVertices, bool noRidge = false)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (int* pEdges = requiredEdges)
            fixed (int* pReqVerts = requiredVertices)
            {
                RemeshInputNative input = new RemeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    targetEdgeLength = targetEdgeLength,
                    hausd = hausd,
                    hgrad = 1.1,
                    noRidge = noRidge ? 1 : 0
                };

                RemeshConstraintsNative constraints = new RemeshConstraintsNative
                {
                    edges = pEdges,
                    numEdges = requiredEdges.Length / 2,
                    vertices = pReqVerts,
                    numVertices = requiredVertices.Length
                };

                RemeshOutputNative* result = mmgs_remesh_uniform_constrained(&input, &constraints);
                return CopyAndFree(result);
            }
        }

        public static unsafe RemeshResult RemeshAdaptiveConstrained(
            double[] vertices, int[] triangles, double targetEdgeLength, double[] sizingField, double hausd,
            int[] requiredEdges, int[] requiredVertices, bool noRidge = false)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (double* pSizes = sizingField)
            fixed (int* pEdges = requiredEdges)
            fixed (int* pReqVerts = requiredVertices)
            {
                RemeshInputNative input = new RemeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    targetEdgeLength = targetEdgeLength,
                    hausd = hausd,
                    hgrad = 1.1,
                    noRidge = noRidge ? 1 : 0
                };

                RemeshMetricNative metric = new RemeshMetricNative
                {
                    sizes = pSizes,
                    numValues = sizingField.Length
                };

                RemeshConstraintsNative constraints = new RemeshConstraintsNative
                {
                    edges = pEdges,
                    numEdges = requiredEdges.Length / 2,
                    vertices = pReqVerts,
                    numVertices = requiredVertices.Length
                };

                RemeshOutputNative* result = mmgs_remesh_adaptive_constrained(&input, &metric, &constraints);
                return CopyAndFree(result);
            }
        }

        private static unsafe RemeshResult CopyAndFree(RemeshOutputNative* result)
        {
            if (result == null || result->numVertices == 0)
            {
                if (result != null) mmgs_free_output(result);
                throw new InvalidOperationException("mmgs remeshing failed — no output produced.");
            }

            var managed = new RemeshResult();
            managed.Vertices = new double[result->numVertices * 3];
            Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);
            managed.Triangles = new int[result->numTriangles * 3];
            Marshal.Copy((IntPtr)result->triangles, managed.Triangles, 0, managed.Triangles.Length);

            mmgs_free_output(result);
            return managed;
        }
    }
}
