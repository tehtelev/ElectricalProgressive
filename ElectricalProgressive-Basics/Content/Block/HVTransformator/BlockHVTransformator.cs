using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.HVTransformator
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

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
            BlockSelection blockSel, ref string failureCode)
        {
            //неваляжка - только вертикально
            // целая ли грань, на которую ставим
            if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
            {
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }




        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // целая ли грань еще
            if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
            {
                return;
            }

            // иначе ломаем
            world.BlockAccessor.BreakBlock(pos, null);
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

            var block = inSlot.Itemstack?.Block;
            if (block == null)
                return;

            var imvoltage = MyMiniLib.GetAttributeInt(block, "imvoltage", 0);
            var voltage = MyMiniLib.GetAttributeInt(block, "voltage", 0);

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage_immersive") + ": " + imvoltage + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + voltage + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

      
    }
}
