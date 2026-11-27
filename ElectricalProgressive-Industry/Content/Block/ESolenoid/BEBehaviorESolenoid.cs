using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolenoid;

public class BEBehaviorESolenoid : BlockEntityBehavior, IElectricConsumer
{
    private readonly int _maxConsumption;
    private float _power;

    public BEBehaviorESolenoid(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 32);
    }

    public bool HasPower => _power * 4f >= _maxConsumption;

    public new BlockPos Pos => Blockentity.Pos;
    public float AvgConsumeCoeff { get; set; }

    public float Consume_request()
    {
        return _maxConsumption;
    }

    public void Consume_receive(float amount)
    {
        _power = (float)Math.Round(Math.Min(amount, _maxConsumption), MidpointRounding.AwayFromZero);
    }

    public float getPowerReceive()
    {
        return _power;
    }

    public float getPowerRequest()
    {
        return _maxConsumption;
    }

    public void Update()
    {
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);
        if (Blockentity is not BlockEntityESolenoid solenoid)
            return;

        var state = solenoid.Extended
            ? Lang.Get("electricalprogressiveindustry:esolenoid-out")
            : Lang.Get("electricalprogressiveindustry:esolenoid-in");
        stringBuilder.AppendLine(state);
        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + (int)_power + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));
        if (solenoid.Blocked)
            stringBuilder.AppendLine(Lang.Get("electricalprogressiveindustry:esolenoid-blocked"));
    }
}
