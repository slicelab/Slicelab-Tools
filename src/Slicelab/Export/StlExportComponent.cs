using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.Export
{
    public class StlExportComponent : GH_Component
    {
        public StlExportComponent()
            : base("STL Export", "SLStl",
                "Export meshes to a binary STL file.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("C2D3E4F5-A6B7-4C8D-9E0F-1A2B3C4D5E20");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Stl.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Meshes to export", GH_ParamAccess.list);
            pManager.AddTextParameter("Folder Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Export", "E", "Set to true to export", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Full path to saved file", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Export status", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var meshes = new List<Mesh>();
            string folderPath = "";
            string fileName = "";
            bool export = false;

            if (!DA.GetDataList(0, meshes)) return;
            if (!DA.GetData(1, ref folderPath)) return;
            if (!DA.GetData(2, ref fileName)) return;
            DA.GetData(3, ref export);

            if (!export)
            {
                DA.SetData(1, "Export toggle is false.");
                return;
            }

            if (meshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No meshes provided.");
                return;
            }
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder path and file name are required.");
                return;
            }

            Directory.CreateDirectory(folderPath);

            if (!fileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
                fileName += ".stl";

            string fullPath = Path.Combine(folderPath, fileName);

            // Combine all meshes
            Mesh combined = new Mesh();
            foreach (var m in meshes)
                combined.Append(m);
            combined.Compact();

            try
            {
                WriteBinaryStl(combined, fullPath);
                DA.SetData(0, fullPath);
                DA.SetData(1, $"Exported {combined.Faces.Count} faces to: {fullPath}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Export failed: " + ex.Message);
            }
        }

        private static void WriteBinaryStl(Mesh mesh, string filePath)
        {
            using (var writer = new BinaryWriter(File.Create(filePath)))
            {
                // 80-byte header
                writer.Write(new byte[80]);

                // Count triangles (quads split into 2)
                int triangleCount = 0;
                foreach (var face in mesh.Faces)
                    triangleCount += face.IsQuad ? 2 : 1;
                writer.Write((uint)triangleCount);

                foreach (var face in mesh.Faces)
                {
                    var v1 = (Point3d)mesh.Vertices[face.A];
                    var v2 = (Point3d)mesh.Vertices[face.B];
                    var v3 = (Point3d)mesh.Vertices[face.C];

                    WriteTriangle(writer, v1, v2, v3);

                    if (face.IsQuad)
                    {
                        var v4 = (Point3d)mesh.Vertices[face.D];
                        WriteTriangle(writer, v1, v3, v4);
                    }
                }
            }
        }

        private static void WriteTriangle(BinaryWriter writer, Point3d v1, Point3d v2, Point3d v3)
        {
            var normal = Vector3d.CrossProduct(v2 - v1, v3 - v1);
            normal.Unitize();

            writer.Write((float)normal.X);
            writer.Write((float)normal.Y);
            writer.Write((float)normal.Z);

            writer.Write((float)v1.X); writer.Write((float)v1.Y); writer.Write((float)v1.Z);
            writer.Write((float)v2.X); writer.Write((float)v2.Y); writer.Write((float)v2.Z);
            writer.Write((float)v3.X); writer.Write((float)v3.Y); writer.Write((float)v3.Z);

            writer.Write((ushort)0);
        }
    }
}
