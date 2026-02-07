using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHotSpringsGenerator;

public class BlockEntityEHotSpringsGenerator : BlockEntityEFacingBase
{
    private Facing _facing = Facing.None;



    /// <summary>
    /// Maximum power output for hot springs generator
    /// </summary>
    public float Power
    {
        get
        {
            return _maxConsumption;
        }
    }

    /// <summary>
    /// Generator efficiency as a fraction
    /// </summary>
    public float Kpd;

    /// <summary>
    /// Error message to display in tooltip, empty if no error
    /// </summary>
    public string ErrorMessage = "";
    private long _listenerId;
    private int _maxConsumption;

    /// <summary>
    /// Block initialization
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            _listenerId = this.RegisterGameTickListener(OnHotSpringsTick, 5000);
        }

        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption");
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
    /// When the block is broken
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer byPlayer = null!)
    {
        base.OnBlockBroken(null);
    }


    /// <summary>
    /// Called when the block is unloaded
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        this.ElectricalProgressive
            ?.OnBlockUnloaded(); // call OnBlockUnloaded on BEBehaviorElectricalProgressive

        // unregister the tick listener
        UnregisterGameTickListener(_listenerId);
    }

    /// <summary>
    /// Tick handler for hot springs generation
    /// </summary>
    /// <param name="deltatime"></param>
    private void OnHotSpringsTick(float deltatime)
    {
        var beh = GetBehavior<BEBehaviorHotSpringsEGenerator>();
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
    /// Calculates the efficiency based on heat exchangers and nearby generators.
    /// Requires boiling water on all four horizontal sides to function.
    /// Heat exchangers provide base efficiency: 4=100%, 3=75%, 2=50%, 1=25%, 0=10%.
    /// Nearby generators apply a penalty multiplier after the heat exchanger calculation.
    /// </summary>
    private void Calculate_kpd()
    {
        var accessor = Api.World.BlockAccessor;

        bool IsBoilingWater(BlockPos pos) =>
            accessor.GetBlock(pos, BlockLayersAccess.Fluid).Code?.Path.StartsWith("boilingwater") == true;

        bool surrounded = IsBoilingWater(Pos.NorthCopy())
            && IsBoilingWater(Pos.SouthCopy())
            && IsBoilingWater(Pos.EastCopy())
            && IsBoilingWater(Pos.WestCopy());

        if (!surrounded)
        {
            Kpd = 0;
            ErrorMessage = "electricalprogressiveqol:hotsprings-not-surrounded";
            return;
        }

        // Base efficiency from heat exchangers: 0=5%, scales linearly to 7=100%
        // Formula: 5% + (count/7 * 95%), capped at 7
        float baseKpd = 0.05f + (CountAdjacentHeatExchangers() * 0.95f / 7f);

        // Count nearby generators within 8 blocks
        int nearbyGenerators = CountNearbyGenerators(8);

        // Apply nearby generator penalty multiplier
        float nearbyPenalty = nearbyGenerators switch
        {
            0 => 1f,
            1 => 0.5f,
            2 => 0.25f,
            3 => 0.1f,
            _ => 0.05f
        };

        Kpd = baseKpd * nearbyPenalty;

        // Set error message based on the most significant issue
        if (nearbyGenerators > 0)
        {
            ErrorMessage = "electricalprogressiveqol:hotsprings-nearby-penalty";
        }
        else // Clears message in case there was one from before
        {
            ErrorMessage = "";
        }
    }

    /// <summary>
    /// Counts other hot springs generators within the specified range that are actively producing power.
    /// </summary>
    private int CountNearbyGenerators(int range)
    {
        var accessor = Api.World.BlockAccessor;
        int count = 0;

        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                for (int z = -range; z <= range; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue; // Skip self

                    var checkPos = Pos.AddCopy(x, y, z);
                    var blockEntity = accessor.GetBlockEntity(checkPos);

                    if (blockEntity is BlockEntityEHotSpringsGenerator generator && generator.Kpd > 0)
                        count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Counts heat exchangers adjacent to this generator (all 8 horizontal directions).
    /// </summary>
    private int CountAdjacentHeatExchangers()
    {
        var accessor = Api.World.BlockAccessor;
        int count = 0;

        // Check all 8 horizontal directions (N, S, E, W, NE, NW, SE, SW)
        BlockPos[] adjacentPositions =
        [
            Pos.NorthCopy(),
            Pos.SouthCopy(),
            Pos.EastCopy(),
            Pos.WestCopy(),
            Pos.NorthCopy().EastCopy(),  // NE
            Pos.NorthCopy().WestCopy(),  // NW
            Pos.SouthCopy().EastCopy(),  // SE
            Pos.SouthCopy().WestCopy()   // SW
        ];

        foreach (var adjPos in adjacentPositions)
        {
            var block = accessor.GetBlock(adjPos);
            if (block?.Code?.Path == "eheatexchanger")
            {
                count++;
            }
        }

        return count;
    }


    /// <summary>
    /// When the block is removed, close the dialog and disconnect electricity
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        var electricity = ElectricalProgressive;

        if (electricity != null)
        {
            electricity.Connection = Facing.None;
        }

        // unregister the tick listener
        UnregisterGameTickListener(_listenerId);

    }


    /// <summary>
    /// Save attributes
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBytes("electricalprogressive:facing", SerializerUtil.Serialize(this._facing));
        tree.SetFloat("electricalprogressive:kpd", Kpd);
        tree.SetString("electricalprogressive:errorMessage", ErrorMessage);
    }


    /// <summary>
    /// Load attributes
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        try
        {
            this._facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes("electricalprogressive:facing"));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }

        Kpd = tree.GetFloat("electricalprogressive:kpd");
        ErrorMessage = tree.GetString("electricalprogressive:errorMessage", "");
    }
}
