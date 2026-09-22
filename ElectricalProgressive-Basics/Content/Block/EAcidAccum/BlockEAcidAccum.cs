using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.EAcidAccum;

public class BlockEAcidAccum : ImmersiveWireBlock, ILiquidSink, ILiquidSource, IMultiBlockInteract, IMultiBlockColSelBoxes
{
    public float CapacityLitres => 100f;
    public bool AllowHeldLiquidTransfer => true;
    public static int ContainerSlotId => 0;
    public float TransferSizeLitres => 1f;

    public static int GetContainerSlotId(BlockPos pos) => ContainerSlotId;
    public static int GetContainerSlotId(ItemStack containerStack) => ContainerSlotId;

    public override void OnLoaded(ICoreAPI coreApi)
    {
        base.OnLoaded(coreApi);
        _skipNonCenterCollisions = true;
    }

    private BlockEntityEAcidAccum? GetBlockEntity(BlockPos pos)
    {
        return api?.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityEAcidAccum;
    }

    public float GetCurrentLitres(BlockPos pos) => GetBlockEntity(pos)?.LiquidAmount ?? 0;
    public ItemStack? GetContent(BlockPos pos) => GetBlockEntity(pos)?.LiquidSlot.Itemstack?.Clone();

    public ItemStack? TryTakeContent(BlockPos pos, int quantityItems)
    {
        var be = GetBlockEntity(pos);
        if (be == null || be.LiquidSlot.Empty)
            return null;

        var stack = be.LiquidStack;
        if (stack == null)
            return null;
        int takeAmount = Math.Min(quantityItems, stack.StackSize);
        var taken = stack.Clone();
        taken.StackSize = takeAmount;
        stack.StackSize -= takeAmount;
        if (stack.StackSize <= 0)
            be.LiquidSlot.Itemstack = null;
        be.LiquidSlot.MarkDirty();
        be.MarkDirty(true);
        return taken;
    }

    public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
    {
        var be = GetBlockEntity(pos);
        if (be == null)
            return 0;
        return be.TryPutLiquidFromStack(liquidStack, desiredLitres);
    }

    public float GetCurrentLitres(ItemStack containerStack) => 0;
    public ItemStack? GetContent(ItemStack containerStack) => null;
    public static int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => 0;
    public static ItemStack? TryTakeContent(ItemStack containerStack, int quantityItems) => null;

