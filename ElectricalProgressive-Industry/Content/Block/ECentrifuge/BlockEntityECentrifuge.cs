using ElectricalProgressive.RicipeSystem;
using ElectricalProgressive.RicipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class BlockEntityECentrifuge : BlockEntityGenericTypedContainer
{
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;

    internal InventoryCentrifuge inventory;
    private GuiDialogCentrifuge clientDialog;
    public override string InventoryClassName => "ecentrifuge";
    public CentrifugeRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;

    public string CurrentRecipeName;
    public float RecipeProgress;

    public virtual string DialogTitle => Lang.Get("ecentrifuge");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    private Facing facing = Facing.None;

    private BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    private BlockEntityAnimationUtil animUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;



    public Facing Facing
    {
        get => this.facing;
        set
        {
            if (value != this.facing)
            {
                this.ElectricalProgressive!.Connection =
                    FacingHelper.FullFace(this.facing = value);
            }
        }
    }

    //передает значения из Block в BEBehaviorElectricalProgressive
    public (EParams, int) Eparams
    {
        get => this.ElectricalProgressive?.Eparams ?? (new EParams(), 0);
        set => this.ElectricalProgressive!.Eparams = value;
    }

    //передает значения из Block в BEBehaviorElectricalProgressive
    public EParams[] AllEparams
    {
        get => this.ElectricalProgressive?.AllEparams ?? new EParams[]
                    {
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams()
                    };
        set
        {
            if (this.ElectricalProgressive != null)
            {
                this.ElectricalProgressive.AllEparams = value;
            }
        }
    }


    public BlockEntityECentrifuge()
    { 
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this.inventory = new InventoryCentrifuge((string)null, (ICoreAPI)null);
        this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }



    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        this.inventory.LateInitialize(
          "ecentrifuge-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
        this.RegisterGameTickListener(new Action<float>(this.Every500ms), 500);
        
        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (animUtil != null)
            {
                animUtil.InitializeAnimator(InventoryClassName, null, null, new Vec3f(0, GetRotation(), 0f));
            }
        }
    }
    
    public int GetRotation()
    {
        string side = Block.Variant["side"];
        int adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }
    
    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI)
            this.clientDialog.Update(RecipeProgress);
        if (slotid != 0)
            return;
        if (this.InputSlot.Empty)
        {
            RecipeProgress = 0;
            StopAnimation();
        }
        this.MarkDirty();
        if (this.clientDialog == null || !this.clientDialog.IsOpened())
            return;
        this.clientDialog.SingleComposer.ReCompose();
        if (Api?.Side == EnumAppSide.Server)
        {
            FindMatchingRecipe();
            MarkDirty(true);
        }
    }
    
    public bool FindMatchingRecipe()
    {
        ItemSlot[] inputSlots = new ItemSlot[] { inventory[0] };
        CurrentRecipe = null;
        CurrentRecipeName = string.Empty;

        foreach (CentrifugeRecipe recipe in RecipeManager.CentrifugeRecipes)
        {
            int outsize;

            if (recipe.Matches(inputSlots, out outsize))
            {
                CurrentRecipe = recipe;
                CurrentRecipeName = recipe.Output.ResolvedItemstack.GetName();
                MarkDirty(true);
                return true;
            }
        }
        return false;
    }

    private void Every500ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorECentrifuge>();
        if (beh == null)
        {
            StopAnimation();
            return;
        }

        bool hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        bool hasRecipe = !InputSlot.Empty && FindMatchingRecipe();
        bool isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
            {                
                StartAnimation(beh.PowerSetting);
            }

            RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
            UpdateState(RecipeProgress);

            if (RecipeProgress >= 1f)
            {
                ProcessCompletedCraft();

                // Проверяем возможность следующего цикла без лишних вызовов
                bool canContinueCrafting = hasPower && !InputSlot.Empty && CurrentRecipe != null &&
                                           InputSlot.Itemstack.StackSize >= CurrentRecipe.Ingredients[0].Quantity;

                if (!canContinueCrafting)
                {
                    StopAnimation();
                }
                else
                {
                    RecipeProgress = 0f; // Сбрасываем для нового цикла
                    UpdateState(RecipeProgress);
                }
            }
        }
        else if (_wasCraftingLastTick)
        {
            StopAnimation();
            MarkDirty(true);
        }

        _wasCraftingLastTick = isCraftingNow;
    }

