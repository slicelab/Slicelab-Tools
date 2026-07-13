using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Slicelab.Visualization
{
    public class PlanarTextureMappingComponent : GH_Component
    {
        public PlanarTextureMappingComponent()
            : base("Planar Texture Mapping", "SLPlnMap",
                "Sample a texture image at each mesh vertex using planar UV projection from a surface.",
                "Slicelab Tools", "Geometry Viz")
        { }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        public override Guid ComponentGuid => new Guid("C2D3E4F5-A6B7-4C8D-9E0F-1A2B3C4D5E22");
        protected override Bitmap Icon => IconHelper.LoadIcon("SL-PlnMap.png");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to sample", GH_ParamAccess.item);
            pManager.AddTextParameter("Texture Path", "T", "File path to the texture image", GH_ParamAccess.item);
            pManager.AddSurfaceParameter("Surface", "S", "Planar surface for UV projection", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Tile", "Ti", "Tile the texture when vertices are outside the surface (default: clamp to edges)", GH_ParamAccess.item, false);
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
            Surface surface = null;
            bool tile = false;
            bool outputMesh = true;

            if (!DA.GetData(0, ref mesh)) return;
            if (!DA.GetData(1, ref texturePath)) return;
            if (!DA.GetData(2, ref surface)) return;
            DA.GetData(3, ref tile);
            DA.GetData(4, ref outputMesh);

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

            if (surface == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Surface is null.");
                return;
            }

            if (!surface.IsPlanar())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Surface is not planar.");
                return;
            }

            if (!surface.TryGetPlane(out Plane plane))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to extract plane from surface.");
                return;
            }

            Interval domU = surface.Domain(0);
            Interval domV = surface.Domain(1);
            double width = domU.Length;
            double height = domV.Length;

            if (width == 0 || height == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Surface has zero-length domain.");
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

            // Build world-to-plane transform
            Transform planeToWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);
            planeToWorld.TryGetInverse(out Transform worldToPlane);

            int imgW = texture.Width - 1;
            int imgH = texture.Height - 1;
            var colors = new List<Color>(mesh.Vertices.Count);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Point3d pt = new Point3d(mesh.Vertices[i]);
                pt.Transform(worldToPlane);

                double u = (pt.X - domU.Min) / width;
                double v = 1.0 - ((pt.Y - domV.Min) / height);

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
