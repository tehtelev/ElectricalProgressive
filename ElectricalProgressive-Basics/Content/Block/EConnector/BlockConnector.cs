using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Content.Block.EGenerator;
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

    private static readonly Dictionary<(Facing, string), MeshData> MeshData = new();



    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshData.Clear();
    }










    //ставим блок
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
            entity.Facing = facing;                             //сообщаем направление

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

        if (api is ICoreClientAPI clientApi &&
            api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEConnector entity &&
            entity.Facing != Facing.None
           )
        {


            var facing = entity.Facing;   //куда смотрит генератор
            string code = entity.Block.Code; //код блока

            if (!MeshData.TryGetValue((facing, code), out var meshData))
            {
                var origin = new Vec3f(0.5f, 0.5f, 0.5f);
                var block = clientApi.World.BlockAccessor.GetBlockEntity(pos).Block;

                clientApi.Tesselator.TesselateBlock(block, out meshData);
                clientApi.TesselatorManager.ThreadDispose(); //обязательно?

                if ((facing & Facing.NorthEast) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.NorthWest) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.NorthUp) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.NorthDown) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.EastNorth) != 0)
                {
                    meshData.Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.EastSouth) != 0)
                {
                    meshData.Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.EastUp) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.EastDown) != 0)
                {
                    meshData.Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.SouthEast) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD,
                        180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.SouthWest) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD,
                        180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.SouthUp) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD,
                        180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.SouthDown) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD,
                        180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.WestNorth) != 0)
                {
                    meshData.Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.WestSouth) != 0)
                {
                    meshData.Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.WestUp) != 0)
                {
                    meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.WestDown) != 0)
                {
                    meshData.Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.UpNorth) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.UpEast) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.UpSouth) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.UpWest) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                }

                if ((facing & Facing.DownNorth) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.DownEast) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.DownSouth) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 0.0f);
                }

                if ((facing & Facing.DownWest) != 0)
                {
                    meshData.Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 0.0f);
                }

                MeshData.TryAdd((facing, code), meshData);
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
        dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("V"));
        dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
    }


}