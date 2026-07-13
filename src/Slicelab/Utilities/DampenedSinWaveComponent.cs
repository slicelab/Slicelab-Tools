using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace Slicelab.Utilities
{
    public class DampenedSinWaveComponent : GH_Component
    {
        public DampenedSinWaveComponent()
            : base("Dampened Sin Wave", "SLDamp",
                "Generate a dampened sine wave: Y = A * e^(-D*F*x) * sin(F * x * 2pi). Damping is relative to wavelength so behavior stays consistent across frequencies.",
                "Slicelab Tools", "Utilities")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quinary;
        public override Guid ComponentGuid => new Guid("B7A8C9D0-E1F2-4A3B-4C5D-6E7F809112B3");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Damp.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("X", "X", "Input X values", GH_ParamAccess.list);
            pManager.AddNumberParameter("Amplitude", "A", "Wave amplitude", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Frequency", "F", "Wave frequency", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Damping", "D", "Damping per cycle (0 = no damping, 1 = fast decay)", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Y", "Y", "Output Y values", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var xValues = new List<double>();
            double amplitude = 1.0;
            double frequency = 1.0;
            double damping = 0.5;

            if (!DA.GetDataList(0, xValues)) return;
            DA.GetData(1, ref amplitude);
            DA.GetData(2, ref frequency);
            DA.GetData(3, ref damping);

            var yValues = new List<double>(xValues.Count);
            foreach (double x in xValues)
            {
                double y = amplitude * Math.Exp(-damping * x * frequency) * Math.Sin(frequency * x * Math.PI * 2.0);
                yValues.Add(y);
            }

            DA.SetDataList(0, yValues);
        }
    }
}
