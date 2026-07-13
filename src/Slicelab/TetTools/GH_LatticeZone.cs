using Grasshopper.Kernel.Types;

namespace Slicelab.TetTools
{
    public class GH_LatticeZone : GH_Goo<LatticeZone>
    {
        private static readonly string[] TypeNames = { "Edge", "Voronoi", "OppFaceCenter", "FaceCenterPairs" };

        public GH_LatticeZone() { Value = null; }
        public GH_LatticeZone(LatticeZone data) { Value = data; }
        public GH_LatticeZone(GH_LatticeZone other) { Value = other.Value; }

        public override bool IsValid =>
            Value != null && Value.RegionMesh != null && Value.RegionMesh.IsClosed && Value.FalloffDistance > 0;

        public override string TypeName => "Lattice Zone";
        public override string TypeDescription => "Region volume with assigned lattice type and falloff";

        public override IGH_Goo Duplicate() => new GH_LatticeZone(this);

        public override string ToString()
        {
            if (Value == null) return "Null LatticeZone";
            string name = Value.LatticeType >= 0 && Value.LatticeType < TypeNames.Length
                ? TypeNames[Value.LatticeType] : $"Type {Value.LatticeType}";
            return $"LatticeZone ({name}, falloff {Value.FalloffDistance:F1})";
        }

        public override bool CastFrom(object source)
        {
            if (source is LatticeZone lz)
            {
                Value = lz;
                return true;
            }
            if (source is GH_LatticeZone ghlz)
            {
                Value = ghlz.Value;
                return true;
            }
            return false;
        }

        public override bool CastTo<Q>(ref Q target)
        {
            if (typeof(Q) == typeof(GH_LatticeZone))
            {
                target = (Q)(object)new GH_LatticeZone(this);
                return true;
            }
            return false;
        }
    }
}
