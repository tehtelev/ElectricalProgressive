using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Utils;

public struct FastPosKeyByteComparer : IEqualityComparer<(FastPosKey key, byte face)>
{
    public bool Equals((FastPosKey key, byte face) x, (FastPosKey key, byte face) y)
    {
        return x.key.Equals(y.key) && x.face == y.face;
    }

    public int GetHashCode((FastPosKey key, byte face) obj)
    {
        unchecked
        {
            var hash = obj.key.GetHashCode();
            hash = (hash << 5) ^ (hash >> 27) ^ obj.face;
            return hash;
        }
    }
}






// Основной класс для поиска пути в сетях
public class PathFinder
{
    // Маски направлений для соединений (север, восток, юг, запад, верх, низ)
    private static readonly Facing[] faceMasks =
    [
        Facing.NorthAll, Facing.EastAll, Facing.SouthAll,
        Facing.WestAll, Facing.UpAll, Facing.DownAll
    ];

    // Переиспользуемые коллекции и буферы для уменьшения нагрузки на GC
    private List<FastPosKey> _neighborsFast = new(27); // Соседние позиции
    private List<byte> _neighborsFace = new(27); // Соответствующие направления
    private bool[] _nowProcessed = new bool[6]; // Флаги обработки направлений
    private Queue<byte> _queue2 = new(); // Очередь для BFS
    private bool[] _processFacesBuf = new bool[6]; // Буфер флагов обработки
    private List<BlockFacing> _bufForDirections = new(6); // Буфер направлений
    private List<BlockFacing> _bufForFaces = new(6); // Буфер граней
    private FastPosKey[] _pathBuffer = new FastPosKey[ElectricalProgressive.maxDistanceForFinding+1];
    private byte[] _faceBuffer = new byte[ElectricalProgressive.maxDistanceForFinding+1];


    private List<byte> _startBlockFacing = []; // Стартовые направления
    private List<byte> _endBlockFacing = []; // Конечные направления
    private PriorityQueue<(FastPosKey, byte), int> _queue = new(); // Приоритетная очередь для A*
    private Dictionary<(FastPosKey, byte), (FastPosKey, byte)> _cameFrom = new(new FastPosKeyByteComparer()); // Для восстановления пути
    private Dictionary<FastPosKey, bool[]> _processedFaces = new(); // Обработанные направления для позиций
    private Dictionary<(FastPosKey, byte), byte> _facingFrom = new(new FastPosKeyByteComparer()); // Направления прихода
    public Dictionary<(FastPosKey, byte), bool[]> nowProcessedFaces = new(new FastPosKeyByteComparer()); // Текущие обработанные направления
    private HashSet<BlockPos> _networkPositions = []; // Позиции в сети
    private List<FastPosKey> _buf1 = []; // Временный буфер
    private List<byte> _buf2 = []; // Временный буфер
    private bool[]? _buf3; // Временный буфер
    private bool[]? _buf4; // Временный буфер

    // lookupPos для поиска в parts без создания новых объектов
    private static BlockPos _lookupPos = new(0, 0, 0, 0);
    // defaultKey для сравнений с default
    private static FastPosKey _defaultKey = new(0, 0, 0, 0);

    // Получение NetworkPart по FastPosKey с переиспользованием lookupPos
    private static bool TryGetPart(Dictionary<BlockPos, NetworkPart> parts, FastPosKey key, out NetworkPart part)
    {
        _lookupPos.X = key.X;
        _lookupPos.Y = key.Y;
        _lookupPos.Z = key.Z;
        _lookupPos.dimension = key.Dim;
        return parts.TryGetValue(_lookupPos, out part);
    }


    public void Clear()
    {
        nowProcessedFaces?.Clear();
        _processedFaces?.Clear(); // Очистка состояния
    }

