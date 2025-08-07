using ElectricalProgressive.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFence;

public class BEBehaviorEFence : BlockEntityBehavior, IElectricConductor
{
    public BEBehaviorEFence(BlockEntity blockentity) : base(blockentity)
    {

    }

    public new BlockPos Pos => Blockentity.Pos;


    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //stringBuilder.AppendLine("Заглушка");

    }

    /// <summary>
    /// Обновление блока кабеля
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void Update()
    {



    }


}

