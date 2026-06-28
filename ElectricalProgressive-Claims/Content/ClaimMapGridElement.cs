using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using ElectricalProgressive.Net;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content;

public sealed class ClaimMapGridElement : GuiElement
{
    private const int FixedVisibleChunks = 16;
    private const int MaxVisibleRequestRadius = 16;
    private const double MapBorderThickness = 6;
    private static readonly Vec4f White = new(1, 1, 1, 1);

    private readonly Action<IReadOnlyList<(int ChunkX, int ChunkZ)>> onChunksSelected;
    private readonly Action onViewChanged;
    private readonly GuiElementMap? worldMapElement;
    private readonly GuiDialogWorldMap? worldMapHost;
    private readonly IClientNetworkChannel? worldMapChannel;
    private readonly HashSet<long> selectedChunks = [];
    private ClaimMapStatePacket? state;
    private int textureId;
    private int hoverChunkX = int.MinValue;
    private int hoverChunkZ = int.MinValue;
    private bool overlayDirty = true;
    private bool isSelecting;
    private bool isPanning;
    private bool panMoved;
    private double panStartMouseX;
    private double panStartMouseY;
    private double panViewCenterX;
    private double panViewCenterZ;
    private double panViewHalfWidth;
    private double panViewHalfLength;
    private long lastDynamicRedrawMs;
    private long lastMapLoadCheckMs;
    private long lastViewChangedCallbackMs;

    public ClaimMapGridElement(
        ICoreClientAPI capi,
        ElementBounds bounds,
        Action<IReadOnlyList<(int ChunkX, int ChunkZ)>> onChunksSelected,
        Action onViewChanged)
        : base(capi, bounds)
    {
        this.onChunksSelected = onChunksSelected;
        this.onViewChanged = onViewChanged;
        MouseOverCursor = "pointer";

        var worldMapManager = capi.ModLoader.GetModSystem<WorldMapManager>();
        if (worldMapManager == null || capi.World.Player?.Entity == null)
        {
            return;
        }

        GuiDialogWorldMap? host = null;
        try
        {
            worldMapChannel = capi.Network.GetChannel("worldmap");
            host = worldMapManager.worldMapDlg ?? CreateWorldMapHost(capi, worldMapManager);
            worldMapHost = host;
            worldMapElement = new GuiElementMap(worldMapManager.MapLayers, capi, host, bounds, false)
            {
                viewChanged = OnViewChanged,
                viewChangedSync = OnViewChangedSync
            };
        }
        catch (Exception exception)
        {
            capi.Logger.Warning("Claim map world map init failed, showing grid only: {0}", exception.Message);
            if (host != null && worldMapManager.worldMapDlg != host)
            {
                host.Dispose();
            }
        }
    }

    private static GuiDialogWorldMap CreateWorldMapHost(ICoreClientAPI capi, WorldMapManager worldMapManager)
    {
        var tabNames = GetWorldMapTabNames(worldMapManager);
        return new GuiDialogWorldMap(OnViewChangedStub, OnViewChangedSyncStub, capi, tabNames);
    }

    private static void OnViewChangedStub(List<FastVec2i> _, List<FastVec2i> __)
    {
    }

    private static void OnViewChangedSyncStub(int _, int __, int ___, int ____)
    {
    }

    private static List<string> GetWorldMapTabNames(WorldMapManager worldMapManager)
    {
        var tabs = new Dictionary<string, double>();

        foreach (var layer in worldMapManager.MapLayers)
        {
            if (string.IsNullOrEmpty(layer.LayerGroupCode) || tabs.ContainsKey(layer.LayerGroupCode))
            {
                continue;
            }

            if (!worldMapManager.LayerGroupPositions.TryGetValue(layer.LayerGroupCode, out var position))
            {
                position = 1;
            }

            tabs[layer.LayerGroupCode] = position;
        }

        if (tabs.Count == 0)
        {
            foreach (var entry in worldMapManager.LayerGroupPositions.OrderBy(pair => pair.Value))
            {
                tabs[entry.Key] = entry.Value;
            }
        }

        if (tabs.Count == 0)
        {
            return ["chunks"];
        }

        return tabs.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToList();
    }

