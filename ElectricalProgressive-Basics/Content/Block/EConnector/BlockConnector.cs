using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EConnector;

public class BlockConnector : BlockEBase
{
    /// <summary>
    /// Кеш мешей
    /// </summary>
    private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = new();
    

    /// <summary>
    /// ставим блок
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <param name="byItemStack"></param>
    /// <returns></returns>
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        var selection = new Selection(blockSel);
        var facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEConnector entity
        )
        {
            entity.Facing = facing;  //сообщаем направление

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this, entity);

            return true;
        }

        return false;
    }




    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos,
        Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

        if (api is ICoreClientAPI &&
            api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEConnector entity &&
            entity.Facing != Facing.None)
        {
            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            // VerticesCount отличается у LOD0 и LOD2
            var cacheKey = (facing, code, sourceMesh.VerticesCount);

            if (!MeshCache.TryGetValue(cacheKey, out var meshData))
            {
                var origin = new Vec3f(0.5f, 0.5f, 0.5f);

                // Клонируем входящий меш (уже правильный — LOD0 или LOD2)
                meshData = sourceMesh.Clone();

                // быстро враащем обьект
                FacingRotations.ApplyRotations(meshData, facing);


                MeshCache.TryAdd(cacheKey, meshData);
            }

            sourceMesh = meshData;
        }
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
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }



    /// <summary>
    /// При выходе из мира
    /// </summary>
    /// <param name="api"></param>
    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshCache?.Clear();
    }


}