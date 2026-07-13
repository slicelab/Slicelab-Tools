using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.Geometry
{
    public class FixInvalidMeshComponent : GH_Component
    {
        public FixInvalidMeshComponent()
            : base("Fix Invalid Mesh", "SLFix",
                "Attempt to repair an invalid mesh by cleaning vertices, faces, normals, and filling holes.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A0B1C2D3-E4F5-4A6B-7C8D-9E0F1A2B3C10");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Fix.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to repair", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Fill Holes", "F", "Close naked edge loops. Set to false to preserve open meshes.", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Repaired mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Repair status", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = null;
            if (!DA.GetData(0, ref mesh)) return;

            bool fillHoles = true;
            DA.GetData(1, ref fillHoles);

            if (mesh == null || mesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is empty or null.");
                return;
            }

            Mesh repaired = mesh.DuplicateMesh();

            int origVerts = repaired.Vertices.Count;
            int origFaces = repaired.Faces.Count;

            repaired.Vertices.CullUnused();
            repaired.Vertices.CombineIdentical(true, true);
            repaired.Faces.CullDegenerateFaces();
            repaired.UnifyNormals();
            if (fillHoles) repaired.FillHoles();
            repaired.Normals.ComputeNormals();
            repaired.Compact();

            bool valid = repaired.IsValid;
            string info = string.Format(
                "Input: {0} verts, {1} faces\nResult: {2} verts, {3} faces\nFill Holes: {4}\nStatus: {5}",
                origVerts, origFaces,
                repaired.Vertices.Count, repaired.Faces.Count,
                fillHoles ? "yes" : "no",
                valid ? "Valid" : "Still invalid");

            DA.SetData(0, repaired);
            DA.SetData(1, info);
        }
    }
}
