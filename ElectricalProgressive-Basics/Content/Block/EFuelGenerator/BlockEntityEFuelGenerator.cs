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
    
    public bool EnableTemperatureDependentConsumption { get; set; } = true;
    public float MaxExpectedTemperature { get; set; } = 1300f;
    public float MaxConsumptionMultiplier { get; set; } = 3f;
    
    public bool IsLiquidAllowed(ItemStack liquidStack)
    {
        if (liquidStack?.Collectible == null) return false;
    
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable) return false;
    
        if (!RequireSpecificLiquid) return true;
    
        string fullCode = liquidStack.Collectible.Code?.ToString() ?? "";
        string liquidName = fullCode.Contains(':') ? fullCode.Split(':')[1] : fullCode;
    
        foreach (var allowed in AllowedLiquids)
        {
            if (string.Equals(liquidName, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
    
        return false;
    }
    
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

public class BlockEntityEFuelGenerator : BlockEntityGenericTypedContainer, IHeatSource
{
    // === Поля ===
    private ICoreClientAPI _capi;
    private InventoryFuelGenerator _inventory;
    private GuiBlockEntityEFuelGenerator _clientDialog;
    private LiquidConfig _liquidConfig;

    private static MeshData? _mesh;
    private static Shape? _resultingShape;

    private float _genTemp = 20f;
    private int _maxTemp;
    private float _fuelBurnTime;
    
    private long _listenerId;
    private bool _wasBurningLastTick;
    
    // === Свойства для GUI ===
    public float GenTemp => _genTemp;
    public float FuelBurnTime => _fuelBurnTime;
    public float WaterAmount => GetWaterAmount();
    public float WaterCapacity => GetWaterCapacity();
    public float CurrentConsumptionRate => CalculateCurrentConsumptionRate();
    public float MinWorkTemperature => GetMinWorkTemperature();
    
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
    
    public bool LiquidRequired
    {
        get
        {
            if (_liquidConfig != null)
                return _liquidConfig.LiquidRequired;
            if (Block?.Attributes?["liquidConfig"]?["liquidRequired"].Exists == true)
                return Block.Attributes["liquidConfig"]["liquidRequired"].AsBool(true);
            return true;
        }
    }
    
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:fuelgen");
    public override string InventoryClassName => "fuelgen";
    
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    
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
    
        LoadLiquidConfig();
    
        if (api.Side == EnumAppSide.Client)
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
        _inventory.SetLiquidConfig(_liquidConfig);
    
        _listenerId = RegisterGameTickListener(OnBurnTick, 1000);
        TryStartBurn();
    
        // Принудительно обновляем GUI после инициализации на клиенте
        if (api.Side == EnumAppSide.Client)
        {
            // Небольшая задержка для гарантии, что GUI создан
            api.Event.RegisterCallback((dt) => {
                if (_clientDialog != null && _clientDialog.IsOpened())
                {
                    _clientDialog.Update(GenTemp, GetFuelBurnTime(), WaterAmount, IsCurrentLiquidAllowed, CurrentConsumptionRate);
                }
            }, 100);
        }
    }

    private void LoadLiquidConfig()
    {
        if (Block?.Attributes?["liquidConfig"].Exists == true)
        {
            try
            {
                _liquidConfig = Block.Attributes["liquidConfig"].AsObject<LiquidConfig>();
            }
            catch (Exception ex)
            {
                Api.Logger.Warning("Failed to load liquid config for fuel generator: " + ex.Message);
                _liquidConfig = new LiquidConfig();
            }
        }
        else
        {
            _liquidConfig = new LiquidConfig();
        }
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
    
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        ElectricalProgressive?.OnBlockUnloaded();
        
        if (_clientDialog != null)
        {
            _clientDialog?.TryClose();
            _clientDialog = null;
        }
        
        UnregisterGameTickListener(_listenerId);
        
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil?.Dispose();

        _mesh?.Dispose();
        _resultingShape = null;
        _capi = null;
    }
    
    public void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            if (!FuelSlot.Empty && FuelStack.Collectible.CombustibleProps != null && _fuelBurnTime == 0)
                TryStartBurn();
        }
        
        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(true);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        
        // Обновляем GUI при изменении слота (как в прессе)
        if (Api?.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(GenTemp, GetFuelBurnTime(), WaterAmount, IsCurrentLiquidAllowed, CurrentConsumptionRate);
        }
    }
    
    public void OnBurnTick(float deltatime)
    {
        bool isBurningNow = false;
    
        if (_fuelBurnTime > 0f)
        {
            bool hasValidLiquid = !WaterSlot.Empty && IsCurrentLiquidAllowed;
            bool hasFuel = _fuelBurnTime > 0; 
    
            if (hasFuel && hasValidLiquid)
            {
                isBurningNow = true;
                StartAnimation();  // анимация только если есть и топливо, и вода
                ConsumeLiquid(CurrentConsumptionRate * deltatime);
            }
            else
            {
                StopAnimation();  // нет воды - останавливаем анимацию
            }
        
            _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
            _fuelBurnTime -= deltatime;
        
            if (_fuelBurnTime <= 0f)
            {
                _fuelBurnTime = 0f;
                _maxTemp = 20;
                StopAnimation();
            
                if (!FuelSlot.Empty)
                    TryStartBurn();
            }
        }
        else
        {
            StopAnimation();
        
            if (_genTemp != 20f)
                _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
        
            TryStartBurn();
        }
    
        // Обновляем GUI при изменении состояния
        if (Api?.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(GenTemp, GetFuelBurnTime(), WaterAmount, IsCurrentLiquidAllowed, CurrentConsumptionRate);
        }
    
        MarkDirty();
    }
    
    private float GetWaterAmount()
    {
        if (WaterSlot.Empty) return 0f;
        var props = BlockLiquidContainerBase.GetContainableProps(WaterSlot.Itemstack);
        if (props == null) return 0f;
        return (float)WaterSlot.Itemstack.StackSize / props.ItemsPerLitre;
    }
    
    private float GetWaterCapacity()
    {
        if (_liquidConfig != null)
            return _liquidConfig.CapacityLitres;
        if (Block?.Attributes?["liquidConfig"]?["capacityLitres"].Exists == true)
            return Block.Attributes["liquidConfig"]["capacityLitres"].AsFloat(100f);
        return 100f;
    }
    
    private float GetMinWorkTemperature()
    {
        if (_liquidConfig != null)
            return _liquidConfig.MinTemperature;
        if (Block?.Attributes?["liquidConfig"]?["minTemperature"].Exists == true)
            return Block.Attributes["liquidConfig"]["minTemperature"].AsFloat(200f);
        return 200f;
    }
    
    private float GetBaseConsumptionRate()
    {
        if (_liquidConfig != null)
            return _liquidConfig.ConsumptionRate;
        if (Block?.Attributes?["liquidConfig"]?["consumptionRate"].Exists == true)
            return Block.Attributes["liquidConfig"]["consumptionRate"].AsFloat(0.1f);
        return 0.1f;
    }
    
    private float CalculateCurrentConsumptionRate()
    {
        float baseRate = GetBaseConsumptionRate();
        
        if (_liquidConfig == null || !_liquidConfig.EnableTemperatureDependentConsumption)
            return baseRate;
        
        if (_genTemp <= MinWorkTemperature)
            return 0f;
        
        float maxExpectedTemp = _liquidConfig.MaxExpectedTemperature;
        float maxMultiplier = _liquidConfig.MaxConsumptionMultiplier;
        
        float tempRange = maxExpectedTemp - MinWorkTemperature;
        if (tempRange <= 0) return baseRate;
        
        float tempFactor = (_genTemp - MinWorkTemperature) / tempRange;
        tempFactor = GameMath.Clamp(tempFactor, 0f, 1f);
        
        float multiplier = 1f + (tempFactor * (maxMultiplier - 1f));
        
        return baseRate * multiplier;
    }
    
    // === Методы работы с жидкостями ===
    
    public int TryPutLiquidFromStack(ItemStack liquidStack, float desiredLitres)
    {
        if (liquidStack == null) return 0;
        
        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable) return 0;
        
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
            
            return movedItems;
        }
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
    }
    
    // === Методы анимации ===
    
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
    
    private void TryStartBurn()
    {
        if (FuelSlot.Empty) return;
        
        var fuelProps = FuelStack.Collectible.CombustibleProps;
        if (fuelProps == null || _fuelBurnTime > 0) return;
        
        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _fuelBurnTime = fuelProps.BurnDuration;
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
            
                // Добавляем небольшую задержку перед первым обновлением
                _capi.Event.RegisterCallback((dt) => 
                {
                    if (_clientDialog != null && _clientDialog.IsOpened())
                    {
                        _clientDialog.Update(GenTemp, GetFuelBurnTime(), WaterAmount, IsCurrentLiquidAllowed, CurrentConsumptionRate);
                    }
                }, 50);
            
                return _clientDialog;
            });
        }
        return true;
    }
    
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (ElectricalProgressive != null)
            ElectricalProgressive.Connection = Facing.None;

        if (_clientDialog != null)
        {
            _clientDialog?.TryClose();
            _clientDialog = null;
        }
        
        UnregisterGameTickListener(_listenerId);
        
        if (Api.Side == EnumAppSide.Client && AnimUtil != null)
            AnimUtil?.Dispose();

        _mesh?.Dispose();
        _resultingShape = null;
        _capi = null;
    }
    
    // === Методы сериализации ===
    
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        _inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
        
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
        
        _genTemp = tree.GetFloat("genTemp", 20);
        _maxTemp = tree.GetInt("maxTemp", 20);
        _fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);
        
        if (tree.HasAttribute("liquidConfig"))
        {
            try
            {
                _liquidConfig = JsonUtil.FromString<LiquidConfig>(tree.GetString("liquidConfig"));
            }
            catch { }
        }
    }
    
    // === Методы информации ===
    
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        if (FuelStack != null)
            dsc.AppendLine(Lang.Get("Contents") + ": " + FuelStack.StackSize + "x" + FuelStack.GetName());
        
        if (_liquidConfig != null && _liquidConfig.RequireSpecificLiquid)
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:requires") + ": " + _liquidConfig.GetAllowedLiquidsText());
        
        if (_fuelBurnTime > 0 && _genTemp > MinWorkTemperature)
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Current consumption") + $": {CurrentConsumptionRate:F2} L/s");
    }
    
    public float GetFuelBurnTime()
    {
        return _fuelBurnTime;
    }
    
    public LiquidConfig GetLiquidConfig()
    {
        return _liquidConfig;
    }
}