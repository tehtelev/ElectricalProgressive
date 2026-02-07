using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Facing = ElectricalProgressive.Utils.Facing;

namespace ElectricalProgressive.Content.Block.ECharger;

public class BlockEntityECharger : BlockEntityContainer, ITexPositionSource
{
    private readonly InventoryECharger _inventory;
    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "charger";

    // Новые поля для системы мешей (как в холодильнике)
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;
    private ICoreClientAPI _capi;

    private long _listenerId;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


    public Size2i AtlasSize => _capi?.BlockTextureAtlas.Size ?? ((ICoreClientAPI)Api)?.BlockTextureAtlas.Size;

    /// <summary>
    /// Необходимо для отрисовки текстур
    /// </summary>
    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            var assetLocation = default(AssetLocation?);

            // Пробуем получить текстуру из item.Textures
            if (_nowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }
            else if (_nowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (block.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }

            // Если не нашли, пробуем из shape.Textures
            if (assetLocation == null && _nowTesselatingShape != null)
            {
                _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);
            }

            // Если все еще не нашли, используем домен предмета и предполагаемый путь
            if (assetLocation == null)
            {
                var domain = _nowTesselatingObj.Code.Domain;
                assetLocation = new(domain, "textures/item/" + textureCode);
                Api.World.Logger.Warning("Текстура {0} не найдена в текстурах предмета или формы, используется путь: {1}", textureCode, assetLocation);
            }

            return GetOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
    {
        var textureAtlasPosition = _capi.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition != null)
            return textureAtlasPosition;

        // берем только base текстуру (первую из кучи наваленных)
        var pos = texturePath.Path.IndexOf("++");
        if (pos >= 0)
            texturePath.Path = texturePath.Path.Substring(0, pos);

