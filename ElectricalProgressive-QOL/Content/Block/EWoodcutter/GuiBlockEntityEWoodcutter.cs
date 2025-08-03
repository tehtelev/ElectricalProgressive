using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EWoodcutter;

public class GuiBlockEntityEWoodcutter : GuiDialogBlockEntity
{
    public GuiBlockEntityEWoodcutter(
        string dialogTitle,
        InventoryEWoodcutter inventory,
        BlockPos blockEntityPos,
        ICoreClientAPI capi
    ) : base(dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate)
            return;

        capi.World.Player.InventoryManager.OpenInventory(inventory);

        SetupDialog();
    }

    public void Update()
    {
        if (!IsOpened())
            return;

        //TODO: Добавить обновление UI как появиться плавная рубка дерева
    }

    public void SetupDialog()
    {
        var window = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0)
            .AddImmersiveOffset(capi.Settings.Bool["immersiveMouseMode"]);

        var dialog = ElementBounds.Fill.WithFixedPadding(20);

        var dialogBounds = ElementBounds.Fixed(250, 60);

        var inputGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0 + GuiStyle.TitleBarHeight, 1, 1);
        var outputGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 48 + 20 + GuiStyle.TitleBarHeight, 5, 1);

        dialog.BothSizing = ElementSizing.FitToChildren;
        dialog.WithChildren(new[]
        {
            dialogBounds,
            inputGrid,
            outputGrid
        });

        SingleComposer = capi.Gui.CreateCompo("Woodcutter" + BlockEntityPosition, window)
            .AddShadedDialogBG(dialog)
            //TODO: Локаль
            .AddDialogTitleBar("Электро лесоруб", OnTitleBarClose)
            .BeginChildElements(dialog)

            .AddItemSlotGrid(Inventory, SendInvPacket, 1, new[] { 0 }, inputGrid, "inputSlot")
            .AddItemSlotGrid(Inventory, SendInvPacket, 5, new[] { 1, 2, 3, 4, 5 }, outputGrid, "outputSlots")

            .EndChildElements()
            .Compose();
    }

    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnSlotModified;
    }

    private void OnSlotModified(int slotId)
    {
        capi.Event.EnqueueMainThreadTask(SetupDialog, "setupewoodcutterdialog");
    }

    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnSlotModified;
        SingleComposer.GetSlotGrid("inputSlot").OnGuiClosed(capi);
        SingleComposer.GetSlotGrid("outputSlots").OnGuiClosed(capi);

        base.OnGuiClosed();
    }
}