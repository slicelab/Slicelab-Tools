using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Slicelab.Utilities
{
    public class MinBoundingBoxComponent : GH_TaskCapableComponent<MinBoundingBoxComponent.SolveResults>
    {
        public MinBoundingBoxComponent()
            : base("Minimum Bounding Box", "SLMinBox",
                "Find the smallest-volume oriented bounding box for geometry.",
                "Slicelab Tools", "Geometry")
        { }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        public override Guid ComponentGuid => new Guid("D3E4F5A6-B7C8-4D9E-0F1A-2B3C4D5E6F70");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-MinBox.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Mesh, Brep, Curve, or Points", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Union", "U", "True: one box for all geometry. False: one box per item.", GH_ParamAccess.item, true);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Box", "B", "The minimum oriented bounding box", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane XY", "Pxy", "XY plane at box center", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane YZ", "Pyz", "YZ plane at box center", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Plane XZ", "Pxz", "XZ plane at box center", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume", "V", "Box volume", GH_ParamAccess.list);
            pManager.AddNumberParameter("X", "X", "Box X dimension", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y", "Y", "Box Y dimension", GH_ParamAccess.list);
            pManager.AddNumberParameter("Z", "Z", "Box Z dimension", GH_ParamAccess.list);
            pManager.AddPointParameter("Center", "C", "Box center point", GH_ParamAccess.list);
        }

        public class BoxResult
        {
            public Box Box;
            public Plane PlaneXY;
            public Plane PlaneYZ;
            public Plane PlaneXZ;
            public double Volume;
            public double DimX;
            public double DimY;
            public double DimZ;
            public Point3d Center;
        }

        public class SolveResults
        {
            public List<BoxResult> Results;
            public bool Coplanar;
        }

        /// <summary>
        /// Extract points from geometry for PCA orientation estimation.
        /// For Breps, meshes the surface to capture curvature in the point cloud.
        /// </summary>
        private static List<Point3d> ExtractPoints(GeometryBase geo)
        {
            var pts = new List<Point3d>();
            if (geo == null) return pts;

            if (geo is Mesh mesh)
            {
                foreach (var v in mesh.Vertices)
                    pts.Add(new Point3d(v));
            }
            else if (geo is Brep brep)
            {
                // Mesh the Brep to capture surface curvature for PCA
                var meshParams = MeshingParameters.Default;
                var meshes = Mesh.CreateFromBrep(brep, meshParams);
                if (meshes != null)
                {
                    foreach (var m in meshes)
                        foreach (var v in m.Vertices)
                            pts.Add(new Point3d(v));
                }
                else
                {
                    // Fallback to vertices + edge samples
                    foreach (var v in brep.Vertices)
                        pts.Add(v.Location);
                    foreach (var edge in brep.Edges)
                    {
                        var crv = edge.ToNurbsCurve();
                        if (crv == null) continue;
                        var parms = crv.DivideByCount(10, false);
                        if (parms != null)
                            foreach (double t in parms)
                                pts.Add(crv.PointAt(t));
                    }
                }
            }
            else if (geo is Curve curve)
            {
                int count = Math.Max(20, (int)(curve.GetLength() * 2));
                count = Math.Min(count, 200);
                var parms = curve.DivideByCount(count, true);
                if (parms != null)
                    foreach (double t in parms)
                        pts.Add(curve.PointAt(t));
            }
            else if (geo is Rhino.Geometry.Point point)
            {
                pts.Add(point.Location);
            }
            else if (geo is Surface srf)
            {
                var brepFromSrf = srf.ToBrep();
                if (brepFromSrf != null)
                    pts.AddRange(ExtractPoints(brepFromSrf));
            }

            return pts;
        }

        // Jacobi eigenvalue algorithm for 3x3 symmetric matrix
        // Returns eigenvalues in descending order and corresponding eigenvectors
        private static void JacobiEigen(double[,] a, out double[] eigenvalues, out double[,] eigenvectors)
        {
            int n = 3;
            var v = new double[n, n];
            for (int i = 0; i < n; i++) v[i, i] = 1.0;

            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = a[i, i];

            for (int iter = 0; iter < 50; iter++)
            {
                // Check convergence
                double offDiag = 0;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        offDiag += a[i, j] * a[i, j];
                if (offDiag < 1e-20) break;

                for (int p = 0; p < n; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-15) continue;

                        double theta = 0.5 * (d[q] - d[p]) / a[p, q];
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        double c = 1.0 / Math.Sqrt(t * t + 1);
                        double s = t * c;
                        double tau = s / (1.0 + c);

                        double apq = a[p, q];
                        a[p, q] = 0;
                        d[p] -= t * apq;
                        d[q] += t * apq;

                        for (int r = 0; r < n; r++)
                        {
                            if (r == p || r == q) continue;
                            double arp = a[Math.Min(r, p), Math.Max(r, p)];
                            double arq = a[Math.Min(r, q), Math.Max(r, q)];
                            a[Math.Min(r, p), Math.Max(r, p)] = arp - s * (arq + tau * arp);
                            a[Math.Min(r, q), Math.Max(r, q)] = arq + s * (arp - tau * arq);
                        }

                        for (int r = 0; r < n; r++)
                        {
                            double vrp = v[r, p];
                            double vrq = v[r, q];
                            v[r, p] = vrp - s * (vrq + tau * vrp);
                            v[r, q] = vrq + s * (vrp - tau * vrq);
                        }
                    }
                }
            }

            // Sort by descending eigenvalue
            var order = new int[] { 0, 1, 2 };
            Array.Sort(order, (i, j) => d[j].CompareTo(d[i]));

            eigenvalues = new double[] { d[order[0]], d[order[1]], d[order[2]] };
            eigenvectors = new double[3, 3];
            for (int col = 0; col < 3; col++)
                for (int row = 0; row < 3; row++)
                    eigenvectors[row, col] = v[row, order[col]];
        }

        /// <summary>
        /// Compute exact oriented bounding box using geometry bounds (not point sampling).
        /// </summary>
        private static Box ComputeBoxFromPlane(List<GeometryBase> geometries, Plane plane)
        {
            var xform = Transform.ChangeBasis(Plane.WorldXY, plane);

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (var geo in geometries)
            {
                var bbox = geo.GetBoundingBox(xform);
                if (!bbox.IsValid) continue;

                if (bbox.Min.X < minX) minX = bbox.Min.X;
                if (bbox.Min.Y < minY) minY = bbox.Min.Y;
                if (bbox.Min.Z < minZ) minZ = bbox.Min.Z;
                if (bbox.Max.X > maxX) maxX = bbox.Max.X;
                if (bbox.Max.Y > maxY) maxY = bbox.Max.Y;
                if (bbox.Max.Z > maxZ) maxZ = bbox.Max.Z;
            }

            if (minX > maxX)
                return Box.Empty;

            return new Box(plane,
                new Interval(minX, maxX),
                new Interval(minY, maxY),
                new Interval(minZ, maxZ));
        }

        private static BoxResult ComputeSingle(List<GeometryBase> geometries, List<Point3d> points)
        {
            if (points.Count < 2)
            {
                var box = ComputeBoxFromPlane(geometries, Plane.WorldXY);
                return MakeResult(box);
            }

            // Compute centroid
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < points.Count; i++)
            {
                cx += points[i].X; cy += points[i].Y; cz += points[i].Z;
            }
            cx /= points.Count; cy /= points.Count; cz /= points.Count;

            // Build covariance matrix
            var cov = new double[3, 3];
            for (int i = 0; i < points.Count; i++)
            {
                double dx = points[i].X - cx;
                double dy = points[i].Y - cy;
                double dz = points[i].Z - cz;
                cov[0, 0] += dx * dx; cov[0, 1] += dx * dy; cov[0, 2] += dx * dz;
                cov[1, 1] += dy * dy; cov[1, 2] += dy * dz;
                cov[2, 2] += dz * dz;
            }
            cov[1, 0] = cov[0, 1]; cov[2, 0] = cov[0, 2]; cov[2, 1] = cov[1, 2];

            // PCA via Jacobi
            JacobiEigen(cov, out _, out double[,] eigvecs);

            var xAxis = new Vector3d(eigvecs[0, 0], eigvecs[1, 0], eigvecs[2, 0]);
            var yAxis = new Vector3d(eigvecs[0, 1], eigvecs[1, 1], eigvecs[2, 1]);
            var zAxis = Vector3d.CrossProduct(xAxis, yAxis);
            xAxis.Unitize(); yAxis.Unitize(); zAxis.Unitize();

            var centroid = new Point3d(cx, cy, cz);

            // Candidate orientations
            var candidates = new List<(Box box, double vol)>();

            // PCA plane
            var pcaPlane = new Plane(centroid, xAxis, yAxis);
            var pcaBox = ComputeBoxFromPlane(geometries, pcaPlane);
            candidates.Add((pcaBox, BoxVolume(pcaBox)));

            // World-aligned planes
            foreach (var wp in new[] { Plane.WorldXY, Plane.WorldYZ, Plane.WorldZX })
            {
                var b = ComputeBoxFromPlane(geometries, wp);
                candidates.Add((b, BoxVolume(b)));
            }

            // Convex hull face normals as candidate orientations
            // The optimal OBB must have at least one face flush with a convex hull facet.
            if (points.Count >= 4)
            {
                var hull = Mesh.CreateConvexHull3D(points, out _, 0.001, 0.001);
                if (hull != null)
                {
                    hull.FaceNormals.ComputeFaceNormals();
                    var testedNormals = new List<Vector3d>();

                    for (int fi = 0; fi < hull.Faces.Count; fi++)
                    {
                        var normal = hull.FaceNormals[fi];
                        if (!normal.IsValid || normal.IsZero) continue;
                        normal.Unitize();

                        // Skip near-duplicate normals
                        bool duplicate = false;
                        foreach (var existing in testedNormals)
                        {
                            if (Math.Abs(normal * existing) > 0.998)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                        if (duplicate) continue;
                        testedNormals.Add(normal);

                        // For each hull face normal, find optimal rotation around it
                        // via 2D PCA of points projected onto the perpendicular plane
                        var projPlane = new Plane(centroid, normal);
                        double cov2d00 = 0, cov2d01 = 0, cov2d11 = 0;
                        foreach (var pt in points)
                        {
                            var v = pt - centroid;
                            double u1 = v * projPlane.XAxis;
                            double u2 = v * projPlane.YAxis;
                            cov2d00 += u1 * u1;
                            cov2d01 += u1 * u2;
                            cov2d11 += u2 * u2;
                        }

                        // Analytic eigenvector of 2x2 symmetric matrix
                        double angle2d = 0.5 * Math.Atan2(2.0 * cov2d01, cov2d00 - cov2d11);
                        double cosA = Math.Cos(angle2d);
                        double sinA = Math.Sin(angle2d);

                        var optX = projPlane.XAxis * cosA + projPlane.YAxis * sinA;
                        var optY = projPlane.XAxis * (-sinA) + projPlane.YAxis * cosA;
                        optX.Unitize();
                        optY.Unitize();

                        var candidatePlane = new Plane(centroid, optX, optY);
                        var candidateBox = ComputeBoxFromPlane(geometries, candidatePlane);
                        candidates.Add((candidateBox, BoxVolume(candidateBox)));
                    }
                }
            }

            // Pick minimum volume
            double bestVol = double.MaxValue;
            Box bestBox = candidates[0].box;

            foreach (var (box, vol) in candidates)
            {
                if (vol < bestVol)
                {
                    bestVol = vol;
                    bestBox = box;
                }
            }

            // Gradient descent refinement — always enabled
            double[] angleSteps = {
                5.0 * Math.PI / 180,
                2.0 * Math.PI / 180,
                1.0 * Math.PI / 180,
                0.5 * Math.PI / 180,
                0.2 * Math.PI / 180
            };

            foreach (double angleStep in angleSteps)
            {
                bool improved = true;
                int maxIter = 30;
                while (improved && maxIter-- > 0)
                {
                    improved = false;

                    var bestPlane = bestBox.Plane;
                    Vector3d[] axes = { bestPlane.XAxis, bestPlane.YAxis, bestPlane.ZAxis };

                    foreach (var rotAxis in axes)
                    {
                        foreach (double dir in new[] { -1.0, 1.0 })
                        {
                            double angle = dir * angleStep;
                            var rotation = Transform.Rotation(angle, rotAxis, bestPlane.Origin);
                            var testPlane = new Plane(bestPlane);
                            testPlane.Transform(rotation);

                            var testBox = ComputeBoxFromPlane(geometries, testPlane);
                            double testVol = BoxVolume(testBox);

                            if (testVol < bestVol)
                            {
                                bestVol = testVol;
                                bestBox = testBox;
                                improved = true;
                            }
                        }
                    }
                }
            }

            return MakeResult(bestBox);
        }

        /// <summary>
        /// Check if points are coplanar (or nearly so) by testing if PCA third eigenvalue is near zero.
        /// </summary>
        private static bool IsCoplanar(List<Point3d> points)
        {
            if (points.Count < 4) return true;

            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < points.Count; i++)
            {
                cx += points[i].X; cy += points[i].Y; cz += points[i].Z;
            }
            cx /= points.Count; cy /= points.Count; cz /= points.Count;

            var cov = new double[3, 3];
            for (int i = 0; i < points.Count; i++)
            {
                double dx = points[i].X - cx;
                double dy = points[i].Y - cy;
                double dz = points[i].Z - cz;
                cov[0, 0] += dx * dx; cov[0, 1] += dx * dy; cov[0, 2] += dx * dz;
                cov[1, 1] += dy * dy; cov[1, 2] += dy * dz;
                cov[2, 2] += dz * dz;
            }
            cov[1, 0] = cov[0, 1]; cov[2, 0] = cov[0, 2]; cov[2, 1] = cov[1, 2];

            JacobiEigen(cov, out double[] eigenvalues, out _);

            // If smallest eigenvalue is negligible compared to largest, points are coplanar
            double maxEig = Math.Max(Math.Abs(eigenvalues[0]), 1e-15);
            return Math.Abs(eigenvalues[2]) / maxEig < 1e-6;
        }

        private static double BoxVolume(Box box)
        {
            if (!box.IsValid) return double.MaxValue;
            return box.X.Length * box.Y.Length * box.Z.Length;
        }

        private static BoxResult MakeResult(Box box)
        {
            var plane = box.Plane;
            var center = box.Center;

            // Rebase plane to box center for output planes
            var centerPlane = new Plane(center, plane.XAxis, plane.YAxis);

            return new BoxResult
            {
                Box = box,
                PlaneXY = new Plane(center, centerPlane.XAxis, centerPlane.YAxis),
                PlaneYZ = new Plane(center, centerPlane.YAxis, centerPlane.ZAxis),
                PlaneXZ = new Plane(center, centerPlane.XAxis, centerPlane.ZAxis),
                Volume = Math.Max(0, box.X.Length * box.Y.Length * box.Z.Length),
                DimX = box.X.Length,
                DimY = box.Y.Length,
                DimZ = box.Z.Length,
                Center = center
            };
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                var gooList = new List<IGH_Goo>();
                bool union = true;

                if (!DA.GetDataList(0, gooList)) return;
                DA.GetData(1, ref union);

                if (gooList.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid geometry found.");
                    return;
                }

                // Convert goo to GeometryBase, handling Point3d → Rhino.Geometry.Point
                var geoCopies = new List<GeometryBase>();
                foreach (var goo in gooList)
                {
                    if (goo == null) continue;

                    if (goo is GH_Point ghPt)
                    {
                        geoCopies.Add(new Rhino.Geometry.Point(ghPt.Value));
                    }
                    else if (goo.CastTo(out GeometryBase geo) && geo != null)
                    {
                        geoCopies.Add(geo.Duplicate());
                    }
                }

                if (geoCopies.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid geometry found.");
                    return;
                }

                // Check if all geometry is just points (need 4+ non-coplanar for 3D box)
                bool allPoints2 = true;
                foreach (var geo in geoCopies)
                {
                    if (!(geo is Rhino.Geometry.Point))
                    {
                        allPoints2 = false;
                        break;
                    }
                }
                if (allPoints2 && geoCopies.Count < 4)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need at least 4 non-coplanar points for a 3D bounding box.");
                    return;
                }

                bool uni = union;

                Task<SolveResults> task = Task.Run(() =>
                {
                    var results = new SolveResults { Results = new List<BoxResult>() };

                    if (uni)
                    {
                        // Union: one box for all geometry combined
                        var allPoints = new List<Point3d>();
                        foreach (var geo in geoCopies)
                            allPoints.AddRange(ExtractPoints(geo));

                        if (allPoints.Count == 0) return results;

                        // Check coplanarity — warn if points don't span 3D
                        if (IsCoplanar(allPoints))
                        {
                            results.Coplanar = true;
                            return results;
                        }

                        results.Results.Add(ComputeSingle(geoCopies, allPoints));
                    }
                    else
                    {
                        // Per-item: one box per geometry (skip individual points — no volume)
                        foreach (var geo in geoCopies)
                        {
                            if (geo is Rhino.Geometry.Point) continue;
                            var pts = ExtractPoints(geo);
                            if (pts.Count == 0) continue;
                            var singleList = new List<GeometryBase> { geo };
                            results.Results.Add(ComputeSingle(singleList, pts));
                        }
                    }

                    return results;
                }, CancelToken);

                TaskList.Add(task);
                return;
            }

            if (!GetSolveResults(DA, out SolveResults result))
                return;

            if (result != null && result.Coplanar)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Points are coplanar — minimum bounding box requires 3D geometry.");
                return;
            }

            if (result != null && result.Results != null && result.Results.Count > 0)
            {
                var boxes = new List<Box>();
                var planesXY = new List<Plane>();
                var planesYZ = new List<Plane>();
                var planesXZ = new List<Plane>();
                var volumes = new List<double>();
                var xDims = new List<double>();
                var yDims = new List<double>();
                var zDims = new List<double>();
                var centers = new List<Point3d>();

                foreach (var r in result.Results)
                {
                    boxes.Add(r.Box);
                    planesXY.Add(r.PlaneXY);
                    planesYZ.Add(r.PlaneYZ);
                    planesXZ.Add(r.PlaneXZ);
                    volumes.Add(r.Volume);
                    xDims.Add(r.DimX);
                    yDims.Add(r.DimY);
                    zDims.Add(r.DimZ);
                    centers.Add(r.Center);
                }

                DA.SetDataList(0, boxes);
                DA.SetDataList(1, planesXY);
                DA.SetDataList(2, planesYZ);
                DA.SetDataList(3, planesXZ);
                DA.SetDataList(4, volumes);
                DA.SetDataList(5, xDims);
                DA.SetDataList(6, yDims);
                DA.SetDataList(7, zDims);
                DA.SetDataList(8, centers);
            }
        }
    }
}
