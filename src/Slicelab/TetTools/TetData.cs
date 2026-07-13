using System.Collections.Generic;
using Grasshopper;
using Rhino.Geometry;

namespace Slicelab.TetTools
{
    public class TetData
    {
        public Point3d[] Points;
        public int[] TetraIndices;  // flat array, 4 per tet
        public int TetCount;

        /// <summary>Tets per cell (e.g. 66 for a BCC cell). 0 = non-BCC data.</summary>
        public int TetsPerCell;

        /// <summary>
        /// Per cell-local tet-face mask: true if the face lies on a cell boundary plane.
        /// Indexed as [localTet * 4 + face]. Null for non-BCC data.
        /// Use globalTet % TetsPerCell to get the local tet index.
        /// </summary>
        public bool[] BoundaryFaceMask;

        /// <summary>
        /// Per cell-local tet-face neighbor direction. -1 = not boundary.
        /// 0=x-, 1=x+, 2=y-, 3=y+, 4=z-, 5=z+. Null for non-BCC data.
        /// </summary>
        public int[] BoundaryFaceNeighborDir;

        public TetData() { }

        public TetData(Point3d[] points, int[] tetraIndices, int tetCount)
        {
            Points = points;
            TetraIndices = tetraIndices;
            TetCount = tetCount;
        }

        /// <summary>
        /// Creates TetData from a point list and a DataTree of tet indices (one branch of 4 ints per tet).
        /// </summary>
        public static TetData FromTree(List<Point3d> points, DataTree<int> tetraTree)
        {
            int tetCount = tetraTree.BranchCount;
            int[] indices = new int[tetCount * 4];
            for (int i = 0; i < tetCount; i++)
            {
                var branch = tetraTree.Branches[i];
                indices[i * 4] = branch[0];
                indices[i * 4 + 1] = branch[1];
                indices[i * 4 + 2] = branch[2];
                indices[i * 4 + 3] = branch[3];
            }
            return new TetData(points.ToArray(), indices, tetCount);
        }
    }
}
