using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.EAccumulator;

public class BEBehaviorEAccumulator : BlockEntityBehavior, IElectricAccumulator
{
    public BEBehaviorEAccumulator(BlockEntity blockEntity) : base(blockEntity)
    {
    }






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

    public float MaxCapacity => MyMiniLib.GetAttributeInt(this.Block, "maxcapacity", 16000);

    float multFromDurab = 1.0F;

    public new BlockPos Pos => this.Blockentity.Pos;

    /// <summary>
    /// Мощность батареи!
    /// </summary>
    public float power => MyMiniLib.GetAttributeFloat(this.Block, "power", 128.0F);



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
        var buf = Math.Min(Math.Min(amount, power), GetMaxCapacity() - Capacity);

        // не позволяем одним пакетом сохранить больше максимального тока.
        // В теории такого превышения и не должно случиться
        Capacity += buf;
    }

    public float Release(float amount)
    {
        var buf = Math.Min(Capacity, Math.Min(amount, power));
        Capacity -= buf;

        // выдаем пакет c учетом тока и запасов
        return buf;
    }

    public float canStore()
    {
        return Math.Min(power, GetMaxCapacity() - Capacity);
    }

    public float canRelease()
    {
        return Math.Min(Capacity, power);
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

        // смотрим надо ли обновить модельку когда сгорает батарея
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is BlockEntityEAccumulator
            {
                AllEparams: not null
            } entity)
        {
            bool hasBurnout = entity.AllEparams.Any(e => e.burnout);

            if (hasBurnout)
            {
                switch (entity.Block.Variant["tier"])
                {
                    case "tier1":
                        ParticleManager.SpawnBlackSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
                        break;
                    case "tier2":
                        ParticleManager.SpawnBlackSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 1.0, 0.1));
                        break;
                }
            }

            if (hasBurnout && entity.Block.Variant["state"] != "burned")
            {
                this.Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "burned")).BlockId, Pos);
            }


            bool prepareBurnout = entity.AllEparams.Any(e => e.ticksBeforeBurnout > 0);
            if (prepareBurnout)
            {
                switch (entity.Block.Variant["tier"])
                {
                    case "tier1":
                        ParticleManager.SpawnWhiteSlowSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
                        break;
                    case "tier2":
                        ParticleManager.SpawnWhiteSlowSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 1.0, 0.1));
                        break;
                }

            }
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
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEAccumulator entity)
            return;


        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(GetCapacity() * 100.0f / GetMaxCapacity()));
        stringBuilder.AppendLine("└ " + Lang.Get("Storage") + ": " + ((int)GetCapacity()).ToString() + "/" + ((int)GetMaxCapacity()).ToString() + " " + Lang.Get("J"));

        stringBuilder.AppendLine();
    }
}