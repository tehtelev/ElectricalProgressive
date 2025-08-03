using ElectricalProgressive.Content.Block.ECable;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{ 
    class GetCableAsset
    {
        /// <summary>
        /// Извлекаем нужный вариант блока провода
        /// </summary>
        /// <param name="api"></param>
        /// <param name="baseBlock"></param>
        /// <param name="material"></param>
        /// <param name="indexQuantity"></param>
        /// <param name="indexType"></param>
        public Block CableAsset(ICoreAPI api, CollectibleObject baseBlock, int indexVoltage, string material, int indexQuantity, int indexType)
        {
            string[] t = new string[4];
            string[] v = new string[4];

            t[0] = "voltage";
            t[1] = "material";
            t[2] = "quantity";
            t[3] = "type";

            v[0] = BlockECable.voltages[indexVoltage];
            v[1] = material;
            v[2] = BlockECable.quantitys[indexQuantity];
            v[3] = BlockECable.types[indexType];


            var assetLocation = baseBlock.CodeWithVariants(t, v);

            return api.World.GetBlock(assetLocation);

        }
    }
}
