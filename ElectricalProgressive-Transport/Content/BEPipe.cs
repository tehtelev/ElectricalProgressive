﻿using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ElectricalProgressiveTransport
{
    public class BEPipe : BlockEntity
    {
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected PipeNetworkManager networkManager;
        
        public bool[] ConnectedSides => connectedSides;
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            // Регистрируем трубу в сети
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);
            
            UpdateConnections();
        }
        
        public virtual void UpdateConnections()
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.Logger.Notification($"=== UpdateConnections для трубы на {Pos} ===");
            }
    
            // Сбрасываем все соединения
            for (int i = 0; i < 6; i++)
            {
                connectedSides[i] = false;
                connectedPipes[i] = null;
            }
    
            int connectionsFound = 0;
    
            // Проверяем все 6 сторон на наличие труб
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
        
                // Получаем блок соседа
                Block neighborBlock = Api?.World.BlockAccessor.GetBlock(checkPos);
        
                if (Api?.Side == EnumAppSide.Server)
                {
                    Api.Logger.Notification($"Проверяем сторону {facing.Code}: блок {neighborBlock?.Code}");
                }
        
                // ПРОВЕРЯЕМ: является ли соседний блок любой трубой
                if (IsPipeBlock(neighborBlock))
                {
                    connectionsFound++;
                    connectedSides[i] = true;
                    connectedPipes[i] = checkPos.Copy();
            
                    if (Api?.Side == EnumAppSide.Server)
                    {
                        Api.Logger.Notification($"Найдено соединение с {checkPos}");
                    }
            
                    // Обновляем соединение у соседа
                    UpdateNeighborConnection(checkPos, facing.Opposite);
                }
            }
            
    
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.Logger.Notification($"Всего соединений: {connectionsFound}");
            }
    
            // Обновляем модель после изменения соединений
            UpdateBlockModel();
    
            MarkDirty();
        }
        
        private bool IsPipeBlock(Block block)
        {
            if (block == null) return false;
    
            // Простая проверка по коду блока
            string code = block.Code?.ToString() ?? "";
            return code.Contains("pipe"); // Все блоки с "pipe" в названии
        }
        
        private void UpdateNeighborConnection(BlockPos neighborPos, BlockFacing fromDirection)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BEPipe neighborPipe)
            {
                neighborPipe.UpdateSingleConnection(fromDirection, Pos);
            }
            else if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BEInsertionPipe neighborInserter)
            {
                neighborInserter.UpdateSingleConnection(fromDirection, Pos);
            }
            else if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BELiquidInsertionPipe neighborLiquidInserter)
            {
                neighborLiquidInserter.UpdateSingleConnection(fromDirection, Pos);
            }
        }
        
        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos)
        {
            int index = side.Index;
            connectedSides[index] = true;
            connectedPipes[index] = fromPos.Copy();
            
            // Обновляем модель
            UpdateBlockModel();
            
            MarkDirty();
        }

        /// <summary>
        /// Автоматически выбирает и устанавливает правильную модель трубы
        /// </summary>
        public void UpdateBlockModel()
        {
            if (Api == null || Api.Side != EnumAppSide.Server) return;

            // Получаем список подключенных сторон
            List<BlockFacing> connectedFacings = new List<BlockFacing>();
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i])
                {
                    connectedFacings.Add(BlockFacing.ALLFACES[i]);
                }
            }

            // Определяем тип модели
            string pipeType = DeterminePipeType(connectedFacings);

            // Получаем текущий блок
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            if (currentBlock == null) return;

            // ПРЯМОЙ ПУТЬ: Создаем правильный код блока
            string newBlockCodeString = $"electricalprogressivetransport:pipe-normal-{pipeType}";
            AssetLocation newBlockCode = new AssetLocation(newBlockCodeString);

            Api.Logger.Notification($"Пытаемся получить блок: {newBlockCode}");

            Block newBlock = Api.World.GetBlock(newBlockCode);

            if (newBlock == null)
            {
                // Пробуем найти среди всех блоков
                Api.Logger.Error($"Блок не найден: {newBlockCode}");
                Api.Logger.Error($"Ищем альтернативы...");

                // Выводим все доступные блоки труб
                foreach (var block in Api.World.Blocks)
                {
                    if (block?.Code?.ToString()?.Contains("pipe-normal") == true)
                    {
                        Api.Logger.Notification($"Доступен: {block.Code}");
                    }
                }

                return;
            }

            if (newBlock.Id != currentBlock.Id)
            {
                // Сохраняем текущие данные
                ITreeAttribute tree = new TreeAttribute();
                this.ToTreeAttributes(tree);

                // Меняем блок
                Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);

                // Восстанавливаем данные
                BlockEntity newEntity = Api.World.BlockAccessor.GetBlockEntity(Pos);
                if (newEntity is BEPipe newPipe)
                {
                    newPipe.FromTreeAttributes(tree, Api.World);
                    newPipe.MarkDirty();
                }

                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                Api.Logger.Notification($"Блок изменен: {currentBlock.Code} -> {newBlock.Code}");
            }
        }



        /// <summary>
        /// Определяет тип трубы на основе соединений
        /// </summary>
        private string DeterminePipeType(List<BlockFacing> facings)
        {
            int count = facings.Count;
    
            if (count == 1)
            {
                // Одно соединение - прямая труба (конец трубы)
                return DetermineSingleConnectionType(facings[0]);
            }
            else if (count == 2)
            {
                return DetermineTwoConnectionType(facings[0], facings[1]);
            }
            else if (count == 3)
            {
                // Проверяем, является ли это Т-образной трубой или тройным углом
                if (IsTeeConnection(facings))
                {
                    return DetermineThreeConnectionType(facings);
                }
                else
                {
                    return DetermineTripleCornerType(facings);
                }
            }
    
            // 0, 4+ соединений → крестовина
            return "cross";
        }

        /// <summary>
        /// Определяет тип для одного соединения (конец трубы)
        /// </summary>
        private string DetermineSingleConnectionType(BlockFacing facing)
        {
            // Для одного соединения используем прямую трубу
            // Определяем ориентацию на основе направления соединения
            return facing.Axis switch
            {
                EnumAxis.X => "straight-ew",  // Восток или Запад
                EnumAxis.Z => "straight-ns",  // Север или Юг
                EnumAxis.Y => "straight-ud",  // Вверх или Вниз
                _ => "cross"
            };
        }
        
        /// <summary>
        /// Определяет тип для двух соединений
        /// </summary>
        private string DetermineTwoConnectionType(BlockFacing f1, BlockFacing f2)
        {
            // Сортируем для единообразия
            List<BlockFacing> sorted = new List<BlockFacing> { f1, f2 };
            sorted.Sort((a, b) => a.Index.CompareTo(b.Index));
            f1 = sorted[0];
            f2 = sorted[1];
    
            // Прямая труба (противоположные стороны)
            if (f1.Opposite == f2)
            {
                if (f1.Axis == EnumAxis.Z) return "straight-ns";  // Север-Юг
                if (f1.Axis == EnumAxis.X) return "straight-ew";  // Восток-Запад
                if (f1.Axis == EnumAxis.Y) return "straight-ud";  // Вверх-Вниз
            }
    
            // Угловая труба
            return (f1.Code, f2.Code) switch
            {
                ("north", "east") => "corner-ne",   // Север-Восток
                ("east", "south") => "corner-se",   // Восток-Юг
                ("south", "west") => "corner-sw",   // Юг-Запад
                ("north", "west") => "corner-nw",   // Север-Запад
        
                ("north", "up") => "corner-nu",     // Север-Вверх
                ("south", "up") => "corner-su",     // Юг-Вверх
                ("east", "up") => "corner-eu",      // Восток-Вверх
                ("west", "up") => "corner-wu",      // Запад-Вверх
        
                ("north", "down") => "corner-nd",   // Север-Вниз
                ("south", "down") => "corner-sd",   // Юг-Вниз
                ("east", "down") => "corner-ed",    // Восток-Вниз
                ("west", "down") => "corner-wd",    // Запад-Вниз
        
                _ => "cross"
            };
        }
        
        
        /// <summary>
        /// Определяет тип для трех соединений (Т-образная)
        /// </summary>
        private string DetermineThreeConnectionType(List<BlockFacing> facings)
        {
            // Определяем, какая сторона является "ножкой" Т
            foreach (BlockFacing facing in facings)
            {
                if (!facings.Contains(facing.Opposite))
                {
                    return facing.Code switch
                    {
                        "north" => "tee-s",  // Ножка на Север → Т смотрит на Юг
                        "east" => "tee-w",   // Ножка на Восток → Т смотрит на Запад
                        "south" => "tee-n",  // Ножка на Юг → Т смотрит на Север
                        "west" => "tee-e",   // Ножка на Запад → Т смотрит на Восток
                        "up" => "tee-d",     // Ножка вверх → Т смотрит вниз
                        "down" => "tee-u",   // Ножка вниз → Т смотрит вверх
                        _ => "cross"
                    };
                }
            }
            return "cross";
        }
        
        /// <summary>
        /// Определяет тип для тройного углового соединения (все три стороны не в одной плоскости)
        /// </summary>
        private string DetermineTripleCornerType(List<BlockFacing> facings)
        {
            // Сортируем для единообразия
            facings.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            // Преобразуем в массив кодов
            string[] codes = facings.Select(f => f.Code).ToArray();
            
            // Определяем тип на основе комбинации сторон
            if (codes.Contains("north") && codes.Contains("east") && codes.Contains("up"))
            {
                return "triple-neu";  // Север-Восток-Вверх
            }
            else if (codes.Contains("north") && codes.Contains("east") && codes.Contains("down"))
            {
                return "triple-ned";  // Север-Восток-Вниз
            }
            else if (codes.Contains("east") && codes.Contains("south") && codes.Contains("up"))
            {
                return "triple-seu";  // Восток-Юг-Вверх
            }
            else if (codes.Contains("east") && codes.Contains("south") && codes.Contains("down"))
            {
                return "triple-sed";  // Восток-Юг-Вниз
            }
            else if (codes.Contains("south") && codes.Contains("west") && codes.Contains("up"))
            {
                return "triple-swu";  // Юг-Запад-Вверх
            }
            else if (codes.Contains("south") && codes.Contains("west") && codes.Contains("down"))
            {
                return "triple-swd";  // Юг-Запад-Вниз
            }
            else if (codes.Contains("west") && codes.Contains("north") && codes.Contains("up"))
            {
                return "triple-nwu";  // Запад-Север-Вверх
            }
            else if (codes.Contains("west") && codes.Contains("north") && codes.Contains("down"))
            {
                return "triple-nwd";  // Запад-Север-Вниз
            }
            
            return "cross";  // Запасной вариант
        }
        
        /// <summary>
        /// Проверяет, является ли соединение Т-образным (все три стороны в одной плоскости)
        /// </summary>
        private bool IsTeeConnection(List<BlockFacing> facings)
        {
            // Для Т-образного соединения две стороны должны быть противоположными
            // (образуют прямую линию), а третья - перпендикулярна к ним
            
            foreach (BlockFacing facing in facings)
            {
                if (facings.Contains(facing.Opposite))
                {
                    return true;  // Нашли противоположные стороны - это Т-образное соединение
                }
            }
            
            return false;  // Нет противоположных сторон - это тройной угол
        }
        
        public void GetBlockInfo(StringBuilder sb)
        {
            int connections = 0;
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i]) connections++;
            }
            
            sb.AppendLine(Lang.Get("electricalprogressivetransport:connections", connections));
            
            if (networkManager != null)
            {
                var network = networkManager.GetNetwork(Pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:network-size", network.Pipes.Count));
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:inserters", network.Inserters.Count));
                }
            }
            
            // Показываем текущий тип модели
            Block block = Api?.World.BlockAccessor.GetBlock(Pos);
            if (block != null && block.Variant.ContainsKey("type"))
            {
                string type = block.Variant["type"];
                string typeName = GetPipeTypeName(type);
                sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-type", typeName));
            }
        }
        
        private string GetPipeTypeName(string typeCode)
        {
            return typeCode switch
            {
                "straight-ns" => Lang.Get("electricalprogressivetransport:pipe-type-straight-ns"),
                "straight-ew" => Lang.Get("electricalprogressivetransport:pipe-type-straight-ew"),
                "straight-ud" => Lang.Get("electricalprogressivetransport:pipe-type-straight-ud"),
                "corner-ne" => Lang.Get("electricalprogressivetransport:pipe-type-corner-ne"),
                "corner-se" => Lang.Get("electricalprogressivetransport:pipe-type-corner-se"),
                "corner-sw" => Lang.Get("electricalprogressivetransport:pipe-type-corner-sw"),
                "corner-nw" => Lang.Get("electricalprogressivetransport:pipe-type-corner-nw"),
                "corner-nu" => Lang.Get("electricalprogressivetransport:pipe-type-corner-nu"),
                "corner-su" => Lang.Get("electricalprogressivetransport:pipe-type-corner-su"),
                "corner-eu" => Lang.Get("electricalprogressivetransport:pipe-type-corner-eu"),
                "corner-wu" => Lang.Get("electricalprogressivetransport:pipe-type-corner-wu"),
                "corner-nd" => Lang.Get("electricalprogressivetransport:pipe-type-corner-nd"),
                "corner-sd" => Lang.Get("electricalprogressivetransport:pipe-type-corner-sd"),
                "corner-ed" => Lang.Get("electricalprogressivetransport:pipe-type-corner-ed"),
                "corner-wd" => Lang.Get("electricalprogressivetransport:pipe-type-corner-wd"),
                "tee-n" => Lang.Get("electricalprogressivetransport:pipe-type-tee-n"),
                "tee-e" => Lang.Get("electricalprogressivetransport:pipe-type-tee-e"),
                "tee-s" => Lang.Get("electricalprogressivetransport:pipe-type-tee-s"),
                "tee-w" => Lang.Get("electricalprogressivetransport:pipe-type-tee-w"),
                "tee-u" => Lang.Get("electricalprogressivetransport:pipe-type-tee-u"),
                "tee-d" => Lang.Get("electricalprogressivetransport:pipe-type-tee-d"),
                "triple-neu" => Lang.Get("electricalprogressivetransport:pipe-type-triple-neu"),
                "triple-ned" => Lang.Get("electricalprogressivetransport:pipe-type-triple-ned"),
                "triple-seu" => Lang.Get("electricalprogressivetransport:pipe-type-triple-seu"),
                "triple-sed" => Lang.Get("electricalprogressivetransport:pipe-type-triple-sed"),
                "triple-swu" => Lang.Get("electricalprogressivetransport:pipe-type-triple-swu"),
                "triple-swd" => Lang.Get("electricalprogressivetransport:pipe-type-triple-swd"),
                "triple-nwu" => Lang.Get("electricalprogressivetransport:pipe-type-triple-nwu"),
                "triple-nwd" => Lang.Get("electricalprogressivetransport:pipe-type-triple-nwd"),
                "cross" => Lang.Get("electricalprogressivetransport:pipe-type-cross"),
                _ => typeCode
            };
        }
        
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }
        
        public override void OnBlockRemoved()
        {
            // Разрываем соединения с соседями
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedPipes[i] != null)
                {
                    BreakNeighborConnection(connectedPipes[i]!, BlockFacing.ALLFACES[i]);
                }
            }
            
            // Удаляем трубу из сети
            networkManager?.RemovePipe(Pos);
            
            base.OnBlockRemoved();
        }
        
        private void BreakNeighborConnection(BlockPos neighborPos, BlockFacing direction)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BEPipe neighborPipe)
            {
                neighborPipe.BreakConnection(direction.Opposite);
            }
            else if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BEInsertionPipe neighborInserter)
            {
                neighborInserter.BreakConnection(direction.Opposite);
            }
            else if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BELiquidInsertionPipe neighborLiquidInserter)
            {
                neighborLiquidInserter.BreakConnection(direction.Opposite);
            }
        }
        
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            
            // Обновляем модель после разрыва соединения
            UpdateBlockModel();
            
            MarkDirty();
        }
        
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            
            byte[] connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedSides[i] = connBytes[i] == 1;
                }
            }
        }
        
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            
            byte[] connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                connBytes[i] = (byte)(connectedSides[i] ? 1 : 0);
            }
            tree.SetBytes("connections", connBytes);
        }
    }
}