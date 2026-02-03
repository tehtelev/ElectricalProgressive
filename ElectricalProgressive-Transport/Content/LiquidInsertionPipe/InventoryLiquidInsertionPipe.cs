using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport.LiquidInsertionPipe;

    public class InventoryLiquidInsertionPipe : InventoryGeneric
    {
        private BELiquidInsertionPipe _entity;
    
        public InventoryLiquidInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BELiquidInsertionPipe entity)
            : base(slots, className, instanceID, api)
        {
            _entity = entity;
        }
    
        private static ItemSlot CreateLiquidFilterSlot(int slotId, InventoryBase inventory)
        {
            return new LiquidFilterSlot(inventory);
        }
    
        protected override ItemSlot NewSlot(int i)
        {
            return CreateLiquidFilterSlot(i, this);
        }
    }
    
    public class LiquidFilterSlot : ItemSlotWatertight
    {
        public LiquidFilterSlot(InventoryBase inventory) : base(inventory, 1000f) // Большая емкость для фильтра
        {
            // capacityLitres уже установлен в базовом конструкторе
        }
        
        public override int MaxSlotStackSize => 1; // В фильтре только 1 предмет
        
        // Переопределяем CanHold для приема только жидкостей
        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot == null || sourceSlot.Empty)
                return true; // Всегда можно очистить слот
            
            ItemStack sourceStack = sourceSlot.Itemstack;
            if (sourceStack == null || sourceStack.Collectible == null)
                return false;
            
            // 1. Проверяем через IsLiquid() - самый прямой способ
            if (sourceStack.Collectible.IsLiquid())
                return true;
            
            // 2. Проверяем, является ли это BlockLiquidContainerBase (ведра)
            if (sourceStack.Block is BlockLiquidContainerBase)
                return true;
            
            // 3. Проверяем атрибуты контейнера с жидкостью
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
                        Item contentItem = this.inventory.Api.World.GetItem(contentAsset);
                        
                        if (contentItem != null && contentItem.IsLiquid())
                        {
                            return true;
                        }
                    }
                    return true; // Даже если не смогли проверить - предполагаем что это жидкость
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
            
            // 5. Проверяем через ItemLadle (черпаки)
            if (sourceStack.Collectible.Code?.Path?.Contains("ladle") == true)
                return true;
            
            // 6. Для предметов с атрибутом "content" - предполагаем жидкость
            if (sourceStack.Attributes?.HasAttribute("content") == true)
                return true;
            
            // 7. Ни один из проверенных способов не подтвердил что это жидкость - НЕ допускаем
            return false;
        }
        
        // Переопределяем CanTake - в фильтре предметы нельзя брать обычным способом
        public override bool CanTake()
        {
            // В фильтре предметы можно брать только правым кликом для очистки
            return false;
        }
        
        // Переопределяем ActivateSlot для специальной обработки
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
        
        // Обрабатывает контейнер с жидкостью (ведро)
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
        
        // Обрабатывает предметы с contentItemCode (бутылки и т.д.)
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
            Item contentItem = world.GetItem(contentAsset);
            
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
                    Block emptiedBlock = world.GetBlock(emptiedAsset);
                    
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
        
        // Обрабатывает предметы жидкости (waterportion и т.д.)
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
        
        // Проверяет, одинаковые ли жидкости
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
        
        // Обработка правого клика для очистки слота
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
        
        // Переопределяем TryFlipWith для ограничения количества
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
        
        // Гарантируем, что в слоте не больше 1 предмета
        public override void OnItemSlotModified(ItemStack extractedStack = null)
        {
            if (!this.Empty && this.Itemstack.StackSize > 1)
            {
                this.Itemstack.StackSize = 1;
            }
            base.OnItemSlotModified(extractedStack);
        }
    }
