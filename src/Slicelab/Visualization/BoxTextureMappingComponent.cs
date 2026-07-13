using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.Visualization
{
    public class BoxTextureMappingComponent : GH_Component
    {
        public BoxTextureMappingComponent()
            : base("Box Texture Mapping", "SLBoxMap",
                "Sample a texture image at each mesh vertex using box-projected UV coordinates.",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("B1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D11");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-BoxMap.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to sample", GH_ParamAccess.item);
            pManager.AddTextParameter("Texture Path", "T", "File path to the texture image", GH_ParamAccess.item);
            pManager.AddBoxParameter("Box", "B", "Reference box for UV projection", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Tile", "Ti", "Tile the texture when vertices are outside the box (default: clamp to edges)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Capped", "Ca", "Use all 6 box faces (true) or 4 sides only (false)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Output Mesh", "OM", "Output mesh with vertex colors applied", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh with vertex colors applied", GH_ParamAccess.item);
            pManager.AddColourParameter("Colors", "C", "Vertex colors sampled from texture", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Summary info", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = null;
            string texturePath = "";
            Box box = Box.Unset;
            bool tile = false;
            bool capped = true;
            bool outputMesh = true;

            if (!DA.GetData(0, ref mesh)) return;
            if (!DA.GetData(1, ref texturePath)) return;
            if (!DA.GetData(2, ref box)) return;
            DA.GetData(3, ref tile);
            DA.GetData(4, ref capped);
            DA.GetData(5, ref outputMesh);

            if (mesh == null || mesh.Vertices.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is empty or null.");
                return;
            }

            texturePath = texturePath.Trim();
            if (!File.Exists(texturePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {texturePath}");
                return;
            }

            if (!box.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Box is invalid.");
                return;
            }

            double boxXLen = box.X.Length;
            double boxYLen = box.Y.Length;
            double boxZLen = box.Z.Length;

            if (boxXLen == 0 || boxYLen == 0 || boxZLen == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Box has zero-length dimension.");
                return;
            }

            Bitmap texture;
            try
            {
                texture = (Bitmap)Image.FromFile(texturePath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load texture: " + ex.Message);
                return;
            }

            // Ensure vertex normals exist for tri-planar projection
            if (mesh.Normals.Count != mesh.Vertices.Count)
                mesh.Normals.ComputeNormals();

            // Compute box local axes from corners
            Point3d[] corners = box.GetCorners();
            Vector3d xAxis = corners[1] - corners[0];
            Vector3d yAxis = corners[3] - corners[0];
            Vector3d zAxis = Vector3d.CrossProduct(xAxis, yAxis);
            zAxis.Unitize();
            zAxis *= boxZLen;

            // Unitized axes for normal dot products
            Vector3d xUnit = xAxis; xUnit.Unitize();
            Vector3d yUnit = yAxis; yUnit.Unitize();
            Vector3d zUnit = zAxis; zUnit.Unitize();

            // World-to-box transform
            Plane boxPlane = new Plane(corners[0], xAxis, yAxis);
            Transform boxToWorld = Transform.PlaneToPlane(Plane.WorldXY, boxPlane);
            boxToWorld.TryGetInverse(out Transform worldToBox);

            int imgW = texture.Width - 1;
            int imgH = texture.Height - 1;
            var colors = new List<Color>(mesh.Vertices.Count);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point3d pt = new Point3d(mesh.Vertices[i]);
                pt.Transform(worldToBox);

                // Determine dominant face via normal dot products
                Vector3d normal = mesh.Normals[i];
                double dotX = Math.Abs(normal * xUnit);
                double dotY = Math.Abs(normal * yUnit);
                double dotZ = Math.Abs(normal * zUnit);

                double u, v;

                bool xDominant = dotX >= dotY && dotX >= dotZ;
                bool zDominant = dotZ >= dotX && dotZ >= dotY;

                if (!capped && zDominant)
                {
                    // Redirect top/bottom to strongest side axis
                    if (dotX >= dotY)
                        xDominant = true;
                    else
                        xDominant = false;
                    zDominant = false;
                }

                if (xDominant)
                {
                    // X-dominant face: project onto YZ plane
                    u = pt.Y / boxYLen;
                    v = 1.0 - (pt.Z / boxZLen);
                }
                else if (zDominant)
                {
                    // Z-dominant face: project onto XY plane
                    u = pt.X / boxXLen;
                    v = 1.0 - (pt.Y / boxYLen);
                }
                else
                {
                    // Y-dominant face: project onto XZ plane
                    u = pt.X / boxXLen;
                    v = 1.0 - (pt.Z / boxZLen);
                }

                int px, py;
                if (tile)
                {
                    px = ((int)(u * imgW) % texture.Width + texture.Width) % texture.Width;
                    py = ((int)(v * imgH) % texture.Height + texture.Height) % texture.Height;
                }
                else
                {
                    px = Math.Max(0, Math.Min(imgW, (int)(u * imgW)));
                    py = Math.Max(0, Math.Min(imgH, (int)(v * imgH)));
                }

                colors.Add(texture.GetPixel(px, py));
            }

            texture.Dispose();

            // Conditional mesh output
            if (outputMesh)
            {
                Mesh coloredMesh = mesh.DuplicateMesh();
                coloredMesh.VertexColors.Clear();
                coloredMesh.VertexColors.AppendColors(colors.ToArray());
                DA.SetData(0, coloredMesh);
            }

            DA.SetDataList(1, colors);
            DA.SetData(2, $"Sampled {colors.Count} vertex colors from {imgW + 1}x{imgH + 1} texture.");
        }
    }
}
