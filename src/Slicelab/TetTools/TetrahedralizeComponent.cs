using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Slicelab.TetTools.Native;

namespace Slicelab.TetTools
{
    public class TetrahedralizeComponent : GH_TaskCapableComponent<TetrahedralizeComponent.SolveResults>, ITetButtonComponent
    {
        public bool ShouldRun { get; set; }

        private SolveResults _cachedResults;

        public TetrahedralizeComponent()
            : base("Tetrahedralize", "SLTet",
                "Tetrahedralize a mesh. Click Run to process. Uses TetGen library.",
                "Slicelab Tools", "Tet Tools")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("B7C966D2-3F80-4FC9-AF25-3A5A15DAE36B");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Tet.png");

        public override void CreateAttributes()
        {
            m_attributes = new TetButtonAttributes(this);
        }

        public override void ExpireSolution(bool recompute)
        {
            if (!ShouldRun && _cachedResults != null)
            {
                Message = "Inputs changed — click Run";
                OnDisplayExpired(false);
                return;
            }
            base.ExpireSolution(recompute);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Input mesh to tetrahedralize", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Ratio", "R",
                "Quality ratio — higher values allow more elongated tets, lower values force more uniform shapes. Clamped to minimum 1.0.",
                GH_ParamAccess.item, 1.5);
            pManager.AddNumberParameter("Target Length", "L",
                "Sets max tet volume to L³/6. This is a volume ceiling, not an exact edge target. " +
                "Strict Min Ratio values cause extra subdivision, making tets smaller than L. " +
                "Compensate by increasing L: at ratio 2.0 use L = TriRemesh edge, at 1.5 use ~1.5x, at 1.1 use ~2x. " +
                "Leave disconnected for no volume constraint.",
                GH_ParamAccess.item, -1.0);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddParameter(new TetDataParam(), "Tet Data", "TD", "Combined points and tetra indices", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Result vertices", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Tetra Indices", "TI", "Tetrahedron vertex indices (one branch per tet)", GH_ParamAccess.tree);
            pManager.AddMeshParameter("Tetra Meshes", "TM", "Individual tetrahedron meshes", GH_ParamAccess.list);
            pManager.AddPointParameter("Centroids", "C", "Centroid of each tetrahedron", GH_ParamAccess.list);
            pManager.AddLineParameter("Inner Edges", "IE", "Interior edges (not on boundary surface)", GH_ParamAccess.list);
            pManager.AddLineParameter("Surface Edges", "SE", "Boundary edges (on the outer surface)", GH_ParamAccess.list);
            pManager.AddMeshParameter("Surface Mesh", "SM", "Reconstructed surface mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Summary statistics", GH_ParamAccess.item);
        }

        public class SolveResults
        {
            public List<Point3d> Points;
            public DataTree<int> TetraIndices;
            public List<Mesh> TetraMeshes;
            public List<Point3d> Centroids;
            public List<Line> InnerEdges;
            public List<Line> SurfaceEdges;
            public Mesh SurfaceMesh;
            public string Info;
        }

        private static SolveResults Compute(Mesh inputMesh, double minRatio, double maxVolume)
        {
            TetgenHelper.MeshToArrays(inputMesh, out double[] vertices, out int[] faceIndices, out int[] faceSizes);

            double tetgenMaxVol = maxVolume > 0 ? maxVolume : 0;

            TetgenResult result = TetgenInterop.Tetrahedralize(
                vertices, faceIndices, faceSizes,
                minRatio, tetgenMaxVol,
                0, 0);

            List<Point3d> points = TetgenHelper.ResultToPoints(result);
            DataTree<int> tetraTree = TetgenHelper.ResultToTetraTree(result);
            List<Mesh> tetraMeshes = TetgenHelper.ResultToTetraMeshes(result, points);
            List<Point3d> centroids = TetgenHelper.ResultToCentroids(result, points);
            var (innerEdges, surfaceEdges) = TetgenHelper.ResultToSplitEdges(result, points);
            Mesh surfaceMesh = TetgenHelper.ResultToSurfaceMesh(result);
            string info = TetgenHelper.ResultToInfo(result);

            // Compute radius-edge ratio stats per tetrahedron
            int tetCount = result.TetraIndices.Length / 4;
            double ratioMin = double.MaxValue;
            double ratioMax = double.MinValue;
            double ratioSum = 0;

            for (int i = 0; i < tetCount; i++)
            {
                Point3d p0 = points[result.TetraIndices[i * 4]];
                Point3d p1 = points[result.TetraIndices[i * 4 + 1]];
                Point3d p2 = points[result.TetraIndices[i * 4 + 2]];
                Point3d p3 = points[result.TetraIndices[i * 4 + 3]];

                double ratio = ComputeRadiusEdgeRatio(p0, p1, p2, p3);
                if (ratio < ratioMin) ratioMin = ratio;
                if (ratio > ratioMax) ratioMax = ratio;
                ratioSum += ratio;
            }

            double ratioAvg = tetCount > 0 ? ratioSum / tetCount : 0;
            info += $"\nEdges: {innerEdges.Count} inner, {surfaceEdges.Count} surface";
            info += $"\nRadius-edge ratio: min {ratioMin:F3}, max {ratioMax:F3}, avg {ratioAvg:F3}";

            return new SolveResults
            {
                Points = points,
                TetraIndices = tetraTree,
                TetraMeshes = tetraMeshes,
                Centroids = centroids,
                InnerEdges = innerEdges,
                SurfaceEdges = surfaceEdges,
                SurfaceMesh = surfaceMesh,
                Info = info
            };
        }

