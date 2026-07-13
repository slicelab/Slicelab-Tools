using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GH_Geo = Grasshopper.Kernel.Geometry;

namespace Slicelab.Geometry
{
    public class VoronoiRelaxationComponent : GH_TaskCapableComponent<VoronoiRelaxationComponent.SolveResults>
    {
        public VoronoiRelaxationComponent()
            : base("Voronoi Relaxation", "SLVoronoi",
                "Lloyd's relaxation on a 2D Voronoi diagram for more uniform cell sizes.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("E4F5A6B7-C8D9-4E0F-1A2B-3C4D5E6F7A80");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-Voronoi.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Seed points", GH_ParamAccess.list);
            pManager.AddCurveParameter("Boundary", "B", "Closed boundary curve", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iterations", "It", "Number of relaxation steps", GH_ParamAccess.item, 10);
            pManager.AddPlaneParameter("Plane", "Pl", "Projection plane (optional, default WorldXY)", GH_ParamAccess.item, Plane.WorldXY);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Relaxed point positions", GH_ParamAccess.list);
            pManager.AddCurveParameter("Cells", "C", "Voronoi cell boundaries", GH_ParamAccess.list);
        }

        public class SolveResults
        {
            public List<Point3d> Points;
            public List<Curve> Cells;
        }

        private static SolveResults Compute(List<Point3d> points, Curve boundary, int iterations, Plane plane)
        {
            // Project points onto working plane
            var xformToPlane = Transform.ChangeBasis(Plane.WorldXY, plane);
            var xformFromPlane = Transform.ChangeBasis(plane, Plane.WorldXY);

            var pts2d = new List<Point3d>(points.Count);
            foreach (var pt in points)
            {
                var p = new Point3d(pt);
                p.Transform(xformToPlane);
                p = new Point3d(p.X, p.Y, 0);
                pts2d.Add(p);
            }

            // Project boundary
            var boundary2d = boundary.DuplicateCurve();
            boundary2d.Transform(xformToPlane);

            // Get bounding box for Voronoi outer boundary (expanded)
            var bbox = boundary2d.GetBoundingBox(false);
            double expand = bbox.Diagonal.Length * 0.5;
            bbox.Inflate(expand, expand, 0);

            // Build bounding nodes for Voronoi solver
            var boundingNodes = new GH_Geo.Node2List();
            boundingNodes.Append(new GH_Geo.Node2(bbox.Min.X, bbox.Min.Y));
            boundingNodes.Append(new GH_Geo.Node2(bbox.Max.X, bbox.Min.Y));
            boundingNodes.Append(new GH_Geo.Node2(bbox.Max.X, bbox.Max.Y));
            boundingNodes.Append(new GH_Geo.Node2(bbox.Min.X, bbox.Max.Y));

            // Lloyd's relaxation
            for (int iter = 0; iter < iterations; iter++)
            {
                // Build Node2List for Delaunay/Voronoi
                var nodeList = new GH_Geo.Node2List();
                for (int i = 0; i < pts2d.Count; i++)
                    nodeList.Append(new GH_Geo.Node2(pts2d[i].X, pts2d[i].Y));

                // Compute Delaunay connectivity
                var delaunay = GH_Geo.Delaunay.Solver.Solve_Connectivity(nodeList, 0.001, false);
                if (delaunay == null) break;

                // Compute Voronoi cells
                var voronoi = GH_Geo.Voronoi.Solver.Solve_Connectivity(
                    nodeList, delaunay, boundingNodes);
                if (voronoi == null) break;

                // Move each point to the centroid of its clipped Voronoi cell
                var newPts = new List<Point3d>(pts2d.Count);
                for (int i = 0; i < pts2d.Count; i++)
                {
                    if (i >= voronoi.Count)
                    {
                        newPts.Add(pts2d[i]);
                        continue;
                    }

                    var cell = voronoi[i];
                    var cellPoly = cell.ToPolyline();
                    var cellCurve = cellPoly.ToPolylineCurve();

                    if (cellCurve == null)
                    {
                        newPts.Add(pts2d[i]);
                        continue;
                    }

                    // Clip cell to boundary
                    var clipped = Curve.CreateBooleanIntersection(cellCurve, boundary2d, 0.001);

                    if (clipped == null || clipped.Length == 0)
                    {
                        // Point may be outside boundary — pull to nearest point on boundary
                        boundary2d.ClosestPoint(pts2d[i], out double t);
                        newPts.Add(boundary2d.PointAt(t));
                        continue;
                    }

                    // Compute centroid of the clipped polygon
                    var centroid = PolygonCentroid(clipped[0]);
                    if (centroid.HasValue)
                        newPts.Add(centroid.Value);
                    else
                        newPts.Add(pts2d[i]);
                }

                pts2d = newPts;
            }

            // Final Voronoi for output
            var finalCells = new List<Curve>();
            var nodeListFinal = new GH_Geo.Node2List();
            for (int i = 0; i < pts2d.Count; i++)
                nodeListFinal.Append(new GH_Geo.Node2(pts2d[i].X, pts2d[i].Y));

            var delFinal = GH_Geo.Delaunay.Solver.Solve_Connectivity(nodeListFinal, 0.001, false);
            if (delFinal != null)
            {
                var vorFinal = GH_Geo.Voronoi.Solver.Solve_Connectivity(
                    nodeListFinal, delFinal, boundingNodes);
                if (vorFinal != null)
                {
                    for (int i = 0; i < vorFinal.Count && i < pts2d.Count; i++)
                    {
                        var cellPoly = vorFinal[i].ToPolyline();
                        var cellCurve = cellPoly.ToPolylineCurve();
                        if (cellCurve == null) continue;

                        var clipped = Curve.CreateBooleanIntersection(cellCurve, boundary2d, 0.001);
                        if (clipped != null && clipped.Length > 0)
                        {
                            var c = clipped[0];
                            c.Transform(xformFromPlane);
                            finalCells.Add(c);
                        }
                    }
                }
            }

            // Transform points back to 3D
            var finalPts = new List<Point3d>(pts2d.Count);
            foreach (var pt in pts2d)
            {
                var p = new Point3d(pt);
                p.Transform(xformFromPlane);
                finalPts.Add(p);
            }

            return new SolveResults { Points = finalPts, Cells = finalCells };
        }

