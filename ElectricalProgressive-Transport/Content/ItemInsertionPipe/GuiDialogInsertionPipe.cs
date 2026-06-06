using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

/// <summary>
/// Диалоговое окно для настройки фильтрации и скорости передачи предметов
/// в фильтрующей трубе.
/// </summary>
public class GuiDialogInsertionPipe : GuiDialogBlockEntity
{
    // Поля диалога
    private BEItemInsertionPipe blockEntity;         // Ссылка на блок-сущность
    private int transferRate = 1;                    // Текущая скорость передачи (1-8)
    private BEItemInsertionPipe.FilterMode filterMode = BEItemInsertionPipe.FilterMode.AllowList;
    private bool matchMod = false;                   // Сопоставление мода предмета
    private bool matchType = true;                   // Сопоставление типа предмета
    private bool matchAttributes = false;           // Сопоставление атрибутов предмета

    /// <summary>
    /// Конструктор диалога.
    /// </summary>
    public GuiDialogInsertionPipe(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BEItemInsertionPipe blockEntity)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        this.blockEntity = blockEntity;

        // Защита от дубликатов диалога
        if (this.IsDuplicate)
            return;

        // Инициализация текущими настройками блока
        transferRate = blockEntity.TransferRate;
        filterMode = blockEntity.CurrentFilterMode;
        matchMod = blockEntity.MatchMod;
        matchType = blockEntity.MatchType;
        matchAttributes = blockEntity.MatchAttributes;

