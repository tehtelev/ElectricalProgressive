using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

#nullable disable
namespace Vintagestory.GameContent;

public class ItemEGlider : Item, IWearableShapeSupplier
{
    private Vintagestory.API.Common.Shape gliderShape_unfoldStep1;
    private Vintagestory.API.Common.Shape gliderShape_unfoldStep2;
    private Vintagestory.API.Common.Shape gliderShape_unfolded;
    private bool subclassed;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.gliderShape_unfoldStep1 = Vintagestory.API.Common.Shape.TryGet(api, this.Attributes["unfoldShapeStep1"].AsObject<CompositeShape>().Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/"));
        this.gliderShape_unfoldStep2 = Vintagestory.API.Common.Shape.TryGet(api, this.Attributes["unfoldShapeStep2"].AsObject<CompositeShape>().Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/"));
        this.gliderShape_unfolded = Vintagestory.API.Common.Shape.TryGet(api, this.Attributes["unfoldedShape"].AsObject<CompositeShape>().Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/"));
    }

    public Vintagestory.API.Common.Shape GetShape(
        ItemStack stack,
        Entity forEntity,
        string texturePrefixCode)
    {
        if (!this.subclassed)
        {
            this.gliderShape_unfolded.SubclassForStepParenting(texturePrefixCode);
            this.gliderShape_unfoldStep1.SubclassForStepParenting(texturePrefixCode);
            this.gliderShape_unfoldStep2.SubclassForStepParenting(texturePrefixCode);
            this.subclassed = true;
        }
        switch (forEntity.Attributes.GetInt("unfoldStep", 0))
        {
            case 1:
                return this.gliderShape_unfoldStep1;
            case 2:
                return this.gliderShape_unfoldStep2;
            case 3:
                return this.gliderShape_unfolded;
            default:
                return (Vintagestory.API.Common.Shape) null;
        }
    }
}