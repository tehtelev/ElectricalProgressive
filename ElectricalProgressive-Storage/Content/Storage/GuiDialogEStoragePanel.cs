using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Окно панели в общей клетке. У каждой грани свой номер пакета и своё место на экране.
/// </summary>
public class GuiDialogEStoragePanel : GuiDialogBlockEntity
{
    private const string SlotKey = "smallblockgui";

    private readonly int _packetBase;
    private readonly EnumPosFlag _screenPos;
    private readonly string _dialogName = "";

    protected GuiDialogEStoragePanel(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, Facing facing, string namePrefix)
        : base(dialogTitle, inventory, pos, capi)
    {
        if (IsDuplicate)
            return;

        var id = inventory.InventoryID;
        if (string.IsNullOrEmpty(id))
            id = pos.X + "-" + pos.InternalY + "-" + pos.Z;
        _dialogName = namePrefix + id.Replace('/', '-');
        _packetBase = PanelStack.FacePacket(facing, 0);
        _screenPos = GetFreePos(SlotKey);
    }

    protected string DialogKey => _dialogName;

    protected ElementBounds WindowBounds(double width, double height)
    {
        var bounds = Ae2Gui.Dialog();
        var saved = capi.Gui.GetDialogPosition(_dialogName);
        if (saved != null)
        {
            bounds.Alignment = EnumDialogArea.None;
            bounds.fixedOffsetX = 0;
            bounds.fixedOffsetY = 0;
            bounds.fixedX = saved.X;
            bounds.fixedY = saved.Y;
            return bounds;
        }

        if (!capi.Settings.Bool["immersiveMouseMode"])
        {
            bounds.fixedOffsetX += (width + 10) * XOffsetMul(_screenPos);
            bounds.fixedOffsetY += (height + 10) * YOffsetMul(_screenPos);
        }

        return bounds;
    }

    protected void Send(object packet)
    {
        capi.Network.SendBlockEntityPacketWithOffset(
            BlockEntityPosition.X,
            BlockEntityPosition.InternalY,
            BlockEntityPosition.Z,
            _packetBase,
            packet);
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        OccupyPos(SlotKey, _screenPos);
    }

    public override void OnGuiClosed()
    {
        if (Inventory != null)
            capi.World.Player.InventoryManager.CloseInventoryAndSync(Inventory);
        if (BlockEntityPosition != null)
            capi.Network.SendBlockEntityPacket(BlockEntityPosition, _packetBase + (int)EnumBlockEntityPacketId.Close);
        capi.Gui.PlaySound(CloseSound);
        FreePos(SlotKey, _screenPos);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (!capi.Settings.Bool["immersiveMouseMode"] || SingleComposer == null || BlockEntityPosition == null)
        {
            base.OnRenderGUI(deltaTime);
            return;
        }

        var above = new Vec3d(BlockEntityPosition.X + 0.5, BlockEntityPosition.Y + FloatyDialogPosition, BlockEntityPosition.Z + 0.5);
        var pos = MatrixToolsd.Project(above, capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, capi.Render.FrameWidth, capi.Render.FrameHeight);
        if (pos.Z < 0)
            return;

        var bounds = SingleComposer.Bounds;
        bounds.Alignment = EnumDialogArea.None;
        bounds.fixedOffsetX = 0;
        bounds.fixedOffsetY = 0;
        var gap = 10 * RuntimeEnv.GUIScale;
        bounds.absFixedX = pos.X - bounds.OuterWidth / 2 + XOffsetMul(_screenPos) * (bounds.OuterWidth + gap);
        bounds.absFixedY = capi.Render.FrameHeight - pos.Y - bounds.OuterHeight * FloatyDialogAlign + YOffsetMul(_screenPos) * (bounds.OuterHeight + gap);
        bounds.absMarginX = 0;
        bounds.absMarginY = 0;

        foreach (var entry in Composers)
        {
            entry.Value.Render(deltaTime);
            MouseOverCursor = entry.Value.MouseOverCursor;
        }
    }
}
