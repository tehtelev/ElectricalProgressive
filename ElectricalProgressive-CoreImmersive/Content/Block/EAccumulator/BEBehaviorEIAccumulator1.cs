using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using EPImmersive.Interface;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace EPImmersive.Content.Block.EAccumulator;

public class BEBehaviorEIAccumulator1 : BlockEntityBehavior, IEImmersiveAccumulator
{
    public BEBehaviorEIAccumulator1(BlockEntity blockEntity) : base(blockEntity)
    {

        Power = MyMiniLib.GetAttributeFloat(this.Block, "power", 128.0F);
        MaxCapacity = MyMiniLib.GetAttributeInt(this.Block, "maxcapacity", 16000);
    }

    bool hasBurnout = false;
    bool prepareBurnout = false;

    public bool IsBurned => this.Block.Variant["state"] == "burned";

    /// <summary>
    /// Предыдущее значение емкости
    /// </summary>
    public float LastCapacity { get; set; }

    /// <summary>
    /// Текущая емкость (сохраняется)
    /// </summary>
    public float Capacity { get; set; }

    public const string CapacityKey = "electricalprogressive:capacity";

    public float MaxCapacity { get; set; }

    float multFromDurab = 1.0F;

    public new BlockPos Pos => this.Blockentity.Pos;

    /// <summary>
    /// Мощность батареи!
    /// </summary>
    public float Power { get; set; }



    public float GetMaxCapacity()
    {
        return MaxCapacity * multFromDurab;
    }

    public float GetCapacity()
    {
        return Capacity;
    }

    /// <summary>
    /// Задает сразу емкость аккумулятору (вызывать только при установке аккумулятора)
    /// </summary>
    /// <returns></returns>
    public void SetCapacity(float value, float multDurab = 1.0F)
    {
        multFromDurab = multDurab;

        Capacity = value > GetMaxCapacity()
            ? GetMaxCapacity()
            : value;
    }

    public void Store(float amount)
    {
        var buf = Math.Min(Math.Min(amount, Power), GetMaxCapacity() - Capacity);

        // не позволяем одним пакетом сохранить больше максимального тока.
        // В теории такого превышения и не должно случиться

        // увеличиваем емкость с учетом скорости распространения электричества
        Capacity += buf * 1.0f / ElectricalProgressive.ElectricalProgressive.speedOfElectricity;
    }

    public float Release(float amount)
    {
        var buf = Math.Min(Capacity, Math.Min(amount, Power));

        // уменьшаем емкость с учетом скорости распространения электричества
        Capacity -= buf * 1.0f / ElectricalProgressive.ElectricalProgressive.speedOfElectricity;

        // выдаем пакет c учетом тока и запасов
        return buf;
    }

    public float canStore()
    {
        return Math.Min(Power, GetMaxCapacity() - Capacity);
    }

    public float canRelease()
    {
        return Math.Min(Capacity, Power);
    }


    public float GetLastCapacity()
    {
        return this.LastCapacity;
    }


    /// <summary>
    /// Обновление блока аккумулятора
    /// </summary>
    public void Update()
    {
        if (Blockentity is not BlockEntityEIAccumulator1 entity ||
            entity.EPImmersive == null)
        {
            return;
        }

        //entity.MarkDirty();

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        var eParam = entity.EPImmersive.MainEparams();
        {
            if (!hasBurnout && eParam.burnout)
            {
                hasBurnout = true;
                entity.MarkDirty(true);
            }

            if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
            {
                prepareBurnout = true;
                entity.MarkDirty(true);
            }

            if (eParam.burnout)
                anyBurnout = true;

            if (eParam.ticksBeforeBurnout > 0)
                anyPrepareBurnout = true;
        }

        if (!anyBurnout && hasBurnout)
        {
            hasBurnout = false;
            entity.MarkDirty(true);
        }

        if (!anyPrepareBurnout && prepareBurnout)
        {
            prepareBurnout = false;
            entity.MarkDirty(true);
        }


        // Кэшируем значения вариантов блока
        var state = entity.Block.Variant["state"];
        

        // Обмен блока если нужно
        if (hasBurnout && state != "burned")
        {
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }



        LastCapacity = Capacity;
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(CapacityKey, Capacity);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Capacity = tree.GetFloat(CapacityKey);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEIAccumulator1)
            return;


        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(GetCapacity() * 100.0f / GetMaxCapacity()));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " + ((int)GetCapacity()).ToString() + "/" + ((int)GetMaxCapacity()).ToString() + " " + Lang.Get("electricalprogressivebasics:J"));

        stringBuilder.AppendLine();
    }
}