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
/// Конфигурация жидкости для генератора
/// </summary>
public class LiquidConfig
{
    public string[] AllowedLiquids { get; set; } = new[] { "water", "waterportion" };
    public float ConsumptionRate { get; set; } = 0.1f;
    public float MinTemperature { get; set; } = 200f;
    public float CapacityLitres { get; set; } = 100f;
    public bool LiquidRequired { get; set; } = true;
    public bool RequireSpecificLiquid { get; set; } = true;
    
    // Параметры для температурной зависимости расхода
    public bool EnableTemperatureDependentConsumption { get; set; } = true;
    public float MaxExpectedTemperature { get; set; } = 1300f;
    public float MaxConsumptionMultiplier { get; set; } = 3f;
    
    /// <summary>
    /// Проверяет, разрешена ли данная жидкость
    /// </summary>
    public bool IsLiquidAllowed(ItemStack liquidStack)
    {
        if (liquidStack?.Collectible == null) return false;
    
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable) return false;
    
        // Если не требуется конкретная жидкость, разрешаем любую
        if (!RequireSpecificLiquid) return true;
    
        // Берем только то, что после двоеточия (или всю строку, если двоеточия нет)
        string fullCode = liquidStack.Collectible.Code?.ToString() ?? "";
        string liquidName = fullCode.Contains(':') ? fullCode.Split(':')[1] : fullCode;
    
