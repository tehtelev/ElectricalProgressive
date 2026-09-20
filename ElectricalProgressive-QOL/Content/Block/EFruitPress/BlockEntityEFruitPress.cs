﻿using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.EFruitPress;

public class BlockEntityEFruitPress : BlockEntityGenericTypedContainer
{
    private InventoryEFruitPress _inventory;
    private GuiDialogEFruitPress _clientDialog;
    public override string InventoryClassName => "efruitpress";

    public bool StructureComplete
    {
        get
        {
            var construct = GetBehavior<MachineConstruct>();
            if (construct != null && construct.HasConstruction)
                return construct.IsReady;
            return true;
        }
    }

    public bool IsFormed => Block?.Variant?["state"] == "formed";
    
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasPressingLastTick;
    private MeshData? _liquidMesh;
    private string? _liquidMeshKey;
    private static readonly SimpleParticleProperties JuiceDripParticles = new()
    {
        MinVelocity = new Vec3f(-0.04f, 0, -0.04f),
        AddVelocity = new Vec3f(0.08f, 0, 0.08f),
        addLifeLength = 0.5f,
        LifeLength = 0.5f,
        MinQuantity = 0.25f,
        GravityEffect = 0.5f,
        SelfPropelled = true,
        MinSize = 0.1f,
        MaxSize = 0.2f
    };

    /// <summary>
    /// Сок вокруг валов GrinderGearAxis / GrinderGearAxis2 (зубцы).
    /// Абсолютные координаты шейпа /16: оси вдоль X, центр в YZ.
    /// </summary>
    private const float GearAxisX0 = 14.0f / 16f;
    private const float GearAxisX1 = 27.0f / 16f;
    private const float GearAxis1Y = 16.0f / 16f;
    private const float GearAxis1Z = 8.9f / 16f;
    private const float GearAxis2Y = 19.0f / 16f;
    private const float GearAxis2Z = 7.9f / 16f;
    private const float GearRadius = 2.8f / 16f;

    private static readonly SimpleParticleProperties GearJuiceParticles = new()
    {
        MinVelocity = new Vec3f(0, 0, 0),
        AddVelocity = new Vec3f(0.01f, 0.01f, 0.01f),
        addLifeLength = 0.04f,
        LifeLength = 0.16f,
        MinQuantity = 0.55f,
        GravityEffect = 0f,
        SelfPropelled = true,
        MinSize = 0.16f,
        MaxSize = 0.28f,
        WindAffected = false,
        WithTerrainCollision = false
    };
    
    public float SqueezeProgress { get; private set; } // 0-1 (0-100%)
    private double _totalJuiceAvailable = 0; // Сколько всего литров сока можно получить из текущего стака
    private double _pressingProgress = 0; // Сколько литров уже отжато
    private ItemStack _currentJuiceStack;
    
    /// <summary>
    /// Накопленная энергия (целые единицы) для отжима
    /// </summary>
    private float _accumulatedEnergyForPress = 0f;
    
    private ILoadedSound _pressSound;
    private AssetLocation _pressSoundLocation = new AssetLocation("sounds/player/wetclothsqueeze.ogg");

    private static MeshData? _animMesh;
    private static Shape? _animShape;
    private bool _animatorReadyForFormed;
    
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
        get => 100f;
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
    
    public float WaterAmount => LiquidAmount;
    public ItemSlot WaterSlot => LiquidSlot;
    public ItemStack WaterStack => LiquidStack;
    
    /// <summary>
    /// Добавить энергию для отжима
    /// </summary>
    public void AddEnergy(int amount)
    {
        if (FruitSlot.Empty || _totalJuiceAvailable <= 0)
            return;
        
        // Проверяем совместимость жидкости
        if (!IsJuiceCompatible())
            return;
        
        // Проверяем, не полон ли бак
        if (IsFull())
            return;
        
        var energyPerLitre = MyMiniLib.GetAttributeFloat(Block, "energyPerLitre", 1000f);
        
        // Добавляем энергию
        _accumulatedEnergyForPress += amount;
        
        // Сколько литров сока можно отжать с накопленной энергией
        float litresToPress = _accumulatedEnergyForPress / energyPerLitre;
        
        if (litresToPress >= 0.001f)
        {
            // Отнимаем использованную энергию
            _accumulatedEnergyForPress -= litresToPress * energyPerLitre;
            
            // Увеличиваем прогресс отжима
            double newProgress = _pressingProgress + litresToPress;
            
            if (newProgress >= _totalJuiceAvailable)
            {
                // Завершаем цикл
                _pressingProgress = _totalJuiceAvailable;
                CompletePressCycle();
            }
            else
            {
                _pressingProgress = newProgress;
            }
            
            // Обновляем прогресс для UI
            if (_totalJuiceAvailable > 0)
            {
                SqueezeProgress = (float)(_pressingProgress / _totalJuiceAvailable);
                SqueezeProgress = Math.Min(Math.Max(SqueezeProgress, 0f), 1f);
            }
            
            UpdateState();
            MarkDirty(true);
        }
    }
    
