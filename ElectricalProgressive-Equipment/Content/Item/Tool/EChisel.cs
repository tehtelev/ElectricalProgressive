using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Tool;

class EChisel : ItemChisel
{
    int consume;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);
        
    }


    /// <summary>
    /// Уменьшаем прочность
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byEntity"></param>
    /// <param name="itemslot"></param>
    /// <param name="amount"></param>
    public override void DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, int amount = 1)
    {
        int durability = itemslot.Itemstack.Attributes.GetInt("durability");
        if (durability > amount)
        {
            durability -= amount;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);            
        }
        else
        {
            durability = 1;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);
        }

        itemslot.MarkDirty();
    }


    /// <summary>
    /// Нажатие левой кнопки
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSel"></param>
    /// <param name="entitySel"></param>
    /// <param name="handling"></param>
    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {

        int durability = slot.Itemstack.Attributes.GetInt("durability");
        if (durability > 1)
        {
            durability -= 1;
            slot.Itemstack.Attributes.SetInt("durability", durability);

            // код ниже взят из ванильно чизла

            if (!(blockSel?.Position == null))
            {
                IPlayer player = (byEntity as EntityPlayer)!.Player;
                BlockPos position = blockSel.Position;
                var block = byEntity.World.BlockAccessor.GetBlock(position);
                ModSystemBlockReinforcement modSystem = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
                if (modSystem != null && modSystem.IsReinforced(position))
                {
                    player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                }
                else if (!byEntity.World.Claims.TryAccess(player, position, EnumBlockAccessFlags.BuildOrBreak))
                {
                    player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                }
                else if (!IsChiselingAllowedFor(api, position, block, player))
                {
                    base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                }
                else if (blockSel == null)
                {
                    base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                }
                else if (block is BlockChisel)
                {
                    OnBlockInteract(byEntity.World, player, blockSel, isBreak: true, ref handling);
                }
            }


            //base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
        }
        else
        {
            durability = 1;
            slot.Itemstack.Attributes.SetInt("durability", durability);

        }

        slot.MarkDirty();
    }

    /// <summary>
    /// Нажатие правой кнопки
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSel"></param>
    /// <param name="entitySel"></param>
    /// <param name="firstEvent"></param>
    /// <param name="handling"></param>
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        int durability = slot.Itemstack.Attributes.GetInt("durability");
        if (durability > 1)
        {
            durability -= 1;
            slot.Itemstack.Attributes.SetInt("durability", durability);


            // код ниже взят из ванильно чизла
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            if (handling == EnumHandHandling.PreventDefault)
            {
                return;
            }


            if (blockSel?.Position == null)
            {
                return;
            }

            BlockPos position = blockSel.Position;
            var block = byEntity.World.BlockAccessor.GetBlock(position);
            IPlayer player = (byEntity as EntityPlayer)!.Player;

            ModSystemBlockReinforcement modSystem = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (modSystem != null && modSystem.IsReinforced(position))
            {
                player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return;
            }

            if (!byEntity.World.Claims.TryAccess(player, position, EnumBlockAccessFlags.BuildOrBreak))
            {
                player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return;
            }

            if (block is BlockGroundStorage)
            {
                ItemSlot firstNonEmptySlot = (api.World.BlockAccessor.GetBlockEntity(position) as BlockEntityGroundStorage)!.Inventory.FirstNonEmptySlot;
                if (firstNonEmptySlot != null && firstNonEmptySlot.Itemstack.Block != null && IsChiselingAllowedFor(api, position, firstNonEmptySlot.Itemstack.Block, player))
                {
                    block = firstNonEmptySlot.Itemstack.Block;
                }

                if (block.Code.Path == "pumpkin-fruit-4" && (!carvingTime || !AllowHalloweenEvent))
                {
                    player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                    api.World.BlockAccessor.MarkBlockDirty(position);
                    return;
                }
            }

            if (!IsChiselingAllowedFor(api, position, block, player))
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            if (block.Resistance > 100f)
            {
                if (api.Side == EnumAppSide.Client)
                {
                    (api as ICoreClientAPI)!.TriggerIngameError(this, "tootoughtochisel", Lang.Get("This material is too strong to chisel"));
                }

                return;
            }

            if (blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            if (block is BlockChisel)
            {
                OnBlockInteract(byEntity.World, player, blockSel, isBreak: false, ref handling);
                return;
            }

            var block2 = byEntity.World.GetBlock(new AssetLocation("chiseledblock"));
            byEntity.World.BlockAccessor.SetBlock(block2.BlockId, blockSel.Position);
            if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityChisel blockEntityChisel)
            {
                blockEntityChisel.WasPlaced(block, null);
                if (carvingTime && block.Code.Path == "pumpkin-fruit-4")
                {
                    blockEntityChisel.AddMaterial(api.World.GetBlock(new AssetLocation("creativeglow-35")));
                }

                handling = EnumHandHandling.PreventDefaultAction;
            }

        }
        else
        {
            durability = 1;
            slot.Itemstack.Attributes.SetInt("durability", durability);
        }

        slot.MarkDirty();
    }



    /// <summary>
    /// Информация о предмете
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        int energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume; //текущая энергия
        int maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;       //максимальная энергия
        dsc.AppendLine(energy + "/" + maxEnergy + " " + Lang.Get("J"));
    }


}