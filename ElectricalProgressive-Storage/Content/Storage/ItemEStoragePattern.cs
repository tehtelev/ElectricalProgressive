using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Storage;

public class ItemEStoragePattern : Vintagestory.API.Common.Item
{
    public const string EncodedKey = "encoded";
    public const string ModeKey = "mode";
    public const string SubKey = "sub";
    public const string OutKey = "out";

    private static int _iconDepth;

    public static bool IsPattern(ItemStack? stack) => stack?.Item is ItemEStoragePattern;

    public static bool IsEncoded(ItemStack? stack) => IsPattern(stack) && stack!.Attributes.GetBool(EncodedKey);

    public static bool IsBlank(ItemStack? stack) => IsPattern(stack) && !stack!.Attributes.GetBool(EncodedKey);

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var key = IsEncoded(itemStack)
            ? "electricalprogressivestorage:item-estoragepattern-encoded"
            : "electricalprogressivestorage:item-estoragepattern";
        return Lang.Get(key);
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (target != EnumItemRenderTarget.Gui || _iconDepth > 0 || !IsEncoded(itemstack))
            return;

        // В ряду шаблонов интерфейса выход виден всегда. В остальных слотах — только пока зажата кнопка красться.
        if (renderinfo.InSlot is not ItemSlotInterface { Row: InterfaceRow.Pattern } && !SneakDown(capi))
            return;

        var output = Primary(capi.World, itemstack);
        if (output == null)
            return;

        var shown = output.Clone();
        shown.StackSize = 1;
        _iconDepth++;
        try
        {
            var info = capi.Render.GetItemStackRenderInfo(new DummySlot(shown), EnumItemRenderTarget.Gui, 0);
            if (info?.ModelRef == null || info.ModelRef.Disposed)
                return;
            renderinfo.ModelRef = info.ModelRef;
            if (info.Transform != null)
                renderinfo.Transform = info.Transform;
            renderinfo.TextureId = info.TextureId;
            renderinfo.CullFaces = info.CullFaces;
            renderinfo.AlphaTest = info.AlphaTest;
        }
        finally
        {
            _iconDepth--;
        }
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var stack = inSlot.Itemstack;
        if (stack == null)
            return;
        if (!IsEncoded(stack))
        {
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-pattern-blank"));
            return;
        }

        var processing = stack.Attributes.GetInt(ModeKey) != 0;
        dsc.AppendLine(Lang.Get(processing
            ? "electricalprogressivestorage:estorage-pattern-process"
            : "electricalprogressivestorage:estorage-pattern-craft"));
        dsc.AppendLine(Lang.Get(stack.Attributes.GetBool(SubKey)
            ? "electricalprogressivestorage:estorage-pattern-sub-on"
            : "electricalprogressivestorage:estorage-pattern-sub-off"));
        var listed = false;
        for (var i = 0; i < 3; i++)
        {
            if (stack.Attributes["out" + i] is not ItemstackAttribute { value: { } output } || !Ready(world, output))
                continue;
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-pattern-makes", output.GetName(), output.StackSize));
            listed = true;
        }

