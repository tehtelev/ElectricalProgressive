using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ElectricalProgressive.Utils
{
    public class ExponentialMovingAverage
    {
        private double _alpha;
        private double _currentValue;
        private bool _isInitialized;

        // Создание фильтра с заданным коэффициентом сглаживания (0 < alpha ≤ 1)
        public ExponentialMovingAverage(double alpha)
        {
            if (alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between 0 (exclusive) and 1 (inclusive).");

            _alpha = alpha;
        }

        // Создание фильтра с заданным периодом усреднения (period ≥ 1)
        public ExponentialMovingAverage(int period)
        {
            if (period < 1)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

            _alpha = 2.0 / (period + 1);
        }

        // Обновление значения фильтра новым измерением
        public double Update(double nextValue)
        {
            if (!_isInitialized)
            {
                _currentValue = nextValue;
                _isInitialized = true;
            }
            else
            {
                _currentValue = _alpha * nextValue + (1 - _alpha) * _currentValue;
            }
            return _currentValue;
        }

        // Текущее сглаженное значение
        public double CurrentValue => _currentValue;
    }
}