    ItemStack ILiquidSource.TryTakeContent(BlockPos pos, int quantityItems) => TryTakeContent(pos, quantityItems)!;
    ItemStack ILiquidSource.TryTakeContent(ItemStack containerStack, int quantityItems) => TryTakeContent(containerStack, quantityItems)!;
    int ILiquidSink.TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => TryPutLiquid(containerStack, liquidStack, desiredLitres);
    int ILiquidSink.TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres) => TryPutLiquid(pos, liquidStack, desiredLitres);

    public void SetContent(BlockPos pos, ItemStack content)
    {
        var be = GetBlockEntity(pos);
        if (be == null)
            return;
        be.LiquidStack = content;
        be.MarkDirty(true);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel is null)
            return false;
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        HandleInteract(world, byPlayer, blockSel.Position, blockSel);
        return true;
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv);
        HandleInteract(world, byPlayer, controllerPos, blockSel);
        return true;
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos, BlockSelection blockSel)
    {
        var liquidSel = blockSel.Clone();
        liquidSel.Position = controllerPos;

        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, controllerPos))
            return true;

        if (IsHoldingEKit(byPlayer))
            return true;

        var construct = world.BlockAccessor.GetBlockEntity(controllerPos)?.GetBehavior<MachineConstruct>();
        if (construct != null && construct.HasConstruction && !construct.IsReady)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressivebasics:eacidaccum-structure-incomplete"));
            }
            return true;
        }

        var active = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (active.Empty)
        {
            OpenGui(world, byPlayer, controllerPos, liquidSel);
            return true;
        }

        var attributes = active.Itemstack.Collectible.Attributes;
        if (attributes != null && attributes.IsTrue("handleLiquidContainerInteract"))
        {
            var handling = EnumHandHandling.NotHandled;
            active.Itemstack.Collectible.OnHeldInteractStart(active, byPlayer.Entity, liquidSel, null, true, ref handling);
            if (handling is EnumHandHandling.PreventDefault or EnumHandHandling.PreventDefaultAction)
                return true;
        }

        if (active.Itemstack.Collectible is not ILiquidInterface)
        {
            OpenGui(world, byPlayer, controllerPos, liquidSel);
            return true;
        }

        var collectible = active.Itemstack.Collectible;
        bool shiftKey = byPlayer.WorldData.EntityControls.ShiftKey;
        bool ctrlKey = byPlayer.WorldData.EntityControls.CtrlKey;

        if (collectible is ILiquidSource objLso && !shiftKey && objLso.AllowHeldLiquidTransfer)
        {
            var content = objLso.GetContent(active.Itemstack);
            float desiredLitres = ctrlKey ? objLso.TransferSizeLitres : objLso.CapacityLitres;
            int moved = TryPutLiquid(controllerPos, content, desiredLitres);
            if (moved > 0)
            {
                SplitStackAndPerformAction(byPlayer.Entity, active, stack =>
                {
                    objLso.TryTakeContent(stack, moved);
                    return moved;
                });
                DoLiquidMovedEffects(byPlayer, content, moved, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
                return true;
            }
        }

        if (collectible is ILiquidSink objLsi && !ctrlKey && objLsi.AllowHeldLiquidTransfer)
        {
            var own = GetContent(controllerPos);
            if (own == null)
                return true;
            var contentStack = own.Clone();
            float litres = shiftKey ? objLsi.TransferSizeLitres : objLsi.CapacityLitres;
            int num = SplitStackAndPerformAction(byPlayer.Entity, active,
                stack => objLsi.TryPutLiquid(stack, own, litres));
            if (num > 0)
            {
                TryTakeContent(controllerPos, num);
                DoLiquidMovedEffects(byPlayer, contentStack, num, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
            }
        }

        OpenGui(world, byPlayer, controllerPos, liquidSel);
        return true;
    }

    private static void OpenGui(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos, BlockSelection liquidSel)
    {
        if (world.BlockAccessor.GetBlockEntity(controllerPos) is BlockEntityOpenableContainer openable)
            openable.OnPlayerRightClick(byPlayer, liquidSel);
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
            return false;
        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public void SetContent(ItemStack containerStack, ItemStack content)
    {
        if (content == null)
        {
            containerStack.Attributes.RemoveAttribute("contents");
            return;
        }
        var tree = new TreeAttribute();
        tree["0"] = new ItemstackAttribute(content);
        containerStack.Attributes["contents"] = tree;
    }

    public WaterTightContainableProps? GetContentProps(BlockPos pos)
    {
        var stack = GetContent(pos);
        return stack == null ? null : BlockLiquidContainerBase.GetContainableProps(stack);
    }

    public WaterTightContainableProps? GetContentProps(ItemStack containerStack)
    {
        var stack = GetContent(containerStack);
        return stack == null ? null : BlockLiquidContainerBase.GetContainableProps(stack);
    }

    public bool IsFull(ItemStack containerStack) => GetCurrentLitres(containerStack) >= CapacityLitres;
    public bool IsFull(BlockPos pos) => GetCurrentLitres(pos) >= CapacityLitres;

    public int SplitStackAndPerformAction(Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
    {
        if (slot.Itemstack == null)
            return 0;
        if (slot.Itemstack.StackSize == 1)
        {
            int moved = action(slot.Itemstack);
            if (moved > 0)
            {
                (byEntity as EntityPlayer)?.WalkInventory(pslot =>
                {
                    if (pslot.Empty || pslot is ItemSlotCreative || pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize)
                        return true;
                    int mergableq = slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, pslot.Itemstack, EnumMergePriority.DirectMerge);
                    if (mergableq == 0)
                        return true;
                    slot.Itemstack.StackSize += mergableq;
                    pslot.TakeOut(mergableq);
                    slot.MarkDirty();
                    pslot.MarkDirty();
                    return false;
                });
            }
            slot.MarkDirty();
            return moved;
        }

        var clone = slot.Itemstack.Clone();
        clone.StackSize = 1;
        var dummy = new DummySlot(clone);
        int moved2 = action(dummy.Itemstack);
        if (moved2 > 0)
        {
            slot.TakeOut(1);
            if (!dummy.Empty)
                if (!byEntity.TryGiveItemStack(dummy.Itemstack))
                    api.World.SpawnItemEntity(dummy.Itemstack, byEntity.Pos.XYZ);
            slot.MarkDirty();
        }
        return moved2;
    }

    private void DoLiquidMovedEffects(IPlayer player, ItemStack contentStack, int moved, BlockLiquidContainerBase.EnumLiquidDirection dir)
    {
        if (player == null)
            return;
        var props = BlockLiquidContainerBase.GetContainableProps(contentStack);
        float litresMoved = moved / (props?.ItemsPerLitre ?? 1);
        (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        api.World.PlaySoundAt(dir == BlockLiquidContainerBase.EnumLiquidDirection.Fill
                ? props?.FillSound ?? "sounds/effect/water-fill.ogg"
                : props?.PourSound ?? "sounds/effect/water-pour.ogg",
            player.Entity, player, true, 16, GameMath.Clamp(litresMoved / 5f, 0.35f, 1f));
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
    public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset) => false;

    public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
    {
        var sel = blockSel.Clone();
        sel.Position = MachineConstructAccess.GetControllerPos(blockSel.Position, offset);
        base.OnBlockInteractStop(secondsUsed, world, byPlayer, sel);
    }

    public bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset) => true;

    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset)
    {
        var controllerPos = MachineConstructAccess.GetControllerPos(pos, offset);
        return OnPickBlock(world, controllerPos);
    }

    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, Vec3i offset)
    {
        var sel = blockSel.Clone();
        sel.Position = MachineConstructAccess.GetControllerPos(blockSel.Position, offset);
        return GetPlacedBlockInteractionHelp(world, sel, forPlayer);
    }

    public BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack, Vec3i offset) => Sounds;

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var blockCode = CodeWithVariants(new Dictionary<string, string>
        {
            { "state", Variant["state"] ?? "formed" },
            { "side", "north" }
        });
        return new ItemStack(world.BlockAccessor.GetBlock(blockCode) ?? this);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var block = inSlot.Itemstack?.Block;
        if (block == null)
            return;
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " +
                       MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " +
                       Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Power") + ": " +
                       MyMiniLib.GetAttributeFloat(block, "power", 0) + " " +
                       Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Capacity") + ": " +
                       MyMiniLib.GetAttributeInt(block, "maxcapacity", 0) + " " +
                       Lang.Get("electricalprogressivebasics:J"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:eacidaccum-tank") + ": 100 " +
                       Lang.Get("electricalprogressivebasics:litres") + " (" +
                       Lang.Get("electricalprogressivebasics:eacidaccum-acid") + ")");
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:eacidaccum-structure-hint"));
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return new WorldInteraction[]
        {
            new()
            {
                ActionLangCode = "blockhelp-barrel-fill",
                MouseButton = EnumMouseButton.Right
            }
        }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
