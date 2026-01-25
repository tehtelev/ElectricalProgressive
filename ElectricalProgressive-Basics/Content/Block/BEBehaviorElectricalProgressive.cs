using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
using static ElectricalProgressive.Content.Block.ECable.BlockECable;

namespace ElectricalProgressive.Content.Block;

public class BEBehaviorElectricalProgressive : BlockEntityBehavior
{
    public const string InterruptionKey = "electricalprogressive:interruption";
    public const string ConnectionKey = "electricalprogressive:connection";
    public const string IsLoadedKey = "electricalprogressive:isloaded";


    private IElectricAccumulator? accumulator;
    private IElectricConsumer? consumer;
    private IElectricConductor? conductor;
    private IElectricProducer? producer;
    private IElectricTransformator? transformator;


    private Facing connection;
    private Facing interruption;
    private bool isLoaded;

    private bool dirty = true;
    private bool paramsSet = false;

    // настройка частиц
    public List<Vec3d> ParticlesOffsetPos = new List<Vec3d>(1);
    public List<int[]> ParticlesFramesAnim = new List<int[]>(1);
    public int ParticlesType = 0;
    private BlockEntityAnimationUtil AnimUtil;

    public EParams eparams;
    public int eparamsFace;
    private EParams[]? allEparams;

    public BEBehaviorElectricalProgressive(BlockEntity blockEntity)
        : base(blockEntity)
    {

    }

    public const int MyPacketIdForServer = 1122334455; // Уникальный идентификатор пакета для передачи данных BEBehaviorElectricalProgressive
    public const int MyPacketIdForClient = 1122334456; // Уникальный идентификатор пакета для передачи данных BEBehaviorElectricalProgressive

    public global::ElectricalProgressive.ElectricalProgressive? System =>
        this.Api?.ModLoader.GetModSystem<global::ElectricalProgressive.ElectricalProgressive>();

    
    public Facing Connection
    {
        get => this.connection;
        set
        {
            if (this.connection != value)
            {
                this.connection = value;
                this.dirty = true;
                this.paramsSet = false;
                this.Update();
            }
        }
    }



    public EParams[]? AllEparams
    {
        get => allEparams!;
        set
        {
            if (this.allEparams != value)
            {
                this.allEparams = value;
                this.dirty = true;
                this.Update();
            }
        }
    }


    public (EParams, int) Eparams
    {
        get => (this.eparams, this.eparamsFace);
        set
        {
            if (!this.eparams.Equals(value.Item1) || this.eparamsFace != value.Item2)
            {
                this.eparams = value.Item1;
                this.eparamsFace = value.Item2;
                this.paramsSet = true;
                this.dirty = true;
                this.Update();
            }
        }
    }

    public Facing Interruption
    {
        get => this.interruption;
        set
        {
            if (this.interruption != value)
            {
                this.interruption = value;
                this.dirty = true;
                this.Update();
            }
        }
    }


    // информация о сети для подсказки 
    public NetworkInformation? NetworkInformation
    {
        get => networkInformation;
    }

    private NetworkInformation? networkInformation = new();


    private DateTime lastExecution = DateTime.MinValue;

    private static double intervalMSeconds;

    private BlockPos[]? multiblockParts; // Все позиции частей мультиблока (включая главную)
    private BlockPos? mainPartPos; // Позиция главной части мультиблока

    /// <summary>
    /// Инициализация поведения электрического блока
    /// </summary>
    /// <param name="api"></param>
    /// <param name="properties"></param>
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        // получаем параметры частиц
        GetParticles();

        // получаем аниматор, если есть
        AnimUtil = Blockentity.GetBehavior<BEBehaviorAnimatable>()?.animUtil!;

        // Регистрируем спавнер частиц для асинхронных частиц
        if (api is ICoreClientAPI capi)
        {
            capi.Event.RegisterAsyncParticleSpawner(OnAsyncParticles);
        }


        intervalMSeconds = this.System!.TickTimeMs;

        // Инициализация мультиблока
        InitMultiblock();

