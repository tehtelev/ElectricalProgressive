using ElectricalProgressive.Content.Block;
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

namespace ElectricalProgressive.Content.EAquaAccum;

/// <summary>
/// Блок электрической помпы для воды.
/// Реализует интерфейсы для работы с жидкостями (ILiquidSink, ILiquidSource).
/// На основе BlockEFruitPress, но с одним слотом для воды.
/// </summary>
public class BlockEAquaAccum : BlockEBase, ILiquidSink, ILiquidSource
{
    // === Параметры контейнера для жидкости ===
    public float CapacityLitres => 100f;
    public bool AllowHeldLiquidTransfer => true;
    public static int ContainerSlotId => 0;
    public float TransferSizeLitres => 1f;

    // === Реализация интерфейса ILiquidSource/ILiquidSink для BlockPos ===

    /// <summary>
    /// Получить ID слота для контейнера по позиции блока.
    /// </summary>
    public static int GetContainerSlotId(BlockPos pos) => ContainerSlotId;

    /// <summary>
    /// Получить ID слота для контейнера по ItemStack.
    /// </summary>
    public static int GetContainerSlotId(ItemStack containerStack) => ContainerSlotId;

    /// <summary>
    /// Получить текущее количество жидкости в блоке.
    /// </summary>
    public float GetCurrentLitres(BlockPos pos)
    {
        var be = GetBlockEntity(pos);
        return be?.LiquidAmount ?? 0;
    }

    /// <summary>
    /// Получить содержимое контейнера.
    /// </summary>
    public ItemStack GetContent(BlockPos pos)
    {
        var be = GetBlockEntity(pos);
        return be?.LiquidSlot.Itemstack?.Clone();
    }

    /// <summary>
    /// Взять жидкость из контейнера.
    /// </summary>
    public ItemStack TryTakeContent(BlockPos pos, int quantityItems)
    {
        var be = GetBlockEntity(pos);
        if (be == null || be.LiquidSlot.Empty) return null;

        ItemStack stack = be.LiquidStack;
        int takeAmount = Math.Min(quantityItems, stack.StackSize);
        ItemStack takenStack = stack.Clone();
        takenStack.StackSize = takeAmount;

        stack.StackSize -= takeAmount;
        if (stack.StackSize <= 0) be.LiquidSlot.Itemstack = null;

        be.LiquidSlot.MarkDirty();
        be.MarkDirty(true);
        return takenStack;
    }

    /// <summary>
    /// Положить жидкость в контейнер.
    /// </summary>
    public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
    {
        var be = GetBlockEntity(pos);
        if (be == null) return 0;

        return be.TryPutLiquidFromStack(liquidStack, desiredLitres);
    }

    // === Реализация интерфейса ILiquidSource/ILiquidSink для ItemStack (упрощенная) ===

    /// <summary>
    /// Получить текущее количество жидкости (для помпы в руке всегда 0).
    /// </summary>
    public float GetCurrentLitres(ItemStack containerStack) => 0;

    /// <summary>
    /// Получить содержимое (для помпы в руке всегда null).
    /// </summary>
    public ItemStack GetContent(ItemStack containerStack) => null;

    /// <summary>
    /// Положить жидкость (для помпы в руке нельзя).
    /// </summary>
    public static int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => 0;

    /// <summary>
    /// Взять жидкость (для помпы в руке нельзя).
    /// </summary>
    public static ItemStack TryTakeContent(ItemStack containerStack, int quantityItems) => null;

    // === Явные реализации интерфейсов ===

