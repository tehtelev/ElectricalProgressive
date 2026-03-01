using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class GuiDialogLiquidInsertionPipe : GuiDialogBlockEntity
{
    private BELiquidInsertionPipe blockEntity;
    private int transferRate = 100;
    private BELiquidInsertionPipe.FilterMode filterMode = BELiquidInsertionPipe.FilterMode.AllowList;

    public GuiDialogLiquidInsertionPipe(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BELiquidInsertionPipe blockEntity)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        this.blockEntity = blockEntity;

        if (this.IsDuplicate)
            return;

        // Загружаем текущие настройки
        transferRate = blockEntity.TransferRate;
        filterMode = blockEntity.CurrentFilterMode;

        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
        SetupDialog();
    }

    private void SetupDialog()
    {
        var dialogBounds = ElementBounds.Fixed(0, 0, 320, 300);
        var dialogAlignment = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();

        SingleComposer = capi.Gui
            .CreateCompo("liquidinsertionpipegui" + BlockEntityPosition.ToString(), dialogAlignment)
            .AddShadedDialogBG(dialogBounds, true)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(dialogBounds)

            // Заголовок фильтров
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-liquidpipe-comment"),
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 40, 320, 25))

            // Сетка фильтров для жидкостей (2x3)
            .AddItemSlotGrid(
                (IInventory)Inventory,
                SendInvPacket,
                6, // 6 колонок
                [0, 1, 2, 3, 4, 5], // 6 слотов
                ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 60, 6, 1),
                "liquidFilterSlots")

            // Настройки фильтра
            .AddStaticText(Lang.Get("electricalprogressivetransport:liquid-filter-pipe-settings"),
                CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(10, 160, 260, 25))

            .AddStaticText("══════════════════════════════",
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 170, 310, 25))

            // Кнопки режима фильтра
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-allow"), OnAllowListClicked,
                ElementBounds.Fixed(10, 185, 130, 30), EnumButtonStyle.Normal,
                "btnAllowList")
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-deny"), OnDenyListClicked,
                ElementBounds.Fixed(180, 185, 130, 30), EnumButtonStyle.Normal,
                "btnDenyList")

            .AddStaticText("══════════════════════════════",
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 225, 310, 25))

            // Скорость передачи жидкостей
            .AddStaticText(Lang.Get("electricalprogressivetransport:liquid-transfer-speed"),
                CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(10, 215, 150, 25))

            .AddSmallButton("-10", OnDecreaseRateClicked,
                ElementBounds.Fixed(12, 240, 100, 30), EnumButtonStyle.Normal,
                "btnDecrease")

            .AddDynamicText(transferRate.ToString(),
                CairoFont.WhiteDetailText().WithFontSize(16).WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(150, 240, 40, 30), "txtTransferRate")

            .AddSmallButton("+10", OnIncreaseRateClicked,
                ElementBounds.Fixed(210, 240, 100, 30), EnumButtonStyle.Normal,
                "btnIncrease")

            .EndChildElements()
            .Compose();

        UpdateFilterButtons();
        UpdateTransferRateDisplay();
    }

    private void UpdateFilterButtons()
    {
        var btnAllow = SingleComposer.GetButton("btnAllowList");
        var btnDeny = SingleComposer.GetButton("btnDenyList");

        if (btnAllow != null)
        {
            btnAllow.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.AllowList;
        }

        if (btnDeny != null)
        {
            btnDeny.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.DenyList;
        }
    }

    private void UpdateTransferRateDisplay()
    {
        var txtRate = SingleComposer.GetDynamicText("txtTransferRate");
        if (txtRate != null)
        {
            txtRate.SetNewText(transferRate.ToString());

            var btnDecrease = SingleComposer.GetButton("btnDecrease");
            var btnIncrease = SingleComposer.GetButton("btnIncrease");

            if (btnDecrease != null)
            {
                btnDecrease.Enabled = transferRate > 10;
            }

            if (btnIncrease != null)
            {
                btnIncrease.Enabled = transferRate < 1000;
            }
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            2001);
    }

    private void SendInvPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition.X,
            BlockEntityPosition.Y,
            BlockEntityPosition.Z,
            p);
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
        catch (Exception ex)
        {
            capi.Logger.Error($"Ошибка при отправке скорости передачи: {ex.Message}");
        }
    }

    private void SendFilterSettings()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write((int)filterMode);
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            2002,
            ms.ToArray());
    }
}