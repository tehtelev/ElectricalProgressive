﻿using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ERecycler;

public class BlockEntityERecycler : BlockEntityGenericTypedContainer
{
    internal InventoryRecycler _inventory;
    private GuiDialogRecycler _clientDialog;
    private static MeshData? _mesh;
    private static Shape? _resultingShape;
    public override string InventoryClassName => "erecycler";
    public RecyclerRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;

    public string CurrentRecipeName;
    public float RecipeProgress;
    private ILoadedSound _ambientSound;
    
    /// <summary>
    /// Накопленная энергия для текущего рецепта (целые единицы)
    /// </summary>
    public int AccumulatedEnergy { get; set; }

    public override string DialogTitle => Lang.Get("erecycler-title-gui");

    public override InventoryBase Inventory => this._inventory;

    private BlockEntityAnimationUtil? AnimUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public Facing Facing
    {
        get => this._facing;
        set
        {
            if (value != this._facing)
            {
                this.ElectricalProgressive!.Connection =
                    FacingHelper.FullFace(this._facing = value);
            }
        }
    }

    private Facing _facing = Facing.None;
    private AssetLocation _recyclerSound;

    public BlockEntityERecycler()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this._inventory = new InventoryRecycler(2, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
        this._inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }

    /// <summary>
    /// Добавить энергию для обработки рецепта
    /// </summary>
    public void AddEnergy(int amount)
    {
        if (CurrentRecipe == null || InputSlot.Empty)
            return;
    
        if (amount <= 0)
            return;
        
        // Ограничиваем добавление энергии, чтобы не перескочить через лимит
        int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
        if (maxNeeded <= 0)
        {
            // Если уже накоплено достаточно для крафта - завершаем его
            while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && !InputSlot.Empty)
            {
                AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                ProcessCompletedCraft();
            
                if (InputSlot.Empty || CurrentRecipe == null)
                    break;
            }
            return;
        }
    
        int energyToAdd = Math.Min(amount, maxNeeded);
        AccumulatedEnergy += energyToAdd;
    
        // Обновляем прогресс для UI
        if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
        {
            RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
            UpdateState(RecipeProgress);
        }
    