        private static Point3d? PolygonCentroid(Curve curve)
        {
            // Convert to polyline and compute area-weighted centroid
            if (!curve.TryGetPolyline(out Polyline poly))
            {
                poly = CurveToPolyline(curve);
                if (poly == null) return null;
            }

            if (poly.Count < 3) return null;

            // Shoelace centroid formula
            double area = 0;
            double cx = 0, cy = 0;

            for (int i = 0; i < poly.Count - 1; i++)
            {
                double x0 = poly[i].X, y0 = poly[i].Y;
                double x1 = poly[i + 1].X, y1 = poly[i + 1].Y;
                double cross = x0 * y1 - x1 * y0;
                area += cross;
                cx += (x0 + x1) * cross;
                cy += (y0 + y1) * cross;
            }

            area *= 0.5;
            if (Math.Abs(area) < 1e-20) return null;

            cx /= (6.0 * area);
            cy /= (6.0 * area);

            return new Point3d(cx, cy, 0);
        }

        private static Polyline CurveToPolyline(Curve curve)
        {
            var pts = new List<Point3d>();
            var parms = curve.DivideByCount(50, true);
            if (parms == null) return null;
            foreach (double t in parms)
                pts.Add(curve.PointAt(t));
            if (pts.Count > 2 && pts[0].DistanceTo(pts[pts.Count - 1]) > 0.001)
                pts.Add(pts[0]);
            return new Polyline(pts);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                var points = new List<Point3d>();
                Curve boundary = null;
                int iterations = 10;
                var plane = Plane.WorldXY;

                if (!DA.GetDataList(0, points)) return;
                if (!DA.GetData(1, ref boundary)) return;
                DA.GetData(2, ref iterations);
                DA.GetData(3, ref plane);

                if (points.Count < 2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "At least 2 points are required.");
                    return;
                }

                if (!boundary.IsClosed)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary curve must be closed.");
                    return;
                }

                if (iterations < 0) iterations = 0;

                var ptsCopy = new List<Point3d>(points);
                var bndCopy = boundary.DuplicateCurve();
                Plane pl = plane;
                int iters = iterations;

                Task<SolveResults> task = Task.Run(() => Compute(ptsCopy, bndCopy, iters, pl), CancelToken);
                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(DA, out SolveResults result))
                return;

            if (result != null)
            {
                DA.SetDataList(0, result.Points);
                DA.SetDataList(1, result.Cells);
            }
        }
    }
}
