using ElectricalProgressive.Content.Block.Termoplastini;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ETermoGenerator;

public class BlockEntityETermoGenerator : BlockEntityGenericTypedContainer, IHeatSource
{

    private Facing _facing = Facing.None;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

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


    private static readonly Dictionary<float, BlockFacing> varRotate = new()
    {
        { 180, BlockFacing.SOUTH },
        { 270, BlockFacing.EAST},
        { 0, BlockFacing.NORTH},
        { 90, BlockFacing.WEST}
    };


    ICoreClientAPI? _capi;
    ICoreServerAPI? _sapi;
    private InventoryTermoGenerator _inventory;
    private GuiBlockEntityETermoGenerator? _clientDialog;


    //private float prevGenTemp = 20f;
    public float _genTemp = 20f;

    /// <summary>
    /// Rэш для мэша топлива, где int - размер топлива в генераторе (от 0 до 8)
    /// </summary>
    private static readonly Dictionary<(int, float), MeshData> MeshData = new();


    /// <summary>
    /// Коэффициенты КПД в зависимости от высоты пластин
    /// </summary>
    public static readonly float[] KpdPerDistance =
    [
        0.15F, // 1-й 
        0.14F, // 2-й 
        0.13F, // 3-й 
        0.12F, // 4-й 
        0.11F, // 5-й 
        0.09F, // 6-й 
        0.08F, // 7-й 
        0.07F, // 8-й 
        0.06F, // 9-й 
        0.05F  // 10-й 
    ];

    /// <summary>
    /// Максимальная температура топлива
    /// </summary>
    private int _maxTemp;

    /// <summary>
    /// Текущее время горения топлива
    /// </summary>
    private float _fuelBurnTime;

    /// <summary>
    /// Максимальное время горения топлива
    /// </summary>
    private float _maxBurnTime;

    /// <summary>
    /// Температура в генераторе
    /// </summary>
    public float GenTemp => _genTemp;


    /// <summary>
    /// Собственно выходная максимальная мощность
    /// </summary>
    public float Power
    {
        get
        {
            var envTemp = EnvironmentTemperature(); //температура окружающей среды
            if (Kpd > 0)
            {
                if (_genTemp <= envTemp) //окружающая среда теплее? 
                {
                    return 1f;
                }
                else
                {
                    return (_genTemp - envTemp) * Kpd / 2.0F;  //учитываем разницу температур с окружающей средой и КПД
                }
            }
            else
                return 1f;
        }
    }

    /// <summary>
    /// КПД генератора в долях
    /// </summary>
    public float Kpd = 0f;

    /// <summary>
    /// Горизонтальные направления для смещения
    /// </summary>
    private static readonly BlockFacing[] OffsetsHorizontal = BlockFacing.HORIZONTALS;

    /// <summary>
    /// Слот для топлива в инвентаре генератора
    /// </summary>
    public ItemSlot FuelSlot => this._inventory[0];

    /// <summary>
    /// Сколько термопластин установлено в генераторе по высоте
    /// </summary>
    public int HeightTermoplastin = 0;



    /// <summary>
    /// Стак дял топлива в генераторе
    /// </summary>
    public ItemStack FuelStack
    {
        get { return this._inventory[0].Itemstack; }
        set
        {
            this._inventory[0].Itemstack = value;
            this._inventory[0].MarkDirty();
        }
    }

    /// <summary>
    /// Аниматор блока, используется для анимации открывания дверцы генератора
    /// </summary>
    private BlockEntityAnimationUtil AnimUtil
    {
        get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
    }


    /// <summary>
    /// Запускает анимацию открытия дверцы
    /// </summary>
    public new void OpenLid()
    {
        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("close") == true)
        {
            AnimUtil?.StopAnimation("close");
        }

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
        {
            AnimUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "open",
                Code = "open",
                AnimationSpeed = 1.4f,
                EaseOutSpeed = 6,
                EaseInSpeed = 15
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 11 };

       

