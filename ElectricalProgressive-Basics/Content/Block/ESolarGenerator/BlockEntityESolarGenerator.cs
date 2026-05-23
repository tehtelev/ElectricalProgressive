using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BlockEntityESolarGenerator : BlockEntityEFacingBase
{




    /// <summary>
    /// Maximum power output for solar panel
    /// </summary>
    public float Power
    {
        get
        {
            return _maxConsumption;
        }
    }

    /// <summary>
    /// КПД генератора в долях
    /// </summary>
    public float Kpd;

  
    private long _listenerId;
    private int _maxConsumption;

    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            _listenerId = this.RegisterGameTickListener(OnSunTick, 1000);
        }

        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }


    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        ElectricalProgressive?.OnReceivedServerPacket(packetid, data);
    }


    /// <summary>
    /// При ломании блока
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer byPlayer = null!)
    {
        base.OnBlockBroken(null);
    }


    public override Facing GetConnection(Facing value)
    {
        // солнечная панель должна занимать полный _facing
        return FacingHelper.FullFace(value);
    }

    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        this.ElectricalProgressive
            ?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);
    }

    /// <summary>
    /// Обработчик тика горения топлива
    /// </summary>
    /// <param name="deltatime"></param>
    private void OnSunTick(float deltatime)
    {


        var beh = GetBehavior<BEBehaviorSolarEGenerator>();
        if (beh is null)
            return;
        
        Calculate_kpd();

        bool effectivePowered = (int)Math.Min(beh.getPowerGive(), beh.getPowerOrder()) >= _maxConsumption * .05;
        if (effectivePowered && this.Block.Variant["state"] == "off")
        {
            var originalBlock = Api.World.BlockAccessor.GetBlock(Pos);
            var newBlockAL = originalBlock.CodeWithVariant("state", "on");
            var newBlock = Api.World.GetBlock(newBlockAL);
            Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
            MarkDirty();
        }
        if (!effectivePowered && this.Block.Variant["state"] == "on")
        {
            var originalBlock = Api.World.BlockAccessor.GetBlock(Pos);
            var newBlockAL = originalBlock.CodeWithVariant("state", "off");
            var newBlock = Api.World.GetBlock(newBlockAL);
            Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
            MarkDirty();
        }
    }


    /// <summary>
    /// Calculates the efficiency. We look at the sunlight the solar panel itself is exposed, the blocks above it, and the time of year.
    ///
    /// We have to clamp values below a certain light threshold because we shouldn't produce energy at night.
    /// </summary>
    private void Calculate_kpd()
    {

        var accessor = Api.World.BlockAccessor;

        /*
         * We don't want the player to be able to put panels underground. There needs to be at least 10 sunlight reaching the block
         */

        var strength = accessor.GetLightLevel(Pos, EnumLightLevelType.OnlySunLight);
        var strength2 = accessor.GetLightLevel(Pos, EnumLightLevelType.TimeOfDaySunLight);

        if (!(strength > 10 && strength2 >= 6))
        {
            Kpd = 0;
        }
        else
        {
            strength -= 6;
            strength2 -= 6;
            /*
             * We don't want to encourage the player to place blocks right on top of the solar panel or within several blocks directly above.
             * This check penalizes that behavior.
             */
            var blocksAbovePenalty = CalculateAbovePenalty(Pos);

            /*
             * The sun is less strong in the winter months. Let's add a penalty since there is no API for it.
             */
            var monthPenalty = GetMonthPenalty(Api.World.Calendar.Month);

            /*
             *  To calculate our efficiency we take our total relative strength to the daylight multiply it by the blocks above penalty.
             *  If there is no reasonable amount of sunlight reaching the block, there is no power given at all.
             *
             *  Clamp the value to 1 at most for floating point math errors.
             */
            Kpd = MathF.Min(1f, strength2 * 1.0f / strength * blocksAbovePenalty * monthPenalty);
        }
    }

    private static float GetMonthPenalty(int currentMonth)
    {
        return currentMonth switch
        {
            3 or 9 => 0.9f,
            2 or 10 => 0.75f,
            11 or 12 or 1 => 0.6f,
            4 or 8 => 1.3f,
            _ => 1.9f
        };
    }

    /**
     * Calculates a penalty for having blocks above the solar panel. The farther away the blocks are above the solar
     * panel the less penalty there is. 
     */
    private float CalculateAbovePenalty(BlockPos pos)
    {
        var accessor = Api.World.BlockAccessor;
        var penalty = 1f; // Start with full sunlight

        // Multipliers for the 5 blocks above
        float[] penalties = [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f];

        for (var i = 0; i < penalties.Length; i++)
        {
            var checkPos = pos.UpCopy(i + 1); // +1 because first block above
            var block = accessor.GetBlock(checkPos);

            if (block.BlockId != 0) // Not air
            {
                return penalties[i];
            }
        }

        return penalty;
    }


    /// <summary>
    /// При удалении блока, закрывает диалоговое окно и отключает электричество
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        var electricity = ElectricalProgressive;

        if (electricity != null)
        {
            electricity.Connection = Facing.None;
        }

        // отключаем слушатель тика горения топлива
        UnregisterGameTickListener(_listenerId);

    }


    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("electricalprogressive:kpd", Kpd);
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);


        Kpd = tree.GetFloat("electricalprogressive:kpd");
    }
}