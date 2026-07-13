using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Slicelab.Geometry.Native;

namespace Slicelab.Geometry
{
    public abstract class MeshBooleanComponentBase : GH_TaskCapableComponent<MeshBooleanComponentBase.SolveResults>
    {
        protected abstract int Operation { get; }

        protected MeshBooleanComponentBase(string name, string nickname, string description)
            : base(name, nickname, description, "Slicelab Tools", "Geometry")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh A", "A", "First mesh (must be closed)", GH_ParamAccess.item);
            pManager.AddMeshParameter("Mesh B", "B", "Second mesh (must be closed)", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Result mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Summary statistics", GH_ParamAccess.item);
        }

        public class SolveResults
        {
            public Mesh OutputMesh;
            public string Info;
        }

        private SolveResults Compute(Mesh meshA, Mesh meshB)
        {
            BooleanHelper.MeshToArrays(meshA, out double[] vertsA, out int[] trisA);
            BooleanHelper.MeshToArrays(meshB, out double[] vertsB, out int[] trisB);

            int inVertsA = vertsA.Length / 3;
            int inTrisA = trisA.Length / 3;
            int inVertsB = vertsB.Length / 3;
            int inTrisB = trisB.Length / 3;

            MeshResult result = ManifoldInterop.MeshBoolean(
                vertsA, trisA, vertsB, trisB, Operation);

            Mesh outputMesh = BooleanHelper.ArraysToMesh(result.Vertices, result.Triangles);

            int outVerts = result.Vertices.Length / 3;
            int outTris = result.Triangles.Length / 3;
            string[] opNames = { "Union", "Difference", "Intersection" };
            string opName = opNames[Operation];
            string info = string.Format(
                "{0}\nA: {1} verts, {2} tris\nB: {3} verts, {4} tris\nResult: {5} verts, {6} tris",
                opName, inVertsA, inTrisA, inVertsB, inTrisB, outVerts, outTris);

            return new SolveResults { OutputMesh = outputMesh, Info = info };
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                Mesh meshA = null;
                Mesh meshB = null;

                if (!DA.GetData(0, ref meshA)) return;
                if (!DA.GetData(1, ref meshB)) return;

                if (meshA == null || meshA.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh A is empty or invalid.");
                    return;
                }
                if (meshB == null || meshB.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh B is empty or invalid.");
                    return;
                }

                Mesh copyA = meshA.DuplicateMesh();
                Mesh copyB = meshB.DuplicateMesh();

                Task<SolveResults> task = Task.Run(() => Compute(copyA, copyB), CancelToken);
                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(DA, out SolveResults result))
            {
                return;
            }

            if (result != null)
            {
                DA.SetData(0, result.OutputMesh);
                DA.SetData(1, result.Info);
            }
        }
    }
}
