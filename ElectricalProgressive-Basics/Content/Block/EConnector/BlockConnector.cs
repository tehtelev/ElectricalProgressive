using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EConnector;

public class BlockConnector : BlockEBase
{
    private ICoreAPI? _coreApi;


    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        this._coreApi = api;
    }






    public override void OnBlockBroken(IWorldAccessor world, BlockPos position, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
            if (this.api is ICoreClientAPI) {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(position) is BlockEntityECable  entity) {
                if (byPlayer is { CurrentBlockSelection: { } blockSelection }) {
                    var connection = entity.Connection & ~Facing.AllAll;

                    if (connection != Facing.None) {
                        var stackSize = FacingHelper.Count(Facing.AllAll);

                        if (stackSize > 0) {
                            entity.Connection = connection;
                            entity.MarkDirty(true);
                            return;
                        }

                    }
                }
            }
        

        base.OnBlockBroken(world, position, byPlayer, dropQuantityMultiplier);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityECable entity)
        {
            var blockFacing = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
            var selectedFacing = FacingHelper.FromFace(blockFacing);

            if ((entity.Connection & ~selectedFacing) == Facing.None)
                world.BlockAccessor.BreakBlock(pos, null);
        }
    }
}