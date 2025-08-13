using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Content.Block.EMotor;
public class BlockEMotor : BlockEBase, IMechanicalPowerBlock
{
    private readonly static Dictionary<(Facing, string), MeshData> MeshData = new();
    private static float[] def_Params = { 10.0F, 100.0F, 0.5F, 0.75F, 0.5F, 0.1F, 0.05F };   //заглушка

   

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        BlockEMotor.MeshData.Clear();
    }

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>() is IMechanicalPowerDevice device)
        {
            return device.Network;
        }

        return null;
    }

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEMotor entity && entity.Facing != Facing.None)
        {
            var directions = FacingHelper.Directions(entity.Facing).ToList();
            if (directions.Count>0)
            {
                return directions.First() == face;
            }
        }

        return false;
    }

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {

    }


    public override void OnLoaded(ICoreAPI coreApi)
    {
        base.OnLoaded(coreApi);

    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        var selection = new Selection(blockSel);

        Facing facing= Facing.None;



        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (
            FacingHelper.Faces(facing).First() is { } blockFacing &&
            !world.BlockAccessor
                .GetBlock(blockSel.Position.AddCopy(blockFacing))
                .SideSolid[blockFacing.Opposite.Index]
        )
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    //ставим блок
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {

        // если блок сгорел, то не ставим
        if (byItemStack.Block.Variant["type"] == "burned")
        {
            return false;
        }

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

        if (
            base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEMotor entity
        )
        {
            entity.Facing = facing;                             //сообщаем направление

            //задаем параметры блока/проводника
            var voltage = MyMiniLib.GetAttributeInt(this, "voltage", 32);
            var maxCurrent = MyMiniLib.GetAttributeFloat(this, "maxCurrent", 5.0F);
            var isolated = MyMiniLib.GetAttributeBool(this, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(this, "isolatedEnvironment", false);

            entity.Eparams = (
                new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
                FacingHelper.Faces(facing).First().Index);


            var blockFacing = FacingHelper.Directions(entity.Facing).First();
            var blockPos = blockSel.Position;
            var blockPos1 = blockPos.AddCopy(blockFacing);

            var beh = entity.GetBehavior<BEBehaviorMPBase>();
            beh?.CreateJoinAndDiscoverNetwork(blockFacing);

            if (
                world.BlockAccessor.GetBlock(blockPos1) is IMechanicalPowerBlock block &&
                block.HasMechPowerConnectorAt(world, blockPos1, blockFacing.Opposite)
            )
            {
                block.DidConnectAt(world, blockPos1, blockFacing.Opposite);

                beh?.tryConnect(blockFacing);
            }

            return true;
        }

        return false;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEMotor entity)
        {
            var faces = FacingHelper.Faces(entity.Facing).ToList();
            if (
            faces != null &&
            faces.Any() &&
            faces.First() is { } blockFacing &&
            !world.BlockAccessor.GetBlock(pos.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index])
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos,
        Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

        if (this.api is ICoreClientAPI clientApi &&
            this.api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEMotor entity &&
            entity.Facing != Facing.None
           )
        {

            var facing = entity.Facing;   //куда смотрит генератор
            string code = entity.Block.Code; //код блока

            if (!BlockEMotor.MeshData.TryGetValue((facing, code), out var meshData))
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

                BlockEMotor.MeshData.TryAdd((facing, code), meshData);
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

        float[] Params = MyMiniLib.GetAttributeArrayFloat(inSlot.Itemstack.Block, "params", def_Params);

        dsc.AppendLine(Lang.Get("Consumption") + ": " + Params[1] + " " + Lang.Get("W"));
        dsc.AppendLine(Lang.Get("max_speed") + ": " + Params[4] + " " + Lang.Get("rps"));
        dsc.AppendLine(Lang.Get("res_speed") + ": " + Params[5]);
        dsc.AppendLine(Lang.Get("max_torque") + ": " + Params[2]);
        dsc.AppendLine(Lang.Get("kpd") + ": " + Params[3] * 100 + " %");
        dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
    }
}
