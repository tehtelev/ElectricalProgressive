using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.EAcidAccum;

public class BlockEntityEAcidAccum : BlockEntityGenericTypedContainer
{
    private readonly InventoryEAcidAccum _inventory;
    private ICoreClientAPI? _capi;
    private GuiDialogEAcidAccum? _clientDialog;
    private MeshData? _liquidMesh;
    private string? _liquidMeshKey;

    public override InventoryBase Inventory => _inventory;
    public override string DialogTitle => Lang.Get("electricalprogressivebasics:eacidaccum-title-gui");
    public override string InventoryClassName => "eacidaccum";

    public BEBehaviorEPImmersive? EPImmersive => GetBehavior<BEBehaviorEPImmersive>();
    public BEBehaviorEAcidAccum? AccumBehavior => GetBehavior<BEBehaviorEAcidAccum>();

    public bool StructureComplete
    {
        get
        {
            var construct = GetBehavior<MachineConstruct>();
            if (construct != null && construct.HasConstruction)
                return construct.IsReady;
            return true;
        }
    }

    public bool IsFormed => Block?.Variant?["state"] == "formed";

    public float LiquidAmount
    {
        get
        {
            if (_inventory.LiquidSlot.Empty)
                return 0f;
            var props = BlockLiquidContainerBase.GetContainableProps(_inventory.LiquidSlot.Itemstack);
            if (props == null)
                return 0f;
            return _inventory.LiquidSlot.Itemstack.StackSize / props.ItemsPerLitre;
        }
    }

    public float LiquidCapacity
    {
        get
        {
            if (Block?.Attributes?["capacityLitres"].Exists == true)
                return Block.Attributes["capacityLitres"].AsFloat(100f);
            return 100f;
        }
    }

    public ItemSlot LiquidSlot => _inventory.LiquidSlot;

    public ItemStack? LiquidStack
    {
        get => _inventory.LiquidSlot.Itemstack;
        set
        {
            _inventory.LiquidSlot.Itemstack = value;
            _inventory.LiquidSlot.MarkDirty();
        }
    }

    public BlockEntityEAcidAccum()
    {
        _inventory = new InventoryEAcidAccum();
        _inventory.SlotModified += _ =>
        {
            _liquidMesh = null;
            MarkDirty(true);
        };
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.LateInitialize("eacidaccum-" + Pos, api);
        _inventory.SetBlockPos(Pos);
        if (IsFormed)
            LoadImmersiveEProperties.Load(Block, this);
        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            RegisterGameTickListener(_ => _clientDialog?.Update(), 50);
        }
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        LoadImmersiveEProperties.Load(Block, this);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogEAcidAccum(DialogTitle, Inventory, Pos, _capi!, this);
                return _clientDialog;
            });
        }
        return true;
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        if (LiquidSlot != null)
            LiquidSlot.Itemstack = null;
        base.OnBlockBroken(byPlayer);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var invTree = new TreeAttribute();
        _inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;
    }

    public int TryPutLiquidFromStack(ItemStack liquidStack, float desiredLitres)
    {
        if (liquidStack == null || !InventoryEAcidAccum.IsSulfuricAcid(liquidStack))
            return 0;

        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable)
            return 0;

        float itemsPerLitre = props.ItemsPerLitre;
        int desiredItems = (int)(itemsPerLitre * desiredLitres);
        float availItems = liquidStack.StackSize;
        float maxItems = LiquidCapacity * itemsPerLitre;
        var currentStack = LiquidStack;

        if (currentStack == null)
        {
            int movedItems = (int)GameMath.Min(desiredItems, maxItems, availItems);
            if (movedItems <= 0)
                return 0;
            var placed = liquidStack.Clone();
            placed.StackSize = movedItems;
            LiquidStack = placed;
            MarkDirty(true);
            return movedItems;
        }

        if (!currentStack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes))
            return 0;

        int canAdd = (int)Math.Min(availItems, maxItems - currentStack.StackSize);
        int moved = Math.Min(canAdd, desiredItems);
        if (moved <= 0)
            return 0;
        currentStack.StackSize += moved;
        LiquidSlot.MarkDirty();
        MarkDirty(true);
        return moved;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var mesh = GetLiquidMesh();
        if (mesh != null)
            mesher.AddMeshData(mesh);
        return base.OnTesselation(mesher, tesselator);
    }

    private MeshData? GetLiquidMesh()
    {
        if (_capi == null || LiquidSlot == null || LiquidSlot.Empty)
            return null;
        if (!InventoryEAcidAccum.IsSulfuricAcid(LiquidStack))
            return null;
        var fill = LiquidCapacity > 0 ? LiquidAmount / LiquidCapacity : 0f;
        if (fill < 0.02f)
            return null;
        var stack = LiquidStack;
        if (stack?.Collectible == null)
            return null;

        var rotY = Block?.Shape?.rotateY ?? 0;
        if (Math.Abs(rotY) < 0.01f)
        {
            rotY = Block?.Variant?["side"] switch
            {
                "east" => 270,
                "south" => 180,
                "west" => 90,
                _ => 0
            };
        }

        var key = stack.Collectible.Code + "@" + (int)(fill * 40) + "@" + rotY;
        if (_liquidMesh != null && _liquidMeshKey == key)
            return _liquidMesh;

        var props = BlockLiquidContainerBase.GetContainableProps(stack);
        var shape = Shape.TryGet(_capi,
            new AssetLocation("electricalprogressivebasics", "shapes/block/eacidaccum/liquidcontents.json"))?.Clone();
        if (shape == null)
            return null;

        ITexPositionSource texSource;
        if (props?.Texture != null)
            texSource = new ContainerTextureSource(_capi, stack, props.Texture);
        else
            texSource = new ContainerTextureSource(_capi, stack,
                new CompositeTexture(new AssetLocation("game", "block/liquid/dye/yellow")));

        _capi.Tesselator.TesselateShape("eacidaccum-liquid", shape, out var mesh, texSource);
        if (mesh == null || mesh.VerticesCount <= 0)
            return null;

        mesh.Translate(0, GameMath.Clamp(fill, 0.02f, 1f) * (19.5f / 16f), 0);
        if (Math.Abs(rotY) > 0.01f)
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotY * GameMath.DEG2RAD, 0);

        _liquidMesh = mesh;
        _liquidMeshKey = key;
        return mesh;
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        EPImmersive?.OnBlockUnloaded();
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        if (tree.HasAttribute("inventory"))
            _inventory.FromTreeAttributes(tree["inventory"] as ITreeAttribute);
        _liquidMesh = null;
        if (Api?.Side == EnumAppSide.Client)
            _clientDialog?.Update();
    }
}
