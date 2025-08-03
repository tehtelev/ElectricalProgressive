using System;
using System.IO;
using System.Linq;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static System.Reflection.Metadata.BlobBuilder;

namespace ElectricalProgressive.Content.Block.EFreezer2;

class BlockEntityEFreezer2 : ContainerEFreezer2, ITexPositionSource
{
    public bool IsOpened { get; set; }
    private int _closedDelay;

    private InventoryBase _inventory;
    private GuiEFreezer2? _freezerDialog;
    private ICoreClientAPI _capi;

    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;

    private readonly int _maxConsumption;

    private long listenerId;


    public BlockEntityEFreezer2()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        IsOpened = false;
        _closedDelay = 0;

        // Инициализируем инвентарь раньше всего
        _inventory = new InventoryGeneric(6, null, null);
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "efreezer";


    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы генератора
    /// </summary>
    public BlockEntityAnimationUtil animUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }


    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        // Очищаем ссылки на объекты для предотвращения утечек памяти
        _freezerDialog?.TryClose();
        _freezerDialog?.Dispose();
        _freezerDialog = null;
        _capi = null!;
        _meshes = null!;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null!;
        animUtil?.Dispose();

        // Удаляем слушатель тиков
        UnregisterGameTickListener(listenerId);
    }




    /// <summary>
    /// Запускает анимацию открытия дверцы
    /// </summary>
    public void OpenLid()
    {
        //animUtil.Dispose();
        animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));

        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
        {
            animUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "open",
                Code = "open",
                AnimationSpeed = 1.8f,
                EaseOutSpeed = 6,
                EaseInSpeed = 6                
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 11 };

            //добавляем звук
            _capi.World.PlaySoundAt(new ("electricalprogressiveqol:sounds/freezer_open.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);

        }

    }


    /// <summary>
    /// Закрывает дверцу генератора, останавливая анимацию открытия, если она запущена
    /// </summary>
    public void CloseLid()
    {
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("open") == true)
        {
            animUtil?.StopAnimation("open");

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 0 };

            //добавляем звук
            _capi.World.PlaySoundAt(new ("electricalprogressiveqol:sounds/freezer_close.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
        }
    }



    /// <summary>
    /// Получает угол поворота блока в градусах
    /// </summary>
    /// <returns></returns>
    public int GetRotation()
    {
        string side = Block.Variant["side"];
        int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }


    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        // Инициализируем инвентарь
        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

        base.Initialize(api);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;

            // инициализируем аниматор
            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
                
            }
        }

        _meshes = new MeshData[_inventory.Count];

        // Как только инвентарь изменится — подписываемся на событие изменения любого слота и перерисовываем их все
        Inventory.SlotModified += slotId =>
        {
            UpdateMeshes();
        };

        // Рисуем содержимое
        UpdateMeshes();
        MarkDirty(true);

        // Слушатель для обновления содержимого 
        listenerId=RegisterGameTickListener(FreezerTick, 500);
    }


    /// <summary>
    /// Обновляем mesh для конкретного слота
    /// </summary>
    /// <param name="slotid"></param>
    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server || _capi==null)
            return;

        if (slotid >= _inventory.Count)
            return;

        if (_inventory[slotid].Empty)
        {
            _meshes[slotid] = null;
            return;
        }

        var meshData = GenMesh(_inventory[slotid].Itemstack);
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
    /// Перемещаем mesh в нужную позицию в зависимости от слота
    /// </summary>
    /// <param name="meshData"></param>
    /// <param name="slotId"></param>
    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null)
            return;

        const float stdoffset = 0.2f;

        var (x, y) = slotId switch
        {
            0 => (-stdoffset, 1.435f),
            1 => (+stdoffset, 1.435f),
            2 => (-stdoffset, 0.81f),
            3 => (+stdoffset, 0.81f),
            4 => (-stdoffset, 0.19f),
            5 => (+stdoffset, 0.19f),
            _ => (0, 0)
        };

        if (!Inventory[slotId].Empty)
        {
            if (Inventory[slotId].Itemstack.Class == EnumItemClass.Block)
            {
                meshData.Scale(new(0.5f, 0, 0.5f), 0.53f, 0.53f, 0.53f);
                meshData.Rotate(new(0.5f, 0, 0.5f), 0, 8 * GameMath.DEG2RAD, 0);
            }
            else
            {
                meshData.Scale(new(0.5f, 0, 0.5f), 0.8f, 0.8f, 0.8f);
                meshData.Rotate(new(0.5f, 0, 0.5f), 0, 15 * GameMath.DEG2RAD, 0);
            }
        }

        meshData.Translate(x, y, 0.025f);

        var orientationRotate = Block.Variant["horizontalorientation"] switch
        {
            "east" => 270,
            "south" => 180,
            "west" => 90,
            _ => 0
        };

        meshData.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, orientationRotate * GameMath.DEG2RAD, 0);
    }

    public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;



    /// <summary>
    /// Получаем позицию текстуры по коду текстуры
    /// </summary>
    /// <param name="textureCode"></param>
    /// <returns></returns>
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

            return getOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition? getOrCreateTexPos(AssetLocation texturePath)
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
    /// Рисуем meshы
    /// </summary>
    /// <param name="stack"></param>
    /// <returns></returns>
    public MeshData? GenMesh(ItemStack stack)
    {
        if (stack == null) // если стек пустой, то ничего не рисуем
            return null;

        var meshSource = stack.Collectible as IContainedMeshSource;
        MeshData meshData;

        if (meshSource != null)
        {
            meshData = meshSource.GenMesh(stack, _capi.BlockTextureAtlas, Pos);
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

                try
                {
                    _capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
                catch (Exception e)
                {
                    Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code, e.Message);
                    meshData = null;
                }

                _capi.TesselatorManager.ThreadDispose(); // Проверьте, нужен ли этот вызов
            }
        }

        return meshData;
    }

    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tessThreadTesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        base.OnTesselation(mesher, tessThreadTesselator); // вызываем базовую логику тесселяции

        if (_meshes != null)
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                    mesher.AddMeshData(_meshes[i]);
            }
        }

        // если анимации нет, то рисуем блок базовый
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
        {
            return false;
        }

        return true;  // не рисует базовый блок, если есть анимация
    }


    /// <summary>
    /// Обновляет все meshы в инвентаре
    /// </summary>
    public void UpdateMeshes()
    {
        for (var i = 0; i < _inventory.Count; i++)
            UpdateMesh(i);

        MarkDirty(true);
    }

    /// <summary>
    /// Тики холодильника
    /// </summary>
    /// <param name="dt"></param>
    private void FreezerTick(float dt)
    {
        if (Api.Side != EnumAppSide.Server || this.Block.Variant["state"] == "burned") // если сгорел, то не тикаем
            return; 

        TryRefuel();

    }


    /// <summary>
    /// Проверяет, нужно ли размораживать или замораживать блок
    /// </summary>
    private void TryRefuel()
    {
        var beh=GetBehavior<BEBehaviorEFreezer2>();

        if (beh is null)
            return;

        // Энергии хватает?
        if (beh.PowerSetting >= _maxConsumption * 0.1F && this.Block.Variant["state"] == "melted")
        {
            var originalBlock = Api.World.BlockAccessor.GetBlock(Pos);
            var newBlockAL = originalBlock.CodeWithVariant("state", "frozen");
            var newBlock = Api.World.GetBlock(newBlockAL);
            Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
            MarkDirty();
        }

        if (beh.PowerSetting < _maxConsumption * 0.1F && this.Block.Variant["state"] == "frozen")
        {
            var originalBlock = Api.World.BlockAccessor.GetBlock(Pos);
            var newBlockAL = originalBlock.CodeWithVariant("state", "melted");
            var newBlock = Api.World.GetBlock(newBlockAL);
            Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
            MarkDirty();
        }
    }



    /// <summary>
    /// Вызывается при взаимодействии с блоком игроком
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="isOwner"></param>
    /// <param name="blockSel"></param>
    public void OnBlockInteract(IPlayer byPlayer, bool isOwner, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Server)
        {
            byte[] data;
            using (var ms = new MemoryStream())
            {
                var writer = new BinaryWriter(ms);
                var tree = new TreeAttribute();
                _inventory.ToTreeAttributes(tree);
                tree.ToBytes(writer);
                data = ms.ToArray();
            }

            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                (IServerPlayer)byPlayer,
                blockSel.Position,
                (int)EnumBlockStovePacket.OpenGUI,
                data
            );

            byPlayer.InventoryManager.OpenInventory(_inventory);
        }
        else
        {
            // Логика клиента
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        _closedDelay = tree.GetInt("closedDelay");
        IsOpened = tree.GetBool("isOpened");

        if (Api == null)
            return;

        _inventory.AfterBlocksLoaded(Api.World);
        if (Api.Side == EnumAppSide.Client)
            UpdateMeshes();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetInt("closedDelay", _closedDelay);
        tree.SetBool("isOpened", IsOpened);
    }

    public override void OnReceivedClientPacket (IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);

        if (packetid <= (int)EnumBlockEntityPacketId.Open)
            _inventory.InvNetworkUtil.HandleClientPacket(fromPlayer, packetid, data);

        if (packetid == (int)EnumBlockEntityPacketId.Close)
            fromPlayer.InventoryManager?.CloseInventory(Inventory);
    }


    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        var clientWorld = (IClientWorldAccessor)Api.World;

        if (packetid == (int)EnumBlockStovePacket.OpenGUI)
        {
            using (var ms = new MemoryStream(data))
            {
                var reader = new BinaryReader(ms);
                var tree = new TreeAttribute();
                tree.FromBytes(reader);
                Inventory.FromTreeAttributes(tree);
                Inventory.ResolveBlocksOrItems();


                if (_freezerDialog == null)
                {
                    _freezerDialog = new(Lang.Get("freezer-title-gui"), Inventory, Pos, Api as ICoreClientAPI, this);
                    _freezerDialog.OnClosed += () =>
                    {
                        _freezerDialog = null;
                    };
                }

                _freezerDialog.TryOpen();
            }
        }

        if (packetid == (int)EnumBlockEntityPacketId.Close)
        {
            clientWorld.Player.InventoryManager.CloseInventory(Inventory);

            if (_freezerDialog != null)
            {
                _freezerDialog?.TryClose();
                _freezerDialog?.Dispose();
                _freezerDialog = null;
            }
        }
    }


    /// <summary>
    /// Возвращает скорость порчи предметов в холодильнике
    /// </summary>
    /// <returns></returns>
    public override float GetPerishRate()
    {
        var initial = base.GetPerishRate();
        var side = Api.Side;
        if (GetBehavior<BEBehaviorEFreezer2>().PowerSetting < _maxConsumption * 0.1F)
            return initial;

        return 0.05F;
    }



    /// <summary>
    /// Вызывается при установке блока в мир
    /// </summary>
    /// <param name="byItemStack"></param>
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack == null)
            return;

        ElectricalProgressive.Connection = Facing.DownAll;

        // Задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

        this.ElectricalProgressive.Eparams = (
            new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }



    /// <summary>
    /// Вызывается при удалении блока
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (_freezerDialog != null)
        {
            _freezerDialog?.TryClose();
            _freezerDialog?.Dispose();
            _freezerDialog = null;
        }
    }



}