using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Сам пакет с энергией
    /// </summary>
    public class EnergyPacket
    {
        /// <summary>
        /// Энергия, которая движется в этом пакете.
        /// </summary>
        public float energy;

        /// <summary>
        /// Напряжение, с которым движется энергия.
        /// </summary>
        public int voltage;


        /// <summary>
        /// Текущий индекс в пути, где сейчас пакет
        /// </summary>
        public int currentIndex = -1;

        /// <summary>
        /// Последовательность позиций по которой движется энергия.
        /// </summary>
        public readonly BlockPos[] path;

        /// <summary>
        /// Откуда мы пришли в каждой точке пути.
        /// </summary>
        public readonly int[] facingFrom;

        /// <summary>
        /// Какие грани каждого блока уже обработаны.
        /// </summary>
        public readonly bool[][] nowProcessedFaces;

        /// <summary>
        /// Через какие соединения шёл ток.
        /// </summary>
        public readonly Facing[] usedConnections;


        /// <summary>
        /// Флаг, который говорит, что пакет должен быть удалён и считается невалидным
        /// </summary>
        public bool shouldBeRemoved;



        /// <summary>
        /// Создаёт пакет, просто сохраняя ссылки на массивы из кэша.
        /// </summary>
        public EnergyPacket(
            float Energy,
            int Voltage,
            int CurrentIndex,
            BlockPos[] Path,
            int[] FacingFrom,
            bool[][] NowProcessedFaces,
            Facing[] UsedConnections
        )
        {
            energy = Energy;
            voltage = Voltage;
            currentIndex = CurrentIndex;
            path = Path;
            facingFrom = FacingFrom;
            nowProcessedFaces = NowProcessedFaces;
            usedConnections = UsedConnections;
            shouldBeRemoved = false;
        
        }


    }
}