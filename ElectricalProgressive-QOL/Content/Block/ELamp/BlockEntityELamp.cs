using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ELamp
{
    internal class BlockEntityELamp : BlockEntityEFacingBase
    {
        private BEBehaviorELamp Behavior => this.GetBehavior<BEBehaviorELamp>();

        public bool IsEnabled => this.Behavior.LightLevel >= 1;

        public override Facing GetConnection(Facing value)
        {
            // если лампа маленькая
            if (Block.Code.ToString().Contains("small") || Block.Code.ToString().Contains("nasteniy"))
                return value;

            // если лампа обычная
            return FacingHelper.FullFace(value);
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
                Facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
            }
            catch (Exception exception)
            {
                if (!this.Block.Code.ToString().Contains("small") && !this.Block.Code.ToString().Contains("nasteniy"))
                    Facing = Facing.UpNorth;
                this.Api?.Logger.Error(exception.ToString());
            }
        }
    }
}

