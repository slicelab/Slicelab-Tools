using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Slicelab.PDF
{
    public class PdfGroupElementComponent : GH_Component
    {
        public PdfGroupElementComponent()
            : base("PDF Group", "SLPGrp",
                "Group PDF elements so they stay together on a page. Each input branch becomes a separate group.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C05");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PGrp.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "E", "PDF elements to group (DataTree: each branch = one group)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Space After", "SA", "Space after each group in points", GH_ParamAccess.item, 6.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF group element(s)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_Goo> elementTree = null;
            double spaceAfter = 6;

            if (!DA.GetDataTree(0, out elementTree)) return;
            DA.GetData(1, ref spaceAfter);

            if (elementTree == null || elementTree.DataCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No elements provided.");
                return;
            }

            var groups = new List<GH_PdfElement>();

            foreach (var path in elementTree.Paths)
            {
                var branch = elementTree[path];
                var children = new List<PdfElement>();

                foreach (var goo in branch)
                {
                    if (goo == null) continue;
                    var ghEl = goo as GH_PdfElement;
                    if (ghEl == null)
                    {
                        // Try unwrap from GH_ObjectWrapper
                        if (goo is GH_ObjectWrapper wrapper && wrapper.Value is PdfElement pe)
                            children.Add(pe);
                        else if (goo is GH_ObjectWrapper wrapper2 && wrapper2.Value is GH_PdfElement inner)
                            children.Add(inner.Value);
                        continue;
                    }
                    if (ghEl.Value != null)
                        children.Add(ghEl.Value);
                }

                if (children.Count == 0) continue;

                var group = new PdfGroupElement
                {
                    Children = children,
                    SpaceAfter = spaceAfter
                };
                groups.Add(new GH_PdfElement(group));
            }

            DA.SetDataList(0, groups);
        }
    }
}
