using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.WallConnector
{
    public class BEBehaviorWConnector : BlockEntityBehavior, IEImmersiveConductor
    {
       
        private bool hasBurnout;
        private bool prepareBurnout;
        public bool IsOpen { get; set; }


        public const string IsOpenKey = "electricalprogressive:isopen";


        public BEBehaviorWConnector(BlockEntity blockEntity) : base(blockEntity)
        {
            
        }



        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            IsOpen = MyMiniLib.GetAttributeBool(this.Block, "IsOpen", true);
        }




        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            if (Blockentity is BlockEntityWConnector entity &&
                entity.EPImmersive != null)
            {
                // вычисляем высоту для частиц дыма
                /*
                var heightStr = entity.Block.Variant["height"];
                var height = heightStr.ToFloat() - 1;
                entity.ElectricalProgressive.ParticlesOffsetPos?.Clear();
                entity.ElectricalProgressive.ParticlesOffsetPos.Add(new Vec3d(0.1, height, 0.1));
                */
            }
        }


        public void Update()
        {
            if (Blockentity is not BlockEntityWConnector entity ||
                entity.EPImmersive == null)
            {
                return;
            }

            //entity.MarkDirty();

            bool anyBurnout = false;
            bool anyPrepareBurnout = false;

            var eParam = entity.EPImmersive.MainEparams();
            {
                if (!hasBurnout && eParam.burnout)
                {
                    hasBurnout = true;
                    entity.MarkDirty(true);
                }

                if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
                {
                    prepareBurnout = true;
                    entity.MarkDirty(true);
                }

                if (eParam.burnout)
                    anyBurnout = true;

                if (eParam.ticksBeforeBurnout > 0)
                    anyPrepareBurnout = true;
            }

            if (!anyBurnout && hasBurnout)
            {
                hasBurnout = false;
                entity.MarkDirty(true);
            }

            if (!anyPrepareBurnout && prepareBurnout)
            {
                prepareBurnout = false;
                entity.MarkDirty(true);
            }

            
        }



        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool(IsOpenKey, IsOpen);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            IsOpen = tree.GetBool(IsOpenKey);
        }
    }
}
