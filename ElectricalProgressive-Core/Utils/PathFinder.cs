using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;


namespace ElectricalProgressive.Utils;

public class PathFinder
{

    /// <summary>
    /// Эвристическая функция (манхэттенское расстояние)
    /// </summary>
    private static int Heuristic(BlockPos a, BlockPos b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
    }


    /// <summary>
    /// Массив масок граней для фильтрации соединений
    /// </summary>
    private static Facing[] faceMasks =
    {
        Facing.NorthAll, Facing.EastAll, Facing.SouthAll, Facing.WestAll, Facing.UpAll, Facing.DownAll
    };

    // Переменные используемые в ReconstructPath, чтобы избежать очень частых аллокаций
    BlockPos[] pathArray = new BlockPos[1];
    int[] faceArray = new int[1];


    // Переменные используемые в GetNeighbors, чтобы избежать очень частых аллокаций
    private List<BlockPos> Neighbors = new(27);      // координата соседа
    private List<int> NeighborsFace = new(27);            // грань соседа с которым мы взаимодействовать будем
    private bool[] NowProcessed = new bool[6];                    // задействованные грани в этой точке
    private Queue<int> queue2 = new();
    private bool[] processFacesBuf = new bool[6];
    private BlockPos? neighborPosition;
    private List<BlockFacing> bufForDirections = new List<BlockFacing>(6);
    private List<BlockFacing> bufForFaces = new List<BlockFacing>(6);

    // Переменные используемые в FindShortestPath, чтобы избежать очень частых аллокаций
    private List<int> startBlockFacing = new();
    private List<int> endBlockFacing = new();
    private PriorityQueue<(BlockPos, int), int> queue = new();
    private Dictionary<(BlockPos, int), (BlockPos, int)> cameFrom = new();
    private List<BlockPos> cameFromList = new();
    private Dictionary<BlockPos, bool[]> processedFaces = new();
    private Dictionary<(BlockPos, int), int> facingFrom = new();
    private Dictionary<(BlockPos, int), bool[]> nowProcessedFaces = new();
    private HashSet<BlockPos> networkPositions = new();
    private List<BlockPos> buf1 = new();     //список соседей
    private List<int> buf2 = new();          //список граней соседей
    private bool[]? buf3;            //список граней, которые сейчас в работе
    private bool[]? buf4;            //список граней, которые уже просчитаны
    private BlockPos? currentPos;    //текущая позиция
    private int currentFace;        //текущая грань



    public void Clear()
    {
        processedFaces.Clear();
    }

