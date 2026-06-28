using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalProgressive.Content;
using ElectricalProgressive.Net;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: ModDependency("game", "1.22.0")]
[assembly: ModInfo(
    "Electrical Progressive: Claims",
    "electricalprogressiveclaims",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Chunk claim map interface.",
    Version = "1.0.0",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
)]

namespace ElectricalProgressive;

public sealed class ElectricalProgressiveClaims : ModSystem
{
    private const string ChannelName = "EPClaims";
    private static readonly string CommandPrivilege = Privilege.chat;
    private const int DefaultRadius = 10;
    private const int MaxRadius = 32;
    private const int ProtectionLevel = 1;

    private ICoreClientAPI? clientApi;
    private ICoreServerAPI? serverApi;
    private IClientNetworkChannel? clientChannel;
    private IServerNetworkChannel? serverChannel;
    private ClaimMapDialog? dialog;

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        api.Logger.Notification("Electrical Progressive Claims client side starting.");

        clientApi = api;
        clientChannel = api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<ClaimMapRequestPacket>()
            .RegisterMessageType<ClaimChunkActionPacket>()
            .RegisterMessageType<ClaimChunksBatchActionPacket>()
            .RegisterMessageType<ClaimMapStatePacket>()
            .SetMessageHandler<ClaimMapStatePacket>(OnMapStatePacket);

        api.Logger.Notification("[EPClaims] Client channel registered with 4 message types");

        api.Input.RegisterHotKey("epclaimsopenmap", Lang.Get("electricalprogressiveclaims:open-map-hotkey"), GlKeys.P, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("epclaimsopenmap", _ =>
        {
            ToggleDialog();
            return true;
        });

        api.ChatCommands.Create("claimmap")
            .WithDescription(Lang.Get("electricalprogressiveclaims:claim-map-command-desc"))
            .RequiresPrivilege(CommandPrivilege)
            .HandleWith(OpenClaimMapCommand);
        api.ChatCommands.Create("privatemap")
            .WithDescription(Lang.Get("electricalprogressiveclaims:claim-map-command-desc"))
            .RequiresPrivilege(CommandPrivilege)
            .HandleWith(OpenClaimMapCommand);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        api.Logger.Notification("Electrical Progressive Claims server side starting.");

        serverApi = api;
        serverChannel = api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<ClaimMapRequestPacket>()
            .RegisterMessageType<ClaimChunkActionPacket>()
            .RegisterMessageType<ClaimChunksBatchActionPacket>()
            .RegisterMessageType<ClaimMapStatePacket>()
            .SetMessageHandler<ClaimMapRequestPacket>(OnMapRequest)
            .SetMessageHandler<ClaimChunkActionPacket>(OnChunkAction)
            .SetMessageHandler<ClaimChunksBatchActionPacket>(OnChunksBatchAction);

        api.Logger.Notification("[EPClaims] Server channel registered with handlers for request + action");
    }

    public override void Dispose()
    {
        dialog?.Dispose();
        dialog = null;
        clientApi = null;
        serverApi = null;
        clientChannel = null;
        serverChannel = null;
        base.Dispose();
    }

    private void ToggleDialog()
    {
        if (dialog?.IsOpened() == true)
        {
            dialog.TryClose();
            return;
        }

        OpenDialog();
    }

    private bool OpenDialog()
    {
        if (clientApi == null || clientChannel == null)
        {
            return false;
        }

        try
        {
            dialog ??= new ClaimMapDialog(clientApi, clientChannel);

            if (!dialog.IsOpened())
            {
                dialog.TryOpen();
            }
            else
            {
                dialog.RequestRefresh();
            }

            return dialog.IsOpened();
        }
        catch (Exception exception)
        {
            clientApi.Logger.Error(exception);
            clientApi.ShowChatMessage("Failed to open claim map. Check client log for details.");
            return false;
        }
    }

    private TextCommandResult OpenClaimMapCommand(TextCommandCallingArgs args)
    {
        return OpenDialog()
            ? TextCommandResult.Success("Opening claim map.", null)
            : TextCommandResult.Error("Failed to open claim map.");
    }

    private void OnMapStatePacket(ClaimMapStatePacket packet)
    {
        clientApi?.Logger.Notification(
            "[EPClaims] Received state: chunks={0} message='{1}'",
            packet.Chunks?.Count ?? 0,
            packet.Message ?? "");

        if (dialog == null || !dialog.IsOpened())
        {
            clientApi?.Logger.Warning("[EPClaims] State packet ignored because dialog is closed");
            return;
        }

        dialog.ApplyState(packet);
    }

    private void OnMapRequest(IServerPlayer fromPlayer, ClaimMapRequestPacket packet)
    {
        try
        {
            serverApi?.Logger.Notification(
                "[EPClaims] Server received map request from {0} center={1},{2} radius={3}",
                fromPlayer.PlayerName,
                packet.CenterChunkX,
                packet.CenterChunkZ,
                packet.Radius);
            SendState(fromPlayer, packet.CenterChunkX, packet.CenterChunkZ, packet.Radius, "", 0);
        }
        catch (Exception exception)
        {
            serverApi?.Logger.Error("Claim map request failed for {0}: {1}", fromPlayer.PlayerName, exception);
        }
    }

