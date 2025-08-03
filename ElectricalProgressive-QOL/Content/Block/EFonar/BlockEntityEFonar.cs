using System;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EFonar
{
    internal class BlockEntityEFonar : BlockEntityEFacingBase
    {
        private BEBehaviorEFonar Behavior => GetBehavior<BEBehaviorEFonar>();

        public bool IsEnabled => Behavior.LightLevel >= 1;

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBytes(FacingKey, SerializerUtil.Serialize(Facing));
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
                Api?.Logger.Error(exception.ToString());
            }
        }
    }
}

