using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EWaterPump;

public class BlockEntityEWaterPump : BlockEntityGenericTypedContainer
{
    private InventoryEWaterPump _inventory;
    private GuiDialogEWaterPump _clientDialog;
    public override string InventoryClassName => "ewaterpump";
    
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasPumpingLastTick;
    
    public float PumpProgress { get; private set; } // 0-1
    private float _pumpRate = 1.0f; // Литр в секунду при 100% мощности
    private double _lastPumpTime;
    
    // Настройки помпы
    private const int WATER_CHECK_RADIUS = 2; // Область 5x5x5 (радиус 2 во все стороны)
    private const float REQUIRED_WATER_PERCENTAGE = 0.3f; // 50%
    private const int MAX_PUMP_HEIGHT = 10; // Максимальная высота подъема
    
    private ILoadedSound _pumpSound;
    private AssetLocation _pumpSoundLocation = new AssetLocation("sounds/machine/pump.ogg");
    
    private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
    public BEBehaviorEWaterPump PowerBehavior => GetBehavior<BEBehaviorEWaterPump>();
    
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
    
    public BlockEntityEWaterPump()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
        this._inventory = new InventoryEWaterPump();
        this._inventory.SlotModified += OnSlotModified;
    }
    
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        
        this._inventory.LateInitialize("ewaterpump-" + Pos, api);
        (_inventory as InventoryEWaterPump)?.SetBlockPos(Pos);
        
        this.RegisterGameTickListener(UpdatePump, 50); // 20 раз в секунду
        
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
            _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetPumpStatus());
        }
    }
    
    /// <summary>
    /// Проверяет условия для работы помпы В РЕАЛЬНОМ ВРЕМЕНИ
    /// </summary>
    public PumpStatus GetPumpStatus()
    {
        if (PowerBehavior == null || ElectricalProgressive == null)
            return PumpStatus.NoPower;
        
        // ИСПРАВЛЕНИЕ: Убрали проверку PowerSetting из определения статуса
        // Теперь статус отражает физическую возможность работы
        
        if (IsFull())
            return PumpStatus.TankFull;
        
        if (!HasEnoughWaterInArea())
            return PumpStatus.InsufficientWater;
        
        // Теперь проверяем питание только для определения Pumping/NoPower
        var hasPower = PowerBehavior.PowerSetting >= _maxConsumption * 0.1f;
        
        return hasPower ? PumpStatus.Pumping : PumpStatus.NoPower;
    }
    
    public enum PumpStatus
    {
        NoPower,
        InsufficientWater,
        TankFull,
        Pumping
    }
    
    /// <summary>
    /// Проверяет, находится ли помпа над блоком воды
    /// </summary>
    private bool IsOverWaterSource()
    {
        if (Api?.World == null) return false;
    
        var blockAccessor = Api.World.BlockAccessor;
    
        // Проверяем блоки под помпой (глубина до MAX_PUMP_HEIGHT)
        for (int depth = 1; depth <= MAX_PUMP_HEIGHT; depth++)
        {
            var checkPos = Pos.DownCopy(depth);
            var block = blockAccessor.GetBlock(checkPos);
        
            // Если достигли воздуха - значит, дальше пустота, воды нет
            if (block.Id == 0) break;
        
            // Проверяем, является ли блок водой
            if (IsWaterBlock(block))
            {
                return true;
            }
        }
    
        return false;
    }
    
    /// <summary>
    /// Проверяет, есть ли достаточно воды в области 5x5 под помпой
    /// </summary>
    public bool HasEnoughWaterInArea()
    {
        if (Api?.World == null) return false;
    
        var blockAccessor = Api.World.BlockAccessor;
    
        int totalBlocks = 0;
        int waterBlocks = 0;
    
        // Проверяем область 5x5 на разных глубинах под помпой
        // dx и dz - горизонтальные координаты, dy - глубина
        for (int dx = -WATER_CHECK_RADIUS; dx <= WATER_CHECK_RADIUS; dx++)
        {
            for (int dz = -WATER_CHECK_RADIUS; dz <= WATER_CHECK_RADIUS; dz++)
            {
                // Проверяем на разных глубинах (до MAX_PUMP_HEIGHT)
                for (int depth = 1; depth <= MAX_PUMP_HEIGHT; depth++)
                {
                    var checkPos = Pos.AddCopy(dx, -depth, dz); // ТОЛЬКО ВНИЗ
                    var block = blockAccessor.GetBlock(checkPos);
                
                    totalBlocks++;
                
                    if (IsWaterBlock(block))
                    {
                        waterBlocks++;
                    
                        // Если нашли воду на этой глубине, прекращаем проверку глубже
                        // для этой колонки (dx, dz)
                        break;
                    }
                
                    // Если достигли неводного блока (камень, земля и т.д.), 
                    // продолжаем проверять глубже
                    if (block.Id == 0) break; // Воздух - дальше пустота
                }
            }
        }
    
        if (totalBlocks == 0) return false;
    
        float waterPercentage = (float)waterBlocks / totalBlocks;
        return waterPercentage >= REQUIRED_WATER_PERCENTAGE;
    }
    
    /// <summary>
    /// Проверяет, является ли блок водой
    /// </summary>
    private bool IsWaterBlock(Vintagestory.API.Common.Block block)
    {
        if (block == null) return false;
    
        // Ванильная вода имеет код "water"
        return block.Code.Path == "water" || 
               block.Code.Path.StartsWith("water-");
    }
    
    private void UpdatePump(float dt)
    {
        if (PowerBehavior == null || ElectricalProgressive == null)
        {
            StopAnimation();
            return;
        }
        
        // ПРОВЕРЯЕМ УСЛОВИЯ КАЖДЫЙ ТИК
        var status = GetPumpStatus();
        var isPumpingNow = status == PumpStatus.Pumping;
        
        if (isPumpingNow)
        {
            if (!_wasPumpingLastTick)
            {
                StartSound();
                StartAnimation();
            }
            
            // Рассчитываем скорость откачки в зависимости от мощности
            float powerRatio = Math.Min(PowerBehavior.PowerSetting / (float)_maxConsumption, 1f);
            float currentPumpRate = _pumpRate * powerRatio;
            
            // Рассчитываем, сколько воды можно откачать за этот тик
            float waterToPump = currentPumpRate * dt;
            
            // Проверяем, сколько места осталось в баке
            float availableSpace = LiquidCapacity - LiquidAmount;
            waterToPump = Math.Min(waterToPump, availableSpace);
            
            if (waterToPump > 0)
            {
                // Создаем или добавляем воду
                AddWater(waterToPump);
                
                // Обновляем прогресс (для отображения)
                PumpProgress = LiquidAmount / LiquidCapacity;
                
                _lastPumpTime = Api.World.Calendar.TotalHours;
            }
            
            UpdateState();
        }
        else if (_wasPumpingLastTick)
        {
            // Откачка прекратилась - останавливаем анимацию
            StopAnimation();
            StopSound();
            MarkDirty(true);
        }
        
        _wasPumpingLastTick = isPumpingNow;
    }
    
    /// <summary>
    /// Добавляет воду в бак
    /// </summary>
    private void AddWater(float litres)
    {
        if (litres <= 0) return;
        
        // Создаем стек воды
        var waterStack = CreateWaterStack(litres);
        if (waterStack == null) return;
        
        // Добавляем в бак
        if (LiquidSlot.Empty)
        {
            LiquidSlot.Itemstack = waterStack;
        }
        else if (LiquidSlot.Itemstack.Collectible.Code.Equals(waterStack.Collectible.Code))
        {
            var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
            if (props == null) return;
            
            int itemsToAdd = (int)(litres * props.ItemsPerLitre);
            LiquidSlot.Itemstack.StackSize += itemsToAdd;
            LiquidSlot.Itemstack.StackSize = Math.Min(
                LiquidSlot.Itemstack.StackSize,
                LiquidSlot.Itemstack.Collectible.MaxStackSize
            );
        }
        else
        {
            // Разная жидкость - заменяем
            LiquidSlot.Itemstack = waterStack;
        }
        
        LiquidSlot.MarkDirty();
    }
    
    /// <summary>
    /// Создает стек воды
    /// </summary>
    private ItemStack CreateWaterStack(float litres)
    {
        // Ищем предмет воды в игре
        var waterItem = Api.World.GetItem(new AssetLocation("game:bucket-water"));
        if (waterItem == null)
        {
            // Пробуем другой вариант
            waterItem = Api.World.GetItem(new AssetLocation("waterportion"));
        }
        
        if (waterItem == null) return null;
        
        var waterStack = new ItemStack(waterItem);
        var props = BlockLiquidContainerBase.GetContainableProps(waterStack);
        if (props == null) return null;
        
        waterStack.StackSize = (int)(litres * props.ItemsPerLitre);
        return waterStack;
    }
    
    /// <summary>
    /// Пытается положить жидкость из стека
    /// </summary>
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
        
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("pump") == false)
        {
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "pump",
                Code = "pump",
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
        _pumpSound.Dispose();
        _pumpSound = null;
    }
    
    private void UpdateState()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetPumpStatus());
        }
        MarkDirty(true);
    }
    
    // === ВЗАИМОДЕЙСТВИЕ С ИГРОКОМ ===
    
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogEWaterPump(DialogTitle, Inventory, Pos, _capi, this);
                _clientDialog.Update(PumpProgress, LiquidAmount, LiquidCapacity, GetPumpStatus());
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
        
        this.PumpProgress = tree.GetFloat("pumpProgress", 0);
        
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
    }
    
    // === ИНФОРМАЦИЯ ДЛЯ ИГРОКА ===
    
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        var status = GetPumpStatus();
        
        // Статус работы
        switch (status)
        {
            case PumpStatus.Pumping:
                dsc.AppendLine(Lang.Get("Status: Pumping"));
                break;
            case PumpStatus.NoPower:
                dsc.AppendLine(Lang.Get("Status: No power"));
                break;
            case PumpStatus.InsufficientWater:
                dsc.AppendLine(Lang.Get("Status: Not enough water in area"));
                break;
            case PumpStatus.TankFull:
                dsc.AppendLine(Lang.Get("Status: Tank full"));
                break;
        }
        
        // Информация о воде
        dsc.AppendLine(Lang.Get("Water: {0:0.##}/{1} L", LiquidAmount, LiquidCapacity));
        
        if (PowerBehavior != null)
        {
            dsc.AppendLine(Lang.Get("Power: {0}/{1} W", PowerBehavior.PowerSetting, _maxConsumption));
            
            if (status == PumpStatus.Pumping)
            {
                float powerRatio = Math.Min(PowerBehavior.PowerSetting / (float)_maxConsumption, 1f);
                float currentPumpRate = _pumpRate * powerRatio;
                
                dsc.AppendLine(Lang.Get("Pump rate: {0:0.##} L/s", currentPumpRate));
                
                if (LiquidAmount < LiquidCapacity)
                {
                    float timeToFill = (LiquidCapacity - LiquidAmount) / currentPumpRate;
                    dsc.AppendLine(Lang.Get("Time to fill: {0:0.#}s", timeToFill));
                }
            }
        }
        
        // Информация о требованиях
        if (status == PumpStatus.InsufficientWater)
        {
            dsc.AppendLine(Lang.Get("Requires: Water source below (max {0} blocks)", MAX_PUMP_HEIGHT));
            dsc.AppendLine(Lang.Get("Requires: At least 50% water in 5x5x5 area"));
        }
    }
    
    // === СВОЙСТВА ===
    
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:ewaterpump");
    
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