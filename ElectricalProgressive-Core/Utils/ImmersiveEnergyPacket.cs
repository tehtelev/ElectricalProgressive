using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Пакет энергии для иммерсивных проводов
    /// </summary>
    public class ImmersiveEnergyPacket
    {
        /// <summary>
        /// Энергия, которая движется в этом пакете
        /// </summary>
        public float energy;

        /// <summary>
        /// Напряжение, с которым движется энергия
        /// </summary>
        public int voltage;

        /// <summary>
        /// Текущий индекс в пути, где сейчас пакет
        /// </summary>
        public int currentIndex = -1;

        /// <summary>
        /// Последовательность позиций по которой движется энергия
        /// </summary>
        public readonly BlockPos[] path;

        /// <summary>
        /// Индексы узлов в каждом блоке пути, через которые проходит пакет
        /// Для каждого шага path[i] -> path[i+1] хранится узел в path[i]
        /// </summary>
        public readonly byte[] nodeIndices;

        /// <summary>
        /// Флаг, который говорит, что пакет должен быть удалён
        /// </summary>
        public bool shouldBeRemoved;

        /// <summary>
        /// Создаёт пакет для иммерсивных проводов
        /// </summary>
        public ImmersiveEnergyPacket(
            float Energy,
            int Voltage,
            int CurrentIndex,
            BlockPos[] Path,
            byte[] NodeIndices
        )
        {
            energy = Energy;
            voltage = Voltage;
            currentIndex = CurrentIndex;
            path = Path;
            nodeIndices = NodeIndices;
            shouldBeRemoved = false;
        }
    }
}