using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using PdfSharpCore.Drawing;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Slicelab.PDF
{
    public class PdfMake2DComponent : GH_Component
    {
        public PdfMake2DComponent()
            : base("PDF Make2D", "SLPMk2",
                "Project 3D geometry to 2D linework for PDF layout. Plan view by default, optional hidden line drawing.",
                "Slicelab Tools", "PDF")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C07");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PMk2.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Curves, breps, surfaces, or meshes to project", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane", "P", "View plane (Z-axis = view direction)", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddColourParameter("Stroke Color", "SC", "Visible line color", GH_ParamAccess.item, Color.Black);
            pManager.AddNumberParameter("Stroke Weight", "SW", "Visible line weight in points", GH_ParamAccess.item, 0.5);
            pManager.AddBooleanParameter("Show Hidden", "SH", "Enable hidden line computation", GH_ParamAccess.item, false);
            pManager.AddColourParameter("Hidden Color", "HC", "Hidden line color", GH_ParamAccess.item, Color.LightGray);
            pManager.AddNumberParameter("Hidden Weight", "HW", "Hidden line weight in points", GH_ParamAccess.item, 0.25);
            pManager.AddIntegerParameter("Alignment", "A", "Left=0, Center=1, Right=2", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Height", "H", "Fixed height in PDF points (0 = auto-scale to column width)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Space After", "SA", "Space after element in points", GH_ParamAccess.item, 6.0);

            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "PDF Make2D element", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();
            if (Params.Input[7].SourceCount > 0) return;
            var grip = Params.Input[7].Attributes.InputGrip;
            var vl = new GH_ValueList();
            vl.CreateAttributes();
            vl.ListMode = GH_ValueListMode.DropDown;
            vl.NickName = "Alignment";
            vl.ListItems.Clear();
            vl.ListItems.Add(new GH_ValueListItem("Left", "0"));
            vl.ListItems.Add(new GH_ValueListItem("Center", "1"));
            vl.ListItems.Add(new GH_ValueListItem("Right", "2"));
            vl.Attributes.PerformLayout();
            IconHelper.AlignWidget(vl, grip);
            document.AddObject(vl, false);
            Params.Input[7].AddSource(vl);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var geoGoos = new List<IGH_GeometricGoo>();
            Plane viewPlane = Plane.WorldXY;
            Color strokeColor = Color.Black;
            double strokeWeight = 0.5;
            bool showHidden = false;
            Color hiddenColor = Color.LightGray;
            double hiddenWeight = 0.25;
            int alignment = 0;
            double fixedHeight = 0;
            double spaceAfter = 6;

            if (!DA.GetDataList(0, geoGoos)) return;
            DA.GetData(1, ref viewPlane);
            DA.GetData(2, ref strokeColor);
            DA.GetData(3, ref strokeWeight);
            DA.GetData(4, ref showHidden);
            DA.GetData(5, ref hiddenColor);
            DA.GetData(6, ref hiddenWeight);
            DA.GetData(7, ref alignment);
            DA.GetData(8, ref fixedHeight);
            DA.GetData(9, ref spaceAfter);

            // Extract GeometryBase from goos
            var geometries = new List<GeometryBase>();
            foreach (var goo in geoGoos)
            {
                if (goo == null) continue;
                if (goo is GH_Curve ghCrv && ghCrv.Value != null)
                    geometries.Add(ghCrv.Value);
                else if (goo is GH_Surface ghSrf && ghSrf.Value != null)
                    geometries.Add(ghSrf.Value);
                else if (goo is GH_Brep ghBrep && ghBrep.Value != null)
                    geometries.Add(ghBrep.Value);
                else if (goo is GH_Mesh ghMesh && ghMesh.Value != null)
                    geometries.Add(ghMesh.Value);
                else if (goo is GH_SubD ghSubD && ghSubD.Value != null)
                {
                    var subDBrep = ghSubD.Value.ToBrep(SubDToBrepOptions.Default);
                    if (subDBrep != null) geometries.Add(subDBrep);
                }
                else
                {
                    var geom = goo.ScriptVariable() as GeometryBase;
                    if (geom != null)
                    {
                        // Handle SubD coming through as raw GeometryBase
                        if (geom is SubD subD)
                        {
                            var brep = subD.ToBrep(SubDToBrepOptions.Default);
                            if (brep != null) geometries.Add(brep);
                        }
                        else
                            geometries.Add(geom);
                    }
                }
            }

            if (geometries.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid geometry provided.");
                return;
            }

            double unitScale = PdfLayoutHelper.GetUnitScale();
            double tolerance = PdfLayoutHelper.GetModelTolerance();

            List<XPoint[]> visibleLines;
            List<XPoint[]> hiddenLines;
            double bboxW, bboxH;

            // Always use HiddenLineDrawing for proper occlusion
            ComputeHiddenLineDrawing(geometries, viewPlane, tolerance, unitScale,
                out visibleLines, out hiddenLines, out bboxW, out bboxH);

            // If ShowHidden is off, discard hidden lines
            if (!showHidden)
                hiddenLines = new List<XPoint[]>();

            if (visibleLines.Count == 0 && hiddenLines.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No lines generated from geometry.");
                return;
            }

            var element = new PdfMake2DElement
            {
                VisibleLines = visibleLines,
                HiddenLines = hiddenLines,
                StrokeColor = XColor.FromArgb(strokeColor.A, strokeColor.R, strokeColor.G, strokeColor.B),
                HiddenColor = XColor.FromArgb(hiddenColor.A, hiddenColor.R, hiddenColor.G, hiddenColor.B),
                StrokeWeight = strokeWeight,
                HiddenWeight = hiddenWeight,
                BBoxWidth = bboxW,
                BBoxHeight = bboxH,
                FixedHeight = fixedHeight,
                Alignment = alignment,
                SpaceAfter = spaceAfter
            };

            DA.SetData(0, new GH_PdfElement(element));
        }

        // ─── Hidden line drawing (used for all modes) ───────────

        private void ComputeHiddenLineDrawing(
            List<GeometryBase> geometries, Plane viewPlane,
            double tolerance, double unitScale,
            out List<XPoint[]> visibleLines, out List<XPoint[]> hiddenLines,
            out double bboxW, out double bboxH)
        {
            visibleLines = new List<XPoint[]>();
            hiddenLines = new List<XPoint[]>();
            bboxW = 0;
            bboxH = 0;

            var hldParams = new HiddenLineDrawingParameters();

            // Set up viewport from the plane
            var vp = new ViewportInfo();
            vp.SetCameraLocation(viewPlane.Origin + viewPlane.ZAxis * 1000);
            vp.SetCameraDirection(-viewPlane.ZAxis);
            vp.SetCameraUp(viewPlane.YAxis);
            vp.SetFrustum(-100, 100, -100, 100, 1, 10000);
            vp.ChangeToParallelProjection(true);
            hldParams.SetViewport(vp);
            hldParams.AbsoluteTolerance = tolerance;

            foreach (var geom in geometries)
                hldParams.AddGeometry(geom, Transform.Identity, 0);

            HiddenLineDrawing result = null;
            try
            {
                result = HiddenLineDrawing.Compute(hldParams, true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"HiddenLineDrawing failed: {ex.Message}");
                return;
            }

            if (result == null) return;

            // Compute bounding box from all segment curves
            var bbox = BoundingBox.Empty;
            foreach (var seg in result.Segments)
            {
                var c = seg.CurveGeometry;
                if (c != null) bbox.Union(c.GetBoundingBox(true));
            }
            if (!bbox.IsValid || (bbox.Diagonal.X < 1e-10 && bbox.Diagonal.Y < 1e-10))
            {
                result.Dispose();
                return;
            }

            double rawW = Math.Max(bbox.Diagonal.X, 1e-6);
            double rawH = Math.Max(bbox.Diagonal.Y, 1e-6);
            bboxW = rawW * unitScale;
            bboxH = rawH * unitScale;

            foreach (var segment in result.Segments)
            {
                var crv = segment.CurveGeometry;
                if (crv == null) continue;

                bool isVisible = segment.SegmentVisibility == HiddenLineDrawingSegment.Visibility.Visible;
                var targetList = isVisible ? visibleLines : hiddenLines;

                Polyline polyline;
                if (!crv.TryGetPolyline(out polyline))
                {
                    var plc = crv.ToPolyline(tolerance, RhinoMath.ToRadians(1.0), 0, 0);
                    if (plc == null || !plc.TryGetPolyline(out polyline)) continue;
                }

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    var p0 = polyline[i];
                    var p1 = polyline[i + 1];
                    targetList.Add(new[]
                    {
                        new XPoint((p0.X - bbox.Min.X) * unitScale, (rawH - (p0.Y - bbox.Min.Y)) * unitScale),
                        new XPoint((p1.X - bbox.Min.X) * unitScale, (rawH - (p1.Y - bbox.Min.Y)) * unitScale)
                    });
                }
            }

            result.Dispose();
        }
    }
}
