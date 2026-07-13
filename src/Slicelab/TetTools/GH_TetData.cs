using Grasshopper.Kernel.Types;

namespace Slicelab.TetTools
{
    public class GH_TetData : GH_Goo<TetData>
    {
        public GH_TetData() { Value = null; }
        public GH_TetData(TetData data) { Value = data; }
        public GH_TetData(GH_TetData other) { Value = other.Value; }

        public override bool IsValid =>
            Value != null && Value.Points != null && Value.TetraIndices != null && Value.TetCount > 0;

        public override string TypeName => "Tet Data";
        public override string TypeDescription => "Combined tetrahedral points and indices";

        public override IGH_Goo Duplicate() => new GH_TetData(this);

        public override string ToString()
        {
            if (Value == null) return "Null TetData";
            return $"TetData ({Value.Points.Length} pts, {Value.TetCount} tets)";
        }

        public override bool CastFrom(object source)
        {
            if (source is TetData td)
            {
                Value = td;
                return true;
            }
            if (source is GH_TetData ghtd)
            {
                Value = ghtd.Value;
                return true;
            }
            return false;
        }

        public override bool CastTo<Q>(ref Q target)
        {
            if (typeof(Q) == typeof(GH_TetData))
            {
                target = (Q)(object)new GH_TetData(this);
                return true;
            }
            return false;
        }
    }
}
