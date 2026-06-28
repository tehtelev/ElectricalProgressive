using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalProgressive.Net;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content;

public sealed class ClaimMapDialog : GuiDialog
{
    private const int DefaultRadius = 10;

    private readonly ICoreClientAPI clientApi;
    private readonly IClientNetworkChannel channel;
    private ClaimMapGridElement? gridElement;
    private int centerChunkX;
    private int centerChunkZ;
    private int radius = DefaultRadius;

    public override string ToggleKeyCombinationCode => "epclaimsopenmap";
    public override bool PrefersUngrabbedMouse => true;

    public ClaimMapDialog(ICoreClientAPI capi, IClientNetworkChannel channel)
        : base(capi)
    {
        clientApi = capi;
        this.channel = channel;
        CenterOnPlayer();
        ComposeDialog();
    }

    public void RequestRefresh(bool useMapView = false)
    {
        var requestCenterX = centerChunkX;
        var requestCenterZ = centerChunkZ;
        var requestRadius = radius;

        if (useMapView)
        {
            var request = gridElement?.GetVisibleRequest(centerChunkX, centerChunkZ, radius) ?? (centerChunkX, centerChunkZ, radius);
            requestCenterX = request.CenterChunkX;
            requestCenterZ = request.CenterChunkZ;
            requestRadius = request.Radius;
            centerChunkX = requestCenterX;
            centerChunkZ = requestCenterZ;
            radius = requestRadius;
        }

        clientApi.Logger.Notification(
            "[EPClaims] Sending map request center={0},{1} radius={2}",
            requestCenterX,
            requestCenterZ,
            requestRadius);

        channel.SendPacket(new ClaimMapRequestPacket
        {
            CenterChunkX = requestCenterX,
            CenterChunkZ = requestCenterZ,
            Radius = requestRadius
        });
    }

    public void ApplyState(ClaimMapStatePacket packet)
    {
        centerChunkX = packet.CenterChunkX;
        centerChunkZ = packet.CenterChunkZ;
        radius = packet.Radius;

        gridElement?.SetState(packet);
        UpdateText(packet);
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        CenterOnPlayer();
        gridElement?.CenterMapOnPlayer();
        RequestRefresh();
    }

    public override bool OnEscapePressed()
    {
        TryClose();
        return true;
    }

