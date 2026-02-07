using System;
using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Utils;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
namespace ElectricalProgressive.Content.Block.EHorn;
public class BlockEntityEHorn : BlockEntityContainer, IHeatSource
{
    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "ehorndata";
    private readonly InventoryEHorn _inventory;
    private readonly Vec3d _tmpPos = new();
    private ILoadedSound? _ambientSound;
    private bool _burning;
    private bool _clientSidePrevBurning;
    private double _lastTickTotalHours;
    private double _lastPlaySoundDin = 0;
    private float MaxTargetTemp => MyMiniLib.GetAttributeFloat(this.Block, "maxTargetTemp", 1100.0F);
    private int MaxConsumption => MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    private ForgeContentsRenderer? _renderer;
    private WeatherSystemBase? _weatherSystem;
    private long _listenerId;
    private Vec3f _blockRotRad = new ();
    public ItemStack? Contents => _inventory[0]?.Itemstack;
    public bool IsBurning
    {
        get => this._burning;
        set
        {
            if (this._burning != value)
            {
                if (value && !this._burning)
                {
                    this._renderer?.SetContents(this.Contents, 0f, this._burning, false, 0f);
                    this._lastTickTotalHours = this.Api.World.Calendar.TotalHours;
                    this.MarkDirty();
                }
                this._burning = value;
            }
        }
    }
    public BlockEntityEHorn()
    {
        _inventory = new InventoryEHorn("ehorndata-" + Pos, null, null);
    }
    /// <summary>
    /// Отвечает за тепло отдаваемое в окружающую среду
    /// </summary>
    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return this._burning
            ? MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)
            : 0;
    }
    public override void Initialize(ICoreAPI api)
    {
        _inventory.LateInitialize("ehorndata-" + Pos, api);
        _inventory.ResolveBlocksOrItems();
        _inventory.SlotModified += OnSlotModified;
        base.Initialize(api);
        this.Contents?.ResolveBlockOrItem(api.World);
        if (api is ICoreClientAPI clientApi)
        {
            this._renderer = new ForgeContentsRenderer(this.Block, this.Pos, clientApi, _blockRotRad);
            clientApi.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque, "forge");
            this._renderer.SetContents(this.Contents, 0f, this._burning, true, 0f);
            this.RegisterGameTickListener(this.OnClientTick, 50);
        }
        this._weatherSystem = api.ModLoader.GetModSystem<WeatherSystemBase>();
        _listenerId = this.RegisterGameTickListener(this.OnCommonTick, 200);
        this._lastTickTotalHours = this.Api.World.Calendar.TotalHours;
    }
    private void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            this._renderer?.SetContents(this.Contents, 0f, this._burning, true, 0f);
            this.MarkDirty();
        }
    }
    /// <summary>
    /// Клиентский тик
    /// </summary>
    private void OnClientTick(float dt)
    {
        if (Api.Side == EnumAppSide.Client && this._clientSidePrevBurning != this._burning)
        {
            this.ToggleAmbientSounds(this.IsBurning);
            this._clientSidePrevBurning = this.IsBurning;
        }
        // рисуем дым, если горн включен
        if (this._burning && this.Api.World.Rand.NextDouble() < 0.13 && this.Block.Variant["state"] == "enabled")
            BlockEntityCoalPile.SpawnBurningCoalParticles(this.Api, this.Pos.ToVec3d().Add(0.25, 0.875, 0.25), 0.5f, 0.5f);
        if (this._renderer == null)
            return;
        this._renderer.SetContents(this.Contents, 0f, this._burning, false, 0f);
    }
    /// <summary>
    /// Тики в общем потоке
    /// </summary>
    private void OnCommonTick(float dt)
    {
        var beh = GetBehavior<BEBehaviorEHorn>();
        if (beh == null)
            return;
        var num1 = this.Api.World.Calendar.TotalHours - this._lastTickTotalHours;
        if (this.Contents != null) //внутри есть что-то?
        {
            var temperature = this.Contents.Collectible.GetTemperature(this.Api.World, this.Contents);
            var power = beh.getPowerReceive();
            // запитано?
            if (power > 0)
                this.IsBurning = true;
            else
            {
                this.IsBurning = false;
            }
            if (power > 0.0F && this.Block.Variant["state"] == "disabled")
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
            }
            else if (power == 0.0F && this.Block.Variant["state"] == "enabled")
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
            }
            if (temperature < power * MaxTargetTemp / MaxConsumption)
            {
                var num2 = (float)(num1 * 1500.0);
                this.Contents.Collectible.SetTemperature(this.Api.World, this.Contents, Math.Min(power * 11F, temperature + num2));
            }
            else
            {
                if (this.Api.Side != EnumAppSide.Client && this.Api.World.Calendar.TotalHours - this._lastPlaySoundDin > 1)
                {
                    Api.World.PlaySoundAt(new AssetLocation("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
                    this._lastPlaySoundDin = this.Api.World.Calendar.TotalHours;
                }
            }
        }
        else
        {
            this.IsBurning = false;
            if (this.Block.Variant["state"] == "enabled")
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
        }
        this._tmpPos.Set(this.Pos.X + 0.5, this.Pos.Y + 0.5, this.Pos.Z + 0.5);
        double rainLevel = 0;
        var rainCheck = this.Api.Side == EnumAppSide.Server
                        && this.Api.World.Rand.NextDouble() < 0.15
                        && this.Api.World.BlockAccessor.GetRainMapHeightAt(this.Pos.X, this.Pos.Z) <= this.Pos.Y
                        && (rainLevel = this._weatherSystem!.GetPrecipitation(this._tmpPos)) > 0.1;
        if (rainCheck && this.Api.World.Rand.NextDouble() < rainLevel * 5)
        {
            var playSound = false;
            if (this._burning)
            {
                playSound = true;
                this.MarkDirty();
            }
            var temp = this.Contents == null
                ? 0
                : this.Contents.Collectible.GetTemperature(this.Api.World, this.Contents);
            if (temp > 20)
            {
                playSound = temp > 100;
                this.Contents?.Collectible.SetTemperature(this.Api.World, this.Contents, Math.Min(beh.getPowerReceive() * 11F, temp - 8), false);
                this.MarkDirty();
            }
            if (playSound)
                this.Api.World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), this.Pos.X + 0.5, this.Pos.Y + 0.75, this.Pos.Z + 0.5, null, false, 16);
        }
        this._lastTickTotalHours = this.Api.World.Calendar.TotalHours;
    }
    public void ToggleAmbientSounds(bool on)
    {
        if (this.Api.Side != EnumAppSide.Client)
            return;
        if (!on)
        {
            this._ambientSound?.Stop();
            this._ambientSound?.Dispose();
            this._ambientSound = null;
            return;
        }
        if (this._ambientSound is { IsPlaying: true })
            return;
        this._ambientSound = ((IClientWorldAccessor)this.Api.World).LoadSound(new()
        {
            Location = new AssetLocation("sounds/effect/embers.ogg"),
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 1
        });
        this._ambientSound.Start();
    }


    /// <summary>
    /// Вызывается при взаимодействии игрока с блоком 
    /// </summary>
    internal bool OnPlayerInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        //проверяем не сгорел ли прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Pos) is BlockEntityEHorn entity &&
            entity.ElectricalProgressive != null &&
            entity.ElectricalProgressive.AllEparams != null)
        {
            var hasBurnout = entity.ElectricalProgressive.AllEparams.Any(e => e.burnout);
            if (hasBurnout)
                return false;
        }
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (!byPlayer.Entity.Controls.ShiftKey)
        {
            if (this.Contents == null)
                return false;
            var split = this.Contents.Clone();
            split.StackSize = 1;
            this.Contents.StackSize--;
            if (this.Contents.StackSize == 0)
            {
                _inventory[0].Itemstack = null;
            }
            if (!byPlayer.InventoryManager.TryGiveItemstack(split))
                world.SpawnItemEntity(split, this.Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            this._renderer?.SetContents(this.Contents, 0f, this._burning, true, 0f);
            this.MarkDirty();
            this.Api.World.PlaySoundAt(new("sounds/block/ingot"), this.Pos.X, this.Pos.Y, this.Pos.Z, byPlayer, false);
            return true;
        }
        if (slot.Itemstack == null)
            return false;
        var firstCodePart = slot.Itemstack.Collectible.FirstCodePart();
        var forgableGeneric = slot.Itemstack.Collectible.Attributes?.IsTrue("forgable") == true;
        var heatable = firstCodePart == "ingot" || firstCodePart == "metalplate" ||
                       firstCodePart == "workitem" || forgableGeneric;
        // Добавляем в горн предметы, которые можно нагреть
        if (this.Contents == null && heatable)
        {
            _inventory[0].Itemstack = slot.Itemstack.Clone();
            this.Contents.StackSize = 1;
            slot.TakeOut(1);
            slot.MarkDirty();
            this._renderer?.SetContents(this.Contents, 0f, this._burning, true, 0f);
            this.MarkDirty();
            this.Api.World.PlaySoundAt(new AssetLocation("sounds/block/ingot"), this.Pos.X, this.Pos.Y, this.Pos.Z,
                byPlayer, false);
            this.IsBurning = true;
            return true;
        }
        // Merge heatable item (объединение с учётом температуры)
        if (!forgableGeneric && this.Contents != null &&
            this.Contents.Equals(this.Api.World, slot.Itemstack, GlobalConstants.IgnoredStackAttributes) &&
            this.Contents.StackSize < 4 &&
            this.Contents.StackSize < this.Contents.Collectible.MaxStackSize)
        {
            var myTemp = this.Contents.Collectible.GetTemperature(this.Api.World, this.Contents);
            var histemp = slot.Itemstack.Collectible.GetTemperature(this.Api.World, slot.Itemstack);
            this.Contents.Collectible.SetTemperature(world, this.Contents,
                (myTemp * this.Contents.StackSize + histemp * 1) / (this.Contents.StackSize + 1));
            this.Contents.StackSize++;
            slot.TakeOut(1);
            slot.MarkDirty();
            this._renderer?.SetContents(this.Contents, 0f, this._burning, true, 0f);
            this.Api.World.PlaySoundAt(new("sounds/block/ingot"), this.Pos.X, this.Pos.Y, this.Pos.Z, byPlayer, false);
            this.MarkDirty();
            return true;
        }
        return false;
    }
    /// <summary>
    /// Вызывается при установке блока в мир
    /// </summary>
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        var electricity = this.ElectricalProgressive;
        if (electricity == null || byItemStack == null)
            return;
        //задаем электрические параметры блока/проводника
        LoadEProperties.Load(this.Block, this);
    }
    /// <summary>
    /// Вызывается при удалении блока из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        this._renderer?.Dispose();
        this._renderer = null;
        this._ambientSound?.Dispose();
        this._ambientSound = null;
        this._weatherSystem?.Dispose();
        this._weatherSystem = null;
    }
    /// <summary>
    /// Вызывается при разрушении блока игроком
    /// </summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (this.Contents != null)
            this.Api.World.SpawnItemEntity(this.Contents, this.Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        this._ambientSound?.Dispose();
        this._ambientSound = null;
        this._weatherSystem?.Dispose();
        this._weatherSystem = null;
        this._renderer?.Dispose();
        this._renderer = null;
    }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        // Загружаем инвентарь
        Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
        {
            Inventory.Api = Api;
            Inventory.ResolveBlocksOrItems();
        }
        this._burning = tree.GetInt("burning") > 0;
        this._lastTickTotalHours = tree.GetDouble("lastTickTotalHours");
        if (this.Api != null)
            this.Contents?.ResolveBlockOrItem(this.Api.World);
        this._renderer?.SetContents(this.Contents, 0f, this._burning, true, 0f);
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        // Сохраняем инвентарь
        ITreeAttribute invtree = new TreeAttribute();
        Inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetInt("burning", this._burning ? 1 : 0);
        tree.SetDouble("lastTickTotalHours", this._lastTickTotalHours);
    }


    /// <summary>
    /// Получение информации о блоке
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (this.Contents == null)
            return;
        var temperature = (int)this.Contents.Collectible.GetTemperature(this.Api.World, this.Contents);
        if (temperature <= 25)
            dsc.AppendLine(Lang.Get("forge-contentsandtemp-cold", (object)this.Contents.StackSize, (object)this.Contents.GetName()));
        else
            dsc.AppendLine(Lang.Get("forge-contentsandtemp", (object)this.Contents.StackSize, (object)this.Contents.GetName(), (object)temperature));
    }
    /// <summary>
    /// Маппинг блоков и предметов при загрузке схемы
    /// </summary>
    public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        foreach (var slot in _inventory)
        {
            if (slot.Itemstack == null)
                continue;
            if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForNewMappings))
            {
                slot.Itemstack = null;
            }
        }
    }
    /// <summary>
    /// Сохраняет соответствия блоков и предметов в словари
    /// </summary>
    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping,
        Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var slot in _inventory)
        {
            if (slot.Itemstack == null)
                continue;
            if (slot.Itemstack.Class == EnumItemClass.Item)
            {
                itemIdMapping[slot.Itemstack.Item.Id] = slot.Itemstack.Item.Code;
            }
            else
            {
                blockIdMapping[slot.Itemstack.Block.BlockId] = slot.Itemstack.Block.Code;
            }
        }
    }
    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        // очистка мусора
        this._renderer?.Dispose();
        this._renderer = null;
        this._ambientSound?.Dispose();
        this._ambientSound = null;
        _weatherSystem?.Dispose();
        _weatherSystem = null;
        // отписываемся от тиков
        UnregisterGameTickListener(_listenerId);
    }
    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
}