using ElectricalProgressive.Content.Block.ETermoGenerator;
using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.Termoplastini;

/// <summary>
/// Термоэлектрические пластины: multiblock + MachineConstruct (incomplete → assemble → formed).
/// </summary>
public class BlockTermoplastini : BlockEBase, IMultiBlockInteract
{
    public static readonly Dictionary<BlockFacing, Vec3i?> VarRotateOffset = new()
    {
        { BlockFacing.SOUTH, new Vec3i(1, 0, 0) },
        { BlockFacing.EAST, new Vec3i(0, 0, -1) },
        { BlockFacing.NORTH, new Vec3i(-1, 0, 0) },
        { BlockFacing.WEST, new Vec3i(0, 0, 1) }
    };

    private static BlockPos GetRealPosition(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        if (block is BlockMultiblock multiblock)
            return GetRealPosition(world, multiblock.GetControlBlockPos(pos));
        return pos;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        // В мир — всегда incomplete
        if (itemstack?.Block != null)
        {
            var side = itemstack.Block.Variant.ContainsKey("side")
                ? itemstack.Block.Variant["side"]
                : (Variant.ContainsKey("side") ? Variant["side"] : "south");

            if (itemstack.Block.Variant.ContainsKey("state") &&
                itemstack.Block.Variant["state"] != "incomplete")
            {
                var incomplete = world.GetBlock(CodeWithVariants(["state", "side"],
                    ["incomplete", side]));
                if (incomplete != null)
                    itemstack = new ItemStack(incomplete);
            }
        }

        var selection = new Selection(blockSel);
        var block = blockSel.Block;
        BlockFacing variant;
        BlockPos realPosition;

        if (block is BlockMultiblock)
            variant = selection.Face;
        else
        {
            realPosition = GetRealPosition(world, blockSel.Position);
            block = world.BlockAccessor.GetBlock(realPosition);

            if (block is BlockETermoGenerator || block is BlockTermoplastini)
                variant = selection.Face;
            else if (block.Code.Path.Contains("air"))
                variant = selection.Face;
            else
                variant = selection.Direction;
        }

        Vec3i? offset = new Vec3i(0, 0, 0);
        if (block is BlockMultiblock multiblock)
            offset = multiblock.OffsetInv;
        else
        {
            block = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(variant));
            if (block is BlockMultiblock mb)
                offset = mb.OffsetInv;
        }

        realPosition = GetRealPosition(world, blockSel.Position.AddCopy(variant));
        block = world.BlockAccessor.GetBlock(realPosition);

        if (block is not BlockETermoGenerator && block is not BlockTermoplastini)
            return false;

        if (block is BlockETermoGenerator)
            if (offset != VarRotateOffset[variant])
                return false;

        if (block is BlockTermoplastini)
            if (offset != new Vec3i(0, 0, 0))
                return false;

        BlockPos check10Pos;
        if (variant == BlockFacing.NORTH)
            check10Pos = blockSel.Position.NorthCopy(10);
        else if (variant == BlockFacing.SOUTH)
            check10Pos = blockSel.Position.SouthCopy(10);
        else if (variant == BlockFacing.EAST)
            check10Pos = blockSel.Position.EastCopy(10);
        else if (variant == BlockFacing.WEST)
            check10Pos = blockSel.Position.WestCopy(10);
        else
            return false;

        block = world.BlockAccessor.GetBlock(check10Pos);
        if (block is BlockTermoplastini)
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel is null)
            return false;
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var controllerPos = MachineConstructAccess.GetControllerPos(world, blockSel.Position);
        return HandleInteract(world, byPlayer, controllerPos);
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv);
        return HandleInteract(world, byPlayer, controllerPos);
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos)
    {
        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, controllerPos))
            return true;

        var construct = MachineConstructAccess.GetBehavior(world, controllerPos);
        if (construct != null && construct.HasConstruction && !construct.IsReady)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressivebasics:termoplastini-structure-incomplete"));
            }

            return true;
        }

        return false;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine();
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:termoplastini-structure-hint"));
    }

    #region IMultiBlockInteract

    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => false;

    public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset) => false;

    public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset)
    {
    }

    public bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset) => true;

    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset)
    {
        var controllerPos = MachineConstructAccess.GetControllerPos(pos, offset);
        var handling = EnumHandling.PassThrough;
        foreach (var bh in BlockBehaviors)
        {
            var h = EnumHandling.PassThrough;
            var stack = bh.OnPickBlock(world, controllerPos, ref h);
            if (h != EnumHandling.PassThrough && stack != null)
                return stack;
        }

        return OnPickBlock(world, controllerPos);
    }

    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel,
        IPlayer forPlayer, Vec3i offset)
    {
        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offset);
        var sel = blockSel.Clone();
        sel.Position = controllerPos;
        return GetPlacedBlockInteractionHelp(world, sel, forPlayer);
    }

    public BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack,
        Vec3i offset) =>
        Sounds;

    #endregion
}
