using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;


namespace ElectricalProgressive.Content.Block.Gauge;
public class BlockGauge : Vintagestory.API.Common.Block
{

    private WorldInteraction[] _interactions = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _interactions = ObjectCacheUtil.GetOrCreate(api, "BlockGaugeInteractions", () =>
        {
            return new WorldInteraction[]
            {
                new()
                {
                    ActionLangCode = "electricalprogressiveindustry:blockhelp-pickup",
                    MouseButton = EnumMouseButton.Right
                }
            };
        });

    }
    


    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        return true;
    }


    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);


        // Создаём предмет из блока
        var stack = new ItemStack(this);
        if (byPlayer.InventoryManager.TryGiveItemstack(stack, true))
        {
            world.BlockAccessor.SetBlock(0, blockSel.Position); // Удаляем блок
            world.PlaySoundAt(new AssetLocation("sounds/player/build"), blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, byPlayer);
        }
    }

    
        



   
}