        var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
        {
            _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out var num, out textureAtlasPosition, null, 0.005f);
        }
        else
        {
            Api.World.Logger.Warning("Текстура не найдена по пути: {0}", texturePath);
        }

        return textureAtlasPosition;
    }

    /// <summary>
    /// Конструктор блока-зарядника
    /// </summary>
    public BlockEntityECharger()
    {
        _inventory = new(1, "charger-" + Pos, null, null, null);
    }

    /// <summary>
    /// Инициализация блока-зарядника
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        Inventory.LateInitialize("charger-" + Pos, api);
        Inventory.ResolveBlocksOrItems();

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;

            // Инициализируем массив мешей как в холодильнике
            _meshes = new MeshData[this._inventory.Count];

            // Подписываемся на изменения инвентаря
            this._inventory.SlotModified += slotId =>
            {
                UpdateMeshes();
            };

            // Первоначальное создание мешей
            UpdateMeshes();
        }
        else
        {
            _listenerId = RegisterGameTickListener(OnTick, 1000);
        }

        MarkDirty(true);
    }

    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        // Очищаем ссылки как в холодильнике
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
        _capi = null;

        UnregisterGameTickListener(_listenerId); //отменяем слушатель тика, если он есть
    }

    //проверка, нужно ли заряжать
    private void OnTick(float dt)
    {
        if (this.Block.Variant["state"] == "burned") //если прибор сгорел, то нечего тут делать
            return;

        var stack = Inventory[0]?.Itemstack;

        // со стаком что-то не так?
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return;

        if (stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false))
        {
            var durability = stack.Attributes.GetInt("durability");             //текущая прочность
            var maxDurability = stack.Collectible.GetMaxDurability(stack);       //максимальная прочность

            if (durability < maxDurability && GetBehavior<BEBehaviorECharger>().PowerSetting > 0)
            {
                if (this.Block.Variant["state"] == "disabled")     //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                    MarkDirty(true);
                }

                var maxReceive = GetBehavior<BEBehaviorECharger>().PowerSetting;        // мощность в заряднике
                var consume = MyMiniLib.GetAttributeInt(stack.Item, "consume", 20);     //размер минимальной порции          
                var received = Math.Min(maxDurability - durability, maxReceive / consume); // приращение прочности
                durability += received;                                                 // новая прочность
                stack.Attributes.SetInt("durability", durability);                      // обновляем прочность в атрибутах
            }
            else
            {
                if (this.Block.Variant["state"] == "enabled")   //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                    Api.World.PlaySoundAt(new("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F); //звоним если зарядилось таки
                    MarkDirty(true);
                }
            }
        }
        else if (stack?.Block is IEnergyStorageItem)
        {
            var durability = stack.Attributes.GetInt("durability");             //текущая прочность
            var maxDurability = stack.Collectible.GetMaxDurability(stack);       //максимальная прочность

            if (durability < maxDurability && GetBehavior<BEBehaviorECharger>().PowerSetting > 0)
            {
                if (this.Block.Variant["state"] == "disabled")     //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                    MarkDirty(true);
                }

                ((IEnergyStorageItem)stack.Block).receiveEnergy(stack, GetBehavior<BEBehaviorECharger>().PowerSetting);
            }
            else
            {
                if (this.Block.Variant["state"] == "enabled")   //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                    Api.World.PlaySoundAt(new("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F); //звоним если зарядилось таки
                    MarkDirty(true);
                }
            }
        }

        MarkDirty();
    }

    /// <summary>
    /// Обновляем mesh для конкретного слота
    /// </summary>
    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
            return;

        if (slotid >= _inventory.Count)
            return;

        // В заряднике отображаем только слот 0
        if (slotid != 0)
        {
            _meshes[slotid] = null;
            return;
        }

        if (_inventory[slotid].Empty)
        {
            _meshes[slotid] = null;
            return;
        }

        var meshData = GenMesh(_inventory[slotid]);
        if (meshData != null)
        {
            TranslateMesh(meshData, slotid);
            _meshes[slotid] = meshData;
        }
        else
        {
            _meshes[slotid] = null;
        }
    }

    /// <summary>
    /// Перемещаем mesh в нужную позицию для зарядника
    /// </summary>
    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null || slotId != 0)
            return;

        var stack = this._inventory[slotId].Itemstack;
        var origin = new Vec3f(0.5f, 0.5f, 0.5f);

        if (stack.Class == EnumItemClass.Item)
        {
            var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.5F);
            var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.5F);
            var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.5F);
            var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
            var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0.4F);
            var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
            var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
            var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
            var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);


            origin.Y = 1f / 30f;
            meshData.Scale(origin, scaleX, scaleY, scaleZ);
            meshData.Translate(translateX, translateY, translateZ);
            meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);
        }
        else
        {
            meshData.Scale(origin, 0.3f, 0.3f, 0.3f);
        }
    }

    /// <summary>
    /// Генерация меша для предмета (как в холодильнике)
    /// </summary>
    public MeshData? GenMesh(ItemSlot slot)
    {
        var stack = slot.Itemstack;

        if (stack == null)
            return null;

        MeshData meshData;
        try
        {
            var meshSource = stack.Collectible as IContainedMeshSource;

            if (meshSource != null)
            {
                meshData = meshSource.GenMesh(slot, _capi.BlockTextureAtlas, Pos);
                meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
            }
            else
            {
                if (stack.Class == EnumItemClass.Block)
                {
                    meshData = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                }
                else
                {
                    _nowTesselatingObj = stack.Collectible;
                    _nowTesselatingShape = null;

                    if (stack.Item.Shape != null)
                        _nowTesselatingShape = _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);

                    _capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
            }
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code, e.Message);
            meshData = null;
        }

        return meshData;
    }

    /// <summary>
    /// Обновляет все meshы в инвентаре
    /// </summary>
    public void UpdateMeshes()
    {
        for (var i = 0; i < this._inventory.Count; i++)
            UpdateMesh(i);

        MarkDirty(true);
    }

    internal bool OnPlayerInteract(IPlayer byPlayer, Vec3d hit)
    {
        if (Inventory[0].Itemstack != null)
        {
            return TakeFromSlot(byPlayer, 0);
        }

        return PutInSlot(byPlayer, 0);
    }

    bool PutInSlot(IPlayer player, int slot)
    {
        var stack = player.InventoryManager.ActiveHotbarSlot.Itemstack;
        // со стаком что-то не так?
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return false;

        var isValid = stack.Class == EnumItemClass.Block
            ? stack.Block is IEnergyStorageItem
            : stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false);
        if (!isValid)
            return false;

        player.InventoryManager.ActiveHotbarSlot.TryPutInto(Api.World, Inventory[slot]);

        DidInteract(player);

        return true;
    }

    /// <summary>
    /// Извлекает предмет из слота инвентаря
    /// </summary>
    /// <param name="player"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    bool TakeFromSlot(IPlayer player, int slot)
    {
        var stack = Inventory[slot].TakeOutWhole();

        if (!player.InventoryManager.TryGiveItemstack(stack))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        // обновляем модель блока
        if (this.Block.Variant["state"] == "enabled")
            Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
        else if (this.Block.Variant["state"] == "burned")
            Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "burned")).BlockId, Pos);

        DidInteract(player);

        return true;
    }

    void DidInteract(IPlayer player)
    {
        Api.World.PlaySoundAt(new AssetLocation("sounds/player/buildhigh"), Pos.X, Pos.Y, Pos.Z, player, false);
        if (Api is ICoreClientAPI)
            UpdateMeshes();

        MarkDirty(true);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        var electricity = ElectricalProgressive;

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
    }

    /// <summary>
    /// Вызывается при разрушении блока игроком
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        var stack = Inventory[0].Itemstack;
        if (stack != null)
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var clientApi = (ICoreClientAPI)Api;
        var block = Api.World.BlockAccessor.GetBlock(Pos);
        var mesh = clientApi.TesselatorManager.GetDefaultBlockMesh(block);

        if (mesh == null || mesher == null)
            return true;

        mesher.AddMeshData(mesh);

        // Отрисовываем меши предметов (как в холодильнике)
        if (_meshes != null)
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                    mesher.AddMeshData(_meshes[i]);
            }
        }

        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
        {
            Inventory.Api = Api;
            Inventory.ResolveBlocksOrItems();
        }

        if (Api is ICoreClientAPI)
        {
            UpdateMeshes(); // обновляем меши при загрузке
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        Inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        foreach (var slot in Inventory)
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

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        foreach (var slot in Inventory)
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
    /// Информация о блоке
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="stringBuilder"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        var stack = Inventory[0]?.Itemstack; //стак инвентаря

        // со стаком что-то не так?
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return;

        if (stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false)) //предмет
        {
            var consume = MyMiniLib.GetAttributeInt(stack.Item, "consume", 20); //количество энергии, которое потребляет блок порцией
            var energy = stack.Attributes.GetInt("durability") * consume;
            var maxEnergy = stack.Collectible.GetMaxDurability(stack) * consume;

            stringBuilder.AppendLine();
            stringBuilder.AppendLine(stack.GetName());
            stringBuilder.AppendLine(StringHelper.Progressbar(energy * 100.0F / maxEnergy));
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
        }
        else if (stack?.Block is IEnergyStorageItem) //блок
        {
            var consume = MyMiniLib.GetAttributeInt(stack.Block, "consume", 20); //количество энергии, которое потребляет блок порцией
            var energy = stack.Attributes.GetInt("durability") * consume;
            var maxEnergy = stack.Collectible.GetMaxDurability(stack) * consume;

            stringBuilder.AppendLine();
            stringBuilder.AppendLine(stack.GetName());
            stringBuilder.AppendLine(StringHelper.Progressbar(energy * 100.0F / maxEnergy));
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
        }
    }
}

