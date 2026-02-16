using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block;

public abstract class BlockEntityEIBase : BlockEntity
{
    public BEBehaviorEPImmersive? EPImmersive => GetBehavior<BEBehaviorEPImmersive>();

    public const string FacingKey = "electricalprogressive:facing";
    // Кэш для преобразований поворотов
    public Dictionary<Facing, RotationData> RotationCache = null;


    


    private Facing _facing = Facing.None;

    public Facing Facing
    {
        get => _facing;
        set
        {
            if (value == _facing)
                return;

            _facing = value;
        }
    }






    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.EPImmersive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive
    }



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(FacingKey, SerializerUtil.Serialize(this.Facing));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        try
        {
            this.Facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }
    }



    public readonly struct RotationData
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public RotationData(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

}