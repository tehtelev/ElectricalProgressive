
namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Конфигуратор сети
    /// </summary>
    public class ElectricityConfig
    {
        public int SpeedOfElectricity = 8;
        public int TimeBeforeBurnout = 30;
        public int MultiThreading = 4;
        public int CacheTimeoutCleanupMinutes = 2;
        public int MaxDistanceForFinding = 200;
        public float EnergyLossFactor = 1.0f;
        public bool EnableLossCompensation = false;
        public bool EnableCameraRotateForGlider = true;
        public float MaxGliderSpeed = 1.0f;
        public int GliderDurabilityLossAmount = 15;
    }
}