        this.isLoaded = true;   // оно загрузилось!
        this.dirty = true;
        this.Update();          // обновляем систему, чтобы она знала, что блок загрузился
    }

    /// <summary>
    /// Получение позиций частиц из атрибутов блока
    /// </summary>
    private void GetParticles()
    {
        // тип частиц
        ParticlesType = MyMiniLib.GetAttributeInt(this.Block, "particlesType", 0 );

        // получаем позиции частиц из атрибутов блока
        ParticlesOffsetPos.Clear(); // чистим данные из сохранений
        var arrayOffsetPos = MyMiniLib.GetAttributeArrayArrayFloat(this.Block, "particlesOffsetPos", new float[1][]{[0,0,0]});
        if (arrayOffsetPos != null)
        {
            for (int i = 0; i < arrayOffsetPos.Length; i++)
            {
                Vec3d buf = new Vec3d(
                    arrayOffsetPos[i][0],
                    arrayOffsetPos[i][1],
                    arrayOffsetPos[i][2]
                );

                ParticlesOffsetPos.Add(buf);
            }
        }

        // получаем привязку частиц к фрэймам анимации
        ParticlesFramesAnim.Clear(); // чистим данные из сохранений
        var arrayFrames = MyMiniLib.GetAttributeArrayArrayInt(this.Block, "particlesFramesAnim", new int[1][] { [-1, -1] });
        if (arrayFrames != null)
        {
            for (int i = 0; i < arrayFrames.Length; i++)
            {
                int[] buf =
                [
                    arrayFrames[i][0],
                    arrayFrames[i][1]
                ];

                ParticlesFramesAnim.Add(buf);
            }
        }
    }


    /// <summary>
    /// Асинхронный спавн частиц
    /// </summary>
    /// <param name="dt"></param>
    /// <param name="manager"></param>
    /// <returns></returns>
    private bool OnAsyncParticles(float dt, IAsyncParticleManager manager)
    {
        // еще не загружено или нет параметров
        if (!this.isLoaded || AllEparams is null)
            return true; 
        
        var hasBurnout = false;
        var prepareBurnout = false;

        // Однопроходная проверка условий
        foreach (var eParam in AllEparams)
        {
            hasBurnout |= eParam.burnout;
            prepareBurnout |= eParam.ticksBeforeBurnout > 0;

            // Ранний выход если оба условия уже выполнены
            if (hasBurnout || prepareBurnout)
                break;
        }

        // Спавн частиц в указанных позициях
        if (ParticlesOffsetPos != null && ParticlesOffsetPos.Count > 0)
        {
            int k = 0;
            foreach (var partPos in ParticlesOffsetPos)
            {
                // Обработка prepareBurnout
                if (prepareBurnout)
                {
                    ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos).Add(0.5d,0d,0.5d), 0);
                }
                
                // Обработка burnout
                if (hasBurnout)
                {
                    ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos).Add(0.5d, 0d, 0.5d), 1);
                }
                
                // частицы собственные для блока
                if (ParticlesType > 1 && !hasBurnout && !prepareBurnout)
                {
                    // настреок анимации нет?
                    if (ParticlesFramesAnim==null || ParticlesFramesAnim[k][0]==-1 || ParticlesFramesAnim[k][1] == -1)
                        ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos), ParticlesType);

                    // ищем аниматор
                    if (AnimUtil != null && AnimUtil.animator!=null && AnimUtil.animator.Animations.Length>0)
                    {
                        float buf = AnimUtil.animator.Animations[0].CurrentFrame;
                        if (buf> ParticlesFramesAnim[k][0] && buf < ParticlesFramesAnim[k][1])
                            ParticleManager.SpawnParticlesAsync(manager, Blockentity.Pos.ToVec3d().Offset(partPos), ParticlesType);
                    }
                }

                k++;
            }
        }

        return this.isLoaded;
    }





    /// <summary>
    /// Попытка инициализации мультиблока
    /// </summary>
    private void InitMultiblock()
    {
        // какие блоки могут получать электричество
        var blockEProperties = MyMiniLib.GetAttributeString(this.Block, "blockEProperties", "main");

        // Проверка мультиблока
        var multiblockBehavior = this.Block.GetBehavior<BlockBehaviorMultiblock>();
        if (multiblockBehavior != null && (blockEProperties == "all" || blockEProperties == "all_down"))
        {
            var properti = multiblockBehavior.propertiesAtString;

            int sizeX = 1, sizeY = 1, sizeZ = 1;
            var cposition = new int[3];

            if (!string.IsNullOrEmpty(properti))
            {
                try
                {
                    var jo = JObject.Parse(properti);
                    sizeX = (int)jo["sizex"]!;
                    sizeY = (int)jo["sizey"]!;
                    sizeZ = (int)jo["sizez"]!;
                    var cpos = jo["cposition"];
                    cposition[0] = (int)cpos["x"];
                    cposition[1] = (int)cpos["y"];
                    cposition[2] = (int)cpos["z"];
                }
                catch
                {
                    // Логирование ошибки, если требуется
                }
            }

            // Определяем позицию главного блока
            mainPartPos = this.Blockentity.Pos;
            // вычисляем нулевую часть мультиблока
            var zeroPartPos = this.Blockentity.Pos.AddCopy(-cposition[0], -cposition[1], -cposition[2]);

            // Собираем все позиции частей мультиблока
            var parts = new List<BlockPos>();
            for (var dx = 0; dx < sizeX; dx++)
            for (var dy = 0; dy < sizeY; dy++)
            for (var dz = 0; dz < sizeZ; dz++)
            {
                var partPos = zeroPartPos.AddCopy(dx, dy, dz);
                // Для "all_down" добавляем только нижние блоки (dy == 0), для "all" — все
                if (blockEProperties == "all" || (blockEProperties == "all_down" && dy == 0))
                {
                    parts.Add(partPos);
                }
            }
            multiblockParts = parts.ToArray();
        }
        else
        {
            mainPartPos = this.Blockentity.Pos;
            multiblockParts = [this.Blockentity.Pos];
        }
    }

    /// <summary>
    /// Что-то в цепи поменялось
    /// </summary>
    /// <param name="force"></param>
    public void Update(bool force = false)
    {
        if (!this.dirty && !force)
            return;

        var system = this.System;
        if (system is null)
        {
            this.dirty = true;
            return;
        }

        this.dirty = false;


        this.consumer = null;
        this.conductor = null;
        this.producer = null;
        this.accumulator = null;
        this.transformator = null;

        foreach (var entityBehavior in this.Blockentity.Behaviors)
        {
            switch (entityBehavior)
            {
                case IElectricConsumer { } consumer:
                    this.consumer = consumer;
                    break;

                case IElectricProducer { } producer:
                    this.producer = producer;
                    break;

                case IElectricAccumulator { } accumulator:
                    this.accumulator = accumulator;
                    break;

                case IElectricTransformator { } transformator:
                    this.transformator = transformator;
                    break;

                case IElectricConductor { } conductor:
                    this.conductor = conductor;
                    break;
            }
        }

        // Главная часть мультиблока — обычная регистрация
        system.SetConductor(this.Blockentity.Pos, this.conductor);
        system.SetConsumer(this.Blockentity.Pos, this.consumer);
        system.SetProducer(this.Blockentity.Pos, this.producer);
        system.SetAccumulator(this.Blockentity.Pos, this.accumulator);
        system.SetTransformator(this.Blockentity.Pos, this.transformator);

        // Для всех остальных частей мультиблока (кроме главной) — регистрируем как проводник с теми же EParams
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
            {
                if (partPos == this.Blockentity.Pos)
                    continue; // Главная часть уже обработана выше
                // Создаём виртуальный проводник для каждой части
                var virtualConductor = new VirtualConductor(partPos);
                system.SetConductor(partPos, virtualConductor);
                // Для Update: используем те же параметры, что и у главной части
                var partEparams = allEparams;
                var Epar = this.paramsSet ?
                    Eparams :
                    (new(), 0);
                system.Update(partPos, this.connection & ~this.interruption, Epar, ref partEparams!, isLoaded);
            }
        }

        // Главная часть — обычный Update
        var mainEpar = this.paramsSet ? Eparams : (new(), 0);
        if (system.Update(this.Blockentity.Pos, this.connection & ~this.interruption, mainEpar, ref allEparams!, isLoaded))
        {
            try
            {
                this.Blockentity.MarkDirty(true);
            }
            catch { }
        }
    }



    /// <summary>
    /// Вызывается, когда блок удаляется из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        this.isLoaded = false;

        // Удаляем все части мультиблока из системы
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
                this.System?.Remove(partPos);
        }
        else
        {
            this.System?.Remove(this.Blockentity.Pos);
        }
        networkInformation = null;

        
        AnimUtil?.Dispose();
    }



    /// <summary>
    /// Вызывается, когда блок выгружается из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.isLoaded = false;
        this.dirty = true;
        // Обновляем все части мультиблока
        if (multiblockParts != null)
        {
            foreach (var partPos in multiblockParts)
            {
                var partEparams = allEparams;
                var Epar = this.paramsSet ? Eparams : (new(), 0);
                this.System?.Update(partPos, this.connection & ~this.interruption, Epar, ref partEparams!, false);
            }
        }
        else
        {
            this.Update();
        }
        networkInformation = null;

        AnimUtil?.Dispose();
    }

 

    /// <summary>
    /// Принимает сигнал от клиента, который наводится на блок, что инициирует обновление информации о блоке-энтити
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid == MyPacketIdForServer) // проверяем, что пакет именно мой
        {
            var dataTuple = SerializerUtil.Deserialize<(BlockPos, Facing, string)>(data);
            networkInformation = this.System?.GetNetworks(dataTuple.Item1, dataTuple.Item2, dataTuple.Item3);
            var sapi= (ICoreServerAPI)Api;
            var fromServerPlayer = fromPlayer as IServerPlayer;
            sapi.Network.SendBlockEntityPacket(fromServerPlayer,this.Blockentity.Pos, MyPacketIdForClient, NetworkInformationSerializer.Serialize(networkInformation!));
            this.Blockentity.MarkDirty();
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);

    }

    /// <summary>
    /// Принимает сигнал от сервера, что пришла информация о сети
    /// </summary>
    /// <param name="packetid"></param>
    /// <param name="data"></param>
    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        if (packetid == MyPacketIdForClient) // проверяем, что пакет именно мой
        {
            networkInformation= NetworkInformationSerializer.Deserialize(data);
        }

        base.OnReceivedServerPacket(packetid, data);
    }


    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);



        if (Api is not ICoreClientAPI)
            return;

        

        //храним направления проводов в этом блоке
        var selectedFacing = Facing.None;

        var entity = this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos); // получаем блок-энитити, чтобы получить информацию о нем
        var methodForInformation = ""; //метод получения информации о сети, в зависимости от типа блока-энитити

        //если это кабель, то мы можем вывести только информацию о сети на одной грани
        if (entity is BlockEntityECable blockEntityECable && entity is not BlockEntityEConnector && blockEntityECable.ElectricalProgressive.AllEparams != null)
        {
            if (forPlayer is { CurrentBlockSelection: { } blockSelection })
            {
                var key = CacheDataKey.FromEntity(blockEntityECable);
                var hitPosition = blockSelection.HitPosition;

                var sf = new SelectionFacingCable();
                selectedFacing = sf.SelectionFacing(key, hitPosition, this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos));  //выделяем напрвление для слома под курсором

                if (selectedFacing != Facing.None)
                    selectedFacing = FacingHelper.FromFace(FacingHelper.Faces(selectedFacing).First());  //выбираем одну грань, если даже их там вдруг окажется больше
                else
                    return;

                methodForInformation = "thisFace"; // только указанную грань


            }
        }
        else if (entity is BlockEntityEConnector blockEntityEConnector && blockEntityEConnector.ElectricalProgressive.AllEparams != null) //если это мет блок
        {
            selectedFacing = Facing.AllAll;
            methodForInformation = "currentFace"; // берем информацию о любой грани, где ток больше 0
        }
        else     //для не кабелей берем все что есть
        {
            selectedFacing = this.Connection;
            methodForInformation = "firstFace"; // берем информацию о первой грани в массиве из многих
        }



        // работаем с выводом информации о причинах сгорания
        if (this.System?.Parts.TryGetValue(this.Blockentity.Pos, out var part) ?? false)
        {
            foreach (var face in FacingHelper.Faces(selectedFacing))
            {
                var faceIndex = face.Index;

                if (part.eparams[faceIndex].burnout || part.eparams[faceIndex].ticksBeforeBurnout > 0)
                {
                    var cause = part.eparams[faceIndex].causeBurnout switch
                    {
                        1 => Lang.Get("electricalprogressivebasics:CauseCurrent"),
                        2 => Lang.Get("electricalprogressivebasics:CauseVoltage"),
                        3 => Lang.Get("electricalprogressivebasics:CauseEnvironment"),
                        _ => null!
                    };

                    if (cause is not null)
                    {
                        if (part.eparams[faceIndex].burnout)
                            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:Burned"));

                        stringBuilder.AppendLine(cause);
                        break;
                    }
                }
            }
        }

        // получаем информацию о сети раз в секунду!
        if ((DateTime.Now - lastExecution).TotalMilliseconds >= intervalMSeconds)
        {
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket<(BlockPos, Facing, string)>(this.Blockentity.Pos, MyPacketIdForServer,
                (this.Blockentity.Pos, selectedFacing, methodForInformation));

            lastExecution = DateTime.Now;
        }

        // если нет информации о сети, то просто выходим
        if (networkInformation == null)
        {
            return;
        }

        //отслеживаем состояние кнопки для подробностей
        var capi = (ICoreClientAPI)Api;
        var altPressed = capi.Input.IsHotKeyPressed("AltPressForNetwork");
        var nameAltPressed = capi.Input.GetHotKeyByCode("AltPressForNetwork").CurrentMapping.ToString();

        if (!altPressed)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:PressForDetails", nameAltPressed));
            return;
        }

        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:Electricity"));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Consumers") + ": " + networkInformation.NumberOfConsumers);
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Generators") + ": " + networkInformation.NumberOfProducers);
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Batteries") + ": " + networkInformation.NumberOfAccumulators);
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Transformers") + ": " + networkInformation.NumberOfTransformators);
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Blocks") + ": " + networkInformation.NumberOfBlocks);
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Generation") + ": " + networkInformation.Production + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + networkInformation.Consumption + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Request") + ": " + networkInformation.Request + " " + Lang.Get("electricalprogressivebasics:W"));

        var capacity = (float)((networkInformation.MaxCapacity == 0f) ? 0f : (networkInformation.Capacity * 100.0F / networkInformation.MaxCapacity));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " + (int)networkInformation.Capacity + "/" + (int)networkInformation.MaxCapacity + " " + Lang.Get("electricalprogressivebasics:J") + " (" + capacity.ToString("F3") + " %)");

        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:Block"));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:MaxCurrent") + ": " + networkInformation.eParamsInNetwork.maxCurrent * networkInformation.eParamsInNetwork.lines + " " + Lang.Get("electricalprogressivebasics:A"));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Current") + ": " + Math.Abs(networkInformation.current).ToString("F3") + " " + Lang.Get("electricalprogressivebasics:A"));

        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is BlockEntityECable) //если кабель!
        {
            stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Resistivity") + ": " + networkInformation.eParamsInNetwork.resistivity / (networkInformation.eParamsInNetwork.isolated ? 2.0F : 1.0F)
                + " " + Lang.Get("electricalprogressivebasics:OmLine"));
            stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Resistance") + ": " + networkInformation.eParamsInNetwork.resistivity / (networkInformation.eParamsInNetwork.lines
                * networkInformation.eParamsInNetwork.crossArea) / (networkInformation.eParamsInNetwork.isolated ? 2.0F : 1.0F) + " " + Lang.Get("electricalprogressivebasics:Om"));
            stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Lines") + ": " + networkInformation.eParamsInNetwork.lines + " " + Lang.Get("electricalprogressivebasics:Pcs"));
            stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:SectionSize") + ": " + networkInformation.eParamsInNetwork.crossArea * networkInformation.eParamsInNetwork.lines + " " + Lang.Get("electricalprogressivebasics:Units"));
        }

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:MaxVoltage") + ": " + networkInformation?.eParamsInNetwork.voltage + " " + Lang.Get("electricalprogressivebasics:V"));
    }



    /// <summary>
    /// Сохраняет в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(ConnectionKey, SerializerUtil.Serialize(this.connection));
        tree.SetBytes(InterruptionKey, SerializerUtil.Serialize(this.interruption));
        tree.SetBool(IsLoadedKey, this.isLoaded);

        tree.SetString("SerializationFormat", "binary");

        if (allEparams != null)
        {
            tree.SetBytes(BlockEntityEBase.AllEparamsKey, EParamsSerializer.Serialize(allEparams!));
        }

        // Сохраняем параметры частиц
        tree.SetInt("ParticlesType", ParticlesType);

        // Сохраняем массив смещений частиц
        tree.SetInt("ParticlesOffsetPosCount", ParticlesOffsetPos.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetDouble($"ParticlesOffsetPosX_{i}", ParticlesOffsetPos[i].X);
            tree.SetDouble($"ParticlesOffsetPosY_{i}", ParticlesOffsetPos[i].Y);
            tree.SetDouble($"ParticlesOffsetPosZ_{i}", ParticlesOffsetPos[i].Z);
        }

        // Сохраняем массив фреймов для синхронизации
        tree.SetInt("ParticlesFramesAnimCount", ParticlesFramesAnim.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetInt($"ParticlesFramesAnimMin_{i}", ParticlesFramesAnim[i][0]);
            tree.SetInt($"ParticlesFramesAnimMax_{i}", ParticlesFramesAnim[i][1]);

        }

    }

    /// <summary>
    /// Считывает из дерева атрибутов
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var connection = SerializerUtil.Deserialize<Facing>(tree.GetBytes(ConnectionKey));
        var interruption = SerializerUtil.Deserialize<Facing>(tree.GetBytes(InterruptionKey));
        var isLoaded = tree.GetBool(IsLoadedKey, false);

        var format = tree.GetString("SerializationFormat", "json");

        EParams[]? AllEparamss;
        if (format == "binary")
        {
            AllEparamss = EParamsSerializer.Deserialize(tree.GetBytes(BlockEntityEBase.AllEparamsKey));
        }
        else
        {
            AllEparamss = JsonConvert.DeserializeObject<EParams[]>(Encoding.UTF8.GetString(tree.GetBytes(BlockEntityEBase.AllEparamsKey)));
        }

        // Загрузка параметров частиц
        ParticlesType = tree.GetInt("ParticlesType", 0);

        int count = tree.GetInt("ParticlesOffsetPosCount", 0);
        ParticlesOffsetPos = new (count);
        for (int i = 0; i < count; i++)
        {
            double x = tree.GetDouble($"ParticlesOffsetPosX_{i}", 0.0);
            double y = tree.GetDouble($"ParticlesOffsetPosY_{i}", 0.0);
            double z = tree.GetDouble($"ParticlesOffsetPosZ_{i}", 0.0);
            ParticlesOffsetPos.Add(new Vec3d(x, y, z));
        }

        count = tree.GetInt("ParticlesFramesAnimCount", 0);
        ParticlesFramesAnim = new (count);
        for (int i = 0; i < count; i++)
        {
            int min = tree.GetInt($"ParticlesFramesAnimMin_{i}", -1);
            int max = tree.GetInt($"ParticlesFramesAnimMax_{i}", -1);
            ParticlesFramesAnim.Add([min,max]);
        }




        this.interruption = interruption;
        this.isLoaded = isLoaded;
        this.connection = connection;
        this.allEparams = AllEparamss;
        this.dirty = true;
        this.Update();
    }



}