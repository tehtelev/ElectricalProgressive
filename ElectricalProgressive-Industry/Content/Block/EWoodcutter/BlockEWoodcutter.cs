using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EWoodcutter;

public class BlockEWoodcutter : BlockEBase
{
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return new[] { OnPickBlock(world, pos) };
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
    {
        if (blockSel is null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        blockSel.Block = this;

        var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (blockEntity is BlockEntityOpenableContainer openableContainer)
            openableContainer.OnPlayerRightClick(byPlayer, blockSel);

        return true;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        var upPos = pos.AddCopy(BlockFacing.UP);

        var block = world.BlockAccessor.GetBlock(upPos);
        if (block is not BlockSapling blockSapling)
            return;

        //TODO: Надо вынести все дублирования проверок на валидность саженца и тд
        var isValidType = blockSapling.Variant["type"] switch
        {
            "redwood" => false,

            _ => true
        };

        if (isValidType)
            return;

        world.BlockAccessor.BreakBlock(upPos, null);
    }
}