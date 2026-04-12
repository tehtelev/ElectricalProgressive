// InventoryLiquidInsertionPipe.cs
// ================================================
// Инвентарь для фильтрации жидкостей в трубе
// Содержит слоты, принимающие только жидкости

using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class InventoryLiquidInsertionPipe : InventoryGeneric
{
    private BELiquidInsertionPipe _entity;

    /// <summary>
    /// Конструктор инвентаря.
    /// </summary>
    /// <param name="slots">Количество слотов</param>
    /// <param name="className">Класс инвентаря</param>
    /// <param name="instanceID">Идентификатор экземпляра</param>
    /// <param name="api">API сервера</param>
    /// <param name="entity">Ссылка на блок-сущность трубы</param>
    public InventoryLiquidInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BELiquidInsertionPipe entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    /// <summary>
    /// Создает слот фильтра жидкости.
    /// </summary>
    private static ItemSlot CreateLiquidFilterSlot(int slotId, InventoryBase inventory)
    {
        return new LiquidFilterSlot(inventory);
    }

    /// <summary>
    /// Создает новый слот в инвентаре (по умолчанию - слот фильтра).
    /// </summary>
    protected override ItemSlot NewSlot(int i)
    {
        return CreateLiquidFilterSlot(i, this);
    }
}

/// <summary>
/// Слот для фильтрации жидкостей.
/// Принимает только жидкости и блоки с контейнером для жидкости.
/// </summary>
public class LiquidFilterSlot : ItemSlotWatertight
{
    /// <summary>
    /// Конструктор слота фильтра.
    /// Большая емкость (1000 литров) позволяет фильтровать большие объемы жидкостей.
    /// </summary>
    public LiquidFilterSlot(InventoryBase inventory)
        : base(inventory, 1000f) // Емкость в литрах - уже установлена в базовом конструкторе
    {
        // capacityLitres установлен в базовом конструкторе ItemSlotWatertight
    }

    /// <summary>
    /// Максимальный размер стопки в слоте.
    /// В фильтре допускается только 1 предмет на слот.
    /// </summary>
    public override int MaxSlotStackSize => 1; // В фильтре только 1 предмет

