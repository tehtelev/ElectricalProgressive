using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using EPImmersive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace EPImmersive.Content.Block.EAccumulator;

public class BlockEIAccumulator1 : ImmersiveWireBlock, IEnergyStorageItem
{
    public int maxcapacity;
    int consume;



    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        maxcapacity = MyMiniLib.GetAttributeInt(this, "maxcapacity", 16000);
        consume = MyMiniLib.GetAttributeInt(this, "consume", 64);
    }

    /// <summary>
    /// Зарядка
    /// </summary>
    public int receiveEnergy(ItemStack itemstack, int maxReceive)
    {
        var energy = itemstack.Attributes.GetInt("durability") * consume;
        var maxEnergy = itemstack.Collectible.GetMaxDurability(itemstack) * consume;

        var received = Math.Min(maxEnergy - energy, maxReceive);
        energy += received;

        var durab = Math.Max(1, energy / consume);
        itemstack.Attributes.SetInt("durability", durab);
        return received;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        return world.BlockAccessor
                   .GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN))
                   .SideSolid[BlockFacing.indexUP]
                   && base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (!world.BlockAccessor
                .GetBlock(pos.AddCopy(BlockFacing.DOWN))
                .SideSolid[BlockFacing.indexUP])
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    /// <summary>
    /// Получение информации о предмете в инвентаре
    /// </summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume;
        var maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Capacity") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Power") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "power", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEIAccumulator1;
        ItemStack item = new(world.BlockAccessor.GetBlock(pos));

        if (be != null)
        {
            var maxDurability = item.Collectible.GetMaxDurability(item);
            var maxEnergy = maxDurability * consume;

            item.Attributes.SetInt("durability", (int)(maxDurability * be.GetBehavior<BEBehaviorEIAccumulator1>().GetCapacity() / maxEnergy));
        }

        return [item];
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEIAccumulator1;
        ItemStack item = new(world.BlockAccessor.GetBlock(pos));

        if (be != null)
        {
            var maxDurability = item.Collectible.GetMaxDurability(item);
            var maxEnergy = maxDurability * consume;

            item.Attributes.SetInt("durability", (int)(maxDurability * be.GetBehavior<BEBehaviorEIAccumulator1>().GetCapacity() / maxEnergy));
        }

        return item;
    }

    /// <summary>
    /// Вызывается при установке блока, чтобы задать начальные параметры
    /// </summary>
    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null!)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        if (byItemStack != null)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockPos) as BlockEntityEIAccumulator1;

            var maxDurability = byItemStack.Collectible.GetMaxDurability(byItemStack);
            var standartDurability = byItemStack.Collectible.Durability;

            var durability = byItemStack.Attributes.GetInt("durability", 1);
            var energy = durability * consume;

            be!.GetBehavior<BEBehaviorEIAccumulator1>().SetCapacity(energy, maxDurability * 1.0F / standartDurability);
        }
    }



    /// <summary>
    /// Очистка кэша при выгрузке
    /// </summary>
    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);

    }



    
}