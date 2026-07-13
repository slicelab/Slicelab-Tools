using Rhino.Geometry;

namespace Slicelab.TetTools
{
    public class LatticeZone
    {
        public Mesh RegionMesh;
        public int LatticeType;       // 0=Edge, 1=Voronoi, 2=OppFaceCenter, 3=FaceCenterPairs
        public double FalloffDistance;

        public LatticeZone() { }

        public LatticeZone(Mesh regionMesh, int latticeType, double falloffDistance)
        {
            RegionMesh = regionMesh;
            LatticeType = latticeType;
            FalloffDistance = falloffDistance;
        }
    }
}
