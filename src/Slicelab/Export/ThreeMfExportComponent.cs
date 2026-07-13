using System;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Collections;
using Rhino.Geometry;

namespace Slicelab.Export
{
    public class ThreeMfExportComponent : GH_Component
    {
        public ThreeMfExportComponent()
            : base("3MF Export", "SL3mf",
                "Export a mesh to 3MF format with optional metadata.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("D3E4F5A6-B7C8-4D9E-0F1A-2B3C4D5E6F21");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-3mf.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to export", GH_ParamAccess.item);
            pManager.AddTextParameter("Folder Path", "F", "Output folder", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddTextParameter("Title", "T", "3MF metadata: title", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Designer", "D", "3MF metadata: designer", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Description", "De", "3MF metadata: description", GH_ParamAccess.item, "");
            pManager.AddTextParameter("License", "Li", "3MF metadata: license terms", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Rating", "Ra", "3MF metadata: rating", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Copyright", "Co", "3MF metadata: copyright", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Export", "E", "Set to true to export", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Move to Origin", "Mo", "Move mesh to positive XYZ octant", GH_ParamAccess.item, false);

            for (int i = 3; i <= 8; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Full path to saved file", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Export status", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = null;
            string folderPath = "";
            string fileName = "";
            string title = "", designer = "", description = "", license = "", rating = "", copyright = "";
            bool export = false;
            bool moveToOrigin = false;

            if (!DA.GetData(0, ref mesh)) return;
            if (!DA.GetData(1, ref folderPath)) return;
            if (!DA.GetData(2, ref fileName)) return;
            DA.GetData(3, ref title);
            DA.GetData(4, ref designer);
            DA.GetData(5, ref description);
            DA.GetData(6, ref license);
            DA.GetData(7, ref rating);
            DA.GetData(8, ref copyright);
            DA.GetData(9, ref export);
            DA.GetData(10, ref moveToOrigin);

            if (!export)
            {
                DA.SetData(1, "Export toggle is false.");
                return;
            }

            if (mesh == null || !mesh.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is null or invalid.");
                return;
            }
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder path and file name are required.");
                return;
            }

            Directory.CreateDirectory(folderPath);

            if (!fileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
                fileName += ".3mf";

            string fullPath = Path.Combine(folderPath, fileName);

            var options = new ArchivableDictionary();
            options.Set("Title", title ?? "");
            options.Set("Designer", designer ?? "");
            options.Set("Description", description ?? "");
            options.Set("Copyright", copyright ?? "");
            options.Set("LicenseTerms", license ?? "");
            options.Set("Rating", rating ?? "");
            options.Set("MoveOutputToPositiveXYZOctant", moveToOrigin);

            using (var doc = RhinoDoc.CreateHeadless(null))
            {
                doc.Objects.AddMesh(mesh);
                doc.Views.Redraw();

                bool success = doc.Export(fullPath, options);
                if (success)
                {
                    DA.SetData(0, fullPath);
                    DA.SetData(1, $"Exported 3MF to: {fullPath}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "3MF export failed.");
                    DA.SetData(1, "Export failed.");
                }
            }
        }
    }
}
