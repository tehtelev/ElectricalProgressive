using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECloche;

public class ItemSlotClocheSingle : ItemSlot
{
    public ItemSlotClocheSingle(InventoryBase inventory) : base(inventory)
    {
    }

    public override int MaxSlotStackSize => 1;
}

public class InventoryECloche : InventoryGeneric
{
    public InventoryECloche(BlockEntityECloche entity)
        : base(BlockEntityECloche.TotalSlots, "ecloche-tmp", (ICoreAPI)null)
    {
    }

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId == 0)
            return new ItemSlotClocheSingle(this);

        if (slotId >= 2)
            return new ItemSlotClocheOutput(this);

        return new ItemSlot(this);
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        if (sourceSlot?.Empty != false)
            return false;

        int id = GetSlotId(sinkSlot);
        if (id == 0)
            return BlockEntityECloche.IsSeed(Api?.World, sourceSlot.Itemstack);

        if (id == 1)
            return BlockEntityECloche.IsFertilizer(sourceSlot.Itemstack);

        return id >= 2;
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (sourceSlot?.Itemstack == null || targetSlot == null)
            return 0f;

        int id = GetSlotId(targetSlot);
        if (id == 0 && BlockEntityECloche.IsSeed(Api?.World, sourceSlot.Itemstack))
            return 4f;

        if (id == 1 && BlockEntityECloche.IsFertilizer(sourceSlot.Itemstack))
            return 4f;

        return id >= 2 ? 0.1f : 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Empty != false)
            return null;

        var stack = fromSlot.Itemstack;
        if (BlockEntityECloche.IsSeed(Api?.World, stack) && HasRoom(0, fromSlot))
            return this[0];

        if (BlockEntityECloche.IsFertilizer(stack) && HasRoom(1, fromSlot))
            return this[1];

        return null;
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        for (int i = 2; i < BlockEntityECloche.TotalSlots; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        return null;
    }

    private bool HasRoom(int slotId, ItemSlot fromSlot)
    {
        var slot = this[slotId];
        if (slot.Empty)
            return true;

        return slot.GetRemainingSlotSpace(fromSlot.Itemstack) > 0;
    }
}

public class ItemSlotClocheOutput : ItemSlot
{
    private static readonly string[] IgnoreSpoil = ConcatIgnore();

    public ItemSlotClocheOutput(InventoryBase inventory) : base(inventory)
    {
    }

    public override int GetRemainingSlotSpace(ItemStack forItemstack)
    {
        if (forItemstack == null)
            return 0;
        if (Empty)
            return Math.Min(MaxSlotStackSize, forItemstack.Collectible.MaxStackSize);
        if (!SameIgnoreSpoil(Inventory?.Api?.World, Itemstack, forItemstack))
            return 0;
        return Math.Max(0, Itemstack.Collectible.MaxStackSize - StackSize);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (sourceSlot?.Itemstack == null)
            return false;
        if (Empty)
            return true;
        return SameIgnoreSpoil(Inventory?.Api?.World, Itemstack, sourceSlot.Itemstack);
    }

    public static bool SameIgnoreSpoil(IWorldAccessor world, ItemStack a, ItemStack b)
    {
        if (a == null || b == null || world == null)
            return false;
        return a.Equals(world, b, IgnoreSpoil);
    }

    public static void MergePerish(ItemStack sink, ItemStack source, int addCount)
    {
        if (sink?.Attributes == null || source?.Attributes == null || addCount <= 0)
            return;

        var a = sink.Attributes["transitionstate"] as ITreeAttribute;
        var b = source.Attributes["transitionstate"] as ITreeAttribute;
        if (a == null && b == null)
            return;
        if (a == null)
        {
            sink.Attributes["transitionstate"] = b.Clone();
            return;
        }
        if (b == null)
            return;

        var ah = Hours(a);
        var bh = Hours(b);
        if (ah == null || bh == null)
            return;

        int n0 = Math.Max(1, sink.StackSize);
        int n = n0 + addCount;
        int len = Math.Min(ah.Length, bh.Length);
        for (int i = 0; i < len; i++)
            ah[i] = (ah[i] * n0 + bh[i] * addCount) / n;

        a["transitionedHours"] = new FloatArrayAttribute(ah);
    }

    private static float[] Hours(ITreeAttribute tree)
    {
        if (tree["transitionedHours"] is FloatArrayAttribute { value: not null } fa)
            return (float[])fa.value.Clone();
        return null;
    }

    private static string[] ConcatIgnore()
    {
        var src = GlobalConstants.IgnoredStackAttributes ?? [];
        var list = new string[src.Length + 1];
        Array.Copy(src, list, src.Length);
        list[src.Length] = "transitionstate";
        return list;
    }
}
