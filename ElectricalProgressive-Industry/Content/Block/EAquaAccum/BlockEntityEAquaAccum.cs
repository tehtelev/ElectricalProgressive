﻿using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.EAquaAccum;

public class BlockEntityEAquaAccum : BlockEntityGenericTypedContainer
{
    // === ИНТЕРФЕЙСЫ И СООБЩЕНИЯ ===

    private InventoryEAquaAccum _inventory;
    private GuiDialogEAquaAccum _clientDialog;

    // === СВОЙСТВА ===

    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:ewaterpump");
    public override string InventoryClassName => "ewaterpump";

    // === НАСТРОЙКИ КОНДЕНСАЦИИ ===
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCondensingLastTick;
    public float PumpProgress { get; private set; }

    // НАКОПЛЕНИЕ ДРОБНОЙ ЧАСТИ ВОДЫ (решает проблему остановки)
    private float _pendingWaterFraction = 0f;

    // === КЛИМАТИЧЕСКИЕ ДАННЫЕ ===
    private float _currentRainfall = 0.5f;
    private long _lastClimateCheck = 0;
    private const int CLIMATE_CHECK_INTERVAL_MS = 30000;

    // === КОНСТАНТЫ КОНДЕНСАЦИИ ===
    private const float BASE_CONDENSATION_RATE = 0.5f;
    private const float MAX_CONDENSATION_RATE = 2f;

    // === ЗВУК ===
    private ILoadedSound _pumpSound;
    private AssetLocation _pumpSoundLocation = new AssetLocation("sounds/machine/pump.ogg");

    // === АНИМАЦИЯ ===
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    public BEBehaviorEAquaAccum PowerBehavior => GetBehavior<BEBehaviorEAquaAccum>();

