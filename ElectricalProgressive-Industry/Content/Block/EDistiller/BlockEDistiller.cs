using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.EDistiller;

public class BlockEDistiller : Vintagestory.API.Common.Block, ILiquidSink, ILiquidSource, IMultiBlockInteract
{
    public bool IsFormed => Variant["state"] == "formed";
    public bool IsIncomplete => Variant["state"] == "incomplete";

    public float CapacityLitres => 50f;
    public bool AllowHeldLiquidTransfer => true;
    public float TransferSizeLitres => 1f;

    private WorldInteraction[] _interactions = [];

    public override void OnLoaded(ICoreAPI coreApi)
    {
        base.OnLoaded(coreApi);
        _interactions = ObjectCacheUtil.GetOrCreate(coreApi, "BlockEDistillerInteractions", () =>
        {
            return new WorldInteraction[]
            {
                new()
                {
                    ActionLangCode = "electricalprogressivebasics:blockhelp-open-gui",
                    MouseButton = EnumMouseButton.Right
                }
            };
        });
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
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
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
        dsc.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-mash") + ": 100 L");
        dsc.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-water") + ": 50 L");
        dsc.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-product") + ": 50 L");
        dsc.AppendLine();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
    {
        if (blockSel is null)
            return false;
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        return HandleInteract(world, byPlayer, blockSel.Position, blockSel);
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv);
        return HandleInteract(world, byPlayer, controllerPos, blockSel);
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos, BlockSelection blockSel)
    {
        blockSel.Block = this;

        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, controllerPos))
            return true;

        var blockEntity = world.BlockAccessor.GetBlockEntity(controllerPos);
        if (blockEntity is null)
            return true;

        var construct = blockEntity.GetBehavior<MachineConstruct>();
        if ((construct != null && construct.HasConstruction && !construct.IsReady) ||
            (blockEntity is BlockEntityEDistiller dist && !dist.StructureComplete))
        {
            if (world.Api is ICoreClientAPI capi)
                capi.TriggerIngameError(this, "incomplete", Lang.Get("electricalprogressivebasics:structure-incomplete"));
            return true;
        }

        var liquidSel = blockSel.Clone();
        liquidSel.Position = controllerPos;
        var active = byPlayer.InventoryManager.ActiveHotbarSlot;

        if (!active.Empty)
        {
            var attributes = active.Itemstack.Collectible.Attributes;
            if (attributes != null && attributes.IsTrue("handleLiquidContainerInteract"))
            {
                var handling = EnumHandHandling.NotHandled;
                active.Itemstack.Collectible.OnHeldInteractStart(active, byPlayer.Entity, liquidSel, null, true, ref handling);
                if (handling is EnumHandHandling.PreventDefault or EnumHandHandling.PreventDefaultAction)
                    return true;
            }
        }

        if (!active.Empty && active.Itemstack.Collectible is ILiquidInterface)
        {
            var collectible = active.Itemstack.Collectible;
            var shift = byPlayer.WorldData.EntityControls.ShiftKey;
            var ctrl = byPlayer.WorldData.EntityControls.CtrlKey;

            if (collectible is ILiquidSource src && !shift)
            {
                if (!src.AllowHeldLiquidTransfer)
                    return false;
                var content = src.GetContent(active.Itemstack);
                var desired = ctrl ? src.TransferSizeLitres : src.CapacityLitres;
                var moved = TryPutLiquid(controllerPos, content, desired);
                if (moved > 0)
                {
                    SplitStackAndPerformAction(byPlayer.Entity, active, stack =>
                    {
                        src.TryTakeContent(stack, moved);
                        return moved;
                    });
                    DoLiquidMovedEffects(byPlayer, content, moved, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
                    return true;
                }
            }

            if (collectible is ILiquidSink sink && !ctrl)
            {
                if (!sink.AllowHeldLiquidTransfer)
                    return false;
                var own = GetContent(controllerPos);
                if (own == null)
                    return true;
                var clone = own.Clone();
                var litres = shift ? sink.TransferSizeLitres : sink.CapacityLitres;
                var num = SplitStackAndPerformAction(byPlayer.Entity, active,
                    stack => sink.TryPutLiquid(stack, own, litres));
                if (num > 0)
                {
                    TryTakeContent(controllerPos, num);
                    DoLiquidMovedEffects(byPlayer, clone, num, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
                    return true;
                }
            }
        }

        if (blockEntity is BlockEntityOpenableContainer openable)
            openable.OnPlayerRightClick(byPlayer, liquidSel);

        return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var beh = MachineConstructAccess.GetBehavior(world, selection.Position);
        if (beh != null && beh.HasConstruction && !beh.IsReady)
        {
            var help = beh.GetInteractionHelp(world, forPlayer);
            if (help is { Length: > 0 })
                return help;
        }

        return _interactions;
    }

    private BlockEntityEDistiller? GetBe(BlockPos pos)
        => api?.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityEDistiller;

    public float GetCurrentLitres(BlockPos pos) => GetBe(pos)?.ProductLitres ?? 0;
    public float GetCurrentLitres(ItemStack containerStack) => 0;
    public ItemStack GetContent(BlockPos pos) => GetBe(pos)?.ProductSlot.Itemstack?.Clone();
    public ItemStack GetContent(ItemStack containerStack) => null!;

    public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
    {
        var be = GetBe(pos);
        if (be == null || liquidStack == null)
            return 0;
        var isWater = liquidStack.Collectible.Code.Path.Contains("water");
        return be.TryPutIntoTank(liquidStack, desiredLitres, isWater);
    }

    public int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => 0;

    public ItemStack TryTakeContent(BlockPos pos, int quantityItems)
        => GetBe(pos)?.TryTakeFromProduct(quantityItems);

    public ItemStack TryTakeContent(ItemStack containerStack, int quantityItems) => null!;

    public void SetContent(ItemStack containerStack, ItemStack content)
    {
    }

    public void SetContent(BlockPos pos, ItemStack content)
    {
        var be = GetBe(pos);
        if (be == null)
            return;
        if (content == null)
        {
            be.ProductSlot.Itemstack = null;
            be.ProductSlot.MarkDirty();
            be.MarkDirty();
            return;
        }

        var props = BlockLiquidContainerBase.GetContainableProps(content);
        var litres = props != null && props.ItemsPerLitre > 0
            ? content.StackSize / props.ItemsPerLitre
            : 0f;
        var isWater = content.Collectible.Code.Path.Contains("water");
        be.TryPutIntoTank(content, litres, isWater);
    }

    public bool IsFull(ItemStack containerStack) => false;

    public bool IsFull(BlockPos pos)
    {
        var be = GetBe(pos);
        if (be == null)
            return true;
        return be.MashLitres >= be.MashCapacity && be.WaterLitres >= be.WaterCapacity;
    }

    public WaterTightContainableProps GetContentProps(ItemStack containerStack) => null!;

    public WaterTightContainableProps GetContentProps(BlockPos pos)
    {
        var stack = GetContent(pos);
        return stack == null ? null! : BlockLiquidContainerBase.GetContainableProps(stack);
    }

    int ILiquidSink.TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres)
        => TryPutLiquid(containerStack, liquidStack, desiredLitres);

    int ILiquidSink.TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
        => TryPutLiquid(pos, liquidStack, desiredLitres);

    ItemStack ILiquidSource.TryTakeContent(BlockPos pos, int quantityItems) => TryTakeContent(pos, quantityItems);
    ItemStack ILiquidSource.TryTakeContent(ItemStack containerStack, int quantityItems)
        => TryTakeContent(containerStack, quantityItems);

    private void DoLiquidMovedEffects(IPlayer player, ItemStack contentStack, int moved,
        BlockLiquidContainerBase.EnumLiquidDirection dir)
    {
        if (player == null)
            return;
        var props = BlockLiquidContainerBase.GetContainableProps(contentStack);
        var litresMoved = moved / (props?.ItemsPerLitre ?? 1);
        (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        api.World.PlaySoundAt(
            dir == BlockLiquidContainerBase.EnumLiquidDirection.Fill
                ? props?.FillSound ?? "sounds/effect/water-fill.ogg"
                : props?.PourSound ?? "sounds/effect/water-pour.ogg",
            player.Entity, player, true, 16, GameMath.Clamp(litresMoved / 5f, 0.35f, 1f));
    }

    public int SplitStackAndPerformAction(Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
    {
        if (slot.Itemstack == null)
            return 0;
        if (slot.Itemstack.StackSize == 1)
        {
            var moved = action(slot.Itemstack);
            if (moved > 0)
            {
                (byEntity as EntityPlayer)?.WalkInventory(pslot =>
                {
                    if (pslot.Empty || pslot is ItemSlotCreative ||
                        pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize)
                        return true;
                    var mergableq = slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, pslot.Itemstack,
                        EnumMergePriority.DirectMerge);
                    if (mergableq == 0)
                        return true;
                    slot.Itemstack.StackSize += mergableq;
                    pslot.TakeOut(mergableq);
                    slot.MarkDirty();
                    pslot.MarkDirty();
                    return true;
                });
            }

            return moved;
        }

        var containerStack = slot.Itemstack.Clone();
        containerStack.StackSize = 1;
        var movedSplit = action(containerStack);
        if (movedSplit > 0)
        {
            slot.TakeOut(1);
            if ((byEntity as EntityPlayer)?.Player.InventoryManager.TryGiveItemstack(containerStack, true) != true)
                api.World.SpawnItemEntity(containerStack, byEntity.Pos.XYZ);
            slot.MarkDirty();
        }

        return movedSplit;
    }

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
        Vec3i offset) => Sounds;
}
