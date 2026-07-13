using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino;

namespace Slicelab.Utilities
{
    public class LayerVisibilityComponent : GH_Component
    {
        public LayerVisibilityComponent()
            : base("Layer Visibility", "SLLayerVis",
                "Toggle a layer's visibility on or off by name.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("6F7A8B9C-0D1E-4F2A-3B4C-5D6E7F809106");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-LayerVis.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Layer Name", "L", "Name of the layer to toggle", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Visible", "V", "True to show, false to hide", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "I", "Result message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string layerName = "";
            bool visible = true;
            if (!DA.GetData(0, ref layerName)) return;
            DA.GetData(1, ref visible);

            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No active Rhino document.");
                return;
            }

            int layerIndex = doc.Layers.FindByFullPath(layerName, -1);
            if (layerIndex < 0)
            {
                // Try matching by short name if full path didn't match
                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    if (doc.Layers[i].Name.Equals(layerName, StringComparison.OrdinalIgnoreCase) && !doc.Layers[i].IsDeleted)
                    {
                        layerIndex = i;
                        break;
                    }
                }
            }

            if (layerIndex < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Layer '{layerName}' not found.");
                DA.SetData(0, $"Layer '{layerName}' not found.");
                return;
            }

            var layer = doc.Layers[layerIndex];
            layer.IsVisible = visible;
            doc.Views.Redraw();

            DA.SetData(0, $"Layer '{layerName}' set to {(visible ? "visible" : "hidden")}.");
        }
    }
}
