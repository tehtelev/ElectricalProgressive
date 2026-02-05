using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFruitPress;

public class BlockEntityEFruitPress : BlockEntityGenericTypedContainer
{
    private InventoryEFruitPress _inventory;
    private GuiDialogEFruitPress _clientDialog;
    public override string InventoryClassName => "efruitpress";
    
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasPressingLastTick;
    
    public float SqueezeProgress { get; private set; } // 0-1 (0-100%)
    private double _totalJuiceAvailable = 0; // Сколько всего литров сока можно получить из текущего стака
    private double _pressingProgress = 0; // Сколько литров уже отжато
    private ItemStack _currentJuiceStack;
    
    private ILoadedSound _pressSound;
    private AssetLocation _pressSoundLocation = new AssetLocation("sounds/player/wetclothsqueeze.ogg");
    
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    public BEBehaviorEFruitPress PowerBehavior => GetBehavior<BEBehaviorEFruitPress>();
    
    public Facing Facing
    {
        get => this._facing;
        set
        {
            if (value != this._facing)
            {
                this.ElectricalProgressive!.Connection =
                    FacingHelper.FullFace(this._facing = value);
            }
        }
    }
    
    private Facing _facing = Facing.None;
    
    // === СВОЙСТВА ЖИДКОСТИ ===
    
    public float LiquidAmount 
    { 
        get 
        {
            if (_inventory.LiquidSlot.Empty) return 0f;
            
            var props = BlockLiquidContainerBase.GetContainableProps(_inventory.LiquidSlot.Itemstack);
            if (props == null) return 0f;
            
            return (float)_inventory.LiquidSlot.Itemstack.StackSize / props.ItemsPerLitre;
        }
    }
    
    public float LiquidCapacity 
    { 
        get => 100f; // Фиксированная емкость бака для сока
    }
    
    public ItemSlot LiquidSlot => _inventory.LiquidSlot;
    public ItemSlot FruitSlot => _inventory.FruitSlot;
    public ItemSlot MashSlot => _inventory.MashSlot;
    
    public ItemStack LiquidStack
    {
        get => _inventory.LiquidSlot.Itemstack;
        set
        {
            _inventory.LiquidSlot.Itemstack = value;
            _inventory.LiquidSlot.MarkDirty();
        }
    }
    
    public BlockEntityEFruitPress()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
        
        // Используем кастомный инвентарь
        this._inventory = new InventoryEFruitPress();
        