    /// <summary>
    /// Проверяет, можно ли положить предмет в этот слот.
    /// Разрешает только жидкости и блоки с контейнером для жидкости.
    /// </summary>
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.Empty)
            return true; // Всегда можно очистить пустой слот

        ItemStack sourceStack = sourceSlot.Itemstack;

        // Проверка 1: Прямая проверка через IsLiquid() - самый надежный способ
        if (sourceStack.Collectible != null && sourceStack.Collectible.IsLiquid())
            return true;

        // Проверка 2: Проверяем, является ли это BlockLiquidContainerBase (ведра)
        if (sourceStack.Block is BlockLiquidContainerBase)
            return true;

        // Проверка 3: Проверяем атрибуты контейнера с жидкостью
        if (sourceStack.ItemAttributes != null)
        {
            // Контейнеры с жидкостью имеют contentItemCode
            if (sourceStack.ItemAttributes["contentItemCode"].Exists)
            {
                string contentCode = sourceStack.ItemAttributes["contentItemCode"].AsString();
                if (!string.IsNullOrEmpty(contentCode))
                {
                    // Получаем предмет содержимого
                    AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceStack.Collectible.Code.Domain);
                    Vintagestory.API.Common.Item contentItem = this.inventory.Api.World.GetItem(contentAsset);

                    if (contentItem != null && contentItem.IsLiquid())
                    {
                        return true;
                    }
                }
                // Даже если не смогли проверить - предполагаем что это жидкость
                return true;
            }

            // Или contentItem2BlockCodes (для бутылок)
            if (sourceStack.ItemAttributes["contentItem2BlockCodes"].Exists)
                return true;

            // Проверяем атрибут containerType на "liquid" или "portion"
            if (sourceStack.ItemAttributes["containerType"].Exists)
            {
                string containerType = sourceStack.ItemAttributes["containerType"].AsString();
                if (containerType?.ToLower() == "liquid" || containerType?.ToLower() == "portion")
                    return true;
            }

            // Проверяем атрибут liquidProps
            if (sourceStack.ItemAttributes["liquidProps"].Exists)
                return true;
        }

        // Проверка 4: Проверяем через ItemLadle (черпаки)
        if (sourceStack.Collectible.Code?.Path?.Contains("ladle") == true)
            return true;

        // Проверка 5: Для предметов с атрибутом "content" - предполагаем жидкость
        if (sourceStack.Attributes?.HasAttribute("content") == true)
            return true;

        // Ни один из проверенных способов не подтвердил что это жидкость - НЕ допускаем
        return false;
    }

    /// <summary>
    /// Проверяет, можно ли взять предмет из слота.
    /// В фильтре предметы можно брать только правым кликом для очистки.
    /// </summary>
    public override bool CanTake()
    {
        // В фильтре предметы можно брать только правым кликом для очистки
        return false;
    }

    /// <summary>
    /// Обработка активации слота (левый клик).
    /// Обрабатывает перемещение жидкостей между слотами.
    /// </summary>
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (!this.Empty)
            {
                this.Itemstack = null;
                this.MarkDirty();
                op.MovedQuantity = 1;
            }
            return;
        }

        // Сначала проверяем, можно ли вообще положить этот предмет
        if (!CanHold(sourceSlot))
        {
            // Не жидкость - ничего не делаем
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }

        ItemStack sourceStack = sourceSlot.Itemstack;

        // Если пытаемся положить контейнер с жидкостью (ведро)
        if (sourceStack.Block is BlockLiquidContainerBase block)
        {
            HandleLiquidContainer(sourceSlot, block, ref op);
            return;
        }

        // Если предмет имеет contentItemCode (бутылки и т.д.)
        if (sourceStack.ItemAttributes?["contentItemCode"].Exists == true)
        {
            HandleContentItem(sourceSlot, ref op);
            return;
        }

        // Если это жидкость (portion)
        string itemCode = sourceStack.Collectible.Code?.ToString() ?? "";
        if (itemCode.ToLower().Contains("portion") || sourceStack.Collectible.IsLiquid())
        {
            HandleLiquidItem(sourceSlot, ref op);
            return;
        }

        // Для других разрешенных жидкостей - стандартная логика с ограничением количества
        if (this.Empty)
        {
            // Берем только 1 предмет
            this.Itemstack = sourceStack.Clone();
            this.Itemstack.StackSize = 1;

            if (sourceSlot.StackSize == 1)
            {
                sourceSlot.Itemstack = null;
            }
            else
            {
                sourceSlot.Itemstack.StackSize -= 1;
            }

            sourceSlot.MarkDirty();
            this.MarkDirty();

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else
        {
            // Заменяем содержимое слота
            ItemStack temp = this.Itemstack;
            this.Itemstack = sourceStack.Clone();
            this.Itemstack.StackSize = 1;

            sourceSlot.Itemstack = temp;
            if (sourceSlot.Itemstack != null)
            {
                sourceSlot.Itemstack.StackSize = Math.Min(sourceSlot.Itemstack.StackSize, sourceSlot.MaxSlotStackSize);
            }

            sourceSlot.MarkDirty();
            this.MarkDirty();

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }

    /// <summary>
    /// Обрабатывает контейнер с жидкостью (ведро).
    /// Переливает содержимое ведра в слот фильтра.
    /// </summary>
    private void HandleLiquidContainer(ItemSlot containerSlot, BlockLiquidContainerBase block, ref ItemStackMoveOperation op)
    {
        // Если слот фильтра пустой
        if (this.Empty)
        {
            // Создаем копию содержимого ведра
            ItemStack content = block.GetContent(containerSlot.Itemstack);
            if (content != null)
            {
                // Клонируем содержимое ведра
                this.Itemstack = content.Clone();
                this.Itemstack.StackSize = 1;
                this.MarkDirty();

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // Ведро пустое - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }
        }
        else
        {
            // Если в слоте уже есть что-то, заменяем
            ItemStack temp = this.Itemstack;

            ItemStack content = block.GetContent(containerSlot.Itemstack);
            if (content != null)
            {
                this.Itemstack = content.Clone();
                this.Itemstack.StackSize = 1;
            }
            else
            {
                this.Itemstack = null;
            }

            this.MarkDirty();

            // Возвращаем старую жидкость в контейнер
            if (temp != null)
            {
                // Пытаемся положить обратно
                containerSlot.Itemstack = temp;
                containerSlot.Itemstack.StackSize = 1;
                containerSlot.MarkDirty();
            }

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }

    /// <summary>
    /// Обрабатывает предметы с contentItemCode (бутылки и т.д.).
    /// Переливает содержимое бутылки в слот фильтра.
    /// </summary>
    private void HandleContentItem(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        IWorldAccessor world = this.inventory.Api.World;

        // Получаем код содержимого из атрибутов
        string contentCode = sourceSlot.Itemstack.ItemAttributes["contentItemCode"].AsString();
        if (contentCode == null)
        {
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }

        AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceSlot.Itemstack.Collectible.Code.Domain);
        Vintagestory.API.Common.Item contentItem = world.GetItem(contentAsset);

        if (contentItem == null)
        {
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }

        // Создаем стек содержимого
        ItemStack contentStack = new ItemStack(contentItem);

        if (this.Empty)
        {
            // Кладем содержимое в слот фильтра
            this.Itemstack = contentStack;
            this.Itemstack.StackSize = 1;
            this.MarkDirty();

            // Создаем пустой контейнер
            string emptiedBlockCode = sourceSlot.Itemstack.ItemAttributes["emptiedBlockCode"].AsString();
            if (emptiedBlockCode != null)
            {
                AssetLocation emptiedAsset = AssetLocation.Create(emptiedBlockCode, sourceSlot.Itemstack.Collectible.Code.Domain);
                Vintagestory.API.Common.Block emptiedBlock = world.GetBlock(emptiedAsset);

                if (emptiedBlock != null)
                {
                    ItemStack emptiedStack = new ItemStack(emptiedBlock);

                    if (sourceSlot.StackSize == 1)
                    {
                        sourceSlot.Itemstack = emptiedStack;
                    }
                    else
                    {
                        sourceSlot.Itemstack.StackSize -= 1;
                        if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(emptiedStack))
                        {
                            world.SpawnItemEntity(emptiedStack, op.ActingPlayer.Entity.Pos.XYZ);
                        }
                    }
                    sourceSlot.MarkDirty();
                }
            }

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else
        {
            // Заменяем содержимое слота
            ItemStack temp = this.Itemstack;
            this.Itemstack = contentStack;
            this.Itemstack.StackSize = 1;
            this.MarkDirty();

            // Возвращаем старую жидкость
            if (temp != null)
            {
                sourceSlot.Itemstack = temp;
                sourceSlot.Itemstack.StackSize = 1;
                sourceSlot.MarkDirty();
            }

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
    }

    /// <summary>
    /// Обрабатывает предметы жидкости (waterportion и т.д.).
    /// Переливает жидкость между слотами фильтра.
    /// </summary>
    private void HandleLiquidItem(ItemSlot liquidSlot, ref ItemStackMoveOperation op)
    {
        if (this.Empty)
        {
            // Берем 1 предмет жидкости
            this.Itemstack = liquidSlot.Itemstack.Clone();
            this.Itemstack.StackSize = 1;

            if (liquidSlot.StackSize == 1)
            {
                liquidSlot.Itemstack = null;
            }
            else
            {
                liquidSlot.Itemstack.StackSize -= 1;
            }

            liquidSlot.MarkDirty();
            this.MarkDirty();

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else if (this.Itemstack != null && liquidSlot.Itemstack != null)
        {
            // Проверяем, та же ли это жидкость
            if (AreLiquidsEqual(this.Itemstack, liquidSlot.Itemstack))
            {
                // Та же жидкость - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }
            else
            {
                // Разная жидкость - заменяем
                ItemStack temp = this.Itemstack;
                this.Itemstack = liquidSlot.Itemstack.Clone();
                this.Itemstack.StackSize = 1;

                liquidSlot.Itemstack = temp;
                if (liquidSlot.Itemstack != null)
                {
                    liquidSlot.Itemstack.StackSize = 1;
                }

                liquidSlot.MarkDirty();
                this.MarkDirty();

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
        }
    }

    /// <summary>
    /// Проверяет, одинаковые ли жидкости.
    /// </summary>
    private bool AreLiquidsEqual(ItemStack stack1, ItemStack stack2)
    {
        if (stack1 == null || stack2 == null)
            return false;

        // Сравниваем коды предметов
        if (!stack1.Collectible.Code.Equals(stack2.Collectible.Code))
            return false;

        // Для контейнеров с жидкостью сравниваем содержимое
        if (stack1.Attributes.HasAttribute("content") && stack2.Attributes.HasAttribute("content"))
        {
            string content1 = stack1.Attributes.GetString("content", "");
            string content2 = stack2.Attributes.GetString("content", "");
            return content1 == content2;
        }

        return true;
    }

    /// <summary>
    /// Обработка правого клика для очистки слота.
    /// Очищает содержимое слота, если клик с пустой рукой.
    /// </summary>
    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем правой кнопкой с пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (!this.Empty)
            {
                this.Itemstack = null;
                this.MarkDirty();
                op.MovedQuantity = 1;
            }
            return;
        }

        // Для предметов с жидкостью используем специальную логику
        if (sourceSlot.Itemstack?.Block is BlockLiquidContainerBase ||
            sourceSlot.Itemstack?.ItemAttributes?["contentItemCode"].Exists == true ||
            sourceSlot.Itemstack?.Collectible.IsLiquid() == true)
        {
            // Используем левый клик для жидкостей
            ActivateSlot(sourceSlot, ref op);
            return;
        }

        // Для не-жидкостей - ничего не делаем
        op.MovedQuantity = 0;
        op.RequestedQuantity = 0;
    }

    /// <summary>
    /// Переопределяем TryFlipWith для ограничения количества.
    /// </summary>
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        // Сначала проверяем, можно ли вообще поместить этот предмет
        if (!CanHold(itemSlot))
            return false;

        if (itemSlot != null && itemSlot.StackSize > 1)
        {
            // Если пытаются положить больше 1 предмета
            if (!this.Empty)
            {
                // Если слот не пуст, нельзя обменять
                return false;
            }

            // Берем только 1 предмет
            ItemStack singleStack = itemSlot.Itemstack.Clone();
            singleStack.StackSize = 1;
            this.Itemstack = singleStack;

            itemSlot.Itemstack.StackSize -= 1;
            if (itemSlot.Itemstack.StackSize <= 0)
                itemSlot.Itemstack = null;

            itemSlot.MarkDirty();
            this.MarkDirty();
            return true;
        }

        // Для 1 предмета - обычный обмен
        return base.TryFlipWith(itemSlot);
    }

    /// <summary>
    /// Гарантируем, что в слоте не больше 1 предмета.
    /// </summary>
    public override void OnItemSlotModified(ItemStack extractedStack = null)
    {
        if (!this.Empty && this.Itemstack.StackSize > 1)
        {
            this.Itemstack.StackSize = 1;
        }
        base.OnItemSlotModified(extractedStack);
    }
}