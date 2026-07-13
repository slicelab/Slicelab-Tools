using Grasshopper.Kernel.Types;

namespace Slicelab.PDF
{
    public class PageSettings
    {
        // Page size enum: 0=A4, 1=Letter, 2=Tabloid, 3=Legal, 4=A3, 5=Custom
        public int PageSize { get; set; }
        public bool IsLandscape { get; set; }
        public double MarginTop { get; set; }
        public double MarginRight { get; set; }
        public double MarginBottom { get; set; }
        public double MarginLeft { get; set; }
        public int Columns { get; set; }
        public double GutterWidth { get; set; }
        public double CustomWidth { get; set; }
        public double CustomHeight { get; set; }

        // Standard page sizes in points (width x height, portrait)
        private static readonly double[][] Sizes = new double[][]
        {
            new[] { 595.28, 841.89 },  // A4
            new[] { 612.0, 792.0 },    // Letter
            new[] { 792.0, 1224.0 },   // Tabloid
            new[] { 612.0, 1008.0 },   // Legal
            new[] { 841.89, 1190.55 }, // A3
        };

        public double PageWidth
        {
            get
            {
                double w = PageSize >= 0 && PageSize < Sizes.Length ? Sizes[PageSize][0] : CustomWidth;
                double h = PageSize >= 0 && PageSize < Sizes.Length ? Sizes[PageSize][1] : CustomHeight;
                return IsLandscape ? h : w;
            }
        }

        public double PageHeight
        {
            get
            {
                double w = PageSize >= 0 && PageSize < Sizes.Length ? Sizes[PageSize][0] : CustomWidth;
                double h = PageSize >= 0 && PageSize < Sizes.Length ? Sizes[PageSize][1] : CustomHeight;
                return IsLandscape ? w : h;
            }
        }

        public double ContentWidth => PageWidth - MarginLeft - MarginRight;
        public double ContentHeight => PageHeight - MarginTop - MarginBottom;
        public double ColumnWidth => (ContentWidth - GutterWidth * (Columns - 1)) / Columns;

        public PageSettings() { }

        public PageSettings(int pageSize, bool isLandscape, double mt, double mr, double mb, double ml,
            int columns, double gutter, double customW, double customH)
        {
            PageSize = pageSize;
            IsLandscape = isLandscape;
            MarginTop = mt;
            MarginRight = mr;
            MarginBottom = mb;
            MarginLeft = ml;
            Columns = columns < 1 ? 1 : columns;
            GutterWidth = gutter;
            CustomWidth = customW;
            CustomHeight = customH;
        }

        public PageSettings Duplicate()
        {
            return new PageSettings(PageSize, IsLandscape, MarginTop, MarginRight, MarginBottom, MarginLeft,
                Columns, GutterWidth, CustomWidth, CustomHeight);
        }

        public static string SizeName(int index)
        {
            switch (index)
            {
                case 0: return "A4";
                case 1: return "Letter";
                case 2: return "Tabloid";
                case 3: return "Legal";
                case 4: return "A3";
                case 5: return "Custom";
                default: return "Unknown";
            }
        }
    }

    public class GH_PageSettings : GH_Goo<PageSettings>
    {
        public GH_PageSettings() : base() { }
        public GH_PageSettings(PageSettings settings) : base(settings) { }

        public override bool IsValid => Value != null && Value.PageWidth > 0 && Value.PageHeight > 0;
        public override string TypeName => "PageSettings";
        public override string TypeDescription => "PDF page size, orientation, margins, and column layout";

        public override IGH_Goo Duplicate()
        {
            return new GH_PageSettings(Value?.Duplicate());
        }

        public override string ToString()
        {
            if (Value == null) return "PageSettings: (null)";
            string orient = Value.IsLandscape ? "Landscape" : "Portrait";
            return $"PageSettings: {PageSettings.SizeName(Value.PageSize)} {orient} ({Value.PageWidth:F0}x{Value.PageHeight:F0}pt, {Value.Columns}col)";
        }
    }
}
