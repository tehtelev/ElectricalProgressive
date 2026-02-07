using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Utils
{
    public class AsyncPathFinder
    {
        private readonly ConcurrentQueue<PathRequest> _requestQueue = new(); // потокобезопасная очередь запросов
        private volatile bool _isRunning = true;                             // Флаг для управления остановкой
        private bool _busy = false;                                          // Флаг для отслеживания загрузки очереди
        private readonly int _maxConcurrentTasks;                            // Максимальное количество параллельных задач
        private readonly Dictionary<BlockPos, NetworkPart> _parts;           // Словарь частей сети
        private readonly int _sizeOfQueue;
        private readonly int _sizeOfNotBusy;

        /// <summary>
        /// Инициализирует новый экземпляр класса AsyncPathFinder.
        /// </summary>
        /// <param name="parts"></param>
        /// <param name="maxConcurrentTasks"></param>
        public AsyncPathFinder(Dictionary<BlockPos, NetworkPart> parts, int maxConcurrentTasks)
        {
            this._parts = parts;
            this._maxConcurrentTasks = maxConcurrentTasks;
            this._sizeOfQueue = 1000 * maxConcurrentTasks;
            this._sizeOfNotBusy = 200 * maxConcurrentTasks;


            // Запускаем задачи-потребители один раз при старте
            for (var i = 0; i < maxConcurrentTasks; i++)
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
            if (_requestQueue.Count < _sizeOfNotBusy)
                _busy = false;

            // если очередь меньше sizeOfQueue и не занята, то добавляем запрос
            if (_requestQueue.Count < _sizeOfQueue && !_busy)
            {
                _requestQueue.Enqueue(new PathRequest(start, end, network));
            }
            else
            {
                _busy = true;
            }
        }

        /// <summary>
        /// Обработка очереди запросов
        /// </summary>
        private void ProcessRequests()
        {
            var pathFinder = new PathFinder(); // Создаем новый экземпляр PathFinder для каждого потока

            // Цикл обработки запросов
            while (_isRunning)
            {
                // Если очередь пуста, очищаем PathFinder и ждем
                if (_requestQueue.IsEmpty)
                {
                    pathFinder.Clear();
                    Thread.Sleep(50); // Если очередь пуста, ждем 100 мс
                }

                // Пытаемся извлечь запрос из очереди
                if (_requestQueue.TryDequeue(out var request))
                {
                    try //при изменении сетей неизбежно будет исключение, поэтому обрабатываем его здесь, чтобы не крашить. Особенно это касается загрузки и выгрузки мира
                    {
                        var (path, facing, processed, usedConn) =
                            pathFinder.FindShortestPath(request.Start, request.End, request.Network, _parts);


                        if (path != null)
                        {
                            // проверка на null, чтобы потом снова посчитать попробовать
                            // Глубокое копирование Start и End
                            var copiedStart = request.Start.Copy();
                            var copiedEnd = request.End.Copy();
                            var voltage = _parts[copiedEnd].eparams[facing.Last()].voltage;

                            // Добавление скопированных данных в кэш
                            PathCacheManager.AddOrUpdate(
                                copiedStart,
                                copiedEnd,
                                request.Network.version,
                                path,
                                facing,
                                processed,
                                usedConn,
                                voltage);
                        }



                    }
                    catch
                    {
                        //sapi.Logger.Error($"Ошибка в асинхронном поиске пути от {request.Start} до {request.End}: {ex.Message}");
                    }


                }


            }
        }


        /// <summary>
        /// Метод для остановки обработки
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            // Очистка очереди
            while (_requestQueue.TryDequeue(out _)) { }
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