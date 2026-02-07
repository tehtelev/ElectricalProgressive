using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.WallConnector
{
    internal class BlockWConnector : ImmersiveWireBlock
    {
        

        public override void OnLoaded(ICoreAPI coreApi)
        {
            base.OnLoaded(coreApi);

            _skipNonCenterCollisions = true;
        }
        
        

     
        
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {

            var blockCode = CodeWithVariants(new()
            {
                { "side", "north" }
            });

            var block = world.BlockAccessor.GetBlock(blockCode);
            return new(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return [OnPickBlock(world, pos)];
        }
        




        /// <summary>
        /// Получение информации о предмете в инвентаре
        /// </summary>
        /// <param name="inSlot"></param>
        /// <param name="dsc"></param>
        /// <param name="world"></param>
        /// <param name="withDebugInfo"></param>
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + (MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

      
    }
}
