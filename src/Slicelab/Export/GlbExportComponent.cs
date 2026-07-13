using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Collections;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Slicelab.Export
{
    public class GlbExportComponent : GH_Component
    {
        public GlbExportComponent()
            : base("GLB Export", "SLGlb",
                "Export meshes to GLB or glTF format with full control over export options.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("A7B8C9D0-E1F2-4A3B-5C6D-7E8F9A0B1C2D");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Glb.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Meshes to export", GH_ParamAccess.list);
            pManager.AddTextParameter("Folder Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Export", "E", "Set to true to export", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Use GLB", "GLB", "True for binary GLB, false for glTF", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Map Z to Y", "ZY", "Transform Rhino Z-up to glTF Y-up", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Double Sided", "DS", "Render both sides of faces (disables backface culling)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Vertex Colors", "VC", "Export vertex colors", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Vertex Normals", "VN", "Export vertex normals", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Texture Coords", "TC", "Export texture coordinates", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Materials", "Mat", "Export materials", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Open Meshes", "OM", "Allow exporting open meshes", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Layers", "Ly", "Export layer hierarchy as nodes", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Display Color", "DC", "Use display color when no material is assigned", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Draco", "Dr", "Enable Draco mesh compression", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Draco Level", "DL", "Draco compression level (1-10)", GH_ParamAccess.item, 7);
            pManager.AddIntegerParameter("Quantize Position", "QP", "Draco quantization bits for positions (8-32)", GH_ParamAccess.item, 14);
            pManager.AddIntegerParameter("Quantize Normal", "QN", "Draco quantization bits for normals (8-32)", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Quantize TexCoord", "QT", "Draco quantization bits for texture coordinates (8-32)", GH_ParamAccess.item, 12);

            // Make all options optional (indices 4-18)
            for (int i = 4; i <= 18; i++)
                pManager[i].Optional = true;
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
            bool useGlb = true;
            bool mapZtoY = true;
            bool doubleSided = true;
            bool vertexColors = false;
            bool vertexNormals = true;
            bool textureCoords = true;
            bool materials = true;
            bool openMeshes = true;
            bool layers = false;
            bool displayColor = true;
            bool draco = false;
            int dracoLevel = 7;
            int quantizePos = 14;
            int quantizeNorm = 10;
            int quantizeTex = 12;

            if (!DA.GetDataList(0, meshes)) return;
            if (!DA.GetData(1, ref folderPath)) return;
            if (!DA.GetData(2, ref fileName)) return;
            DA.GetData(3, ref export);
            DA.GetData(4, ref useGlb);
            DA.GetData(5, ref mapZtoY);
            DA.GetData(6, ref doubleSided);
            DA.GetData(7, ref vertexColors);
            DA.GetData(8, ref vertexNormals);
            DA.GetData(9, ref textureCoords);
            DA.GetData(10, ref materials);
            DA.GetData(11, ref openMeshes);
            DA.GetData(12, ref layers);
            DA.GetData(13, ref displayColor);
            DA.GetData(14, ref draco);
            DA.GetData(15, ref dracoLevel);
            DA.GetData(16, ref quantizePos);
            DA.GetData(17, ref quantizeNorm);
            DA.GetData(18, ref quantizeTex);

            if (!export)
            {
                DA.SetData(1, "Export toggle is false.");
                return;
            }

            // Validate meshes
            var validMeshes = new List<Mesh>();
            foreach (var m in meshes)
                if (m != null && m.IsValid) validMeshes.Add(m);

            if (validMeshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid meshes provided.");
                return;
            }
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder path and file name are required.");
                return;
            }

            Directory.CreateDirectory(folderPath);

            string ext = useGlb ? ".glb" : ".gltf";
            if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                fileName += ext;

            string fullPath = Path.Combine(folderPath, fileName);

            // Clamp values
            dracoLevel = Math.Max(1, Math.Min(10, dracoLevel));
            quantizePos = Math.Max(8, Math.Min(32, quantizePos));
            quantizeNorm = Math.Max(8, Math.Min(32, quantizeNorm));
            quantizeTex = Math.Max(8, Math.Min(32, quantizeTex));

            // Build options dictionary (same pattern as 3MF export)
            var options = new ArchivableDictionary();
            options.Set("MapZToY", mapZtoY);
            options.Set("CullBackfaces", !doubleSided);
            options.Set("ExportVertexColors", vertexColors);
            options.Set("ExportVertexNormals", vertexNormals);
            options.Set("ExportTextureCoordinates", textureCoords);
            options.Set("ExportMaterials", materials);
            options.Set("ExportOpenMeshes", openMeshes);
            options.Set("ExportLayers", layers);
            options.Set("UseDisplayColorForUnsetMaterials", displayColor);
            options.Set("UseDracoCompression", draco);
            if (draco)
            {
                options.Set("DracoCompressionLevel", dracoLevel);
                options.Set("DracoQuantizationBitsPosition", quantizePos);
                options.Set("DracoQuantizationBitsNormal", quantizeNorm);
                options.Set("DracoQuantizationBitsTextureCoordinate", quantizeTex);
            }

            using (var doc = RhinoDoc.CreateHeadless(null))
            {
                foreach (var m in validMeshes)
                    doc.Objects.AddMesh(m.DuplicateMesh(), new ObjectAttributes());

                bool success = doc.Export(fullPath, options);

                if (success && File.Exists(fullPath))
                {
                    long sizeKB = Math.Max(1, new FileInfo(fullPath).Length / 1024);
                    string dracoNote = draco ? $", Draco level {dracoLevel}" : "";
                    DA.SetData(0, fullPath);
                    DA.SetData(1, $"Exported {validMeshes.Count} mesh(es) to {ext.TrimStart('.')}. Size: {sizeKB} KB{dracoNote}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Export failed. Rhino's glTF exporter may not support dictionary options — try updating Rhino 8.");
                    DA.SetData(1, "Export failed.");
                }
            }
        }
    }
}
