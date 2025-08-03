using System;
using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Tool;

class EAxe : ItemAxe
{
    int consume;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);

    }


    private static SimpleParticleProperties dustParticles; //частицы пыли

    static EAxe()
    {
        dustParticles = new SimpleParticleProperties
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(),
            MinQuantity = 0f,
            AddQuantity = 3f,
            Color = ColorUtil.ToRgba(100, 200, 200, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1f, 2f, -1f),
            AddVelocity = new Vec3f(2f, 0f, 2f),
            MinSize = 0.07f,
            MaxSize = 0.1f,
            WindAffected = true
        };
        dustParticles.ParticleModel = EnumParticleModel.Quad;
        dustParticles.AddPos.Set(1.0, 1.0, 1.0);
        dustParticles.MinQuantity = 2f;
        dustParticles.AddQuantity = 12f;
        dustParticles.LifeLength = 4f;
        dustParticles.MinSize = 0.2f;
        dustParticles.MaxSize = 0.5f;
        dustParticles.MinVelocity.Set(-0.4f, -0.4f, -0.4f);
        dustParticles.AddVelocity.Set(0.8f, 1.2f, 0.8f);
        dustParticles.DieOnRainHeightmap = false;
        dustParticles.WindAffectednes = 0.5f;
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

            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
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

            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        }
        else
        {
            durability = 1;
            slot.Itemstack.Attributes.SetInt("durability", durability);
        }

        slot.MarkDirty();
    }

    /// <summary>
    /// Ломаем блок топором
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byEntity"></param>
    /// <param name="itemslot"></param>
    /// <param name="blockSel"></param>
    /// <param name="dropQuantityMultiplier"></param>
    /// <returns></returns>
    public override bool OnBlockBrokenWith(
      IWorldAccessor world,
      Entity byEntity,
      ItemSlot itemslot,
      BlockSelection blockSel,
      float dropQuantityMultiplier = 1f)
    {
        int durability = itemslot.Itemstack.Attributes.GetInt("durability"); //текущая энергия
        if (durability > 1)
        {
            IPlayer? player = null;
            if (byEntity is EntityPlayer)
            {
                player = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            }


            int resistance;
            int woodTier;
            Stack<BlockPos> stack = FindTree(world, blockSel.Position, out resistance, out woodTier);
            if (stack.Count == 0)
            {
                return base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
            }

            bool flag = DamagedBy != null && DamagedBy.Contains(EnumItemDamageSource.BlockBreaking);
            float num2 = 1f;
            float num3 = 0.8f;
            int num4 = 0;
            bool flag2 = true;
            double num = api.ModLoader.GetModSystem<WeatherSystemBase>()?.WeatherDataSlowAccess.GetWindSpeed(byEntity.SidedPos.XYZ) ?? 0.0;
            while (stack.Count > 0)
            {
                BlockPos blockPos = stack.Pop();
                var block = world.BlockAccessor.GetBlock(blockPos);
                bool flag3 = block.BlockMaterial == EnumBlockMaterial.Wood;
                if (flag3 && !flag2)
                {
                    continue;
                }

                num4++;
                bool flag4 = block.Code.Path.Contains("branchy");
                bool flag5 = block.BlockMaterial == EnumBlockMaterial.Leaves;
                world.BlockAccessor.BreakBlock(blockPos, player, flag5 ? num2 : (flag4 ? num3 : 1f));
                if (world.Side == EnumAppSide.Client)
                {
                    dustParticles.Color = block.GetRandomColor(world.Api as ICoreClientAPI, blockPos, BlockFacing.UP);
                    dustParticles.Color |= -16777216;
                    dustParticles.MinPos.Set(blockPos.X, blockPos.Y, blockPos.Z);
                    if (block.BlockMaterial == EnumBlockMaterial.Leaves)
                    {
                        dustParticles.GravityEffect = (float)world.Rand.NextDouble() * 0.1f + 0.01f;
                        dustParticles.ParticleModel = EnumParticleModel.Quad;
                        dustParticles.MinVelocity.Set(-0.4f + 4f * (float)num, -0.4f, -0.4f);
                        dustParticles.AddVelocity.Set(0.8f + 4f * (float)num, 1.2f, 0.8f);
                    }
                    else
                    {
                        dustParticles.GravityEffect = 0.8f;
                        dustParticles.ParticleModel = EnumParticleModel.Cube;
                        dustParticles.MinVelocity.Set(-0.4f + (float)num, -0.4f, -0.4f);
                        dustParticles.AddVelocity.Set(0.8f + (float)num, 1.2f, 0.8f);
                    }

                    world.SpawnParticles(dustParticles);
                }

                if (flag && flag3)
                {
                    DamageItem(world, byEntity, itemslot);
                    if (itemslot.Itemstack == null)
                    {
                        flag2 = false;
                    }
                }

                if (flag5 && num2 > 0.03f)
                {
                    num2 *= 0.85f;
                }

                if (flag4 && num3 > 0.015f)
                {
                    num3 *= 0.7f;
                }
            }

            if (num4 > 35 && flag2)
            {
                api.World.PlaySoundAt(new AssetLocation("sounds/effect/treefell"), blockSel.Position, -0.25, player, randomizePitch: false, 32f, GameMath.Clamp((float)num4 / 100f, 0.25f, 1f));
            }

            return true;
        }
        else
            return false;

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