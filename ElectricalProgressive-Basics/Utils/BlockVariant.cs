using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;

public class BlockVariant
{
    public readonly Cuboidf[] CollisionBoxes;
    public readonly MeshData? MeshData;
    public readonly Cuboidf[] SelectionBoxes;


    public BlockVariant(ICoreAPI api, CollectibleObject baseBlock, string variant)
    {
        var assetLocation = baseBlock.CodeWithVariant("type", variant);
        var block = api.World.GetBlock(assetLocation);

        this.CollisionBoxes = block.CollisionBoxes;
        this.SelectionBoxes = block.SelectionBoxes;

        if (api is ICoreClientAPI clientApi)
        {
            var cachedShape = clientApi.TesselatorManager.GetCachedShape(block.Shape.Base);
            clientApi.Tesselator.TesselateShape(block, cachedShape, out this.MeshData);
            clientApi.TesselatorManager.ThreadDispose(); //обязательно!!
        }
    }
}