    public void SetState(ClaimMapStatePacket newState)
    {
        state = newState;
        ApplyFixedMapViewSize();
        MarkOverlayDirty();
    }

    public void CenterMapOnPlayer()
    {
        var playerPos = api.World.Player?.Entity?.Pos;
        if (playerPos == null)
        {
            return;
        }

        CenterMapToWorld(playerPos.X, playerPos.Z);
        EnsureMapLoaded();
        MarkOverlayDirty();
        NotifyMapViewChanged();
    }

    public (int CenterChunkX, int CenterChunkZ, int Radius) GetVisibleRequest(int fallbackCenterChunkX, int fallbackCenterChunkZ, int fallbackRadius)
    {
        if (worldMapElement == null)
        {
            return (fallbackCenterChunkX, fallbackCenterChunkZ, fallbackRadius);
        }

        var view = worldMapElement.CurrentBlockViewBounds;
        if (view.Width <= 1 || view.Length <= 1)
        {
            return (fallbackCenterChunkX, fallbackCenterChunkZ, fallbackRadius);
        }

        var chunkSize = state?.ChunkSize > 0 ? state.ChunkSize : GlobalConstants.ChunkSize;
        var minChunkX = FloorDiv((int)Math.Floor(view.MinX), chunkSize);
        var maxChunkX = FloorDiv((int)Math.Ceiling(view.MaxX), chunkSize);
        var minChunkZ = FloorDiv((int)Math.Floor(view.MinZ), chunkSize);
        var maxChunkZ = FloorDiv((int)Math.Ceiling(view.MaxZ), chunkSize);
        var centerChunkX = FloorDiv(minChunkX + maxChunkX, 2);
        var centerChunkZ = FloorDiv(minChunkZ + maxChunkZ, 2);
        var radius = Math.Max(maxChunkX - minChunkX, maxChunkZ - minChunkZ) / 2 + 3;

        return (centerChunkX, centerChunkZ, Math.Clamp(radius, 1, MaxVisibleRequestRadius));
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surface)
    {
        worldMapElement?.ComposeElements(ctxStatic, surface);
        ApplyFixedMapViewSize();
        EnsureMapLoaded();
        RecomposeTexture();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (api.ElapsedMilliseconds - lastMapLoadCheckMs > 100)
        {
            EnsureMapLoaded();
            lastMapLoadCheckMs = api.ElapsedMilliseconds;
        }

        worldMapElement?.RenderInteractiveElements(deltaTime);

        if (overlayDirty || api.ElapsedMilliseconds - lastDynamicRedrawMs > 250)
        {
            RecomposeTexture();
            lastDynamicRedrawMs = api.ElapsedMilliseconds;
        }

        if (textureId != 0)
        {
            Render2DTexture(textureId, Bounds, 70, White);
        }
    }

    public override void PostRenderInteractiveElements(float deltaTime)
    {
        worldMapElement?.PostRenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!Bounds.PointInside(args.X, args.Y))
        {
            return;
        }

        args.Handled = true;

        if (args.Button == EnumMouseButton.Left)
        {
            if (TryGetSelectableChunkAt(args.X, args.Y, out var chunkX, out var chunkZ))
            {
                isSelecting = true;
                AddSelectedChunk(chunkX, chunkZ);
            }

            return;
        }

        if (args.Button == EnumMouseButton.Right && worldMapElement != null)
        {
            BeginPan(args.X, args.Y);
        }
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        HandleMouseUp(args);
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        HandleMouseUp(args);
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (isPanning && api.Input.MouseButton.Right)
        {
            UpdatePan(args.X, args.Y);
        }

