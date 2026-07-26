using System;
using System.Collections.Generic;
using System.IO;
using ElectricalProgressive.Content;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

/// <summary>
/// Диалог фильтра жидкостной трубы: поиск + виртуальная плитка 7x3 + настройки.
/// </summary>
public class GuiDialogLiquidInsertionPipe : GuiDialogBlockEntity
{
    public const int SetFilterStackPacketId = 2006;

    private readonly BELiquidInsertionPipe blockEntity;
    private readonly PipeFilterItemBrowser browserInventory;

    private int transferRate = 100;
    private BELiquidInsertionPipe.FilterMode filterMode = BELiquidInsertionPipe.FilterMode.AllowList;

    private float rowHeight = 50f;
    private float visibleGridHeight = 150f;

    public GuiDialogLiquidInsertionPipe(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BELiquidInsertionPipe blockEntity)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        this.blockEntity = blockEntity;
        browserInventory = PipeFilterItemBrowser.Create(capi, liquidsOnly: true);
        browserInventory.OnStackClicked = ApplyBrowserStackToFilter;

        if (IsDuplicate)
            return;

        transferRate = blockEntity.TransferRate;
        filterMode = blockEntity.CurrentFilterMode;

        if (Inventory != null)
            capi.World.Player.InventoryManager.OpenInventory(Inventory);