    private void ComposeDialog()
    {
        var mainBounds = ElementBounds.Fixed(0, 0, 760, 650);
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(mainBounds);

        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        var gridBounds = ElementBounds.Fixed(18, 72, 540, 540);
        var sidePanelBounds = ElementBounds.Fixed(571, 69, 175, 505);
        gridElement = new ClaimMapGridElement(clientApi, gridBounds, OnChunksSelected, () => RequestRefresh(useMapView: true));

        ClearComposers();
        SingleComposer = clientApi.Gui
            .CreateCompo("epclaimmap", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("electricalprogressiveclaims:claim-map-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddStaticText(Lang.Get("electricalprogressiveclaims:claim-map-coords"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(18, 40, 180, 24), "coordsLabel")
            .AddStaticText(Lang.Get("electricalprogressiveclaims:claim-map-hint"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(18, 58, 520, 18), "hintLabel")
            .AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(94, 40, 260, 24), "coordsText")
            .AddDynamicCustomDraw(sidePanelBounds, DrawSidePanelBackground, "sidePanelBg")
            .AddInteractiveElement(gridElement, "chunkGrid")
            .AddStaticText(Lang.Get("electricalprogressiveclaims:claim-map-used"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(582, 78, 150, 24), "usedLabel")
            .AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(582, 106, 150, 54), "usedText")
            .AddStaticText(Lang.Get("electricalprogressiveclaims:claim-map-legend-title"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(582, 178, 150, 24), "legendTitle")
            .AddDynamicCustomDraw(ElementBounds.Fixed(582, 210, 150, 115), DrawLegend, "legend")
            .AddDynamicText("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(582, 350, 150, 88), "messageText")
            .AddButton(Lang.Get("electricalprogressiveclaims:claim-map-center"), CenterButton, ElementBounds.Fixed(582, 536, 150, 34), EnumButtonStyle.Normal, "centerButton")
            .EndChildElements()
            .Compose();
    }

    private void OnChunksSelected(IReadOnlyList<(int ChunkX, int ChunkZ)> chunks)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var request = gridElement?.GetVisibleRequest(centerChunkX, centerChunkZ, radius) ?? (centerChunkX, centerChunkZ, radius);
        centerChunkX = request.CenterChunkX;
        centerChunkZ = request.CenterChunkZ;
        radius = request.Radius;

        SingleComposer?.GetDynamicText("messageText").SetNewText(Lang.Get("electricalprogressiveclaims:claim-map-working"));

        try
        {
            channel.SendPacket(new ClaimChunksBatchActionPacket
            {
                Chunks = chunks.Select(chunk => new ClaimChunkCoordPacket
                {
                    ChunkX = chunk.ChunkX,
                    ChunkZ = chunk.ChunkZ
                }).ToList(),
                CenterChunkX = centerChunkX,
                CenterChunkZ = centerChunkZ,
                Radius = radius
            });
            clientApi.Logger.Notification(
                "[EPClaims] Sent ClaimChunksBatchActionPacket chunks={0} center={1},{2} radius={3}",
                chunks.Count,
                centerChunkX,
                centerChunkZ,
                radius);
        }
        catch (Exception exception)
        {
            clientApi.Logger.Error("Failed to send claim batch packet: {0}", exception);
            SingleComposer?.GetDynamicText("messageText").SetNewText("Failed to send claim request.");
        }
    }

    private bool CenterButton()
    {
        CenterOnPlayer();
        gridElement?.CenterMapOnPlayer();
        RequestRefresh();
        return true;
    }

    private void CenterOnPlayer()
    {
        var player = clientApi.World.Player?.Entity;
        if (player == null)
        {
            centerChunkX = 0;
            centerChunkZ = 0;
            return;
        }

        var blockPos = player.Pos.AsBlockPos;
        var chunkSize = GlobalConstants.ChunkSize;
        centerChunkX = FloorDiv(blockPos.X, chunkSize);
        centerChunkZ = FloorDiv(blockPos.Z, chunkSize);
    }

    private void UpdateText(ClaimMapStatePacket packet)
    {
        SingleComposer?.GetDynamicText("coordsText").SetNewText($"{packet.CenterChunkX}, {packet.CenterChunkZ}");

        var maxText = packet.MaxVolume > 0 ? packet.MaxVolume.ToString() : Lang.Get("electricalprogressiveclaims:claim-map-unlimited");
        var maxAreasText = packet.MaxAreas > 0 ? packet.MaxAreas.ToString() : Lang.Get("electricalprogressiveclaims:claim-map-unlimited");
        SingleComposer?.GetDynamicText("usedText").SetNewText($"{packet.UsedVolume} / {maxText}\n{packet.UsedAreas} / {maxAreasText}");
        SingleComposer?.GetDynamicText("messageText").SetNewText(packet.Message ?? "");
    }

    private static void DrawSidePanelBackground(Cairo.Context ctx, Cairo.ImageSurface surface, ElementBounds bounds)
    {
        var width = bounds.OuterWidth;
        var height = bounds.OuterHeight;

        ctx.SetSourceRGBA(0, 0, 0, 0.96);
        ctx.Rectangle(0, 0, width, height);
        ctx.Fill();

        ctx.SetSourceRGBA(0, 0, 0, 1);
        ctx.LineWidth = 4;
        ctx.Rectangle(2, 2, width - 4, height - 4);
        ctx.Stroke();
    }

    private void DrawLegend(Cairo.Context ctx, Cairo.ImageSurface surface, ElementBounds bounds)
    {
        DrawLegendRow(ctx, 0, 0.16, 0.19, 0.17, Lang.Get("electricalprogressiveclaims:claim-map-legend-free"));
        DrawLegendRow(ctx, 28, 0.08, 0.42, 0.46, Lang.Get("electricalprogressiveclaims:claim-map-legend-own"));
        DrawLegendRow(ctx, 56, 0.55, 0.18, 0.16, Lang.Get("electricalprogressiveclaims:claim-map-legend-other"));
        DrawLegendRow(ctx, 84, 0.045, 0.047, 0.05, Lang.Get("electricalprogressiveclaims:claim-map-legend-out"));
    }

    private static void DrawLegendRow(Cairo.Context ctx, double y, double r, double g, double b, string text)
    {
        ctx.SetSourceRGB(r, g, b);
        ctx.Rectangle(0, y + 3, 18, 18);
        ctx.Fill();

        ctx.SetSourceRGBA(1, 1, 1, 0.86);
        ctx.SelectFontFace("sans-serif", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
        ctx.SetFontSize(14);
        ctx.MoveTo(28, y + 18);
        ctx.ShowText(text);
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private static int FloorDiv(int value, int divisor)
    {
        return (int)System.Math.Floor((double)value / divisor);
    }
}
