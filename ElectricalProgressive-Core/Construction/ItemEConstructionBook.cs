using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Construction;

public class ItemEConstructionBook : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection? blockSel,
        EntitySelection? entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (slot?.Itemstack == null || byEntity is not EntityPlayer epl)
            return;

        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent)
            return;

        var world = byEntity.World;
        var selected = ConstructionCatalog.GetSelectedBlock(world, slot.Itemstack);
        // GUI только в воздухе / без выбранной схемы.
        // Shift+ПКМ по блоку не трогаем — так ставятся термопластины на генератор.
        if (selected == null || blockSel == null)
        {
            if (world.Api is ICoreClientAPI capi)
                OpenGui(capi, slot);
            return;
        }

        TryPlace(world, epl.Player, selected, blockSel);
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection? blockSel, EntitySelection? entitySel)
        => false;

    public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world,
        bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var selected = ConstructionCatalog.GetSelectedBlock(world, inSlot.Itemstack);
        dsc.AppendLine();
        dsc.AppendLine(selected != null
            ? Lang.Get("electricalprogressivecore:construction-book-selected", new ItemStack(selected).GetName())
            : Lang.Get("electricalprogressivecore:construction-book-hint"));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return
        [
            new WorldInteraction
            {
                ActionLangCode = "electricalprogressivecore:construction-book-help-open",
                MouseButton = EnumMouseButton.Right
            },
            new WorldInteraction
            {
                ActionLangCode = "electricalprogressivecore:construction-book-help-place",
                MouseButton = EnumMouseButton.Right
            }
        ];
    }

    private static void OpenGui(ICoreClientAPI capi, ItemSlot slot)
    {
        foreach (var dlg in capi.Gui.LoadedGuis)
        {
            if (dlg is GuiDialogConstructionBook bookDlg)
                bookDlg.TryClose();
        }

        try
        {
            new GuiDialogConstructionBook(capi, slot).TryOpen();
        }
        catch (Exception ex)
        {
            capi.Logger.Error("[ConstructionBook] gui: {0}", ex);
        }
    }

    private static void PlayPlaceSound(IWorldAccessor world, IPlayer byPlayer, Vintagestory.API.MathTools.BlockPos pos)
    {
        world.PlaySoundAt(new AssetLocation("sounds/player/build"), pos.X, pos.Y, pos.Z, byPlayer);
    }

    private static void TryPlace(IWorldAccessor world, IPlayer byPlayer, Block selected, BlockSelection blockSel)
    {
        try
        {
            var block = ConstructionCatalog.ResolvePlaceBlock(world, byPlayer, selected, blockSel);
            var stack = new ItemStack(block);

            var fail = "";
            // Свой TryPlaceBlock (термопластины на генератор и т.п.)
            if (block.TryPlaceBlock(world, byPlayer, stack, blockSel, ref fail))
            {
                PlayPlaceSound(world, byPlayer, blockSel.Position);
                return;
            }

            var sel = ConstructionCatalog.PreparePlaceSelection(world, blockSel, block);
            fail = "";
            if (block.CanPlaceBlock(world, byPlayer, sel, ref fail) &&
                block.DoPlaceBlock(world, byPlayer, sel, stack))
            {
                PlayPlaceSound(world, byPlayer, sel.Position);
                return;
            }

            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(block, "placefailed",
                    Lang.Get("electricalprogressivecore:construction-book-placefail"));
            }
        }
        catch (Exception ex)
        {
            world.Logger.Error("[ConstructionBook] place {0}: {1}", selected.Code, ex);
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(selected, "placefailed",
                    Lang.Get("electricalprogressivecore:construction-book-placefail"));
            }
        }
    }
}
