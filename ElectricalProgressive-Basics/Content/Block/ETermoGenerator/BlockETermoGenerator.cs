using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;
using MachineConstructAccess = global::ElectricalProgressive.Construction.MachineConstructAccess;

namespace ElectricalProgressive.Content.Block.ETermoGenerator;

/// <summary>
/// IMultiBlockInteract — сборка/GUI с любой клетки multiblock.
/// </summary>
public class BlockETermoGenerator : BlockEBase, IMultiBlockInteract
{
    private WorldInteraction[] _interactions = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _interactions = ObjectCacheUtil.GetOrCreate(api, "BlockETermoGeneratorInteractions", () =>
        {
            return new WorldInteraction[]
            {
                new() { ActionLangCode = "blockhelp-door-openclose", MouseButton = EnumMouseButton.Right },
                new()
                {
                    ActionLangCode = "blockhelp-firepit-refuel",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl"
                }
            };
        });
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        if (itemstack?.Block != null)
        {
            var side = itemstack.Block.Variant.ContainsKey("side")
                ? itemstack.Block.Variant["side"]
                : (Variant.ContainsKey("side") ? Variant["side"] : "south");

            if (itemstack.Block.Variant.ContainsKey("state") &&
                itemstack.Block.Variant["state"] != "incomplete")
            {
                var incomplete = world.GetBlock(CodeWithVariants(["state", "side"],
                    ["incomplete", side]));
                if (incomplete != null)
                    itemstack = new ItemStack(incomplete);
            }
        }

        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        if (byItemStack.Block.Variant.ContainsKey("type") &&
            byItemStack.Block.Variant["type"] == "burned")
            return false;

        var selection = new Selection(blockSel);
        Facing facing;
        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityETermoGenerator entity)
        {
            LoadEProperties.Load(this, entity);
            return true;
        }

        return false;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityETermoGenerator gen)
            gen.RefreshKpdFromNeighbours();

        if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
            return;

        world.BlockAccessor.BreakBlock(pos, null);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel is null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var controllerPos = MachineConstructAccess.GetControllerPos(world, blockSel.Position);
        return HandleInteract(world, byPlayer, controllerPos, blockSel);
    }

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        Vec3i offsetInv)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offsetInv);
        return HandleInteract(world, byPlayer, controllerPos, blockSel);
    }

    private bool HandleInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos controllerPos,
        BlockSelection blockSel)
    {
        blockSel.Block = this;

        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, controllerPos))
            return true;

        var bef = world.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityETermoGenerator;
        var construct = bef?.GetBehavior<MachineConstruct>();
        if (construct != null && construct.HasConstruction && !construct.IsReady)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressivebasics:etermogenerator-structure-incomplete"));
            }

            return true;
        }

        if (bef != null && !bef.StructureComplete)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "incomplete",
                    Lang.Get("electricalprogressivebasics:etermogenerator-structure-incomplete"));
            }

            return true;
        }

        var stack = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        if (bef != null && stack != null)
        {
            var activated = false;

            if (byPlayer.Entity.Controls.CtrlKey)
            {
                if (stack.Collectible.CombustibleProps != null &&
                    stack.Collectible.CombustibleProps.MeltingPoint > 0)
                {
                    var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0,
                        EnumMergePriority.DirectMerge, 1);
                    byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(bef.FuelSlot, ref op);
                    if (op.MovedQuantity > 0)
                        activated = true;
                }

                if (stack.Collectible.CombustibleProps != null &&
                    stack.Collectible.CombustibleProps.BurnTemperature > 0)
                {
                    var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0,
                        EnumMergePriority.DirectMerge, 1);
                    byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(bef.FuelSlot, ref op);
                    if (op.MovedQuantity > 0)
                        activated = true;
                }
            }

            if (activated)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                var loc = stack.ItemAttributes?["placeSound"].Exists == true
                    ? AssetLocation.Create(stack.ItemAttributes["placeSound"].AsString(),
                        stack.Collectible.Code.Domain)
                    : null;

                if (loc != null)
                {
                    api.World.PlaySoundAt(loc.WithPathPrefixOnce("sounds/"),
                        controllerPos.X, controllerPos.InternalY, controllerPos.Z, byPlayer,
                        0.88f + (float)api.World.Rand.NextDouble() * 0.24f, 16);
                }

                return true;
            }
        }

        if (bef is BlockEntityOpenableContainer openable)
        {
            openable.OnPlayerRightClick(byPlayer, blockSel);
            return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world,
        BlockSelection selection, IPlayer forPlayer)
    {
        var beh = MachineConstructAccess.GetBehavior(world, selection.Position);
        if (beh != null && beh.HasConstruction && !beh.IsReady)
        {
            var help = beh.GetInteractionHelp(world, forPlayer);
            if (help is { Length: > 0 })
                return help;
        }

        return _interactions;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var block = inSlot.Itemstack?.Block;
        if (block == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " +
                       MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " +
                       Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                       (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false)
                           ? Lang.Get("electricalprogressivebasics:Yes")
                           : Lang.Get("electricalprogressivebasics:No")));
        dsc.AppendLine();
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:etermogenerator-structure-hint"));
    }

    #region IMultiBlockInteract

    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => false;

    public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset) => false;

    public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, Vec3i offset)
    {
    }

    public bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset) => true;

    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset)
    {
        var controllerPos = MachineConstructAccess.GetControllerPos(pos, offset);
        foreach (var bh in BlockBehaviors)
        {
            var h = EnumHandling.PassThrough;
            var stack = bh.OnPickBlock(world, controllerPos, ref h);
            if (h != EnumHandling.PassThrough && stack != null)
                return stack;
        }

        return OnPickBlock(world, controllerPos);
    }

    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel,
        IPlayer forPlayer, Vec3i offset)
    {
        var controllerPos = MachineConstructAccess.GetControllerPos(blockSel.Position, offset);
        var sel = blockSel.Clone();
        sel.Position = controllerPos;
        return GetPlacedBlockInteractionHelp(world, sel, forPlayer);
    }

    public BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack,
        Vec3i offset) =>
        Sounds;

    #endregion
}