    // Эвристика для A* (манхэттенское расстояние)
    public static int Heuristic(BlockPos a, BlockPos b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    /// <summary>
    /// Основной поиск пути
    /// </summary>
    public (BlockPos[], byte[], bool[][], Facing[]) FindShortestPath(
        BlockPos start, BlockPos end, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        // Инициализация коллекций
        _startBlockFacing?.Clear();
        _endBlockFacing?.Clear();
        _queue?.Clear();
        _cameFrom?.Clear();
        _facingFrom?.Clear();
        _buf1?.Clear();
        _buf2?.Clear();
        _buf3 = [];
        _buf4 = [];

        _networkPositions = network.PartPositions;

        // стартовые и конечные значения
        var startKey = new FastPosKey(start.X, start.Y, start.Z, start.dimension, start);
        var endKey = new FastPosKey(end.X, end.Y, end.Z, end.dimension, end);

        // Проверка на валидность старта и конца
        if (!_networkPositions.Contains(start) ||
            !_networkPositions.Contains(end) ||
            start.Equals(end))
            return (null!, null!, null!, null!);

        // Заполнение стартовых и конечных направлений
        foreach (var face in FacingHelper.Faces(parts[start].Connection))
            _startBlockFacing.Add((byte)face.Index);
        foreach (var face in FacingHelper.Faces(parts[end].Connection))
            _endBlockFacing.Add((byte)face.Index);

        // Добавление стартовых точек в очередь
        foreach (var sFace in _startBlockFacing)
        {
            _queue.Enqueue((startKey, sFace), 0);
            _cameFrom[(startKey, sFace)] = (_defaultKey, 0);
            _facingFrom[(startKey, sFace)] = sFace;

            if (!nowProcessedFaces.TryGetValue((startKey, sFace), out var val))
            {
                var buffer = new bool[6];
                buffer[sFace] = true;
                nowProcessedFaces.Add((startKey, sFace), buffer);
            }
            else
            {
                Array.Fill(val, false);
                val[sFace] = true;
            }
        }

        // Инициализация processedFaces для всех позиций сети
        foreach (var pos in _networkPositions)
        {
            if (!_processedFaces.TryGetValue(new FastPosKey(pos.X, pos.Y, pos.Z, pos.dimension), out var val))
                _processedFaces.Add(new FastPosKey(pos.X, pos.Y, pos.Z, pos.dimension), new bool[6]);
            else Array.Fill(val, false);
        }

        var currentKey = _defaultKey;
        byte currentFace = 0;

        // Основной цикл A*
        while (_queue.Count > 0)
        {
            (currentKey, currentFace) = _queue.Dequeue();
            if (currentKey.Equals(endKey)) // Путь найден
                break;

            // Получение соседей
            (_buf1, _buf2, _buf3, _buf4) = GetNeighbors(currentKey, _processedFaces[currentKey], _facingFrom[(currentKey, currentFace)], network, parts);

            _processedFaces[currentKey] = _buf4; // Обновление обработанных направлений

            var i = 0;
            foreach (var neighbor in _buf1)
            {
                var state = (neighbor, _buf2[i]);
                var priority = Math.Abs(neighbor.X - end.X) +
                               Math.Abs(neighbor.Y - end.Y) +
                               Math.Abs(neighbor.Z - end.Z);

                // Добавление в очередь, если соответствует условиям
                if (priority < ElectricalProgressive.maxDistanceForFinding &&
                    !_cameFrom.ContainsKey(state) &&
                    !_processedFaces[neighbor][_buf2[i]])
                {
                    _queue.Enqueue(state, priority);
                    _cameFrom[state] = (currentKey, _facingFrom[(currentKey, currentFace)]);
                    _facingFrom[state] = _buf2[i];

                    // Копирование массива флагов
                    if (!nowProcessedFaces.TryGetValue(state, out var val))
                    {
                        var buf3copy= new bool[6];
                        _buf3.CopyTo(buf3copy, 0);
                        nowProcessedFaces.Add(state, buf3copy);
                    }
                    else
                    {
                        val[0]=_buf3[0]? true : false;
                        val[1]=_buf3[1]? true : false;
                        val[2]=_buf3[2]? true : false;
                        val[3]=_buf3[3]? true : false;
                        val[4]=_buf3[4]? true : false;
                        val[5]=_buf3[5]? true : false;
                    }
                }
                i++;
            }
        }

        // Восстановление пути
        var (fastPath, faces, pathLength) = ReconstructFastPath(startKey, endKey, _endBlockFacing, _cameFrom);
        if (fastPath == null)
            return (null!, null!, null!, null!);

        // Конвертация FastPosKey[] в BlockPos[] для пути
        var path = new BlockPos[pathLength];
        for (var i = 0; i < pathLength; i++)
        {
            var key = fastPath[i];
            
            if (key.Pos!=null)
                path[i] = key.Pos; // Используем существующий BlockPos
            else
                return (null!, null!, null!, null!);
        }

        // Построение дополнительных данных о пути
        var len = path.Length;
        var nowProcessingFaces = new Facing[len];
        var nowProcessedFacesList = new bool[len][];
        var facingFromList = new byte[len];

        facingFromList[0] = _facingFrom[(fastPath[0], faces[0])];

        for (var i = 1; i < len; i++)
        {
            facingFromList[i] = _facingFrom[(fastPath[i], faces[i])];
            var npf = nowProcessedFaces[(fastPath[i], faces[i])];
            nowProcessedFacesList[i - 1] = npf;

            // Вычисление активных направлений
            var facing = parts[path[i - 1]].Connection &
                ((npf[0] ? Facing.NorthAll : Facing.None) |
                 (npf[1] ? Facing.EastAll : Facing.None) |
                 (npf[2] ? Facing.SouthAll : Facing.None) |
                 (npf[3] ? Facing.WestAll : Facing.None) |
                 (npf[4] ? Facing.UpAll : Facing.None) |
                 (npf[5] ? Facing.DownAll : Facing.None));

            nowProcessingFaces[i - 1] = facing;
        }

        // Обработка последней позиции
        var lastNpf = new bool[6];
        lastNpf[_endBlockFacing[0]] = true;
        nowProcessedFacesList[len - 1] = lastNpf;
        nowProcessingFaces[len - 1] = parts[path[len - 1]].Connection &
            ((lastNpf[0] ? Facing.NorthAll : Facing.None) |
             (lastNpf[1] ? Facing.EastAll : Facing.None) |
             (lastNpf[2] ? Facing.SouthAll : Facing.None) |
             (lastNpf[3] ? Facing.WestAll : Facing.None) |
             (lastNpf[4] ? Facing.UpAll : Facing.None) |
             (lastNpf[5] ? Facing.DownAll : Facing.None));

        return (path, facingFromList, nowProcessedFacesList, nowProcessingFaces);
    }


    /// <summary>
    /// Считает, со сколькими соседями по сети network соединён данный part.
    /// Учитываются соединения через грани, рёбра и перпендикулярные грани (как в GetNeighbors).
    /// </summary>
    /// <param name="part">Часть, для которой считаем соседей.</param>
    /// <param name="network">Сеть, к которой относится часть.</param>
    /// <param name="parts">Словарь всех частей (BlockPos -> NetworkPart).</param>
    /// <returns>Количество уникальных позиций соседей в той же сети.</returns>
    public static int CountConnectedNeighbors(NetworkPart part, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        if (part == null || network == null || parts == null)
            return 0;

        var pos = part.Position;
        var connections = part.Connection;

        // 1. Вычисляем hereConnections – те направления, которые принадлежат сети network
        Facing hereConnections = Facing.None;
        for (byte i = 0; i < 6; i++)
        {
            if (part.Networks[i] == network)
                hereConnections |= connections & faceMasks[i];
        }

        if (hereConnections == Facing.None)
            return 0;

        // 2. BFS для определения всех граней, достижимых через внутренние соединения
        bool[] processFacesBuf = new bool[6];
        Queue<byte> queue = new Queue<byte>();

        for (byte i = 0; i < 6; i++)
        {
            if (part.Networks[i] == network && (connections & faceMasks[i]) != 0)
            {
                processFacesBuf[i] = true;
                queue.Enqueue(i);
            }
        }

        List<BlockFacing> _bufForDirections = new(6); // Буфер направлений
        List<BlockFacing> _bufForFaces = new(6); // Буфер граней

        while (queue.Count > 0)
        {
            int faceIndex = queue.Dequeue();
            var face = FacingHelper.BlockFacingFromIndex(faceIndex);
            var mask = FacingHelper.FromFace(face);
            var allowed = hereConnections & mask;

            FacingHelper.FillDirections(allowed, _bufForDirections);
            foreach (var dir in _bufForDirections)
            {
                byte idx = (byte)dir.Index;
                if (!processFacesBuf[idx] && (hereConnections & FacingHelper.From(dir, face)) != 0)
                {
                    processFacesBuf[idx] = true;
                    queue.Enqueue(idx);
                }
            }
        }

        // 3. Маска действительно активных направлений
        Facing validMask = Facing.None;
        for (byte i = 0; i < 6; i++)
        {
            if (processFacesBuf[i])
                validMask |= FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
        }
        hereConnections &= validMask;

        // 4. Поиск всех соседних позиций
        HashSet<BlockPos> neighborSet = new HashSet<BlockPos>();
        int px = pos.X, py = pos.Y, pz = pos.Z, dim = pos.dimension;

        FacingHelper.FillDirections(hereConnections, _bufForDirections);
        foreach (var dir in _bufForDirections)
        {
            // --- соседи по граням ---
            var dv = dir.Normali;
            int nx = px + dv.X, ny = py + dv.Y, nz = pz + dv.Z;
            var neighborKey = new FastPosKey(nx, ny, nz, dim);
            if (TryGetPart(parts, neighborKey, out var neighborPart))
            {
                FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
                foreach (var face in _bufForFaces)
                {
                    var opp = dir.Opposite;
                    if ((neighborPart.Connection & FacingHelper.From(face, opp)) != 0 ||
                        (neighborPart.Connection & FacingHelper.From(opp, face)) != 0)
                    {
                        neighborSet.Add(neighborPart.Position);
                    }
                }
            }

            // --- соседи по рёбрам ---
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
            foreach (var face in _bufForFaces)
            {
                dv = dir.Normali;
                var fv = face.Normali;
                nx = px + dv.X + fv.X;
                ny = py + dv.Y + fv.Y;
                nz = pz + dv.Z + fv.Z;
                neighborKey = new FastPosKey(nx, ny, nz, dim);
                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppDir = dir.Opposite;
                    var oppFace = face.Opposite;
                    if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0 ||
                        (neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                    {
                        neighborSet.Add(neighborPart.Position);
                    }
                }
            }

            // --- соседи по перпендикулярной грани ---
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
            foreach (var face in _bufForFaces)
            {
                var fv = face.Normali;
                nx = px + fv.X;
                ny = py + fv.Y;
                nz = pz + fv.Z;
                neighborKey = new FastPosKey(nx, ny, nz, dim);
                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppFace = face.Opposite;
                    if ((neighborPart.Connection & FacingHelper.From(dir, oppFace)) != 0 ||
                        (neighborPart.Connection & FacingHelper.From(oppFace, dir)) != 0)
                    {
                        neighborSet.Add(neighborPart.Position);
                    }
                }
            }
        }

        return neighborSet.Count;
    }



