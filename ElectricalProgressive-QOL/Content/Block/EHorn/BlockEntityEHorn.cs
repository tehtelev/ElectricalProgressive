using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHorn;

public class BlockEntityEHorn : BlockEntityEBase, IHeatSource
{
    private readonly Vec3d tmpPos = new();
    private ILoadedSound? ambientSound;
    private bool burning;
    private bool clientSidePrevBurning;
    private double lastTickTotalHours;
    private double lastPlaySoundDin = 0;
    private float maxTargetTemp => MyMiniLib.GetAttributeFloat(this.Block, "maxTargetTemp", 1100.0F);
    private int maxConsumption => MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);

    private ForgeContentsRenderer? renderer;
    private WeatherSystemBase? weatherSystem;

    private long listenerId;

    public ItemStack? Contents { get; private set; }

    public bool IsBurning
    {
        get => this.burning;
        set
        {
            if (this.burning != value)
            {
                if (value && !this.burning)
                {
                    this.renderer?.SetContents(this.Contents, 0, this.burning, false);
                    this.lastTickTotalHours = this.Api.World.Calendar.TotalHours;
                    this.MarkDirty();
                }

                this.burning = value;
            }
        }
    }

    /// <summary>
    /// Отвечает за тепло отдаваемое в окружающую среду
    /// </summary>
    /// <param name="world"></param>
    /// <param name="heatSourcePos"></param>
    /// <param name="heatReceiverPos"></param>
    /// <returns></returns>
    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return this.burning
            ? MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)
            : 0;
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        this.Contents?.ResolveBlockOrItem(api.World);

        if (api is ICoreClientAPI clientApi)
        {
            this.renderer = new(this.Pos, clientApi);
            clientApi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "forge");
            this.renderer.SetContents(this.Contents, 0, this.burning, true);

            this.RegisterGameTickListener(this.OnClientTick, 50);
        }

        this.weatherSystem = api.ModLoader.GetModSystem<WeatherSystemBase>();

        listenerId=this.RegisterGameTickListener(this.OnCommonTick, 200);

        this.lastTickTotalHours = this.Api.World.Calendar.TotalHours;
    }

    /// <summary>
    /// Клиентский тик
    /// </summary>
    /// <param name="dt"></param>
    private void OnClientTick(float dt)
    {
        if (Api.Side == EnumAppSide.Client && this.clientSidePrevBurning != this.burning)
        {
            this.ToggleAmbientSounds(this.IsBurning);
            this.clientSidePrevBurning = this.IsBurning;
        }

        // рисуем дым, если горн включен
        if (this.burning && this.Api.World.Rand.NextDouble() < 0.13 && this.Block.Variant["state"] == "enabled")
            BlockEntityCoalPile.SpawnBurningCoalParticles(this.Api, this.Pos.ToVec3d().Add(0.25, 0.875, 0.25), 0.5f, 0.5f);

        if (this.renderer == null)
            return;

        this.renderer.SetContents(this.Contents, 0, this.burning, false);
    }

    /// <summary>
    /// Тики в общем потоке
    /// </summary>
    /// <param name="dt"></param>
    private void OnCommonTick(float dt)
    {
        if (this.burning)
        {
            var num1 = this.Api.World.Calendar.TotalHours - this.lastTickTotalHours;
            if (this.Contents != null)  //внутри есть что-то?
            {
                var temperature = this.Contents.Collectible.GetTemperature(this.Api.World, this.Contents);
                var power = GetBehavior<BEBehaviorEHorn>().getPowerReceive();

                if (power > 0.0F && this.Block.Variant["state"] == "disabled")
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                }
                else if (power == 0.0F && this.Block.Variant["state"] == "enabled")
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                }

                if (temperature < power * maxTargetTemp / maxConsumption)
                {
                    var num2 = (float)(num1 * 1500.0);

                    this.Contents.Collectible.SetTemperature(this.Api.World, this.Contents, Math.Min(power * 11F, temperature + num2));
                }
                else
                {

                    if (this.Api.Side != EnumAppSide.Client && this.Api.World.Calendar.TotalHours - this.lastPlaySoundDin > 1)  //если нагрелось до максимума, то какждый раз в час звоним, чтобы игрок не забыл за горн
                    {
                        Api.World.PlaySoundAt(new AssetLocation("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
                        this.lastPlaySoundDin = this.Api.World.Calendar.TotalHours;
                    }
                }

            }
            else
            {
                this.IsBurning = false;

                if (this.Block.Variant["state"] == "enabled")
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
            }
        }

        this.tmpPos.Set(this.Pos.X + 0.5, this.Pos.Y + 0.5, this.Pos.Z + 0.5);

        double rainLevel = 0;

        var rainCheck = this.Api.Side == EnumAppSide.Server
                        && this.Api.World.Rand.NextDouble() < 0.15
                        && this.Api.World.BlockAccessor.GetRainMapHeightAt(this.Pos.X, this.Pos.Z) <= this.Pos.Y
                        && (rainLevel = this.weatherSystem!.GetPrecipitation(this.tmpPos)) > 0.1;

        if (rainCheck && this.Api.World.Rand.NextDouble() < rainLevel * 5)
        {
            var playSound = false;

            if (this.burning)
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
                this.Contents?.Collectible.SetTemperature(this.Api.World, this.Contents, Math.Min(GetBehavior<BEBehaviorEHorn>().getPowerReceive() * 11F, temp - 8), false);
                this.MarkDirty();
            }

            if (playSound)
                this.Api.World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), this.Pos.X + 0.5, this.Pos.Y + 0.75, this.Pos.Z + 0.5, null, false, 16);
        }

        this.lastTickTotalHours = this.Api.World.Calendar.TotalHours;
    }

    public void ToggleAmbientSounds(bool on)
    {
        if (this.Api.Side != EnumAppSide.Client)
            return;

        if (!on)
        {
            this.ambientSound?.Stop();
            this.ambientSound?.Dispose();
            this.ambientSound = null;
            return;
        }

        if (this.ambientSound is { IsPlaying: true })
            return;

        this.ambientSound = ((IClientWorldAccessor)this.Api.World).LoadSound(new()
        {
            Location = new AssetLocation("sounds/effect/embers.ogg"),
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 1
        });

        this.ambientSound.Start();
    }


    /// <summary>
    /// Вызывается при взаимодействии игрока с блоком
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    internal bool OnPlayerInteract(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        //проверяем не сгорел ли прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Pos) is BlockEntityEHorn entity && entity.AllEparams != null)
        {
            var hasBurnout = entity.AllEparams.Any(e => e.burnout);
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
                this.Contents = null;

            if (!byPlayer.InventoryManager.TryGiveItemstack(split))
                world.SpawnItemEntity(split, this.Pos.ToVec3d().Add(0.5, 0.5, 0.5));

            this.renderer?.SetContents(this.Contents, 0, this.burning, true);
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
            this.Contents = slot.Itemstack.Clone();
            this.Contents.StackSize = 1;

            slot.TakeOut(1);
            slot.MarkDirty();

            this.renderer?.SetContents(this.Contents, 0, this.burning, true);
            this.MarkDirty();
            this.Api.World.PlaySoundAt(new AssetLocation("sounds/block/ingot"), this.Pos.X, this.Pos.Y, this.Pos.Z,
                byPlayer, false);

            this.IsBurning = true;

            return true;
        }

        // Merge heatable item
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

            this.renderer?.SetContents(this.Contents, 0, this.burning, true);
            this.Api.World.PlaySoundAt(new("sounds/block/ingot"), this.Pos.X, this.Pos.Y, this.Pos.Z, byPlayer, false);

            this.MarkDirty();

            return true;
        }

        return false;
    }




    /// <summary>
    /// Вызывается при установке блока в мир
    /// </summary>
    /// <param name="byItemStack"></param>
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        var electricity = this.ElectricalProgressive;

        if (electricity == null || byItemStack == null)
            return;

        electricity.Connection = Facing.DownAll;

        //задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(byItemStack!.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack!.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

        this.ElectricalProgressive!.Connection = Facing.DownAll;
        this.ElectricalProgressive.Eparams = (
            new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }



    /// <summary>
    /// Вызывается при удалении блока из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        this.renderer?.Dispose();
        this.renderer = null;

        this.ambientSound?.Dispose();
        this.ambientSound = null;

        this.weatherSystem?.Dispose();
        this.weatherSystem = null;

    }

    /// <summary>
    /// Вызывается при разрушении блока игроком
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);

        if (this.Contents != null)
            this.Api.World.SpawnItemEntity(this.Contents, this.Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        this.ambientSound?.Dispose();
        this.ambientSound = null;

        this.weatherSystem?.Dispose();
        this.weatherSystem = null;

        this.renderer?.Dispose();
        this.renderer = null;

    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        this.Contents = tree.GetItemstack("contents");
        this.burning = tree.GetInt("burning") > 0;
        this.lastTickTotalHours = tree.GetDouble("lastTickTotalHours");

        if (this.Api != null)
            this.Contents?.ResolveBlockOrItem(this.Api.World);

        this.renderer?.SetContents(this.Contents, 0, this.burning, true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetItemstack("contents", this.Contents);

        tree.SetInt("burning", this.burning ? 1 : 0);

        tree.SetDouble("lastTickTotalHours", this.lastTickTotalHours);
    }

    /// <summary>
    /// Получение информации о блоке 
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="dsc"></param>
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
    /// <param name="worldForNewMappings"></param>
    /// <param name="oldBlockIdMapping"></param>
    /// <param name="oldItemIdMapping"></param>
    /// <param name="schematicSeed"></param>
    /// <param name="resolveImports"></param>
    public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);

        if (this.Contents?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForNewMappings) == false)
        {
            this.Contents = null;
        }
    }



    /// <summary>
    /// Сохраняет соответствия блоков и предметов в словари
    /// </summary>
    /// <param name="blockIdMapping"></param>
    /// <param name="itemIdMapping"></param>
    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping,
        Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);

        if (this.Contents != null)
        {
            if (this.Contents.Class == EnumItemClass.Item)
            {
                blockIdMapping[this.Contents.Id] = this.Contents.Item.Code;
            }
            else
            {
                itemIdMapping[this.Contents.Id] = this.Contents.Block.Code;
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
        this.renderer?.Dispose();
        this.renderer = null;

        this.ambientSound?.Dispose();
        this.ambientSound = null;

        weatherSystem?.Dispose();
        weatherSystem = null;

        // отписываемся от тиков
        UnregisterGameTickListener(listenerId);

    }
}