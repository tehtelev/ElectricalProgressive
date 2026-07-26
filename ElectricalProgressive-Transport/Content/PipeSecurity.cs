using System;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content;

/// <summary>
/// Server-side guards for pipe GUI packets, filter snapshots, and automated transfers.
/// </summary>
public static class PipeSecurity
{
    public const int MinItemTransferRate = 1;
    public const int MaxItemTransferRate = 8;
    public const int MinLiquidTransferRate = 10;
    public const int MaxLiquidTransferRate = 1000;

    /// <summary>Max accepted client packet size for filter/settings trees.</summary>
    public const int MaxClientPacketBytes = 8192;

    /// <summary>Max serialized attribute size kept on a filter snapshot.</summary>
    public const int MaxFilterAttributeBytes = 2048;

    /// <summary>Max interaction distance for configuring a pipe (blocks).</summary>
    public const double MaxConfigureDistance = 7.5;

    public static int ClampItemTransferRate(int rate)
        => Math.Max(MinItemTransferRate, Math.Min(rate, MaxItemTransferRate));

    public static int ClampLiquidTransferRate(int rate)
        => Math.Max(MinLiquidTransferRate, Math.Min(rate, MaxLiquidTransferRate));

    public static bool IsValidFilterMode(int mode)
        => mode == 0 || mode == 1;

    /// <summary>
    /// Player may open/configure this pipe (claims + range). Always true on client side of VS claims.
    /// </summary>
    public static bool CanPlayerConfigure(IWorldAccessor world, IPlayer player, BlockPos pos)
    {
        if (world == null || player?.Entity == null || pos == null)
            return false;

        double dx = player.Entity.Pos.X - (pos.X + 0.5);
        double dy = player.Entity.Pos.Y - (pos.Y + 0.5);
        double dz = player.Entity.Pos.Z - (pos.Z + 0.5);
        if (dx * dx + dy * dy + dz * dz > MaxConfigureDistance * MaxConfigureDistance)
            return false;

        return world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.Use);
    }

    /// <summary>
    /// Automated pipe transfer access. Uses TestAccess (no chat spam) when owner is known.
    /// Unknown/missing owner fails open so pre-owner pipes keep working.
    /// </summary>
    public static bool MayAutomatedUse(IWorldAccessor world, string? ownerUid, BlockPos pos)
    {
        if (world?.Claims == null || pos == null)
            return true;

        // No owner stamped (legacy / missed place hook): do not block automation.
        if (string.IsNullOrEmpty(ownerUid))
            return true;

        IPlayer? owner = world.PlayerByUid(ownerUid);
        if (owner != null)
        {
            // TestAccess — no error message / MarkDirty side effects every tick.
            EnumWorldAccessResponse resp = world.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.Use);
            return resp == EnumWorldAccessResponse.Granted;
        }

        // Offline owner: claim geometry only.
        LandClaim[]? claims = world.Claims.Get(pos);
        if (claims == null || claims.Length == 0)
            return true;

        foreach (LandClaim claim in claims)
        {
            if (claim == null || claim.AllowUseEveryone)
                continue;

            if (string.Equals(claim.OwnedByPlayerUid, ownerUid, StringComparison.OrdinalIgnoreCase))
                continue;

            if (claim.PermittedPlayerUids != null
                && claim.PermittedPlayerUids.TryGetValue(ownerUid, out EnumBlockAccessFlags flags)
                && (flags & EnumBlockAccessFlags.Use) != 0)
                continue;

            return false;
        }

        return true;
    }

    public static bool MayAutomatedTransfer(IWorldAccessor world, string? ownerUid, BlockPos source, BlockPos target)
        => MayAutomatedUse(world, ownerUid, source) && MayAutomatedUse(world, ownerUid, target);

    public static bool IsPacketSizeOk(byte[]? data)
        => data != null && data.Length > 0 && data.Length <= MaxClientPacketBytes;

    /// <summary>
    /// Rebuild a filter snapshot server-side from a client-supplied stack.
    /// Drops unresolved / non-liquid (when required) stacks and caps attributes.
    /// </summary>
    public static ItemStack? SanitizeFilterSnapshot(IWorldAccessor world, ItemStack? clientStack, bool liquidsOnly)
    {
        if (world == null || clientStack == null)
            return null;

        clientStack.ResolveBlockOrItem(world);
        if (clientStack.Collectible == null || clientStack.Collectible.IsMissing || clientStack.Collectible.Code == null)
            return null;

        if (liquidsOnly)
        {
            ItemStack? portion = PipeFilterItemBrowser.TryGetLiquidPortionStack(world, clientStack);
            if (portion == null)
                return null;

            portion.StackSize = 1;
            InventoryLiquidInsertionPipe.FreezeSnapshotTemperature(world, portion);
            return StripOversizedAttributes(portion);
        }

        // Rebuild from collectible so client cannot invent a free orphan stack identity.
        var clean = new ItemStack(clientStack.Collectible, 1);
        CopyAttributesIfSafe(clientStack, clean);
        return clean;
    }

    private static void CopyAttributesIfSafe(ItemStack source, ItemStack dest)
    {
        if (source.Attributes == null || source.Attributes.Count == 0)
            return;

        try
        {
            if (source.Attributes is TreeAttribute tree)
            {
                byte[] bytes = tree.ToBytes();
                if (bytes.Length > MaxFilterAttributeBytes)
                    return;
            }

            dest.Attributes = source.Attributes.Clone() as ITreeAttribute;
        }
        catch
        {
            // drop attributes on any serialization failure
        }
    }

    private static ItemStack StripOversizedAttributes(ItemStack stack)
    {
        if (stack.Attributes is not TreeAttribute tree || tree.Count == 0)
            return stack;

        try
        {
            if (tree.ToBytes().Length > MaxFilterAttributeBytes)
                stack.Attributes = new TreeAttribute();
        }
        catch
        {
            stack.Attributes = new TreeAttribute();
        }

        return stack;
    }
}
