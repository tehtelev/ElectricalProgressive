using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EAcidAccum;

public class BEBehaviorEAcidAccum : BlockEntityBehavior, IEImmersiveAccumulator
{
    public const string CapacityKey = "electricalprogressive:capacity";

    bool hasBurnout;
    bool prepareBurnout;
    float multFromDurab = 1.0F;

    public BEBehaviorEAcidAccum(BlockEntity blockEntity) : base(blockEntity)
    {
        Power = MyMiniLib.GetAttributeFloat(Block, "power", 2048.0F);
        MaxCapacity = MyMiniLib.GetAttributeInt(Block, "maxcapacity", 4096000);
    }

    public bool IsBurned => Block.Variant["state"] == "burned";
    public float LastCapacity { get; set; }
    public float Capacity { get; set; }
    public float MaxCapacity { get; set; }
    public float Power { get; set; }
    public new BlockPos Pos => Blockentity.Pos;

    private BlockEntityEAcidAccum? Entity => Blockentity as BlockEntityEAcidAccum;

    public float AcidFill
    {
        get
        {
            var entity = Entity;
            if (entity == null || entity.LiquidCapacity <= 0)
                return 0f;
            if (!InventoryEAcidAccum.IsSulfuricAcid(entity.LiquidStack))
                return 0f;
            return GameMath.Clamp(entity.LiquidAmount / entity.LiquidCapacity, 0f, 1f);
        }
    }

    public float GetMaxCapacity()
    {
        return MaxCapacity * AcidFill * multFromDurab;
    }

    public float GetCapacity() => Capacity;

    public float GetLastCapacity() => LastCapacity;

    public void SetCapacity(float value, float multDurab = 1.0F)
    {
        multFromDurab = multDurab;
        var max = GetMaxCapacity();
        Capacity = value > max ? max : value;
    }

    public void Store(float amount)
    {
        var max = GetMaxCapacity();
        if (max <= 0)
            return;
        var buf = Math.Min(Math.Min(amount, Power), max - Capacity);
        Capacity += buf * 1.0f / global::ElectricalProgressive.ElectricalProgressive.speedOfElectricity;
        if (Capacity > max)
            Capacity = max;
    }

    public float Release(float amount)
    {
        var max = GetMaxCapacity();
        if (max <= 0)
            return 0;
        var buf = Math.Min(Capacity, Math.Min(amount, Power));
        Capacity -= buf * 1.0f / global::ElectricalProgressive.ElectricalProgressive.speedOfElectricity;
        if (Capacity < 0)
            Capacity = 0;
        return buf;
    }

    public float canStore()
    {
        var max = GetMaxCapacity();
        if (max <= 0)
            return 0;
        return Math.Min(Power, max - Capacity);
    }

    public float canRelease()
    {
        if (GetMaxCapacity() <= 0)
            return 0;
        return Math.Min(Capacity, Power);
    }

    public void Update()
    {
        if (Entity is not { } entity || entity.EPImmersive == null)
            return;

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;
        var eParam = entity.EPImmersive.MainEparams();
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

        var state = entity.Block.Variant["state"];
        if (hasBurnout && state != "burned")
        {
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }

        var max = GetMaxCapacity();
        if (Capacity > max)
            Capacity = max;

        if (Math.Abs(LastCapacity - Capacity) > 0.5f)
            entity.MarkDirty();

        LastCapacity = Capacity;
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        MaxCapacity = MyMiniLib.GetAttributeInt(Block, "maxcapacity", 4096000);
        Power = MyMiniLib.GetAttributeFloat(Block, "power", 2048f);
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
        if (Entity == null || IsBurned)
            return;

        var fill = AcidFill;
        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:eacidaccum-acid") + ": " +
                                 ((int)(fill * 100)).ToString() + "% (" +
                                 Entity.LiquidAmount.ToString("0.#") + "/" +
                                 Entity.LiquidCapacity.ToString("0") + " " +
                                 Lang.Get("electricalprogressivebasics:litres") + ")");

        if (fill < 0.001f)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:eacidaccum-no-acid"));
            stringBuilder.AppendLine();
            return;
        }

        var max = GetMaxCapacity();
        stringBuilder.AppendLine(StringHelper.Progressbar(max > 0 ? GetCapacity() * 100.0f / max : 0));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " +
                                 ((int)GetCapacity()).ToString() + "/" + ((int)max).ToString() + " " +
                                 Lang.Get("electricalprogressivebasics:J"));
        stringBuilder.AppendLine();
    }
}
