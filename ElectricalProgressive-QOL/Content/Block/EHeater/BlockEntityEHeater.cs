using System;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHeater
{
    public class BlockEntityEHeater : BlockEntityEFacingBase, IHeatSource
    {
        private BEBehaviorEHeater Behavior => this.GetBehavior<BEBehaviorEHeater>();

        public bool IsEnabled => this.Behavior?.HeatLevel >= 1;

        public override Facing GetConnection(Facing value)
        {
            return FacingHelper.FullFace(value);
        }

        /// <summary>
        /// Отвечает за тепло отдаваемое в окружающую среду
        /// </summary>
        /// <param name="world"></param>
        /// <param name="heatSourcePos"></param>
        /// <param name="heatReceiverPos"></param>
        /// <returns></returns>
        public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
        {
            if (this.Behavior == null)
                return 0.0f;

            return this.Behavior.HeatLevel / this.Behavior.getPowerRequest() * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBytes(FacingKey, SerializerUtil.Serialize(this.Facing));
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                this.Facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
            }
            catch (Exception exception)
            {
                this.Api?.Logger.Error(exception.ToString());
            }
        }
    }
}
