using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.TetTools
{
    public abstract class LatticeComponentBase : GH_Component
    {
        protected LatticeComponentBase(string name, string nickname, string description)
            : base(name, nickname, description, "Slicelab Tools", "Tet Tools")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new TetDataParam(), "Tet Data", "TD", "Combined points and tetra indices from Tetrahedralize", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Inner Struts", "IS", "Interior lattice struts", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Struts", "SS", "Boundary skin struts", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "N", "Lattice nodes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Statistics", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!LatticeHelper.ReadTetraInputs(DA, this, out var points, out var tetraIndices, out int tetCount))
                return;

            ComputeLattice(points, tetraIndices, tetCount,
                out var innerStruts, out var surfaceStruts, out var nodes);

            DA.SetDataList(0, innerStruts);
            DA.SetDataList(1, surfaceStruts);
            DA.SetDataList(2, nodes);
            DA.SetData(3, $"Inner struts: {innerStruts.Count}\nSurface struts: {surfaceStruts.Count}\nNodes: {nodes.Count}");
        }

        protected abstract void ComputeLattice(List<Point3d> points, int[] tetraIndices, int tetCount,
            out List<Line> innerStruts, out List<Line> surfaceStruts, out List<Point3d> nodes);
    }
}
