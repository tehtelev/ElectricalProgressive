using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHammer;

/// <summary>
/// Заготовка автомолота. Один удар двигает или снимает один воксель,
/// как механический молот на наковальне.
/// </summary>
public sealed class HammerWorkpiece
{
    public byte[,,] Voxels = new byte[16, 6, 16];
    public bool Active;
    public string Metal = "";
    public int RecipeId = -1;
    public int RemovedMetal;

    public int MetalCount
    {
        get
        {
            var n = 0;
            foreach (var voxel in Voxels)
            {
                if (voxel == (byte)EnumVoxelMaterial.Metal)
                    n++;
            }

            return n;
        }
    }

    public void Clear()
    {
        Voxels = new byte[16, 6, 16];
        Active = false;
        Metal = "";
        RecipeId = -1;
        RemovedMetal = 0;
    }

    public void Read(ITreeAttribute tree)
    {
        Active = tree.GetBool("workActive");
        Metal = tree.GetString("workMetal") ?? "";
        RecipeId = tree.GetInt("workRecipeId", -1);
        RemovedMetal = tree.GetInt("workRemoved");
        Voxels = BlockEntityAnvil.deserializeVoxels(tree.GetBytes("workVoxels"));
        if (!Active)
            Voxels = new byte[16, 6, 16];
    }

    public void Write(ITreeAttribute tree)
    {
        tree.SetBool("workActive", Active);
        tree.SetString("workMetal", Metal ?? "");
        tree.SetInt("workRecipeId", RecipeId);
        tree.SetInt("workRemoved", RemovedMetal);
        tree.SetBytes("workVoxels", Active ? BlockEntityAnvil.serializeVoxels(Voxels) : Array.Empty<byte>());
    }

    public void Begin(ICoreAPI api, string metal, SmithingRecipe recipe)
    {
        Metal = metal;
        RecipeId = recipe.RecipeId;
        RemovedMetal = 0;
        ItemIngot.CreateVoxelsFromIngot(api, ref Voxels, metal.Equals("blistersteel", StringComparison.OrdinalIgnoreCase));
        Active = true;
    }

    public int TryAddIngot() => ItemIngot.AddVoxelsFromIngot(ref Voxels);

    public bool Hit(SmithingRecipe recipe)
    {
        if (recipe.Voxels == null)
            return false;

        var ymax = recipe.QuantityLayers > 0 ? recipe.QuantityLayers : 6;
        var spare = FindSpare(recipe, ymax);

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 0; y < 6; y++)
                {
                    var require = y < ymax && recipe.Voxels[x, y, z];
                    var mat = (EnumVoxelMaterial)Voxels[x, y, z];

                    if (mat == EnumVoxelMaterial.Slag)
                    {
                        Voxels[x, y, z] = (byte)EnumVoxelMaterial.Empty;
                        return true;
                    }

                    if (require && spare != null && mat == EnumVoxelMaterial.Empty)
                    {
                        Voxels[x, y, z] = (byte)EnumVoxelMaterial.Metal;
                        Voxels[spare.X, spare.Y, spare.Z] = (byte)EnumVoxelMaterial.Empty;
                        return true;
                    }
                }
            }
        }

        if (spare == null)
            return false;

        Voxels[spare.X, spare.Y, spare.Z] = (byte)EnumVoxelMaterial.Empty;
        RemovedMetal++;
        return true;
    }

    public bool Matches(SmithingRecipe recipe)
    {
        if (recipe.Voxels == null)
            return false;

        var ymax = Math.Min(6, recipe.QuantityLayers > 0 ? recipe.QuantityLayers : 6);
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < ymax; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var desired = (byte)(recipe.Voxels[x, y, z] ? EnumVoxelMaterial.Metal : EnumVoxelMaterial.Empty);
                    if (Voxels[x, y, z] != desired)
                        return false;
                }
            }
        }

        return true;
    }

    public float MatchRatio(SmithingRecipe recipe)
    {
        if (recipe.Voxels == null)
            return 0f;

        var ymax = Math.Min(6, recipe.QuantityLayers > 0 ? recipe.QuantityLayers : 6);
        var need = 0;
        var got = 0;
        var extra = 0;
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < ymax; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var metal = Voxels[x, y, z] == (byte)EnumVoxelMaterial.Metal;
                    if (recipe.Voxels[x, y, z])
                    {
                        need++;
                        if (metal)
                            got++;
                    }
                    else if (metal)
                    {
                        extra++;
                    }
                }
            }
        }

        var denom = need + extra;
        return denom == 0 ? 0f : got / (float)denom;
    }

    public int LeftoverMetal(SmithingRecipe recipe)
    {
        var ymax = recipe.QuantityLayers > 0 ? recipe.QuantityLayers : 6;
        var leftover = RemovedMetal;
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 6; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var require = y < ymax && recipe.Voxels[x, y, z];
                    if (!require && Voxels[x, y, z] == (byte)EnumVoxelMaterial.Metal)
                        leftover++;
                }
            }
        }

        return leftover;
    }

    private Vec3i? FindSpare(SmithingRecipe recipe, int ymax)
    {
        for (var y = 5; y >= 0; y--)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var require = y < ymax && recipe.Voxels[x, y, z];
                    if (!require && Voxels[x, y, z] == (byte)EnumVoxelMaterial.Metal)
                        return new Vec3i(x, y, z);
                }
            }
        }

        return null;
    }
}
