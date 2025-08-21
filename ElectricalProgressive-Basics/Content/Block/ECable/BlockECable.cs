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
        // ---- Оптимизированный OnJsonTesselation ----
        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos position, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (this.api.World.BlockAccessor.GetBlockEntity(position) is not BlockEntityECable entity
                || entity.Connection == Facing.None
                || entity.AllEparams == null
                || !entity.Block.Code.ToString().Contains("ecable"))
            {
                base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
                return;
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!BlockECable.MeshDataCache.TryGetValue(key, out var meshData))
            {
                MeshData? built = null;
                var origin = new Vec3f(0.5f, 0.5f, 0.5f);
                var origin0 = new Vec3f(0f, 0f, 0f);
                var rnd = new Random();
                var deg2rad = GameMath.DEG2RAD;

                // helper to add rotated+translated dot
                void AddDot(MeshData? md, MeshData? dot, float rx, float ry, float rz, Vec3f trans)
                {
                    if (dot == null) return;
                    var m = dot.Clone()
                               .Scale(origin0, RndHelp(ref rnd), RndHelp(ref rnd), RndHelp(ref rnd))
                               .Rotate(origin, rx * deg2rad, ry * deg2rad, rz * deg2rad)
                               .Translate(trans.X, trans.Y, trans.Z);
                    AddMeshData(ref built, m);
                }

                // helper to add rotated part
                void AddPart(MeshData? md, MeshData? part, float rx, float ry, float rz)
                {
                    if (part == null) return;
                    AddMeshData(ref built, part.Clone().Rotate(origin, rx * deg2rad, ry * deg2rad, rz * deg2rad));
                }

                // Generic face processor: takes face mask, function that returns rotations/translations for subfaces
                void ProcessFace(Facing faceAll, (Facing sub, float rx, float ry, float rz, Vec3f trans)[] subs, float fixRx, float fixRy, float fixRz)
                {
                    if ((key.Connection & faceAll) == 0) return;

                    int bufIndex = FacingHelper.Faces(faceAll).First().Index;
                    var eparam = entity.AllEparams[bufIndex];
                    var indexV = eparam.voltage;
                    var indexM = eparam.material;
                    var indexQ = eparam.lines;
                    var indexB = eparam.burnout;
                    var isol = eparam.isolated;

                    var dotVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 7 : 0);
                    BlockVariants partVariant = !indexB
                        ? new BlockVariants(api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1)
                        : new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 3);
                    var fixVariant = new BlockVariants(api, entity.Block, indexV, indexM, indexQ, 4);

                    if (!indexB)
                    {
                        AddMeshData(ref built, fixVariant.MeshData?.Clone().Rotate(origin, fixRx * deg2rad, fixRy * deg2rad, fixRz * deg2rad));
                    }

                    foreach (var (sub, rx, ry, rz, trans) in subs)
                    {
                        if ((key.Connection & sub) != 0)
                        {
                            AddPart(built, partVariant.MeshData, rx, ry, rz);
                            AddDot(built, dotVariant.MeshData, rx, ry, rz, trans);
                        }
                    }
                }

                // Define subfaces for each face (rotations in degrees, translations)
                ProcessFace(Facing.NorthAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.NorthEast, 90f, 270f, 0f, new Vec3f(0.5f, 0f, 0f)),
            (Facing.NorthWest, 90f, 90f, 0f, new Vec3f(-0.5f, 0f, 0f)),
            (Facing.NorthUp, 90f, 0f, 0f, new Vec3f(0f, 0.5f, 0f)),
            (Facing.NorthDown, 90f, 180f, 0f, new Vec3f(0f, -0.5f, 0f))
                }, 90f, 0f, 0f);

                ProcessFace(Facing.EastAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.EastNorth, 0f, 0f, 90f, new Vec3f(0f, 0f, -0.5f)),
            (Facing.EastSouth, 180f, 0f, 90f, new Vec3f(0f, 0f, 0.5f)),
            (Facing.EastUp, 90f, 0f, 90f, new Vec3f(0f, 0.5f, 0f)),
            (Facing.EastDown, 270f, 0f, 90f, new Vec3f(0f, -0.5f, 0f))
                }, 0f, 0f, 90f);

                ProcessFace(Facing.SouthAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.SouthEast, 270f, 270f, 0f, new Vec3f(0.5f, 0f, 0f)),
            (Facing.SouthWest, 270f, 90f, 0f, new Vec3f(-0.5f, 0f, 0f)),
            (Facing.SouthUp, 270f, 180f, 0f, new Vec3f(0f, 0.5f, 0f)),
            (Facing.SouthDown, 270f, 0f, 0f, new Vec3f(0f, -0.5f, 0f))
                }, 270f, 0f, 0f);

                ProcessFace(Facing.WestAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.WestNorth, 0f, 0f, 270f, new Vec3f(0f, 0f, -0.5f)),
            (Facing.WestSouth, 180f, 0f, 270f, new Vec3f(0f, 0f, 0.5f)),
            (Facing.WestUp, 90f, 0f, 270f, new Vec3f(0f, 0.5f, 0f)),
            (Facing.WestDown, 270f, 0f, 270f, new Vec3f(0f, -0.5f, 0f))
                }, 0f, 0f, 270f);

                ProcessFace(Facing.UpAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.UpNorth, 0f, 0f, 180f, new Vec3f(0f, 0f, -0.5f)),
            (Facing.UpEast, 0f, 270f, 180f, new Vec3f(0.5f, 0f, 0f)),
            (Facing.UpSouth, 0f, 180f, 180f, new Vec3f(0f, 0f, 0.5f)),
            (Facing.UpWest, 0f, 90f, 180f, new Vec3f(-0.5f, 0f, 0f))
                }, 0f, 0f, 180f);

                ProcessFace(Facing.DownAll, new (Facing, float, float, float, Vec3f)[]
                {
            (Facing.DownNorth, 0f, 0f, 0f, new Vec3f(0f, 0f, -0.5f)),
            (Facing.DownSouth, 0f, 180f, 0f, new Vec3f(0f, 0f, 0.5f)),
            (Facing.DownEast, 0f, 270f, 0f, new Vec3f(0.5f, 0f, 0f)),
            (Facing.DownWest, 0f, 90f, 0f, new Vec3f(-0.5f, 0f, 0f))
                }, 0f, 0f, 0f);

                // Switches (orientation): reuse enabled/disabled variants with precomputed rotations
                void AddSwitchIf(Facing mask, Facing allMask, float rx, float ry, float rz)
                {
                    if ((key.Orientation & mask) != 0)
                    {
                        var variant = ((key.Orientation & key.SwitchesState & allMask) != 0 ? enabledSwitchVariant : disabledSwitchVariant);
                        AddMeshData(ref built, variant?.MeshData?.Clone().Rotate(origin, rx * deg2rad, ry * deg2rad, rz * deg2rad));
                    }
                }

                // All switch placements (kept same rotations as original)
                AddSwitchIf(Facing.NorthEast, Facing.NorthAll, 90f, 90f, 0f);
                AddSwitchIf(Facing.NorthWest, Facing.NorthAll, 90f, 270f, 0f);
                AddSwitchIf(Facing.NorthUp, Facing.NorthAll, 90f, 180f, 0f);
                AddSwitchIf(Facing.NorthDown, Facing.NorthAll, 90f, 0f, 0f);

                AddSwitchIf(Facing.EastNorth, Facing.EastAll, 180f, 0f, 90f);
                AddSwitchIf(Facing.EastSouth, Facing.EastAll, 0f, 0f, 90f);
                AddSwitchIf(Facing.EastUp, Facing.EastAll, 270f, 0f, 90f);
                AddSwitchIf(Facing.EastDown, Facing.EastAll, 90f, 0f, 90f);

                AddSwitchIf(Facing.SouthEast, Facing.SouthAll, 90f, 90f, 180f);
                AddSwitchIf(Facing.SouthWest, Facing.SouthAll, 90f, 270f, 180f);
                AddSwitchIf(Facing.SouthUp, Facing.SouthAll, 90f, 180f, 180f);
                AddSwitchIf(Facing.SouthDown, Facing.SouthAll, 90f, 0f, 180f);

                AddSwitchIf(Facing.WestNorth, Facing.WestAll, 180f, 0f, 270f);
                AddSwitchIf(Facing.WestSouth, Facing.WestAll, 0f, 0f, 270f);
                AddSwitchIf(Facing.WestUp, Facing.WestAll, 270f, 0f, 270f);
                AddSwitchIf(Facing.WestDown, Facing.WestAll, 90f, 0f, 270f);

                AddSwitchIf(Facing.UpNorth, Facing.UpAll, 0f, 180f, 180f);
                AddSwitchIf(Facing.UpEast, Facing.UpAll, 0f, 90f, 180f);
                AddSwitchIf(Facing.UpSouth, Facing.UpAll, 0f, 0f, 180f);
                AddSwitchIf(Facing.UpWest, Facing.UpAll, 0f, 270f, 180f);

                AddSwitchIf(Facing.DownNorth, Facing.DownAll, 0f, 180f, 0f);
                AddSwitchIf(Facing.DownEast, Facing.DownAll, 0f, 90f, 0f);
                AddSwitchIf(Facing.DownSouth, Facing.DownAll, 0f, 0f, 0f);
                AddSwitchIf(Facing.DownWest, Facing.DownAll, 0f, 270f, 0f);

                // store to cache
                BlockECable.MeshDataCache[key] = built ?? new MeshData();
                meshData = BlockECable.MeshDataCache[key];
            }

            sourceMesh = meshData ?? sourceMesh;
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, position, chunkExtBlocks, extIndex3d);
        }


        // ---- Оптимизированный CalculateBoxes ----
        public static Dictionary<Facing, Cuboidf[]> CalculateBoxes(CacheDataKey key, IDictionary<CacheDataKey, Dictionary<Facing, Cuboidf[]>> boxesCache, BlockEntityECable entity)
        {
            // Если уже в кэше — сразу вернуть
            if (boxesCache.TryGetValue(key, out var cached))
                return cached;

            // Если нет соединений или не ecable — вернуть дефолтные коллайдеры для блока
            if (entity.Connection == Facing.None || !entity.Block.Code.ToString().Contains("ecable"))
            {
                var simple = new Dictionary<Facing, Cuboidf[]>();
                simple.Add(Facing.NorthAll, entity.Block.CollisionBoxes);
                boxesCache[key] = simple;
                return simple;
            }

            var origin = new Vec3d(0.5, 0.5, 0.5);
            var boxes = new Dictionary<Facing, Cuboidf[]>();

            // helper для добавления набора RotatedCopy'ов
            void AddRotated(Facing addKey, Cuboidf[] baseBoxes, double rx, double ry, double rz)
            {
                AddBoxes(ref boxes, addKey, baseBoxes.Select(b => b.RotatedCopy((float)rx, (float)ry, (float)rz, origin)).ToArray());
            }

            // Обрабатываем каждую грань: один раз вычисляем partBoxes и fixBoxes, потом добавляем нужные повороты
            void ProcessFaceBoxes(Facing faceAll,
                (Facing sub, double rx, double ry, double rz)[] subs,
                double fixRx, double fixRy, double fixRz)
            {
                if ((key.Connection & faceAll) == 0) return;

                int bufIndex = FacingHelper.Faces(faceAll).First().Index;
                var eparam = entity.AllEparams![bufIndex];
                var indexV = eparam.voltage;
                var indexM = eparam.material;
                var indexQ = eparam.lines;
                var indexB = eparam.burnout;
                var isol = eparam.isolated;

                Cuboidf[] partBoxes = !indexB
                    ? new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, isol ? 6 : 1).CollisionBoxes
                    : new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 3).CollisionBoxes;

                var fixBoxes = new BlockVariants(entity.Api, entity.Block, indexV, indexM, indexQ, 4).CollisionBoxes;

                if (!indexB)
                {
                    AddRotated(faceAll, fixBoxes, fixRx, fixRy, fixRz);
                }

                foreach (var (sub, rx, ry, rz) in subs)
                {
                    if ((key.Connection & sub) != 0)
                    {
                        AddRotated(sub, partBoxes, rx, ry, rz);
                    }
                }
            }

            ProcessFaceBoxes(Facing.NorthAll, new (Facing, double, double, double)[]
            {
        (Facing.NorthEast, 90, 270, 0),
        (Facing.NorthWest, 90, 90, 0),
        (Facing.NorthUp, 90, 0, 0),
        (Facing.NorthDown, 90, 180, 0)
            }, 90, 0, 0);

            ProcessFaceBoxes(Facing.EastAll, new (Facing, double, double, double)[]
            {
        (Facing.EastNorth, 0, 0, 90),
        (Facing.EastSouth, 180, 0, 90),
        (Facing.EastUp, 90, 0, 90),
        (Facing.EastDown, 270, 0, 90)
            }, 0, 0, 90);

            ProcessFaceBoxes(Facing.SouthAll, new (Facing, double, double, double)[]
            {
        (Facing.SouthEast, 270, 270, 0),
        (Facing.SouthWest, 270, 90, 0),
        (Facing.SouthUp, 270, 180, 0),
        (Facing.SouthDown, 270, 0, 0)
            }, 270, 0, 0);

            ProcessFaceBoxes(Facing.WestAll, new (Facing, double, double, double)[]
            {
        (Facing.WestNorth, 0, 0, 270),
        (Facing.WestSouth, 180, 0, 270),
        (Facing.WestUp, 90, 0, 270),
        (Facing.WestDown, 270, 0, 270)
            }, 0, 0, 270);

            ProcessFaceBoxes(Facing.UpAll, new (Facing, double, double, double)[]
            {
        (Facing.UpNorth, 0, 0, 180),
        (Facing.UpEast, 0, 270, 180),
        (Facing.UpSouth, 0, 180, 180),
        (Facing.UpWest, 0, 90, 180)
            }, 0, 0, 180);

            ProcessFaceBoxes(Facing.DownAll, new (Facing, double, double, double)[]
            {
        (Facing.DownNorth, 0, 0, 0),
        (Facing.DownSouth, 0, 180, 0),
        (Facing.DownEast, 0, 270, 0),
        (Facing.DownWest, 0, 90, 0)
            }, 0, 0, 0);

            // Switch colliders (use block variants for switches if present)
            // Precompute switch boxes once
            var enabledSwitchBoxes = enabledSwitchVariant?.CollisionBoxes;
            var disabledSwitchBoxes = disabledSwitchVariant?.CollisionBoxes;
            if (enabledSwitchBoxes != null || disabledSwitchBoxes != null)
            {
                void AddSwitchBoxesIf(Facing mask, Facing allMask, double rx, double ry, double rz)
                {
                    if ((key.Orientation & mask) != 0)
                    {
                        var boxesToUse = ((key.Orientation & key.SwitchesState & allMask) != 0 ? enabledSwitchBoxes : disabledSwitchBoxes) ?? enabledSwitchBoxes ?? disabledSwitchBoxes;
                        AddBoxes(ref boxes, allMask, boxesToUse.Select(b => b.RotatedCopy((float)rx, (float)ry, (float)rz, origin)).ToArray());
                    }
                }

                AddSwitchBoxesIf(Facing.NorthEast, Facing.NorthAll, 90, 90, 0);
                AddSwitchBoxesIf(Facing.NorthWest, Facing.NorthAll, 90, 270, 0);
                AddSwitchBoxesIf(Facing.NorthUp, Facing.NorthAll, 90, 180, 0);
                AddSwitchBoxesIf(Facing.NorthDown, Facing.NorthAll, 90, 0, 0);

                AddSwitchBoxesIf(Facing.EastNorth, Facing.EastAll, 180, 0, 90);
                AddSwitchBoxesIf(Facing.EastSouth, Facing.EastAll, 0, 0, 90);
                AddSwitchBoxesIf(Facing.EastUp, Facing.EastAll, 270, 0, 90);
                AddSwitchBoxesIf(Facing.EastDown, Facing.EastAll, 90, 0, 90);

                AddSwitchBoxesIf(Facing.SouthEast, Facing.SouthAll, 90, 90, 180);
                AddSwitchBoxesIf(Facing.SouthWest, Facing.SouthAll, 90, 270, 180);
                AddSwitchBoxesIf(Facing.SouthUp, Facing.SouthAll, 90, 180, 180);
                AddSwitchBoxesIf(Facing.SouthDown, Facing.SouthAll, 90, 0, 180);

                AddSwitchBoxesIf(Facing.WestNorth, Facing.WestAll, 180, 0, 270);
                AddSwitchBoxesIf(Facing.WestSouth, Facing.WestAll, 0, 0, 270);
                AddSwitchBoxesIf(Facing.WestUp, Facing.WestAll, 270, 0, 270);
                AddSwitchBoxesIf(Facing.WestDown, Facing.WestAll, 90, 0, 270);

                AddSwitchBoxesIf(Facing.UpNorth, Facing.UpAll, 0, 180, 180);
                AddSwitchBoxesIf(Facing.UpEast, Facing.UpAll, 0, 90, 180);
                AddSwitchBoxesIf(Facing.UpSouth, Facing.UpAll, 0, 0, 180);
                AddSwitchBoxesIf(Facing.UpWest, Facing.UpAll, 0, 270, 180);

                AddSwitchBoxesIf(Facing.DownNorth, Facing.DownAll, 0, 180, 0);
                AddSwitchBoxesIf(Facing.DownEast, Facing.DownAll, 0, 90, 0);
                AddSwitchBoxesIf(Facing.DownSouth, Facing.DownAll, 0, 0, 0);
                AddSwitchBoxesIf(Facing.DownWest, Facing.DownAll, 0, 270, 0);
            }

            boxesCache[key] = boxes;
            return boxes;
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
