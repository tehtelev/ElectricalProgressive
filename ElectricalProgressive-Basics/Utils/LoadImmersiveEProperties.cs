using ElectricalProgressive.Content.Block;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Utils
{
    public static class LoadImmersiveEProperties
    {
        /// <summary>
        /// Загружает и применяет электрические параметры для блоков с иммерсивными проводами
        /// </summary>
        /// <param name="block">Блок для загрузки параметров</param>
        /// <param name="entity">Сущность блока с поведением BEBehaviorElectricalProgressive</param>
        public static void Load(Block block, dynamic entity)
        {
            BEBehaviorEPImmersive? immersiveElectricity = entity.GetBehavior<BEBehaviorEPImmersive>();
            if (immersiveElectricity == null)
            {
                return;
            }

            // Загружаем параметры из JSON атрибутов

            var voltage = MyMiniLib.GetAttributeInt(block, "imvoltage", 0);
            if (voltage==0) //если imvoltage нет, то берем обычный
                voltage = MyMiniLib.GetAttributeInt(block, "voltage", 512);

            var maxCurrent = MyMiniLib.GetAttributeFloat(block, "maxCurrent", 20.0F);
            var isolated = MyMiniLib.GetAttributeBool(block, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false);

            // Создаем основные параметры блока
            var mainParams = new EParams(
                voltage,
                maxCurrent,
                "",
                0,
                1,
                1,
                false,
                isolated,
                isolatedEnvironment
            );

            // Устанавливаем основные параметры в поведение
            immersiveElectricity.AddMainEparams(mainParams);
        }
    }
}