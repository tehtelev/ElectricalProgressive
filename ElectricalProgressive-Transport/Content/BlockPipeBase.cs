using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content
{
    /// <summary>
    /// Базовый класс для всех типов труб в моде Electrical Progressive Transport
    /// Обрабатывает взаимодействие с блоком и переключение между типами труб
    /// </summary>
    public class BlockPipeBase : Vintagestory.API.Common.Block
    {
        private static readonly Dictionary<int, MeshData> PipeMeshCache = [];

        /// <summary>
        /// Вызывается при начале взаимодействия с блоком (правый клик)
        /// </summary>
        /// <param name="world">Доступ к миру</param>
        /// <param name="byPlayer">Игрок, взаимодействующий с блоком</param>
        /// <param name="blockSel">Выбор блока</param>
        /// <returns>true если взаимодействие обработано, false иначе</returns>
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            // Проверяем валидность выбора блока
            if (blockSel == null)
                return false;

            // Проверяем, есть ли в руке ключ для смены режима трубы
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool hasWrench = activeSlot.Itemstack?.Collectible?.Code?.ToString()?.Contains("wrench") == true;

            // Если в руке ключ - меняем режим трубы
            if (hasWrench)
            {
                return TransformPipeType(world, blockSel.Position, byPlayer);
            }

            // Если это фильтрующая труба и нет ключа - передаём управление BlockEntity
            // Он откроет GUI через OnPlayerRightClick
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);

            if (be != null)
            {
                if (be is BEItemInsertionPipe itempipe)
                {
                    itempipe.OnPlayerRightClick(byPlayer, blockSel);
                }

                if (be is BELiquidInsertionPipe liquidPipe)
                {
                    liquidPipe.OnPlayerRightClick(byPlayer, blockSel);
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }


        /// <summary>
        /// Переключает режим трубы между обычной, фильтрующей для предметов и фильтрующей для жидкостей
        /// </summary>
        /// <param name="world">Доступ к миру</param>
        /// <param name="pos">Позиция блока</param>
        /// <param name="player">Игрок, выполняющий действие</param>
        /// <returns>true если переключение успешно выполнено</returns>
        protected virtual bool TransformPipeType(IWorldAccessor world, BlockPos pos, IPlayer player)
        {
            // Получаем текущий блок
            var currentBlock = world.BlockAccessor.GetBlock(pos);
            string currentCode = currentBlock.Code.Path;

            // Парсим текущий код блока для извлечения типа и конфигурации
            string[] parts = currentCode.Split('-');

            if (parts.Length < 2)
            {
                return false;
            }

            // Определяем базовый тип трубы и конфигурацию
            string baseType;
            string configuration;

            // Объединяем части для более удобной проверки
            string fullCode = string.Join("-", parts);

            // Определяем тип трубы по ключевым словам в коде
            if (fullCode.Contains("pipe-item-insertion"))
            {
                baseType = "pipe-item-insertion";
                configuration = ExtractConfiguration(fullCode, baseType, "straight-ns");
            }
            else if (fullCode.Contains("pipe-liquid-insertion"))
            {
                baseType = "pipe-liquid-insertion";
                configuration = ExtractConfiguration(fullCode, baseType, "straight-ns");
            }
            else if (fullCode.Contains("pipe-normal"))
            {
                baseType = "pipe-normal";
                configuration = ExtractConfiguration(fullCode, baseType, "straight-ns");
            }
            else
            {
                return false;
            }

            // Определяем следующий тип в цикле переключения
            string nextBaseType = baseType switch
            {
                "pipe-normal" => "pipe-item-insertion",
                "pipe-item-insertion" => "pipe-liquid-insertion",
                "pipe-liquid-insertion" => "pipe-normal",
                _ => "pipe-normal"
            };

            // Собираем новый код блока с сохранением конфигурации
            string newBlockCode = !string.IsNullOrEmpty(configuration)
                ? $"{nextBaseType}-{configuration}"
                : nextBaseType;

            // Пытаемся получить новый блок по коду
            var newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{newBlockCode}"));

            // Если блок с конфигурацией не найден, пробуем найти с дефолтной конфигурацией
            if (newBlock == null && !string.IsNullOrEmpty(configuration))
            {
                newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{nextBaseType}-straight-ns"));

                // Если и с дефолтной не найден, пробуем базовый
                if (newBlock == null)
                {
                    newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{nextBaseType}"));
                }
            }

            if (newBlock == null)
            {
                return false;
            }

            // Сохраняем данные текущей сущности перед заменой блока
            ITreeAttribute tree = SaveEntityData(world, pos);

            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
            // Запускаем асинхронное восстановление данных на главном потоке
            world.Api.Event.EnqueueMainThreadTask(() =>
            {
                // Сначала восстанавливаем данные
                if (tree != null)
                {
                    BlockEntity newEntity = world.BlockAccessor.GetBlockEntity(pos);
                    if (newEntity != null)
                    {
                        newEntity.FromTreeAttributes(tree, world);
                        newEntity.MarkDirty();
                    }
                }

                UpdatePipeConnections(world, pos, false);
                UpdateNeighborConnections(world, pos);

                // Проигрываем звук успешного действия
                world.BlockAccessor.MarkBlockDirty(pos);
                world.PlaySoundAt(new AssetLocation("game:sounds/effect/tooluse"),
                    pos.X, pos.Y, pos.Z, player);
            }, "transform-pipe");

            return true;
        }

        /// <summary>
        /// Извлекает конфигурацию из кода блока трубы
        /// </summary>
        /// <param name="fullCode">Полный код блока</param>
        /// <param name="baseType">Базовый тип трубы</param>
        /// <param name="defaultConfiguration">Конфигурация по умолчанию</param>
        /// <returns>Строка конфигурации или дефолтная при ошибке</returns>
        private string ExtractConfiguration(string fullCode, string baseType, string defaultConfiguration)
        {
            int configStartIndex = fullCode.IndexOf(baseType) + baseType.Length + 1;

            if (configStartIndex > 0 && configStartIndex < fullCode.Length)
            {
                return fullCode.Substring(configStartIndex);
            }

            return defaultConfiguration;
        }

        /// <summary>
        /// Сохраняет данные сущности трубы перед изменением типа
        /// </summary>
        private ITreeAttribute SaveEntityData(IWorldAccessor world, BlockPos pos)
        {
            var currentEntity = world.BlockAccessor.GetBlockEntity(pos);

            if (currentEntity == null)
                return null;

            var tree = new TreeAttribute();
            currentEntity.ToTreeAttributes(tree);

            // Сохраняем специфичные для типа трубы данные
            if (currentEntity is BEItemInsertionPipe itemPipe)
            {
                tree.SetInt("transferRate", itemPipe.TransferRate);
                tree.SetInt("filterMode", (int)itemPipe.CurrentFilterMode);
                tree.SetBool("matchMod", itemPipe.MatchMod);
                tree.SetBool("matchType", itemPipe.MatchType);
                tree.SetBool("matchAttributes", itemPipe.MatchAttributes);
            }
            else if (currentEntity is BELiquidInsertionPipe liquidPipe)
            {
                tree.SetInt("transferRate", liquidPipe.TransferRate);
                tree.SetInt("filterMode", (int)liquidPipe.CurrentFilterMode);
            }

            return tree;
        }

        /// <summary>
        /// Применяет настройки по умолчанию при переходе между типами труб
        /// </summary>
        private void ApplyDefaultSettings(BlockEntity newEntity, ITreeAttribute savedData, IWorldAccessor world)
        {
            if (newEntity is BEItemInsertionPipe newItemPipe && savedData != null)
            {
                // Переход из обычной в предметную фильтрующую трубу
                newItemPipe.UpdateFilterSettings(
                    BEItemInsertionPipe.FilterMode.AllowList,
                    false, true, false);
            }
            else if (newEntity is BELiquidInsertionPipe newLiquidPipe && savedData != null)
            {
                // Переход из обычной в жидкостную фильтрующую трубу
                newLiquidPipe.UpdateFilterSettings(
                    BELiquidInsertionPipe.FilterMode.AllowList);
            }
        }



        /// <summary>
        /// Обновляет соединения и модели всех труб-соседей при изменении блока
        /// </summary>
        private void UpdateNeighborConnections(IWorldAccessor world, BlockPos pos)
        {
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos neighborPos = pos.AddCopy(facing);

                var neighborBlock = world.BlockAccessor.GetBlock(neighborPos);

                if (neighborBlock is BlockPipeBase)
                {
                    UpdatePipeConnections(world, neighborPos, true);
                }
            }
        }

        private static void UpdatePipeConnections(IWorldAccessor world, BlockPos pos, bool updateNeighbors)
        {
            var entity = world.BlockAccessor.GetBlockEntity(pos);

            if (entity is BEPipe normalPipe)
                normalPipe.UpdateConnections(updateNeighbors);
            else if (entity is BlockEntityPipeBase pipe)
                pipe.UpdateConnections(updateNeighbors);
        }


        /// <summary>
        /// Возвращает подсказку для взаимодействия с блоком в интерфейсе
        /// </summary>
        /// <param name="world">Доступ к миру</param>
        /// <param name="selection">Выбор блока</param>
        /// <param name="forPlayer">Игрок, которому показывается подсказка</param>
        /// <returns>Массив описаний взаимодействия</returns>
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer)
        {
            // Собираем все предметы типа "ключ" в мире
            var wrenchStacks = new List<ItemStack>();

            foreach (var obj in world.Collectibles)
            {
                if (obj.FirstCodePart() == "wrench")
                {
                    var stacks = obj.GetHandBookStacks(api as ICoreClientAPI);

                    if (stacks != null)
                    {
                        wrenchStacks.AddRange(stacks);
                    }
                }
            }

            // Возвращаем описание взаимодействия с трубой
            return new WorldInteraction[1]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "electricalprogressivetransport:blockhelp-pipe-switch-type",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = wrenchStacks.Count > 0 ? wrenchStacks.ToArray() : null
                }
            }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }



        /// <summary>
        /// Вызывается при изменении любого из соседних блоков
        /// </summary>
        /// <param name="world">Доступ к миру</param>
        /// <param name="pos">Позиция текущего блока</param>
        /// <param name="neibpos">Позиция изменившегося соседа</param>
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            var entity = world.BlockAccessor.GetBlockEntity(pos);

            // Если сущности нет, то обновлять соединения не нужно
            if (entity == null)
                return;

            // Обновляем соединения текущей трубы для актуализации модели
            if (entity is BEPipe pipe)
                pipe.UpdateConnections(false);
            else if (entity is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(false);
        }

        public override void OnJsonTesselation(
            ref MeshData sourceMesh,
            ref int[] lightRgbsByCorner,
            BlockPos pos,
            Vintagestory.API.Common.Block[] chunkExtBlocks,
            int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

            if (api is not ICoreClientAPI capi)
                return;

            var blockEntity = capi.World.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity is not IPipeRenderState renderState)
            {
                return;
            }

            string pipeType = renderState.CurrentPipeType;
            string baseBlockCode = renderState.GetBaseBlockCode();

            if (string.IsNullOrEmpty(pipeType) || string.IsNullOrEmpty(baseBlockCode))
                return;

            var renderBlockCode = baseBlockCode.Contains(':')
                ? new AssetLocation($"{baseBlockCode}-{pipeType}")
                : new AssetLocation(Code.Domain, $"{baseBlockCode}-{pipeType}");

            var renderBlock = capi.World.GetBlock(renderBlockCode);
            if (renderBlock == null)
                return;

            if (!PipeMeshCache.TryGetValue(renderBlock.Id, out var meshData))
            {
                meshData = capi.TesselatorManager.GetDefaultBlockMesh(renderBlock);
                if (meshData == null)
                {
                    var cachedShape = capi.TesselatorManager.GetCachedShape(renderBlock.Shape.Base);
                    capi.Tesselator.TesselateShape(renderBlock, cachedShape, out meshData);
                    capi.TesselatorManager.ThreadDispose();
                }

                PipeMeshCache[renderBlock.Id] = meshData;
            }

            sourceMesh = meshData;
        }

    }
}
