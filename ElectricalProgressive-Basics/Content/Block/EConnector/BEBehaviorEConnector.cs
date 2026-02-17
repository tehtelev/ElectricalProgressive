using ElectricalProgressive.Interface;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EConnector
{
    public class BEBehaviorEConnector : BlockEntityBehavior, IElectricConductor
    {
        public BEBehaviorEConnector(BlockEntity blockentity) : base(blockentity)
        {
        }

        public new BlockPos Pos => Blockentity.Pos;




        /// <summary>
        /// Обновление блока кабеля
        /// </summary>
        public void Update()
        {
            // Blockentity.MarkDirty();
        }


    }
}