        foreach (var allowed in AllowedLiquids)
        {
            // Сравниваем только имена (без домена)
            if (string.Equals(liquidName, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
    
        return false;
    }
    
    /// <summary>
    /// Получить локализованное название разрешенных жидкостей
    /// </summary>
    public string GetAllowedLiquidsText()
    {
        if (!RequireSpecificLiquid) return Lang.Get("electricalprogressivebasics:Any liquid");
        
        string[] names = new string[AllowedLiquids.Length];
        for (int i = 0; i < AllowedLiquids.Length; i++)
        {
            names[i] = Lang.Get("item-" + AllowedLiquids[i]);
        }
        return string.Join(", ", names);
    }
}

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
    private LiquidConfig _liquidConfig;

    private static MeshData? _mesh;
    private static Shape? _resultingShape;

    private float _genTemp = 20f;
    private float _waterAmount = 0f;
    
    private int _maxTemp;
    private float _fuelBurnTime;
    private float _maxBurnTime;
    
    private long _listenerId;
    
    // === Свойства ===
    
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    public float GenTemp => _genTemp;
    
    /// <summary>
    /// Текущее количество жидкости с учетом конфигурации
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
    
    /// <summary>
    /// Вместимость для жидкости из конфигурации
    /// </summary>
    public float WaterCapacity 
    { 
        get
        {
            if (_liquidConfig != null)
                return _liquidConfig.CapacityLitres;
                
            if (Block?.Attributes?["liquidConfig"]?["capacityLitres"].Exists == true)
            {
                return Block.Attributes["liquidConfig"]["capacityLitres"].AsFloat(100f);
            }
            
            return 100f;
        }
    }
    
    /// <summary>
    /// Базовая скорость потребления жидкости из конфигурации
    /// </summary>
    public float WaterConsumptionRate
    {
        get
        {
            if (_liquidConfig != null)
                return _liquidConfig.ConsumptionRate;
                
            if (Block?.Attributes?["liquidConfig"]?["consumptionRate"].Exists == true)
            {
                return Block.Attributes["liquidConfig"]["consumptionRate"].AsFloat(0.1f);
            }
            
            return 0.1f;
        }
    }
    
    /// <summary>
    /// Текущая скорость потребления жидкости с учетом температуры.
    /// Чем выше температура, тем быстрее расход.
    /// </summary>
    public float CurrentConsumptionRate
    {
        get
        {
            // Базовая скорость из конфига
            float baseRate = WaterConsumptionRate;
            
            // Проверяем, включена ли зависимость от температуры
            if (_liquidConfig == null || !_liquidConfig.EnableTemperatureDependentConsumption)
            {
                return baseRate;
            }
            
            // Если температура ниже минимальной для работы, расход нулевой
            if (_genTemp <= MinWorkTemperature)
            {
                return 0f;
            }
            
            // Рассчитываем коэффициент на основе температуры.
            float maxExpectedTemp = _liquidConfig.MaxExpectedTemperature;
            float maxMultiplier = _liquidConfig.MaxConsumptionMultiplier;
            
            // Нормализуем температуру от 0 до 1 в диапазоне от MinWorkTemperature до maxExpectedTemp
            float tempRange = maxExpectedTemp - MinWorkTemperature;
            if (tempRange <= 0) return baseRate; // Защита от деления на ноль
            
            float tempFactor = (_genTemp - MinWorkTemperature) / tempRange;
            tempFactor = GameMath.Clamp(tempFactor, 0f, 1f);
            
            // Итоговый множитель от 1 до maxMultiplier
            float multiplier = 1f + (tempFactor * (maxMultiplier - 1f));
            
            return baseRate * multiplier;
        }
    }
    
    /// <summary>
    /// Минимальная температура для работы из конфигурации
    /// </summary>
    public float MinWorkTemperature
    {
        get
        {
            if (_liquidConfig != null)
                return _liquidConfig.MinTemperature;
                
            if (Block?.Attributes?["liquidConfig"]?["minTemperature"].Exists == true)
            {
                return Block.Attributes["liquidConfig"]["minTemperature"].AsFloat(200f);
            }
            
            return 200f;
        }
    }
    
    /// <summary>
    /// Требуется ли жидкость для работы
    /// </summary>
    public bool LiquidRequired
    {
        get
        {
            if (_liquidConfig != null)
                return _liquidConfig.LiquidRequired;
                
            if (Block?.Attributes?["liquidConfig"]?["liquidRequired"].Exists == true)
            {
                return Block.Attributes["liquidConfig"]["liquidRequired"].AsBool(true);
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// Проверяет, разрешена ли текущая жидкость
    /// </summary>
    public bool IsCurrentLiquidAllowed
    {
        get
        {
            if (WaterSlot.Empty) return false;
            if (_liquidConfig == null) return true;
            return _liquidConfig.IsLiquidAllowed(WaterSlot.Itemstack);
        }
    }
    
    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature();
            
            // Проверяем наличие и тип жидкости
            bool hasValidLiquid = !WaterSlot.Empty && IsCurrentLiquidAllowed;
            
            if (_genTemp <= envTemp || _genTemp < MinWorkTemperature || (LiquidRequired && !hasValidLiquid))
                return 1f;
                
            return (_genTemp - envTemp) * 2f;
        }
    }
    
    public ItemSlot FuelSlot => _inventory[0];
    public ItemSlot WaterSlot => _inventory[1];
    
    public ItemStack FuelStack
    {
        get => _inventory[0].Itemstack;
        set
        {
            _inventory[0].Itemstack = value;
            _inventory[0].MarkDirty();
        }
    }
    
    public ItemStack WaterStack
    {
        get => _inventory[1].Itemstack;
        set
        {
            _inventory[1].Itemstack = value;
            _inventory[1].MarkDirty();
        }
    }
    
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:fuelgen");
    public override string InventoryClassName => "fuelgen";
    
    // === Конструктор ===
    
    public BlockEntityEFuelGenerator()
    {
        _inventory = new InventoryFuelGenerator(null, null);
        _inventory.SlotModified += OnSlotModified;
    }
    
    // === Основные методы ===
    
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        
        // Загружаем конфигурацию жидкости из атрибутов блока
        if (Block?.Attributes?["liquidConfig"].Exists == true)
        {
            try
            {
                _liquidConfig = Block.Attributes["liquidConfig"].AsObject<LiquidConfig>();
            }
            catch (Exception ex)
            {
                api.Logger.Warning("Failed to load liquid config for fuel generator: " + ex.Message);
                _liquidConfig = new LiquidConfig();
            }
        }
        else
        {
            _liquidConfig = new LiquidConfig();
        }
        
        if (api.Side == EnumAppSide.Server)
        {
            _sapi = api as ICoreServerAPI;
        }
        else
        {
            _capi = api as ICoreClientAPI;
            if (AnimUtil != null)
            {
                PrepareAnimUtil(api, InventoryClassName);
                AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
            }
        }
        
        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
        _inventory.SetLiquidConfig(_liquidConfig); // Передаем конфигурацию в инвентарь
        
        _listenerId = RegisterGameTickListener(OnBurnTick, 1000);
        CanDoBurn();
    }

    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (_mesh == null || _resultingShape == null)
        {
            AssetLocation shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            Shape _shape = Shape.TryGet(api, shapePath);

            _mesh = AnimUtil.CreateMesh(cacheDictKey, _shape, out _resultingShape, null);
        }
    }

    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }
    
    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return Math.Max(((_genTemp - 20.0f) / (1300f - 20.0f) * MyMiniLib.GetAttributeFloat(Block, "maxHeat", 0.0f)), 0.0f);
    }
    
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
    
    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
    }
    
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ElectricalProgressive?.OnBlockUnloaded();
        
        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }
        
        UnregisterGameTickListener(_listenerId);
        
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();

        _mesh?.Dispose();
        _resultingShape = null;
        _capi = null;
        _sapi = null;
    }
    
    public void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            if (!FuelSlot.Empty && FuelStack.Collectible.CombustibleProps != null && _fuelBurnTime == 0)
                CanDoBurn();
        }
        else if (slotId == 1)
        {
            CheckAnimationState();
        }
        
        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server, null);
        
        
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }
    
    public void OnBurnTick(float deltatime)
    {
        if (_fuelBurnTime > 0f)
        {
            // Проверяем наличие и тип жидкости
            bool hasValidLiquid = !WaterSlot.Empty && IsCurrentLiquidAllowed;
            bool canProducePower = _genTemp > MinWorkTemperature && (LiquidRequired ? hasValidLiquid : true);
            
            if (canProducePower && hasValidLiquid)
            {
                StartAnimation();
                // Используем текущую скорость расхода с учетом температуры
                ConsumeLiquid(CurrentConsumptionRate * deltatime);
            }
            else
            {
                StopAnimation();
            }
            
            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
            
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxBurnTime = 0f;
                _maxTemp = 20;
                StopAnimation();
                
                if (!FuelSlot.Empty)
                    CanDoBurn();
            }
        }
        else
        {
            StopAnimation();
            
            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
            
            CanDoBurn();
        }
        
        MarkDirty();
    }
    
    // === Методы работы с жидкостями ===
    
    public int TryPutLiquidFromStack(ItemStack liquidStack, float desiredLitres)
    {
        if (liquidStack == null) return 0;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable) return 0;
        
        // Проверяем, разрешена ли эта жидкость
        if (_liquidConfig != null && !_liquidConfig.IsLiquidAllowed(liquidStack))
            return 0;
        
        float itemsPerLitre = props.ItemsPerLitre;
        int desiredItems = (int)(itemsPerLitre * desiredLitres);
        float availItems = liquidStack.StackSize;
        float maxItems = WaterCapacity * itemsPerLitre;
        
        ItemStack currentStack = WaterStack;
        
        if (currentStack == null)
        {
            int placeableItems = (int)GameMath.Min(desiredItems, maxItems, availItems);
            int movedItems = Math.Min(desiredItems, placeableItems);
            
            ItemStack placedstack = liquidStack.Clone();
            placedstack.StackSize = movedItems;
            WaterStack = placedstack;
            
            CheckAnimationState();
            MarkDirty();
            
            return movedItems;
        }
        else
        {
            if (!currentStack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) 
                return 0;
            
            int placeableItems = (int)Math.Min(availItems, maxItems - (float)currentStack.StackSize);
            int movedItems = Math.Min(placeableItems, desiredItems);
            
            currentStack.StackSize += movedItems;
            WaterSlot.MarkDirty();
            MarkDirty(true);
            CheckAnimationState();
            
            return movedItems;
        }
    }
    
    public bool AddLiquidFromContainer(ItemStack liquidStack, bool consumeFromSource = true)
    {
        if (liquidStack == null) return false;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable)
            return false;
            
        // Проверяем, разрешена ли эта жидкость
        if (_liquidConfig != null && !_liquidConfig.IsLiquidAllowed(liquidStack))
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
                    return false; // Нельзя смешивать разные жидкости
                }
            }
            WaterSlot.MarkDirty();
            
            if (consumeFromSource)
            {
                liquidStack.StackSize -= takeItems;
            }
            
            CheckAnimationState();
            MarkDirty();
            
            return true;
        }
        
        return false;
    }
    
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
    
    public void UpdateWaterAmount(float newAmount)
    {
        float capacity = WaterCapacity;
        
        if (newAmount > capacity)
        {
            newAmount = capacity;
        }
        
        if (Math.Abs(_waterAmount - newAmount) > 0.1f)
        {
            _waterAmount = newAmount;
            MarkDirty();
        }
    }
    
    // === Методы анимации ===
    
    private void CheckAnimationState()
    {
        bool hasValidLiquid = !WaterSlot.Empty && IsCurrentLiquidAllowed;
        bool shouldBeAnimated = _fuelBurnTime > 0 && _genTemp > MinWorkTemperature && (LiquidRequired ? hasValidLiquid : true);
        
        if (shouldBeAnimated)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }
    
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;
        
        if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 14 };
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
    
    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null) return;
        
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            Block.LightHsv = new byte[] { 0, 0, 0 };
            AnimUtil.StopAnimation("work-on");
        }
    }
    
    // === Методы работы с топливом ===
    
    private void CanDoBurn()
    {
        if (FuelSlot.Empty) return;
        
        var fuelProps = FuelStack.Collectible.CombustibleProps;
        if (fuelProps == null || _fuelBurnTime > 0) return;
        
        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _maxBurnTime = _fuelBurnTime = fuelProps.BurnDuration;
            _maxTemp = fuelProps.BurnTemperature;
            
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
                FuelStack = null;
            FuelSlot.MarkDirty();
        }
    }
    
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
    
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiBlockEntityEFuelGenerator(DialogTitle, Inventory, Pos, _capi, this);
                return _clientDialog;
            });
        }
        return true;
    }
    
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (ElectricalProgressive != null)
        {
            ElectricalProgressive.Connection = Facing.None;
        }

        if (_clientDialog != null)
        {
            _clientDialog.TryClose();
            _clientDialog = null;
        }
        
        UnregisterGameTickListener(_listenerId);
        
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil.Dispose();

        _mesh?.Dispose();
        _resultingShape = null;
        _capi = null;
        _sapi = null;
    }
    
    // === Методы сериализации ===
    
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        _inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
        
        // Сохраняем конфигурацию жидкости
        if (_liquidConfig != null)
        {
            tree.SetString("liquidConfig", JsonUtil.ToString(_liquidConfig));
        }
    }
    
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
        
        // Загружаем конфигурацию жидкости
        if (tree.HasAttribute("liquidConfig"))
        {
            try
            {
                _liquidConfig = JsonUtil.FromString<LiquidConfig>(tree.GetString("liquidConfig"));
            }
            catch { }
        }
        
        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            CheckAnimationState();
            
            // Обновляем GUI при получении данных с сервера
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                UpdateGuiData(true);
            }
        }
    }
    
    // === Методы информации ===
    
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        if (FuelStack != null)
            dsc.AppendLine(Lang.Get("Contents") + ": " + FuelStack.StackSize + "x" + FuelStack.GetName());
        
        // Информация о жидкости
        if (_liquidConfig != null && _liquidConfig.RequireSpecificLiquid)
        {
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:requires") + ": " + _liquidConfig.GetAllowedLiquidsText());
        }
        
        // Добавляем информацию о текущем расходе воды
        if (_fuelBurnTime > 0 && _genTemp > MinWorkTemperature)
        {
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Current consumption") + $": {CurrentConsumptionRate:F2} L/s");
        }
    }
    
    public float GetFuelBurnTime()
    {
        return _fuelBurnTime;
    }
    
    public LiquidConfig GetLiquidConfig()
    {
        return _liquidConfig;
    }
    
    public void UpdateGuiData(bool force = false)
    {
        if (_clientDialog == null || !_clientDialog.IsOpened())
            return;
        
        // На клиенте просто обновляем GUI
        if (Api?.Side == EnumAppSide.Client)
        {
            _clientDialog.Update(_genTemp, _fuelBurnTime, WaterAmount, IsCurrentLiquidAllowed, CurrentConsumptionRate);
        }
        // На сервере отправляем пакет с обновлением только если force = true
        else if (Api?.Side == EnumAppSide.Server && force)
        {
            MarkDirty(true);
        }
    }
}