private void ProcessCompletedCraft()
{
    // Проверяем наличие рецепта и API
    if (CurrentRecipe == null || Api == null || CurrentRecipe.Output?.ResolvedItemstack == null) 
    {
        return;
    }

    try
    {
        // Создаем копию выходного предмета
        ItemStack outputItem = CurrentRecipe.Output.ResolvedItemstack.Clone();

        // Проверяем ингредиенты и слоты
        if (CurrentRecipe.Ingredients == null || CurrentRecipe.Ingredients.Length == 0 || InputSlot == null)
        {
            Api.Logger.Error("Ошибка в рецепте центрифуги: отсутствуют ингредиенты или входной слот");
            return;
        }

        // Обработка выходного слота
        if (OutputSlot == null)
        {
            Api.Logger.Error("Ошибка: выходной слот не существует");
            return;
        }

        if (OutputSlot.Empty)
        {
            OutputSlot.Itemstack = outputItem;
        }
        else if (OutputSlot.Itemstack != null && 
                outputItem.Collectible != null &&
                OutputSlot.Itemstack.Collectible == outputItem.Collectible &&
                OutputSlot.Itemstack.StackSize < OutputSlot.Itemstack.Collectible.MaxStackSize)
        {
            int freeSpace = OutputSlot.Itemstack.Collectible.MaxStackSize - OutputSlot.Itemstack.StackSize;
            int toAdd = Math.Min(freeSpace, outputItem.StackSize);

            OutputSlot.Itemstack.StackSize += toAdd;
            outputItem.StackSize -= toAdd;

            if (outputItem.StackSize > 0)
            {
                Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }
        else
        {
            Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        // Извлекаем ингредиенты из входного слота
        InputSlot.TakeOut(CurrentRecipe.Ingredients[0].Quantity);
        InputSlot.MarkDirty();
    }
    catch (Exception ex)
    {
        Api?.Logger.Error($"Ошибка в обработке крафта центрифуги: {ex}");
    }
}
    

    private void StartAnimation(float powerSetting)
    {
        if (Api?.Side != EnumAppSide.Client || animUtil == null || CurrentRecipe == null) return;


        if (animUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            animUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "craft",
                Code = "craft",
                AnimationSpeed = 1f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }

    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || animUtil == null) return;

        try
        {
            animUtil.StopAnimation("craft");
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Error stopping centrifuge animation: {ex}");
        }
    }

    protected virtual void UpdateState(float RecipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && clientDialog != null && clientDialog.IsOpened())
        {
            clientDialog.Update(RecipeProgress);
        }
        MarkDirty(true);
    }
    
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (this.Api.Side == EnumAppSide.Client)
            this.toggleInventoryDialogClient(byPlayer, (CreateDialogDelegate)(() =>
            {
                this.clientDialog =
                  new GuiDialogCentrifuge(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
                this.clientDialog.Update(RecipeProgress);
                return (GuiDialogBlockEntity)this.clientDialog;
            }));
        return true;
    }


    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        ElectricalProgressive?.OnReceivedServerPacket(packetid, data);

        if (packetid != 1001)
            return;
        (this.Api.World as IClientWorldAccessor).Player.InventoryManager.CloseInventory((IInventory)this.Inventory);
        this.invDialog?.TryClose();
        this.invDialog?.Dispose();
        this.invDialog = (GuiDialogBlockEntity)null;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");
        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);
        ICoreAPI api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0 || this.clientDialog == null)
            return;
        this.clientDialog.Update(RecipeProgress);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute tree1 = (ITreeAttribute)new TreeAttribute();
        this.Inventory.ToTreeAttributes(tree1);
        tree["inventory"] = (IAttribute)tree1;
        tree.SetFloat("PowerCurrent", this.RecipeProgress);
    }
    
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack == null)
            return;

        ElectricalProgressive.Connection = Facing.DownAll;

        var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

        this.ElectricalProgressive.Eparams = (
            new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        var electricity = ElectricalProgressive;
        if (electricity != null)
        {
            electricity.Connection = Facing.None;
        }
        
        if (this.Api is ICoreClientAPI && this.clientDialog != null)
        {
            this.clientDialog.TryClose();
            this.clientDialog = null;
        }
        
        StopAnimation();
        
        if (this.Api.Side == EnumAppSide.Client && this.animUtil != null)
        {
            this.animUtil.Dispose();
        }
    }
    
    public ItemSlot InputSlot => this.inventory[0];
    public ItemSlot OutputSlot => this.inventory[1];

    public ItemStack InputStack
    {
        get => this.inventory[0].Itemstack;
        set
        {
            this.inventory[0].Itemstack = value;
            this.inventory[0].MarkDirty();
        }
    }

    public ItemStack OutputStack
    {
        get => this.inventory[1].Itemstack;
        set
        {
            this.inventory[1].Itemstack = value;
            this.inventory[1].MarkDirty();
        }
    }
    
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.clientDialog?.TryClose();
    }
}