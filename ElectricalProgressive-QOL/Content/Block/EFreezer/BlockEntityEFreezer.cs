using ElectricalProgressive.Content.Block.EFreezer;
using ElectricalProgressive.Content.Block.EStove;
using ElectricalProgressive.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static System.Reflection.Metadata.BlobBuilder;

namespace ElectricalProgressive.Content.Block.EFreezer;

class BlockEntityEFreezer : ContainerEFreezer, ITexPositionSource
{
    public bool IsOpened { get; set; }
    private int _closedDelay;

    private static MeshData? _mesh; // кеш для меша, который используется в анимации. Кеш нужен, чтобы не загружать меш из ресурсов каждый раз при тесселяции блока, а использовать уже загруженный и обработанный меш.
    private static Shape? _resultingShape; // кеш для формы, которая используется в анимации. Кеш нужен, чтобы не загружать форму из ресурсов каждый раз при тесселяции блока, а использовать уже загруженную и обработанную форму.

    private InventoryBase _inventory;
    private GuiEFreezer? _freezerDialog;
    private ICoreClientAPI _capi;

    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;

    private readonly int _maxConsumption;

    private long _listenerId;

    private double _accumulatedColdHours = 0; // накопленные часы холода
    private double _lastUpdateTime = 0;  // время последнего обновления в часах
    private bool _wasPowered = false;    // запитан
    private const double _maxColdHours = 6.0; // максимальное количество часов холода


    public BlockEntityEFreezer()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        IsOpened = false;
        _closedDelay = 0;

