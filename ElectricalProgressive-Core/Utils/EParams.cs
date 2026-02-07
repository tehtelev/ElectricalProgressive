using System;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Параметры проводов/приборов как участников электрической цепи
    /// </summary>
    public class EParams : IEquatable<EParams>
    {
        private static readonly int maxSecBeforeBurnout = ElectricalProgressive.timeBeforeBurnout; //максимальное время в секундах до сгорания проводника
        private static readonly int maxTicksBeforeBurnout = maxSecBeforeBurnout*ElectricalProgressive.speedOfElectricity; //максимальное количество тиков до сгорания проводника 

        public int voltage;         //напряжение
        public float maxCurrent;    //максимальный ток
        public string material;     //индекс материала
        public float resistivity;   //удельное сопротивление
        public byte lines;          //количество линий
        public float crossArea;     //площадь поперечного сечения
        public bool burnout;        //провод сгорел
        public bool isolated;       //изолированный проводник
        public bool isolatedEnvironment; //изолированный от окружающей среды проводник
        public byte causeBurnout;   //причина сгорания (0 - не сгорел, 1 - перегрузка по току, 2 - неверное напряжение, 3 - окружающая среда)
        public int ticksBeforeBurnout; //количество тиков, которые накопил проводник
        public float current;            //ток проходящий тут

        /// <summary>
        /// Конструктор для создания параметров проводника/приборов
        /// </summary>
        /// <param name="voltage"></param>
        /// <param name="maxCurrent"></param>
        /// <param name="material"></param>
        /// <param name="resistivity"></param>
        /// <param name="lines"></param>
        /// <param name="crossArea"></param>
        /// <param name="burnout"></param>
        /// <param name="isolated"></param>
        /// <param name="isolatedEnvironment"></param>
        /// <param name="causeBurnout"></param>
        public EParams(int voltage,
            float maxCurrent,
            string material,
            float resistivity,
            byte lines,
            float crossArea,
            bool burnout,
            bool isolated,
            bool isolatedEnvironment, 
            byte causeBurnout=0,
            float current = 0.0F
            )
        {
            this.voltage = voltage;
            this.maxCurrent = maxCurrent;
            this.material = material;
            this.resistivity = resistivity;
            this.lines = lines;
            this.crossArea = crossArea;
            this.burnout = burnout;
            this.isolated = isolated;
            this.isolatedEnvironment = isolatedEnvironment;
            this.causeBurnout = causeBurnout;
            this.ticksBeforeBurnout = 0;
            this.current = current;
        }


        /// <summary>
        /// Конструктор по умолчанию для создания параметров проводника/приборов
        /// </summary>
        public EParams()
        {
            voltage = 0;
            maxCurrent = 0.0F;
            material = "";
            resistivity = 0.0F;
            lines = 0;
            crossArea = 0.0F;
            burnout = false;
            isolated = false;
            isolatedEnvironment = true;
            causeBurnout = 0;
            ticksBeforeBurnout = 0;
            current = 0.0F;
        }

        /// <summary>
        /// Метод для подготовки проводника к сгоранию
        /// </summary>
        /// <param name="causeBurnout"></param>
        public void prepareForBurnout(byte causeBurnout)
        {
            if (burnout) // если проводник уже сгорел, то ничего не делаем
                return;

            if (causeBurnout != this.causeBurnout) // если причина сгорания изменилась
                this.causeBurnout = causeBurnout;

            if (this.causeBurnout == 3)
                this.ticksBeforeBurnout += 40; // увеличиваем количество тиков, которые накопил проводник от погоды
            else
                this.ticksBeforeBurnout += 2; // увеличиваем количество тиков, которые накопил проводник

            if (ticksBeforeBurnout > maxTicksBeforeBurnout) // если проводник накопил максимальное количество тиков
            {
                burnout = true; // проводник сгорел
                ticksBeforeBurnout = 0; // обнуляем количество тиков
            }

        }

        /// <summary>
        /// Проверка на равенство двух экземпляров EParams
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(EParams other)
        {
            return voltage == other.voltage &&
                   maxCurrent.Equals(other.maxCurrent) &&
                   material == other.material &&
                   resistivity.Equals(other.resistivity) &&
                   lines == other.lines &&
                   crossArea.Equals(other.crossArea) &&
                   burnout == other.burnout &&
                   isolated == other.isolated &&
                   isolatedEnvironment == other.isolatedEnvironment &&
                   causeBurnout == other.causeBurnout &&
                   ticksBeforeBurnout == other.ticksBeforeBurnout;

        }

        /// <summary>
        /// Копирует значения из другого экземпляра EParams в текущий
        /// </summary>
        public void CopyFrom(EParams other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            this.voltage = other.voltage;
            this.maxCurrent = other.maxCurrent;
            this.material = other.material;
            this.resistivity = other.resistivity;
            this.lines = other.lines;
            this.crossArea = other.crossArea;
            this.burnout = other.burnout;
            this.isolated = other.isolated;
            this.isolatedEnvironment = other.isolatedEnvironment;
            this.causeBurnout = other.causeBurnout;
            this.ticksBeforeBurnout = other.ticksBeforeBurnout;
            this.current = other.current;
        }

        /// <summary>
        /// Переопределение метода Equals для сравнения объектов EParams
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            return obj is EParams other && Equals(other);
        }


        /// <summary>
        /// Создает глубокую копию текущего экземпляра EParams
        /// </summary>
        /// <returns>Новый экземпляр EParams с теми же значениями</returns>
        public EParams Clone()
        {
            return new EParams
            {
                voltage = this.voltage,
                maxCurrent = this.maxCurrent,
                material = this.material, // string неизменяем, можно просто присвоить
                resistivity = this.resistivity,
                lines = this.lines,
                crossArea = this.crossArea,
                burnout = this.burnout,
                isolated = this.isolated,
                isolatedEnvironment = this.isolatedEnvironment,
                causeBurnout = this.causeBurnout,
                ticksBeforeBurnout = this.ticksBeforeBurnout,
                current = this.current
            };
        }


        /// <summary>
        /// Переопределение метода GetHashCode для получения хэш-кода объекта EParams
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + voltage;
                hash = hash * 31 + maxCurrent.GetHashCode();
                hash = hash * 31 + material.GetHashCode();
                hash = hash * 31 + resistivity.GetHashCode();
                hash = hash * 31 + lines;
                hash = hash * 31 + crossArea.GetHashCode();
                hash = hash * 31 + burnout.GetHashCode();
                hash = hash * 31 + isolated.GetHashCode();
                hash = hash * 31 + isolatedEnvironment.GetHashCode();
                hash = hash * 31 + causeBurnout;
                hash = hash * 31 + ticksBeforeBurnout;
                hash = hash * 31 + current.GetHashCode();
                return hash;
            }
        }

    }
}