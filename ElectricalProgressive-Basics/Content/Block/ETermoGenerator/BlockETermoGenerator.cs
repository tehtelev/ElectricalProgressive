using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;


namespace ElectricalProgressive.Content.Block.ETermoGenerator;

public class BlockETermoGenerator : BlockEBase
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



    /// <summary>
    /// Проверка возможности установки блока
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="itemstack"></param>
    /// <param name="blockSel"></param>
    /// <param name="failureCode"></param>
    /// <returns></returns>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
       BlockSelection blockSel, ref string failureCode)
    {
        //неваляжка - только вертикально
        // целая ли грань, на которую ставим
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }




    /// <summary>
    /// ставим блок
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <param name="byItemStack"></param>
    /// <returns></returns>
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        // если блок сгорел, то не ставим
        if (byItemStack.Block.Variant["type"] == "burned")
        {
            return false;
        }

        var selection = new Selection(blockSel);

        var facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityETermoGenerator entity
        )
        {
            LoadEProperties.Load(this, entity);
            
            return true;
        }

        return false;
    }




    /// <summary>
    /// Обработчик изменения соседнего блока
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="neibpos"></param>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityETermoGenerator)
        {

            // целая ли грань еще
            if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
            {
                return;
            }

            // иначе ломаем
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }


    /// <summary>
    /// Взаимодействие с блоком
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Проверка прав на использование
        if (blockSel != null && !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            return false;
        }

        // текущее выбранное в руке
        var stack = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;

        // получаем блокэнтити
        var bef = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityETermoGenerator;


        // если есть блокэнтити и в руке что-то есть
        if (bef != null && stack != null)
        {
            // флаг, что что-то поменяли
            var activated = false;

            // шифт нажата
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                if (stack.Collectible.CombustibleProps != null && stack.Collectible.CombustibleProps.MeltingPoint > 0)
                {
                    var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                    byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(bef.FuelSlot, ref op);
                    if (op.MovedQuantity > 0)
                        activated = true;
                }

                if (stack.Collectible.CombustibleProps != null && stack.Collectible.CombustibleProps.BurnTemperature > 0)
                {
                    var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                    byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(bef.FuelSlot, ref op);
                    if (op.MovedQuantity > 0)
                        activated = true;
                }
            }

            if (activated)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                var loc = stack.ItemAttributes?["placeSound"].Exists == true ? AssetLocation.Create(stack.ItemAttributes["placeSound"].AsString(), stack.Collectible.Code.Domain) : null;

                if (loc != null)
                {
                    api.World.PlaySoundAt(loc.WithPathPrefixOnce("sounds/"), blockSel.Position.X, blockSel.Position.InternalY, blockSel.Position.Z, byPlayer, 0.88f + (float)api.World.Rand.NextDouble() * 0.24f, 16);
                }

                return true;
            }
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);


    }


    


    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }



    /// <summary>
    /// Получение подсказок для взаимодействия с блоком
    /// </summary>
    /// <param name="world"></param>
    /// <param name="selection"></param>
    /// <param name="forPlayer"></param>
    /// <returns></returns>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions;
    }





    /// <summary>
    /// Получение информации о предмете в инвентаре
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var block = inSlot.Itemstack?.Block;
        if (block == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }
}