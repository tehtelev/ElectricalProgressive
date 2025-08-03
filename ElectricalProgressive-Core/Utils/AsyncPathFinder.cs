using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Utils
{
    public class AsyncPathFinder
    {
        private readonly ConcurrentQueue<PathRequest> requestQueue = new(); // потокобезопасная очередь запросов
        private volatile bool isRunning = true;                             // Флаг для управления остановкой
        private bool busy = false;                                          // Флаг для отслеживания загрузки очереди
        private readonly int maxConcurrentTasks;                            // Максимальное количество параллельных задач
        private Dictionary<BlockPos, NetworkPart> parts;                    // Словарь частей сети
        private int sizeOfQueue;


        /// <summary>
        /// Инициализирует новый экземпляр класса AsyncPathFinder.
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="maxConcurrentTasks"></param>
        public AsyncPathFinder(Dictionary<BlockPos, NetworkPart> parts, int maxConcurrentTasks)
        {
            this.parts = parts;
            this.maxConcurrentTasks = maxConcurrentTasks;
            this.sizeOfQueue = 500 * maxConcurrentTasks;

            // Запускаем задачи-потребители один раз при старте
            for (int i = 0; i < maxConcurrentTasks; i++)
            {
                Task.Factory.StartNew(() => ProcessRequests(), TaskCreationOptions.LongRunning)
                    .ConfigureAwait(false);                     // Используем LongRunning для выделенных потоков
                //Task.Run(() => ProcessRequests());
            }
        }


        /// <summary>
        /// Добавление запроса в очередь
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="network"></param>
        public void EnqueueRequest(BlockPos start, BlockPos end, Network network)
        {
            // если очередь пуста считай, то можно снова заполнять
            if (requestQueue.Count < 100)
                busy = false;

            // если очередь меньше sizeOfQueue и не занята, то добавляем запрос
            if (requestQueue.Count < sizeOfQueue && !busy)
            {
                requestQueue.Enqueue(new PathRequest(start, end, network));
            }
            else
            {
                busy = true;
            }
        }

        /// <summary>
        /// Обработка очереди запросов
        /// </summary>
        private void ProcessRequests()
        {
            var pathFinder = new PathFinder(); // Создаем новый экземпляр PathFinder для каждого потока

            // Цикл обработки запросов
            while (isRunning)
            {
                // Если очередь пуста, очищаем PathFinder и ждем
                if (requestQueue.Count == 0)
                {
                    pathFinder.Clear();
                    Thread.Sleep(100); // Если очередь пуста, ждем 100 мс
                }

                // Пытаемся извлечь запрос из очереди
                if (requestQueue.TryDequeue(out var request))
                {
                    try //при изменении сетей неизбежно будет исключение, поэтому обрабатываем его здесь, чтобы не крашить. Особенно это касается загрузки и выгрузки мира
                    {
                        var (path, facing, processed, usedConn) =
                            pathFinder.FindShortestPath(request.Start, request.End, request.Network, parts);


                        if (path != null)
                        {
                            // проверка на null, чтобы потом снова посчитать попробовать
                            // Глубокое копирование Start и End
                            BlockPos copiedStart = request.Start.Copy();
                            BlockPos copiedEnd = request.End.Copy();

                            // Глубокое копирование массива path
                            BlockPos[] copiedPath =  new BlockPos[path.Length];
                            for (int i = 0; i < path.Length; i++)
                            {
                                copiedPath[i] = path[i].Copy();
                            }


                            // Копирование массива facing
                            int[] copiedFacing = new int[facing.Length];
                            Array.Copy(facing, copiedFacing, facing.Length);


                            // Глубокое копирование двумерного массива processed
                            bool[][] copiedProcessed = new bool[processed.Length][];
                            for (int i = 0; i < processed.Length; i++)
                            {
                                copiedProcessed[i] = new bool[processed[i].Length];
                                Array.Copy(processed[i], copiedProcessed[i], processed[i].Length);

                            }


                            // Копирование массива usedConn
                            Facing[] copiedUsedConn = new Facing[usedConn.Length];
                            Array.Copy(usedConn, copiedUsedConn, usedConn.Length);


                            // Добавление скопированных данных в кэш
                            PathCacheManager.AddOrUpdate(
                                copiedStart,
                                copiedEnd,
                                request.Network.version,
                                copiedPath,
                                copiedFacing,
                                copiedProcessed,
                                copiedUsedConn);
                        }



                    }
                    catch
                    {
                        //api.Logger.Error($"Ошибка в асинхронном поиске пути от {request.Start} до {request.End}: {ex.Message}");
                    }


                }


            }
        }


        /// <summary>
        /// Метод для остановки обработки
        /// </summary>
        public void Stop()
        {
            isRunning = false;
            // Очистка очереди
            while (requestQueue.TryDequeue(out _)) { }
        }




    }

    /// <summary>
    /// Класс для представления запроса на поиск пути
    /// </summary>
    public class PathRequest
    {
        public BlockPos Start { get; }
        public BlockPos End { get; }
        public Network Network { get; }

        public PathRequest(BlockPos start, BlockPos end, Network network)
        {
            Start = start;
            End = end;
            Network = network;
        }
    }
}