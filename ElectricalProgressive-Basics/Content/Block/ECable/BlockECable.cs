using ElectricalProgressive.Content.Block.ESwitch;
using ElectricalProgressive.Utils;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ECable
{
    public class BlockECable : BlockEBase
    {
        private readonly static ConcurrentDictionary<CacheDataKey, Dictionary<Facing, Cuboidf[]>> CollisionBoxesCache = new();

        public readonly static ConcurrentDictionary<CacheDataKey, Dictionary<Facing, Cuboidf[]>> SelectionBoxesCache = new();

        public readonly static Dictionary<CacheDataKey, MeshData> MeshDataCache = new();

        public static BlockVariant? enabledSwitchVariant;
        public static BlockVariant? disabledSwitchVariant;

        public float res;                       //удельное сопротивление из ассета
        public float maxCurrent;                //максимальный ток из ассета
        public float crosssectional;            //площадь сечения из ассета
        public string material = "";              //материал из ассета

        public static readonly Dictionary<int, string> voltages = new()
        {
            { 32, "32v" },
            { 128, "128v" }
        };

        public static readonly Dictionary<string, int> voltagesInvert = new()
        {
            { "32v", 32 },
            { "128v", 128 }
        };

        public static Dictionary<int, string> quantitys = new()
        {
            { 1, "single" },
            { 2, "double" },
            { 3, "triple" },
            { 4, "quadruple"}
        };

        public static Dictionary<int, string> types = new()
        {
            { 0, "dot" },
            { 1, "part" },
            { 2, "block" },
            { 3, "burned" },
            { 4, "fix" },
            { 5, "block_isolated" },
            { 6, "isolated" },
            { 7, "dot_isolated" }
        };

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // предзагрузка ассетов выключателя
            var assetLocation = new AssetLocation("electricalprogressivebasics:switch-enabled");
            var block = api.World.BlockAccessor.GetBlock(assetLocation);

            enabledSwitchVariant = new(api, block, "enabled");
            disabledSwitchVariant = new(api, block, "disabled");
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            BlockECable.CollisionBoxesCache.Clear();
            BlockECable.SelectionBoxesCache.Clear();
            BlockECable.MeshDataCache.Clear();
        }

        public override bool IsReplacableBy(Vintagestory.API.Common.Block block)
        {
            return base.IsReplacableBy(block) || block is BlockECable || block is BlockESwitch;
        }


        /// <summary>
        /// Ставим кабель
        /// </summary>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSelection"></param>
        /// <param name="byItemStack"></param>
        /// <returns></returns>
        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSelection, ItemStack byItemStack)
        {
            var selection = new Selection(blockSelection);
            var facing = FacingHelper.From(selection.Face, selection.Direction);
            var faceIndex = FacingHelper.Faces(facing).First().Index;
            var currentGameMode = byPlayer.WorldData.CurrentGameMode;

            // Если размещаем кабель в блоке без кабелей
            if (world.BlockAccessor.GetBlockEntity(blockSelection.Position) is not BlockEntityECable entity)
            {
                if (!HasSolidNeighbor(world, blockSelection.Position, faceIndex))
                    return false;

                // если установка все же успешна
                if (!base.DoPlaceBlock(world, byPlayer, blockSelection, byItemStack))
                    return false;

                // В теории такого не должно произойти
                if (world.BlockAccessor.GetBlockEntity(blockSelection.Position) is not BlockEntityECable placedCable)
                    return false;

                // обновляем текущий блок с кабелем 
                var material = MyMiniLib.GetAttributeString(byItemStack.Block, "material", "");  // определяем материал
                var indexV = voltagesInvert[byItemStack.Block.Variant["voltage"]];    // определяем индекс напряжения
                var isolated = byItemStack.Block.Code.ToString().Contains("isolated");     // определяем изоляцию
                var isolatedEnvironment = isolated; // гидроизоляция

                //подгружаем некоторые параметры из ассета
                res = MyMiniLib.GetAttributeFloat(byItemStack.Block, "res", 1);
                maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 1);
                crosssectional = MyMiniLib.GetAttributeFloat(byItemStack.Block, "crosssectional", 1);

                var newEparams = new EParams(indexV, maxCurrent, material, res, 1, crosssectional, false, isolated, isolatedEnvironment);

                placedCable.Connection = facing;       //сообщаем направление
                placedCable.Eparams = (newEparams, faceIndex);

                placedCable.AllEparams![faceIndex] = newEparams;
                //markdirty тут строго не нужен!

                return true;
            }

            // обновляем текущий блок с кабелем 
            var lines = entity.AllEparams![faceIndex].lines; //сколько линий на грани уже?

            if ((entity.Connection & facing) != 0)  //мы навелись уже на существующий кабель?
            {
                //какие соединения уже есть на грани?
                var entityConnection = entity.Connection & FacingHelper.FromFace(FacingHelper.Faces(facing).First());

                //какой блок сейчас здесь находится
                var indexV = entity.AllEparams[faceIndex].voltage;          //индекс напряжения этой грани
                var material = entity.AllEparams[faceIndex].material;          //индекс материала этой грани
                var burnout = entity.AllEparams[faceIndex].burnout;            //сгорело?
                var isolated = entity.AllEparams[faceIndex].isolated;            //изолировано ?

                // берем ассет блока кабеля
                var block = new GetCableAsset().CableAsset(api, entity.Block, indexV, material, 1, isolated ? 6 : 1);

                //проверяем сколько у игрока проводов в руке и совпадают ли они с теми что есть
                if (!CanAddCableToFace(burnout, block.Code, currentGameMode, byItemStack, FacingHelper.Count(entityConnection)))
                    return false;

                // для 32V 1-4 линии, для 128V 2 линии
                if ((indexV == 32 && lines == 4) || (indexV == 128 && lines == 2))
                {
                    if (this.api is ICoreClientAPI apii)
                        apii.TriggerIngameError((object)this, "cable", "Линий уже достаточно.");

                    return false;
                }

                lines++; //приращиваем линии
                if (currentGameMode != EnumGameMode.Creative) // чтобы в креативе не уменьшало стак
                {
                    // отнимаем у игрока столько же, сколько установили
                    byItemStack.StackSize -= FacingHelper.Count(entityConnection) - 1;
                }

                entity.AllEparams[faceIndex].lines = lines; // применяем линии
                entity.MarkDirty(true);
                return true;
            }
            else
            {
                //проверка на сплошную соседнюю грань
                if (lines == 0 && !HasSolidNeighbor(world, blockSelection.Position, faceIndex))
                    return false;

                var indexV = voltagesInvert[byItemStack.Block.Variant["voltage"]];    //определяем индекс напряжения
                var isolated = byItemStack.Block.Code.ToString().Contains("isolated");     //определяем изоляцию
                var isolatedEnvironment = isolated; //гидроизоляция

                //подгружаем некоторые параметры из ассета
                var material = MyMiniLib.GetAttributeString(byItemStack.Block, "material", "");  //определяем материал
                res = MyMiniLib.GetAttributeFloat(byItemStack.Block, "res", 1);
                maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 1);
                crosssectional = MyMiniLib.GetAttributeFloat(byItemStack.Block, "crosssectional", 1);

                //линий 0? Значит грань была пустая
                if (lines == 0)
                {
                    var newEparams = new EParams(indexV, maxCurrent, material, res, 1, crosssectional, false, isolated, isolatedEnvironment);
                    entity.Eparams = (newEparams, faceIndex);

                    entity.AllEparams[faceIndex] = newEparams;
                }
                else   //линий не 0, значит уже что-то там есть на грани
                {
                    //какой блок сейчас здесь находится
                    var indexV2 = entity.AllEparams[faceIndex].voltage;          //индекс напряжения этой грани
                    var indexM2 = entity.AllEparams[faceIndex].material;          //индекс материала этой грани
                    var burnout = entity.AllEparams[faceIndex].burnout;            //сгорело?
                    var iso2 = entity.AllEparams[faceIndex].isolated;            //изолировано ?

                    var block = new GetCableAsset().CableAsset(api, entity.Block, indexV2, indexM2, 1, iso2 ? 6 : 1); // берем ассет блока кабеля

                    //проверяем сколько у игрока проводов в руке и совпадают ли они с теми что есть
                    if (!CanAddCableToFace(burnout, block.Code, currentGameMode, byItemStack, lines))
                        return false;

                    if (currentGameMode != EnumGameMode.Creative) // чтобы в креативе не уменьшало стак
                        byItemStack.StackSize -= lines - 1;          // отнимаем у игрока столько же, сколько установили

                    var newEparams = new EParams(indexV, maxCurrent, material, res, lines, crosssectional, false, isolated, isolatedEnvironment);
                    entity.Eparams = (newEparams, faceIndex);

                    entity.AllEparams[faceIndex] = newEparams;
                }

                entity.Connection |= facing;
                entity.MarkDirty(true);
            }

            return true;
        }

        private bool HasSolidNeighbor(IWorldAccessor world, BlockPos pos, int faceIndex)
        {
            var neighborPos = pos.Copy();
            int checkFace;

            switch (faceIndex)
            {
                case 0: neighborPos.Z--; checkFace = 2; break;
                case 1: neighborPos.X++; checkFace = 3; break;
                case 2: neighborPos.Z++; checkFace = 0; break;
                case 3: neighborPos.X--; checkFace = 1; break;
                case 4: neighborPos.Y++; checkFace = 5; break;
                case 5: neighborPos.Y--; checkFace = 4; break;
                default: return false;
            }

            var neighborBlock = world.BlockAccessor.GetBlock(neighborPos);
            return neighborBlock != null && neighborBlock.SideIsSolid(neighborPos, checkFace);
        }

        private bool CanAddCableToFace(bool burnout, AssetLocation requiredCable, EnumGameMode gameMode, ItemStack itemStack, int requiredCount)
        {
            if (api is not ICoreClientAPI clientApi)
                return true;

            if (burnout)
            {
                clientApi.TriggerIngameError(this, "cable", "Уберите сгоревший кабель сначала.");
                return false;
            }

            if (!itemStack.Block.Code.ToString().Contains(requiredCable))
            {
                clientApi.TriggerIngameError(this, "cable", "Кабеля должны быть того же типа.");
                return false;
            }

            if (gameMode != EnumGameMode.Creative && itemStack.StackSize < requiredCount)
            {
                clientApi.TriggerIngameError(this, "cable", "Недостаточно кабелей для размещения.");
                return false;
            }

            return true;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos position, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (this.api is ICoreClientAPI)
                return;

            if (world.BlockAccessor.GetBlockEntity(position) is not BlockEntityECable entity)
            {
                base.OnBlockBroken(world, position, byPlayer, dropQuantityMultiplier);
                return;
            }

            if (byPlayer is not { CurrentBlockSelection: { } blockSelection })
            {
                base.OnBlockBroken(world, position, byPlayer, dropQuantityMultiplier);
                return;
            }

            var key = CacheDataKey.FromEntity(entity);
            var hitPosition = blockSelection.HitPosition;

            var sf = new SelectionFacingCable();
            var selectedFacing = sf.SelectionFacing(key, hitPosition, entity); // выделяем направление для слома под курсором

            //определяем какой выключатель ломать
            var faceSelect = Facing.None;
            var selectedSwitches = Facing.None;

            if (selectedFacing != Facing.None)
            {
                faceSelect = FacingHelper.FromFace(FacingHelper.Faces(selectedFacing).First());
                selectedSwitches = entity.Switches & faceSelect;
            }

            // тут ломаем переключатель
            if (selectedSwitches != Facing.None)
            {
                var switchesStackSize = FacingHelper.Faces(selectedSwitches).ToList().Count();
                if (switchesStackSize > 0)
                {
                    entity.Orientation &= ~faceSelect;
                    entity.Switches &= ~faceSelect;
                    

                    entity.MarkDirty(true);

                    var assetLocation = new AssetLocation("electricalprogressivebasics:switch-enabled");
                    var block = world.BlockAccessor.GetBlock(assetLocation);
                    var itemStack = new ItemStack(block, switchesStackSize);
                    world.SpawnItemEntity(itemStack, position.ToVec3d());

                    return;
                }
            }

            // здесь уже ломаем кабеля
            var connection = entity.Connection & ~selectedFacing; // отнимает выбранные соединения
            if (connection == Facing.None)
            {
                base.OnBlockBroken(world, position, byPlayer, dropQuantityMultiplier);
                return;
            }

            var stackSize = FacingHelper.Count(selectedFacing); // соединений выделено
            if (stackSize <= 0)
            {
                base.OnBlockBroken(world, position, byPlayer, dropQuantityMultiplier);
                return;
            }

            entity.Connection = connection;
            entity.MarkDirty(true);

            //перебираем все грани выделенных кабелей
            foreach (var face in FacingHelper.Faces(selectedFacing))
            {
                var indexV = entity.AllEparams![face.Index].voltage; //индекс напряжения этой грани
                var material = entity.AllEparams[face.Index].material; //индекс материала этой грани
                var indexQ = entity.AllEparams[face.Index].lines; //индекс линий этой грани
                var isol = entity.AllEparams[face.Index].isolated; //изолировано ли?
                var burn = entity.AllEparams[face.Index].burnout; //сгорело ли?

                // берем направления только в этой грани
                connection = selectedFacing & FacingHelper.FromFace(face);

                //если грань осталась пустая
                if ((entity.Connection & FacingHelper.FromFace(face)) == 0)
                    entity.AllEparams[face.Index] = new();

                //сколько на этой грани проводов выронить
                stackSize = FacingHelper.Count(connection) * indexQ;

                ItemStack itemStack = null!;
                if (burn) //если сгорело, то бросаем кусочки металла
                {
                    var assetLoc = new AssetLocation("metalbit-" + material);
                    var item = api.World.GetItem(assetLoc);
                    itemStack = new(item, stackSize);
                }
                else
                {
                    // берем ассет блока кабеля
                    var block = new GetCableAsset().CableAsset(api, entity.Block, indexV, material, 1, isol ? 6 : 1);
                    itemStack = new(block, stackSize);
                }

                world.SpawnItemEntity(itemStack, position.ToVec3d());
            }
        }


        /// <summary>
        /// Роняем все соединения этого блока?
        /// </summary>
        /// <param name="world"></param>
        /// <param name="position"></param>
        /// <param name="byPlayer"></param>
        /// <param name="dropQuantityMultiplier"></param>
        /// <returns></returns>
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos position, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (world.BlockAccessor.GetBlockEntity(position) is not BlockEntityECable entity)
                return base.GetDrops(world, position, byPlayer, dropQuantityMultiplier);

            var itemStacks = new ItemStack[] { };

            var connection = entity.Connection;

            foreach (var face in FacingHelper.Faces(entity.Connection))         //перебираем все грани выделенных кабелей
            {
                var indexV = entity.AllEparams![face.Index].voltage;          //индекс напряжения этой грани
                var material = entity.AllEparams[face.Index].material;          //индекс материала этой грани
                var indexQ = entity.AllEparams[face.Index].lines;          //индекс линий этой грани
                var isolated = entity.AllEparams[face.Index].isolated;          //изолировано ли?
                var burnout = entity.AllEparams[face.Index].burnout;          //сгорело ли?

                connection = entity.Connection & FacingHelper.FromFace(face);                   //берем направления только в этой грани

                if ((entity.Connection & FacingHelper.FromFace(face)) == 0) //если грань осталась пустая
                    entity.AllEparams[face.Index] = new();

                var stackSize = FacingHelper.Count(connection) * indexQ;          //сколько на этой грани проводов выронить

                var itemStack = default(ItemStack?);

                // если сгорело, то бросаем кусочки металла
                if (burnout)
                {
                    var assetLoc = new AssetLocation("metalbit-" + material);
                    var item = api.World.GetItem(assetLoc);
                    itemStack = new(item, stackSize);
                }
                else
                {
                    //берем ассет блока кабеля
                    var block = new GetCableAsset().CableAsset(api, entity.Block, indexV, material, 1, isolated ? 6 : 1);
                    itemStack = new(block, stackSize);
                }

                itemStacks = itemStacks.AddToArray(itemStack);
            }

            return itemStacks;

        }

        /// <summary>
        /// Обновился соседний блок
        /// </summary>
        /// <param name="world"></param>
        /// <param name="pos"></param>
        /// <param name="neibpos"></param>
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityECable entity)
                return;

            var blockFacing = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
            var selectedFacing = FacingHelper.FromFace(blockFacing);

            var delayReturn = false;
            if ((entity.Connection & ~selectedFacing) == Facing.None)
            {
                world.BlockAccessor.BreakBlock(pos, null);

                delayReturn = true;
                //return;
            }

            //ломаем выключатели
            var selectedSwitches = entity.Switches & selectedFacing;
            if (selectedSwitches != Facing.None)
            {
                var switchStackSize = FacingHelper.Faces(selectedSwitches).ToList().Count();
                if (switchStackSize > 0)
                {
                    var assetLocation = new AssetLocation("electricalprogressivebasics:switch-enabled");
                    var block = world.BlockAccessor.GetBlock(assetLocation);
                    var itemStack = new ItemStack(block, switchStackSize);
                    world.SpawnItemEntity(itemStack, pos.ToVec3d());
                }

                entity.Orientation &= ~selectedFacing;
                entity.Switches &= ~selectedFacing;
                
            }

            if (delayReturn)
                return;

            //ломаем провода
            var selectedConnection = entity.Connection & selectedFacing;
            if (selectedConnection == Facing.None)
                return;

            //соединений выделено
            var connectionStackSize = FacingHelper.Count(selectedConnection);
            if (connectionStackSize <= 0)
                return;

            entity.Connection &= ~selectedConnection;

            foreach (var face in FacingHelper.Faces(selectedConnection))         //перебираем все грани выделенных кабелей
            {
                var indexV = entity.AllEparams![face.Index].voltage;          //индекс напряжения этой грани
                var material = entity.AllEparams![face.Index].material;          //индекс материала этой грани
                var indexQ = entity.AllEparams![face.Index].lines;          //индекс линий этой грани
                var isolated = entity.AllEparams![face.Index].isolated;          //изолировано ли?
                var burnout = entity.AllEparams![face.Index].burnout;          //сгорело ли?

                var connection = selectedConnection & FacingHelper.FromFace(face);                   //берем направления только в этой грани

                if ((entity.Connection & FacingHelper.FromFace(face)) == 0) //если грань осталась пустая
                    entity.AllEparams[face.Index] = new();

                connectionStackSize = FacingHelper.Count(connection) * indexQ;          //сколько на этой грани проводов выронить

                var itemStack = default(ItemStack?);
                if (burnout)       //если сгорело, то бросаем кусочки металла
                {
                    AssetLocation assetLoc = new("metalbit-" + material);
                    var item = api.World.GetItem(assetLoc);
                    itemStack = new(item, connectionStackSize);
                }
                else
                {
                    var block = new GetCableAsset().CableAsset(api, entity.Block, indexV, material, 1, isolated ? 6 : 1); //берем ассет блока кабеля
                    itemStack = new(block, connectionStackSize);
                }

                world.SpawnItemEntity(itemStack, pos.ToVec3d());
            }
        }

        /// <summary>
        /// взаимодействие с кабелем/переключателем
        /// </summary>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSel"></param>
        /// <returns></returns>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (this.api is ICoreClientAPI)
                return true;

            //это кабель?
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityECable entity)
            {
                var key = CacheDataKey.FromEntity(entity);
                var hitPosition = blockSel.HitPosition;

                var sf = new SelectionFacingCable();
                var selectedFacing = sf.SelectionFacing(key, hitPosition, entity);  //выделяем грань выключателя

                var selectedSwitches = selectedFacing & entity.Switches;
                if (selectedSwitches != 0)
                {
                    entity.SwitchesState ^= selectedSwitches;
                    return true;
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }


        /// <summary>
        /// Переопределение системной функции выделений
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos position)
        {
            if (blockAccessor.GetBlockEntity(position) is BlockEntityECable { AllEparams: not null } entity)
            {
                var key = CacheDataKey.FromEntity(entity);

                var boxes = CalculateBoxes(key, BlockECable.SelectionBoxesCache, entity);
                return boxes.Values.ToArray() // копируем значения
                    .SelectMany(x => x)
                    .Distinct()
                    .ToArray();

            }

            return base.GetSelectionBoxes(blockAccessor, position);
        }


        /// <summary>
        /// Переопределение системной функции коллизий
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos position)
        {
            if (blockAccessor.GetBlockEntity(position) is BlockEntityECable { AllEparams: not null } entity)
            {
                var key = CacheDataKey.FromEntity(entity);

                var boxes = CalculateBoxes(key, BlockECable.CollisionBoxesCache, entity);
                return boxes.Values.ToArray() // копируем значения
                    .SelectMany(x => x)
                    .Distinct()
                    .ToArray();

            }

            return base.GetSelectionBoxes(blockAccessor, position);
        }



        /// <summary>
        /// Помогает рандомизировать шейпы
        /// </summary>
        /// <param name="rand"></param>
        /// <returns></returns>
        private float RndHelp(ref Random rand)
        {
            return (float)((rand.NextDouble() * 0.01F) - 0.005F + 1.0F);
        }



        /// <summary>
        /// Отрисовщик шейпов
        /// </summary>
        /// <param name="sourceMesh"></param>
        /// <param name="lightRgbsByCorner"></param>
        /// <param name="position"></param>
        /// <param name="chunkExtBlocks"></param>
        /// <param name="extIndex3d"></param>
        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos position, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (this.api.World.BlockAccessor.GetBlockEntity(position) is BlockEntityECable entity
                && entity.Connection != Facing.None
                && entity.AllEparams != null
                && entity.Block.Code.ToString().Contains("ecable"))
            {
                var key = CacheDataKey.FromEntity(entity);

                if (!BlockECable.MeshDataCache.TryGetValue(key, out var meshData))
                {
                    var origin = new Vec3f(0.5f, 0.5f, 0.5f);
                    var origin0 = new Vec3f(0f, 0f, 0f);

                    // инициализируем рандомайзер системный
                    var rnd = new Random();

                    int bufIndex, indexV;
                    EParams eparam;
                    string indexM;
                    byte indexQ;
                    bool indexB, isol;

                    // рисуем на северной грани
                    if ((key.Connection & Facing.NorthAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.NorthAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?


                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 0.0f));


                        if ((key.Connection & Facing.NorthEast) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD, 0.0f)); //ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0.5F, 0, 0));   //cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.NorthWest) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(-0.5F, 0, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.NorthUp) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0, 0.5F, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.NorthDown) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0, -0.5F, 0));//cтавим крепление на ребре
                        }

                    }

                    // рисуем на восточной грани
                    if ((key.Connection & Facing.EastAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.EastAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?

                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f, 90.0f * GameMath.DEG2RAD));

                        if ((key.Connection & Facing.EastNorth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 90.0f * GameMath.DEG2RAD).Translate(0, 0, -0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.EastSouth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 90.0f * GameMath.DEG2RAD).Translate(0, 0, 0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.EastUp) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 90.0f * GameMath.DEG2RAD).Translate(0, 0.5F, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.EastDown) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 90.0f * GameMath.DEG2RAD).Translate(0, -0.5F, 0));//cтавим крепление на ребре
                        }
                    }

                    // рисуем на южной грани
                    if ((key.Connection & Facing.SouthAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.SouthAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?

                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 0.0f));

                        if ((key.Connection & Facing.SouthEast) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0.5F, 0, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.SouthWest) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(-0.5F, 0, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.SouthUp) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0, 0.5F, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.SouthDown) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 0.0f).Translate(0, -0.5F, 0));//cтавим крепление на ребре
                        }
                    }

                    // рисуем на западной грани
                    if ((key.Connection & Facing.WestAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.WestAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?

                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f, 270.0f * GameMath.DEG2RAD));

                        if ((key.Connection & Facing.WestNorth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 270.0f * GameMath.DEG2RAD).Translate(0, 0, -0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.WestSouth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 270.0f * GameMath.DEG2RAD).Translate(0, 0, 0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.WestUp) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 270.0f * GameMath.DEG2RAD).Translate(0, 0.5F, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.WestDown) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 270.0f * GameMath.DEG2RAD).Translate(0, -0.5F, 0));//cтавим крепление на ребре
                        }
                    }

                    // рисуем на верхней грани
                    if ((key.Connection & Facing.UpAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.UpAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?

                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f, 180.0f * GameMath.DEG2RAD));

                        if ((key.Connection & Facing.UpNorth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 180.0f * GameMath.DEG2RAD).Translate(0, 0, -0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.UpSouth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 180.0f * GameMath.DEG2RAD).Translate(0, 0, 0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.UpEast) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 180.0f * GameMath.DEG2RAD).Translate(0.5F, 0, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.UpWest) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 180.0f * GameMath.DEG2RAD).Translate(-0.5F, 0, 0));//cтавим крепление на ребре
                        }
                    }

                    // рисуем на нижней грани
                    if ((key.Connection & Facing.DownAll) != 0)
                    {
                        bufIndex = FacingHelper.Faces(Facing.DownAll).First().Index; //индекс грани
                        eparam = entity.AllEparams[bufIndex];
                        indexV = eparam.voltage; //индекс напряжения этой грани
                        indexM = eparam.material; //индекс материала этой грани
                        indexQ = eparam.lines;  //индекс линий этой грани
                        indexB = eparam.burnout; //индекс перегорания этой грани
                        isol = eparam.isolated; //изолировано ли?

                        var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);   //получаем шейп нужной точки кабеля

                        BlockVariants partVariant;
                        if (!indexB)
                        {
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1);  //получаем шейп нужного кабеля изолированного или целого
                        }
                        else
                            partVariant = new(api, entity.Block, indexV, indexM, indexQ, 3);  //получаем шейп нужного кабеля сгоревшего

                        var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);   //получаем шейп крепления кабеля

                        //ставим точку посередине, если провода не перегорел
                        if (!indexB)
                            AddMeshData(ref meshData, fixVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f, 0.0f));

                        if ((key.Connection & Facing.DownNorth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 0.0f).Translate(0, 0, -0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.DownSouth) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 0.0f).Translate(0, 0, 0.5F));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.DownEast) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 0.0f).Translate(0.5F, 0, 0));//cтавим крепление на ребре
                        }

                        if ((key.Connection & Facing.DownWest) != 0)
                        {
                            AddMeshData(ref meshData, partVariant.MeshData?.Clone().Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 0.0f));//ставим кусок
                            AddMeshData(ref meshData, dotVariant.MeshData?.Clone().Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd)).Rotate(origin, 0.0f, 0.0f, 0.0f).Translate(-0.5F, 0, 0));//cтавим крепление на ребре
                        }
                    }

                    // Переключатели
                    if ((key.Orientation & Facing.NorthEast) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                90.0f * GameMath.DEG2RAD,
                                0.0f
                            )
                        );
                    }

                    if ((key.Orientation & Facing.NorthWest) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                270.0f * GameMath.DEG2RAD,
                                0.0f
                            )
                        );
                    }

                    if ((key.Orientation & Facing.NorthUp) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD,
                                0.0f
                            )
                        );
                    }

                    if ((key.Orientation & Facing.NorthDown) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                0.0f * GameMath.DEG2RAD,
                                0.0f
                            )
                        );
                    }

                    if ((key.Orientation & Facing.EastNorth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                180.0f * GameMath.DEG2RAD,
                                0.0f,
                                90.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.EastSouth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f * GameMath.DEG2RAD,
                                0.0f,
                                90.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.EastUp) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                270.0f * GameMath.DEG2RAD,
                                0.0f,
                                90.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.EastDown) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                0.0f,
                                90.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.SouthEast) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                90.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.SouthWest) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                270.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.SouthUp) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.SouthDown) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                0.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.WestNorth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                180.0f * GameMath.DEG2RAD,
                                0.0f,
                                270.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.WestSouth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f * GameMath.DEG2RAD,
                                0.0f,
                                270.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.WestUp) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                270.0f * GameMath.DEG2RAD,
                                0.0f,
                                270.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.WestDown) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                90.0f * GameMath.DEG2RAD,
                                0.0f,
                                270.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.UpNorth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f,
                                180.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.UpEast) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f,
                                90.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.UpSouth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f,
                                0.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.UpWest) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone().Rotate(
                                origin,
                                0.0f,
                                270.0f * GameMath.DEG2RAD,
                                180.0f * GameMath.DEG2RAD
                            )
                        );
                    }

                    if ((key.Orientation & Facing.DownNorth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone()
                            .Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 0.0f)
                        );
                    }

                    if ((key.Orientation & Facing.DownEast) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone()
                            .Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 0.0f)
                        );
                    }

                    if ((key.Orientation & Facing.DownSouth) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone()
                            .Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 0.0f)
                        );
                    }

                    if ((key.Orientation & Facing.DownWest) != 0)
                    {
                        AddMeshData(
                            ref meshData,
                            ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                                ? enabledSwitchVariant
                                : disabledSwitchVariant)?.MeshData?.Clone()
                            .Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 0.0f)
                        );
                    }

                    BlockECable.MeshDataCache[key] = meshData!;
                }

                sourceMesh = meshData ?? sourceMesh;
            }

            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
        }

        /// <summary>
        /// Просчет коллайдеров (коллизии проводов должны совпадать с коллизиями выделения)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="boxesCache"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static Dictionary<Facing, Cuboidf[]> CalculateBoxes(CacheDataKey key, IDictionary<CacheDataKey, Dictionary<Facing, Cuboidf[]>> boxesCache, BlockEntityECable entity)
        {
            if (!boxesCache.TryGetValue(key, out var boxes)
                && entity.Connection != Facing.None
                && entity.Block.Code.ToString().Contains("ecable"))
            {
                var origin = new Vec3d(0.5, 0.5, 0.5);

                boxesCache[key] = boxes = new();

                int bufIndex, indexV;
                EParams eparam;
                string indexM;
                byte indexQ;
                bool indexB, isol;


                // Connections
                if ((key.Connection & Facing.NorthAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.NorthAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?


                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.NorthAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 0.0f, origin)).ToArray());


                    if ((key.Connection & Facing.NorthEast) != 0)
                    {
                        boxes.Add(Facing.NorthEast, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 270.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.NorthWest) != 0)
                    {
                        boxes.Add(Facing.NorthWest, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 90.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.NorthUp) != 0)
                    {
                        boxes.Add(Facing.NorthUp, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.NorthDown) != 0)
                    {
                        boxes.Add(Facing.NorthDown, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 180.0f, 0.0f, origin)).ToArray());
                    }

                }


                if ((key.Connection & Facing.EastAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.EastAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?


                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.EastAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 90.0f, origin)).ToArray());

                    if ((key.Connection & Facing.EastNorth) != 0)
                    {
                        boxes.Add(Facing.EastNorth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 90.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.EastSouth) != 0)
                    {
                        boxes.Add(Facing.EastSouth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(180.0f, 0.0f, 90.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.EastUp) != 0)
                    {
                        boxes.Add(Facing.EastUp, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 90.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.EastDown) != 0)
                    {
                        boxes.Add(Facing.EastDown, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 90.0f, origin)).ToArray());
                    }
                }

                if ((key.Connection & Facing.SouthAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.SouthAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?


                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.SouthAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 0.0f, origin)).ToArray());

                    if ((key.Connection & Facing.SouthEast) != 0)
                    {
                        boxes.Add(Facing.SouthEast, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 270.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.SouthWest) != 0)
                    {
                        boxes.Add(Facing.SouthWest, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 90.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.SouthUp) != 0)
                    {
                        boxes.Add(Facing.SouthUp, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 180.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.SouthDown) != 0)
                    {
                        boxes.Add(Facing.SouthDown, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 0.0f, origin)).ToArray());
                    }
                }



                if ((key.Connection & Facing.WestAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.WestAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?


                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.WestAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 270.0f, origin)).ToArray());

                    if ((key.Connection & Facing.WestNorth) != 0)
                    {
                        boxes.Add(Facing.WestNorth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 270.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.WestSouth) != 0)
                    {
                        boxes.Add(Facing.WestSouth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(180.0f, 0.0f, 270.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.WestUp) != 0)
                    {
                        boxes.Add(Facing.WestUp, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 270.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.WestDown) != 0)
                    {
                        boxes.Add(Facing.WestDown, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 270.0f, origin)).ToArray());
                    }
                }



                if ((key.Connection & Facing.UpAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.UpAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?



                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.UpAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 180.0f, origin)).ToArray());

                    if ((key.Connection & Facing.UpNorth) != 0)
                    {
                        boxes.Add(Facing.UpNorth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 180.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.UpEast) != 0)
                    {
                        boxes.Add(Facing.UpEast, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 270.0f, 180.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.UpSouth) != 0)
                    {
                        boxes.Add(Facing.UpSouth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 180.0f, 180.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.UpWest) != 0)
                    {
                        boxes.Add(Facing.UpWest, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 90.0f, 180.0f, origin)).ToArray());
                    }
                }



                if ((key.Connection & Facing.DownAll) != 0)
                {
                    bufIndex = FacingHelper.Faces(Facing.DownAll).First().Index; //индекс грани
                    eparam = entity.AllEparams![bufIndex];
                    indexV = eparam.voltage; //индекс напряжения этой грани
                    indexM = eparam.material; //индекс материала этой грани
                    indexQ = eparam.lines;  //индекс линий этой грани
                    indexB = eparam.burnout; //индекс перегорания этой грани
                    isol = eparam.isolated; //изолировано ли?



                    Cuboidf[] partBoxes;
                    if (!indexB)
                    {
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes;  //получаем шейп нужного кабеля изолированного или целого
                    }
                    else
                        partBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;  //получаем шейп нужного кабеля сгоревшего

                    var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;   //получаем шейп крепления кабеля

                    //ставим точку посередине, если провода не перегорел
                    if (!indexB)
                        boxes.Add(Facing.DownAll, fixBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 0.0f, origin)).ToArray());

                    if ((key.Connection & Facing.DownNorth) != 0)
                    {
                        boxes.Add(Facing.DownNorth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.DownEast) != 0)
                    {
                        boxes.Add(Facing.DownEast, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 270.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.DownSouth) != 0)
                    {
                        boxes.Add(Facing.DownSouth, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 180.0f, 0.0f, origin)).ToArray());
                    }

                    if ((key.Connection & Facing.DownWest) != 0)
                    {
                        boxes.Add(Facing.DownWest, partBoxes.Select(selectionBox => selectionBox.RotatedCopy(0.0f, 90.0f, 0.0f, origin)).ToArray());
                    }
                }

                Cuboidf[] enabledSwitchBoxes = enabledSwitchVariant?.CollisionBoxes ?? Array.Empty<Cuboidf>();
                Cuboidf[] disabledSwitchBoxes = disabledSwitchVariant?.CollisionBoxes ?? Array.Empty<Cuboidf>();

                // переключатели
                if ((key.Orientation & Facing.NorthEast) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.NorthAll,
                        ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 90.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.NorthWest) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.NorthAll,
                        ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 270.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.NorthUp) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.NorthAll,
                        ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 180.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.NorthDown) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.NorthAll,
                        ((key.Orientation & key.SwitchesState & Facing.NorthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.EastNorth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.EastAll,
                        ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(180.0f, 0.0f, 90.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.EastSouth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.EastAll,
                        ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 90.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.EastUp) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.EastAll,
                        ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 90.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.EastDown) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.EastAll,
                        ((key.Orientation & key.SwitchesState & Facing.EastAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 90.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.SouthEast) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.SouthAll,
                        ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 90.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.SouthWest) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.SouthAll,
                        ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes).Select(
                            selectionBox =>
                                selectionBox.RotatedCopy(90.0f, 270.0f, 180.0f, origin)
                        ).ToArray()
                    );
                }

                if ((key.Orientation & Facing.SouthUp) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.SouthAll,
                        ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes).Select(
                            selectionBox =>
                                selectionBox.RotatedCopy(90.0f, 180.0f, 180.0f, origin)
                        ).ToArray()
                    );
                }

                if ((key.Orientation & Facing.SouthDown) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.SouthAll,
                        ((key.Orientation & key.SwitchesState & Facing.SouthAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.WestNorth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.WestAll,
                        ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(180.0f, 0.0f, 270.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.WestSouth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.WestAll,
                        ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 270.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.WestUp) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.WestAll,
                        ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(270.0f, 0.0f, 270.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.WestDown) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.WestAll,
                        ((key.Orientation & key.SwitchesState & Facing.WestAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(90.0f, 0.0f, 270.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.UpNorth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.UpAll,
                        ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 180.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.UpEast) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.UpAll,
                        ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 90.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.UpSouth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.UpAll,
                        ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.UpWest) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.UpAll,
                        ((key.Orientation & key.SwitchesState & Facing.UpAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 270.0f, 180.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.DownNorth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.DownAll,
                        ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 180.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.DownEast) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.DownAll,
                        ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 90.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.DownSouth) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.DownAll,
                        ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 0.0f, 0.0f, origin)).ToArray()
                    );
                }

                if ((key.Orientation & Facing.DownWest) != 0)
                {
                    AddBoxes(
                        ref boxes,
                        Facing.DownAll,
                        ((key.Orientation & key.SwitchesState & Facing.DownAll) != 0
                            ? enabledSwitchBoxes
                            : disabledSwitchBoxes)
                        .Select(selectionBox => selectionBox.RotatedCopy(0.0f, 270.0f, 0.0f, origin)).ToArray()
                    );
                }
            }

            //если это не кабель, то просто возвращаем коллайдеры
            if (!entity.Block.Code.ToString().Contains("ecable"))
            {
                boxes = new Dictionary<Facing, Cuboidf[]>();
                boxes.Add(Facing.NorthAll, entity.Block.CollisionBoxes);
            }

            return boxes!;
        }

        private static void AddBoxes(ref Dictionary<Facing, Cuboidf[]> cache, Facing key, Cuboidf[] boxes)
        {
            if (cache.ContainsKey(key))
            {
                cache[key] = cache[key].Concat(boxes).ToArray();
            }
            else
            {
                cache[key] = boxes;
            }
        }

        private static void AddMeshData(ref MeshData? sourceMesh, MeshData? meshData)
        {
            if (meshData != null)
            {
                if (sourceMesh != null)
                {
                    sourceMesh.AddMeshData(meshData);
                }
                else
                {
                    sourceMesh = meshData;
                }
            }
        }

        /// <summary>
        /// Получение информации о предмете в инвентаре
        /// </summary>
        /// <param name="inSlot"></param>
        /// <param name="dsc"></param>
        /// <param name="world"></param>
        /// <param name="withDebugInfo"></param>
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            string text = inSlot.Itemstack.Block.Variant["voltage"];
            dsc.AppendLine(Lang.Get("Voltage") + ": " + text.Substring(0, text.Length - 1) + " " + Lang.Get("V"));
            dsc.AppendLine(Lang.Get("Max. current") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxCurrent", 0) + " " + Lang.Get("A"));
            dsc.AppendLine(Lang.Get("Resistivity") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "res", 0) + " " + Lang.Get("units"));
            dsc.AppendLine(Lang.Get("WResistance") + ": " + (inSlot.Itemstack.Block.Code.Path.Contains("isolated") ? Lang.Get("Yes") : Lang.Get("No")));
        }


        /// <summary>
        /// Получение подсказок для взаимодействия с блоком
        /// </summary>
        /// <param name="world"></param>
        /// <param name="selection"></param>
        /// <param name="forPlayer"></param>
        /// <returns></returns>
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "ThickenCables",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right                    
                }
            }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }




        /// <summary>
        /// Структура для хранения ключей для словарей
        /// </summary>
        public struct CacheDataKey : IEquatable<CacheDataKey>
        {
            public readonly Facing Connection;
            public readonly Facing SwitchesState;
            public readonly Facing Orientation;
            public readonly EParams[] AllEparams;

            public CacheDataKey(Facing connection, Facing orientation, Facing switchesState, EParams[] allEparams)
            {
                Connection = connection;
                Orientation = orientation;
                SwitchesState = switchesState;
                AllEparams = allEparams;
            }

            public static CacheDataKey FromEntity(BlockEntityECable entityE)
            {
                EParams[] bufAllEparams = entityE.AllEparams!.ToArray();
                return new(
                    entityE.Connection,
                    entityE.Orientation,
                    entityE.SwitchesState,
                    bufAllEparams
                );
            }

            public bool Equals(CacheDataKey other)
            {
                if (Connection != other.Connection ||
                    Orientation != other.Orientation ||
                    SwitchesState != other.SwitchesState ||
                    AllEparams.Length != other.AllEparams.Length)
                    return false;

                for (int i = 0; i < AllEparams.Length; i++)
                {
                    if (!AllEparams[i].Equals(other.AllEparams[i]))
                        return false;
                }

                return true;
            }

            public override bool Equals(object? obj)
            {
                return obj is CacheDataKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + Connection.GetHashCode();
                    hash = hash * 31 + Orientation.GetHashCode();
                    hash = hash * 31 + SwitchesState.GetHashCode();
                    foreach (var param in AllEparams)
                    {
                        hash = hash * 31 + param.GetHashCode();
                    }
                    return hash;
                }
            }
        }
    }
}