    // Восстановление пути от конца к началу
    private (FastPosKey[]?, byte[]?, int) ReconstructFastPath(
    FastPosKey start, FastPosKey end, List<byte> endFacing,
    Dictionary<(FastPosKey, byte), (FastPosKey, byte)> cameFrom)
{
    var length = 0;
    var current = (end, endFacing[0]);
    var foundEndFace = endFacing[0];
    var valid = false;

    // Подсчет длины пути и поиск валидного конца
    while (!current.end.Equals(_defaultKey))
    {
        length++;
        if (current.end.Equals(end))
        {
            valid = false;
            foreach (var eFace in endFacing)
            {
                var test = (end, eFace);
                if (cameFrom.TryGetValue(test, out current))
                {
                    foundEndFace = eFace;
                    valid = true;
                    break;
                }
            }
            if (!valid) return (null, null, 0);
        }
        else if (!cameFrom.TryGetValue(current, out current))
            return (null, null, 0);
    }

    // Построение массива пути с найденным направлением
    var pathArray = _pathBuffer;
    var faceArray = _faceBuffer;
    current = (end, foundEndFace);

    for (var i = length - 1; i >= 0; i--)
    {
        pathArray[i] = current.end;
        faceArray[i] = current.Item2;
        if (!cameFrom.TryGetValue(current, out current))
            break;
    }

    return pathArray[0].Equals(start) ? (pathArray, faceArray, length) : (null, null, 0);
}




