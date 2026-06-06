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
    // кеш мешей
    private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = [];

    private static readonly float[] DefParams = [10.0F, 100.0F, 0.5F, 0.75F, 0.5F, 0.1F, 0.05F];   //заглушка



    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshCache?.Clear();
    }



    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>() is IMechanicalPowerDevice device)
        {
            return device.Network;
        }

        return null;
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

        Facing facing;


        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        // целая ли грань, на которую ставим
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, facing))
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
        Facing facing;

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

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this, entity, selection.Face.Index);


            var blockFacing = FacingHelper.Directions(entity.Facing).First();
            var blockPos = blockSel.Position;
            var blockPos1 = blockPos.AddCopy(blockFacing);

            var beh = entity.GetBehavior<BEBehaviorMPBase>();


            if (world.BlockAccessor.GetBlock(blockPos1) is BlockMPBase block &&
                this.HasMechPowerConnectorAt(world, blockPos, blockFacing.Opposite, block)
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
            // целая ли грань еще
            if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, entity.Facing))
            {
                return;
            }

            // иначе ломаем
            world.BlockAccessor.BreakBlock(pos, null);
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
            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            // VerticesCount отличается у LOD0 и LOD2
            var cacheKey = (facing, code, sourceMesh.VerticesCount);

            if (!MeshCache.TryGetValue(cacheKey, out var meshData))
            {
                // Клонируем входящий меш (уже правильный — LOD0 или LOD2)
                meshData = sourceMesh.Clone();

                // быстро враащем обьект
                FacingRotations.ApplyRotations(meshData, facing);


                MeshCache.TryAdd(cacheKey, meshData);
            }

            sourceMesh = meshData;
        }
    }


    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock)
    {
        var entity = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEMotor;
        // блокэнтити не готов или не существует
        if (entity == null || entity.Facing == Facing.None)
        {
            return false;
        }
        var powerOutFacing = FacingHelper.Directions(entity.Facing).First();
        return face == powerOutFacing;
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

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));

        var paramss = MyMiniLib.GetAttributeArrayFloat(block, "params", DefParams);

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + paramss[1] + " " + Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:max_speed") + ": " + paramss[4] + " " + Lang.Get("electricalprogressivebasics:rps"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:res_speed") + ": " + paramss[5]);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:max_torque") + ": " + paramss[2]);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:kpd") + ": " + paramss[3] * 100 + " %");
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }



}
