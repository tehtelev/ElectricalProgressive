using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block;

/// <summary>
/// Наследует логику из <see cref="BlockEntityEBase"/> и добавляет логику с направлениями
/// </summary>
public abstract class BlockEntityEFacingBase : BlockEntityEBase
{
    private Facing _facing = Facing.None;

    public Facing Facing
    {
        get => _facing;
        set
        {
            if (value == _facing)
                return;

            _facing = value;
            if (ElectricalProgressive != null)
                ElectricalProgressive.Connection = GetConnection(value);
        }
    }

    public const string FacingKey = "electricalprogressive:facing";

    /// <summary>
    /// Позволяет переопределить устанавливаемое в <see cref="Facing"/> значение
    /// </summary>
    public virtual Facing GetConnection(Facing value)
    {
        return value;
    }




    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(FacingKey, SerializerUtil.Serialize(_facing));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        try
        {
            _facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
        }
        catch
        {
            // ignored
        }
    }
}