using ElectricalProgressive.Utils;

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



}