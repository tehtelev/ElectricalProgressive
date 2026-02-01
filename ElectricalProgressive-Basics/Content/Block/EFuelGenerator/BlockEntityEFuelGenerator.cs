﻿using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

/// <summary>
/// Сущность блока электрического генератора на топливе.
/// Управляет состоянием генератора, обработкой топлива и жидкости.
/// Реализует IHeatSource для распространения тепла.
/// </summary>
public class BlockEntityEFuelGenerator : BlockEntityGenericTypedContainer, IHeatSource
{
    // === Поля ===
    private ICoreClientAPI _capi;
    private ICoreServerAPI _sapi;
    private InventoryFuelGenerator _inventory;
    private GuiBlockEntityEFuelGenerator _clientDialog;
    
    private float _genTemp = 20f;                    // Текущая температура генератора
    private const float WaterConsumptionRate = 0.1f; // Скорость потребления воды
    private float _waterAmount = 0f;                 // Текущее количество воды
    
    private int _maxTemp;                            // Максимальная температура горения
    private float _fuelBurnTime;                     // Оставшееся время горения
    private float _maxBurnTime;                      // Максимальное время горения
    
    // Поля для оптимизации обновления GUI
    private float _lastGuiUpdateTemp = -1;
    private float _lastGuiUpdateBurnTime = -1;
    private float _lastGuiUpdateWater = -1;
    private long _lastGuiUpdateTime = 0;
    
    private long _listenerId;                        // ID слушателя игровых тиков
    
    // === Свойства ===
    
    /// <summary>
    /// Поведение для электрической системы
    /// </summary>
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    
    /// <summary>
    /// Текущая температура генератора
    /// </summary>
    public float GenTemp => _genTemp;
    
    /// <summary>
    /// Количество жидкости в генераторе (литры)
    /// </summary>
    public float WaterAmount 
    { 
        get 
        {
            if (WaterSlot.Empty) return 0f;
            
            var props = BlockLiquidContainerBase.GetContainableProps(WaterSlot.Itemstack);
            if (props == null) return 0f;
            
            return (float)WaterSlot.Itemstack.StackSize / props.ItemsPerLitre;
        }
    }
    
    public float WaterCapacity 
    { 
        get
        {
            // Получаем вместимость из блока
            if (Block?.Attributes?["capacityLitres"].Exists == true)
            {
                return Block.Attributes["capacityLitres"].AsFloat(100f);
            }
        
            // Проверяем, есть ли у блока свойство CapacityLitres
            var property = Block?.GetType().GetProperty("CapacityLitres");
            if (property != null)
            {
                object value = property.GetValue(Block);
                if (value is float floatValue)
                    return floatValue;
                if (value is int intValue)
                    return (float)intValue;
            }
        
            return 100f; // Значение по умолчанию
        }
    }
    
