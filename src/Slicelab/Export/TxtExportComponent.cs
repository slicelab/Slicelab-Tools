using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;

namespace Slicelab.Export
{
    public class TxtExportComponent : GH_Component
    {
        public TxtExportComponent()
            : base("TXT Export", "SLTxt",
                "Export a list of text lines to a .txt file.",
                "Slicelab Tools", "Export")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("E4F5A6B7-C8D9-4E0F-1A2B-3C4D5E6F7A22");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Txt.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "T", "Lines of text to write", GH_ParamAccess.list);
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
            var lines = new List<string>();
            string folderPath = "";
            string fileName = "";
            bool export = false;

            if (!DA.GetDataList(0, lines)) return;
            if (!DA.GetData(1, ref folderPath)) return;
            if (!DA.GetData(2, ref fileName)) return;
            DA.GetData(3, ref export);

            if (!export)
            {
                DA.SetData(1, "Export toggle is false.");
                return;
            }

            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder path and file name are required.");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder path does not exist.");
                return;
            }

            if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                fileName += ".txt";

            string fullPath = Path.Combine(folderPath, fileName);

            try
            {
                File.WriteAllLines(fullPath, lines);
                DA.SetData(0, fullPath);
                DA.SetData(1, $"Wrote {lines.Count} lines to: {fullPath}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Write failed: " + ex.Message);
            }
        }
    }
}