    /// <summary>
    /// Ищет кратчайший путь от начальной позиции к конечной в сети
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <returns></returns>
    public (BlockPos[], int[], bool[][], Facing[]) FindShortestPath(BlockPos start, BlockPos end, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        // очищаем предыдущие данные
        startBlockFacing.Clear();
        endBlockFacing.Clear();
        queue.Clear();
        cameFrom.Clear();
        cameFromList.Clear();
        facingFrom.Clear();
        nowProcessedFaces.Clear();
        //processedFaces.Clear();
        buf1.Clear();
        buf2.Clear();
        buf3 = Array.Empty<bool>();
        buf4 = Array.Empty<bool>();





        networkPositions = network.PartPositions; // ни в коем случае не очищать

        //проверяем наличие начальной и конечной точки в этой цепи
        if (!networkPositions.Contains(start) || !networkPositions.Contains(end))
            return (null!, null!, null!, null!);


        //смотрим с какой грани начинать
        var startConnection = parts[start].Connection;
        foreach (var face in FacingHelper.Faces(startConnection))
        {
            startBlockFacing.Add(face.Index);
        }


        // startBlockFacing[0] и endBlockFacing[0] будут работать корректно до тех пор, пока не появятся источники и приемники энергии, у которых несколько граней на передачу и прием!!!!



        //смотрим с какой грани заканчивать
        var endConnection = parts[end].Connection;
        foreach (var face in FacingHelper.Faces(endConnection))
        {
            endBlockFacing.Add(face.Index);
        }

        //очередь обработки

        queue.Enqueue((start, startBlockFacing[0]), 0);

        //хранит цепочку пути и грань

        cameFrom[(start, startBlockFacing[0])] = (null!, 0);

        //хранит цепочку пути (для вывода наружу)
        cameFromList.Add(start);

        //хранит номер задействованной грани соседа 

        facingFrom[(start, startBlockFacing[0])] = startBlockFacing[0];


        //хранит для каждого кусочка цепи посещенные грани в данный момент

        nowProcessedFaces[(start, startBlockFacing[0])] = new bool[6] { false, false, false, false, false, false };
        nowProcessedFaces[(start, startBlockFacing[0])][startBlockFacing[0]] = true;




        // хранит для каждого кусочка цепи все посещенные грани
        // словарь не перезаполняется, а лишь очищается при каждом новом запуске поиска пути для той же сети, чтобы не создавать новые объекты
        foreach (var index in networkPositions)
        {
            if (!processedFaces.TryGetValue(index, out _))
            {
                processedFaces.Add(index, new bool[6] { false, false, false, false, false, false });
            }
            else
            {
                processedFaces[index][0] = false;
                processedFaces[index][1] = false;
                processedFaces[index][2] = false;
                processedFaces[index][3] = false;
                processedFaces[index][4] = false;
                processedFaces[index][5] = false;
            }

        }



        while (queue.Count > 0)                 //пока очередь не опустеет
        {
            (currentPos, currentFace) = queue.Dequeue();

            if (currentPos.Equals(end))            //достигли конца и прекращаем просчет
                break;


            // Затем используйте распаковку:
            (buf1, buf2, buf3, buf4) = GetNeighbors(currentPos, processedFaces[currentPos], facingFrom[(currentPos, currentFace)], network, parts);


            processedFaces[currentPos] = buf4;    //обновляем информацию о всех просчитанных гранях

            int i = 0;
            foreach (var neighbor in buf1)
            {
                var state = (neighbor, buf2[i]);
                int priority = Heuristic(neighbor, end); // Приоритет = эвристика
                if (!processedFaces[neighbor][buf2[i]]   // проверяем, что грань соседа еще не обработана
                    && !cameFrom.ContainsKey(state)      // проверяем, что состояние еще не посещали
                    && priority < 200)                     // ограничение на приоритет, чтобы не зацикливаться на бесконечном поиске
                {

                    queue.Enqueue(state, priority);

                    cameFrom[state] = (currentPos, facingFrom[(currentPos, currentFace)]);
                    cameFromList.Add(neighbor);

                    facingFrom[state] = buf2[i];

                    // тут только копировать
                    var buf3copy=new bool[6];
                    Array.Copy(buf3, buf3copy, 6);
                    nowProcessedFaces.Add(state, buf3copy);

                }

                i++;
            }



            //if (cameFrom.Count > 1000)
            //{ // Ограничение на количество посещенных состояний
            //    return (null!, null!, null!, null!);
            //}

        }

        if (!cameFromList.Contains(end))        //не нашли конец?
            return (null!, null!, null!, null!);

        var (path, faces) = ReconstructPath(start, end, endBlockFacing[0], cameFrom);    //реконструкция маршрута


        // Если путь не найден, возвращаем null
        if (path == null !| start!=path?.First() || end!=path?.Last())
            return (null!, null!, null!, null!);

        Facing[] nowProcessingFaces = null!;      //храним тут Facing граней, которые сейчас в работе                                           
        bool[][] nowProcessedFacesList = null!; //хранит для каждого кусочка цепи посещенные грани в данный момент (для вывода наружу)                                                
        int[] facingFromList = null!;            //хранит номер задействованной грани соседа (для вывода наружу)


        // ниже можно код сделать компактнее, но потом

        bool[] npf;
        Facing facing;
        int pathLength = path.Count(); //длина пути
        nowProcessingFaces = new Facing[pathLength];
        nowProcessedFacesList = new bool[pathLength][];
        facingFromList = new int[pathLength];

        facingFromList[0] = facingFrom[(path[0], faces![0])];

        for (int i = 1; i < pathLength; i++)                                //подготавливаем дополнительные данные
        {
            facingFromList[i] = facingFrom[(path[i], faces[i])];
            // первый элемент не добавляем

            npf = nowProcessedFaces[(path[i], faces[i])];

            nowProcessedFacesList[i - 1] = npf;

            //фильтруем только нужные грани
            facing = parts[path[i - 1]].Connection &
                ((npf[0] ? Facing.NorthAll : Facing.None)
                | (npf[1] ? Facing.EastAll : Facing.None)
                | (npf[2] ? Facing.SouthAll : Facing.None)
                | (npf[3] ? Facing.WestAll : Facing.None)
                | (npf[4] ? Facing.UpAll : Facing.None)
                | (npf[5] ? Facing.DownAll : Facing.None));

            nowProcessingFaces[i - 1] = facing;


        }

        // последний элемент

        npf = new bool[6] { false, false, false, false, false, false };
        npf[endBlockFacing[0]] = true; //маркер, что мы закончили этой гранью

        nowProcessedFacesList[pathLength - 1] = npf;

        //фильтруем только нужные грани
        facing = parts[path[pathLength - 1]].Connection &
            ((npf[0] ? Facing.NorthAll : Facing.None)
            | (npf[1] ? Facing.EastAll : Facing.None)
            | (npf[2] ? Facing.SouthAll : Facing.None)
            | (npf[3] ? Facing.WestAll : Facing.None)
            | (npf[4] ? Facing.UpAll : Facing.None)
            | (npf[5] ? Facing.DownAll : Facing.None));

        nowProcessingFaces[pathLength - 1] = facing;

        

        return (path, facingFromList, nowProcessedFacesList, nowProcessingFaces);
    }




