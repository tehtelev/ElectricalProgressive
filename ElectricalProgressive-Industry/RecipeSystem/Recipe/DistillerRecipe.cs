using System.IO;
using Vintagestory.API.Common;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class DistillerRecipe : BaseMultiRecipe<DistillerRecipe>
{
    public float MashLitres = 1f;
    public float WaterLitres = 0.25f;
    public float ProductLitres = 0.1f;

    protected override DistillerRecipe CreateInstance() => new();

    public override DistillerRecipe Clone()
    {
        var clone = base.Clone();
        clone.MashLitres = MashLitres;
        clone.WaterLitres = WaterLitres;
        clone.ProductLitres = ProductLitres;
        return clone;
    }

    public new void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        base.FromBytes(reader, resolver);
        MashLitres = reader.ReadSingle();
        WaterLitres = reader.ReadSingle();
        ProductLitres = reader.ReadSingle();
    }

    public new void ToBytes(BinaryWriter writer)
    {
        base.ToBytes(writer);
        writer.Write(MashLitres);
        writer.Write(WaterLitres);
        writer.Write(ProductLitres);
    }
}
