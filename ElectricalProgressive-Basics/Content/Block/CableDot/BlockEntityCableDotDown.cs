using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.CableDot
{
    internal class BlockEntityCableDotDown : BlockEntityEIBase
    {
        private BEBehaviorCableDot Behavior => GetBehavior<BEBehaviorCableDot>();
        

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RotationCache = CreateRotationCache();
        }



        private static Dictionary<Facing, RotationData> CreateRotationCache()
        {
            return new Dictionary<Facing, RotationData>
            {
                { Facing.DownNorth, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.DownEast, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.DownSouth, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.DownWest, new RotationData(0.0f, 90.0f, 0.0f) },
            };
        }

    }
}

