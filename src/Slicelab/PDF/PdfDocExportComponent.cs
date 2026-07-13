using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Slicelab.PDF
{
    public class PdfDocExportComponent : GH_Component
    {
        public PdfDocExportComponent()
            : base("PDF Doc Export", "SLPdfD",
                "Compose PDF elements into a document with page settings and save to file.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C06");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PdfD.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Page Settings", "PS", "Page settings from PDF Page Settings component", GH_ParamAccess.item);
            pManager.AddGenericParameter("Elements", "E", "PDF elements (text, image, geometry, group)", GH_ParamAccess.list);
            pManager.AddTextParameter("File Name", "N", "Output file name (without extension)", GH_ParamAccess.item);
            pManager.AddTextParameter("Folder Path", "F", "Output folder path", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Save", "Sa", "Set to true to save PDF", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Full path to saved file", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Export info", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object settingsObj = null;
            var elementGoos = new List<IGH_Goo>();
            string fileName = "";
            string folderPath = "";
            bool save = false;

            if (!DA.GetData(0, ref settingsObj)) return;
            if (!DA.GetDataList(1, elementGoos)) return;
            if (!DA.GetData(2, ref fileName)) return;
            if (!DA.GetData(3, ref folderPath)) return;
            DA.GetData(4, ref save);

            // Unwrap PageSettings
            PageSettings settings = null;
            if (settingsObj is GH_PageSettings ghPs)
                settings = ghPs.Value;
            else if (settingsObj is GH_ObjectWrapper wrapper && wrapper.Value is GH_PageSettings inner)
                settings = inner.Value;
            else if (settingsObj is GH_ObjectWrapper wrapper2 && wrapper2.Value is PageSettings ps)
                settings = ps;

            if (settings == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid Page Settings input.");
                return;
            }

            // Unwrap elements
            var elements = new List<PdfElement>();
            int skipped = 0;
            foreach (var goo in elementGoos)
            {
                if (goo == null) { skipped++; continue; }

                if (goo is GH_PdfElement ghEl && ghEl.Value != null)
                    elements.Add(ghEl.Value);
                else if (goo is GH_ObjectWrapper ow && ow.Value is GH_PdfElement ghEl2 && ghEl2.Value != null)
                    elements.Add(ghEl2.Value);
                else if (goo is GH_ObjectWrapper ow2 && ow2.Value is PdfElement pe)
                    elements.Add(pe);
                else
                    skipped++;
            }

            if (elements.Count == 0)
            {
                string typeInfo = elementGoos.Count > 0 && elementGoos[0] != null
                    ? $" (received type: {elementGoos[0].GetType().Name})"
                    : "";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No valid PDF elements provided. {elementGoos.Count} items received, {skipped} skipped.{typeInfo}");
                return;
            }

            if (!save)
            {
                DA.SetData(1, $"Ready: {elements.Count} elements. Set Save=true to export.");
                return;
            }

            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File name and folder path are required.");
                return;
            }

            // Compose and save
            try
            {
                var doc = PdfLayoutHelper.Compose(settings, elements, out string info);

                Directory.CreateDirectory(folderPath);
                string fullPath = Path.Combine(folderPath,
                    fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".pdf");

                doc.Save(fullPath);
                doc.Dispose();

                DA.SetData(0, fullPath);
                DA.SetData(1, $"Saved: {info}");
            }
            catch (Exception ex)
            {
                string detail = ex.Message;
                if (ex.StackTrace != null)
                {
                    // Show the first relevant frame
                    var lines = ex.StackTrace.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("Slicelab"))
                        {
                            detail += " | at " + line.Trim();
                            break;
                        }
                    }
                }
                if (ex.InnerException != null)
                    detail += " | Inner: " + ex.InnerException.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, detail);
            }
        }
    }
}
