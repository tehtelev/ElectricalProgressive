using System.IO;
using Vintagestory.API.Common;
using Newtonsoft.Json;
using System;

namespace ElectricalProgressive.RecipeSystem.Recipe
{
    [JsonObject(MemberSerialization.OptIn)]
    public class RecipeOutput : IRecipeOutput
    {
        [JsonProperty]
        public string Type { get; set; } = "item";

        [JsonProperty]
        public AssetLocation Code { get; set; }

        [JsonProperty]
        public int StackSize { get; set; } = 1;

        [JsonProperty]
        public float Chance { get; set; } = 1.0f;

        // Добавляем ResolvedItemstack
        [JsonIgnore]
        public ItemStack ResolvedItemstack { get; set; }

        // Реализация IRecipeOutput.ResolvedItemStack
        ItemStack IRecipeOutput.ResolvedItemStack => ResolvedItemstack;

        // Реализация ICloneable.Clone()
        public object Clone()
        {
            return Cloone();
        }

        // Оставляем типизированный Clone для удобства
        public RecipeOutput Cloone()
        {
            return new RecipeOutput()
            {
                Type = Type,
                Code = Code?.Clone(),
                StackSize = StackSize,
                Chance = Chance,
                ResolvedItemstack = ResolvedItemstack?.Clone()
            };
        }

        public void FillPlaceHolder(string placeholder, string code)
        {
            if (Code != null)
            {
                Code.Path = Code.Path.Replace("{" + placeholder + "}", code);
            }
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            if (Code == null)
            {
                world.Logger.Error("Recipe output code is null in {0}", sourceForErrorLogging);
                return false;
            }

            if (Type == "item")
            {
                var item = world.GetItem(Code);
                if (item == null)
                {
                    world.Logger.Error("Recipe output item with code {0} not found in {1}", Code, sourceForErrorLogging);
                    return false;
                }
                // Создаем ResolvedItemstack
                ResolvedItemstack = new ItemStack(item, StackSize);
            }
            else if (Type == "block")
            {
                var block = world.GetBlock(Code);
                if (block == null || block.IsMissing)
                {
                    world.Logger.Error("Recipe output block with code {0} not found in {1}", Code, sourceForErrorLogging);
                    return false;
                }
                // Создаем ResolvedItemstack
                ResolvedItemstack = new ItemStack(block, StackSize);
            }

            return true;
        }

        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Type ?? "item");
            writer.Write(Code?.ToString() ?? "");
            writer.Write(StackSize);
            writer.Write(Chance);

            // Записываем информацию о ResolvedItemstack
            writer.Write(ResolvedItemstack != null);
            if (ResolvedItemstack != null)
            {
                ResolvedItemstack.ToBytes(writer);
            }
        }

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            Type = reader.ReadString();
            var codeStr = reader.ReadString();
            if (!string.IsNullOrEmpty(codeStr))
            {
                Code = new AssetLocation(codeStr);
            }
            StackSize = reader.ReadInt32();
            Chance = reader.ReadSingle();

            // Читаем информацию о ResolvedItemstack
            var hasResolvedStack = reader.ReadBoolean();
            if (hasResolvedStack)
            {
                ResolvedItemstack = new ItemStack();
                ResolvedItemstack.FromBytes(reader);
            }
        }
    }
}