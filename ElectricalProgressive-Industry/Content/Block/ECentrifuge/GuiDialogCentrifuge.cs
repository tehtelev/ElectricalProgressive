using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class GuiDialogCentrifuge : GuiDialogBlockEntity
{
  private long lastRedrawMs;
  private float _recipeprogress;

  protected override double FloatyDialogPosition => 0.75;

  public GuiDialogCentrifuge(
    string DialogTitle,
    InventoryBase Inventory,
    BlockPos BlockEntityPosition,
    ICoreClientAPI capi)
    : base(DialogTitle, Inventory, BlockEntityPosition, capi)
  {
    if (this.IsDuplicate)
      return;
    capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
    this.SetupDialog();
  }

  public void OnInventorySlotModified(int slotid)
  {
    this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupquerndlg");
  }

  private void SetupDialog()
  {
    ItemSlot itemSlot = this.capi.World.Player.InventoryManager.CurrentHoveredSlot;
    if (itemSlot != null && itemSlot.Inventory == this.Inventory)
      this.capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
    else
      itemSlot = (ItemSlot)null;
    ElementBounds bounds1 = ElementBounds.Fixed(0.0, 0.0, 200.0, 90.0);
    ElementBounds bounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 30.0, 1, 1);
    ElementBounds bounds3 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 153.0, 30.0, 1, 1);
    ElementBounds bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
    bounds4.BothSizing = ElementSizing.FitToChildren;
    bounds4.WithChildren(bounds1);
    ElementBounds bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
      .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);
    this.ClearComposers();
    this.SingleComposer = this.capi.Gui
      .CreateCompo("blockentitymillstone" + this.BlockEntityPosition?.ToString(), bounds5).AddShadedDialogBG(bounds4)
      .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose)).BeginChildElements(bounds4)
      .AddDynamicCustomDraw(bounds1, new DrawDelegateWithBounds(this.OnBgDraw), "symbolDrawer")
      .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[1], bounds2,
        "inputSlot").AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[1]
      {
        1
      }, bounds3, "outputslot").EndChildElements().Compose();
    this.lastRedrawMs = this.capi.ElapsedMilliseconds;
    if (itemSlot == null)
      return;
    this.SingleComposer.OnMouseMove(new MouseEvent(this.capi.Input.MouseX, this.capi.Input.MouseY));
  }

  public void Update(float RecipeProgress)
  {
    _recipeprogress = RecipeProgress;
    if (!this.IsOpened() || this.capi.ElapsedMilliseconds - this.lastRedrawMs <= 500L)
      return;
    if (this.SingleComposer != null)
      this.SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
    this.lastRedrawMs = this.capi.ElapsedMilliseconds;
  }

  private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
  {
    double num1 = 30.0;
    ctx.Save();
    Matrix matrix = ctx.Matrix;
    matrix.Translate(GuiElement.scaled(63.0), GuiElement.scaled(num1 + 2.0));
    matrix.Scale(GuiElement.scaled(0.6), GuiElement.scaled(0.6));
    ctx.Matrix = matrix;
    this.capi.Gui.Icons.DrawArrowRight(ctx, 2.0);
    ctx.Rectangle(GuiElement.scaled(5.0), 0.0, GuiElement.scaled(125.0 * _recipeprogress), GuiElement.scaled(100.0));
    ctx.Clip();
    LinearGradient source = new LinearGradient(0.0, 0.0, GuiElement.scaled(200.0), 0.0);
    int num3 = (int)source.AddColorStop(0.0, new Color(0.0, 0.4, 0.0, 1.0));
    int num4 = (int)source.AddColorStop(1.0, new Color(0.2, 0.6, 0.2, 1.0));
    ctx.SetSource((Pattern)source);
    this.capi.Gui.Icons.DrawArrowRight(ctx, 0.0, false, false);
    source.Dispose();
    ctx.Restore();
  }

  private void SendInvPacket(object p)
  {
    this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition.X, this.BlockEntityPosition.Y,
      this.BlockEntityPosition.Z, p);
  }

  private void OnTitleBarClose() => this.TryClose();

  public override void OnGuiOpened()
  {
    base.OnGuiOpened();
    this.Inventory.SlotModified += new Action<int>(this.OnInventorySlotModified);
  }

  public override void OnGuiClosed()
  {
    this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
    this.SingleComposer.GetSlotGrid("inputSlot").OnGuiClosed(this.capi);
    this.SingleComposer.GetSlotGrid("outputslot").OnGuiClosed(this.capi);
    base.OnGuiClosed();
  }
}