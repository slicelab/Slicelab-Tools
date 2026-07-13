using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Slicelab.Geometry.Native;

namespace Slicelab.Geometry
{
    public class SurfaceRemeshComponent : GH_TaskCapableComponent<SurfaceRemeshComponent.SolveResults>
    {
        public SurfaceRemeshComponent()
            : base("Adaptive TriRemesh", "SLRem",
                "Adaptive surface remeshing with feature embedding. Accepts Mesh or Brep.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("40055F6E-719C-4462-BF13-F1EBB5BB12A7");
        protected override System.Drawing.Bitmap Icon => IconHelper.LoadIcon("SL-Rem.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Brep or Mesh to remesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Target Edge Length", "L", "Target edge length for uniform remeshing", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iterations", "It", "Number of remeshing passes (more = cleaner mesh)", GH_ParamAccess.item, 5);
            pManager.AddPointParameter("Attractor Points", "AP", "Points that attract finer resolution", GH_ParamAccess.list);
            pManager.AddCurveParameter("Attractor Curves", "AC", "Curves that attract finer resolution", GH_ParamAccess.list);
            pManager.AddCurveParameter("Feature Curves", "FC", "Curves to embed as mesh edges — the remeshed mesh will have edges following these curves", GH_ParamAccess.list);
            pManager.AddPointParameter("Feature Points", "FP", "Points to embed as required vertices — the remeshed mesh will have vertices at these locations", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Cleanup", "Cl", "Run an extra unconstrained pass after feature embedding to improve triangle quality (may slightly shift feature vertices)", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Min Edge Length", "Min", "Minimum edge length near attractors (default: Target * 0.3)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Edge Length", "Max", "Maximum edge length far from attractors (default: Target * 1.5)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Transition", "Tr", "How sharply edge length transitions between attractor zones and base. 0 = smooth gradual blend, 1 = sharp abrupt change.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Falloff Radius", "FR", "Distance from attractors over which edge length transitions from Min to Max (in model units). Default: half of geometry diagonal.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Curvature", "C", "Curvature preservation (0 = none, 1 = maximum). Controls how tightly the mesh follows surface curvature.", GH_ParamAccess.item, 0.5);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
            pManager[10].Optional = true;
            pManager[11].Optional = true;
            pManager[12].Optional = true;
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // Hide Dual output preview by default — only visible when connected downstream
            if (Params.Output.Count > 1 && Params.Output[1] is IGH_PreviewObject dualParam)
                dualParam.Hidden = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Remeshed surface", GH_ParamAccess.item);
            pManager.AddMeshParameter("Dual", "D", "Dual polygonal mesh (hexagons)", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Summary statistics", GH_ParamAccess.item);
        }

        public class SolveResults
        {
            public Mesh OutputMesh;
            public Mesh DualMesh;
            public string Info;
        }

        private static SolveResults Compute(Mesh inputMesh, double targetEdgeLength, int iterations,
            List<Point3d> attractorPts, List<Curve> attractorCrvs, List<Curve> featureCrvs,
            List<Point3d> featurePts, bool cleanup,
            double minEdge, double maxEdge, double hausd, double transition, double falloffRadius,
            bool inputWasClosed)
        {
            RemeshHelper.MeshToArrays(inputMesh, out double[] vertices, out int[] triangles);

            int inputVerts = vertices.Length / 3;
            int inputTris = triangles.Length / 3;

            bool hasAttractors = (attractorPts != null && attractorPts.Count > 0)
                              || (attractorCrvs != null && attractorCrvs.Count > 0);
            bool hasFeatureCrvs = featureCrvs != null && featureCrvs.Count > 0;
            bool hasFeaturePts = featurePts != null && featurePts.Count > 0;
            bool hasFeatures = hasFeatureCrvs || hasFeaturePts;

            if (hasAttractors)
            {
                // Adaptive mode: iterate in C# so we can recompute the sizing field
                // each pass (vertices move + new vertices get added).
                //
                // Pre-pass: uniform at targetEdgeLength to establish a balanced base mesh.
                // Using minEdge here would create an overly dense mesh that mmgs struggles
                // to coarsen in far-from-attractor regions. The adaptive passes handle refinement.
                //
                // Falloff Radius: distance (model units) over which sizing field transitions
                // from minEdge to maxEdge. Default: half of geometry bounding box diagonal.
                // Transition: controls hgrad (how abruptly mmgs allows size changes).
                // 0 = smooth (hgrad=1.1, 10% max change per edge),
                // 1 = sharp (hgrad=2.0, 100% change per edge).
                double falloff = falloffRadius;
                if (falloff <= 0)
                {
                    BoundingBox bbox = inputMesh.GetBoundingBox(false);
                    falloff = bbox.Diagonal.Length * 0.5;
                }
                double clampedT = Math.Max(0.0, Math.Min(1.0, transition));
                double adaptiveHgrad = 1.1 + clampedT * 0.9; // 0→1.1, 1→2.0
                RemeshResult prePass = MmgsInterop.RemeshUniform(vertices, triangles, targetEdgeLength, hausd);
                vertices = prePass.Vertices;
                triangles = prePass.Triangles;

                // Adaptive passes: unconstrained for all but the final pass
                for (int pass = 0; pass < iterations; pass++)
                {
                    bool isFinalPass = (pass == iterations - 1);
                    int numVerts = vertices.Length / 3;
                    double[] sizingField = RemeshHelper.ComputeSizingField(
                        vertices, numVerts, attractorPts, attractorCrvs,
                        minEdge, maxEdge, falloff);

                    if (isFinalPass && hasFeatures)
                    {
                        // Embed features into the current mesh for the final pass
                        Mesh currentMesh = RemeshHelper.ArraysToMesh(vertices, triangles);
                        var allReqEdges = new List<int>();
                        var allReqVerts = new List<int>();

                        if (hasFeatureCrvs)
                        {
                            EmbedResult embed = RemeshHelper.EmbedCurvesInMesh(currentMesh, featureCrvs, targetEdgeLength);
                            currentMesh = embed.ModifiedMesh;
                            allReqEdges.AddRange(embed.RequiredEdges);
                            allReqVerts.AddRange(embed.RequiredVertices);
                        }

                        if (hasFeaturePts)
                        {
                            int[] ptVerts = RemeshHelper.EmbedPointsInMesh(currentMesh, featurePts);
                            allReqVerts.AddRange(ptVerts);
                        }

                        RemeshHelper.MeshToArrays(currentMesh, out vertices, out triangles);

                        // Recompute sizing field on modified mesh
                        numVerts = vertices.Length / 3;
                        sizingField = RemeshHelper.ComputeSizingField(
                            vertices, numVerts, attractorPts, attractorCrvs,
                            minEdge, maxEdge, falloff);

                        RemeshResult adaptive = MmgsInterop.RemeshAdaptiveConstrained(
                            vertices, triangles, targetEdgeLength, sizingField, hausd,
                            allReqEdges.ToArray(), allReqVerts.ToArray());
                        vertices = adaptive.Vertices;
                        triangles = adaptive.Triangles;
                    }
                    else
                    {
                        RemeshResult adaptive = MmgsInterop.RemeshAdaptive(
                            vertices, triangles, targetEdgeLength, sizingField, hausd, adaptiveHgrad);
                        vertices = adaptive.Vertices;
                        triangles = adaptive.Triangles;
                    }
                }

                // Cleanup pass: unconstrained adaptive to improve triangle quality near features
                if (hasFeatures && cleanup)
                {
                    int numVerts = vertices.Length / 3;
                    double[] sizingField = RemeshHelper.ComputeSizingField(
                        vertices, numVerts, attractorPts, attractorCrvs,
                        minEdge, maxEdge, falloff);
                    RemeshResult cleanupResult = MmgsInterop.RemeshAdaptive(
                        vertices, triangles, targetEdgeLength, sizingField, hausd, adaptiveHgrad);
                    vertices = cleanupResult.Vertices;
                    triangles = cleanupResult.Triangles;
                }
            }
            else
            {
                // Uniform mode: simple multi-pass
                for (int pass = 0; pass < iterations; pass++)
                {
                    bool isFinalPass = (pass == iterations - 1);

                    if (isFinalPass && hasFeatures)
                    {
                        // Embed features into the current mesh for the final pass
                        Mesh currentMesh = RemeshHelper.ArraysToMesh(vertices, triangles);
                        var allReqEdges = new List<int>();
                        var allReqVerts = new List<int>();

                        if (hasFeatureCrvs)
                        {
                            EmbedResult embed = RemeshHelper.EmbedCurvesInMesh(currentMesh, featureCrvs, targetEdgeLength);
                            currentMesh = embed.ModifiedMesh;
                            allReqEdges.AddRange(embed.RequiredEdges);
                            allReqVerts.AddRange(embed.RequiredVertices);
                        }

                        if (hasFeaturePts)
                        {
                            int[] ptVerts = RemeshHelper.EmbedPointsInMesh(currentMesh, featurePts);
                            allReqVerts.AddRange(ptVerts);
                        }

                        RemeshHelper.MeshToArrays(currentMesh, out vertices, out triangles);

                        RemeshResult uniform = MmgsInterop.RemeshUniformConstrained(
                            vertices, triangles, targetEdgeLength, hausd,
                            allReqEdges.ToArray(), allReqVerts.ToArray());
                        vertices = uniform.Vertices;
                        triangles = uniform.Triangles;
                    }
                    else
                    {
                        RemeshResult uniform = MmgsInterop.RemeshUniform(vertices, triangles, targetEdgeLength, hausd);
                        vertices = uniform.Vertices;
                        triangles = uniform.Triangles;
                    }
                }

                // Cleanup pass: unconstrained uniform to improve triangle quality near features
                if (hasFeatures && cleanup)
                {
                    RemeshResult cleanupResult = MmgsInterop.RemeshUniform(vertices, triangles, targetEdgeLength, hausd);
                    vertices = cleanupResult.Vertices;
                    triangles = cleanupResult.Triangles;
                }
            }

            Mesh outputMesh = RemeshHelper.ArraysToMesh(vertices, triangles);

            // If input was closed but remeshing opened it, try to heal
            if (inputWasClosed && !outputMesh.IsClosed)
            {
                outputMesh.FillHoles();
                outputMesh.Vertices.CombineIdentical(true, true);
                outputMesh.Faces.CullDegenerateFaces();
                outputMesh.Compact();
                outputMesh.UnifyNormals();
            }

            Mesh dualMesh = DualMeshHelper.ComputeDual(outputMesh);

            int outVerts = outputMesh.Vertices.Count;
            int outTris = outputMesh.Faces.Count;

            // Compute edge length stats from output mesh
            double edgeMin = double.MaxValue;
            double edgeMax = double.MinValue;
            double edgeSum = 0;
            int edgeCount = outputMesh.TopologyEdges.Count;
            for (int ei = 0; ei < edgeCount; ei++)
            {
                double len = outputMesh.TopologyEdges.EdgeLine(ei).Length;
                if (len < edgeMin) edgeMin = len;
                if (len > edgeMax) edgeMax = len;
                edgeSum += len;
            }
            double edgeAvg = edgeCount > 0 ? edgeSum / edgeCount : 0;

            string info = string.Format(
                "Vertices: {0} -> {1}\nTriangles: {2} -> {3}\nEdges: {4}\nEdge Length: min {5:F2} / avg {6:F2} / max {7:F2}",
                inputVerts, outVerts,
                inputTris, outTris,
                edgeCount,
                edgeMin, edgeAvg, edgeMax);

            return new SolveResults { OutputMesh = outputMesh, DualMesh = dualMesh, Info = info };
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                GeometryBase geometry = null;
                double targetEdgeLength = 1.0;
                int iterations = 5;
                var attractorPts = new List<Point3d>();
                var attractorCrvs = new List<Curve>();
                var featureCrvs = new List<Curve>();
                var featurePts = new List<Point3d>();
                bool cleanup = false;
                double minEdge = 0;
                double maxEdge = 0;
                double transition = 0.0;
                double falloffRadius = 0;
                double curvature = 0.5;

                if (!DA.GetData(0, ref geometry)) return;
                DA.GetData(1, ref targetEdgeLength);
                if (targetEdgeLength <= 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Target Edge Length must be greater than zero.");
                    return;
                }
                DA.GetData(2, ref iterations);
                DA.GetDataList(3, attractorPts);
                DA.GetDataList(4, attractorCrvs);
                DA.GetDataList(5, featureCrvs);
                DA.GetDataList(6, featurePts);
                DA.GetData(7, ref cleanup);
                DA.GetData(8, ref minEdge);
                DA.GetData(9, ref maxEdge);
                DA.GetData(10, ref transition);
                DA.GetData(11, ref falloffRadius);
                DA.GetData(12, ref curvature);

                // Derive defaults from target edge length when not explicitly set
                if (minEdge <= 0) minEdge = targetEdgeLength * 0.3;
                if (maxEdge <= 0) maxEdge = targetEdgeLength * 1.5;

                // Clamp curvature to 0-1
                curvature = Math.Max(0.0, Math.Min(1.0, curvature));

                // Compute hausd from curvature coefficient using exponential mapping:
                // curvature=0 → hausd = targetEdge * 1.0 (balanced: respects surface without over-refining corners)
                // curvature=1 → hausd = targetEdge * 0.005 (very tight, maximum preservation)
                double hausd = targetEdgeLength * 1.0 * Math.Pow(0.005, curvature);

                if (iterations < 1) iterations = 1;

                if (geometry == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid geometry input.");
                    return;
                }

                Mesh inputMesh;
                if (geometry is Brep brep)
                {
                    inputMesh = RemeshHelper.BrepToMesh(brep, targetEdgeLength);
                    if (inputMesh == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to mesh Brep input.");
                        return;
                    }
                }
                else if (geometry is Mesh mesh)
                {
                    inputMesh = mesh.DuplicateMesh();
                    // Weld to ensure no unwelded seam vertices
                    inputMesh.Weld(Math.PI);
                    inputMesh.Vertices.CombineIdentical(true, true);
                    inputMesh.Faces.CullDegenerateFaces();
                    inputMesh.Compact();
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input must be a Mesh or Brep.");
                    return;
                }

                if (inputMesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input mesh has no faces.");
                    return;
                }

                bool inputWasClosed = inputMesh.IsClosed;

                var curvesCopy = new List<Curve>();
                for (int i = 0; i < attractorCrvs.Count; i++)
                    curvesCopy.Add(attractorCrvs[i].DuplicateCurve());

                var featureCopy = new List<Curve>();
                for (int i = 0; i < featureCrvs.Count; i++)
                    featureCopy.Add(featureCrvs[i].DuplicateCurve());

                Task<SolveResults> task = Task.Run(() => Compute(
                    inputMesh, targetEdgeLength, iterations, attractorPts, curvesCopy,
                    featureCopy, featurePts, cleanup, minEdge, maxEdge, hausd, transition, falloffRadius,
                    inputWasClosed), CancelToken);
                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(DA, out SolveResults result))
            {
                return;
            }

            if (result != null)
            {
                DA.SetData(0, result.OutputMesh);
                DA.SetData(1, result.DualMesh);
                DA.SetData(2, result.Info);
            }
        }
    }
}
