// GuiDialogLiquidInsertionPipe.cs
// ================================================
// Диалоговое окно для настройки фильтра жидкостей в трубе
// Открывается при правой кнопке мыши по трубной сущности

using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class GuiDialogLiquidInsertionPipe : GuiDialogBlockEntity
{
    // Ссылка на блок-сущность трубы
    private BELiquidInsertionPipe blockEntity;

    // Текущая скорость передачи жидкостей (литров в секунду)
    private int transferRate = 100;

    // Текущий режим фильтрации
    private BELiquidInsertionPipe.FilterMode filterMode = BELiquidInsertionPipe.FilterMode.AllowList;

    /// <summary>
    /// Конструктор диалогового окна.
    /// </summary>
    /// <param name="DialogTitle">Заголовок диалога</param>
    /// <param name="Inventory">Инвентарь для слотов фильтров</param>
    /// <param name="BlockEntityPosition">Позиция блока-сущности</param>
    /// <param name="capi">API клиента</param>
    /// <param name="blockEntity">Ссылка на блок-сущность трубы</param>
    public GuiDialogLiquidInsertionPipe(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BELiquidInsertionPipe blockEntity)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        this.blockEntity = blockEntity;

        // Если это дубликат диалога - выходим сразу
        if (this.IsDuplicate)
            return;

        // Загружаем текущие настройки из сущности
        transferRate = blockEntity.TransferRate;
        filterMode = blockEntity.CurrentFilterMode;

        // Открываем инвентарь для фильтрации жидкостей
        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);

        SetupDialog();
    }

    /// <summary>
    /// Настройка элементов диалогового окна.
    /// </summary>
    private void SetupDialog()
    {
        // Определяем размеры и выравнивание диалога
        var dialogBounds = ElementBounds.Fixed(0, 0, 320, 300);
        var dialogAlignment = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();

        SingleComposer = capi.Gui
            // Создаем основной композер диалога
            .CreateCompo("liquidinsertionpipegui" + BlockEntityPosition.ToString(), dialogAlignment)

            // Добавляем затемненный фон диалога
            .AddShadedDialogBG(dialogBounds, true)

            // Добавляем заголовок с кнопкой закрытия
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)

            // Начинаем добавлять дочерние элементы
            .BeginChildElements(dialogBounds)

                // --- Заголовок фильтров ---
                .AddStaticText(Lang.Get("electricalprogressivetransport:filter-liquidpipe-comment"),
                    CairoFont.WhiteDetailText(),
                    ElementBounds.Fixed(10, 40, 320, 25))

                // --- Сетка слотов фильтров жидкостей (2x3 = 6 слотов) ---
                .AddItemSlotGrid(
                    (IInventory)Inventory,
                    SendInvPacket,
                    6, // Количество колонок
                    [0, 1, 2, 3, 4, 5], // Индексы слотов
                    ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 60, 6, 1),
                    "liquidFilterSlots")

                // --- Настройки режима фильтра ---
                .AddStaticText(Lang.Get("electricalprogressivetransport:liquid-filter-pipe-settings"),
                    CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                    ElementBounds.Fixed(10, 160, 260, 25))

                .AddStaticText("══════════════════════════════",
                    CairoFont.WhiteDetailText(),
                    ElementBounds.Fixed(10, 170, 310, 25))

                // Кнопки выбора режима фильтра
                .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-allow"), OnAllowListClicked,
                    ElementBounds.Fixed(10, 185, 130, 30), EnumButtonStyle.Normal,
                    "btnAllowList")

                .AddSmallButton(Lang.Get("electricalprogressivetransport:filter-mode-deny"), OnDenyListClicked,
                    ElementBounds.Fixed(180, 185, 130, 30), EnumButtonStyle.Normal,
                    "btnDenyList")

                .AddStaticText("══════════════════════════════",
                    CairoFont.WhiteDetailText(),
                    ElementBounds.Fixed(10, 225, 310, 25))

                // --- Скорость передачи жидкостей ---
                .AddStaticText(Lang.Get("electricalprogressivetransport:liquid-transfer-speed"),
                    CairoFont.WhiteDetailText().WithWeight(Cairo.FontWeight.Bold),
                    ElementBounds.Fixed(10, 215, 150, 25))

                // Кнопка уменьшения скорости (-10)
                .AddSmallButton("-10", OnDecreaseRateClicked,
                    ElementBounds.Fixed(12, 240, 100, 30), EnumButtonStyle.Normal,
                    "btnDecrease")

                // Отображение текущей скорости
                .AddDynamicText(transferRate.ToString(),
                    CairoFont.WhiteDetailText().WithFontSize(16).WithWeight(Cairo.FontWeight.Bold),
                    ElementBounds.Fixed(150, 245, 40, 30), "txtTransferRate")

                // Кнопка увеличения скорости (+10)
                .AddSmallButton("+10", OnIncreaseRateClicked,
                    ElementBounds.Fixed(210, 240, 100, 30), EnumButtonStyle.Normal,
                    "btnIncrease")

            // Завершаем добавление дочерних элементов
            .EndChildElements()

            // Финализируем композер
            .Compose();

        // Обновляем состояние кнопок и отображения
        UpdateFilterButtons();
        UpdateTransferRateDisplay();
    }

    /// <summary>
    /// Обновляет состояние кнопок фильтра.
    /// Кнопка активна только если режим не соответствует текущему значению.
    /// </summary>
    private void UpdateFilterButtons()
    {
        if (SingleComposer == null)
            return; // Проверка на существование композера

        var btnAllow = SingleComposer.GetButton("btnAllowList");
        var btnDeny = SingleComposer.GetButton("btnDenyList");

        if (btnAllow != null && !IsDuplicate)
            btnAllow.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.AllowList;

        if (btnDeny != null && !IsDuplicate)
            btnDeny.Enabled = filterMode != BELiquidInsertionPipe.FilterMode.DenyList;
    }

    /// <summary>
    /// Обновляет отображение скорости передачи и ограничений кнопок.
    /// </summary>
    private void UpdateTransferRateDisplay()
    {
        var txtRate = SingleComposer.GetDynamicText("txtTransferRate");
        if (txtRate != null)
        {
            // Устанавливаем текст текущей скорости
            txtRate.SetNewText(transferRate.ToString());

            var btnDecrease = SingleComposer.GetButton("btnDecrease");
            var btnIncrease = SingleComposer.GetButton("btnIncrease");

            // Ограничиваем уменьшение: минимум 10 л/с
            if (btnDecrease != null)
            {
                btnDecrease.Enabled = transferRate > 10;
            }

            // Ограничиваем увеличение: максимум 1000 л/с
            if (btnIncrease != null)
            {
                btnIncrease.Enabled = transferRate < 1000;
            }
        }
    }

    /// <summary>
    /// Обработка закрытия заголовка диалога.
    /// </summary>
    private void OnTitleBarClose()
    {
        TryClose();

        // Отправляем пакет закрытия сущности (ID 2001)
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            2001);
    }

    /// <summary>
    /// Отправка инвентарного пакета при изменении слотов.
    /// </summary>
    /// <param name="p">Данные инвентаря</param>
    private void SendInvPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition.X,
            BlockEntityPosition.Y,
            BlockEntityPosition.Z,
            p);
    }

    /// <summary>
    /// Обработка клика по кнопке "Разрешить" (AllowList).
    /// </summary>
    private bool OnAllowListClicked()
    {
        filterMode = BELiquidInsertionPipe.FilterMode.AllowList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    /// <summary>
    /// Обработка клика по кнопке "Запретить" (DenyList).
    /// </summary>
    private bool OnDenyListClicked()
    {
        filterMode = BELiquidInsertionPipe.FilterMode.DenyList;
        UpdateFilterButtons();
        SendFilterSettings();
        return true;
    }

    /// <summary>
    /// Обработка клика по кнопке уменьшения скорости.
    /// </summary>
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

    /// <summary>
    /// Обработка клика по кнопке увеличения скорости.
    /// </summary>
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

    /// <summary>
    /// Отправка обновления скорости передачи.
    /// </summary>
    private void SendTransferRateUpdate()
    {
        try
        {
            var tree = new TreeAttribute();
            tree.SetInt("transferRate", transferRate);

            // Отправляем пакет с обновленной скоростью (ID 2003)
            capi.Network.SendBlockEntityPacket(BlockEntityPosition, 2003, tree.ToBytes());
        }
        catch (Exception ex)
        {
            //Api.Logger.Error($"Ошибка при отправке скорости передачи: {ex.Message}");
        }
    }

    /// <summary>
    /// Отправка настроек фильтра.
    /// </summary>
    private void SendFilterSettings()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);

        // Записываем текущий режим фильтра
        bw.Write((int)filterMode);

        // Отправляем пакет с настройками фильтра (ID 2002)
        capi.Network.SendBlockEntityPacket(
            BlockEntityPosition,
            2002,
            ms.ToArray());
    }
}