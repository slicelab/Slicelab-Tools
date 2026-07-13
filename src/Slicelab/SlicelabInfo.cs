using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Slicelab.PDF;

namespace Slicelab
{
    public class SlicelabPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            Bitmap tabIcon = IconHelper.LoadIconRaw("SL-Tab.png");
            if (tabIcon != null)
                Instances.ComponentServer.AddCategoryIcon("Slicelab Tools", tabIcon);

            PdfFontResolver.Register();

            return GH_LoadingInstruction.Proceed;
        }
    }

    public class SlicelabInfo : GH_AssemblyInfo
    {
        public override string Name => "Slicelab Tools";
        public override string Description => "Mesh processing, tetrahedral lattice generation, and texture mapping tools for Grasshopper.";
        public override Guid Id => new Guid("8A4B2C1D-3E5F-6071-9B8C-D0E1F2A34567");
        public override Bitmap Icon => IconHelper.LoadIconRaw("SL-Tab.png");
        public override string AuthorName => "Arthur Azoulai";
        public override string AuthorContact => "";
        public override string Version => "0.1.0";
        public override GH_LibraryLicense License => GH_LibraryLicense.unset;
    }
}