    ItemStack ILiquidSource.TryTakeContent(BlockPos pos, int quantityItems) => TryTakeContent(pos, quantityItems);
    ItemStack ILiquidSource.TryTakeContent(ItemStack containerStack, int quantityItems) => TryTakeContent(containerStack, quantityItems);
    int ILiquidSink.TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres) => TryPutLiquid(containerStack, liquidStack, desiredLitres);
    int ILiquidSink.TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres) => TryPutLiquid(pos, liquidStack, desiredLitres);

    // === Вспомогательные методы ===

    /// <summary>
    /// Получить BlockEntity помпы по позиции.
    /// </summary>
    private BlockEntityEAquaAccum GetBlockEntity(BlockPos pos)
    {
        return api?.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityEAquaAccum;
    }

    public virtual void SetContents(ItemStack containerStack, ItemStack[] stacks)
    {
        if (stacks == null || stacks.Length == 0 || ((IEnumerable<ItemStack>)stacks).All<ItemStack>((System.Func<ItemStack, bool>)(x => x == null)))
        {
            containerStack.Attributes.RemoveAttribute("contents");
        }
        else
        {
            TreeAttribute treeAttribute = new TreeAttribute();
            for (int index = 0; index < stacks.Length; ++index)
                treeAttribute[index.ToString() ?? ""] = (IAttribute)new ItemstackAttribute(stacks[index]);
            containerStack.Attributes["contents"] = (IAttribute)treeAttribute;
        }
    }

    /// <summary>
    /// Sets the containers contents to given stack.
    /// </summary>
    /// <param name="containerStack"></param>
    /// <param name="content"></param>
    public void SetContent(ItemStack containerStack, ItemStack content)
    {
        if (content == null)
        {
            SetContents(containerStack, null);
            return;
        }
        SetContents(containerStack, [content]);
    }

    /// <summary>
    /// Установить содержимое контейнера по позиции блока.
    /// </summary>
    public void SetContent(BlockPos pos, ItemStack content)
    {
        var be = api?.World?.BlockAccessor.GetBlockEntity(pos) as BlockEntityContainer;
        if (be == null) return;

        if (content == null)
        {
            be.Inventory[GetContainerSlotId(pos)].Itemstack = null;
        }
        else
        {
            var dummySlot = new DummySlot(content);
            dummySlot.TryPutInto(api.World, be.Inventory[GetContainerSlotId(pos)], content.StackSize);
        }

        be.Inventory[GetContainerSlotId(pos)].MarkDirty();
        be.MarkDirty(true);
    }

    /// <summary>
    /// Воспроизведение звуковых эффектов при работе с жидкостью.
    /// </summary>
    private void DoLiquidMovedEffects(IPlayer player, ItemStack contentStack, int moved, BlockLiquidContainerBase.EnumLiquidDirection dir)
    {
        if (player == null) return;

        WaterTightContainableProps props = GetContainableProps(contentStack);
        float litresMoved = moved / (props?.ItemsPerLitre ?? 1);

        (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        api.World.PlaySoundAt(dir == BlockLiquidContainerBase.EnumLiquidDirection.Fill ?
            props?.FillSound ?? "sounds/effect/water-fill.ogg" :
            props?.PourSound ?? "sounds/effect/water-pour.ogg",
            player.Entity, player, true, 16, GameMath.Clamp(litresMoved / 5f, 0.35f, 1f));
        api.World.SpawnCubeParticles(player.Entity.Pos.AheadCopy(0.25).XYZ.Add(0, player.Entity.SelectionBox.Y2 / 2, 0),
            contentStack, 0.75f, (int)litresMoved * 2, 0.45f);
    }

    /// <summary>
    /// Разделение стека и выполнение действия.
    /// </summary>
    public int SplitStackAndPerformAction(Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
    {
        if (slot.Itemstack == null) return 0;
        if (slot.Itemstack.StackSize == 1)
        {
            int moved = action(slot.Itemstack);

            if (moved > 0)
            {
                // Автоматическое объединение с другими стеками в инвентаре
                (byEntity as EntityPlayer)?.WalkInventory((pslot =>
                {
                    if (pslot.Empty || pslot is ItemSlotCreative || pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize)
                        return true;

                    int mergableq = slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, pslot.Itemstack, EnumMergePriority.DirectMerge);
                    if (mergableq == 0) return true;

                    var selfLiqBlock = slot.Itemstack.Collectible as BlockLiquidContainerBase;
                    var invLiqBlock = pslot.Itemstack.Collectible as BlockLiquidContainerBase;

                    if ((selfLiqBlock?.GetContent(slot.Itemstack)?.StackSize ?? 0) != (invLiqBlock?.GetContent(pslot.Itemstack)?.StackSize ?? 0))
                        return true;

                    slot.Itemstack.StackSize += mergableq;
                    pslot.TakeOut(mergableq);

                    slot.MarkDirty();
                    pslot.MarkDirty();
                    return true;
                }));
            }

            return moved;
        }
        else
        {
            // Разделение стека перед выполнением действия
            ItemStack containerStack = slot.Itemstack.Clone();
            containerStack.StackSize = 1;

            int moved = action(containerStack);

            if (moved > 0)
            {
                slot.TakeOut(1);
                if ((byEntity as EntityPlayer)?.Player.InventoryManager.TryGiveItemstack(containerStack, true) != true)
                {
                    api.World.SpawnItemEntity(containerStack, byEntity.Pos.XYZ);
                }

                slot.MarkDirty();
            }

            return moved;
        }
    }

    /// <summary>
    /// Получить свойства жидкости из ItemStack.
    /// </summary>
    public static WaterTightContainableProps? GetContainableProps(ItemStack? stack)
    {
        try
        {
            JsonObject obj = stack?.ItemAttributes?["waterTightContainerProps"];
            if (obj != null && obj.Exists)
                return obj.AsObject<WaterTightContainableProps>(null, stack.Collectible.Code.Domain);
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Получить свойства содержимого контейнера по позиции блока.
    /// </summary>
    public WaterTightContainableProps? GetContentProps(BlockPos pos)
    {
        BlockEntityContainer becontainer = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityContainer;
        if (becontainer == null) return null;

        int slotid = GetContainerSlotId(pos);
        if (slotid >= becontainer.Inventory.Count) return null;

        ItemStack stack = becontainer.Inventory[slotid]?.Itemstack;
        if (stack == null) return null;

        return GetContainableProps(stack);
    }

    /// <summary>
    /// Получить свойства содержимого контейнера по ItemStack.
    /// </summary>
    public WaterTightContainableProps? GetContentProps(ItemStack containerStack)
    {
        ItemStack? stack = GetContent(containerStack);
        return GetContainableProps(stack);
    }

    /// <summary>
    /// Проверить, полон ли контейнер в ItemStack.
    /// </summary>
    public bool IsFull(ItemStack containerStack)
    {
        return GetCurrentLitres(containerStack) >= CapacityLitres;
    }

    /// <summary>
    /// Проверить, полон ли контейнер по позиции блока.
    /// </summary>
    public bool IsFull(BlockPos pos)
    {
        return GetCurrentLitres(pos) >= CapacityLitres;
    }

    // === Основные методы блока ===

    /// <summary>
    /// Попытка разместить блок в мире.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
       BlockSelection blockSel, ref string failureCode)
    {
        var selection = new Selection(blockSel);
        var facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        // Проверка возможности размещения на соседнем блоке
        if (FacingHelper.Faces(facing).First() is { } blockFacing &&
            !world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index])
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    /// <summary>
    /// Размещение блока в мире.
    /// </summary>
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        if (byItemStack.Block.Variant["type"] == "burned")
            return false;

        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEAquaAccum entity)
        {
            return false;
        }

        LoadEProperties.Load(this, entity);
        return true;
    }

    /// <summary>
    /// Обработка взаимодействия с блоком (ЖИДКОСТИ И ИНВЕНТАРЬ).
    /// </summary>
    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        ItemSlot activeHotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

        // ПЕРВОЕ: Проверяем, есть ли в руке контейнер с жидкостью
        if (!activeHotbarSlot.Empty)
        {
            JsonObject attributes = activeHotbarSlot.Itemstack.Collectible.Attributes;
            if ((attributes != null ? (attributes.IsTrue("handleLiquidContainerInteract") ? 1 : 0) : 0) != 0)
            {
                EnumHandHandling handling = EnumHandHandling.NotHandled;
                activeHotbarSlot.Itemstack.Collectible.OnHeldInteractStart(activeHotbarSlot,
                    (EntityAgent)byPlayer.Entity, blockSel, (EntitySelection)null, true, ref handling);
                if (handling == EnumHandHandling.PreventDefault || handling == EnumHandHandling.PreventDefaultAction)
                    return true;
            }
        }

        // ВТОРОЕ: Проверяем, есть ли в руке предмет с интерфейсом жидкости
        if (!activeHotbarSlot.Empty && activeHotbarSlot.Itemstack.Collectible is ILiquidInterface)
        {
            CollectibleObject collectible = activeHotbarSlot.Itemstack.Collectible;
            bool shiftKey = byPlayer.WorldData.EntityControls.ShiftKey;
            bool ctrlKey = byPlayer.WorldData.EntityControls.CtrlKey;
            ILiquidSource objLso = collectible as ILiquidSource;

            // Наливание жидкости из контейнера в руке в блок
            if (objLso != null && !shiftKey)
            {
                if (!objLso.AllowHeldLiquidTransfer)
                    return false;
                ItemStack content = objLso.GetContent(activeHotbarSlot.Itemstack);
                float desiredLitres = ctrlKey ? objLso.TransferSizeLitres : objLso.CapacityLitres;
                int moved = this.TryPutLiquid(blockSel.Position, content, desiredLitres);
                if (moved > 0)
                {
                    this.SplitStackAndPerformAction((Entity)byPlayer.Entity, activeHotbarSlot,
                        (System.Func<ItemStack, int>)(stack =>
                        {
                            objLso.TryTakeContent(stack, moved);
                            return moved;
                        }));
                    this.DoLiquidMovedEffects(byPlayer, content, moved,
                        BlockLiquidContainerBase.EnumLiquidDirection.Pour);
                    return true;
                }
            }

            // Сливание жидкости из блока в контейнер в руке
            ILiquidSink objLsi = collectible as ILiquidSink;
            if (objLsi != null && !ctrlKey)
            {
                if (!objLsi.AllowHeldLiquidTransfer)
                    return false;
                ItemStack owncontentStack = this.GetContent(blockSel.Position);
                if (owncontentStack == null)
                    return base.OnBlockInteractStart(world, byPlayer, blockSel);
                ItemStack contentStack = owncontentStack.Clone();
                float litres = shiftKey ? objLsi.TransferSizeLitres : objLsi.CapacityLitres;
                int num = this.SplitStackAndPerformAction((Entity)byPlayer.Entity, activeHotbarSlot,
                    (System.Func<ItemStack, int>)(stack => objLsi.TryPutLiquid(stack, owncontentStack, litres)));
                if (num > 0)
                {
                    this.TryTakeContent(blockSel.Position, num);
                    this.DoLiquidMovedEffects(byPlayer, contentStack, num,
                        BlockLiquidContainerBase.EnumLiquidDirection.Fill);
                    return true;
                }
            }
        }

        // ТРЕТЬЕ: Если не работали с жидкостями, открываем инвентарь
        var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (blockEntity != null && blockEntity is BlockEntityOpenableContainer openableContainer)
        {
            // Проверяем права доступа
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
                return false;

            openableContainer.OnPlayerRightClick(byPlayer, blockSel);
            return true;
        }

        // Если ничего не сработало, вызываем базовый метод
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    /// <summary>
    /// Получить дроп при разрушении блока.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    /// <summary>
    /// Получить подсказки по взаимодействию с блоком.
    /// </summary>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return new WorldInteraction[]
        {
            new()
            {
                ActionLangCode = "blockhelp-watergen-fillliquid",
                MouseButton = EnumMouseButton.Right
            }
        }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    /// <summary>
    /// Получить информацию о предмете в руке.
    /// </summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + (MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        dsc.AppendLine(Lang.Get("Pump height") + ": 10 blocks");
        dsc.AppendLine(Lang.Get("Water check area") + ": 5x5x5 blocks");
        dsc.AppendLine(Lang.Get("Required water") + ": 50%");
        dsc.AppendLine(Lang.Get("Liquid capacity") + ": 100 L");
    }
}