    /// <summary>
    /// Вычисляет позиции соседей от текущего значения
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    private (List<BlockPos>, List<int>, bool[], bool[]) GetNeighbors(BlockPos pos, bool[] processFaces, int startFace, Network network, Dictionary<BlockPos, NetworkPart> parts)
    {
        // очищаем предыдущие данные
        Neighbors.Clear();                                // координата соседа
        NeighborsFace.Clear();                            // грань соседа с которым мы взаимодействовать будем
        NowProcessed.Fill(false);                    // задействованные грани в этой точке
        queue2.Clear();
        processFacesBuf.Fill(false);


        var part = parts[pos];                                // текущий элемент
        var Connections = part.Connection;                    // соединения этого элемента


        Facing hereConnections = Facing.None;

        // выясняем какие грани соединены с сетью, не сгорели, или не обработаны еще
        for (int i = 0; i < 6; i++)
        {
            if (part.Networks[i] == network && !part.eparams[i].burnout && !processFaces[i])
            {
                hereConnections |= Connections & faceMasks[i];
            }
        }

        // выясняем с какой гранью мы работаем и соединены ли грани одной цепи
        int startFaceIndex = startFace;
        queue2.Enqueue(startFaceIndex);


        processFaces.CopyTo(processFacesBuf, 0);
        processFacesBuf[startFaceIndex] = true;

        // Поиск всех связанных граней
        while (queue2.Count > 0)
        {
            int currentFaceIndex = queue2.Dequeue();
            BlockFacing currentFace = FacingHelper.BlockFacingFromIndex(currentFaceIndex);
            Facing currentFaceMask = FacingHelper.FromFace(currentFace);
            Facing connections = hereConnections & currentFaceMask;

            FacingHelper.FillDirections(connections, bufForDirections);
            foreach (var direction in bufForDirections)
            {
                int targetFaceIndex = direction.Index;

                if (!processFacesBuf[targetFaceIndex] && (hereConnections & FacingHelper.From(direction, currentFace)) != 0)
                {
                    processFacesBuf[targetFaceIndex] = true;
                    queue2.Enqueue(targetFaceIndex);
                }
            }
        }

        // Обновляем hereConnections, оставляя только связи найденных граней
        Facing validConnectionsMask = Facing.None;
        for (int i = 0; i < 6; i++)
        {
            if (processFacesBuf[i])
            {
                validConnectionsMask |= FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i));
            }
        }
        hereConnections &= validConnectionsMask;


        // ищем соседей везде
        FacingHelper.FillDirections(hereConnections, bufForDirections);
        foreach (var direction in bufForDirections)
        {
            // ищем соседей по граням
            var directionFilter = FacingHelper.FromDirection(direction);

            neighborPosition = part.Position.AddCopy(direction);


            if (parts.TryGetValue(neighborPosition, out var neighborPart))
            {

                FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
                foreach (var face in bufForFaces)
                {
                    var opposite = direction.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(face, opposite)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(face.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(opposite, face)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(opposite.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }


            }

            // ищем соседей по ребрам
            directionFilter = FacingHelper.FromDirection(direction);

            FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
            foreach (var face in bufForFaces)
            {
                neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                if (parts.TryGetValue(neighborPosition, out neighborPart))
                {
                    var oppDir = direction.Opposite;
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(oppDir, oppFace)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppDir.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, oppDir)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }

            }


            // ищем соседей по перпендикулярной грани
            directionFilter = FacingHelper.FromDirection(direction);

            FacingHelper.FillFaces(hereConnections & directionFilter, bufForFaces);
            foreach (var face in bufForFaces)
            {
                neighborPosition = part.Position.AddCopy(face);

                if (parts.TryGetValue(neighborPosition, out neighborPart))
                {
                    var oppFace = face.Opposite;

                    if ((neighborPart.Connection & FacingHelper.From(direction, oppFace)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(direction.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }

                    if ((neighborPart.Connection & FacingHelper.From(oppFace, direction)) != 0)
                    {
                        Neighbors.Add(neighborPosition);
                        NeighborsFace.Add(oppFace.Index);
                        NowProcessed[face.Index] = true;
                        processFaces[face.Index] = true;
                    }
                }


            }
        }



        return (Neighbors, NeighborsFace, NowProcessed, processFaces);
    }






    /// <summary>
    /// Реконструирует маршрут
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="endFacing"></param>
    /// <param name="cameFrom"></param>
    /// <returns></returns>
    private (BlockPos[]? path, int[]? faces) ReconstructPath(
        BlockPos start,
        BlockPos end,
        int endFacing,
        Dictionary<(BlockPos, int), (BlockPos, int)> cameFrom)
    {
        // 1) Первый проход: считаем длину пути
        int length = 0;
        var current = (pos: end, facing: endFacing);

        while (current.pos != null)
        {
            length++;
            // пытаемся перейти к предку; если не можем — значит путь неполный
            if (!cameFrom.TryGetValue(current, out current))
                return (null, null);
        }

        // 2) Аллокация массивов ровно под нужный размер
        Array.Resize(ref pathArray, length);
        Array.Resize(ref faceArray, length);


        //var pathArray = new BlockPos[length];
        //var faceArray = new int[length];

        // 3) Второй проход: заполняем массивы с конца в начало
        current = (end, endFacing);
        for (int i = length - 1; i >= 0; i--)
        {
            pathArray[i] = current.pos;
            faceArray[i] = current.facing;
            // при последней итерации (i == 0) попытка провалится, но нам уже не нужен следующий элемент
            cameFrom.TryGetValue(current, out current);
        }

        // 4) Проверяем, что начало пути совпадает со стартовой точкой
        return pathArray[0].Equals(start)
            ? (pathArray, faceArray)
            : (null, null);
    }


}