        // Проверяем, не накопилось ли достаточно для завершения
        if (AccumulatedEnergy >= CurrentRecipe.EnergyOperation)
        {
            while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && !InputSlot.Empty)
            {
                AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                ProcessCompletedCraft();
            
                if (InputSlot.Empty || CurrentRecipe == null)
                    break;
            }
        }
    
        MarkDirty(true);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        this._inventory.LateInitialize(
            InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);

        this.RegisterGameTickListener(new Action<float>(this.Every1000Ms), 1000);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (AnimUtil != null)
            {
                PrepareAnimUtil(api, InventoryClassName);
                AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
            }

            _recyclerSound = new AssetLocation("electricalprogressiveindustry:sounds/erecycler/erecycler.ogg");
        }
    }

    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (_mesh == null || _resultingShape == null)
        {
            AssetLocation shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            Shape _shape = Shape.TryGet(api, shapePath);

            _mesh = AnimUtil.CreateMesh(cacheDictKey, _shape, out _resultingShape, null);
        }
    }

    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
            this._clientDialog.Update(RecipeProgress);

        if (slotid != 0)
            return;

        // Защита от горячей смены стака
        if (slotid == 0 && RecipeProgress < 1f)
        {
            RecipeProgress = 0f;
            AccumulatedEnergy = 0;
            UpdateState(RecipeProgress);
        }

        if (this.InputSlot.Empty)
        {
            RecipeProgress = 0;
            AccumulatedEnergy = 0;
            StopAnimation();
            StopSound();
            CurrentRecipe = null;
        }
        else
        {
            FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]);
            if (CurrentRecipe == null)
            {
                FindPerishProperties(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]);
            }
        }
    
        this.MarkDirty();

        if (this._clientDialog == null || !this._clientDialog.IsOpened())
            return;

        this._clientDialog.SingleComposer.ReCompose();

        if (Api?.Side == EnumAppSide.Server)
        {
            MarkDirty(true);
        }
    }

    public static bool FindMatchingRecipe(ref RecyclerRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        ItemSlot[] inputSlots = [inputSlot];
        currentRecipe = null;
        currentRecipeName = string.Empty;

        foreach (var recipe in ElectricalProgressiveRecipeManager.RecyclerRecipes)
        {
            if (recipe.Matches(inputSlots, out _))
            {
                currentRecipe = recipe;
                if (recipe.Outputs != null && recipe.Outputs.Length > 0)
                {
                    currentRecipeName = recipe.Outputs[0].ResolvedItemstack?.GetName() ?? "Unknown";
                }
                return true;
            }
        }
        return false;
    }

    public static bool FindPerishProperties(ref RecyclerRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        var transProps = inputSlot.Itemstack.Collectible.TransitionableProps;
        if (transProps != null)
        {
            foreach (var prop in transProps)
            {
                if (prop.Type == EnumTransitionType.Perish)
                {
                    var inputSize = 1;
                    var outputSize = 1;
                    double coeff = 0;

                    if (prop.TransitionedStack.Code.Path == "rot")
                    {
                        coeff = Math.Ceiling(8.0f / (prop.TransitionedStack.StackSize * prop.TransitionRatio));
                        inputSize = (int)coeff;
                        if (coeff < 1)
                        {
                            outputSize = (int)Math.Floor((prop.TransitionedStack.StackSize * prop.TransitionRatio) / 8.0f);
                        }
                    }
                    else
                    {
                        continue;
                    }

                    foreach (var recipe in ElectricalProgressiveRecipeManager.RecyclerRecipes)
                    {
                        if (recipe.Code == "default_perish" && inputSlot.StackSize >= inputSize)
                        {
                            recipe.Ingredients[0].Quantity = inputSize;
                            if (recipe.Outputs != null && recipe.Outputs.Length > 0)
                            {
                                recipe.Outputs[0].StackSize = outputSize;
                            }

                            currentRecipe = recipe;
                            if (recipe.Outputs != null && recipe.Outputs.Length > 0)
                            {
                                currentRecipeName = recipe.Outputs[0].ResolvedItemstack?.GetName() ?? "Unknown";
                            }
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private void Every1000Ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorERecycler>();
        if (beh == null)
        {
            StopAnimation();
            StopSound();
            return;
        }

        if (ElectricalProgressive == null &&
            ElectricalProgressive.AllEparams == null &&
            ElectricalProgressive.AllEparams.Any(e => e.burnout))
            return;

        var stack = InputSlot?.Itemstack;

        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
        {
            if (_wasCraftingLastTick)
            {
                StopAnimation();
                StopSound();
                _wasCraftingLastTick = false;
            }
            return;
        }

        var hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        var hasRecipe = !InputSlot.Empty
                        && (BlockEntityERecycler.FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName,
                                Inventory[0])
                            || FindPerishProperties(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]));
        var isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
            {
                StartSound();
            }

            StartAnimation();
            
            // Обновляем прогресс из накопленной энергии
            if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                UpdateState(RecipeProgress);
            }
        }
        else if (_wasCraftingLastTick)
        {
            StopAnimation();
            StopSound();
            MarkDirty(true);
        }

        _wasCraftingLastTick = isCraftingNow;
    }

    private void ProcessCompletedCraft()
    {
        if (CurrentRecipe == null
            || Api == null
            || CurrentRecipe.Outputs == null
            || CurrentRecipe.Outputs.Length == 0)
        {
            return;
        }
   
        try
        {
            foreach (var output in CurrentRecipe.Outputs)
            {
                if (output.Chance < 1.0f && Api.World.Rand.NextDouble() > output.Chance)
                {
                    continue;
                }

                var outputItem = output.ResolvedItemstack?.Clone();
                if (outputItem == null) continue;

                if (CurrentRecipe.Ingredients == null || CurrentRecipe.Ingredients.Length == 0 || InputSlot == null)
                {
                    Api.Logger.Error("Ошибка в рецепте: отсутствуют ингредиенты или входной слот");
                    return;
                }

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
                    var freeSpace = OutputSlot.Itemstack.Collectible.MaxStackSize - OutputSlot.Itemstack.StackSize;
                    var toAdd = Math.Min(freeSpace, outputItem.StackSize);

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
            }

            InputSlot.TakeOut(CurrentRecipe.Ingredients[0].Quantity);
            InputSlot.MarkDirty();
            
            // Проверяем, можно ли продолжить с тем же рецептом
            if (!InputSlot.Empty && CurrentRecipe != null)
            {
                if (!FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]) &&
                    !FindPerishProperties(ref CurrentRecipe, ref CurrentRecipeName, Inventory[0]))
                {
                    CurrentRecipe = null;
                    AccumulatedEnergy = 0;
                    RecipeProgress = 0;
                }
            }
            else
            {
                CurrentRecipe = null;
                RecipeProgress = 0;
            }
        }
        catch (Exception ex)
        {
            Api?.Logger.Error($"Ошибка в обработке крафта: {ex}");
        }
    }

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client
            || AnimUtil == null
            || CurrentRecipe == null)
            return;

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
        {
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 1f,
                EaseOutSpeed = 4f,
                EaseInSpeed = 1f
            });
        }
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
        {
            AnimUtil.StopAnimation("work-on");
        }
    }

    public void StartSound()
    {
        if (this._ambientSound != null)
            return;
        if ((Api != null ? (Api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
            return;
        this._ambientSound = (this.Api as ICoreClientAPI).World.LoadSound(new SoundParams()
        {
            Location = _recyclerSound,
            ShouldLoop = true,
            Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 1f,
        });

        this._ambientSound.Start();
    }

    public void StopSound()
    {
        if (this._ambientSound == null)
            return;
        this._ambientSound.Stop();
        this._ambientSound?.Dispose();
        this._ambientSound = (ILoadedSound)null;
    }

    protected virtual void UpdateState(float recipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(recipeProgress);
        }
        MarkDirty(true);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (this.Api.Side == EnumAppSide.Client)
            this.toggleInventoryDialogClient(byPlayer, (CreateDialogDelegate)(() =>
            {
                this._clientDialog =
                  new GuiDialogRecycler(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
                this._clientDialog.Update(RecipeProgress);
                return (GuiDialogBlockEntity)this._clientDialog;
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
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");
        this.AccumulatedEnergy = tree.GetInt("accumulatedEnergy");
        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);
        var api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0 || this._clientDialog == null)
            return;
        this._clientDialog.Update(RecipeProgress);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var tree1 = (ITreeAttribute)new TreeAttribute();
        this.Inventory.ToTreeAttributes(tree1);
        tree["_inventory"] = (IAttribute)tree1;
        tree.SetFloat("PowerCurrent", this.RecipeProgress);
        tree.SetInt("accumulatedEnergy", this.AccumulatedEnergy);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack == null)
            return;

        LoadEProperties.Load(this.Block, this);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (ElectricalProgressive != null)
        {
            ElectricalProgressive.Connection = Facing.None;
        }

        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog?.TryClose();
            this._clientDialog = null;
        }

        StopAnimation();

        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil?.Dispose();
        }

        if (this._ambientSound != null)
        {
            this._ambientSound.Stop();
            this._ambientSound?.Dispose();
        }

        _mesh?.Dispose();
        _resultingShape = null;
    }

    public ItemSlot InputSlot => this._inventory[0];
    public ItemSlot OutputSlot => this._inventory[1];

    public ItemStack InputStack
    {
        get => this._inventory[0].Itemstack;
        set
        {
            this._inventory[0].Itemstack = value;
            this._inventory[0].MarkDirty();
        }
    }

    public ItemStack OutputStack
    {
        get => this._inventory[1].Itemstack;
        set
        {
            this._inventory[1].Itemstack = value;
            this._inventory[1].MarkDirty();
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._clientDialog?.TryClose();
        if (this._ambientSound == null)
            return;
        this._ambientSound.Stop();
        this._ambientSound?.Dispose();
        this._ambientSound = (ILoadedSound)null;

        _mesh?.Dispose();
        _resultingShape = null;
    }
}