        /// <summary>
        /// Computes the radius-edge ratio of a tetrahedron: circumsphere radius / shortest edge.
        /// A regular tet has ratio ≈ 0.612. Higher = more elongated/degenerate.
        /// </summary>
        private static double ComputeRadiusEdgeRatio(Point3d a, Point3d b, Point3d c, Point3d d)
        {
            // 6 edge lengths
            double e0 = a.DistanceTo(b);
            double e1 = a.DistanceTo(c);
            double e2 = a.DistanceTo(d);
            double e3 = b.DistanceTo(c);
            double e4 = b.DistanceTo(d);
            double e5 = c.DistanceTo(d);

            double shortestEdge = Math.Min(Math.Min(Math.Min(e0, e1), Math.Min(e2, e3)), Math.Min(e4, e5));
            if (shortestEdge < 1e-12) return double.MaxValue;

            // Circumsphere radius via determinant method
            // Set up vectors relative to vertex a
            double ax = b.X - a.X, ay = b.Y - a.Y, az = b.Z - a.Z;
            double bx = c.X - a.X, by = c.Y - a.Y, bz = c.Z - a.Z;
            double cx = d.X - a.X, cy = d.Y - a.Y, cz = d.Z - a.Z;

            double aa = ax * ax + ay * ay + az * az;
            double bb = bx * bx + by * by + bz * bz;
            double cc = cx * cx + cy * cy + cz * cz;

            // Determinant of the 3x3 matrix [a; b; c]
            double det = ax * (by * cz - bz * cy)
                       - ay * (bx * cz - bz * cx)
                       + az * (bx * cy - by * cx);

            if (Math.Abs(det) < 1e-12) return double.MaxValue; // degenerate tet

            double ox = (aa * (by * cz - bz * cy) - bb * (ay * cz - az * cy) + cc * (ay * bz - az * by)) / (2.0 * det);
            double oy = -(aa * (bx * cz - bz * cx) - bb * (ax * cz - az * cx) + cc * (ax * bz - az * bx)) / (2.0 * det);
            double oz = (aa * (bx * cy - by * cx) - bb * (ax * cy - ay * cx) + cc * (ax * by - ay * bx)) / (2.0 * det);

            double circumRadius = Math.Sqrt(ox * ox + oy * oy + oz * oz);

            return circumRadius / shortestEdge;
        }

        private void OutputResults(IGH_DataAccess DA, SolveResults results)
        {
            DA.SetData(0, new GH_TetData(TetData.FromTree(results.Points, results.TetraIndices)));
            DA.SetDataList(1, results.Points);
            DA.SetDataTree(2, results.TetraIndices);
            DA.SetDataList(3, results.TetraMeshes);
            DA.SetDataList(4, results.Centroids);
            DA.SetDataList(5, results.InnerEdges);
            DA.SetDataList(6, results.SurfaceEdges);
            DA.SetData(7, results.SurfaceMesh);
            DA.SetData(8, results.Info);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                if (!ShouldRun)
                {
                    if (_cachedResults == null)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Click Run to process.");
                    return;
                }

                ShouldRun = false;
                Message = null;

                Mesh mesh = null;
                double minRatio = 1.5;
                double targetLength = -1.0;

                if (!DA.GetData(0, ref mesh)) return;
                DA.GetData(1, ref minRatio);
                DA.GetData(2, ref targetLength);

                if (mesh == null || mesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid mesh input.");
                    return;
                }

                // Hard clamp — values below 1.0 cause TetGen infinite loops
                if (minRatio < 1.0)
                {
                    minRatio = 1.0;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Min Ratio clamped to 1.0 — values below 1.0 cause the solver to freeze.");
                    Message = "Ratio clamped to 1.0";
                }
                else if (minRatio <= 1.1)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Min Ratio {minRatio:F2} is very strict — solver may be slow or freeze on complex meshes.");
                    Message = $"Ratio {minRatio:F2} — may be slow";
                }

                // Volume constraint from Target Length
                double effectiveMaxVolume = -1.0;
                if (targetLength > 0)
                {
                    effectiveMaxVolume = (targetLength * targetLength * targetLength) / 6.0;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Target Length {targetLength:F1} → max volume {effectiveMaxVolume:F1}");
                }

                Mesh meshCopy = mesh.DuplicateMesh();

                Task<SolveResults> task = Task.Run(() => Compute(meshCopy, minRatio, effectiveMaxVolume), CancelToken);
                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(DA, out SolveResults result))
                return;

            if (result != null)
            {
                _cachedResults = result;
                OutputResults(DA, result);
            }
        }
    }
}
