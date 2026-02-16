using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Точка подключения провода
    /// </summary>
    public class WireNode
    {
        public byte Index { get; set; }           // Индекс точки подключения
        public int Voltage { get; set; }          // Напряжение точки (максимальное)
        public Vec3d Position { get; set; }       // Локальная позиция относительно позиции блока
        public float Radius { get; set; }         // Радиус области подключения (для размеров рамки выделения вокрг точки)
    }
}
