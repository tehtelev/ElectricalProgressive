using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block;

/// <summary>
/// Наследует логику из <see cref="BlockEntityEBase"/> и добавляет логику с направлениями
/// </summary>
public abstract class BlockEntityEFacingBase : BlockEntityEBase
{
    /// <summary>
    /// Ключ для хранения направления
    /// </summary>
    public const string FacingKey = "electricalprogressive:facing";


    /// <summary>
    /// Напраление поворота устройства
    /// </summary>
    private Facing _facing = Facing.None;


    /// <summary>
    /// Направление поворота устройства для внешнего использования
    /// </summary>
    public Facing Facing
    {
        get => _facing;
        set
        {
            if (value == _facing)
                return;

            _facing = value;

            ElectricalProgressive?.Connection = GetConnection(value);
        }
    }



    /// <summary>
    /// Позволяет переопределить устанавливаемое значение _facing
    /// </summary>
    public virtual Facing GetConnection(Facing value)
    {
        return value;
    }



    /// <summary>
    /// Сохранеяет настройки _facing
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(FacingKey, SerializerUtil.Serialize(_facing));
    }


    /// <summary>
    /// Загружает настройки _facing
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        try
        {
            _facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
        }
        catch
        {
            // ignore
        }
    }
}