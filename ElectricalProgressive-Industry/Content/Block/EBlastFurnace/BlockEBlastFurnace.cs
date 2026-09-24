using ElectricalProgressive.Utils;
using ImmersiveWireBlock = ElectricalProgressive.Content.Block.ImmersiveWireBlock;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

/// <summary>
/// IMultiBlockInteract — сборка/GUI с любого dummy multiblock, не только с контроллера.
/// </summary>
public class BlockEBlastFurnace : ImmersiveWireBlock, IMultiBlockInteract, IMultiBlockColSelBoxes
{
    public bool IsFormed => Variant["state"] == "formed";
    public bool IsIncomplete => Variant["state"] == "incomplete";

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
    {
        if (blockSel is null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        return HandleInteract(world, byPlayer, blockSel.Position, blockSel);
    }

    /// <summary>С dummy Multiblock: blockSel — кликнутая клетка, offsetInv → контроллер.</summary>
    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv);
        return HandleInteract(world, byPlayer, controllerPos, blockSel);
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos,
        BlockSelection blockSel)
    {
        blockSel.Block = this;

        // Сборка с контроллера (или после редиректа с dummy)
        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, controllerPos))
            return true;

        if (IsHoldingEKit(byPlayer))
            return true;

        var blockEntity = world.BlockAccessor.GetBlockEntity(controllerPos);
        if (blockEntity is null)
            return true;

        var construct = blockEntity.GetBehavior<MachineConstruct>();
        if (construct != null && construct.HasConstruction && !construct.IsReady)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressiveindustry:eblastfurnace-structure-incomplete"));
            }

            return true;
        }

        if (blockEntity is BlockEntityEBlastFurnace furnace && !furnace.StructureComplete)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressiveindustry:eblastfurnace-structure-incomplete"));
            }

            return true;
        }

        if (blockEntity is BlockEntityOpenableContainer openable)
            openable.OnPlayerRightClick(byPlayer, blockSel);

        return true;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
            return;

        world.BlockAccessor.BreakBlock(pos, null);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " +
                       MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " +
                       Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " +
                       MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxConsumption", 0) + " " +
                       Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                       (MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)
                           ? Lang.Get("electricalprogressivebasics:Yes")
                           : Lang.Get("electricalprogressivebasics:No")));
        dsc.AppendLine();
        dsc.AppendLine(Lang.Get("electricalprogressiveindustry:eblastfurnace-structure-hint"));
    }

    #region IMultiBlockInteract

    public override void OnLoaded(ICoreAPI coreApi)
    {
        base.OnLoaded(coreApi);
        _skipNonCenterCollisions = true;
    }

    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => IsEKitMode();

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (IsEKitMode())
            return GetNodeSelectionBoxes(blockAccessor, pos);
        return BodySelectionBoxes();
    }

    public new Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
    {
        if (IsEKitMode())
        {
            var coll = GetNodeSelectionBoxes(blockAccessor, pos.AddCopy(offset));
            foreach (var col in coll)
            {
                col.X1 += offset.X;
                col.Y1 += offset.Y;
                col.Z1 += offset.Z;
                col.X2 += offset.X;
                col.Y2 += offset.Y;
                col.Z2 += offset.Z;
            }
            return coll;
        }

        if (Math.Abs(offset.Y) >= 2)
            return [];

        return BodySelectionBoxes();
    }

    public new Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
    {
        if (Math.Abs(offset.Y) >= 2)
            return [];

        var boxes = new List<Cuboidf>(1);
        boxes.AddRange(base.GetCollisionBoxes(blockAccessor, pos));
        return boxes.ToArray();
    }

    private bool IsEKitMode()
    {
        if (api.Side != EnumAppSide.Client)
            return false;
        var player = ((ICoreClientAPI)api).World.Player;
        return player != null && IsHoldingEKit(player);
    }

    private Cuboidf[] BodySelectionBoxes()
    {
        if (_CustomSelBoxes is { Length: > 0 })
            return _CustomSelBoxes;
        if (SelectionBoxes is { Length: > 0 })
            return SelectionBoxes;
        return [new Cuboidf(0, 0, 0, 1, 1, 1)];
    }

    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => IsEKitMode();

    public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset) => false;

    public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset)
    {
        var sel = blockSel.Clone();
        sel.Position = MachineConstructAccess.GetControllerPos(blockSel.Position, offset);
        base.OnBlockInteractStop(secondsUsed, world, byPlayer, sel);
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
