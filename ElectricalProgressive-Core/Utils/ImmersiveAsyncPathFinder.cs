using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public class ImmersiveAsyncPathFinder
    {
        private readonly ConcurrentQueue<PathRequestImmersive> _requestQueue = new();
        private volatile bool _isRunning = true;
        private bool _busy = false;
        private readonly int _maxConcurrentTasks;
        private readonly Dictionary<BlockPos, ImmersiveNetworkPart> _parts;
        private readonly int _sizeOfQueue;
        private readonly int _sizeOfNotBusy;

        public ImmersiveAsyncPathFinder(Dictionary<BlockPos, ImmersiveNetworkPart> parts, int maxConcurrentTasks)
        {
            _parts = parts;
            _maxConcurrentTasks = maxConcurrentTasks;
            _sizeOfQueue = 1000 * maxConcurrentTasks;
            _sizeOfNotBusy = 200 * maxConcurrentTasks;

            for (var i = 0; i < maxConcurrentTasks; i++)
            {
                Task.Factory.StartNew(() => ProcessRequests(), TaskCreationOptions.LongRunning)
                    .ConfigureAwait(false);
            }
        }

        public void EnqueueRequest(BlockPos start, BlockPos end, ImmersiveNetwork immersiveNetwork)
        {
            if (_requestQueue.Count < _sizeOfNotBusy)
                _busy = false;

            if (_requestQueue.Count < _sizeOfQueue && !_busy)
            {
                _requestQueue.Enqueue(new PathRequestImmersive(start, end, immersiveNetwork));
            }
            else
            {
                _busy = true;
            }
        }

        private void ProcessRequests()
        {
            var pathFinder = new ImmersivePathFinder();

            while (_isRunning)
            {
                if (_requestQueue.IsEmpty)
                {
                    pathFinder.Clear();
                    Thread.Sleep(50);
                }

                if (_requestQueue.TryDequeue(out var request))
                {
                    try
                    {
                        var (path, nodeIndices, pathLength) = pathFinder.FindShortestPath(
                            request.Start, request.End, request.ImmersiveNetwork, _parts);

                        if (path != null)
                        {
                            var copiedStart = request.Start.Copy();
                            var copiedEnd = request.End.Copy();

                            // Получаем напряжение из параметров соединения конечного узла
                            var endPart = _parts[copiedEnd];
                            var voltage = 0;
                            if (nodeIndices.Length > 0)
                            {
                                voltage = endPart.MainEparams.voltage;
                            }

                            ImmersivePathCacheManager.AddOrUpdate(
                                copiedStart,
                                copiedEnd,
                                request.ImmersiveNetwork.version,
                                path,
                                nodeIndices,
                                pathLength,
                                voltage);
                        }
                        else
                        {
                            /*
                            ImmersivePathCacheManager.AddOrUpdate(
                                request.Start.Copy(),
                                request.End.Copy(),
                                request.ImmersiveNetwork.version,
                                path,
                                nodeIndices,
                                0);
                            */
                        }
                    }
                    catch
                    {
                        // Логирование ошибки
                    }
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            while (_requestQueue.TryDequeue(out _)) { }
        }
    }


}