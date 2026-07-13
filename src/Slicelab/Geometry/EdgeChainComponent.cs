using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Geometry;

namespace Slicelab.Utilities
{
    public class EdgeChainComponent : GH_Component
    {
        public EdgeChainComponent()
            : base("Edge Chain", "SLChain",
                "Find chains of continuous edges on a Brep. Outputs only chains with 2+ edges. " +
                "Useful for selecting edge sequences for fillet/chamfer operations.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("C2D3E4F5-A6B7-4C8D-9E0F-1A2B3C4D5E6F");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-EdgeChain.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "B", "Input Brep to find edge chains on", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Continuity", "C",
                "Chain continuity: 0 = Position (any shared vertex), 1 = Tangency (smooth edges), 2 = Curvature (curvature-continuous edges)",
                GH_ParamAccess.item, 1);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Chain Curves", "C", "Connected curve chains with 2+ edges (one branch per chain)", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Chain Indices", "I", "Brep edge indices per chain (one branch per chain)", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Count", "N", "Number of chains found", GH_ParamAccess.item);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            Attributes.PerformLayout();

            if (Params.Input[1].SourceCount > 0) return;

            var grip = Params.Input[1].Attributes.InputGrip;
            var valueList = new GH_ValueList();
            valueList.CreateAttributes();
            valueList.ListMode = GH_ValueListMode.DropDown;
            valueList.NickName = "Continuity";

            valueList.ListItems.Clear();
            valueList.ListItems.Add(new GH_ValueListItem("Position", "0"));
            valueList.ListItems.Add(new GH_ValueListItem("Tangency", "1"));
            valueList.ListItems.Add(new GH_ValueListItem("Curvature", "2"));

            // Default to Tangency (index 1)
            valueList.SelectItem(1);

            valueList.Attributes.PerformLayout();
            IconHelper.AlignWidget(valueList, grip);

            document.AddObject(valueList, false);
            Params.Input[1].AddSource(valueList);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep brep = null;
            int continuity = 1;

            if (!DA.GetData(0, ref brep)) return;
            DA.GetData(1, ref continuity);

            if (brep == null || brep.Edges.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No edges on input Brep.");
                return;
            }

            continuity = Math.Max(0, Math.Min(2, continuity));

            double tol = RhinoDoc.ActiveDoc != null
                ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance
                : 0.001;
            double angleTol = RhinoDoc.ActiveDoc != null
                ? RhinoDoc.ActiveDoc.ModelAngleToleranceRadians
                : RhinoMath.ToRadians(1.0);

            // Extract all edges
            var edges = brep.Edges;
            int edgeCount = edges.Count;

            // Precompute endpoints
            var endpointsA = new Point3d[edgeCount];
            var endpointsB = new Point3d[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                endpointsA[i] = edges[i].PointAtStart;
                endpointsB[i] = edges[i].PointAtEnd;
            }

            // Find all connected chains
            var visited = new HashSet<int>();
            var curveTree = new DataTree<Curve>();
            var indexTree = new DataTree<int>();
            int chainCount = 0;

            for (int seed = 0; seed < edgeCount; seed++)
            {
                if (visited.Contains(seed)) continue;

                visited.Add(seed);

                var forwardChain = new List<int>();
                WalkChain(edges, endpointsA, endpointsB, visited, seed, false, tol, angleTol, continuity, forwardChain);

                var backwardChain = new List<int>();
                WalkChain(edges, endpointsA, endpointsB, visited, seed, true, tol, angleTol, continuity, backwardChain);

                int totalEdges = backwardChain.Count + 1 + forwardChain.Count;

                // Only output chains with 2+ edges
                if (totalEdges < 2)
                    continue;

                var path = new GH_Path(chainCount);
                backwardChain.Reverse();
                foreach (int pos in backwardChain)
                {
                    curveTree.Add(edges[pos].DuplicateCurve(), path);
                    indexTree.Add(pos, path);
                }
                curveTree.Add(edges[seed].DuplicateCurve(), path);
                indexTree.Add(seed, path);
                foreach (int pos in forwardChain)
                {
                    curveTree.Add(edges[pos].DuplicateCurve(), path);
                    indexTree.Add(pos, path);
                }

                chainCount++;
            }

            DA.SetDataTree(0, curveTree);
            DA.SetDataTree(1, indexTree);
            DA.SetData(2, chainCount);
        }

        private static void WalkChain(Rhino.Geometry.Collections.BrepEdgeList edges,
            Point3d[] endpointsA, Point3d[] endpointsB,
            HashSet<int> visited, int currentPos, bool walkFromStart,
            double tol, double angleTol, int continuity, List<int> chain)
        {
            Point3d tip = walkFromStart
                ? edges[currentPos].PointAtStart
                : edges[currentPos].PointAtEnd;

            while (true)
            {
                int bestIdx = -1;
                double bestAngle = double.MaxValue;
                bool bestConnectsAtStart = false;

                for (int i = 0; i < edges.Count; i++)
                {
                    if (visited.Contains(i)) continue;

                    bool connectsAtStart = tip.DistanceTo(endpointsA[i]) < tol;
                    bool connectsAtEnd = tip.DistanceTo(endpointsB[i]) < tol;

                    if (!connectsAtStart && !connectsAtEnd) continue;

                    // For Position continuity, just pick the first connected edge
                    if (continuity == 0)
                    {
                        bestIdx = i;
                        bestConnectsAtStart = connectsAtStart;
                        break;
                    }

                    // Compute tangent angle for Tangency and Curvature modes
                    Vector3d currentTangent = walkFromStart
                        ? -edges[currentPos].TangentAtStart
                        : edges[currentPos].TangentAtEnd;

                    Vector3d nextTangent = connectsAtStart
                        ? edges[i].TangentAtStart
                        : -edges[i].TangentAtEnd;

                    double angle = Vector3d.VectorAngle(currentTangent, nextTangent);

                    // Reject if tangent angle exceeds tolerance
                    if (angle > angleTol) continue;

                    // Curvature continuity: also check curvature magnitude match
                    if (continuity == 2)
                    {
                        double curCurvature = walkFromStart
                            ? edges[currentPos].CurvatureAt(edges[currentPos].Domain.Min).Length
                            : edges[currentPos].CurvatureAt(edges[currentPos].Domain.Max).Length;

                        double nextCurvature = connectsAtStart
                            ? edges[i].CurvatureAt(edges[i].Domain.Min).Length
                            : edges[i].CurvatureAt(edges[i].Domain.Max).Length;

                        // Curvature must match within 10% (or both near zero)
                        double maxK = Math.Max(curCurvature, nextCurvature);
                        if (maxK > 1e-10)
                        {
                            double curvatureDiff = Math.Abs(curCurvature - nextCurvature) / maxK;
                            if (curvatureDiff > 0.1) continue;
                        }
                    }

                    if (angle < bestAngle)
                    {
                        bestAngle = angle;
                        bestIdx = i;
                        bestConnectsAtStart = connectsAtStart;
                    }
                }

                if (bestIdx < 0) break;

                visited.Add(bestIdx);
                chain.Add(bestIdx);

                tip = bestConnectsAtStart ? endpointsB[bestIdx] : endpointsA[bestIdx];
                currentPos = bestIdx;
                walkFromStart = !bestConnectsAtStart;
            }
        }
    }
}
