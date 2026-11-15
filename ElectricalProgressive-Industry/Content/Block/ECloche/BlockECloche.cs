using System;
using ElectricalProgressive.Construction;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ECloche;

public class BlockECloche : BlockEBase, ILiquidSink, ILiquidSource, IMultiBlockInteract
{
    public float CapacityLitres => MyCapacity();
    public bool AllowHeldLiquidTransfer => true;
    public float TransferSizeLitres => 1f;

    private WorldInteraction[] _interactions = [];

    public override byte[] GetLightHsv(IBlockAccessor blockAccess, BlockPos pos, ItemStack stack)
    {
        if (stack != null || pos == null)
            return [36, 4, 3];

        try
        {
            if (blockAccess?.GetBlockEntity(pos) is BlockEntityECloche { LampOn: true })
                return [36, 7, 8];
        }
        catch (NullReferenceException)
        {
        }

        return [36, 4, 3];
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _interactions = ObjectCacheUtil.GetOrCreate(api, "BlockEClocheInteractions", () => new WorldInteraction[]
        {
            new()
            {
                ActionLangCode = "blockhelp-behavior-rightclickpickup",
                MouseButton = EnumMouseButton.Right
            },
            new()
            {
                ActionLangCode = "game:blockhelp-bucket-rightclick",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = [new ItemStack(api.World.GetBlock(new AssetLocation("game", "woodbucket")))]
            }
        });
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        return HandleInteract(world, byPlayer, blockSel, MachineConstructAccess.GetControllerPos(world, blockSel.Position));
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        return HandleInteract(world, byPlayer, blockSel, MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv));
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockPos controllerPos)
    {
        var liquidSel = blockSel.Clone();
        liquidSel.Position = controllerPos;
        var active = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (active is { Empty: false })
        {
            var attributes = active.Itemstack.Collectible.Attributes;
            if (attributes != null && attributes.IsTrue("handleLiquidContainerInteract"))
            {
                var handling = EnumHandHandling.NotHandled;
                active.Itemstack.Collectible.OnHeldInteractStart(active, byPlayer.Entity, liquidSel, null, true, ref handling);
                if (handling is EnumHandHandling.PreventDefault or EnumHandHandling.PreventDefaultAction)
                    return true;
            }

            if (active.Itemstack.Collectible is ILiquidInterface)
            {
                var collectible = active.Itemstack.Collectible;
                bool shiftKey = byPlayer.WorldData.EntityControls.ShiftKey;
                bool ctrlKey = byPlayer.WorldData.EntityControls.CtrlKey;

                if (collectible is ILiquidSource source && !shiftKey && source.AllowHeldLiquidTransfer)
                {
                    var content = source.GetContent(active.Itemstack);
                    float desired = ctrlKey ? source.TransferSizeLitres : source.CapacityLitres;
                    int moved = TryPutLiquid(controllerPos, content, desired);
                    if (moved > 0)
                    {
                        SplitStackAndPerformAction(byPlayer.Entity, active, stack =>
                        {
                            source.TryTakeContent(stack, moved);
                            return moved;
                        });
                        DoLiquidMovedEffects(byPlayer, content, moved, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
                        return true;
                    }
                }

                if (collectible is ILiquidSink sink && !ctrlKey && sink.AllowHeldLiquidTransfer)
                {
                    var own = GetContent(controllerPos);
                    if (own != null)
                    {
                        var clone = own.Clone();
                        float litres = shiftKey ? sink.TransferSizeLitres : sink.CapacityLitres;
                        int num = SplitStackAndPerformAction(byPlayer.Entity, active,
                            stack => sink.TryPutLiquid(stack, own, litres));
                        if (num > 0)
                        {
                            TryTakeContent(controllerPos, num);
                            DoLiquidMovedEffects(byPlayer, clone, num, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
                            return true;
                        }
                    }
                }
            }
        }

        if (world.BlockAccessor.GetBlockEntity(controllerPos) is BlockEntityECloche cloche)
            cloche.OnPlayerRightClick(byPlayer, liquidSel);

        return true;
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

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public float GetCurrentLitres(BlockPos pos) => Entity(pos)?.Litres ?? 0f;

    public float GetCurrentLitres(ItemStack containerStack) => 0f;

    public bool IsFull(BlockPos pos) => Entity(pos)?.WaterFull ?? false;

    public bool IsFull(ItemStack containerStack) => true;

    public ItemStack GetContent(BlockPos pos) => Entity(pos)?.WaterContent();

    public ItemStack GetContent(ItemStack containerStack) => null;

    public WaterTightContainableProps GetContentProps(BlockPos pos) => Entity(pos)?.WaterProps();

    public WaterTightContainableProps GetContentProps(ItemStack containerStack) => null;

    public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
    {
        return Entity(pos)?.TryPutWater(liquidStack, desiredLitres) ?? 0;
    }

    public int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => 0;

    public ItemStack TryTakeContent(BlockPos pos, int quantityItems)
    {
        return Entity(pos)?.TryTakeWater(quantityItems);
    }

    public ItemStack TryTakeContent(ItemStack containerStack, int quantityItems) => null;

    public void SetContent(BlockPos pos, ItemStack content)
    {
        Entity(pos)?.SetWater(content);
    }

    public void SetContent(ItemStack containerStack, ItemStack content)
    {
    }

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
        if (player == null || contentStack == null)
            return;

        var props = BlockLiquidContainerBase.GetContainableProps(contentStack);
        float litresMoved = moved / (props?.ItemsPerLitre ?? 1);
        (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        api.World.PlaySoundAt(dir == BlockLiquidContainerBase.EnumLiquidDirection.Fill
                ? props?.FillSound ?? "sounds/effect/water-fill.ogg"
                : props?.PourSound ?? "sounds/effect/water-pour.ogg",
            player.Entity, player, true, 16, GameMath.Clamp(litresMoved / 5f, 0.35f, 1f));
    }

    private BlockEntityECloche Entity(BlockPos pos)
    {
        if (api?.World == null)
            return null;

        var controllerPos = MachineConstructAccess.GetControllerPos(api.World, pos);
        return api.World.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityECloche;
    }

    private float MyCapacity()
    {
        if (Attributes?["capacityLitres"].Exists == true)
            return Attributes["capacityLitres"].AsFloat(10f);

        return 10f;
    }
}
