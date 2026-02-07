using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools; 

namespace ElectricalProgressive.Content.Block.EAccumulator;

public class BlockEAccumulator : BlockEBase, IEnergyStorageItem
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
    /// <param name="itemstack"></param>
    /// <param name="maxReceive"></param>
    /// <returns></returns>
    public int receiveEnergy(ItemStack itemstack, int maxReceive)
    {
        var energy = itemstack.Attributes.GetInt("durability") * consume; //текущая энергия
        var maxEnergy = itemstack.Collectible.GetMaxDurability(itemstack) * consume;       //максимальная энергия

        var received = Math.Min(maxEnergy - energy, maxReceive);

        energy += received;

        var durab = Math.Max(1, energy / consume);
        itemstack.Attributes.SetInt("durability", durab);
        return received;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        //неваляжка - только вертикально
        return world.BlockAccessor
                   .GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN))
                   .SideSolid[BlockFacing.indexUP]
                   && base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        //проверяем только блок под нами
        if (
            !world.BlockAccessor
                .GetBlock(pos.AddCopy(BlockFacing.DOWN))
                .SideSolid[BlockFacing.indexUP]
        )
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    /// <summary>
    /// Получение информации о предмете в инвентаре
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume; //текущая энергия
        var maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;       //максимальная энергия

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Capacity") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Power") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "power", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEAccumulator;
        ItemStack item = new(world.BlockAccessor.GetBlock(pos));


        if (be != null)
        {
            var maxDurability = item.Collectible.GetMaxDurability(item); //максимальная прочность
            var maxEnergy = maxDurability * consume;       //максимальная энергия


            item.Attributes.SetInt("durability", (int)(maxDurability * be.GetBehavior<BEBehaviorEAccumulator>().GetCapacity() / maxEnergy));
        }

        return [item];
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEAccumulator;
        ItemStack item = new(world.BlockAccessor.GetBlock(pos));


        if (be != null)
        {
            var maxDurability = item.Collectible.GetMaxDurability(item); //максимальная прочность
            var maxEnergy = maxDurability * consume;       //максимальная энергия


            item.Attributes.SetInt("durability", (int)(maxDurability * be.GetBehavior<BEBehaviorEAccumulator>().GetCapacity() / maxEnergy));
        }

        return item;
    }

    /// <summary>
    /// Вызывается при установке блока, чтобы задать начальные параметры
    /// </summary>
    /// <param name="world"></param>
    /// <param name="blockPos"></param>
    /// <param name="byItemStack"></param>
    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null!)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        if (byItemStack != null)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockPos) as BlockEntityEAccumulator;

            var maxDurability = byItemStack.Collectible.GetMaxDurability(byItemStack); //максимальная прочность
            var standartDurability = byItemStack.Collectible.Durability;       //стандартная прочность

            var durability = byItemStack.Attributes.GetInt("durability", 1);  //текущая прочность
            var energy = durability * consume;       //максимальная энергия

            be!.GetBehavior<BEBehaviorEAccumulator>().SetCapacity(energy, maxDurability * 1.0F / standartDurability);
        }
    }
}