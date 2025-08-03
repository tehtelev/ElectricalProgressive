using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFreezer2;

class GuiEFreezer2 : GuiDialogBlockEntity
{
    BlockEntityEFreezer2? _freezer;
    public GuiEFreezer2(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPos, ICoreClientAPI capi, BlockEntityEFreezer2 freezer) : base(
        dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate)
            return;

        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        Inventory.SlotModified += OnInventorySlotModified;

        _freezer = freezer;

        SetupDialog();
    }

    public void OnInventorySlotModified(int slotid)
    {
        //SetupDialog();
        capi.Event.EnqueueMainThreadTask(SetupDialog, "setupfreezerslotdlg");


    }

    void SetupDialog()
    {
        ItemSlot hoveredSlot = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hoveredSlot != null && hoveredSlot.Inventory == Inventory)
        {
            capi.Input.TriggerOnMouseLeaveSlot(hoveredSlot);
        }
        else
        {
            hoveredSlot = null!;
        }

        ElementBounds mainBounds = ElementBounds.Fixed(0, 0, 200, 100);
        ElementBounds slotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 30, 2, 3);

        // 2. Around all that is 10 pixel padding
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(mainBounds);

        // 3. Finally Dialog
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);



        ClearComposers();


        SingleComposer = capi.Gui
            .CreateCompo("beeightslots" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddItemSlotGrid(Inventory, SendInvPacket, 2, new[] { 0, 1, 2, 3, 4, 5 }, slotsBounds)
            .EndChildElements()
            .Compose();


        if (hoveredSlot != null)
        {
            SingleComposer.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
        }
    }
    
    private void SendInvPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, p);
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    public override bool OnEscapePressed()
    {
        base.OnEscapePressed();
        OnTitleBarClose();
        return TryClose();
    }


    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        base.Inventory.SlotModified += this.OnInventorySlotModified;

        _freezer?.OpenLid(); //открываем крышку при открытии диалога
    }

    public override void OnGuiClosed()
    {
        base.Inventory.SlotModified -= this.OnInventorySlotModified;
        base.OnGuiClosed();

        _freezer?.CloseLid(); //закрываем крышку генератора при закрытии диалога
    }
}