    // Получение соседних позиций с учетом направлений и соединений
    private (List<FastPosKey>, List<byte>, bool[], bool[]) GetNeighbors(
    FastPosKey pos, bool[] processFaces, byte startFace,
    Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        _neighborsFast?.Clear();
        _neighborsFace?.Clear();
        _nowProcessed.Fill(false);
        _queue2?.Clear();
        _processFacesBuf.Fill(false);

        if (!TryGetPart(parts, pos, out var part))
            return (_neighborsFast, _neighborsFace, _nowProcessed, processFaces);

        var Connections = part.Connection;
        var hereConnections = Facing.None;

        // Определение доступных соединений
        for (byte i = 0; i < 6; i++)
            if (part.Networks[i] == network && !processFaces[i])
                hereConnections |= Connections & faceMasks[i];

        // BFS по направлениям
        _queue2.Enqueue(startFace);
        processFaces.CopyTo(_processFacesBuf, 0);
        _processFacesBuf[startFace] = true;

        while (_queue2.Count > 0)
        {
            int faceIndex = _queue2.Dequeue();
            var face = FacingHelper.BlockFacingFromIndex(faceIndex);
            var mask = FacingHelper.FromFace(face);
            var connections = hereConnections & mask;

            FacingHelper.FillDirections(connections, _bufForDirections);
            foreach (var dir in _bufForDirections)
            {
                var idx = (byte)dir.Index;
                if (!_processFacesBuf[idx] &&
                    (hereConnections & FacingHelper.From(dir, face)) != 0)
                {
                    _processFacesBuf[idx] = true;
                    _queue2.Enqueue(idx);
                }
            }
        }

        // Формирование маски валидных направлений
        var validMask = Facing.None;
        for (byte i = 0; i < 6; i++)
            if (_processFacesBuf[i])
                validMask |= FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
        hereConnections &= validMask;

        int px = part.Position.X, py = part.Position.Y, pz = part.Position.Z, dim = part.Position.dimension;
        FacingHelper.FillDirections(hereConnections, _bufForDirections);

        foreach (var dir in _bufForDirections)
        {
            // ищем соседей по граням
            var dv = dir.Normali;
            int nx = px + dv.X, ny = py + dv.Y, nz = pz + dv.Z;
            var neighborKey = new FastPosKey(nx, ny, nz, dim);

            if (TryGetPart(parts, neighborKey, out var neighborPart))
            {
                FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
                foreach (var face in _bufForFaces)
                {
                    var opp = dir.Opposite;
                    if ((neighborPart.Connection & FacingHelper.From(face, opp)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)face.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(opp, face)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)opp.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }

            // ищем соседей по ребрам
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
            foreach (var face in _bufForFaces)
            {
                dv = dir.Normali;
                var fv = face.Normali;
                nx = px + dv.X + fv.X;
                ny = py + dv.Y + fv.Y;
                nz = pz + dv.Z + fv.Z;

                neighborKey = new FastPosKey(nx, ny, nz, dim);

                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppDir = dir.Opposite;
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)oppDir.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)oppFace.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }

            // ищем соседей по перпендикулярной грани
            FacingHelper.FillFaces(hereConnections & FacingHelper.FromDirection(dir), _bufForFaces);
            foreach (var face in _bufForFaces)
            {
                var fv = face.Normali;
                nx = px + fv.X;
                ny = py + fv.Y;
                nz = pz + fv.Z;

                neighborKey = new FastPosKey(nx, ny, nz, dim);

                if (TryGetPart(parts, neighborKey, out neighborPart))
                {
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(dir, oppFace)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)dir.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, dir)) != 0)
                    {
                        _neighborsFast.Add(new FastPosKey(nx, ny, nz, dim, neighborPart.Position));
                        _neighborsFace.Add((byte)oppFace.Index);
                        _nowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }
            }
        }

        return (_neighborsFast, _neighborsFace, _nowProcessed, processFaces);
    }
}
