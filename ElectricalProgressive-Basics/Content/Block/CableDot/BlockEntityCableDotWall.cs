using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.CableDot
{
    internal class BlockEntityCableDotWall : BlockEntityEIBase
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
                { Facing.NorthEast, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthWest, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthUp, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthDown, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.EastNorth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastSouth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastUp, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastDown, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.SouthEast, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthWest, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthUp, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthDown, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.WestNorth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestSouth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestUp, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestDown, new RotationData(0.0f, 90.0f, 0.0f) }
            };
        }
    }
}

