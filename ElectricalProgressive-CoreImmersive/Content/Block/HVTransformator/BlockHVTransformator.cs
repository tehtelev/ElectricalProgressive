using ElectricalProgressive.Utils;
using EPImmersive.Content.Block.CableSwitch;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace EPImmersive.Content.Block.HVTransformator
{
    internal class BlockHVTransformator : ImmersiveWireBlock
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
        


        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (
                !world.BlockAccessor
                    .GetBlock(pos.AddCopy(BlockFacing.DOWN))
                    .SideSolid[BlockFacing.indexUP]
            )
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }


        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
           

            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
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

            var imvoltage = MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "imvoltage", 0);
            var voltage = MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0);

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage_immersive") + ": " + imvoltage + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + voltage + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

      
    }
}