        this._inventory.SlotModified += OnSlotModified;
    }
    
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        
        // Правильная инициализация инвентаря
        this._inventory.LateInitialize("efruitpress-" + Pos, api);
        (_inventory as InventoryEFruitPress)?.SetBlockPos(Pos); // Устанавливаем позицию
        
        this.RegisterGameTickListener(UpdatePress, 50); // 20 раз в секунду
        
        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (AnimUtil != null)
            {
                AnimUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }
        }
    }
    
    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }
    
    private void OnSlotModified(int slotid)
    {
        if (slotid == 0) // Фрукты изменились
        {
            UpdateJuiceableProperties();
        }
        else if (slotid == 1) // Жидкость изменилась (выкачали или налили)
        {
            // При изменении жидкости в баке нужно перепроверить совместимость
            if (FruitSlot.Empty)
            {
                // Если фруктов нет, сбрасываем все
                _totalJuiceAvailable = 0;
                _currentJuiceStack = null;
                _pressingProgress = 0;
                SqueezeProgress = 0;
                StopAnimation();
            }
            else if (_currentJuiceStack != null)
            {
                // Если есть фрукты, проверяем совместимость
                if (!IsJuiceCompatible())
                {
                    // Жидкости несовместимы - сбрасываем прогресс
                    _totalJuiceAvailable = 0;
                    _pressingProgress = 0;
                    SqueezeProgress = 0;
                    StopAnimation();
                }
                else
                {
                    // Если жидкости совместимы или бак пустой, пересчитываем доступный сок
                    UpdateTotalJuiceAvailable();
                    
                    // Если бак пустой после выкачивания, можно продолжить отжим
                    if (LiquidSlot.Empty)
                    {
                        // Перезапускаем анимацию если есть питание
                        var hasPower = PowerBehavior?.PowerSetting >= _maxConsumption * 0.1f;
                        if (hasPower && _totalJuiceAvailable > 0)
                        {
                            StartAnimation();
                        }
                    }
                }
            }
        }
        
        this.MarkDirty();
        
        if (this.Api is ICoreClientAPI)
        {
            UpdateGui();
        }
    }
    
    // Новый метод для пересчета доступного сока:
    private void UpdateTotalJuiceAvailable()
    {
        if (FruitSlot.Empty)
        {
            _totalJuiceAvailable = 0;
            return;
        }
        
        var itemStack = FruitSlot.Itemstack;
        var props = GetJuiceableProperties(itemStack);
        if (props != null && props.LitresPerItem.HasValue)
        {
            // Рассчитываем сколько всего литров сока можно получить
            _totalJuiceAvailable = itemStack.StackSize * props.LitresPerItem.Value;
        }
        else
        {
            _totalJuiceAvailable = 0;
        }
    }
    
    // Новый метод для обновления GUI
    public void UpdateGui()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(SqueezeProgress, LiquidAmount, LiquidCapacity);
        }
    }
    
    private void UpdateJuiceableProperties()
    {
        if (FruitSlot.Empty)
        {
            _totalJuiceAvailable = 0;
            _currentJuiceStack = null; // Очищаем ссылку на сок
            _pressingProgress = 0;
            SqueezeProgress = 0;
            StopAnimation();
            return;
        }
        
        var itemStack = FruitSlot.Itemstack;
        var props = GetJuiceableProperties(itemStack);
        if (props != null && props.LitresPerItem.HasValue)
        {
            props.LiquidStack?.Resolve(Api.World, "juiceable properties liquidstack", itemStack.Collectible.Code);
            
            if (props.LiquidStack?.ResolvedItemstack != null)
            {
                _currentJuiceStack = props.LiquidStack.ResolvedItemstack.Clone();
            }
            else
            {
                _currentJuiceStack = null; // Если нет сока, очищаем
            }
            
            // Рассчитываем сколько всего литров сока можно получить
            _totalJuiceAvailable = itemStack.StackSize * props.LitresPerItem.Value;
            
            // Восстанавливаем прогресс если есть сохраненные данные
            if (_pressingProgress > 0 && _totalJuiceAvailable > 0)
            {
                SqueezeProgress = (float)Math.Min(Math.Max(_pressingProgress / _totalJuiceAvailable, 0), 1);
            }
            else
            {
                _pressingProgress = 0;
                SqueezeProgress = 0;
            }
            
            // Проверяем совместимость жидкости
            if (!IsJuiceCompatible())
            {
                _totalJuiceAvailable = 0;
                _pressingProgress = 0;
                SqueezeProgress = 0;
                StopAnimation();
            }
        }
        else
        {
            _totalJuiceAvailable = 0;
            _currentJuiceStack = null;
            _pressingProgress = 0;
            SqueezeProgress = 0;
            StopAnimation();
        }
    }

    // Класс JuiceableProperties для совместимости с ванильным прессом
    public class JuiceableProperties
    {
        public JsonItemStack LiquidStack { get; set; }
        public JsonItemStack PressedStack { get; set; }
        public JsonItemStack ReturnStack { get; set; } // Жмых после отжима
        public float? LitresPerItem { get; set; }
        public float? PressedDryRatio { get; set; } = 0.1f; // Коэффициент для жмыха
    }
    
    private JuiceableProperties GetJuiceableProperties(ItemStack stack)
    {
        if (stack == null) return null;
        
        var juiceableProps = stack.ItemAttributes?["juiceableProperties"];
        if (juiceableProps == null || !juiceableProps.Exists) return null;
        
        var props = juiceableProps.AsObject<JuiceableProperties>(null, stack.Collectible.Code.Domain);
        
        if (props != null)
        {
            props.LiquidStack?.Resolve(Api.World, "juiceable properties liquidstack", stack.Collectible.Code);
            props.PressedStack?.Resolve(Api.World, "juiceable properties pressedstack", stack.Collectible.Code);
            props.ReturnStack?.Resolve(Api.World, "juiceable properties returnstack", stack.Collectible.Code);
        }
        
        return props;
    }
    
    // Новый метод для проверки совместимости жидкости
    private bool IsJuiceCompatible()
    {
        if (_currentJuiceStack == null)
            return false;
        
        // Если бак пустой - любая жидкость подходит
        if (LiquidSlot.Empty)
            return true;
        
        // Проверяем, совпадает ли жидкость в баке с жидкостью из фруктов
        var currentLiquid = LiquidSlot.Itemstack;
        if (currentLiquid == null)
            return true;
        
        // Сравниваем коды предметов (игнорируя количество)
        return currentLiquid.Collectible.Code.Equals(_currentJuiceStack.Collectible.Code);
    }
    
    private void UpdatePress(float dt)
    {
        if (PowerBehavior == null || ElectricalProgressive == null)
        {
            StopAnimation();
            return;
        }
        
        if (FruitSlot.Empty || _totalJuiceAvailable <= 0)
        {
            StopAnimation();
            _wasPressingLastTick = false;
            return;
        }
        
        // Проверяем совместимость жидкости перед началом отжима
        if (!IsJuiceCompatible())
        {
            StopAnimation();
            _wasPressingLastTick = false;
            return;
        }
        
        // Минимальная энергия для работы (10% от максимальной)
        var hasPower = PowerBehavior.PowerSetting >= _maxConsumption * 0.1f;
        var isPressingNow = hasPower && _totalJuiceAvailable > 0 && !IsFull() && IsJuiceCompatible();
        
        if (isPressingNow)
        {
            if (!_wasPressingLastTick)
            {
                StartSound();
                StartAnimation();
            }
            
            // ПРОСТАЯ ЛОГИКА ОТЖИМА:
            // 100 энергии на 1 литр сока
            const float energyPerLitre = 100f;
            
            // Сколько энергии получили за этот тик (Вт * сек = Дж)
            float energyReceived = PowerBehavior.PowerSetting * dt;
            
            // Сколько литров сока можно отжать с этой энергией
            double juiceProgress = energyReceived / energyPerLitre;
            
            // Увеличиваем прогресс отжима в литрах
            _pressingProgress += juiceProgress;
            
            // Если достигли максимума - завершаем цикл
            if (_pressingProgress >= _totalJuiceAvailable)
            {
                _pressingProgress = _totalJuiceAvailable;
                CompletePressCycle();
            }
            
            // Рассчитываем процент прогресса для отображения (0-1)
            if (_totalJuiceAvailable > 0)
            {
                SqueezeProgress = (float)(_pressingProgress / _totalJuiceAvailable);
            }
            else
            {
                SqueezeProgress = 0;
            }
            
            // Гарантируем диапазон 0-1
            SqueezeProgress = Math.Min(Math.Max(SqueezeProgress, 0f), 1f);
            
            UpdateState();
        }
        else if (_wasPressingLastTick)
        {
            // Отжим прекратился - останавливаем анимацию
            StopAnimation();
            StopSound();
            
            MarkDirty(true);
        }
        
        _wasPressingLastTick = isPressingNow;
    }
    
    private void CompletePressCycle()
    {
        try
        {
            if (_currentJuiceStack == null || _totalJuiceAvailable <= 0)
                return;
            
            // Проверяем совместимость перед извлечением сока
            if (!IsJuiceCompatible())
            {
                // Несовместимая жидкость - не добавляем
                _totalJuiceAvailable = 0;
                _pressingProgress = 0;
                SqueezeProgress = 0;
                StopAnimation();
                StopSound();
                MarkDirty(true);
                return;
            }
            
            // Добавляем сок в бак
            ExtractJuice(_totalJuiceAvailable);
            
            // Создаем жмых
            var props = GetJuiceableProperties(FruitSlot.Itemstack);
            CreatePressedMash(props, FruitSlot.Itemstack.StackSize);
            
            // Убираем фрукты
            FruitSlot.Itemstack = null;
            FruitSlot.MarkDirty();
            
            // Сбрасываем прогресс
            _totalJuiceAvailable = 0;
            _pressingProgress = 0;
            SqueezeProgress = 0;
            
            // Останавливаем анимацию и звук при завершении цикла
            StopAnimation();
            StopSound();
            
            MarkDirty(true);
            UpdateState();
            
            // Обновляем свойства
            UpdateJuiceableProperties();
        }
        catch (Exception ex)
        {
            Api?.Logger?.Error($"Error in CompletePressCycle: {ex.Message}");
        }
    }
    
    private void ExtractJuice(double litres)
    {
        if (_currentJuiceStack == null || litres <= 0)
            return;
        
        float availableSpace = LiquidCapacity - LiquidAmount;
        float juiceToExtract = (float)Math.Min(litres, availableSpace);
        
        if (juiceToExtract <= 0)
            return;
        
        ItemStack juiceStack = _currentJuiceStack.Clone();
        var props = BlockLiquidContainerBase.GetContainableProps(juiceStack);
        if (props == null) return;
        
        int itemsToAdd = (int)(juiceToExtract * props.ItemsPerLitre);
        juiceStack.StackSize = itemsToAdd;
        
        TryPutLiquidFromStack(juiceStack, juiceToExtract);
    }
    
    private void CreatePressedMash(JuiceableProperties props, int fruitsUsed)
    {
        if (props == null || fruitsUsed <= 0)
            return;
        
        ItemStack mashStack = null;
        
        // Используем ReturnStack если есть
        if (props?.ReturnStack?.ResolvedItemstack != null)
        {
            mashStack = props.ReturnStack.ResolvedItemstack?.Clone();
        }
        // Иначе используем PressedStack
        else if (props?.PressedStack?.ResolvedItemstack != null)
        {
            mashStack = props.PressedStack.ResolvedItemstack?.Clone();
        }
        
        // Добавляем жмых
        if (mashStack != null)
        {
            // Количество жмыха = количество литров сока (округляем вверх)
            int mashCount = (int)Math.Ceiling(_totalJuiceAvailable);
            mashStack.StackSize = Math.Max(1, mashCount);
            
            if (MashSlot.Empty)
            {
                MashSlot.Itemstack = mashStack;
            }
            else if (MashSlot.Itemstack.Collectible.Code == mashStack.Collectible.Code)
            {
                MashSlot.Itemstack.StackSize = Math.Min(
                    MashSlot.Itemstack.StackSize + mashStack.StackSize,
                    MashSlot.Itemstack.Collectible.MaxStackSize
                );
            }
            else
            {
                Api.World.SpawnItemEntity(mashStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            
            MashSlot.MarkDirty();
        }
    }
    
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;
        
        // Проверяем, не запущена ли уже анимация (как в центрифуге)
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "craft",
                Code = "craft",
                AnimationSpeed = 1f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }
    }
    
    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;
        
        // Проверяем, запущена ли анимация (как в центрифуге)
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("craft") == true)
        {
            AnimUtil.StopAnimation("craft");
        }
    }
    
    private void StartSound()
    {
        if (_pressSound != null || Api?.Side != EnumAppSide.Client)
            return;
        
        _pressSound = _capi.World.LoadSound(new SoundParams()
        {
            Location = _pressSoundLocation,
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 0.5f,
        });
        
        _pressSound?.Start();
    }
    
    private void StopSound()
    {
        if (_pressSound == null)
            return;
        
        _pressSound.Stop();
        _pressSound.Dispose();
        _pressSound = null;
    }
    
    private void UpdateState()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(SqueezeProgress, LiquidAmount, LiquidCapacity);
        }
        MarkDirty(true);
    }
    
    // === МЕТОДЫ РАБОТЫ С ЖИДКОСТЬЮ ===
    
    public int TryPutLiquidFromStack(ItemStack liquidStack, float desiredLitres)
    {
        if (liquidStack == null) return 0;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props != null && props.Containable)
        {
            float itemsPerLitre = props.ItemsPerLitre;
            int desiredItems = (int)(itemsPerLitre * desiredLitres);
            float availItems = liquidStack.StackSize;
            float maxItems = LiquidCapacity * itemsPerLitre;
            
            ItemStack currentStack = LiquidStack;
            
            if (currentStack == null)
            {
                int placeableItems = (int)GameMath.Min(desiredItems, maxItems, availItems);
                int movedItems = Math.Min(desiredItems, placeableItems);
                
                ItemStack placedstack = liquidStack.Clone();
                placedstack.StackSize = movedItems;
                LiquidStack = placedstack;
                
                MarkDirty();
                UpdateState();
                
                return movedItems;
            }
            else
            {
                if (!currentStack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) 
                    return 0;
                
                int placeableItems = (int)Math.Min(availItems, maxItems - (float)currentStack.StackSize);
                int movedItems = Math.Min(placeableItems, desiredItems);
                
                currentStack.StackSize += movedItems;
                LiquidSlot.MarkDirty();
                MarkDirty(true);
                UpdateState();
                
                return movedItems;
            }
        }
        
        return 0;
    }
    
    public bool IsFull()
    {
        return LiquidAmount >= LiquidCapacity - 0.01f;
    }
    
    // === ВЗАИМОДЕЙСТВИЕ С ИГРОКОМ ===
    
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogEFruitPress(DialogTitle, Inventory, Pos, _capi, this);
                _clientDialog.Update(SqueezeProgress, LiquidAmount, LiquidCapacity);
                return _clientDialog;
            });
        }
        return true;
    }
    
    // === СЕРИАЛИЗАЦИЯ ===
    
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        
        if (tree.HasAttribute("inventory"))
        {
            this._inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        }
        
        this.SqueezeProgress = tree.GetFloat("squeezeProgress", 0);
        this._totalJuiceAvailable = tree.GetDouble("totalJuiceAvailable", 0);
        this._pressingProgress = tree.GetDouble("pressingProgress", 0);
        
        // Восстанавливаем процентный прогресс
        if (_totalJuiceAvailable > 0)
        {
            // Гарантируем, что SqueezeProgress в диапазоне 0-1
            SqueezeProgress = (float)Math.Min(Math.Max(_pressingProgress / _totalJuiceAvailable, 0), 1);
        }
        else
        {
            SqueezeProgress = 0;
        }
        
        if (this.Api != null)
        {
            this._inventory.AfterBlocksLoaded(this.Api.World);
        }
            
        UpdateJuiceableProperties();
    }
    
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        
        var invTree = new TreeAttribute();
        this._inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;
        
        tree.SetFloat("squeezeProgress", this.SqueezeProgress);
        tree.SetDouble("totalJuiceAvailable", this._totalJuiceAvailable);
        tree.SetDouble("pressingProgress", this._pressingProgress);
    }
    
    // === ИНФОРМАЦИЯ ДЛЯ ИГРОКА ===
    
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        if (!FruitSlot.Empty && _totalJuiceAvailable > 0)
        {
            var props = GetJuiceableProperties(FruitSlot.Itemstack);
            if (props != null && props.LiquidStack?.ResolvedItemstack != null)
            {
                string juiceName = props.LiquidStack.ResolvedItemstack.GetName();
                
                // Проверяем совместимость
                if (!IsJuiceCompatible())
                {
                    dsc.AppendLine(Lang.Get("Cannot press: different liquid in tank"));
                    dsc.AppendLine(Lang.Get("Tank contains: {0}", LiquidSlot.Itemstack?.GetName() ?? "Empty"));
                    dsc.AppendLine(Lang.Get("Fruit produces: {0}", juiceName));
                }
                else
                {
                    dsc.AppendLine(Lang.Get("Produces {0:0.##}L of {1}", _totalJuiceAvailable, juiceName));
                }
            }
        }
        
        if (PowerBehavior != null)
        {
            dsc.AppendLine(Lang.Get("Power: {0}/{1} W", PowerBehavior.PowerSetting, _maxConsumption));
            
            if (PowerBehavior.PowerSetting >= _maxConsumption * 0.1f && _totalJuiceAvailable > 0 && IsJuiceCompatible())
            {
                // Показываем скорость отжима
                const float energyPerLitre = 100f;
                double litresPerSecond = PowerBehavior.PowerSetting / energyPerLitre;
                double timeLeft = (_totalJuiceAvailable - _pressingProgress) / litresPerSecond;
                
                dsc.AppendLine(Lang.Get("Speed: {0:0.##} L/s", litresPerSecond));
                dsc.AppendLine(Lang.Get("Time left: {0:0.#}s", timeLeft));
            }
        }
        
        if (SqueezeProgress > 0 && IsJuiceCompatible())
        {
            dsc.AppendLine(Lang.Get("Progress: {0:0%}", SqueezeProgress));
        }
        
        dsc.AppendLine(Lang.Get("Liquid: {0:0.##}/{1} L", LiquidAmount, LiquidCapacity));
        
        // Показываем предупреждение о несовместимости
        if (!FruitSlot.Empty && !IsJuiceCompatible())
        {
            dsc.AppendLine(Lang.Get("Warning: Incompatible liquid in tank"));
        }
    }
    
    // === СВОЙСТВА ===
    
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:efruitpress");
    
    // === ОЧИСТКА РЕСУРСОВ ===
    
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        
        if (ElectricalProgressive != null)
        {
            ElectricalProgressive.Connection = Facing.None;
        }
        
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog.TryClose();
            this._clientDialog = null;
        }
        
        StopAnimation();
        StopSound();
    }
    
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._clientDialog?.TryClose();
        StopSound();
    }
    
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
    }
    
    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);
        ElectricalProgressive?.OnReceivedServerPacket(packetid, data);
    }
}