        // Открываем инвентарь для выбора предметов в фильтрах
        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);

        // Создаем и настраиваем интерфейс
        SetupDialog();
    }

    /// <summary>
    /// Инициализация элементов GUI.
    /// </summary>
    private void SetupDialog()
    {
        // Настройка размеров окна диалога (320x400 пикселей)
        var dialogBounds = ElementBounds.Fixed(0, 0, 320, 400);

        // Выравнивание по центру правой части экрана
        var dialogAlignment = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();

        // Создаем компоновщик GUI
        SingleComposer = capi.Gui
            .CreateCompo("insertionpipegui" + BlockEntityPosition.ToString(), dialogAlignment)
            .AddShadedDialogBG(dialogBounds, true)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(dialogBounds)

        // --- Секция 1: Сетка фильтров (4x3 = 12 слотов) ---
        
        // Заголовок секции фильтров
        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-pipe-comment"),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(10, 40, 320, 25))

        // Сетка из 12 слотов для предметов фильтрации
        .AddItemSlotGrid(
            (IInventory)Inventory,
            SendInvPacket,
            6, // Количество колонок
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], // Нумерация слотов
            ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 60, 6, 2), // Размер сетки 4x3
            "filterSlots")

        // --- Секция 2: Настройки режима фильтрации ---

        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-pipe-settings"),
            CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
            ElementBounds.Fixed(10, 165, 300, 25))

        // Разделительная линия
        .AddStaticText("═════════════════════════════",
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(10, 175, 300, 25))

        // Кнопки выбора режима фильтрации
        .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-allow"),
            OnAllowListClicked,
            ElementBounds.Fixed(10, 190, 130, 30), EnumButtonStyle.Normal, "btnAllowList")

        .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-deny"),
            OnDenyListClicked,
            ElementBounds.Fixed(170, 190, 130, 30), EnumButtonStyle.Normal, "btnDenyList")

        // Разделительная линия
        .AddStaticText("═════════════════════════════",
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(10, 225, 300, 25))

        // Чекбоксы для настройки условий фильтрации
        .AddSwitch(OnMatchModToggled, ElementBounds.Fixed(10, 235, 40, 25), "swMatchMod")
        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-mod"),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(55, 240, 160, 25))

        .AddSwitch(OnMatchTypeToggled, ElementBounds.Fixed(10, 270, 40, 25), "swMatchType")
        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-type"),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(55, 275, 160, 25))

        .AddSwitch(OnMatchAttrsToggled, ElementBounds.Fixed(10, 305, 40, 25), "swMatchAttrs")
        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-match-attrs"),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(55, 310, 160, 25))

        // Разделительная линия
        .AddStaticText("═════════════════════════════",
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(10, 350, 300, 25))

        // --- Секция 3: Настройка скорости передачи ---

        .AddStaticText(Lang.Get("electricalprogressivetransport:filter-speet-transfer"),
            CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
            ElementBounds.Fixed(10, 340, 150, 25))

        // Кнопка уменьшения скорости
        .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-speet-down"),
            OnDecreaseRateClicked,
            ElementBounds.Fixed(12, 365, 100, 30), EnumButtonStyle.Normal, "btnDecrease")

        // Отображение текущей скорости
        .AddDynamicText(transferRate.ToString(),
            CairoFont.WhiteDetailText().WithFontSize(18).WithWeight(Cairo.FontWeight.Bold),
            ElementBounds.Fixed(150, 368, 40, 30), "txtTransferRate")

        // Кнопка увеличения скорости
        .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-speet-up"),
            OnIncreaseRateClicked,
            ElementBounds.Fixed(200, 365, 100, 30), EnumButtonStyle.Normal, "btnIncrease")

        .EndChildElements()
        .Compose();

        // Применяем начальные значения чекбоксов
        ApplyInitialSwitchValues();

        // Обновляем состояние элементов UI
        UpdateFilterButtons();
        UpdateTransferRateDisplay();
    }

    /// <summary>
    /// Применение начальных значений чекбоксов при создании диалога.
    /// </summary>
    private void ApplyInitialSwitchValues()
    {
        var swMatchMod = SingleComposer.GetSwitch("swMatchMod");
        swMatchMod?.SetValue(matchMod);

        var swMatchType = SingleComposer.GetSwitch("swMatchType");
        swMatchType?.SetValue(matchType);

        var swMatchAttrs = SingleComposer.GetSwitch("swMatchAttrs");
        swMatchAttrs?.SetValue(matchAttributes);
    }

    /// <summary>
    /// Обновление состояния кнопок режима фильтрации.
    /// </summary>
    private void UpdateFilterButtons()
    {
        var btnAllow = SingleComposer.GetButton("btnAllowList");
        var btnDeny = SingleComposer.GetButton("btnDenyList");

        if (btnAllow != null)
            btnAllow.Enabled = filterMode != BEItemInsertionPipe.FilterMode.AllowList;

        if (btnDeny != null)
            btnDeny.Enabled = filterMode != BEItemInsertionPipe.FilterMode.DenyList;
    }

    /// <summary>
    /// Обновление отображения и состояния кнопок скорости передачи.
    /// </summary>
    private void UpdateTransferRateDisplay()
    {
        var txtRate = SingleComposer.GetDynamicText("txtTransferRate");
        if (txtRate != null)
        {
            txtRate.SetNewText(transferRate.ToString());

            var btnDecrease = SingleComposer.GetButton("btnDecrease");
            var btnIncrease = SingleComposer.GetButton("btnIncrease");

            if (btnDecrease != null)
                btnDecrease.Enabled = transferRate > 1;

            if (btnIncrease != null)
                btnIncrease.Enabled = transferRate < 8;
        }
    }

    // --- Обработчики событий ---

    /// <summary>
    /// Обработка закрытия заголовка диалога.
    /// </summary>
    private void OnTitleBarClose()
    {
        TryClose();

        // Отправка пакета серверу о закрытии GUI
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            1001); // Пакет закрытия GUI
    }

    /// <summary>
    /// Обработка клика по кнопке "Разрешить" (Allow List).
    /// </summary>
    private bool OnAllowListClicked()
    {
        filterMode = BEItemInsertionPipe.FilterMode.AllowList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    /// <summary>
    /// Обработка клика по кнопке "Запретить" (Deny List).
    /// </summary>
    private bool OnDenyListClicked()
    {
        filterMode = BEItemInsertionPipe.FilterMode.DenyList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    /// <summary>
    /// Обработка переключения чекбокса "Сопоставление мода".
    /// </summary>
    private void OnMatchModToggled(bool state)
    {
        matchMod = state;
        SendFilterSettings();
    }

    /// <summary>
    /// Обработка переключения чекбокса "Сопоставление типа".
    /// </summary>
    private void OnMatchTypeToggled(bool state)
    {
        matchType = state;
        SendFilterSettings();
    }

    /// <summary>
    /// Обработка переключения чекбокса "Сопоставление атрибутов".
    /// </summary>
    private void OnMatchAttrsToggled(bool state)
    {
        matchAttributes = state;
        SendFilterSettings();
    }

    /// <summary>
    /// Обработка нажатия кнопки уменьшения скорости.
    /// </summary>
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

    /// <summary>
    /// Обработка нажатия кнопки увеличения скорости.
    /// </summary>
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

    // --- Отправка данных на сервер ---

    /// <summary>
    /// Обновление скорости передачи.
    /// </summary>
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
            //Api.Logger.Error($"Ошибка при отправке скорости передачи: {ex.Message}");
        }
    }

    /// <summary>
    /// Отправка настроек фильтрации на сервер.
    /// </summary>
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

    /// <summary>
    /// Отправка инвентаря для отображения в слотах фильтра.
    /// </summary>
    private void SendInvPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition.X,
            BlockEntityPosition.Y,
            BlockEntityPosition.Z,
            p);
    }

    // --- Жизненный цикл ---

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        // Сохранение настроек при закрытии
        SendTransferRateUpdate();

        var slotGrid = SingleComposer?.GetSlotGrid("filterSlots");

        slotGrid?.OnGuiClosed(capi);

        capi.World.Player.InventoryManager.CloseInventory((IInventory)Inventory);
    }
}