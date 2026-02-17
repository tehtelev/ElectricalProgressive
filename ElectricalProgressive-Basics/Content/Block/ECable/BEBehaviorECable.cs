using ElectricalProgressive.Interface;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECable
{
    public class BEBehaviorECable : BlockEntityBehavior, IElectricConductor
    {
        public BEBehaviorECable(BlockEntity blockentity) : base(blockentity)
        {
        }

        public new BlockPos Pos => Blockentity.Pos;


        bool hasBurnout = false;
        bool prepareBurnout = false;

        /// <summary>
        /// Подсказка при наведении на блок
        /// </summary>
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);


            //if (Blockentity is not BlockEntityECable entity)
            //    return;
            
            //stringBuilder.AppendLine("Заглушка");

        }

        /// <summary>
        /// Обновление блока кабеля
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Update()
        {
            if (Blockentity is not BlockEntityECable entity ||
                entity.ElectricalProgressive == null ||
                entity.ElectricalProgressive.AllEparams is null)
            {
                return;
            }

            bool anyBurnout = false;
            bool anyPrepareBurnout = false;

            foreach (var eParam in entity.ElectricalProgressive.AllEparams)
            {
                if (!hasBurnout && eParam.burnout)
                {
                    hasBurnout = true;
                    entity.MarkDirty(true);
                }

                if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
                {
                    prepareBurnout = true;
                    entity.MarkDirty(true);
                }

                if (eParam.burnout)
                    anyBurnout = true;

                if (eParam.ticksBeforeBurnout > 0)
                    anyPrepareBurnout = true;
            }

            if (!anyBurnout && hasBurnout)
            {
                hasBurnout = false;
                entity.MarkDirty(true);
            }

            if (!anyPrepareBurnout && prepareBurnout)
            {
                prepareBurnout = false;
                entity.MarkDirty(true);
            }
        }


    }
}