            //добавляем звук
            _capi?.World.PlaySoundAt(new AssetLocation("game:sounds/block/cokeovendoor-open"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);

        }

    }


    /// <summary>
    /// Закрывает дверцу генератора, останавливая анимацию открытия, если она запущена
    /// </summary>
    public new void CloseLid()
    {

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == true)
        {
            AnimUtil?.StopAnimation("open");

            AnimUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "close",
                Code = "close",
                AnimationSpeed = 1.4f,
                EaseOutSpeed = 6,
                EaseInSpeed = 15
            });

            //применяем цвет и яркость
            Block.LightHsv = new byte[] { 7, 7, 0 };

        

            //добавляем звук
            _capi?.World.PlaySoundAt(new AssetLocation("game:sounds/block/cokeovendoor-close"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
        }

    }


    /// <summary>
    /// Запускает анимацию работы
    /// </summary>
    public new void StartWorkingAnim()
    {
        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false && _genTemp>20)
        {
            AnimUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 1f,
                EaseOutSpeed = 6,
                EaseInSpeed = 15
            });

            
        }

    }


    /// <summary>
    /// Останавливает анимацию работы
    /// </summary>
    public new void StopWorkingAnim()
    {
        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            AnimUtil?.StopAnimation("work-on");
          
        }
    }





    private long _listenerId;

    public override InventoryBase Inventory => _inventory;

    public override string DialogTitle => Lang.Get("electricalprogressivebasics:termogen");

    public override string InventoryClassName => "termogen";

    public BlockEntityETermoGenerator()
    {
        this._inventory = new InventoryTermoGenerator(null!, null!);
        this._inventory.SlotModified += OnSlotModified;
    }


    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            _sapi = api as ICoreServerAPI;
        }
        else
        {
            _capi = api as ICoreClientAPI;

            // инициализируем аниматор
            if (AnimUtil != null)
            {
                AnimUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }

        }

        this._inventory.Pos = this.Pos;
        this._inventory.LateInitialize(InventoryClassName + "-" + Pos, api);

        _listenerId=this.RegisterGameTickListener(new Action<float>(OnBurnTick), 1000);

        CanDoBurn();
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



    /// <summary>
    /// При ломании блока
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer byPlayer = null!)
    {
        base.OnBlockBroken(null);
    }




    /// <summary>
    /// Отвечает за тепло отдаваемое в окружающую среду
    /// </summary>
    /// <param name="world"></param>
    /// <param name="heatSourcePos"></param>
    /// <param name="heatReceiverPos"></param>
    /// <returns></returns>
    public float GetHeatStrength(
      IWorldAccessor world,
      BlockPos heatSourcePos,
      BlockPos heatReceiverPos)
    {
        return Math.Max((float)(((float)this._genTemp - 20.0F) / ((float)1300F - 20.0F) * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)), 0.0f);
    }




    /// <summary>
    /// Получает температуру окружающей среды
    /// </summary>
    /// <returns></returns>
    protected virtual int EnvironmentTemperature()
    {
        return (int)this.Api.World.BlockAccessor.GetClimateAt(this.Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, this.Api.World.Calendar.TotalDays).Temperature;
    }






    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        MeshData.Clear(); //не забываем очищать кэш мэша при выгрузке блока

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog.TryClose();
            this._clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil.Dispose();
        }

        // очищаем ссылки на API
        _capi = null;
        _sapi = null;

    }

    /// <summary>
    /// Обработчик изменения слота инвентаря
    /// </summary>
    /// <param name="slotId"></param>
    public void OnSlotModified(int slotId)
    {
        if (slotId == 0)
        {
            if (Inventory[0].Itemstack != null && !Inventory[0].Empty &&
                Inventory[0].Itemstack.Collectible.CombustibleProps != null)
            {
                if (_fuelBurnTime == 0)
                    CanDoBurn();
            }
        }

        base.Block = this.Api.World.BlockAccessor.GetBlock(this.Pos);
        this.MarkDirty(this.Api.Side == EnumAppSide.Server, null);

        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            _clientDialog.Update(_genTemp, _fuelBurnTime);
        }

        var chunkatPos = this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos);
        if (chunkatPos == null)
            return;

        chunkatPos.MarkModified();
    }


    private static readonly Dictionary<float, Vec3f?> fuelMeshOffset = new()
    {
        { 180, new Vec3f(0.98f,0.2f,-1.37f) },
        { 270, new Vec3f(-1.37f,0.2f,-0.98f) },
        { 0, new Vec3f(-0.98f,0.2f,1.37f) },
        { 90, new Vec3f(1.37f,0.2f,0.98f) }
    };


    /// <summary>
    /// Обработчик тесселяции блока, добавляет мэш блока и мэш топлива, если он есть
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator); // вызываем базовую логику тесселяции


        var stack = Inventory[0].Itemstack;
        var sizeFuel = 0; // размер топлива в генераторе

        if (stack != null && stack.Collectible!=null &&  stack.Collectible.CombustibleProps != null)
        {
            // смотрим сколько топлива в генераторе
            sizeFuel = (int)(stack.StackSize * 8.0F / stack.Collectible.MaxStackSize) + 1;
            sizeFuel = Math.Clamp(sizeFuel, 1, 8); // ограничиваем размер топлива от 1 до 8
        }

        

        if (!MeshData.TryGetValue((sizeFuel, Block.Shape.rotateY), out var fuelMesh))
        {
            // если есть топливо, то добавляем его в мэш
            
            _capi?.Tesselator.TesselateShape(this.Block, Vintagestory.API.Common.Shape.TryGet(Api, "electricalprogressivebasics:shapes/block/termogenerator/toplivo/toplivo-" + sizeFuel + ".json"), out fuelMesh);

            _capi?.TesselatorManager.ThreadDispose(); //обязательно

            var offset = fuelMeshOffset[Block.Shape.rotateY];

            fuelMesh =fuelMesh.Clone().Translate(offset.X, offset.Y, offset.Z);

            MeshData.TryAdd((sizeFuel, Block.Shape.rotateY), fuelMesh!);
            
        }

        if (fuelMesh != null)
        {
            mesher.AddMeshData(fuelMesh);
        }


        // если анимации нет, то рисуем блок базовый
        if (AnimUtil?.animator.ActiveAnimationCount==0)
        {
            return false;
        }


        return true;  // не рисует базовый блок, если есть анимация
    }

    /// <summary>
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    public void OnBurnTick(float deltatime)
    {
        Calculate_kpd();

        if (this.Api is ICoreServerAPI)
        {
            if (_fuelBurnTime > 0f)
            {
                _genTemp = ChangeTemperature(_genTemp, _maxTemp, deltatime);
                _fuelBurnTime -= deltatime; 
                if (_fuelBurnTime <= 0f)
                {
                    _fuelBurnTime = 0f;
                    _maxBurnTime = 0f;
                    _maxTemp = 20; // важно
                    if (!Inventory[0].Empty)
                        CanDoBurn();
                }
            }
            else
            {
                if (_genTemp != 20f)
                    _genTemp = ChangeTemperature(_genTemp, 20f, deltatime);
                CanDoBurn();
            }



            MarkDirty();
        }



        // обновляем диалоговое окно на клиенте
        if (this.Api != null && this.Api.Side == EnumAppSide.Client)
        {
            if (this._clientDialog != null)
                _clientDialog.Update(_genTemp, _fuelBurnTime);

            if(_genTemp>20)
                StartWorkingAnim();
            else
            {
                StopWorkingAnim();
            }

        }

    }



    /// <summary>
    /// Расчет КПД генератора
    /// </summary>
    private void Calculate_kpd()
    {
        // Получаем доступ к блочным данным один раз
        var accessor = this.Api.World.BlockAccessor;
        Kpd = 0f;
        
        // Перебираем потенциальные термопластины по высоте
        for (var level = 1; level <= 11; level++)
        {
            // Получаем позицию и блок термопластины
            BlockFacing facing = varRotate[Block.Shape.rotateY];
            BlockPos platePos = Pos.AddCopy(BlockTermoplastini.varRotateOffset[facing]);

            if (facing == BlockFacing.NORTH)
                platePos = platePos.NorthCopy(level);
            else if (facing == BlockFacing.SOUTH)
                platePos = platePos.SouthCopy(level);
            else if (facing == BlockFacing.EAST)
                platePos = platePos.EastCopy(level);
            else if (facing == BlockFacing.WEST)
                platePos = platePos.WestCopy(level);
            else
            {
                HeightTermoplastin = level - 1; //сохраняем высоту термопластин
                break;
            }

            if (accessor.GetBlock(platePos) is not BlockTermoplastini)
            {
                HeightTermoplastin = level - 1; //сохраняем высоту термопластин
                break;
            }
            

            // Учитываем множитель КПД на данном уровне
            Kpd +=  KpdPerDistance[level - 1];
        }
    }




    /// <summary>
    /// Проверяет, можно ли сжечь топливо в генераторе
    /// </summary>
    private void CanDoBurn()
    {
        var fuelProps = FuelSlot.Itemstack?.Collectible?.CombustibleProps ?? null!;
        if (fuelProps == null)
            return;

        if (_fuelBurnTime > 0)
            return;

        if (fuelProps.BurnTemperature > 0f && fuelProps.BurnDuration > 0f)
        {
            _maxBurnTime = _fuelBurnTime = fuelProps.BurnDuration;
            _maxTemp = fuelProps.BurnTemperature;
            FuelStack.StackSize--;
            if (FuelStack.StackSize <= 0)
            {
                FuelStack = null!;
            }

            FuelSlot.MarkDirty();
            //MarkDirty(true);
        }
    }


    /// <summary>
    /// Изменяет температуру в зависимости от времени и разницы температур
    /// </summary>
    /// <param name="fromTemp"></param>
    /// <param name="toTemp"></param>
    /// <param name="deltaTime"></param>
    /// <returns></returns>
    private static float ChangeTemperature(float fromTemp, float toTemp, float deltaTime)
    {
        var diff = Math.Abs(fromTemp - toTemp);
        deltaTime += deltaTime * (diff / 28f);
        if (diff < deltaTime)
        {
            return toTemp;
        }

        if (fromTemp > toTemp)
        {
            deltaTime = -deltaTime;
        }

        if (Math.Abs(fromTemp - toTemp) < 1f)
        {
            return toTemp;
        }
        return fromTemp + deltaTime;
    }








    /// <summary>
    /// Обработчик нажатия правой кнопкой мыши по блоку, открывает диалоговое окно
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {

        // открываем диалоговое окно
        if (this.Api.Side == EnumAppSide.Client)
        {
            base.toggleInventoryDialogClient(byPlayer, delegate
            {
                this._clientDialog =
                    new GuiBlockEntityETermoGenerator(DialogTitle, Inventory, this.Pos, this._capi!, this);
                _clientDialog.Update(_genTemp, _fuelBurnTime);
                return this._clientDialog;
            });
        }
        return true;
    }




    /// <summary>
    /// При удалении блока, закрывает диалоговое окно и отключает электричество
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        var electricity = ElectricalProgressive;

        if (electricity != null)
        {
            electricity.Connection = Facing.None;
        }


        MeshData.Clear(); //не забываем очищать кэш мэша при выгрузке блока

        // закрываем диалоговое окно, если оно открыто
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog.TryClose();
            this._clientDialog = null;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);

        // отключаем аниматор, если он есть
        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil.Dispose();
        }

        // очищаем ссылки на API
        _capi = null;
        _sapi = null;
    }



    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        this._inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("_genTemp", _genTemp);
        tree.SetInt("maxTemp", _maxTemp);
        tree.SetFloat("fuelBurnTime", _fuelBurnTime);
        tree.SetBytes("electricalprogressive:facing", SerializerUtil.Serialize(this._facing));
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this._inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
            Inventory.AfterBlocksLoaded(this.Api.World);
        _genTemp = tree.GetFloat("_genTemp", 0);
        _maxTemp = tree.GetInt("maxTemp", 0);
        _fuelBurnTime = tree.GetFloat("fuelBurnTime", 0);

        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            if (this._clientDialog != null)
                _clientDialog.Update(_genTemp, _fuelBurnTime);
            MarkDirty();
        }

        try
        {
            this._facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes("electricalprogressive:facing"));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }
    }


    /// <summary>
    /// Получение информации о блоке 
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="dsc"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        if (this.FuelStack == null)
            return;
        
        dsc.AppendLine(Lang.Get("Contents") + ": "+(object)this.FuelStack.StackSize +"x"+ (object)this.FuelStack.GetName());
        
    }

}