        if (isSelecting && api.Input.MouseButton.Left)
        {
            if (TryGetSelectableChunkAt(args.X, args.Y, out var chunkX, out var chunkZ))
            {
                AddSelectedChunk(chunkX, chunkZ);
            }
        }

        var previousX = hoverChunkX;
        var previousZ = hoverChunkZ;

        if (!TryGetChunkAt(args.X, args.Y, out hoverChunkX, out hoverChunkZ))
        {
            hoverChunkX = int.MinValue;
            hoverChunkZ = int.MinValue;
        }

        if (previousX != hoverChunkX || previousZ != hoverChunkZ)
        {
            MarkOverlayDirty();
        }
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
        {
            args.SetHandled(true);
        }
    }

    public override void Dispose()
    {
        if (textureId != 0)
        {
            api.Render.GLDeleteTexture(textureId);
            textureId = 0;
        }

        worldMapElement?.Dispose();

        if (worldMapHost != null && api.ModLoader.GetModSystem<WorldMapManager>()?.worldMapDlg != worldMapHost)
        {
            worldMapHost.Dispose();
        }

        base.Dispose();
    }

    private void HandleMouseUp(MouseEvent args)
    {
        if (args.Button == EnumMouseButton.Left && isSelecting)
        {
            CommitSelection();
            isSelecting = false;
            args.Handled = true;
            return;
        }

        if (args.Button == EnumMouseButton.Right && isPanning)
        {
            EndPan();
            args.Handled = true;
        }
    }

    private void CommitSelection()
    {
        if (selectedChunks.Count == 0)
        {
            return;
        }

        var chunks = new List<(int ChunkX, int ChunkZ)>(selectedChunks.Count);
        foreach (var packed in selectedChunks)
        {
            chunks.Add((UnpackX(packed), UnpackZ(packed)));
        }

        selectedChunks.Clear();
        MarkOverlayDirty();
        onChunksSelected(chunks);
    }

    private void BeginPan(double mouseX, double mouseY)
    {
        if (worldMapElement == null)
        {
            return;
        }

        var view = worldMapElement.CurrentBlockViewBounds;
        if (view.Width <= 1 || view.Length <= 1)
        {
            return;
        }

        isPanning = true;
        panMoved = false;
        panStartMouseX = mouseX;
        panStartMouseY = mouseY;
        panViewCenterX = (view.MinX + view.MaxX) / 2;
        panViewCenterZ = (view.MinZ + view.MaxZ) / 2;
        panViewHalfWidth = view.Width / 2;
        panViewHalfLength = view.Length / 2;
    }

    private void UpdatePan(double mouseX, double mouseY)
    {
        if (worldMapElement == null)
        {
            return;
        }

        var dx = mouseX - panStartMouseX;
        var dy = mouseY - panStartMouseY;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
        {
            panMoved = true;
        }

        var zoom = Math.Max(worldMapElement.ZoomLevel, 0.0001f);
        var centerX = panViewCenterX - dx / zoom;
        var centerZ = panViewCenterZ - dy / zoom;

        worldMapElement.CurrentBlockViewBounds = new Cuboidd(
            centerX - panViewHalfWidth,
            0,
            centerZ - panViewHalfLength,
            centerX + panViewHalfWidth,
            0,
            centerZ + panViewHalfLength);
        MarkOverlayDirty();
    }

    private void EndPan()
    {
        isPanning = false;
        if (panMoved)
        {
            NotifyMapViewChanged();
        }
    }

    private void AddSelectedChunk(int chunkX, int chunkZ)
    {
        if (selectedChunks.Add(Pack(chunkX, chunkZ)))
        {
            MarkOverlayDirty();
        }
    }

    private bool TryGetSelectableChunkAt(int mouseX, int mouseY, out int chunkX, out int chunkZ)
    {
        if (!TryGetChunkAt(mouseX, mouseY, out chunkX, out chunkZ))
        {
            return false;
        }

        return TryGetChunkCell(chunkX, chunkZ, out var cell)
            && (cell.State == ClaimChunkCellState.Free || cell.State == ClaimChunkCellState.Own);
    }

    private bool TryGetChunkCell(int chunkX, int chunkZ, out ClaimChunkCellPacket cell)
    {
        cell = null!;
        if (state == null)
        {
            return false;
        }

        foreach (var chunk in state.Chunks)
        {
            if (chunk.ChunkX == chunkX && chunk.ChunkZ == chunkZ)
            {
                cell = chunk;
                return true;
            }
        }

        return false;
    }

    private bool TryGetChunkAt(int mouseX, int mouseY, out int chunkX, out int chunkZ)
    {
        chunkX = 0;
        chunkZ = 0;

        if (!Bounds.PointInside(mouseX, mouseY) || worldMapElement == null)
        {
            return false;
        }

        var worldPos = ScreenToWorld(mouseX, mouseY);
        var chunkSize = state?.ChunkSize > 0 ? state.ChunkSize : GlobalConstants.ChunkSize;
        chunkX = FloorDiv((int)Math.Floor(worldPos.X), chunkSize);
        chunkZ = FloorDiv((int)Math.Floor(worldPos.Z), chunkSize);
        return true;
    }

    private Vec3d ScreenToWorld(int mouseX, int mouseY)
    {
        if (worldMapElement == null)
        {
            return Vec3d.Zero;
        }

        var worldPos = new Vec3d();
        worldMapElement.TranslateViewPosToWorldPos(ScreenToLocal(mouseX, mouseY), ref worldPos);
        return worldPos;
    }

    private void RecomposeTexture()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0)
        {
            return;
        }

        if (textureId != 0)
        {
            api.Render.GLDeleteTexture(textureId);
            textureId = 0;
        }

        using var surface = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using var ctx = genContext(surface);
        DrawOverlay(ctx, Bounds.OuterWidth, Bounds.OuterHeight);
        generateTexture(surface, ref textureId, false);
        overlayDirty = false;
    }

    private void DrawOverlay(Context ctx, double width, double height)
    {
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        if (worldMapElement == null)
        {
            ctx.SetSourceRGB(0.075, 0.078, 0.08);
            ctx.Paint();
            DrawCenteredText(ctx, width, height, "World map unavailable");
            DrawMapBorder(ctx, width, height);
            return;
        }

        if (state == null)
        {
            DrawCenteredText(ctx, width, height, "Loading...");
            DrawMapBorder(ctx, width, height);
            return;
        }

        var cellsByCoord = new Dictionary<long, ClaimChunkCellPacket>(state.Chunks.Count);
        foreach (var chunk in state.Chunks)
        {
            cellsByCoord[Pack(chunk.ChunkX, chunk.ChunkZ)] = chunk;
        }

        foreach (var chunk in state.Chunks)
        {
            DrawChunk(ctx, chunk);
        }

        foreach (var packed in selectedChunks)
        {
            if (cellsByCoord.TryGetValue(packed, out var selectedChunk))
            {
                DrawChunkSelection(ctx, selectedChunk);
            }
        }

        foreach (var chunk in state.Chunks)
        {
            DrawChunkBorder(ctx, chunk, 0, 0, 0, 0.42, 1.0);
        }

        if (cellsByCoord.TryGetValue(Pack(state.PlayerChunkX, state.PlayerChunkZ), out var playerChunk))
        {
            DrawPlayerMarker(ctx, playerChunk);
        }

        if (!isSelecting
            && hoverChunkX != int.MinValue
            && cellsByCoord.TryGetValue(Pack(hoverChunkX, hoverChunkZ), out var hoveredChunk))
        {
            DrawChunkBorder(ctx, hoveredChunk, 1, 1, 1, 0.95, 3.0);
        }

        DrawMapBorder(ctx, width, height);
    }

    private static void DrawMapBorder(Context ctx, double width, double height)
    {
        var thickness = MapBorderThickness;
        ctx.SetSourceRGBA(0, 0, 0, 1);
        ctx.Rectangle(0, 0, width, thickness);
        ctx.Fill();
        ctx.Rectangle(0, height - thickness, width, thickness);
        ctx.Fill();
        ctx.Rectangle(0, 0, thickness, height);
        ctx.Fill();
        ctx.Rectangle(width - thickness, 0, thickness, height);
        ctx.Fill();
    }

    private void DrawChunk(Context ctx, ClaimChunkCellPacket chunk)
    {
        if (!TryGetChunkScreenRect(chunk.ChunkX, chunk.ChunkZ, out var x, out var y, out var width, out var height))
        {
            return;
        }

        SetCellColor(ctx, chunk.State);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();
    }

    private void DrawChunkSelection(Context ctx, ClaimChunkCellPacket chunk)
    {
        if (!TryGetChunkScreenRect(chunk.ChunkX, chunk.ChunkZ, out var x, out var y, out var width, out var height))
        {
            return;
        }

        ctx.SetSourceRGBA(1, 0.86, 0.2, 0.34);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();
        DrawChunkBorder(ctx, chunk, 1, 0.86, 0.2, 0.98, 2.5);
    }

    private void DrawChunkBorder(Context ctx, ClaimChunkCellPacket chunk, double r, double g, double b, double a, double lineWidth)
    {
        if (!TryGetChunkScreenRect(chunk.ChunkX, chunk.ChunkZ, out var x, out var y, out var width, out var height))
        {
            return;
        }

        ctx.SetSourceRGBA(r, g, b, a);
        ctx.LineWidth = lineWidth;
        ctx.Rectangle(x, y, width, height);
        ctx.Stroke();
    }

    private void DrawPlayerMarker(Context ctx, ClaimChunkCellPacket chunk)
    {
        if (!TryGetChunkScreenRect(chunk.ChunkX, chunk.ChunkZ, out var x, out var y, out var width, out var height))
        {
            return;
        }

        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var radius = Math.Max(3, Math.Min(width, height) * 0.18);

        ctx.SetSourceRGBA(1, 0.92, 0.2, 0.95);
        ctx.Arc(centerX, centerY, radius, 0, Math.PI * 2);
        ctx.Fill();
    }

    private bool TryGetChunkScreenRect(int chunkX, int chunkZ, out double x, out double y, out double width, out double height)
    {
        x = y = width = height = 0;
        if (worldMapElement == null || state == null)
        {
            return false;
        }

        var chunkSize = state.ChunkSize > 0 ? state.ChunkSize : GlobalConstants.ChunkSize;
        var x1 = chunkX * chunkSize;
        var z1 = chunkZ * chunkSize;
        var x2 = x1 + chunkSize;
        var z2 = z1 + chunkSize;

        var topLeft = WorldToLocal(new Vec3d(x1, 0, z1));
        var bottomRight = WorldToLocal(new Vec3d(x2, 0, z2));
        x = Math.Min(topLeft.X, bottomRight.X);
        y = Math.Min(topLeft.Y, bottomRight.Y);
        width = Math.Abs(bottomRight.X - topLeft.X);
        height = Math.Abs(bottomRight.Y - topLeft.Y);

        if (width < 1 || height < 1)
        {
            return false;
        }

        return x <= Bounds.OuterWidth && y <= Bounds.OuterHeight && x + width >= 0 && y + height >= 0;
    }

    private Vec2d WorldToLocal(Vec3d worldPos)
    {
        var viewPos = new Vec2f();
        worldMapElement?.TranslateWorldPosToViewPos(worldPos, ref viewPos);
        return new Vec2d(viewPos.X, viewPos.Y);
    }

    private Vec2f ScreenToLocal(int mouseX, int mouseY)
    {
        return new Vec2f((float)(mouseX - Bounds.renderX), (float)(mouseY - Bounds.renderY));
    }

    private void CenterMapToWorld(double x, double z)
    {
        if (worldMapElement == null)
        {
            return;
        }

        SetFixedMapView(x, z);
    }

    private void ApplyFixedMapViewSize()
    {
        if (worldMapElement == null)
        {
            return;
        }

        var view = worldMapElement.CurrentBlockViewBounds;
        if (view.Width > 1 && view.Length > 1)
        {
            SetFixedMapView((view.MinX + view.MaxX) / 2, (view.MinZ + view.MaxZ) / 2);
            return;
        }

        var playerPos = api.World.Player?.Entity?.Pos;
        if (playerPos != null)
        {
            SetFixedMapView(playerPos.X, playerPos.Z);
        }
    }

    private void SetFixedMapView(double centerX, double centerZ)
    {
        if (worldMapElement == null || Bounds.InnerWidth <= 0)
        {
            return;
        }

        var sizeInBlocks = GetChunkSize() * FixedVisibleChunks;
        var halfSize = sizeInBlocks / 2.0;

        worldMapElement.ZoomLevel = (float)(Bounds.InnerWidth / sizeInBlocks);
        worldMapElement.CurrentBlockViewBounds = new Cuboidd(
            centerX - halfSize,
            0,
            centerZ - halfSize,
            centerX + halfSize,
            0,
            centerZ + halfSize);
    }

    private int GetChunkSize()
    {
        return state?.ChunkSize > 0 ? state.ChunkSize : GlobalConstants.ChunkSize;
    }

    private static void SetCellColor(Context ctx, int cellState)
    {
        switch (cellState)
        {
            case ClaimChunkCellState.Free:
                ctx.SetSourceRGBA(0.2, 0.72, 0.38, 0.18);
                break;
            case ClaimChunkCellState.Own:
                ctx.SetSourceRGBA(0.0, 0.78, 0.92, 0.34);
                break;
            case ClaimChunkCellState.Other:
                ctx.SetSourceRGBA(0.9, 0.18, 0.14, 0.38);
                break;
            default:
                ctx.SetSourceRGBA(0.02, 0.02, 0.025, 0.58);
                break;
        }
    }

    private static void DrawCenteredText(Context ctx, double width, double height, string text)
    {
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(18);
        var extents = ctx.TextExtents(text);
        ctx.SetSourceRGBA(1, 1, 1, 0.78);
        ctx.MoveTo((width - extents.Width) / 2 - extents.XBearing, (height - extents.Height) / 2 - extents.YBearing);
        ctx.ShowText(text);
    }

    private static long Pack(int chunkX, int chunkZ)
    {
        return ((long)chunkX << 32) ^ (uint)chunkZ;
    }

    private static int UnpackX(long packed)
    {
        return (int)(packed >> 32);
    }

    private static int UnpackZ(long packed)
    {
        return (int)(packed & uint.MaxValue);
    }

    private static int FloorDiv(int value, int divisor)
    {
        return (int)Math.Floor((double)value / divisor);
    }

    private void OnViewChanged(List<FastVec2i> nowVisibleChunks, List<FastVec2i> nowHiddenChunks)
    {
        if (worldMapElement != null)
        {
            foreach (var layer in worldMapElement.mapLayers)
            {
                layer.OnViewChangedClient(nowVisibleChunks, nowHiddenChunks);
            }
        }

        MarkOverlayDirty();
        NotifyMapViewChanged();
    }

    private void OnViewChangedSync(int x1, int z1, int x2, int z2)
    {
        worldMapChannel?.SendPacket(new OnViewChangedPacket
        {
            X1 = x1,
            Z1 = z1,
            X2 = x2,
            Z2 = z2
        });

        NotifyMapViewChanged();
    }

    private void NotifyMapViewChanged()
    {
        var now = api.ElapsedMilliseconds;
        if (now - lastViewChangedCallbackMs < 250)
        {
            return;
        }

        lastViewChangedCallbackMs = now;
        onViewChanged();
    }

    private void EnsureMapLoaded()
    {
        worldMapElement?.EnsureMapFullyLoaded();
    }

    private void MarkOverlayDirty()
    {
        overlayDirty = true;
    }
}