    /// <summary>
    /// Текущая мощность генератора
    /// </summary>
    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature();
            if (_genTemp <= envTemp || _genTemp < 200 || WaterSlot.Empty)
                return 1f;
            return (_genTemp - envTemp) * 2f;
        }
    }
    
    /// <summary>
    /// Слот для топлива
    /// </summary>
    public ItemSlot FuelSlot => _inventory[0];
    
    /// <summary>
    /// Слот для жидкости
    /// </summary>
    public ItemSlot WaterSlot => _inventory[1];
    
    /// <summary>
    /// Стек топлива
    /// </summary>
    public ItemStack FuelStack
    {
        get => _inventory[0].Itemstack;
        set
        {
            _inventory[0].Itemstack = value;
            _inventory[0].MarkDirty();
        }
    }
    
    /// <summary>
    /// Стек жидкости
    /// </summary>
    public ItemStack WaterStack
    {
        get => _inventory[1].Itemstack;
        set
        {
            _inventory[1].Itemstack = value;
            _inventory[1].MarkDirty();
        }
    }
    
    /// <summary>
    /// Утилита для анимаций
    /// </summary>
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    
    /// <summary>
    /// Инвентарь генератора
    /// </summary>
    public override InventoryBase Inventory => _inventory;
    
    /// <summary>
    /// Заголовок диалога
    /// </summary>
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:fuelgen");
    
    /// <summary>
    /// Имя класса инвентаря
    /// </summary>
    public override string InventoryClassName => "fuelgen";
    
    // === Конструктор ===
    
    public BlockEntityEFuelGenerator()
    {
        _inventory = new InventoryFuelGenerator(null, null);
        _inventory.SlotModified += OnSlotModified;
    }
    
    // === Основные методы ===
    
    /// <summary>
    /// Инициализация сущности блока
    /// </summary>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        
        if (api.Side == EnumAppSide.Server)
            _sapi = api as ICoreServerAPI;
        else
        {
            _capi = api as ICoreClientAPI;
            // Инициализация аниматора на клиенте
            if (AnimUtil != null)
                AnimUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
        }
        
        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
        
        // Регистрация слушателя для обработки горения
        _listenerId = RegisterGameTickListener(OnBurnTick, 1000);
        CanDoBurn();
    }
    
    /// <summary>
    /// Получить угол поворота на основе стороны блока
    /// </summary>
    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }
    
    /// <summary>
    /// Получить силу тепла для окружающих блоков
    /// </summary>
    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return Math.Max(((_genTemp - 20.0f) / (1300f - 20.0f) * MyMiniLib.GetAttributeFloat(Block, "maxHeat", 0.0f)), 0.0f);
    }
    
    /// <summary>
    /// Получить температуру окружающей среды
    /// </summary>
    protected virtual int EnvironmentTemperature()
    {
        return (int)Api.World.BlockAccessor.GetClimateAt(Pos, 
            EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, 
            Api.World.Calendar.TotalDays).Temperature;
    }
    
    // === Обработка сетевых пакетов ===
    
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
    
    // === Обработка событий блока ===
    
    /// <summary>
    /// Обработка разрушения блока
    /// </summary>
    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
    }
    
    /// <summary>
    /// Обработка выгрузки блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ElectricalProgressive?.OnBlockUnloaded();
        
        // Закрытие GUI на клиенте
        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }
        
        // Отмена регистрации слушателя
        UnregisterGameTickListener(_listenerId);
        
        // Очистка анимаций на клиенте
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();
        
        // Очистка ссылок на API
        _capi = null;
        _sapi = null;
    }
    
    /// <summary>
    /// Обработка изменения слота инвентаря
    /// </summary>
    public void OnSlotModified(int slotId)
    {
        if (slotId == 0) // Слот топлива
        {
            if (!FuelSlot.Empty && FuelStack.Collectible.CombustibleProps != null && _fuelBurnTime == 0)
                CanDoBurn();
        }
        else if (slotId == 1) // Слот жидкости
        {
            CheckAnimationState();
        }
        
        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server, null);
        UpdateGuiData(true);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }
    
    /// <summary>
    /// Обработка тика горения (вызывается каждую секунду)
    /// </summary>
    public void OnBurnTick(float deltatime)
    {
        if (_fuelBurnTime > 0f)
        {
            bool canProducePower = _genTemp > 200 && !WaterSlot.Empty;
            
            if (canProducePower)
            {
                StartAnimation();
                ConsumeLiquid(WaterConsumptionRate * deltatime);
            }
            else
            {
                StopAnimation();
            }
            
            // Изменение температуры
            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
            
            // Завершение горения
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxBurnTime = 0f;
                _maxTemp = 20;
                StopAnimation();
                
                // Попытка начать новое горение
                if (!FuelSlot.Empty)
                    CanDoBurn();
            }
        }
        else
        {
            StopAnimation();
            
            // Охлаждение до температуры окружающей среды
            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
                
            CanDoBurn();
        }
        
        MarkDirty();
        UpdateGuiData();
    }
    
    // === Методы работы с жидкостями ===
    
    /// <summary>
    /// Попытка добавить жидкость из стека
    /// </summary>
    public int TryPutLiquidFromStack(ItemStack liquidStack, float desiredLitres)
    {
        if (liquidStack == null) return 0;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable) return 0;
        
        float itemsPerLitre = props.ItemsPerLitre;
        int desiredItems = (int)(itemsPerLitre * desiredLitres);
        float availItems = liquidStack.StackSize;
        float maxItems = WaterCapacity * itemsPerLitre;
        
        ItemStack currentStack = WaterStack;
        
        if (currentStack == null)
        {
            // Создание нового стека
            int placeableItems = (int)GameMath.Min(desiredItems, maxItems, availItems);
            int movedItems = Math.Min(desiredItems, placeableItems);
            
            ItemStack placedstack = liquidStack.Clone();
            placedstack.StackSize = movedItems;
            WaterStack = placedstack;
            
            CheckAnimationState();
            MarkDirty();
            UpdateGuiData(true);
            
            return movedItems;
        }
        else
        {
            // Проверка совместимости жидкостей
            if (!currentStack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) 
                return 0;
            
            // Добавление к существующему стеку
            int placeableItems = (int)Math.Min(availItems, maxItems - (float)currentStack.StackSize);
            int movedItems = Math.Min(placeableItems, desiredItems);
            
            currentStack.StackSize += movedItems;
            WaterSlot.MarkDirty();
            MarkDirty(true);
            CheckAnimationState();
            UpdateGuiData(true);
            
            return movedItems;
        }
    }
    
    /// <summary>
    /// Добавить жидкость из контейнера
    /// </summary>
    public bool AddLiquidFromContainer(ItemStack liquidStack, bool consumeFromSource = true)
    {
        if (liquidStack == null) return false;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable)
            return false;
        
        float slotLitres = (float)liquidStack.StackSize / props.ItemsPerLitre;
        float tankLitres = WaterAmount;
        float neededLitres = WaterCapacity - tankLitres;
        
        if (neededLitres > 0 && slotLitres > 0)
        {
            float takeLitres = Math.Min(neededLitres, slotLitres);
            int takeItems = (int)(takeLitres * props.ItemsPerLitre);
            
            if (takeItems <= 0) return false;
            
            ItemStack liquidForTank = liquidStack.Clone();
            liquidForTank.StackSize = takeItems;
            
            if (WaterSlot.Empty)
            {
                WaterSlot.Itemstack = liquidForTank;
            }
            else
            {
                if (WaterStack.Collectible.Code == liquidForTank.Collectible.Code)
                {
                    WaterStack.StackSize += takeItems;
                }
                else
                {
                    WaterSlot.Itemstack = liquidForTank;
                }
            }
            WaterSlot.MarkDirty();
            
            if (consumeFromSource)
            {
                liquidStack.StackSize -= takeItems;
            }
            
            CheckAnimationState();
            MarkDirty();
            UpdateGuiData(true);
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Потребление жидкости
    /// </summary>
    private void ConsumeLiquid(float litres)
    {
        if (WaterSlot.Empty) 
        {
            StopAnimation();
            return;
        }
        
        var props = BlockLiquidContainerBase.GetContainableProps(WaterStack);
        if (props == null) 
        {
            StopAnimation();
            return;
        }
        
        int itemsToConsume = (int)(litres * props.ItemsPerLitre);
        if (itemsToConsume <= 0) return;
        
        WaterStack.StackSize -= itemsToConsume;
        if (WaterStack.StackSize <= 0)
        {
            WaterSlot.Itemstack = null;
            StopAnimation();
        }
        
        WaterSlot.MarkDirty();
        UpdateWaterAmount(WaterAmount);
    }
    
    /// <summary>
    /// Обновление количества жидкости
    /// </summary>
    public void UpdateWaterAmount(float newAmount)
    {
        float capacity = WaterCapacity;
        
        // Проверяем, не превышает ли новое значение вместимость
        if (newAmount > capacity)
        {
            newAmount = capacity;
        }
        
        if (Math.Abs(_waterAmount - newAmount) > 0.1f)
        {
            _waterAmount = newAmount;
            UpdateGuiData();
            MarkDirty();
        }
    }
    
    // === Методы анимации ===
    
    /// <summary>
    /// Проверка состояния анимации
    /// </summary>
    private void CheckAnimationState()
    {
        bool shouldBeAnimated = _fuelBurnTime > 0 && _genTemp > 200 && !WaterSlot.Empty;
        
        if (shouldBeAnimated)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }
    
    /// <summary>
    /// Запуск анимации работы
    /// </summary>
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;
        
        if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 14 }; // Включение света
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 2f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }
    }
    
    /// <summary>
    /// Остановка анимации работы
    /// </summary>
    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;
        
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 0 }; // Выключение света
            AnimUtil.StopAnimation("work-on");
        }
    }
    
    // === Методы работы с топливом ===
    
    /// <summary>
    /// Проверка возможности и начало горения
    /// </summary>
    private void CanDoBurn()
    {
        if (FuelSlot.Empty) return;
        
        var fuelProps = FuelStack.Collectible.CombustibleProps;
        if (fuelProps == null || _fuelBurnTime > 0) return;
        
        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _maxBurnTime = _fuelBurnTime = fuelProps.BurnDuration;
            _maxTemp = fuelProps.BurnTemperature;
            
            // Потребление одного предмета топлива
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
                FuelStack = null;
            FuelSlot.MarkDirty();
            UpdateGuiData(true);
        }
    }
    
    /// <summary>
    /// Изменение температуры с учетом времени
    /// </summary>
    private static float ChangeTemperature(float fromTemp, float toTemp, float deltaTime)
    {
        var diff = Math.Abs(fromTemp - toTemp);
        deltaTime += deltaTime * (diff / 28f);
        if (diff < deltaTime) return toTemp;
        if (fromTemp > toTemp) deltaTime = -deltaTime;
        if (Math.Abs(fromTemp - toTemp) < 1f) return toTemp;
        return fromTemp + deltaTime;
    }
    
    // === Методы взаимодействия с игроком ===
    
    /// <summary>
    /// Обработка правого клика игрока
    /// </summary>
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiBlockEntityEFuelGenerator(DialogTitle, Inventory, Pos, _capi, this);
                _lastGuiUpdateTemp = -1;
                _lastGuiUpdateBurnTime = -1;
                _lastGuiUpdateWater = -1;
                UpdateGuiData(true);
                return _clientDialog;
            });
        }
        return true;
    }
    
    /// <summary>
    /// Обработка удаления блока
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        ElectricalProgressive.Connection = Facing.None;
        
        // Закрытие GUI
        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }
        
        // Отмена регистрации слушателя
        UnregisterGameTickListener(_listenerId);
        
        // Очистка анимаций
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();
        
        // Очистка ссылок
        _capi = null;
        _sapi = null;
    }
    
    // === Методы сериализации ===
    
    /// <summary>
    /// Сохранение состояния в дерево атрибутов
    /// </summary>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        _inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
    }
    
    /// <summary>
    /// Загрузка состояния из дерева атрибутов
    /// </summary>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        
        if (tree.HasAttribute("inventory"))
            _inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        
        if (Api != null)
            Inventory.AfterBlocksLoaded(Api.World);
            
        _genTemp = tree.GetFloat("_genTemp", 20);
        _maxTemp = tree.GetInt("maxTemp", 20);
        _fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);
        
        // Обновление состояния на клиенте
        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            CheckAnimationState();
            
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _lastGuiUpdateTemp = -1;
                _lastGuiUpdateBurnTime = -1;
                _lastGuiUpdateWater = -1;
                UpdateGuiData(true);
                MarkDirty();
            }
        }
    }
    
    // === Методы информации ===
    
    /// <summary>
    /// Получить информацию о блоке для отображения
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        if (FuelStack != null)
            dsc.AppendLine(Lang.Get("Contents") + ": " + FuelStack.StackSize + "x" + FuelStack.GetName());
        
        dsc.AppendLine(Lang.Get("Liquid") + ": " + WaterAmount.ToString("0.0") + "/" + WaterCapacity + " L");
        if (WaterSlot.Empty)
            dsc.AppendLine(Lang.Get("No liquid - reduced power"));
            
        if (_fuelBurnTime > 0)
        {
            dsc.AppendLine(Lang.Get("Burn time") + ": " + (int)_fuelBurnTime + " " + Lang.Get("gui-word-seconds"));
            if (!WaterSlot.Empty && _genTemp > 200)
                dsc.AppendLine(Lang.Get("Working at full power"));
            else if (WaterSlot.Empty)
                dsc.AppendLine(Lang.Get("No liquid - generator stopped"));
        }
    }
    
    /// <summary>
    /// Получить оставшееся время горения
    /// </summary>
    public float GetFuelBurnTime()
    {
        return _fuelBurnTime;
    }
    
    /// <summary>
    /// Обновление данных GUI
    /// </summary>
    public void UpdateGuiData(bool force = false)
    {
        if (_clientDialog == null || !_clientDialog.IsOpened())
            return;
            
        long currentTime = Api?.World?.ElapsedMilliseconds ?? 0;
        float currentWater = WaterAmount;
        float currentBurnTime = _fuelBurnTime;
        
        if (force)
        {
            _clientDialog.Update(_genTemp, currentBurnTime, currentWater);
            _lastGuiUpdateTemp = _genTemp;
            _lastGuiUpdateBurnTime = currentBurnTime;
            _lastGuiUpdateWater = currentWater;
            _lastGuiUpdateTime = currentTime;
            return;
        }
        
        // Оптимизация: обновляем не чаще чем раз в 250мс
        if (currentTime - _lastGuiUpdateTime < 250)
            return;
            
        bool tempChanged = Math.Abs(_genTemp - _lastGuiUpdateTemp) > 0.5f;
        bool burnTimeChanged = Math.Abs(currentBurnTime - _lastGuiUpdateBurnTime) > 0.5f;
        bool waterChanged = Math.Abs(currentWater - _lastGuiUpdateWater) > 0.1f;
        
        if (tempChanged || burnTimeChanged || waterChanged)
        {
            _clientDialog.Update(_genTemp, currentBurnTime, currentWater);
            _lastGuiUpdateTemp = _genTemp;
            _lastGuiUpdateBurnTime = currentBurnTime;
            _lastGuiUpdateWater = currentWater;
            _lastGuiUpdateTime = currentTime;
        }
    }
}