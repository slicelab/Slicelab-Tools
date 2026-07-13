using System;
using System.Drawing;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Slicelab.Geometry.Native;

namespace Slicelab.Geometry
{
    public class MeshSimplifyComponent : GH_TaskCapableComponent<MeshSimplifyComponent.SolveResults>
    {
        public MeshSimplifyComponent()
            : base("Mesh Decimate", "SLDec",
                "Decimate a closed mesh by reducing triangle count within a tolerance.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A3B7E2D1-6F4C-4A8E-9D15-7C3E8F2A1B50");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Simplify.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Closed mesh to simplify", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tolerance", "T",
                "Maximum surface deviation allowed (model units)", GH_ParamAccess.item, 0.1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Simplified mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Summary statistics", GH_ParamAccess.item);
        }

        public class SolveResults
        {
            public Mesh OutputMesh;
            public string Info;
        }

        private SolveResults Compute(Mesh mesh, double tolerance)
        {
            BooleanHelper.MeshToArrays(mesh, out double[] verts, out int[] tris);
            int inVerts = verts.Length / 3;
            int inTris = tris.Length / 3;

            MeshResult result = ManifoldInterop.Simplify(verts, tris, tolerance);

            Mesh outputMesh = BooleanHelper.ArraysToMesh(result.Vertices, result.Triangles);
            int outVerts = result.Vertices.Length / 3;
            int outTris = result.Triangles.Length / 3;

            double reduction = inTris > 0 ? (1.0 - (double)outTris / inTris) * 100.0 : 0;
            string info = string.Format(
                "Simplify (tolerance={0:F4})\nInput: {1} verts, {2} tris\nResult: {3} verts, {4} tris\nReduction: {5:F1}%",
                tolerance, inVerts, inTris, outVerts, outTris, reduction);

            return new SolveResults { OutputMesh = outputMesh, Info = info };
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                Mesh mesh = null;
                double tolerance = 0.1;

                if (!DA.GetData(0, ref mesh)) return;
                DA.GetData(1, ref tolerance);

                if (mesh == null || mesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is empty or invalid.");
                    return;
                }
                if (tolerance <= 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tolerance must be positive.");
                    return;
                }

                Mesh copy = mesh.DuplicateMesh();
                Task<SolveResults> task = Task.Run(() => Compute(copy, tolerance), CancelToken);
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