    private void OnChunkAction(IServerPlayer fromPlayer, ClaimChunkActionPacket packet)
    {
        serverApi?.Logger.Notification(
            "[EPClaims] Server received ClaimChunkActionPacket from {0} chunk={1},{2}",
            fromPlayer.PlayerName,
            packet.ChunkX,
            packet.ChunkZ);

        try
        {
            var result = ToggleChunkClaim(fromPlayer, packet.ChunkX, packet.ChunkZ);
            serverApi?.Logger.Notification(
                "[EPClaims] ToggleChunkClaim result for {0}: type={1} message='{2}'",
                fromPlayer.PlayerName,
                result.MessageType,
                result.Message);

            SendState(fromPlayer, packet.CenterChunkX, packet.CenterChunkZ, packet.Radius, result.Message, result.MessageType);
        }
        catch (Exception exception)
        {
            serverApi?.Logger.Error("[EPClaims] Claim action failed for {0}: {1}", fromPlayer.PlayerName, exception);
        }
    }

    private void OnChunksBatchAction(IServerPlayer fromPlayer, ClaimChunksBatchActionPacket packet)
    {
        serverApi?.Logger.Notification(
            "[EPClaims] Server received ClaimChunksBatchActionPacket from {0} chunks={1}",
            fromPlayer.PlayerName,
            packet.Chunks?.Count ?? 0);

        try
        {
            var result = ProcessChunksBatch(fromPlayer, packet.Chunks ?? []);
            serverApi?.Logger.Notification(
                "[EPClaims] Batch claim result for {0}: type={1} message='{2}'",
                fromPlayer.PlayerName,
                result.MessageType,
                result.Message);

            SendState(fromPlayer, packet.CenterChunkX, packet.CenterChunkZ, packet.Radius, result.Message, result.MessageType);
        }
        catch (Exception exception)
        {
            serverApi?.Logger.Error("[EPClaims] Batch claim action failed for {0}: {1}", fromPlayer.PlayerName, exception);
        }
    }

    private ClaimActionResult ProcessChunksBatch(IServerPlayer player, IReadOnlyList<ClaimChunkCoordPacket> chunks)
    {
        if (chunks.Count == 0)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-unknown"));
        }

        if (serverApi == null)
        {
            return ClaimActionResult.Error("Server API is not ready.");
        }

        if (!IsLandClaimingEnabled())
        {
            return ClaimActionResult.Error("Land claiming is disabled on this world.");
        }