        // Инициализируем инвентарь раньше всего
        _inventory = new InventoryEFreezer(6, null, null);
        
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "efreezer";


    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы
    /// </summary>
    public BlockEntityAnimationUtil animUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }


    /// <summary>
    /// Обновденение состояния холода
    /// </summary>
    /// <param name="deltaHours"></param>
    /// <param name="powered"></param>
    private void UpdateColdState(double deltaHours, bool powered)
    {
        if (deltaHours <= 0)
            return;

        // холодильник запитан?
        if (powered)
        {
            _accumulatedColdHours += deltaHours;
            if (_accumulatedColdHours > _maxColdHours) _accumulatedColdHours = _maxColdHours;
        }
        else
        {
            _accumulatedColdHours -= deltaHours;
            if (_accumulatedColdHours < 0) _accumulatedColdHours = 0;
        }
        MarkDirty();
    }


    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        _wasPowered = GetBehavior<BEBehaviorEFreezer>()?.PowerSetting >= _maxConsumption * 0.1F;
        MarkDirty(true);

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
        _mesh.Dispose();
        _resultingShape = null;
        // Удаляем слушатель тиков
        UnregisterGameTickListener(_listenerId);
    }




    /// <summary>
    /// Запускает анимацию открытия дверцы
    /// </summary>
    public void OpenLid()
    {
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("close") == true)
        {
            animUtil?.StopAnimation("close");
        }

        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
        {
            animUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "open",
                Code = "open",
                AnimationSpeed = 1.4f,
                EaseOutSpeed = 10,
                EaseInSpeed = 10
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 11 };


            //добавляем звук
            _capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_open.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);

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

            animUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "close",
                Code = "close",
                AnimationSpeed = 1.4f,
                EaseOutSpeed = 10,
                EaseInSpeed = 10
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 0 };


            //добавляем звук
            _capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_close.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
        }

    }

    /// <summary>
    /// Запускает анимацию работы
    /// </summary>
    public void StartWorkingAnim()
    {
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false && _wasPowered)
        {
            animUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 1f,
                EaseOutSpeed = 15,
                EaseInSpeed = 15,
            });


        }

    }


    /// <summary>
    /// Останавливает анимацию работы
    /// </summary>
    public void StopWorkingAnim()
    {
        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            animUtil?.StopAnimation("work-on");

        }
    }

    /// <summary>
    /// Получает угол поворота блока в градусах
    /// </summary>
    /// <returns></returns>
    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }


    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {


        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

        base.Initialize(api);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;

            // инициализируем аниматор
            if (animUtil != null)
            {
                PrepareAnimUtil(api, InventoryClassName);
                animUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
            }
        }

        if (api.Side == EnumAppSide.Server)
        {
            // обновляем состояние холода при загрузке блока
            double now = api.World.Calendar.TotalHours;
            if (_lastUpdateTime > 0)
            {
                double delta = now - _lastUpdateTime;
                if (delta > 0)
                {
                    UpdateColdState(delta, _wasPowered);
                }
            }
            _lastUpdateTime = now;
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
        _listenerId = RegisterGameTickListener(FreezerTick, 500);
    }

    /// <summary>
    /// Подготавливает анимационный утилит для блока, загружая меш и форму из ресурсов
    /// </summary>
    /// <param name="api"></param>
    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (_mesh == null || _resultingShape == null)
        {
            AssetLocation shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            Shape _shape = Shape.TryGet(api, shapePath);

            _mesh = animUtil.CreateMesh(cacheDictKey, _shape, out _resultingShape, null);

        }
    }


    /// <summary>
    /// Обновляем mesh для конкретного слота
    /// </summary>
    /// <param name="slotid"></param>
    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
            return;

        if (slotid >= _inventory.Count)
            return;

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
    /// Перемещаем mesh в нужную позицию в зависимости от слота
    /// </summary>
    /// <param name="meshData"></param>
    /// <param name="slotId"></param>
    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null)
            return;

        ItemStack itemstack = Inventory[slotId].Itemstack;
        if (itemstack == null)
            return;// Though typically meshData null if empty

        bool isBlock = itemstack.Class == EnumItemClass.Block;
        bool isCarcass = itemstack.Collectible.Code.Domain.Contains("butchering") &&
                         itemstack.Collectible.Code.Path.Contains("dead");

        var origin = new Vec3f(0.5f, 0.5f, 0.5f);

        if (isBlock)
        {
            meshData.Scale(origin, 0.53f, 0.53f, 0.53f);
            meshData.Translate(0, -0.14f, 0);
            // meshData.Rotate(origin, 90 * GameMath.DEG2RAD, 0, 8 * GameMath.DEG2RAD);  // Uncomment if needed
        }
        else if (isCarcass)
        {
            meshData.Scale(origin, 0.4f, 0.4f, 0.4f);
            meshData.Translate(0.65f, -0.14f, -0.24f);
            meshData.Rotate(origin, 0, -90f * GameMath.DEG2RAD, 0);
        }
        else
        {
            meshData.Scale(origin, 0.8f, 0.8f, 0.8f);
        }

        const float stdoffset = 0.2f;
        float px, py, pz;
        if (isCarcass)
        {
            px = -stdoffset;
            py = 0.15f;
            pz = 0f;
        }
        else
        {
            (px, py, pz) = slotId switch
            {
                0 => (-stdoffset, 0.15f, 0f),     // Верхняя полка, лево
                1 => (+stdoffset, 0.15f, 0f),     // Верхняя полка, право
                2 => (-stdoffset, 0.15f, 0.62f),  // Средняя полка, лево
                3 => (+stdoffset, 0.15f, 0.62f),  // Средняя полка, право
                4 => (-stdoffset, 0.15f, 1.245f), // Нижняя полка, лево
                5 => (+stdoffset, 0.15f, 1.245f), // Нижняя полка, право
                _ => (0, 0.2f, 0)
            };
        }

        meshData.Translate(px, py, pz);

        var orientationRotate = Block.Shape.rotateY + 90f;
        meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
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
    public MeshData? GenMesh(ItemSlot slot)
    {
        var stack = slot.Itemstack;
        
        if (stack == null) // если стек пустой, то ничего не рисуем
            return null;

        MeshData meshData;
        try
        {
            var meshSource = stack.Collectible as IContainedMeshSource;

            if (meshSource != null)
            {
                meshData = meshSource.GenMesh(slot, _capi.BlockTextureAtlas, Pos);
                //meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY, 0f);
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

                    _capi.TesselatorManager.ThreadDispose(); // Проверьте, нужен ли этот вызов
                }
            }
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code,
                e.Message);
            meshData = null;
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
        if (animUtil?.activeAnimationsByAnimCode.Count == 0)
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
        if (Api.Side == EnumAppSide.Server)
        {
            double now = Api.World.Calendar.TotalHours;
            double delta = now - _lastUpdateTime;
            _lastUpdateTime = now;

            bool currentlyPowered = GetBehavior<BEBehaviorEFreezer>().PowerSetting >= _maxConsumption * 0.1F;
            UpdateColdState(delta, currentlyPowered);
            _wasPowered = currentlyPowered;

        }
        else
        {
            TryRefuel();
        }


    }


    /// <summary>
    /// Проверяет, нужно ли запускать анимацию
    /// </summary>
    private void TryRefuel()
    {

        if (_wasPowered)
        {
            StartWorkingAnim();
        }
        else
        {
            StopWorkingAnim();
        }
    }


    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("electricalprogressiveqol:cold_reserve", _accumulatedColdHours.ToString("F2")));
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

        _accumulatedColdHours = tree.GetDouble("accumulatedColdHours", 0);
        _lastUpdateTime = tree.GetDouble("lastUpdateTime", 0);
        _wasPowered = tree.GetBool("_wasPowered", false);

        _closedDelay = tree.GetInt("closedDelay");
        IsOpened = tree.GetBool("isOpened");

        if (Api == null)
            return;

        _inventory.AfterBlocksLoaded(Api.World);
        //if (Api.Side == EnumAppSide.Client)
        //   UpdateMeshes();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetDouble("accumulatedColdHours", _accumulatedColdHours);
        tree.SetDouble("lastUpdateTime", _lastUpdateTime);
        tree.SetBool("_wasPowered", _wasPowered);
        tree.SetInt("closedDelay", _closedDelay);
        tree.SetBool("isOpened", IsOpened);
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
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
            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);
            var tree = new TreeAttribute();
            tree.FromBytes(reader);
            Inventory.FromTreeAttributes(tree);
            Inventory.ResolveBlocksOrItems();


            if (_freezerDialog == null)
            {
                _freezerDialog = new(Lang.Get("electricalprogressiveqol:freezer-title-gui"), Inventory, Pos, Api as ICoreClientAPI, this);
                _freezerDialog.OnClosed += () =>
                {
                    _freezerDialog = null;
                };
            }

            _freezerDialog.TryOpen();
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
        bool currentPowered = GetBehavior<BEBehaviorEFreezer>().PowerSetting >= _maxConsumption * 0.1F;
        if (currentPowered || _accumulatedColdHours > 0)
        {
            return 0.025F;
        }
        return initial;
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

        //задаем электрические параметры блока/проводника
        LoadEProperties.Load(this.Block, this);
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

        animUtil?.Dispose();

        _mesh.Dispose();
        _resultingShape = null;
    }



}