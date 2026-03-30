using ElectricalProgressive.Content.Block;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;


public static class LoadEProperties
{
    // Словарь для преобразования строк в BlockFacing
    public static readonly Dictionary<string, BlockFacing> Facings = new()
    {
        { "north", BlockFacing.NORTH },
        { "east", BlockFacing.EAST },
        { "south", BlockFacing.SOUTH },
        { "west", BlockFacing.WEST },
        { "up", BlockFacing.UP },
        { "down", BlockFacing.DOWN }
    };

    /// <summary>
    /// Грузим и применяем электрические параметры блока/проводника
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="block"></param>
    /// <param name="entity"></param>
    /// <param name="faceNumber"></param>
    public static void Load(Block block, dynamic entity, int faceNumber = -1, Facing facing=Facing.None)
    {
        BEBehaviorElectricalProgressive? electricity = entity.GetBehavior<BEBehaviorElectricalProgressive>();
        if (electricity == null)
        {
            return;
        }

        //задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false);
        
        // если грань не указана, то берем из массива в json
        if (faceNumber < 0)
        {
            // какие грани могут получать электричество
            var facesEProperties =
                MyMiniLib.GetAttributeArrayString(block, "facesEProperties", ["down"]);

            foreach (var Face in facesEProperties)
            {
                var face = Facings[Face];

                faceNumber = face.Index;

                facing |= FacingHelper.FromFace(face);
            }

            // сначала прокидываем соединение
            electricity.Connection = facing;

            foreach (var Face in facesEProperties)
            {
                var face = Facings[Face];

                faceNumber = face.Index;

                electricity.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), faceNumber);

            }
        }
        else
        {
            if (facing==Facing.None)
                facing=FacingHelper.FromFace(BlockFacing.ALLFACES[faceNumber]);

                // сначала прокидываем соединение
            electricity.Connection = facing;
            electricity.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), faceNumber);
            
        }

        
        
    }
}