    // === НАПРАВЛЕНИЕ ===
    public Facing Facing
    {
        get => this._facing;
        set
        {
            if (value != this._facing)
            {
                this.ElectricalProgressive!.Connection = FacingHelper.FullFace(this._facing = value);
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

    public float LiquidCapacity => 100f;
    public ItemSlot LiquidSlot => _inventory.LiquidSlot;

    public ItemStack LiquidStack
    {
        get => _inventory.LiquidSlot.Itemstack;
        set
        {
            _inventory.LiquidSlot.Itemstack = value;
            _inventory.LiquidSlot.MarkDirty();
        }
    }

    // === СТАТУС КОНДЕНСАЦИИ ===
    public enum CondensationStatus
    {
        NoPower,
        TankFull,
        Condensing
    }

    public BlockEntityEAquaAccum()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
        this._inventory = new InventoryEAquaAccum();
        this._inventory.SlotModified += OnSlotModified;
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        this._inventory.LateInitialize("ewaterpump-" + Pos, api);
        (_inventory as InventoryEAquaAccum)?.SetBlockPos(Pos);

        this.RegisterGameTickListener(UpdateCondenser, 50);
        UpdateClimateData();

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
        MarkDirty();
        if (Api is ICoreClientAPI)
        {
            UpdateGui();
        }
    }

    public void UpdateGui()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetCondensationStatus());
        }
    }

    public CondensationStatus GetCondensationStatus()
    {
        if (PowerBehavior == null || ElectricalProgressive == null)
            return CondensationStatus.NoPower;

        if (IsFull())
            return CondensationStatus.TankFull;

        var hasPower = PowerBehavior.PowerSetting >= _maxConsumption * 0.1f;
        return hasPower ? CondensationStatus.Condensing : CondensationStatus.NoPower;
    }

    private void UpdateClimateData()
    {
        if (Api?.World == null) return;

        long now = Api.World.ElapsedMilliseconds;
        if (now - _lastClimateCheck < CLIMATE_CHECK_INTERVAL_MS) return;

        try
        {
            var climate = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.WorldGenValues);
            
            if (climate != null)
            {
                _currentRainfall = GameMath.Clamp(climate.Rainfall, 0.1f, 1.0f);
            }
        }
        catch 
        {
            _currentRainfall = 0.5f;
        }
        
        _lastClimateCheck = now;
    }

    private float GetCurrentCondensationRate()
    {
        float rainfallFactor = _currentRainfall;
        float powerRatio = Math.Min(PowerBehavior.PowerSetting / (float)_maxConsumption, 1f);
        float powerFactor = 0.2f + (powerRatio * 0.8f);
        
        float rate = BASE_CONDENSATION_RATE * rainfallFactor * powerFactor;
        
        return Math.Min(rate, MAX_CONDENSATION_RATE);
    }

    private void UpdateCondenser(float dt)
    {
        if (PowerBehavior == null || ElectricalProgressive == null)
        {
            StopAnimation();
            return;
        }

        UpdateClimateData();

        var status = GetCondensationStatus();
        var isCondensingNow = status == CondensationStatus.Condensing;

        if (isCondensingNow)
        {
            if (!_wasCondensingLastTick)
            {
                StartSound();
                StartAnimation();
            }

            float currentRate = GetCurrentCondensationRate();
            
            // НАКОПЛЕНИЕ ДРОБНОЙ ЧАСТИ
            _pendingWaterFraction += currentRate * dt;
            
            // Проверяем, накопилось ли хотя бы 0.001 литра (1 мл)
            if (_pendingWaterFraction >= 0.001f)
            {
                float waterToAdd = _pendingWaterFraction;
                float availableSpace = LiquidCapacity - LiquidAmount;
                waterToAdd = Math.Min(waterToAdd, availableSpace);
                
                if (waterToAdd > 0)
                {
                    AddWater(waterToAdd);
                    _pendingWaterFraction -= waterToAdd;
                    PumpProgress = LiquidAmount / LiquidCapacity;
                }
            }

            UpdateState();
        }
        else if (_wasCondensingLastTick)
        {
            StopAnimation();
            StopSound();
            MarkDirty(true);
            _pendingWaterFraction = 0f;
        }

        _wasCondensingLastTick = isCondensingNow;
    }

    private void AddWater(float litres)
    {
        if (litres <= 0) return;

        var waterStack = CreateWaterStack(litres);
        if (waterStack == null) return;

        if (LiquidSlot.Empty)
        {
            LiquidSlot.Itemstack = waterStack;
        }
        else if (LiquidSlot.Itemstack.Collectible.Code.Equals(waterStack.Collectible.Code))
        {
            var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
            if (props == null) return;

            // ПРЕОБРАЗУЕМ ЛИТРЫ В ПРЕДМЕТЫ С ОКРУГЛЕНИЕМ
            float itemsToAddFloat = litres * props.ItemsPerLitre;
            int itemsToAdd = (int)Math.Round(itemsToAddFloat, MidpointRounding.AwayFromZero);
            itemsToAdd = Math.Max(1, itemsToAdd);
            
            LiquidSlot.Itemstack.StackSize += itemsToAdd;
            LiquidSlot.Itemstack.StackSize = Math.Min(
                LiquidSlot.Itemstack.StackSize,
                LiquidSlot.Itemstack.Collectible.MaxStackSize
            );
        }
        else
        {
            LiquidSlot.Itemstack = waterStack;
        }

        LiquidSlot.MarkDirty();
    }

    private ItemStack CreateWaterStack(float litres)
    {
        var waterItem = Api.World.GetItem(new AssetLocation("game:bucket-water"));
        if (waterItem == null)
        {
            waterItem = Api.World.GetItem(new AssetLocation("waterportion"));
        }

        if (waterItem == null) return null;

        var waterStack = new ItemStack(waterItem);
        var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
        if (props == null) return null;

        // Округляем, чтобы получить хотя бы 1 предмет
        int stackSize = (int)Math.Round(litres * props.ItemsPerLitre, MidpointRounding.AwayFromZero);
        waterStack.StackSize = Math.Max(1, stackSize);
        return waterStack;
    }

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

                if (movedItems > 0)
                {
                    ItemStack placedstack = liquidStack.Clone();
                    placedstack.StackSize = movedItems;
                    LiquidStack = placedstack;

                    MarkDirty();
                    UpdateState();

                    return movedItems;
                }
            }
            else
            {
                if (!currentStack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes))
                    return 0;

                int placeableItems = (int)Math.Min(availItems, maxItems - (float)currentStack.StackSize);
                int movedItems = Math.Min(placeableItems, desiredItems);

                if (movedItems > 0)
                {
                    currentStack.StackSize += movedItems;
                    LiquidSlot.MarkDirty();
                    MarkDirty(true);
                    UpdateState();

                    return movedItems;
                }
            }
        }

        return 0;
    }

    public bool IsFull()
    {
        return LiquidAmount >= LiquidCapacity - 0.01f;
    }

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
        {
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
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

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("pump") == true)
        {
            AnimUtil.StopAnimation("pump");
        }
    }

    private void StartSound()
    {
        if (_pumpSound != null || Api?.Side != EnumAppSide.Client)
            return;

        _pumpSound = _capi.World.LoadSound(new SoundParams()
        {
            Location = _pumpSoundLocation,
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 0.3f,
            Range = 16f
        });

        _pumpSound?.Start();
    }

    private void StopSound()
    {
        if (_pumpSound == null)
            return;

        _pumpSound.Stop();
        _pumpSound?.Dispose();
        _pumpSound = null;
    }

    private void UpdateState()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetCondensationStatus());
        }
        MarkDirty(true);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogEAquaAccum(DialogTitle, Inventory, Pos, _capi, this);
                _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetCondensationStatus());
                return _clientDialog;
            });
        }
        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        if (tree.HasAttribute("inventory"))
        {
            this._inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        }

        this.PumpProgress = tree.GetFloat("pumpProgress", 0);
        this._pendingWaterFraction = tree.GetFloat("pendingWaterFraction", 0);

        if (this.Api != null)
        {
            this._inventory.AfterBlocksLoaded(this.Api.World);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        var invTree = new TreeAttribute();
        this._inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;

        tree.SetFloat("pumpProgress", this.PumpProgress);
        tree.SetFloat("pendingWaterFraction", this._pendingWaterFraction);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        var status = GetCondensationStatus();

        switch (status)
        {
            case CondensationStatus.Condensing:
                dsc.AppendLine(Lang.Get("Status: Condensing water"));
                break;
            case CondensationStatus.NoPower:
                dsc.AppendLine(Lang.Get("Status: No power"));
                break;
            case CondensationStatus.TankFull:
                dsc.AppendLine(Lang.Get("Status: Tank full"));
                break;
        }

        dsc.AppendLine(Lang.Get("Water: {0:0.##}/{1} L", LiquidAmount, LiquidCapacity));

        if (PowerBehavior != null)
        {
            dsc.AppendLine(Lang.Get("Power: {0}/{1} W", PowerBehavior.PowerSetting, _maxConsumption));

            if (status == CondensationStatus.Condensing)
            {
                float currentRate = GetCurrentCondensationRate();
                dsc.AppendLine(Lang.Get("Condensation rate: {0:0.##} L/s", currentRate));
                
                string rainfallText = _currentRainfall switch
                {
                    < 0.2f => "Very dry (desert)",
                    < 0.4f => "Dry",
                    < 0.6f => "Moderate",
                    < 0.8f => "Wet",
                    _ => "Very wet (rainforest)"
                };
                dsc.AppendLine(Lang.Get("Rainfall: {0} ({1:0}%)", rainfallText, _currentRainfall * 100));

                if (LiquidAmount < LiquidCapacity)
                {
                    float timeToFill = (LiquidCapacity - LiquidAmount) / currentRate;
                    dsc.AppendLine(Lang.Get("Time to fill: {0:0.#}s", timeToFill));
                }
            }
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (ElectricalProgressive != null)
        {
            ElectricalProgressive.Connection = Facing.None;
        }

        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog?.TryClose();
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