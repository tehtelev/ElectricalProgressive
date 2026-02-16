using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

public class GuiDialogInsertionPipe : GuiDialogBlockEntity
{
    private BEItemInsertionPipe blockEntity;
    private int transferRate = 1;
    private BEItemInsertionPipe.FilterMode filterMode = BEItemInsertionPipe.FilterMode.AllowList;
    private bool matchMod = false;
    private bool matchType = true;
    private bool matchAttributes = false;

    public GuiDialogInsertionPipe(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BEItemInsertionPipe blockEntity)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        this.blockEntity = blockEntity;

        if (this.IsDuplicate)
            return;

        // Загружаем текущие настройки
        transferRate = blockEntity.TransferRate;
        filterMode = blockEntity.CurrentFilterMode;
        matchMod = blockEntity.MatchMod;
        matchType = blockEntity.MatchType;
        matchAttributes = blockEntity.MatchAttributes;

        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
        SetupDialog();
    }

    private void SetupDialog()
    {
        // Увеличиваем размеры окна
        var dialogBounds = ElementBounds.Fixed(0, 0, 320, 400);
        var dialogAlignment = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();

        // Создаем компоновку GUI
        SingleComposer = capi.Gui
            .CreateCompo("insertionpipegui" + BlockEntityPosition.ToString(), dialogAlignment)
            .AddShadedDialogBG(dialogBounds, true)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(dialogBounds)

            // Заголовок фильтров (12 слотов: 4x3)
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-pipe-comment"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 40, 320, 25))

            // Сетка фильтров 4x3 (12 слотов)
            .AddItemSlotGrid(
                (IInventory)Inventory,
                SendInvPacket,
                6, // 4 колонки
                [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], // 12 слотов
                ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 60, 6, 2), // 4x3
                "filterSlots")

            // Настройки фильтра
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-pipe-settings"),
                CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(10, 165, 300, 25))
            // Разделительная линия
            .AddStaticText("══════════════════════════════",
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 175, 300, 25))



            // Кнопки режима фильтра (шире и с большими отступами)
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-allow"), OnAllowListClicked,
                ElementBounds.Fixed(10, 190, 130, 30), EnumButtonStyle.Normal,
                "btnAllowList")
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-deny"), OnDenyListClicked,
                ElementBounds.Fixed(170, 190, 130, 30), EnumButtonStyle.Normal,
                "btnDenyList")

            // Чекбоксы сравнения с выравниванием
            .AddSwitch(OnMatchModToggled, ElementBounds.Fixed(10, 230, 40, 25), "swMatchMod")
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-mod"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(55, 235, 150, 25))

            .AddSwitch(OnMatchTypeToggled, ElementBounds.Fixed(10, 265, 40, 25), "swMatchType")
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-type"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(55, 270, 150, 25))

            .AddSwitch(OnMatchAttrsToggled, ElementBounds.Fixed(10, 300, 40, 25), "swMatchAttrs")
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-attrs"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(55, 305, 150, 25))

            // Еще одна разделительная линия
            .AddStaticText("══════════════════════════════",
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(10, 345, 300, 25))

            // Скорость передачи
            .AddStaticText(Lang.Get("electricalprogressivetransport:filter-speet-transfer"),
                CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(10, 335, 150, 25))

            // Кнопка "-"
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-speet-down"), OnDecreaseRateClicked,
                ElementBounds.Fixed(12, 360, 100, 30), EnumButtonStyle.Normal,
                "btnDecrease")

            // Отображение текущей скорости
            .AddDynamicText(transferRate.ToString(),
                CairoFont.WhiteDetailText().WithFontSize(18).WithWeight(Cairo.FontWeight.Bold),
                ElementBounds.Fixed(150, 363, 40, 30), "txtTransferRate")

            // Кнопка "+"
            .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-speet-up"), OnIncreaseRateClicked,
                ElementBounds.Fixed(200, 360, 100, 30), EnumButtonStyle.Normal,
                "btnIncrease")

            .EndChildElements()
            .Compose();

        // Обновляем состояние UI
        UpdateFilterButtons();
        UpdateTransferRateDisplay();

        // Устанавливаем начальные значения переключателей
        var swMatchMod = SingleComposer.GetSwitch("swMatchMod");
        if (swMatchMod != null) swMatchMod.SetValue(matchMod);

        var swMatchType = SingleComposer.GetSwitch("swMatchType");
        if (swMatchType != null) swMatchType.SetValue(matchType);

        var swMatchAttrs = SingleComposer.GetSwitch("swMatchAttrs");
        if (swMatchAttrs != null) swMatchAttrs.SetValue(matchAttributes);
    }

    private void UpdateFilterButtons()
    {
        // Обновляем состояние кнопок в зависимости от выбранного режима
        var btnAllow = SingleComposer.GetButton("btnAllowList");
        var btnDeny = SingleComposer.GetButton("btnDenyList");

        if (btnAllow != null)
        {
            btnAllow.Enabled = filterMode != BEItemInsertionPipe.FilterMode.AllowList;
        }

        if (btnDeny != null)
        {
            btnDeny.Enabled = filterMode != BEItemInsertionPipe.FilterMode.DenyList;
        }

    }

    private void UpdateTransferRateDisplay()
    {
        var txtRate = SingleComposer.GetDynamicText("txtTransferRate");
        if (txtRate != null)
        {
            txtRate.SetNewText(transferRate.ToString());

            // Обновляем состояние кнопок в зависимости от границ
            var btnDecrease = SingleComposer.GetButton("btnDecrease");
            var btnIncrease = SingleComposer.GetButton("btnIncrease");

            if (btnDecrease != null)
            {
                btnDecrease.Enabled = transferRate > 1;
            }

            if (btnIncrease != null)
            {
                btnIncrease.Enabled = transferRate < 8;
            }
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
        // Отправляем пакет серверу о закрытии GUI
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            1001); // Пакет закрытия GUI
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
        filterMode = BEItemInsertionPipe.FilterMode.AllowList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    private bool OnDenyListClicked()
    {
        filterMode = BEItemInsertionPipe.FilterMode.DenyList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    private void OnMatchModToggled(bool state)
    {
        matchMod = state;
        SendFilterSettings();
    }

    private void OnMatchTypeToggled(bool state)
    {
        matchType = state;
        SendFilterSettings();
    }

    private void OnMatchAttrsToggled(bool state)
    {
        matchAttributes = state;
        SendFilterSettings();
    }

    private bool OnDecreaseRateClicked()
    {
        if (transferRate > 1)
        {
            transferRate--;
            UpdateTransferRateDisplay();
            SendTransferRateUpdate();
        }

        return true;
    }

    private bool OnIncreaseRateClicked()
    {
        if (transferRate < 8)
        {
            transferRate++;
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

            capi.Network.SendBlockEntityPacket(BlockEntityPosition, 1003, tree.ToBytes());
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
        bw.Write(matchMod);
        bw.Write(matchType);
        bw.Write(matchAttributes);

        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            1002, // Пакет настроек фильтра
            ms.ToArray());
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        // Автоматически сохраняем настройки при закрытии
        SendTransferRateUpdate();

        var slotGrid = SingleComposer?.GetSlotGrid("filterSlots");
        if (slotGrid != null)
        {
            slotGrid.OnGuiClosed(capi);
        }

        capi.World.Player.InventoryManager.CloseInventory((IInventory)Inventory);
    }
}