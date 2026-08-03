using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.ECrusher;

/// <summary>
/// IMultiBlockInteract — сборка/GUI с любого dummy multiblock, не только с контроллера.
/// </summary>
public class BlockECrusher : Vintagestory.API.Common.Block, IMultiBlockInteract
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

        var blockEntity = world.BlockAccessor.GetBlockEntity(controllerPos);
        if (blockEntity is null)
            return true;

        var construct = blockEntity.GetBehavior<MachineConstruct>();
        if (construct != null && construct.HasConstruction && !construct.IsReady)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressiveindustry:ecrusher-structure-incomplete"));
            }

            return true;
        }

        if (blockEntity is BlockEntityECrusher crusher && !crusher.StructureComplete)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressiveindustry:ecrusher-structure-incomplete"));
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
        // В GUI/инвентаре — formed-модель (shapeInventory), в мир — всегда incomplete-контроллер
        if (itemstack?.Block != null)
        {
            var side = itemstack.Block.Variant.ContainsKey("side")
                ? itemstack.Block.Variant["side"]
                : (Variant.ContainsKey("side") ? Variant["side"] : "north");

            if (itemstack.Block.Variant.ContainsKey("state") &&
                itemstack.Block.Variant["state"] != "incomplete")
            {
                var incomplete = world.GetBlock(CodeWithVariants(["state", "side"],
                    ["incomplete", side]));
                if (incomplete != null)
                    itemstack = new ItemStack(incomplete);
            }
        }

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
        dsc.AppendLine(Lang.Get("electricalprogressiveindustry:ecrusher-structure-hint"));
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
