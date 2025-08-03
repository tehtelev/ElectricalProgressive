using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ECable
{
    public class BlockEntityECable : BlockEntityEBase
    {
        private Facing switches = Facing.None;
        private Facing orientation = Facing.None;

        public Facing Connection  //соединение этого провода
        {
            get => this.ElectricalProgressive?.Connection ?? Facing.None;
            set
            {
                if (this.ElectricalProgressive != null)
                {
                    this.ElectricalProgressive.Connection = value;
                }
            }
        }


        public Facing Orientation
        {
            get => this.orientation;
            set => this.orientation = value;
        }

        public Facing Switches
        {
            get => this.switches;
            set => this.ElectricalProgressive!.Interruption &= this.switches = value;
        }

        public const string SwitchesKey = "electricalprogressive:switches";
        public const string OrientationKey = "electricalprogressive:orientation";


        public Facing SwitchesState
        {
            get => ~this.ElectricalProgressive!.Interruption;
            set => this.ElectricalProgressive!.Interruption = this.switches & ~value;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBytes(SwitchesKey, SerializerUtil.Serialize(this.switches));
            tree.SetBytes(OrientationKey, SerializerUtil.Serialize(this.orientation));
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                this.switches = SerializerUtil.Deserialize<Facing>(tree.GetBytes(SwitchesKey));
                this.orientation = SerializerUtil.Deserialize<Facing>(tree.GetBytes(OrientationKey));
            }
            catch (Exception exception)
            {
                this.Api?.Logger.Error(exception.ToString());
            }
        }
    }
}
