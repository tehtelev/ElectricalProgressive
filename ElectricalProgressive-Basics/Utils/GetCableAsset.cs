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
            var t = new string[4];
            var v = new string[4];

            t[0] = "voltage";
            t[1] = "material";
            t[2] = "quantity";
            t[3] = "type";

            v[0] = BlockECable.Voltages[indexVoltage];
            v[1] = material;
            v[2] = BlockECable.Quantitys[indexQuantity];
            v[3] = BlockECable.Types[indexType];


            var assetLocation = baseBlock.CodeWithVariants(t, v);

            return api.World.GetBlock(assetLocation);

        }
    }
}