        if (!listed && stack.Attributes[OutKey] is ItemstackAttribute { value: { } single } && Ready(world, single))
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-pattern-makes", single.GetName(), single.StackSize));
    }

    private static bool SneakDown(ICoreClientAPI capi)
    {
        var map = capi.Input.GetHotKeyByCode("sneak")?.CurrentMapping;
        var raw = capi.Input.KeyboardKeyStateRaw;
        if (map == null || raw == null)
            return capi.World.Player?.Entity?.Controls.Sneak == true;

        if (!Held(raw, map.KeyCode))
            return false;
        return map.SecondKeyCode is not int second || second <= 0 || Held(raw, second);
    }

    private static bool Held(bool[] raw, int code)
        => code > 0 && code < raw.Length && raw[code];

    public static ItemStack? Primary(IWorldAccessor world, ItemStack stack)
    {
        for (var i = 0; i < 3; i++)
        {
            if (stack.Attributes["out" + i] is ItemstackAttribute { value: { } output } && Ready(world, output))
                return output;
        }

        if (stack.Attributes[OutKey] is ItemstackAttribute { value: { } single } && Ready(world, single))
            return single;
        return null;
    }

    public static bool IsProcessing(ItemStack? stack)
        => IsEncoded(stack) && stack!.Attributes.GetInt(ModeKey) != 0;

    public static int PerCraft(IWorldAccessor world, ItemStack stack)
        => Math.Max(1, Primary(world, stack)?.StackSize ?? 1);

    public static ItemStack? Cell(IWorldAccessor world, ItemStack stack, int index)
    {
        if (index < 0 || index > 8)
            return null;
        if (stack.Attributes["in" + index] is not ItemstackAttribute { value: { } sample } || !Ready(world, sample))
            return null;

        var copy = sample.Clone();
        if (copy.StackSize < 1)
            copy.StackSize = 1;
        return copy;
    }

    public static void Inputs(IWorldAccessor world, ItemStack stack, List<ItemStack> into)
    {
        into.Clear();
        for (var i = 0; i < 9; i++)
        {
            if (stack.Attributes["in" + i] is not ItemstackAttribute { value: { } sample } || !Ready(world, sample))
                continue;
            var copy = sample.Clone();
            if (copy.StackSize < 1)
                copy.StackSize = 1;
            into.Add(copy);
        }
    }

    private static bool Ready(IWorldAccessor world, ItemStack stack)
        => stack.Collectible != null || stack.ResolveBlockOrItem(world);

    public static ItemStack? Encode(IWorldAccessor world, IPlayer player, ItemStack?[] grid, ItemStack?[]? processOut, bool processing, bool substitute)
    {
        ItemStack?[] outputs;
        if (processing)
        {
            if (processOut == null || !HasAny(grid) || !HasAny(processOut))
                return null;
            outputs = processOut;
        }
        else
        {
            var crafted = ResolveCraft(world, player, grid);
            if (crafted == null)
                return null;
            outputs = [crafted];
        }

        var item = world.GetItem(new AssetLocation("electricalprogressivestorage:estoragepattern"));
        if (item == null)
            return null;

        var made = new ItemStack(item, 1);
        made.Attributes.SetBool(EncodedKey, true);
        made.Attributes.SetInt(ModeKey, processing ? 1 : 0);
        made.Attributes.SetBool(SubKey, substitute);
        for (var i = 0; i < outputs.Length; i++)
        {
            if (outputs[i] == null)
                continue;
            var sample = outputs[i]!.Clone();
            made.Attributes["out" + i] = new ItemstackAttribute(sample);
            if (i == 0)
                made.Attributes[OutKey] = new ItemstackAttribute(sample.Clone());
        }

        for (var i = 0; i < grid.Length; i++)
        {
            if (grid[i] == null)
                continue;
            made.Attributes["in" + i] = new ItemstackAttribute(grid[i]!.Clone());
        }

        return made;
    }

    public static ItemStack? ResolveCraft(IWorldAccessor world, IPlayer? player, ItemStack?[] grid)
    {
        var slots = new ItemSlot[9];
        for (var i = 0; i < 9; i++)
        {
            var sample = grid[i]?.Clone();
            if (sample != null)
                sample.StackSize = 1;
            slots[i] = new DummySlot(sample);
        }

        foreach (var recipe in world.GridRecipes)
        {
            var ingredient = recipe.Output;
            var resolved = ingredient?.ResolvedItemStack;
            if (!recipe.Enabled || ingredient == null || resolved == null || player == null
                || !recipe.Matches(player, world, slots, 3))
                continue;
            var output = resolved.Clone();
            output.StackSize = Math.Max(1, ingredient.StackSize);
            return output;
        }

        return null;
    }

    public static bool HasAny(ItemStack?[] grid)
    {
        foreach (var stack in grid)
        {
            if (stack != null)
                return true;
        }

        return false;
    }
}
