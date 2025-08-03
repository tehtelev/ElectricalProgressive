using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block;

public abstract class BlockEBase : Vintagestory.API.Common.Block
{
    /// <summary>
    /// Проверка на возможность установки блока
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSelection"></param>
    /// <param name="byItemStack"></param>
    /// <returns></returns>
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSelection, ItemStack byItemStack)
    {
        //if (byItemStack.Block.Variant["state"] == "burned")
        if (byItemStack.Block.Code.GetName().Contains("burned")) // пока так 
            return false;

        return base.DoPlaceBlock(world, byPlayer, blockSelection, byItemStack);
    }

    /// <summary>
    /// Кто-то или что-то коснулось блока и теперь получит урон
    /// </summary>
    /// <param name="world"></param>
    /// <param name="entity"></param>
    /// <param name="pos"></param>
    /// <param name="facing"></param>
    /// <param name="collideSpeed"></param>
    /// <param name="isImpact"></param>
    public override void OnEntityCollide(
        IWorldAccessor world,
        Entity entity,
        BlockPos pos,
        BlockFacing facing,
        Vec3d collideSpeed,
        bool isImpact
    )
    {
        // если это клиент, то не надо 
        if (world.Side == EnumAppSide.Client)
            return;

        // энтити не живой и не создание? выходим
        if (!entity.Alive || !entity.IsCreature)
            return;

        // если блокэнтити не найден, выходим
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEBase blockEntityEBase)
            return;

        // передаем работу в наш обработчик урона
        ElectricalProgressive.damageManager?.DamageEntity(world, entity, pos, facing, blockEntityEBase.AllEparams!, this);
    }
}