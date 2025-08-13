
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ElectricalProgressive.RicipeSystem.Recipe;

public class HammerRecipe : IByteSerializable, IRecipeBase<HammerRecipe>
{
    public string Code;
    public double EnergyOperation;
    public AssetLocation Name { get; set; }
    public bool Enabled { get; set; } = true;
    
    IRecipeIngredient[] IRecipeBase<HammerRecipe>.Ingredients => Ingredients;
    IRecipeOutput IRecipeBase<HammerRecipe>.Output => Output;
    
    public CraftingRecipeIngredient[] Ingredients;
    public JsonItemStack Output;
    
    // Новые поля для второго выхода
    public JsonItemStack SecondaryOutput;
    public float SecondaryOutputChance = 0f; // Шанс в диапазоне 0-1 (0-100%)

    public HammerRecipe Clone()
    {
        CraftingRecipeIngredient[] ingredients = new CraftingRecipeIngredient[Ingredients.Length];
        for (int i = 0; i < Ingredients.Length; i++)
        {
            ingredients[i] = Ingredients[i].Clone();
        }

        return new HammerRecipe()
        {
            EnergyOperation = EnergyOperation,
            Output = Output.Clone(),
            SecondaryOutput = SecondaryOutput?.Clone(),
            SecondaryOutputChance = SecondaryOutputChance,
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

    // 1. Сначала разрешаем ингредиенты
    for (int i = 0; i < Ingredients.Length; i++)
    {
        ok &= Ingredients[i].Resolve(world, sourceForErrorLogging);
    }

    // 2. Собираем словарь для подстановок
    Dictionary<string, string> substitutions = new Dictionary<string, string>();
    
    if (Ingredients.Length > 0 && Ingredients[0].Code != null)
    {
        // Автоматически извлекаем значения из кода ингредиента
        var parts = Ingredients[0].Code.Path.Split('-');
        if (parts.Length > 1)
        {
            substitutions["metal"] = parts[1]; // Для совместимости
            substitutions["material"] = parts[1]; // Более универсальное имя
        }
        
        // Добавляем имя ингредиента, если указано
        if (!string.IsNullOrEmpty(Ingredients[0].Name))
        {
            substitutions[Ingredients[0].Name] = parts.Length > 1 ? parts[1] : "";
        }
    }

    // 3. Обрабатываем основной выход
    ok &= ResolveWithSubstitutions(Output, world, sourceForErrorLogging, substitutions);
    
    // 4. Обрабатываем дополнительный выход
    if (SecondaryOutput != null)
    {
        ok &= ResolveWithSubstitutions(SecondaryOutput, world, sourceForErrorLogging, substitutions);
    }

    return ok;
}

private bool ResolveWithSubstitutions(JsonItemStack stack, IWorldAccessor world, 
    string sourceForErrorLogging, Dictionary<string, string> substitutions)
{
    if (stack == null) return true;
    
    // Клонируем, чтобы не менять оригинальный объект
    JsonItemStack tempStack = stack.Clone();
    
    // Заменяем все шаблоны {variable}
    if (tempStack.Code?.Path != null && substitutions.Count > 0)
    {
        foreach (var sub in substitutions)
        {
            tempStack.Code.Path = tempStack.Code.Path
                .Replace($"{{{sub.Key}}}", sub.Value);
        }
    }

    bool resolved = tempStack.Resolve(world, sourceForErrorLogging);
    
    // Если разрешилось успешно, сохраняем изменения
    if (resolved)
    {
        stack.Code = tempStack.Code;
        stack.StackSize = tempStack.StackSize;
        // ... другие необходимые поля
    }

    return resolved;
}

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        Code = reader.ReadString();
        Ingredients = new CraftingRecipeIngredient[reader.ReadInt32()];

        for (int i = 0; i < Ingredients.Length; i++)
        {
            Ingredients[i] = new CraftingRecipeIngredient();
            Ingredients[i].FromBytes(reader, resolver);
            Ingredients[i].Resolve(resolver, "Hammer Recipe (FromBytes)");
        }

        Output = new JsonItemStack();
        Output.FromBytes(reader, resolver.ClassRegistry);
        Output.Resolve(resolver, "Hammer Recipe (FromBytes)");

        // Чтение дополнительного выхода
        bool hasSecondaryOutput = reader.ReadBoolean();
        if (hasSecondaryOutput)
        {
            SecondaryOutput = new JsonItemStack();
            SecondaryOutput.FromBytes(reader, resolver.ClassRegistry);
            SecondaryOutput.Resolve(resolver, "Hammer Recipe Secondary Output (FromBytes)");
            SecondaryOutputChance = reader.ReadSingle();
        }

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

        // Запись дополнительного выхода
        writer.Write(SecondaryOutput != null);
        if (SecondaryOutput != null)
        {
            SecondaryOutput.ToBytes(writer);
            writer.Write(SecondaryOutputChance);
        }

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

        if (ingredientList.Count > 0)
        {
            return null;
        }

        return matched;
    }
}