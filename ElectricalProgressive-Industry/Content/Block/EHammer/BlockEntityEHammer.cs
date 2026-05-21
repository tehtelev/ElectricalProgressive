﻿using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;


namespace ElectricalProgressive.Content.Block.EHammer;

public class BlockEntityEHammer : BlockEntityGenericTypedContainer, ITexPositionSource
{
    internal InventoryHammer inventory;
    private GuiDialogHammer _clientDialog;

    private static MeshData? _mesh;
    private static Shape? _resultingShape;
    public override string InventoryClassName => "ehammer";
    public HammerRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;
    public ItemSlot InputSlot => this.inventory[0];
    public ItemSlot OutputSlot => this.inventory[1];
    public ItemSlot SecondaryOutputSlot => this.inventory[2];

    public string CurrentRecipeName;
    public float RecipeProgress;
    
    /// <summary>
    /// Накопленная энергия для текущего рецепта (целые единицы)
    /// </summary>
    public int AccumulatedEnergy { get; set; }

    private static float _maxTargetTemp = 1350f;

    public override string DialogTitle => Lang.Get("ehammer-title-gui");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    private int _lastSoundFrame = -1;
    private long _lastAnimationCheckTime;
    private BlockEntityAnimationUtil AnimUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    // Новые поля для системы мешей (как в холодильнике)
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;

    //--------------------------------------------------------------------------------

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

    //--------------------------------------------------------------------------------

    private AssetLocation _soundHammer;
    private Facing _facing = Facing.None;

    public BlockEntityEHammer()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this.inventory = new InventoryHammer(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
        this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }

    /// <summary>
    /// Добавить энергию для обработки рецепта
    /// </summary>
    public void AddEnergy(int amount)
    {
        if (CurrentRecipe == null || InputSlot.Empty)
            return;
            
        AccumulatedEnergy += amount;
        
        // Проверяем, достаточно ли энергии для завершения
        while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && !InputSlot.Empty)
        {
            AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
            ProcessCompletedCraft();
            
            // После крафта проверяем, можно ли продолжить
            if (InputSlot.Empty || CurrentRecipe == null)
                break;
        }
        
        // Обновляем прогресс для UI
        if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
        {
            RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
            
            // Обновляем температуру предмета на основе прогресса
            if (InputSlot?.Itemstack != null)
            {
                var stack = InputSlot.Itemstack;
                if (RecipeProgress < 0.5f)
                {
                    stack.Collectible.SetTemperature(this.Api.World, stack, RecipeProgress * 2 * _maxTargetTemp);
                }
                else
                {
                    stack.Collectible.SetTemperature(this.Api.World, stack, _maxTargetTemp);
                }
            }
            
            UpdateState(RecipeProgress);
        }
        
