using Rhino.Geometry;

namespace Slicelab.Geometry
{
    public static class BooleanHelper
    {
        public static void MeshToArrays(Mesh mesh, out double[] vertices, out int[] triangles)
        {
            Mesh m = mesh.DuplicateMesh();
            m.Faces.ConvertQuadsToTriangles();
            m.Vertices.CombineIdentical(true, true);
            m.Faces.CullDegenerateFaces();
            m.UnifyNormals();
            m.Compact();

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

            mesh.Normals.ComputeNormals();
            mesh.UnifyNormals();
            return mesh;
        }
    }
}