        if (!CanClaimLand(player))
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-no-privilege"));
        }

        var freeChunks = new List<(int ChunkX, int ChunkZ)>();
        var ownChunks = new List<(int ChunkX, int ChunkZ)>();
        var seen = new HashSet<long>();

        foreach (var chunk in chunks)
        {
            var packed = PackChunkCoord(chunk.ChunkX, chunk.ChunkZ);
            if (!seen.Add(packed))
            {
                continue;
            }

            switch (BuildCell(player, chunk.ChunkX, chunk.ChunkZ).State)
            {
                case ClaimChunkCellState.Free:
                    freeChunks.Add((chunk.ChunkX, chunk.ChunkZ));
                    break;
                case ClaimChunkCellState.Own:
                    ownChunks.Add((chunk.ChunkX, chunk.ChunkZ));
                    break;
                case ClaimChunkCellState.Other:
                {
                    var ownerName = BuildCell(player, chunk.ChunkX, chunk.ChunkZ).OwnerName;
                    return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-owned-by-other", ownerName ?? "?"));
                }
                default:
                    return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-out-of-world"));
            }
        }

        var claimed = 0;
        var unclaimed = 0;
        string? lastError = null;

        if (freeChunks.Count > 0)
        {
            var claimResult = TryClaimFreeChunksBatch(player, freeChunks);
            if (claimResult.MessageType != 0)
            {
                return claimResult;
            }

            claimed = freeChunks.Count;
        }

        if (ownChunks.Count > 0)
        {
            var unclaimResult = TryUnclaimOwnChunksBatch(player, ownChunks);
            if (unclaimResult.MessageType != 0 && claimed == 0)
            {
                return unclaimResult;
            }

            if (unclaimResult.MessageType != 0)
            {
                lastError = unclaimResult.Message;
            }
            else
            {
                unclaimed = ownChunks.Count;
            }
        }

        if (claimed == 0 && unclaimed == 0)
        {
            return ClaimActionResult.Error(lastError ?? Lang.Get("electricalprogressiveclaims:error-unknown"));
        }

        var message = BuildBatchResultMessage(claimed, unclaimed);
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            message = $"{message} {lastError}";
        }

        return ClaimActionResult.Success(message);
    }

    private ClaimActionResult TryClaimFreeChunksBatch(IServerPlayer player, IReadOnlyList<(int ChunkX, int ChunkZ)> chunks)
    {
        if (chunks.Count == 1)
        {
            if (!TryBuildChunkArea(chunks[0].ChunkX, chunks[0].ChunkZ, out var singleArea))
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-out-of-world"));
            }

            return TryAddChunkClaim(player, singleArea);
        }

        if (TryBuildSolidSelectionRectangle(chunks, out var rectangleArea))
        {
            serverApi?.Logger.Notification(
                "[EPClaims] Claiming solid rectangle for {0}: {1},{2},{3} to {4},{5},{6}",
                player.PlayerName,
                rectangleArea.X1, rectangleArea.Y1, rectangleArea.Z1,
                rectangleArea.X2, rectangleArea.Y2, rectangleArea.Z2);
            return TryAddChunkClaim(player, rectangleArea);
        }

        return TryAddConnectedChunkAreas(player, chunks);
    }

    private ClaimActionResult TryUnclaimOwnChunksBatch(IServerPlayer player, IReadOnlyList<(int ChunkX, int ChunkZ)> chunks)
    {
        if (chunks.Count == 0)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-unknown"));
        }

        if (TryBuildSolidSelectionRectangle(chunks, out var rectangleArea))
        {
            var claim = FindIntersectingClaim(rectangleArea);
            if (claim != null && claim.OwnedByPlayerUid == player.PlayerUID)
            {
                var result = TryRemoveAreaFromClaim(claim, rectangleArea);
                if (result.MessageType == 0)
                {
                    return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-batch-unclaimed", chunks.Count));
                }
            }
        }

        foreach (var (chunkX, chunkZ) in chunks)
        {
            if (!TryBuildChunkArea(chunkX, chunkZ, out var chunkArea))
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-out-of-world"));
            }

            var claim = FindIntersectingClaim(chunkArea);
            if (claim == null || claim.OwnedByPlayerUid != player.PlayerUID)
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-cannot-remove"));
            }

            var result = TryRemoveAreaFromClaim(claim, chunkArea);
            if (result.MessageType != 0)
            {
                return result;
            }
        }

        return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-batch-unclaimed", chunks.Count));
    }

    private bool TryBuildSolidSelectionRectangle(IReadOnlyList<(int ChunkX, int ChunkZ)> chunks, out Cuboidi area)
    {
        area = null!;
        if (chunks.Count == 0)
        {
            return false;
        }

        var minChunkX = chunks[0].ChunkX;
        var maxChunkX = chunks[0].ChunkX;
        var minChunkZ = chunks[0].ChunkZ;
        var maxChunkZ = chunks[0].ChunkZ;
        var selected = new HashSet<long>(chunks.Count);

        foreach (var (chunkX, chunkZ) in chunks)
        {
            minChunkX = Math.Min(minChunkX, chunkX);
            maxChunkX = Math.Max(maxChunkX, chunkX);
            minChunkZ = Math.Min(minChunkZ, chunkZ);
            maxChunkZ = Math.Max(maxChunkZ, chunkZ);
            selected.Add(PackChunkCoord(chunkX, chunkZ));
        }

        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                if (!selected.Contains(PackChunkCoord(chunkX, chunkZ)))
                {
                    return false;
                }
            }
        }

        return TryBuildChunksBoundingArea(minChunkX, minChunkZ, maxChunkX, maxChunkZ, out area);
    }

    private bool TryBuildChunksBoundingArea(int minChunkX, int minChunkZ, int maxChunkX, int maxChunkZ, out Cuboidi area)
    {
        area = null!;
        if (!TryBuildChunkArea(minChunkX, minChunkZ, out var minCorner)
            || !TryBuildChunkArea(maxChunkX, maxChunkZ, out var maxCorner))
        {
            return false;
        }

        area = new Cuboidi(
            Math.Min(minCorner.X1, maxCorner.X1),
            Math.Min(minCorner.Y1, maxCorner.Y1),
            Math.Min(minCorner.Z1, maxCorner.Z1),
            Math.Max(minCorner.X2, maxCorner.X2),
            Math.Max(minCorner.Y2, maxCorner.Y2),
            Math.Max(minCorner.Z2, maxCorner.Z2));
        return true;
    }

    private ClaimActionResult TryAddConnectedChunkAreas(IServerPlayer player, IReadOnlyList<(int ChunkX, int ChunkZ)> chunks)
    {
        var remaining = new HashSet<long>(chunks.Count);
        var areasByChunk = new Dictionary<long, Cuboidi>(chunks.Count);

        foreach (var (chunkX, chunkZ) in chunks)
        {
            if (!TryBuildChunkArea(chunkX, chunkZ, out var area))
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-out-of-world"));
            }

            var existing = FindIntersectingClaim(area);
            if (existing != null && existing.OwnedByPlayerUid != player.PlayerUID)
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-owned-by-other", existing.LastKnownOwnerName ?? "?"));
            }

            var packed = PackChunkCoord(chunkX, chunkZ);
            remaining.Add(packed);
            areasByChunk[packed] = area;
        }

        var ownClaims = GetOwnClaims(player.PlayerUID).ToList();
        LandClaim? targetClaim = null;
        foreach (var packed in remaining)
        {
            targetClaim = FindAdjacentOwnClaim(ownClaims, areasByChunk[packed]);
            if (targetClaim != null)
            {
                break;
            }
        }

        var createdNewClaim = targetClaim == null;
        if (createdNewClaim)
        {
            var usedVolume = ownClaims.Sum(static claim => (long)claim.SizeXYZ);
            var totalVolume = remaining.Sum(packed => (long)areasByChunk[packed].SizeXYZ);
            var allowance = GetLandClaimAllowance(player);
            if (allowance > 0 && usedVolume + totalVolume > allowance)
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-allowance"));
            }

            var usedAreas = ownClaims.Sum(static claim => claim.Areas?.Count ?? 0);
            var maxAreas = GetLandClaimMaxAreas(player);
            if (maxAreas > 0 && usedAreas + 1 > maxAreas)
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-areas"));
            }

            var claimIndex = GetNextClaimIndex(player, ownClaims);
            targetClaim = LandClaim.CreateClaim(player, ProtectionLevel);
            targetClaim.Description = BuildClaimName(player, claimIndex);
        }

        while (remaining.Count > 0)
        {
            var madeProgress = false;
            foreach (var packed in remaining.ToList())
            {
                var area = areasByChunk[packed];
                if (createdNewClaim && targetClaim!.Areas!.Count == 0)
                {
                    var firstError = targetClaim.AddArea(area);
                    if (firstError != EnumClaimError.NoError)
                    {
                        return ClaimActionResult.Error(ClaimErrorText(firstError));
                    }

                    remaining.Remove(packed);
                    madeProgress = true;
                    continue;
                }

                if (TryExpandTouchingArea(targetClaim!, area))
                {
                    remaining.Remove(packed);
                    madeProgress = true;
                    continue;
                }

                if (WouldOverlapAnotherClaim(targetClaim!, area, player.PlayerUID))
                {
                    continue;
                }

                var addError = targetClaim!.AddArea(area);
                if (addError == EnumClaimError.NoError)
                {
                    remaining.Remove(packed);
                    madeProgress = true;
                }
            }

            if (!madeProgress)
            {
                break;
            }

            ConsolidateClaimAreas(targetClaim!);
        }

        if (remaining.Count > 0)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-batch-not-connected"));
        }

        if (createdNewClaim)
        {
            serverApi!.World.Claims.Add(targetClaim!);
            serverApi.Logger.Notification(
                "[EPClaims] Added connected land claim '{0}' for {1} with {2} chunk areas",
                targetClaim!.Description,
                player.PlayerName,
                chunks.Count);
        }
        else
        {
            TouchClaim(targetClaim!);
        }

        MergeTouchingOwnClaims(player, targetClaim!);
        return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-batch-claimed", chunks.Count));
    }

    private static long PackChunkCoord(int chunkX, int chunkZ)
    {
        return ((long)chunkX << 32) ^ (uint)chunkZ;
    }

    private static string BuildBatchResultMessage(int claimed, int unclaimed)
    {
        if (claimed > 0 && unclaimed > 0)
        {
            return Lang.Get("electricalprogressiveclaims:message-batch-mixed", claimed, unclaimed);
        }

        if (claimed > 0)
        {
            return Lang.Get("electricalprogressiveclaims:message-batch-claimed", claimed);
        }

        return Lang.Get("electricalprogressiveclaims:message-batch-unclaimed", unclaimed);
    }

    private void SendState(IServerPlayer player, int centerChunkX, int centerChunkZ, int radius, string message, int messageType)
    {
        if (serverApi == null || serverChannel == null)
        {
            return;
        }

        radius = Math.Clamp(radius <= 0 ? DefaultRadius : radius, 1, MaxRadius);
        var packet = BuildStatePacket(player, centerChunkX, centerChunkZ, radius, message, messageType);
        if (packet.Chunks.Count == 0)
        {
            serverApi.Logger.Warning("[EPClaims] State packet has 0 chunks, not sending to {0}", player.PlayerName);
            return;
        }

        try
        {
            serverApi.Logger.Notification(
                "[EPClaims] Sending state to {0}: {1} chunks, message='{2}'",
                player.PlayerName,
                packet.Chunks.Count,
                message);
            serverChannel.SendPacket(packet, [player]);
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error("Failed to send claim map state to {0}: {1}", player.PlayerName, exception);
        }
    }

    private ClaimMapStatePacket BuildStatePacket(IServerPlayer player, int centerChunkX, int centerChunkZ, int radius, string message, int messageType)
    {
        var sapi = serverApi!;
        var chunkSize = sapi.WorldManager.ChunkSize;
        if (chunkSize <= 0)
        {
            throw new InvalidOperationException("World chunk size is not available.");
        }

        if (!TryGetPlayerChunk(player, out var playerChunkX, out var playerChunkZ))
        {
            playerChunkX = centerChunkX;
            playerChunkZ = centerChunkZ;
        }

        var ownClaims = GetOwnClaims(player.PlayerUID).ToList();
        var packet = new ClaimMapStatePacket
        {
            CenterChunkX = centerChunkX,
            CenterChunkZ = centerChunkZ,
            PlayerChunkX = playerChunkX,
            PlayerChunkZ = playerChunkZ,
            Radius = radius,
            ChunkSize = chunkSize,
            MapSizeX = sapi.WorldManager.MapSizeX,
            MapSizeZ = sapi.WorldManager.MapSizeZ,
            UsedVolume = ownClaims.Sum(static claim => (long)claim.SizeXYZ),
            MaxVolume = GetLandClaimAllowance(player),
            UsedAreas = ownClaims.Sum(static claim => claim.Areas?.Count ?? 0),
            MaxAreas = GetLandClaimMaxAreas(player),
            Message = message ?? "",
            MessageType = messageType
        };

        for (var z = centerChunkZ - radius; z <= centerChunkZ + radius; z++)
        {
            for (var x = centerChunkX - radius; x <= centerChunkX + radius; x++)
            {
                packet.Chunks.Add(BuildCell(player, x, z));
            }
        }

        return packet;
    }

    private bool TryGetPlayerChunk(IServerPlayer player, out int chunkX, out int chunkZ)
    {
        chunkX = 0;
        chunkZ = 0;

        if (serverApi == null)
        {
            return false;
        }

        var entity = player.Entity;
        var chunkSize = serverApi.WorldManager.ChunkSize;
        if (entity == null || chunkSize <= 0)
        {
            return false;
        }

        var blockPos = entity.Pos.AsBlockPos;
        chunkX = FloorDiv(blockPos.X, chunkSize);
        chunkZ = FloorDiv(blockPos.Z, chunkSize);
        return true;
    }

    private ClaimChunkCellPacket BuildCell(IServerPlayer player, int chunkX, int chunkZ)
    {
        if (!TryBuildChunkArea(chunkX, chunkZ, out var area))
        {
            return new ClaimChunkCellPacket
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                State = ClaimChunkCellState.OutOfWorld
            };
        }

        var claim = FindIntersectingClaim(area);
        if (claim == null)
        {
            return new ClaimChunkCellPacket
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                State = ClaimChunkCellState.Free
            };
        }

        var ownerName = claim.LastKnownOwnerName ?? "";
        return new ClaimChunkCellPacket
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            State = claim.OwnedByPlayerUid == player.PlayerUID ? ClaimChunkCellState.Own : ClaimChunkCellState.Other,
            OwnerName = ownerName
        };
    }

    private ClaimActionResult ToggleChunkClaim(IServerPlayer player, int chunkX, int chunkZ)
    {
        if (serverApi == null)
        {
            return ClaimActionResult.Error("Server API is not ready.");
        }

        if (!IsLandClaimingEnabled())
        {
            return ClaimActionResult.Error("Land claiming is disabled on this world.");
        }

        if (!CanClaimLand(player))
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-no-privilege"));
        }

        if (!TryBuildChunkArea(chunkX, chunkZ, out var area))
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-out-of-world"));
        }

        var existing = FindIntersectingClaim(area);
        if (existing != null)
        {
            if (existing.OwnedByPlayerUid != player.PlayerUID)
            {
                return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-owned-by-other", existing.LastKnownOwnerName ?? "?"));
            }

            return TryRemoveChunkFromClaim(existing, area);
        }

        return TryAddChunkClaim(player, area);
    }

    private ClaimActionResult TryAddChunkClaim(IServerPlayer player, Cuboidi area)
    {
        var ownClaims = GetOwnClaims(player.PlayerUID).ToList();
        var usedVolume = ownClaims.Sum(static claim => (long)claim.SizeXYZ);
        var allowance = GetLandClaimAllowance(player);
        if (allowance > 0 && usedVolume + area.SizeXYZ > allowance)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-allowance"));
        }

        if (TryExpandExistingArea(ownClaims, area, out var expandedClaim, player.PlayerUID))
        {
            MergeTouchingOwnClaims(player, expandedClaim);
            TouchClaim(expandedClaim);
            return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-claimed"));
        }

        var adjacentClaim = FindAdjacentOwnClaim(ownClaims, area);
        if (adjacentClaim != null)
        {
            if (TryExpandTouchingArea(adjacentClaim, area))
            {
                ConsolidateClaimAreas(adjacentClaim);
                MergeTouchingOwnClaims(player, adjacentClaim);
                TouchClaim(adjacentClaim);
                return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-claimed"));
            }

            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-overlap"));
        }

        var usedAreas = ownClaims.Sum(static claim => claim.Areas?.Count ?? 0);
        var maxAreas = GetLandClaimMaxAreas(player);
        if (maxAreas > 0 && usedAreas + 1 > maxAreas)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-areas"));
        }

        var claimIndex = GetNextClaimIndex(player, ownClaims);
        var newClaim = LandClaim.CreateClaim(player, ProtectionLevel);
        newClaim.Description = BuildClaimName(player, claimIndex);
        var addError = newClaim.AddArea(area);
        if (addError != EnumClaimError.NoError)
        {
            return ClaimActionResult.Error(ClaimErrorText(addError));
        }

        serverApi!.World.Claims.Add(newClaim);
        MergeTouchingOwnClaims(player, newClaim);
        serverApi.Logger.Notification(
            "[EPClaims] Added land claim '{0}' for {1} area={2},{3},{4} to {5},{6},{7}",
            newClaim.Description,
            player.PlayerName,
            area.X1, area.Y1, area.Z1,
            area.X2, area.Y2, area.Z2);
        return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-claimed"));
    }

    private static LandClaim? FindAdjacentOwnClaim(IEnumerable<LandClaim> ownClaims, Cuboidi area)
    {
        foreach (var claim in ownClaims)
        {
            if (claim.Areas == null)
            {
                continue;
            }

            foreach (var existing in claim.Areas)
            {
                if (AreAdjacent(existing, area))
                {
                    return claim;
                }
            }
        }

        return null;
    }

    private void MergeTouchingOwnClaims(IServerPlayer player, LandClaim anchorClaim)
    {
        if (anchorClaim.Areas == null || anchorClaim.Areas.Count == 0)
        {
            return;
        }

        var mergedAny = true;
        while (mergedAny)
        {
            mergedAny = false;
            foreach (var otherClaim in GetOwnClaims(player.PlayerUID).ToList())
            {
                if (ReferenceEquals(otherClaim, anchorClaim) || !ClaimsTouch(anchorClaim, otherClaim))
                {
                    continue;
                }

                AbsorbClaimInto(player, anchorClaim, otherClaim);
                mergedAny = true;
            }
        }

        ConsolidateClaimAreas(anchorClaim);
    }

    private void AbsorbClaimInto(IServerPlayer player, LandClaim primary, LandClaim other)
    {
        if (other.Areas == null || primary.Areas == null)
        {
            return;
        }

        var primaryIndex = TryParseClaimIndex(primary.Description, player.PlayerName);
        var otherIndex = TryParseClaimIndex(other.Description, player.PlayerName);
        if (otherIndex > 0 && (primaryIndex == 0 || otherIndex < primaryIndex))
        {
            primary.Description = BuildClaimName(player, otherIndex);
        }

        foreach (var otherArea in other.Areas.ToList())
        {
            if (TryExpandExistingArea(new[] { primary }, otherArea, out _, player.PlayerUID)
                || TryExpandTouchingArea(primary, otherArea))
            {
                continue;
            }

            if (primary.Areas.Any(existing => existing.Equals(otherArea) || existing.Intersects(otherArea)))
            {
                continue;
            }

            primary.AddArea(otherArea);
        }

        serverApi!.World.Claims.Remove(other);
        serverApi.Logger.Notification(
            "[EPClaims] Merged claim '{0}' into '{1}' for {2}",
            other.Description,
            primary.Description,
            player.PlayerName);
    }

    private void ConsolidateClaimAreas(LandClaim claim)
    {
        if (claim.Areas == null || claim.Areas.Count <= 1)
        {
            return;
        }

        var mergedAny = true;
        while (mergedAny)
        {
            mergedAny = false;
            for (var i = 0; i < claim.Areas.Count; i++)
            {
                for (var j = i + 1; j < claim.Areas.Count; j++)
                {
                    var first = claim.Areas[i];
                    var second = claim.Areas[j];
                    if (!TryCreateExpandedArea(first, second, out var expandedArea)
                        && !TryCreateExpandedArea(second, first, out expandedArea))
                    {
                        continue;
                    }

                    first.Set(expandedArea.X1, expandedArea.Y1, expandedArea.Z1, expandedArea.X2, expandedArea.Y2, expandedArea.Z2);
                    claim.Areas.RemoveAt(j);
                    mergedAny = true;
                    break;
                }

                if (mergedAny)
                {
                    break;
                }
            }
        }
    }

    private static bool ClaimsTouch(LandClaim first, LandClaim second)
    {
        if (first.Areas == null || second.Areas == null)
        {
            return false;
        }

        foreach (var firstArea in first.Areas)
        {
            foreach (var secondArea in second.Areas)
            {
                if (firstArea.Intersects(secondArea) || AreAdjacent(firstArea, secondArea))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildClaimName(IServerPlayer player, int index)
    {
        return $"{player.PlayerName} {index}";
    }

    private static int GetNextClaimIndex(IServerPlayer player, IEnumerable<LandClaim> ownClaims)
    {
        var maxIndex = 0;
        foreach (var claim in ownClaims)
        {
            maxIndex = Math.Max(maxIndex, TryParseClaimIndex(claim.Description, player.PlayerName));
        }

        return maxIndex + 1;
    }

    private static int TryParseClaimIndex(string? description, string playerName)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        var prefix = playerName + " ";
        if (!description.StartsWith(prefix, StringComparison.Ordinal))
        {
            return 0;
        }

        return int.TryParse(description.AsSpan(prefix.Length), out var index) ? index : 0;
    }

    private bool IsLandClaimingEnabled()
    {
        return serverApi?.World.Config.GetAsBool("allowLandClaiming", true) != false;
    }

    private ClaimActionResult TryRemoveAreaFromClaim(LandClaim claim, Cuboidi removeArea)
    {
        if (claim.Areas == null)
        {
            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-cannot-remove"));
        }

        for (var i = 0; i < claim.Areas.Count; i++)
        {
            var area = claim.Areas[i];
            if (!area.Intersects(removeArea))
            {
                continue;
            }

            if (area.Equals(removeArea))
            {
                claim.Areas.RemoveAt(i);
                if (claim.Areas.Count == 0)
                {
                    serverApi!.World.Claims.Remove(claim);
                }
                else
                {
                    ConsolidateClaimAreas(claim);
                    TouchClaim(claim);
                }

                return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-unclaimed"));
            }

            if (TryShrinkArea(area, removeArea))
            {
                ConsolidateClaimAreas(claim);
                TouchClaim(claim);
                return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-unclaimed"));
            }

            if (TrySubtractAreaFromArea(area, removeArea, out var remainingPieces))
            {
                claim.Areas.RemoveAt(i);
                foreach (var piece in remainingPieces)
                {
                    claim.Areas.Add(piece);
                }

                if (claim.Areas.Count == 0)
                {
                    serverApi!.World.Claims.Remove(claim);
                }
                else
                {
                    ConsolidateClaimAreas(claim);
                    TouchClaim(claim);
                }

                return ClaimActionResult.Success(Lang.Get("electricalprogressiveclaims:message-unclaimed"));
            }

            return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-cannot-remove"));
        }

        return ClaimActionResult.Error(Lang.Get("electricalprogressiveclaims:error-cannot-remove"));
    }

    private ClaimActionResult TryRemoveChunkFromClaim(LandClaim claim, Cuboidi chunkArea)
    {
        return TryRemoveAreaFromClaim(claim, chunkArea);
    }

    private static bool TrySubtractAreaFromArea(Cuboidi area, Cuboidi removeArea, out List<Cuboidi> remainingPieces)
    {
        remainingPieces = [];

        if (!area.Intersects(removeArea))
        {
            return false;
        }

        if (area.Y1 != removeArea.Y1 || area.Y2 != removeArea.Y2)
        {
            return false;
        }

        if (removeArea.X1 < area.X1 || removeArea.X2 > area.X2 || removeArea.Z1 < area.Z1 || removeArea.Z2 > area.Z2)
        {
            return false;
        }

        if (removeArea.X1 > area.X1)
        {
            remainingPieces.Add(new Cuboidi(area.X1, area.Y1, area.Z1, removeArea.X1 - 1, area.Y2, area.Z2));
        }

        if (removeArea.X2 < area.X2)
        {
            remainingPieces.Add(new Cuboidi(removeArea.X2 + 1, area.Y1, area.Z1, area.X2, area.Y2, area.Z2));
        }

        var overlapX1 = Math.Max(area.X1, removeArea.X1);
        var overlapX2 = Math.Min(area.X2, removeArea.X2);

        if (removeArea.Z1 > area.Z1)
        {
            remainingPieces.Add(new Cuboidi(overlapX1, area.Y1, area.Z1, overlapX2, area.Y2, removeArea.Z1 - 1));
        }

        if (removeArea.Z2 < area.Z2)
        {
            remainingPieces.Add(new Cuboidi(overlapX1, area.Y1, removeArea.Z2 + 1, overlapX2, area.Y2, area.Z2));
        }

        remainingPieces.RemoveAll(static piece => piece.X1 > piece.X2 || piece.Z1 > piece.Z2);
        return true;
    }

    private bool TryExpandExistingArea(IEnumerable<LandClaim> ownClaims, Cuboidi chunkArea, out LandClaim expandedClaim, string? ownerPlayerUid = null)
    {
        foreach (var claim in ownClaims)
        {
            if (TryExpandTouchingArea(claim, chunkArea, out var expandedArea, out var expandedExisting)
                && !WouldOverlapAnotherAreaInSameClaim(claim, expandedExisting, expandedArea, chunkArea)
                && !WouldOverlapAnotherClaim(claim, expandedArea, ownerPlayerUid))
            {
                expandedExisting.Set(expandedArea.X1, expandedArea.Y1, expandedArea.Z1, expandedArea.X2, expandedArea.Y2, expandedArea.Z2);
                ConsolidateClaimAreas(claim);
                expandedClaim = claim;
                return true;
            }
        }

        expandedClaim = null!;
        return false;
    }

    private static bool TryExpandTouchingArea(LandClaim claim, Cuboidi chunkArea)
    {
        if (!TryExpandTouchingArea(claim, chunkArea, out var expandedArea, out var existing))
        {
            return false;
        }

        existing.Set(expandedArea.X1, expandedArea.Y1, expandedArea.Z1, expandedArea.X2, expandedArea.Y2, expandedArea.Z2);
        return true;
    }

    private static bool TryExpandTouchingArea(LandClaim claim, Cuboidi chunkArea, out Cuboidi expandedArea, out Cuboidi expandedExisting)
    {
        expandedArea = null!;
        expandedExisting = null!;

        if (claim.Areas == null)
        {
            return false;
        }

        foreach (var existing in claim.Areas)
        {
            if (!TryCreateExpandedArea(existing, chunkArea, out var candidate))
            {
                continue;
            }

            expandedArea = candidate;
            expandedExisting = existing;
            return true;
        }

        return false;
    }

    private static bool TryCreateExpandedArea(Cuboidi existing, Cuboidi chunkArea, out Cuboidi expanded)
    {
        expanded = null!;

        if (existing.Y1 != chunkArea.Y1 || existing.Y2 != chunkArea.Y2)
        {
            return false;
        }

        if (existing.Z1 == chunkArea.Z1 && existing.Z2 == chunkArea.Z2)
        {
            if (existing.X2 + 1 == chunkArea.X1)
            {
                expanded = new Cuboidi(existing.X1, existing.Y1, existing.Z1, chunkArea.X2, existing.Y2, existing.Z2);
                return true;
            }

            if (chunkArea.X2 + 1 == existing.X1)
            {
                expanded = new Cuboidi(chunkArea.X1, existing.Y1, existing.Z1, existing.X2, existing.Y2, existing.Z2);
                return true;
            }
        }

        if (existing.X1 == chunkArea.X1 && existing.X2 == chunkArea.X2)
        {
            if (existing.Z2 + 1 == chunkArea.Z1)
            {
                expanded = new Cuboidi(existing.X1, existing.Y1, existing.Z1, existing.X2, existing.Y2, chunkArea.Z2);
                return true;
            }

            if (chunkArea.Z2 + 1 == existing.Z1)
            {
                expanded = new Cuboidi(existing.X1, existing.Y1, chunkArea.Z1, existing.X2, existing.Y2, existing.Z2);
                return true;
            }
        }

        return false;
    }

    private bool TryShrinkArea(Cuboidi existing, Cuboidi chunkArea)
    {
        if (existing.Y1 != chunkArea.Y1 || existing.Y2 != chunkArea.Y2)
        {
            return false;
        }

        if (existing.Z1 == chunkArea.Z1 && existing.Z2 == chunkArea.Z2)
        {
            if (existing.X1 == chunkArea.X1 && existing.X2 > chunkArea.X2)
            {
                existing.X1 = chunkArea.X2 + 1;
                return true;
            }

            if (existing.X2 == chunkArea.X2 && existing.X1 < chunkArea.X1)
            {
                existing.X2 = chunkArea.X1 - 1;
                return true;
            }
        }

        if (existing.X1 == chunkArea.X1 && existing.X2 == chunkArea.X2)
        {
            if (existing.Z1 == chunkArea.Z1 && existing.Z2 > chunkArea.Z2)
            {
                existing.Z1 = chunkArea.Z2 + 1;
                return true;
            }

            if (existing.Z2 == chunkArea.Z2 && existing.Z1 < chunkArea.Z1)
            {
                existing.Z2 = chunkArea.Z1 - 1;
                return true;
            }
        }

        return false;
    }

    private bool WouldOverlapAnotherClaim(LandClaim ownClaim, Cuboidi area, string? ownerPlayerUid = null)
    {
        foreach (var claim in serverApi!.World.Claims.All)
        {
            if (ReferenceEquals(claim, ownClaim))
            {
                continue;
            }

            if (ownerPlayerUid != null && claim.OwnedByPlayerUid == ownerPlayerUid)
            {
                continue;
            }

            if (claim.Intersects(area))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WouldOverlapAnotherAreaInSameClaim(LandClaim claim, Cuboidi originalArea, Cuboidi expandedArea, Cuboidi chunkArea)
    {
        if (claim.Areas == null)
        {
            return false;
        }

        foreach (var area in claim.Areas)
        {
            if (ReferenceEquals(area, originalArea) || !area.Intersects(expandedArea))
            {
                continue;
            }

            if (AreAdjacent(area, chunkArea) || AreAdjacent(area, originalArea))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool AreAdjacent(Cuboidi first, Cuboidi second)
    {
        if (first.Y1 != second.Y1 || first.Y2 != second.Y2)
        {
            return false;
        }

        var touchesX = first.X2 + 1 == second.X1 || second.X2 + 1 == first.X1;
        var overlapsZ = first.Z1 <= second.Z2 && second.Z1 <= first.Z2;
        if (touchesX && overlapsZ)
        {
            return true;
        }

        var touchesZ = first.Z2 + 1 == second.Z1 || second.Z2 + 1 == first.Z1;
        var overlapsX = first.X1 <= second.X2 && second.X1 <= first.X2;
        return touchesZ && overlapsX;
    }

    private void TouchClaim(LandClaim claim)
    {
        var claims = serverApi!.World.Claims;
        if (claims.Remove(claim))
        {
            claims.Add(claim);
        }
    }

    private IEnumerable<LandClaim> GetOwnClaims(string playerUid)
    {
        var claims = serverApi?.World.Claims?.All;
        if (claims == null)
        {
            return [];
        }

        return claims.Where(claim => claim.OwnedByPlayerUid == playerUid);
    }

    private LandClaim? FindIntersectingClaim(Cuboidi area)
    {
        var claims = serverApi?.World.Claims?.All;
        if (claims == null)
        {
            return null;
        }

        foreach (var claim in claims)
        {
            try
            {
                if (claim.Intersects(area))
                {
                    return claim;
                }
            }
            catch (Exception exception)
            {
                serverApi?.Logger.Warning("Skipped invalid land claim while building map: {0}", exception.Message);
            }
        }

        return null;
    }

    private bool TryBuildChunkArea(int chunkX, int chunkZ, out Cuboidi area)
    {
        var sapi = serverApi!;
        var chunkSize = sapi.WorldManager.ChunkSize;
        if (chunkSize <= 0)
        {
            area = null!;
            return false;
        }

        long x1 = (long)chunkX * chunkSize;
        long z1 = (long)chunkZ * chunkSize;
        if (x1 < 0 || z1 < 0 || x1 > int.MaxValue || z1 > int.MaxValue)
        {
            area = null!;
            return false;
        }

        var mapSizeX = sapi.WorldManager.MapSizeX;
        var mapSizeZ = sapi.WorldManager.MapSizeZ;
        var mapSizeY = sapi.WorldManager.MapSizeY;
        if (mapSizeX <= 0 || mapSizeZ <= 0 || mapSizeY <= 0)
        {
            area = null!;
            return false;
        }

        var ix1 = (int)x1;
        var iz1 = (int)z1;
        if (ix1 >= mapSizeX || iz1 >= mapSizeZ)
        {
            area = null!;
            return false;
        }

        var x2 = Math.Min(ix1 + chunkSize - 1, mapSizeX - 1);
        var z2 = Math.Min(iz1 + chunkSize - 1, mapSizeZ - 1);
        area = new Cuboidi(ix1, 0, iz1, x2, mapSizeY - 1, z2);
        return true;
    }

    private bool CanClaimLand(IServerPlayer player)
    {
        return player.HasPrivilege(Privilege.claimland)
            || player.HasPrivilege(Privilege.controlserver)
            || serverApi?.Server?.IsDedicated == false;
    }

    private static long GetLandClaimAllowance(IServerPlayer player)
    {
        var roleAllowance = player.Role?.LandClaimAllowance ?? 0;
        var extraAllowance = player.ServerData?.ExtraLandClaimAllowance ?? 0;
        return (long)roleAllowance + extraAllowance;
    }

    private static int GetLandClaimMaxAreas(IServerPlayer player)
    {
        var roleAreas = player.Role?.LandClaimMaxAreas ?? 0;
        var extraAreas = player.ServerData?.ExtraLandClaimAreas ?? 0;
        return roleAreas + extraAreas;
    }

    private static int FloorDiv(int value, int divisor)
    {
        return (int)Math.Floor((double)value / divisor);
    }

    private static string ClaimErrorText(EnumClaimError error)
    {
        return error switch
        {
            EnumClaimError.NotAdjacent => Lang.Get("electricalprogressiveclaims:error-not-adjacent"),
            EnumClaimError.Overlapping => Lang.Get("electricalprogressiveclaims:error-overlap"),
            _ => Lang.Get("electricalprogressiveclaims:error-unknown")
        };
    }

    private readonly struct ClaimActionResult
    {
        public readonly string Message;
        public readonly int MessageType;

        private ClaimActionResult(string message, int messageType)
        {
            Message = message;
            MessageType = messageType;
        }

        public static ClaimActionResult Success(string message) => new(message, 0);
        public static ClaimActionResult Error(string message) => new(message, 1);
    }
}