        SetupDialog();
    }

    private void SetupDialog()
    {
        double left = PipeFilterGuiStyle.OuterPad;
        double y = 36;
        double browserY = y + PipeFilterGuiStyle.SearchHeight + PipeFilterGuiStyle.RowGap;
        var browser = PipeFilterGuiStyle.MeasureBrowser(
            left,
            y,
            browserY,
            PipeFilterItemBrowser.Cols,
            PipeFilterItemBrowser.VisibleRows);

        double contentW = browser.ContentWidth;
        double dialogW = contentW + PipeFilterGuiStyle.OuterPad * 2;
        double right = left + contentW;

        ElementBounds searchBounds = browser.SearchBounds;
        ElementBounds searchResultsBounds = browser.SearchResultsBounds;
        ElementBounds insetBounds = browser.InsetBounds;
        ElementBounds gridBounds = browser.GridBounds;
        ElementBounds scrollbarBounds = browser.ScrollbarBounds;

        visibleGridHeight = (float)browser.GridHeight;
        rowHeight = visibleGridHeight / PipeFilterItemBrowser.VisibleRows;
        y = browserY + browser.InsetHeight + PipeFilterGuiStyle.SectionGap;

        ElementBounds settingsHeader = ElementBounds.Fixed(left, y, contentW, PipeFilterGuiStyle.LabelHeight);
        y += PipeFilterGuiStyle.LabelHeight + PipeFilterGuiStyle.RowGap;

        // Mode buttons: left and right edges of content, equal width, gap in center.
        double modeBtnW = (contentW - PipeFilterGuiStyle.ButtonGap) / 2.0;
        ElementBounds btnAllow = ElementBounds.Fixed(left, y, modeBtnW, PipeFilterGuiStyle.ButtonHeight);
        ElementBounds btnDeny = ElementBounds.Fixed(
            right - modeBtnW, y, modeBtnW, PipeFilterGuiStyle.ButtonHeight);
        y += PipeFilterGuiStyle.ButtonHeight + PipeFilterGuiStyle.SectionGap;

        ElementBounds speedHeader = ElementBounds.Fixed(left, y, contentW, PipeFilterGuiStyle.LabelHeight);
        y += PipeFilterGuiStyle.LabelHeight + PipeFilterGuiStyle.RowGap;

        // Speed: − left edge, value centered, + right edge (same column as mode buttons).
        const double rateW = 56;
        double speedBtnW = (contentW - rateW - PipeFilterGuiStyle.ButtonGap * 2) / 2.0;
        ElementBounds btnDown = ElementBounds.Fixed(left, y, speedBtnW, PipeFilterGuiStyle.ButtonHeight);
        ElementBounds rateText = ElementBounds.Fixed(
            left + speedBtnW + PipeFilterGuiStyle.ButtonGap, y + 4, rateW, PipeFilterGuiStyle.ButtonHeight);
        ElementBounds btnUp = ElementBounds.Fixed(
            right - speedBtnW, y, speedBtnW, PipeFilterGuiStyle.ButtonHeight);
        y += PipeFilterGuiStyle.ButtonHeight + PipeFilterGuiStyle.SectionGap;

        ElementBounds countText = ElementBounds.Fixed(left, y, contentW, PipeFilterGuiStyle.LabelHeight);
        y += PipeFilterGuiStyle.LabelHeight + PipeFilterGuiStyle.OuterPad;

        var dialogBounds = ElementBounds.Fixed(0, 0, dialogW, y);
        var dialogAlignment = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        ClearComposers();

        SingleComposer = capi.Gui
            .CreateCompo("liquidinsertionpipegui" + BlockEntityPosition, dialogAlignment)
            .AddShadedDialogBG(dialogBounds, true)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(dialogBounds)

            .AddTextInput(searchBounds, OnSearchTextChanged, PipeFilterGuiStyle.SearchFont(), "searchbox")
            .AddDynamicText(
                "",
                PipeFilterGuiStyle.MutedFont().WithOrientation(EnumTextOrientation.Center),
                searchResultsBounds,
                "searchResults")

            .AddInset(insetBounds, 3)
            .AddItemSlotGrid(
                browserInventory,
                _ => { },
                PipeFilterItemBrowser.Cols,
                gridBounds,
                "browserSlots")
            .AddVerticalScrollbar(OnBrowserScrollbar, scrollbarBounds, "browserScrollbar")

            .AddStaticText(
                Lang.Get("electricalprogressivetransport:liquid-filter-pipe-settings"),
                PipeFilterGuiStyle.HeaderFont().WithOrientation(EnumTextOrientation.Center),
                settingsHeader)

            .AddSmallButton(
                Lang.Get("electricalprogressivetransport:filter-mode-allow"),
                OnAllowListClicked,
                btnAllow,
                EnumButtonStyle.Normal,
                "btnAllowList")

            .AddSmallButton(
                Lang.Get("electricalprogressivetransport:filter-mode-deny"),
                OnDenyListClicked,
                btnDeny,
                EnumButtonStyle.Normal,
                "btnDenyList")

            .AddStaticText(
                Lang.Get("electricalprogressivetransport:liquid-transfer-speed"),
                PipeFilterGuiStyle.HeaderFont().WithOrientation(EnumTextOrientation.Center),
                speedHeader)

            .AddSmallButton(
                "-10",
                OnDecreaseRateClicked,
                btnDown,
                EnumButtonStyle.Normal,
                "btnDecrease")

            .AddDynamicText(
                transferRate.ToString(),
                PipeFilterGuiStyle.ValueFont(16).WithOrientation(EnumTextOrientation.Center),
                rateText,
                "txtTransferRate")

            .AddSmallButton(
                "+10",
                OnIncreaseRateClicked,
                btnUp,
                EnumButtonStyle.Normal,
                "btnIncrease")

            .AddDynamicText(
                "",
                PipeFilterGuiStyle.CountFont().WithOrientation(EnumTextOrientation.Center),
                countText,
                "filterCountText")

            .EndChildElements()
            .Compose();

        var searchBox = SingleComposer.GetTextInput("searchbox");
        searchBox?.SetPlaceHolderText(Lang.Get("electricalprogressivetransport:filter-search-placeholder"));

        UpdateFilterButtons();
        UpdateTransferRateDisplay();
        RefreshBrowser(resetScroll: true);
    }

    private void OnSearchTextChanged(string text)
    {
        browserInventory.SetSearch(text ?? "", resetScroll: true);
        UpdateScrollbarAndCounters(resetScrollPos: true);
    }

    private void RefreshBrowser(bool resetScroll)
    {
        if (Inventory is not InventoryLiquidInsertionPipe filterInv)
            return;

        var codes = new List<string>();
        for (int i = 0; i < filterInv.Count; i++)
        {
            ItemStack? snap = filterInv.GetFilterSnapshot(i);
            string code = PipeFilterItemBrowser.GetCodeKey(snap);
            if (!string.IsNullOrEmpty(code))
                codes.Add(code);
        }

        string search = SingleComposer?.GetTextInput("searchbox")?.GetText() ?? "";
        browserInventory.SetSelectedCodes(codes, resetScroll);
        browserInventory.SetSearch(search, resetScroll);
        UpdateScrollbarAndCounters(resetScrollPos: resetScroll);

        int active = 0;
        for (int i = 0; i < filterInv.Count; i++)
        {
            if (filterInv.IsFilterSet(i))
                active++;
        }

        SingleComposer?.GetDynamicText("filterCountText")?.SetNewText(
            Lang.Get("electricalprogressivetransport:active-liquid-filters", active, filterInv.Count));
    }

    private void UpdateScrollbarAndCounters(bool resetScrollPos)
    {
        var scrollbar = SingleComposer?.GetScrollbar("browserScrollbar");
        var resultsText = SingleComposer?.GetDynamicText("searchResults");

        float totalH = browserInventory.GetScrollTotalHeight(visibleGridHeight, rowHeight);
        scrollbar?.SetHeights(visibleGridHeight, totalH);
        if (resetScrollPos)
            scrollbar?.SetScrollbarPosition(0);

        resultsText?.SetNewText(
            Lang.Get("electricalprogressivetransport:filter-search-results", browserInventory.FilteredCount));
    }

    private void OnBrowserScrollbar(float value)
    {
        browserInventory.SetScrollPixels(value, rowHeight);
    }

    private void ApplyBrowserStackToFilter(ItemStack stack)
    {
        if (Inventory is not InventoryLiquidInsertionPipe filterInv || stack?.Collectible == null)
            return;

        ItemStack? filterSnapshot = PipeFilterItemBrowser.TryGetLiquidPortionStack(capi.World, stack);
        if (filterSnapshot == null)
            return;

        InventoryLiquidInsertionPipe.FreezeSnapshotTemperature(capi.World, filterSnapshot);

        for (int i = 0; i < filterInv.Count; i++)
        {
            ItemStack? existing = filterInv.GetFilterSnapshot(i);
            if (PipeFilterItemBrowser.StacksMatchForFilter(capi.World, existing, filterSnapshot))
            {
                filterInv.ClearFilterSnapshot(i);
                SendSetFilterStackPacket(i, null);
                PlayClickSound();
                RefreshBrowser(resetScroll: false);
                return;
            }
        }

        // Hard limit: only free slots accept new entries. Click a selected item to free one.
        int targetSlot = -1;
        for (int i = 0; i < filterInv.Count; i++)
        {
            if (!filterInv.IsFilterSet(i))
            {
                targetSlot = i;
                break;
            }
        }

        if (targetSlot < 0)
            return;

        filterInv.SetFilterSnapshot(targetSlot, filterSnapshot);
        SendSetFilterStackPacket(targetSlot, filterSnapshot);
        PlayClickSound();
        RefreshBrowser(resetScroll: false);
    }

    private void SendSetFilterStackPacket(int slotId, ItemStack? stack)
    {
        var tree = new TreeAttribute();
        tree.SetInt("slotId", slotId);
        if (stack != null)
            tree.SetItemstack("stack", stack.Clone());

        capi.Network.SendBlockEntityPacket(BlockEntityPosition, SetFilterStackPacketId, tree.ToBytes());
    }

    private void PlayClickSound()
    {
        capi.World.PlaySoundAt(
            new AssetLocation("sounds/player/collect"),
            capi.World.Player.Entity,
            null,
            true,
            16f);
    }

    private void UpdateFilterButtons()
    {
        if (SingleComposer == null)
            return;

        var btnAllow = SingleComposer.GetButton("btnAllowList");
        var btnDeny = SingleComposer.GetButton("btnDenyList");

        if (btnAllow != null && !IsDuplicate)
            btnAllow.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.AllowList;

        if (btnDeny != null && !IsDuplicate)
            btnDeny.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.DenyList;
    }

    private void UpdateTransferRateDisplay()
    {
        var txtRate = SingleComposer.GetDynamicText("txtTransferRate");
        if (txtRate == null)
            return;

        txtRate.SetNewText(transferRate.ToString());

        var btnDecrease = SingleComposer.GetButton("btnDecrease");
        var btnIncrease = SingleComposer.GetButton("btnIncrease");

        if (btnDecrease != null)
            btnDecrease.Enabled = transferRate > 10;

        if (btnIncrease != null)
            btnIncrease.Enabled = transferRate < 1000;
    }

    private void OnTitleBarClose()
    {
        TryClose();
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, 2001);
    }

    private bool OnAllowListClicked()
    {
        filterMode = BELiquidInsertionPipe.FilterMode.AllowList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    private bool OnDenyListClicked()
    {
        filterMode = BELiquidInsertionPipe.FilterMode.DenyList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    private bool OnDecreaseRateClicked()
    {
        if (transferRate > 10)
        {
            transferRate = Math.Max(10, transferRate - 10);
            UpdateTransferRateDisplay();
            SendTransferRateUpdate();
        }

        return true;
    }

    private bool OnIncreaseRateClicked()
    {
        if (transferRate < 1000)
        {
            transferRate = Math.Min(1000, transferRate + 10);
            UpdateTransferRateDisplay();
            SendTransferRateUpdate();
        }

        return true;
    }

    private void SendTransferRateUpdate()
    {
        try
        {
            var tree = new TreeAttribute();
            tree.SetInt("transferRate", transferRate);
            capi.Network.SendBlockEntityPacket(BlockEntityPosition, 2003, tree.ToBytes());
        }
        catch
        {
            // ignore
        }
    }

    private void SendFilterSettings()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((int)filterMode);
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, 2002, ms.ToArray());
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        SendTransferRateUpdate();
        SingleComposer?.GetSlotGrid("browserSlots")?.OnGuiClosed(capi);

        if (Inventory != null)
            capi.World.Player.InventoryManager.CloseInventory(Inventory);

        browserInventory.OnStackClicked = null;
        browserInventory.DiscardAll();
    }
}
