using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Slicelab.TetTools.Native
{
    public static class Mmg3dInterop
    {
        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe VolMeshOutputNative* mmg3d_tetrahedralize(VolMeshInputNative* input);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe VolMeshOutputNative* mmg3d_tetrahedralize_adaptive(
            VolMeshInputNative* input, VolMeshMetricNative* metric);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe VolMeshOutputNative* mmg3d_tetrahedralize_with_required(
            VolMeshInputNative* input, VolMeshConstraintsNative* constraints);

        [DllImport("MmgsWrapper", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void mmg3d_free_output(VolMeshOutputNative* output);

        private static unsafe string ExtractLog(VolMeshOutputNative* result)
        {
            if (result->errorLog == null || result->errorLogLength <= 0)
                return null;
            return Encoding.UTF8.GetString(result->errorLog, result->errorLogLength);
        }

        public static unsafe Mmg3dResult Tetrahedralize(
            double[] vertices, int[] triangles, int[] tetrahedra,
            double hsiz, double hmin, double hmax, double hausd, double hgrad)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (int* pTets = tetrahedra)
            {
                VolMeshInputNative input = new VolMeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    tetrahedra = pTets,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    numTetrahedra = tetrahedra.Length / 4,
                    hsiz = hsiz,
                    hmin = hmin,
                    hmax = hmax,
                    hausd = hausd,
                    hgrad = hgrad
                };

                VolMeshOutputNative* result = mmg3d_tetrahedralize(&input);

                if (result == null)
                    throw new InvalidOperationException("MMG3D native call returned null.");

                int returnCode = result->returnCode;
                string log = ExtractLog(result);

                if (result->numVertices == 0 || result->numTetrahedra == 0)
                {
                    mmg3d_free_output(result);
                    string codeDesc = returnCode == 0 ? "strong failure" :
                                      returnCode == -1 ? "low failure" : "unknown";

                    // Truncate log to last 500 chars for the error message
                    string logTail = "";
                    if (log != null && log.Length > 0)
                    {
                        logTail = log.Length > 500 ? "\n..." + log.Substring(log.Length - 500) : "\n" + log;
                    }

                    throw new InvalidOperationException(
                        $"MMG3D failed ({codeDesc}).{logTail}");
                }

                var managed = new Mmg3dResult();

                managed.Vertices = new double[result->numVertices * 3];
                Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);

                managed.Triangles = new int[result->numTriangles * 3];
                if (result->numTriangles > 0)
                    Marshal.Copy((IntPtr)result->triangles, managed.Triangles, 0, managed.Triangles.Length);

                managed.TetraIndices = new int[result->numTetrahedra * 4];
                Marshal.Copy((IntPtr)result->tetrahedra, managed.TetraIndices, 0, managed.TetraIndices.Length);

                mmg3d_free_output(result);
                return managed;
            }
        }

        public static unsafe Mmg3dResult TetrahedralizeAdaptive(
            double[] vertices, int[] triangles, int[] tetrahedra,
            double[] sizingField, double hmin, double hmax, double hausd, double hgrad)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (int* pTets = tetrahedra)
            fixed (double* pSizes = sizingField)
            {
                VolMeshInputNative input = new VolMeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    tetrahedra = pTets,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    numTetrahedra = tetrahedra.Length / 4,
                    hsiz = 0,
                    hmin = hmin,
                    hmax = hmax,
                    hausd = hausd,
                    hgrad = hgrad
                };

                VolMeshMetricNative metric = new VolMeshMetricNative
                {
                    sizes = pSizes,
                    numValues = sizingField.Length
                };

                VolMeshOutputNative* result = mmg3d_tetrahedralize_adaptive(&input, &metric);

                if (result == null)
                    throw new InvalidOperationException("MMG3D adaptive native call returned null.");

                int returnCode = result->returnCode;
                string log = ExtractLog(result);

                if (result->numVertices == 0 || result->numTetrahedra == 0)
                {
                    mmg3d_free_output(result);
                    string codeDesc = returnCode == 0 ? "strong failure" :
                                      returnCode == -1 ? "low failure" : "unknown";

                    // Truncate log to last 500 chars for the error message
                    string logTail = "";
                    if (log != null && log.Length > 0)
                    {
                        logTail = log.Length > 500 ? "\n..." + log.Substring(log.Length - 500) : "\n" + log;
                    }

                    throw new InvalidOperationException(
                        $"MMG3D adaptive failed ({codeDesc}).{logTail}");
                }

                var managed = new Mmg3dResult();

                managed.Vertices = new double[result->numVertices * 3];
                Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);

                managed.Triangles = new int[result->numTriangles * 3];
                if (result->numTriangles > 0)
                    Marshal.Copy((IntPtr)result->triangles, managed.Triangles, 0, managed.Triangles.Length);

                managed.TetraIndices = new int[result->numTetrahedra * 4];
                Marshal.Copy((IntPtr)result->tetrahedra, managed.TetraIndices, 0, managed.TetraIndices.Length);

                mmg3d_free_output(result);
                return managed;
            }
        }

        public static unsafe Mmg3dResult TetrahedralizeWithRequired(
            double[] vertices, int[] triangles, int[] tetrahedra,
            int[] requiredVertexIndices,
            double hsiz, double hmin, double hmax, double hausd, double hgrad)
        {
            fixed (double* pVerts = vertices)
            fixed (int* pTris = triangles)
            fixed (int* pTets = tetrahedra)
            fixed (int* pReq = requiredVertexIndices)
            {
                VolMeshInputNative input = new VolMeshInputNative
                {
                    vertices = pVerts,
                    triangles = pTris,
                    tetrahedra = pTets,
                    numVertices = vertices.Length / 3,
                    numTriangles = triangles.Length / 3,
                    numTetrahedra = tetrahedra.Length / 4,
                    hsiz = hsiz,
                    hmin = hmin,
                    hmax = hmax,
                    hausd = hausd,
                    hgrad = hgrad
                };

                VolMeshConstraintsNative constraints = new VolMeshConstraintsNative
                {
                    requiredVertices = pReq,
                    numRequired = requiredVertexIndices.Length
                };

                VolMeshOutputNative* result = mmg3d_tetrahedralize_with_required(&input, &constraints);

                if (result == null)
                    throw new InvalidOperationException("MMG3D with required vertices native call returned null.");

                int returnCode = result->returnCode;
                string log = ExtractLog(result);

                if (result->numVertices == 0 || result->numTetrahedra == 0)
                {
                    mmg3d_free_output(result);
                    string codeDesc = returnCode == 0 ? "strong failure" :
                                      returnCode == -1 ? "low failure" : "unknown";

                    string logTail = "";
                    if (log != null && log.Length > 0)
                    {
                        logTail = log.Length > 500 ? "\n..." + log.Substring(log.Length - 500) : "\n" + log;
                    }

                    throw new InvalidOperationException(
                        $"MMG3D with required vertices failed ({codeDesc}).{logTail}");
                }

                var managed = new Mmg3dResult();

                managed.Vertices = new double[result->numVertices * 3];
                Marshal.Copy((IntPtr)result->vertices, managed.Vertices, 0, managed.Vertices.Length);

                managed.Triangles = new int[result->numTriangles * 3];
                if (result->numTriangles > 0)
                    Marshal.Copy((IntPtr)result->triangles, managed.Triangles, 0, managed.Triangles.Length);

                managed.TetraIndices = new int[result->numTetrahedra * 4];
                Marshal.Copy((IntPtr)result->tetrahedra, managed.TetraIndices, 0, managed.TetraIndices.Length);

                mmg3d_free_output(result);
                return managed;
            }
        }
    }
}
