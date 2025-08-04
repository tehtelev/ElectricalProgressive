
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ElectricalProgressive.RicipeSystem.Recipe;

  public class CentrifugeRecipe : IByteSerializable, IRecipeBase<CentrifugeRecipe>
  {
    public string Code;

    public double EnergyOperation;

    public AssetLocation Name { get; set; }

    public bool Enabled { get; set; } = true;

    IRecipeIngredient[] IRecipeBase<CentrifugeRecipe>.Ingredients => Ingredients;

    IRecipeOutput IRecipeBase<CentrifugeRecipe>.Output => Output;

    public CraftingRecipeIngredient[] Ingredients;

    public JsonItemStack Output;

    public CentrifugeRecipe Clone()
    {
      CraftingRecipeIngredient[] ingredients = new CraftingRecipeIngredient[Ingredients.Length];
      for (int i = 0; i < Ingredients.Length; i++)
      {
        ingredients[i] = Ingredients[i].Clone();
      }

      return new CentrifugeRecipe()
      {
        EnergyOperation = EnergyOperation,
        Output = Output.Clone(),
        Code = Code,
        Enabled = Enabled,
        Name = Name,
        Ingredients = ingredients
      };
    }

    public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
    {
      Dictionary<string, string[]> mappings = new Dictionary<string, string[]>();

      if (Ingredients == null || Ingredients.Length == 0) return mappings;

      foreach (CraftingRecipeIngredient ingred in Ingredients)
      {
        if (!ingred.Code.Path.Contains("*")) continue;

        int wildcardStartLen = ingred.Code.Path.IndexOf("*");
        int wildcardEndLen = ingred.Code.Path.Length - wildcardStartLen - 1;

        List<string> codes = new List<string>();

        if (ingred.Type == EnumItemClass.Block)
        {
          for (int i = 0; i < world.Blocks.Count; i++)
          {
            if (world.Blocks[i].Code == null || world.Blocks[i].IsMissing) continue;

            if (WildcardUtil.Match(ingred.Code, world.Blocks[i].Code))
            {
              string code = world.Blocks[i].Code.Path.Substring(wildcardStartLen);
              string codepart = code.Substring(0, code.Length - wildcardEndLen);
              if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart)) continue;

              codes.Add(codepart);

            }
          }
        }
        else
        {
          for (int i = 0; i < world.Items.Count; i++)
          {
            if (world.Items[i].Code == null || world.Items[i].IsMissing) continue;

            if (WildcardUtil.Match(ingred.Code, world.Items[i].Code))
            {
              string code = world.Items[i].Code.Path.Substring(wildcardStartLen);
              string codepart = code.Substring(0, code.Length - wildcardEndLen);
              if (ingred.AllowedVariants != null && !ingred.AllowedVariants.Contains(codepart)) continue;

              codes.Add(codepart);
            }
          }
        }

        mappings[ingred.Name] = codes.ToArray();
      }

      return mappings;
    }

    public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
    {
      bool ok = true;

      for (int i = 0; i < Ingredients.Length; i++)
      {
        ok &= Ingredients[i].Resolve(world, sourceForErrorLogging);
      }

      ok &= Output.Resolve(world, sourceForErrorLogging);

      return ok;
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
      Code = reader.ReadString();
      Ingredients = new CraftingRecipeIngredient[reader.ReadInt32()];

      for (int i = 0; i < Ingredients.Length; i++)
      {
        Ingredients[i] = new CraftingRecipeIngredient();
        Ingredients[i].FromBytes(reader, resolver);
        Ingredients[i].Resolve(resolver, "Centrifuge Recipe (FromBytes)");
      }

      Output = new JsonItemStack();
      Output.FromBytes(reader, resolver.ClassRegistry);
      Output.Resolve(resolver, "Centrifuge Recipe (FromBytes)");

      EnergyOperation = reader.ReadDouble();
    }

    public void ToBytes(BinaryWriter writer)
    {
      writer.Write(Code);
      writer.Write(Ingredients.Length);
      for (int i = 0; i < Ingredients.Length; i++)
      {
        Ingredients[i].ToBytes(writer);
      }

      Output.ToBytes(writer);

      writer.Write(EnergyOperation);
    }

    public bool Matches(ItemSlot[] inputSlots, out int outputStackSize)
    {
      outputStackSize = 0;

      List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>> matched = PairInput(inputSlots);
      if (matched == null) return false;

      outputStackSize = Output.StackSize;

      return outputStackSize >= 0;
    }

    List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>> PairInput(ItemSlot[] inputStacks)
    {
      List<CraftingRecipeIngredient> ingredientList = new List<CraftingRecipeIngredient>(Ingredients);

      Queue<ItemSlot> inputSlotsList = new Queue<ItemSlot>();
      foreach (ItemSlot val in inputStacks)
      {
        if (!val.Empty)
        {
          inputSlotsList.Enqueue(val);
        }
      }

      if (inputSlotsList.Count != Ingredients.Length)
      {
        return null;
      }

      List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>> matched = new List<KeyValuePair<ItemSlot, CraftingRecipeIngredient>>();

      while (inputSlotsList.Count > 0)
      {
        ItemSlot inputSlot = inputSlotsList.Dequeue();
        bool found = false;

        for (int i = 0; i < ingredientList.Count; i++)
        {
          CraftingRecipeIngredient ingred = ingredientList[i];

          if (ingred.SatisfiesAsIngredient(inputSlot.Itemstack))
          {
            matched.Add(new KeyValuePair<ItemSlot, CraftingRecipeIngredient>(inputSlot, ingred));
            found = true;
            ingredientList.RemoveAt(i);
            break;
          }
        }

        if (!found) return null;
      }

      // We're missing ingredients
      if (ingredientList.Count > 0)
      {
        return null;
      }

      return matched;
    }
  }