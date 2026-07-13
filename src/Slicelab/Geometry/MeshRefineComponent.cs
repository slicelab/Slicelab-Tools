using System;
using System.Drawing;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Slicelab.Geometry.Native;

namespace Slicelab.Geometry
{
    public class MeshRefineComponent : GH_TaskCapableComponent<MeshRefineComponent.SolveResults>
    {
        public MeshRefineComponent()
            : base("Mesh Refine", "SLRefine",
                "Subdivide and refine a closed mesh. First applies subdivision smoothing with tangent interpolation (preserving edges sharper than the angle threshold), then refines to a target edge length. Unlike Laplacian smoothing, this adds geometry and preserves volume.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("E8C4F6A2-3D17-4B9E-A520-9F1D6E8B3C74");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Refine.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Closed mesh to smooth", GH_ParamAccess.item);
            pManager.AddNumberParameter("Sharp Angle", "SA",
                "Edges sharper than this angle (degrees) are kept sharp. 0 = smooth everything.",
                GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Edge Length", "EL",
                "Target edge length for refinement (model units)",
                GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Smoothed mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Summary statistics", GH_ParamAccess.item);
        }

        public class SolveResults
        {
            public Mesh OutputMesh;
            public string Info;
        }

        private SolveResults Compute(Mesh mesh, double minSharpAngle, double targetEdgeLength)
        {
            BooleanHelper.MeshToArrays(mesh, out double[] verts, out int[] tris);
            int inVerts = verts.Length / 3;
            int inTris = tris.Length / 3;

            MeshResult result = ManifoldInterop.Smooth(verts, tris, minSharpAngle, targetEdgeLength);

            Mesh outputMesh = BooleanHelper.ArraysToMesh(result.Vertices, result.Triangles);
            int outVerts = result.Vertices.Length / 3;
            int outTris = result.Triangles.Length / 3;

            string info = string.Format(
                "Smooth (sharp angle={0:F1}°, edge length={1:F4})\nInput: {2} verts, {3} tris\nResult: {4} verts, {5} tris",
                minSharpAngle, targetEdgeLength, inVerts, inTris, outVerts, outTris);

            return new SolveResults { OutputMesh = outputMesh, Info = info };
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                Mesh mesh = null;
                double minSharpAngle = 60.0;
                double targetEdgeLength = 1.0;

                if (!DA.GetData(0, ref mesh)) return;
                DA.GetData(1, ref minSharpAngle);
                DA.GetData(2, ref targetEdgeLength);

                if (mesh == null || mesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is empty or invalid.");
                    return;
                }
                if (targetEdgeLength <= 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Edge length must be positive.");
                    return;
                }

                Mesh copy = mesh.DuplicateMesh();
                Task<SolveResults> task = Task.Run(
                    () => Compute(copy, minSharpAngle, targetEdgeLength), CancelToken);
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
