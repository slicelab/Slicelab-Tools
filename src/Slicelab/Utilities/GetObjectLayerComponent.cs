using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;

namespace Slicelab.Utilities
{
    public class GetObjectLayerComponent : GH_Component
    {
        public GetObjectLayerComponent()
            : base("Get Object Layer", "SLLayer",
                "Get the layer name for Rhino objects by their IDs.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("5E6F7A8B-9C0D-4E1F-2A3B-4C5D6E7F8005");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Layer.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Objects", "O", "Referenced Rhino objects or object GUIDs", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Full Path", "F", "Return full layer path instead of short name", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Refresh", "R", "Toggle to refresh results", GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Layer Names", "L", "Layer names for each object", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var items = new List<object>();
            bool fullPath = false;
            if (!DA.GetDataList(0, items)) return;
            DA.GetData(1, ref fullPath);

            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No active Rhino document.");
                return;
            }

            var layerNames = new List<string>(items.Count);
            foreach (object item in items)
            {
                Guid id = Guid.Empty;

                if (item is GH_Guid ghGuid)
                    id = ghGuid.Value;
                else if (item is IGH_GeometricGoo goo)
                    id = goo.ReferenceID;
                else if (item is Guid rawGuid)
                    id = rawGuid;
                else if (item is string str)
                    Guid.TryParse(str, out id);

                if (id == Guid.Empty)
                {
                    layerNames.Add("Invalid GUID");
                    continue;
                }

                var obj = doc.Objects.FindId(id);
                if (obj == null)
                {
                    layerNames.Add("Object not found");
                    continue;
                }

                var layer = doc.Layers[obj.Attributes.LayerIndex];
                layerNames.Add(fullPath ? layer.FullPath : layer.Name);
            }

            DA.SetDataList(0, layerNames);
        }
    }
}