        MarkDirty(true);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);

        this.RegisterGameTickListener(new Action<float>(this.Every1000Ms), 1000);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;

            // Инициализируем массив мешей как в холодильнике
            _meshes = new MeshData[this.inventory.Count];

            // Подписываемся на изменения инвентаря
            this.inventory.SlotModified += slotId =>
            {
                UpdateMeshes();
            };

            // Первоначальное создание мешей
            UpdateMeshes();

            if (AnimUtil != null)
            {
                PrepareAnimUtil(api, InventoryClassName);
                AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
            }

            _soundHammer = new AssetLocation("electricalprogressiveindustry:sounds/ehammer/hammer.ogg");

            this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
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

    private void CheckAnimationFrame(float dt)
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        const int startFrame = 27;
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("craft"))
        {
            var currentTime = Api.World.ElapsedMilliseconds;
            _lastAnimationCheckTime = currentTime;

            var currentFrame = AnimUtil.animator.Animations[0].CurrentFrame;
            if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
            {
                PlayHammerSound();
                _lastSoundFrame = startFrame;
            }
            else if ((int)currentFrame < startFrame)
            {
                _lastSoundFrame = -1;
            }
        }
        else
        {
            _lastSoundFrame = -1;
        }
    }

    private void PlayHammerSound()
    {
        if (Api?.Side != EnumAppSide.Client)
            return;

        var capi = Api as ICoreClientAPI;
        capi.World.PlaySoundAt(
            _soundHammer,
            Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
            null,
            false,
            32,
            1f
        );
    }

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            var assetLocation = default(AssetLocation?);

            if (_nowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }
            else if (_nowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (block.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }

            if (assetLocation == null && _nowTesselatingShape != null)
            {
                _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);
            }

            if (assetLocation == null)
            {
                var domain = _nowTesselatingObj.Code.Domain;
                assetLocation = new(domain, "textures/item/" + textureCode);
                Api.World.Logger.Warning("Текстура {0} не найдена в текстурах предмета или формы, используется путь: {1}", textureCode, assetLocation);
            }

            return GetOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
    {
        var textureAtlasPosition = _capi.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition != null)
            return textureAtlasPosition;

        var pos = texturePath.Path.IndexOf("++");
        if (pos >= 0)
            texturePath.Path = texturePath.Path.Substring(0, pos);

        var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
        {
            _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out var num, out textureAtlasPosition, null, 0.005f);
        }
        else
        {
            Api.World.Logger.Warning("Текстура не найдена по пути: {0}", texturePath);
        }

        return textureAtlasPosition;
    }

    public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
            return;

        if (slotid >= inventory.Count)
            return;

        if (slotid != 0)
        {
            _meshes[slotid] = null;
            return;
        }

        if (inventory[slotid].Empty)
        {
            _meshes[slotid] = null;
            return;
        }

        var meshData = GenMesh(inventory[slotid]);
        if (meshData != null)
        {
            TranslateMesh(meshData, slotid);
            _meshes[slotid] = meshData;
        }
        else
        {
            _meshes[slotid] = null;
        }
    }

    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null || slotId != 0)
            return;

        var stack = this.inventory[slotId].Itemstack;
        var origin = new Vec3f(0.5f, 0, 0.5f);

        if (stack.Class == EnumItemClass.Item)
        {
            var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.8F);
            var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.8F);
            var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.8F);
            var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
            var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0F);
            var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
            var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
            var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
            var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

            meshData.Scale(origin, scaleX, scaleY, scaleZ);
            meshData.Translate(translateX, translateY + 0.95f, translateZ);
            meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);
        }
        else
        {
            meshData.Scale(origin, 0.3f, 0.3f, 0.3f);
            meshData.Translate(0f, 0.95f, 0f);
        }
    }

    public MeshData? GenMesh(ItemSlot slot)
    {
        var stack = slot.Itemstack;

        if (stack == null)
            return null;

        MeshData meshData;
        try
        {
            var meshSource = stack.Collectible as IContainedMeshSource;

            if (meshSource != null)
            {
                meshData = meshSource.GenMesh(slot, _capi.BlockTextureAtlas, Pos);
                meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
            }
            else
            {
                if (stack.Class == EnumItemClass.Block)
                {
                    meshData = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                }
                else
                {
                    _nowTesselatingObj = stack.Collectible;
                    _nowTesselatingShape = null;

                    if (stack.Item.Shape != null)
                        _nowTesselatingShape = _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);

                    _capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
            }
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code, e.Message);
            meshData = null;
        }

        return meshData;
    }

    public void UpdateMeshes()
    {
        for (var i = 0; i < this.inventory.Count; i++)
            UpdateMesh(i);

        MarkDirty(true);
    }

    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
            this._clientDialog.Update(RecipeProgress);

        if (slotid != 0)
            return;

        // защита от горячей смены стака
        if (slotid == 0 && RecipeProgress < 1f)
        {
            RecipeProgress = 0f;
            AccumulatedEnergy = 0;
            UpdateState(RecipeProgress);
        }

        if (slotid == 0 && Api.Side == EnumAppSide.Client)
        {
            UpdateMesh(0);
        }

        if (this.InputSlot.Empty)
        {
            RecipeProgress = 0;
            AccumulatedEnergy = 0;
            StopAnimation();
        }

        this.MarkDirty();
        if (this._clientDialog == null || !this._clientDialog.IsOpened())
            return;

        this._clientDialog.SingleComposer.ReCompose();
        if (Api?.Side == EnumAppSide.Server)
        {
            FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
            MarkDirty(true);
        }
    }

    public static bool FindMatchingRecipe(ref HammerRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        ItemSlot[] inputSlots = [inputSlot];
        currentRecipe = null;
        currentRecipeName = string.Empty;

        foreach (var recipe in ElectricalProgressiveRecipeManager.HammerRecipes)
        {
            if (recipe.Matches(inputSlots, out _))
            {
                currentRecipe = recipe;
                if (recipe.Outputs.Length > 0 && recipe.Outputs[0].ResolvedItemstack != null)
                {
                    currentRecipeName = recipe.Outputs[0].ResolvedItemstack.GetName();
                }
                return true;
            }
        }
        return false;
    }

    private void Every1000Ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null)
        {
            StopAnimation();
            return;
        }

        var stack = InputSlot?.Itemstack;

        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null ||
            stack.Collectible.Attributes == null)
            return;

        var hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        var hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
        var isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;

        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
            {
                StartAnimation();
            }

            // Обновляем прогресс из накопленной энергии
            if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                
                // Обновляем температуру
                if (InputSlot?.Itemstack != null)
                {
                    var inputStack = InputSlot.Itemstack;
                    if (RecipeProgress < 0.5f)
                    {
                        inputStack.Collectible.SetTemperature(this.Api.World, inputStack, RecipeProgress * 2 * _maxTargetTemp);
                    }
                    else
                    {
                        inputStack.Collectible.SetTemperature(this.Api.World, inputStack, _maxTargetTemp);
                    }
                }
                
                UpdateState(RecipeProgress);
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
        if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
        {
            return;
        }

        try
        {
            for (int i = 0; i < CurrentRecipe.Outputs.Length; i++)
            {
                var output = CurrentRecipe.Outputs[i];

                if (Api.World.Rand.NextDouble() > output.Chance)
                    continue;

                var outputItem = output.ResolvedItemstack?.Clone();
                if (outputItem == null)
                    continue;

                outputItem.Collectible.SetTemperature(this.Api.World, outputItem, _maxTargetTemp);

                if (i == 0)
                {
                    TryMergeOrSpawn(outputItem, OutputSlot);
                }
                else if (i == 1)
                {
                    TryMergeOrSpawn(outputItem, SecondaryOutputSlot);
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
                if (!FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]))
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

    private void TryMergeOrSpawn(ItemStack stack, ItemSlot targetSlot)
    {
        if (targetSlot.Empty)
        {
            targetSlot.Itemstack = stack;
        }
        else if (targetSlot.Itemstack.Collectible == stack.Collectible &&
                 targetSlot.Itemstack.StackSize < targetSlot.Itemstack.Collectible.MaxStackSize)
        {
            var freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.Itemstack.StackSize;
            var toAdd = Math.Min(freeSpace, stack.StackSize);

            var stackTemp = stack.Collectible.GetTemperature(this.Api.World, stack);
            var targetstackTemp = targetSlot.Itemstack.Collectible.GetTemperature(this.Api.World, targetSlot.Itemstack);

            var stackCapacity = stackTemp * toAdd;
            var targetCapacity = targetstackTemp * targetSlot.Itemstack.StackSize;

            targetSlot.Itemstack.StackSize += toAdd;

            targetSlot.Itemstack.Collectible.SetTemperature(this.Api.World, targetSlot.Itemstack, (stackCapacity + targetCapacity) / targetSlot.Itemstack.StackSize);

            stack.StackSize -= toAdd;

            if (stack.StackSize > 0)
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }
        else
        {
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
        targetSlot.MarkDirty();
    }

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null || CurrentRecipe == null)
            return;

        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null) return;

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            float powerRatio = Math.Max(0.1f, Math.Min(1f, beh.PowerSetting / (float)CurrentRecipe.EnergyOperation));
            float animationSpeed = powerRatio * 2.0f;
            
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "Animation1",
                Code = "craft",
                AnimationSpeed = animationSpeed,
                EaseOutSpeed = 2.0f,
                EaseInSpeed = 1f
            });
        }
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == true)
        {
            AnimUtil.StopAnimation("craft");
        }
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
                  new GuiDialogHammer(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
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

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator);

        if (_meshes != null)
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] != null)
                    mesher.AddMeshData(_meshes[i]);
            }
        }

        if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
        {
            return false;
        }

        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");
        this.AccumulatedEnergy = tree.GetInt("accumulatedEnergy");

        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);

        if (Api is ICoreClientAPI)
        {
            UpdateMeshes();
        }

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

        _mesh?.Dispose();
        _resultingShape = null;
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
    }

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
        this._clientDialog?.TryClose();

        StopAnimation();

        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil?.Dispose();
        }

        _mesh?.Dispose();
        _resultingShape = null;
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
        _capi = null;
    }
}