    public BlockEntityEFruitPress()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 200);
        this._inventory = new InventoryEFruitPress();
        this._inventory.SlotModified += OnSlotModified;
    }
    
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        
        this._inventory.LateInitialize("efruitpress-" + Pos, api);
        (_inventory as InventoryEFruitPress)?.SetBlockPos(Pos);
        
        // Все еще нужен тикер для анимации и проверки состояния
        this.RegisterGameTickListener(UpdatePress, 50);
        
        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            EnsureAnimatorReady();
        }
    }

    private void EnsureAnimatorReady()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (!IsFormed)
            return;

        if (_animatorReadyForFormed && AnimUtil.animator != null)
            return;

        PrepareAnimUtil(Api, InventoryClassName);
        if (_animMesh == null || _animShape == null)
            return;

        AnimUtil.InitializeAnimator(
            InventoryClassName,
            _animMesh,
            _animShape,
            new Vec3f(0, GetRotation(), 0f));
        _animatorReadyForFormed = true;
    }

    private Vintagestory.API.Common.Block? GetFormedBlockForAnim(ICoreAPI api)
    {
        if (Block?.Variant == null || !Block.Variant.ContainsKey("state"))
            return Block;

        return api.World.GetBlock(Block.CodeWithVariant("state", "formed")) ?? Block;
    }

    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (AnimUtil == null)
            return;

        var shapeBlock = GetFormedBlockForAnim(api) ?? Block;
        if (shapeBlock?.Shape?.Base == null)
            return;

        var shapePath = shapeBlock.Shape.Base.Clone()
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");

        var shape = Shape.TryGet(api, shapePath);
        if (shape == null)
            return;

        _animMesh = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _animShape, null);
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
        else if (slotid == 1) // Жидкость изменилась
        {
            if (FruitSlot.Empty)
            {
                _totalJuiceAvailable = 0;
                _currentJuiceStack = null;
                _pressingProgress = 0;
                _accumulatedEnergyForPress = 0;
                SqueezeProgress = 0;
                StopAnimation();
            }
            else if (_currentJuiceStack != null)
            {
                if (!IsJuiceCompatible())
                {
                    _totalJuiceAvailable = 0;
                    _pressingProgress = 0;
                    _accumulatedEnergyForPress = 0;
                    SqueezeProgress = 0;
                    StopAnimation();
                }
                else
                {
                    UpdateTotalJuiceAvailable();
                }
            }
        }
        
        _liquidMeshKey = null;
        this.MarkDirty(true);
        
        if (this.Api is ICoreClientAPI)
        {
            UpdateGui();
        }
    }
    
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
            _totalJuiceAvailable = itemStack.StackSize * props.LitresPerItem.Value;
        }
        else
        {
            _totalJuiceAvailable = 0;
        }
    }
    
    public void UpdateGui()
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(SqueezeProgress, LiquidAmount, LiquidCapacity);
        }
    }
    
    private void UpdateJuiceableProperties()
    {
        if (FruitSlot.Empty || FruitSlot.Itemstack?.Collectible == null)
        {
            _totalJuiceAvailable = 0;
            _currentJuiceStack = null;
            _pressingProgress = 0;
            _accumulatedEnergyForPress = 0;
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
                _currentJuiceStack = null;
            }
            
            _totalJuiceAvailable = itemStack.StackSize * props.LitresPerItem.Value;
            
            if (_pressingProgress > 0 && _totalJuiceAvailable > 0)
            {
                SqueezeProgress = (float)Math.Min(Math.Max(_pressingProgress / _totalJuiceAvailable, 0), 1);
            }
            else
            {
                _pressingProgress = 0;
                _accumulatedEnergyForPress = 0;
                SqueezeProgress = 0;
            }
            
            if (!IsJuiceCompatible())
            {
                _totalJuiceAvailable = 0;
                _pressingProgress = 0;
                _accumulatedEnergyForPress = 0;
                SqueezeProgress = 0;
                StopAnimation();
            }
        }
        else
        {
            _totalJuiceAvailable = 0;
            _currentJuiceStack = null;
            _pressingProgress = 0;
            _accumulatedEnergyForPress = 0;
            SqueezeProgress = 0;
            StopAnimation();
        }
    }

    public class JuiceableProperties
    {
        public JsonItemStack LiquidStack { get; set; }
        public JsonItemStack PressedStack { get; set; }
        public JsonItemStack ReturnStack { get; set; }
        public float? LitresPerItem { get; set; }
        public float? PressedDryRatio { get; set; } = 0.1f;
    }
    
    private JuiceableProperties GetJuiceableProperties(ItemStack stack)
    {
        if (stack?.Collectible == null) return null;
        
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
    
    public bool IsJuiceCompatible()
    {
        if (_currentJuiceStack == null)
            return false;
        
        if (LiquidSlot.Empty)
            return true;
        
        var currentLiquid = LiquidSlot.Itemstack;
        if (currentLiquid == null)
            return true;
        
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

            SpawnJuiceParticles();
            
            // Обновляем прогресс из накопленной энергии
            if (_totalJuiceAvailable > 0)
            {
                SqueezeProgress = (float)Math.Min(Math.Max(_pressingProgress / _totalJuiceAvailable, 0), 1);
                UpdateState();
            }
        }
        else if (_wasPressingLastTick)
        {
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
            
            if (!IsJuiceCompatible())
            {
                _totalJuiceAvailable = 0;
                _pressingProgress = 0;
                _accumulatedEnergyForPress = 0;
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
            _accumulatedEnergyForPress = 0;
            SqueezeProgress = 0;
            
            // Останавливаем анимацию и звук
            StopAnimation();
            StopSound();
            
            MarkDirty(true);
            UpdateState();
            
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
        
        if (props?.ReturnStack?.ResolvedItemstack != null)
        {
            mashStack = props.ReturnStack.ResolvedItemstack?.Clone();
        }
        else if (props?.PressedStack?.ResolvedItemstack != null)
        {
            mashStack = props.PressedStack.ResolvedItemstack?.Clone();
        }
        
        if (mashStack != null)
        {
            int mashCount = 0;
            
            var fruitStack = FruitSlot.Itemstack;
            if (fruitStack != null && IsHoneycomb(fruitStack))
            {
                mashCount = fruitsUsed;
            }
            else
            {
                mashCount = (int)Math.Ceiling(_totalJuiceAvailable);
            }
            
            mashCount = Math.Max(1, mashCount);
            mashStack.StackSize = mashCount;
            
            if (MashSlot.Empty)
            {
                MashSlot.Itemstack = mashStack;
                MashSlot.MarkDirty();
            }
            else if (MashSlot.Itemstack.Collectible.Code == mashStack.Collectible.Code)
            {
                int maxStackSize = MashSlot.Itemstack.Collectible.MaxStackSize;
                int currentStackSize = MashSlot.Itemstack.StackSize;
                int availableSpace = maxStackSize - currentStackSize;
                
                if (availableSpace > 0)
                {
                    int toAdd = Math.Min(mashCount, availableSpace);
                    MashSlot.Itemstack.StackSize += toAdd;
                    MashSlot.MarkDirty();
                    
                    int remaining = mashCount - toAdd;
                    if (remaining > 0)
                    {
                        ItemStack remainingStack = mashStack.Clone();
                        remainingStack.StackSize = remaining;
                        Api.World.SpawnItemEntity(remainingStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }
                else
                {
                    Api.World.SpawnItemEntity(mashStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
            else
            {
                Api.World.SpawnItemEntity(mashStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }
    }
    
    private bool IsHoneycomb(ItemStack stack)
    {
        if (stack == null) return false;
        var code = stack.Collectible.Code;
        return code.Path.Contains("honeycomb", StringComparison.OrdinalIgnoreCase);
    }
    
    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || !IsFormed)
            return;

        EnsureAnimatorReady();
        if (AnimUtil == null)
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
        
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            AnimUtil.StopAnimation("work-on");
        }
    }

    /// <summary>
    /// Капли из куба "particle" + сок на зубцах GrinderGear.
    /// </summary>
    private void SpawnJuiceParticles()
    {
        if (Api?.Side != EnumAppSide.Client || _capi == null)
            return;

        var juice = _currentJuiceStack ?? LiquidStack;
        GetWorldParticleBox(20f / 16f, 20f / 16f, 2f / 16f, 21f / 16f, 21f / 16f, 3f / 16f,
            out var min, out var size);

        JuiceDripParticles.MinPos = min;
        JuiceDripParticles.AddPos = size;
        JuiceDripParticles.MinQuantity = 0.25f + SqueezeProgress * 0.75f;

        for (var i = 0; i < 4; i++)
        {
            TintJuiceParticles(JuiceDripParticles, juice);
            Api.World.SpawnParticles(JuiceDripParticles);
        }

        SpawnGearWrapParticles(juice, GearAxis1Y, GearAxis1Z, -1f);
        SpawnGearWrapParticles(juice, GearAxis2Y, GearAxis2Z, 1f);
    }

    private void SpawnGearWrapParticles(ItemStack? juice, float yC, float zC, float spinSign)
    {
        var rand = Api.World.Rand;
        var count = 16 + (SqueezeProgress > 0.35f ? 6 : 0);
        var qty = 0.85f + SqueezeProgress * 0.55f;
        var spin = 0.16f * spinSign;
        const float xMin = 14.6f / 16f;
        const float xMax = 26.2f / 16f;
        const float zMax = 9.4f / 16f;

        for (var i = 0; i < count; i++)
        {
            var t = (float)rand.NextDouble();
            var ang = (float)(rand.NextDouble() * GameMath.TWOPI);
            var r = GearRadius * (0.9f + 0.08f * (float)rand.NextDouble());
            var sin = GameMath.Sin(ang);
            var cos = GameMath.Cos(ang);
            var lx = GameMath.Clamp(GearAxisX0 + t * (GearAxisX1 - GearAxisX0), xMin, xMax);
            var ly = yC + sin * r;
            var lz = Math.Min(zC + cos * r, zMax);
            var local = RotateLocal(lx, ly, lz);

            GearJuiceParticles.MinPos = Pos.ToVec3d().Add(local.X, local.Y, local.Z);
            GearJuiceParticles.AddPos = new Vec3d(0.008, 0.008, 0.008);
            GearJuiceParticles.MinQuantity = qty;

            var vy = -spin * cos - 0.06f * sin;
            var vz = spin * sin - 0.06f * cos;
            GearJuiceParticles.MinVelocity = RotateVel(0, vy, vz);
            GearJuiceParticles.AddVelocity = new Vec3f(0.012f, 0.012f, 0.012f);

            TintJuiceParticles(GearJuiceParticles, juice);
            Api.World.SpawnParticles(GearJuiceParticles);
        }
    }

    private void TintJuiceParticles(SimpleParticleProperties props, ItemStack? juice)
    {
        var texPos = GetJuiceTexPos(juice);
        if (texPos != null)
        {
            props.Color = _capi.BlockTextureAtlas.GetRandomColor(texPos,
                Api.World.Rand.Next(TextureAtlasPosition.RndColorsLength));
            return;
        }

        if (juice?.Item != null)
            props.Color = juice.Item.GetRandomColor(_capi, juice);
    }

    private TextureAtlasPosition? GetJuiceTexPos(ItemStack? juice)
    {
        if (_capi == null || juice?.Collectible == null)
            return null;

        var props = BlockLiquidContainerBase.GetContainableProps(juice);
        var loc = props?.Texture?.Base;
        if (loc == null)
            return null;

        var tile = _capi.BlockTextureAtlas[loc];
        if (tile != null)
            return tile;

        _capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out tile, null, 0.005f);
        return tile;
    }

    private void GetWorldParticleBox(
        float x0, float y0, float z0, float x1, float y1, float z1,
        out Vec3d worldMin, out Vec3d size)
    {
        var a = RotateLocal(x0, y0, z0);
        var b = RotateLocal(x1, y1, z1);
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var minZ = Math.Min(a.Z, b.Z);
        worldMin = Pos.ToVec3d().Add(minX, minY, minZ);
        size = new Vec3d(
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y),
            Math.Abs(a.Z - b.Z));
    }

    private Vec3d RotateLocal(float lx, float ly, float lz)
    {
        var deg = GetRotation();
        if (Math.Abs(deg) < 0.01f)
            return new Vec3d(lx, ly, lz);

        var rad = deg * GameMath.DEG2RAD;
        var ox = lx - 0.5f;
        var oz = lz - 0.5f;
        var cos = GameMath.Cos(rad);
        var sin = GameMath.Sin(rad);
        return new Vec3d(0.5f + ox * cos + oz * sin, ly, 0.5f + -ox * sin + oz * cos);
    }

    private Vec3f RotateVel(float vx, float vy, float vz)
    {
        var deg = GetRotation();
        if (Math.Abs(deg) < 0.01f)
            return new Vec3f(vx, vy, vz);

        var rad = deg * GameMath.DEG2RAD;
        var cos = GameMath.Cos(rad);
        var sin = GameMath.Sin(rad);
        return new Vec3f(vx * cos + vz * sin, vy, -vx * sin + vz * cos);
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
        _pressSound?.Dispose();
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
        this._accumulatedEnergyForPress = tree.GetFloat("accumulatedEnergyForPress", 0);
        
        if (_totalJuiceAvailable > 0)
        {
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

        if (Api is ICoreClientAPI)
            EnsureAnimatorReady();
            
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
        tree.SetFloat("accumulatedEnergyForPress", this._accumulatedEnergyForPress);
    }
    
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        
        if (!FruitSlot.Empty && _totalJuiceAvailable > 0)
        {
            var props = GetJuiceableProperties(FruitSlot.Itemstack);
            if (props != null && props.LiquidStack?.ResolvedItemstack != null)
            {
                string juiceName = props.LiquidStack.ResolvedItemstack.GetName();
                
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
            
            if (PowerBehavior.PowerSetting >= _maxConsumption * 0.1f && _totalJuiceAvailable > 0 && IsJuiceCompatible() && !IsFull())
            {
                var energyPerLitre = MyMiniLib.GetAttributeFloat(Block, "energyPerLitre", 1000f);
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
        
        if (!FruitSlot.Empty && !IsJuiceCompatible())
        {
            dsc.AppendLine(Lang.Get("Warning: Incompatible liquid in tank"));
        }
    }
    
    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressiveqol:efruitpress-title-gui");
    
    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        var creative = byPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (!creative && Api?.World != null)
        {
            DropInvSlot(FruitSlot);
            DropInvSlot(MashSlot);
        }

        if (FruitSlot != null) FruitSlot.Itemstack = null;
        if (MashSlot != null) MashSlot.Itemstack = null;
        if (LiquidSlot != null) LiquidSlot.Itemstack = null;

        base.OnBlockBroken(byPlayer);
    }

    private void DropInvSlot(ItemSlot slot)
    {
        if (slot?.Itemstack is not { StackSize: > 0 } stack || stack.Collectible == null)
            return;
        Api.World.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        slot.Itemstack = null;
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
        _liquidMesh = null;
        _liquidMeshKey = null;
    }
    
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._clientDialog?.TryClose();
        StopSound();
        _liquidMesh = null;
        _liquidMeshKey = null;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (IsFormed)
            EnsureAnimatorReady();

        var mesh = GetLiquidMesh();
        if (mesh != null)
            mesher.AddMeshData(mesh);
        return base.OnTesselation(mesher, tesselator);
    }

    private MeshData? GetLiquidMesh()
    {
        if (_capi == null || LiquidSlot == null || LiquidSlot.Empty)
            return null;
        var fill = LiquidCapacity > 0 ? LiquidAmount / LiquidCapacity : 0f;
        if (fill < 0.02f)
            return null;
        var stack = LiquidStack;
        if (stack?.Collectible == null)
            return null;

        var rotY = Block?.Shape?.rotateY ?? 0;
        if (Math.Abs(rotY) < 0.01f)
        {
            rotY = Block?.Variant?["side"] switch
            {
                "east" => 270,
                "south" => 180,
                "west" => 90,
                _ => 0
            };
        }

        var key = stack.Collectible.Code + "@" + (int)(fill * 40) + "@" + rotY;
        if (_liquidMesh != null && _liquidMeshKey == key)
            return _liquidMesh;

        var props = BlockLiquidContainerBase.GetContainableProps(stack);
        var shape = Vintagestory.API.Common.Shape.TryGet(_capi,
            new AssetLocation("electricalprogressiveqol", "shapes/block/efruitpress/liquidcontents.json"))?.Clone();
        if (shape == null)
            return null;

        ITexPositionSource texSource;
        if (props?.Texture != null)
            texSource = new ContainerTextureSource(_capi, stack, props.Texture);
        else
            texSource = new ContainerTextureSource(_capi, stack, new CompositeTexture(new AssetLocation("game", "block/liquid/water")));

        if (props?.ClimateColorMap != null && shape.Elements != null)
        {
            foreach (var el in shape.Elements)
                el.ClimateColorMap = props.ClimateColorMap;
        }

        _capi.Tesselator.TesselateShape("efruitpress-liquid", shape, out var mesh, texSource);
        if (mesh == null || mesh.VerticesCount <= 0)
            return null;

        mesh.Translate(0, GameMath.Clamp(fill, 0.02f, 1f) * (19.5f / 16f), 0);
        if (Math.Abs(rotY) > 0.01f)
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotY * GameMath.DEG2RAD, 0);

        _liquidMesh = mesh;
        _liquidMeshKey = key;
        return mesh;
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