using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NormalPipe;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content
{
    public class BlockPipeBase : Vintagestory.API.Common.Block
    {
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel == null) return false;

            // Проверяем, есть ли в руке ключ для смены режима
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool hasWrench = activeSlot.Itemstack?.Collectible?.Code?.ToString()?.Contains("wrench") == true;

            // Если в руке ключ - меняем режим трубы
            if (hasWrench)
            {
                return TransformPipeType(world, blockSel.Position, byPlayer);
            }

            // Если это фильтрующая труба и нет ключа - передаем управление BlockEntity
            // (Он откроет GUI через OnPlayerRightClick)
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
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
        

        /// <summary>
        /// Переключает режим трубы между обычной, фильтрующей для предметов и фильтрующей для жидкостей
        /// </summary>
        protected virtual bool TransformPipeType(IWorldAccessor world, BlockPos pos, IPlayer player)
        {
            // Получаем текущий блок
            Vintagestory.API.Common.Block currentBlock = world.BlockAccessor.GetBlock(pos);
            string currentCode = currentBlock.Code.Path;
            
            // Для отладки
            world.Logger.Notification($"Текущий код блока: {currentCode}");

            // Парсим текущий код блока
            // Формат: electricalprogressivetransport:{базовый-тип}-{конфигурация}
            // Примеры: 
            // - electricalprogressivetransport:pipe-normal-straight-ns
            // - electricalprogressivetransport:pipe-item-insertion-corner-ne
            // - electricalprogressivetransport:pipe-liquid-insertion-tee-n
            
            string[] parts = currentCode.Split('-');
            if (parts.Length < 2)
            {
                world.Logger.Warning($"Некорректный формат кода трубы: {currentCode}");
                return false;
            }
            
            // Определяем базовый тип трубы и конфигурацию
            string baseType;
            string configuration;
            
            // Проверяем, есть ли в коде "item-insertion" или "liquid-insertion"
            string fullCode = string.Join("-", parts);
            
            if (fullCode.Contains("pipe-item-insertion"))
            {
                // Формат: pipe-item-insertion-{configuration}
                // Находим индекс начала конфигурации
                int configStartIndex = fullCode.IndexOf("pipe-item-insertion") + "pipe-item-insertion".Length + 1;
                
                if (configStartIndex > 0 && configStartIndex < fullCode.Length)
                {
                    baseType = "pipe-item-insertion";
                    configuration = fullCode.Substring(configStartIndex);
                }
                else
                {
                    baseType = "pipe-item-insertion";
                    configuration = "straight-ns"; // Дефолтная конфигурация
                }
            }
            else if (fullCode.Contains("pipe-liquid-insertion"))
            {
                // Формат: pipe-liquid-insertion-{configuration}
                int configStartIndex = fullCode.IndexOf("pipe-liquid-insertion") + "pipe-liquid-insertion".Length + 1;
                
                if (configStartIndex > 0 && configStartIndex < fullCode.Length)
                {
                    baseType = "pipe-liquid-insertion";
                    configuration = fullCode.Substring(configStartIndex);
                }
                else
                {
                    baseType = "pipe-liquid-insertion";
                    configuration = "straight-ns";
                }
            }
            else if (fullCode.Contains("pipe-normal"))
            {
                // Формат: pipe-normal-{configuration}
                int configStartIndex = fullCode.IndexOf("pipe-normal") + "pipe-normal".Length + 1;
                
                if (configStartIndex > 0 && configStartIndex < fullCode.Length)
                {
                    baseType = "pipe-normal";
                    configuration = fullCode.Substring(configStartIndex);
                }
                else
                {
                    baseType = "pipe-normal";
                    configuration = "straight-ns";
                }
            }
            else
            {
                world.Logger.Warning($"Неизвестный тип трубы: {currentCode}");
                return false;
            }
            
            world.Logger.Notification($"Базовый тип: {baseType}, Конфигурация: {configuration}");

            // Определяем следующий тип в цикле
            string nextBaseType = baseType switch
            {
                "pipe-normal" => "pipe-item-insertion",
                "pipe-item-insertion" => "pipe-liquid-insertion",
                "pipe-liquid-insertion" => "pipe-normal",
                _ => "pipe-normal"
            };
            
            world.Logger.Notification($"Следующий тип: {nextBaseType}");

            // Собираем новый код блока
            string newBlockCode = !string.IsNullOrEmpty(configuration) 
                ? $"{nextBaseType}-{configuration}"
                : nextBaseType;
            
            // Получаем новый блок
            Vintagestory.API.Common.Block newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{newBlockCode}"));
            
            // Если блок с конфигурацией не найден, пробуем найти с дефолтной конфигурацией
            if (newBlock == null && !string.IsNullOrEmpty(configuration))
            {
                world.Logger.Notification($"Блок {newBlockCode} не найден, пробуем дефолтную конфигурацию...");
                newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{nextBaseType}-straight-ns"));
                
                // Если и с дефолтной не найден, пробуем базовый
                if (newBlock == null)
                {
                    newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{nextBaseType}"));
                }
            }
            
            if (newBlock == null)
            {
                world.Logger.Warning($"Не удалось найти блок: electricalprogressivetransport:{newBlockCode}");
                return false;
            }
            
            world.Logger.Notification($"Новый блок найден: {newBlock.Code}");

            // Получаем текущую сущность и сохраняем её данные
            BlockEntity currentEntity = world.BlockAccessor.GetBlockEntity(pos);
            ITreeAttribute tree = null;
            
            if (currentEntity != null)
            {
                world.Logger.Notification($"Сохраняем данные текущей сущности...");
                
                tree = new TreeAttribute();
                currentEntity.ToTreeAttributes(tree);
                
                // Сохраняем данные соединений для всех типов труб
                if (currentEntity is BEPipe normalPipe)
                {
                    // Сохраняем соединения
                    var connectionsAttr = new TreeAttribute();
                    normalPipe.ToTreeAttributes(connectionsAttr);
                }
                else if (currentEntity is BEItemInsertionPipe itemPipe)
                {
                    // Для фильтрующих труб сохраняем дополнительные данные
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
            }

            // Меняем блок
            world.Logger.Notification($"Меняем блок {currentBlock.Code} на {newBlock.Code}...");
            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
            
            // Ждем немного, чтобы сущность успела инициализироваться
            world.Api.Event.EnqueueMainThreadTask(() =>
            {
                // Восстанавливаем данные в новую сущность
                if (tree != null)
                {
                    BlockEntity newEntity = world.BlockAccessor.GetBlockEntity(pos);
                    if (newEntity != null)
                    {
                        world.Logger.Notification($"Восстанавливаем данные в новую сущность типа {newEntity.GetType().Name}...");
                        
                        try
                        {
                            newEntity.FromTreeAttributes(tree, world);
                            newEntity.MarkDirty();
                            
                            // Устанавливаем дефолтные настройки при переходе между разными типами труб
                            if (newEntity is BEItemInsertionPipe newItemPipe)
                            {
                                if (currentEntity is BEPipe)
                                {
                                    // Переход из обычной в предметную фильтрующую
                                    world.Logger.Notification($"Устанавливаем дефолтные настройки для предметной трубы...");
                                    newItemPipe.UpdateFilterSettings(
                                        BEItemInsertionPipe.FilterMode.AllowList, 
                                        false, true, false);
                                }
                            }
                            else if (newEntity is BELiquidInsertionPipe newLiquidPipe)
                            {
                                if (currentEntity is BEPipe)
                                {
                                    // Переход из обычной в жидкостную фильтрующую
                                    world.Logger.Notification($"Устанавливаем дефолтные настройки для жидкостной трубы...");
                                    newLiquidPipe.UpdateFilterSettings(
                                        BELiquidInsertionPipe.FilterMode.AllowList);
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            world.Logger.Error($"Ошибка при восстановлении данных: {ex.Message}");
                        }
                    }
                }
                
                // Обновляем соединения с соседями
                UpdateNeighborConnections(world, pos);
                
                // Обновляем модель нового блока
                UpdateBlockModel(world, pos);
                
                // Проигрываем звук
                world.BlockAccessor.MarkBlockDirty(pos);
                world.PlaySoundAt(new AssetLocation("game:sounds/effect/tooluse"), pos.X, pos.Y, pos.Z, player);
            }, "transform-pipe");
            
            return true;
        }

        /// <summary>
        /// Обновляет соединения с соседними блоками
        /// </summary>
        private void UpdateNeighborConnections(IWorldAccessor world, BlockPos pos)
        {
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos neighborPos = pos.AddCopy(facing);
                
                Vintagestory.API.Common.Block neighborBlock = world.BlockAccessor.GetBlock(neighborPos);
                
                if (neighborBlock is BlockPipeBase)
                {
                    var neighborEntity = world.BlockAccessor.GetBlockEntity(neighborPos);
                    
                    if (neighborEntity is BEPipe normalPipe)
                    {
                        normalPipe.UpdateConnections();
                    }
                    else if (neighborEntity is BEItemInsertionPipe itemPipe)
                    {
                        itemPipe.UpdateConnections();
                    }
                    else if (neighborEntity is BELiquidInsertionPipe liquidPipe)
                    {
                        liquidPipe.UpdateConnections();
                    }
                }
            }
        }

        /// <summary>
        /// Обновляет модель блока
        /// </summary>
        private void UpdateBlockModel(IWorldAccessor world, BlockPos pos)
        {
            var entity = world.BlockAccessor.GetBlockEntity(pos);
            
            if (entity is BEPipe normalPipe)
            {
                normalPipe.UpdateBlockModel();
            }
            else if (entity is BEItemInsertionPipe itemPipe)
            {
                itemPipe.UpdateBlockModel();
            }
            else if (entity is BELiquidInsertionPipe liquidPipe)
            {
                liquidPipe.UpdateBlockModel();
            }
        }
       
        
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer)
        {
            base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
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

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            // Определяем режим трубы по коду
            string modeText;
            string code = Code.ToString();
            
            if (code.Contains("pipe-item-insertion"))
            {
                modeText = Lang.Get("electricalprogressivetransport:pipe-mode-item-filter");
            }
            else if (code.Contains("pipe-liquid-insertion"))
            {
                modeText = Lang.Get("electricalprogressivetransport:pipe-mode-liquid-filter");
            }
            else
            {
                modeText = Lang.Get("electricalprogressivetransport:pipe-mode-normal");
            }
            
            sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-mode", modeText));
            sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-switch-help"));
            
            // Добавляем специфичную информацию для каждого типа трубы
            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                if (be is BEPipe normalPipe)
                {
                    normalPipe.GetBlockInfo(forPlayer, sb);
                }
                else if (be is BEItemInsertionPipe itemPipe)
                {
                    itemPipe.GetBlockInfo(forPlayer, sb);
                }
                else if (be is BELiquidInsertionPipe liquidPipe)
                {
                    liquidPipe.GetBlockInfo(forPlayer, sb);
                }
            }
            
            return sb.ToString();
        }
    }
}