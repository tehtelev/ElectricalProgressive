using ElectricalProgressive.Content.Block;
using ElectricalProgressive.RecipeSystem;
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
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;


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

    /// <summary>
    /// Ковка идёт (нагрев до 900°C закончен). Синится на клиент для анимации.
    /// </summary>
    public bool IsForging { get; private set; }

    /// <summary>
    /// Машина готова к работе (сборка Core MachineConstruct завершена / formed).
    /// </summary>
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

    private static float _maxTargetTemp = 1350f;
    public const float CraftStartTemp = 900f;
    private const float HeatPerEnergy = 0.5f;

    public override string DialogTitle => Lang.Get("ehammer-title-gui");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    private int _lastSoundFrame = -1;
    private long _lastAnimationCheckTime;
    private BlockEntityAnimationUtil? AnimUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    private bool _animatorReadyForFormed;

    // Новые поля для системы мешей (как в холодильнике)
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;
    private SmithingWorkItemRenderer? _workItemRenderer;

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
    /// Добавить энергию: сначала нагрев входа до 900°C, затем прогресс крафта.
    /// </summary>
    public void AddEnergy(int amount)
    {
        if (!StructureComplete)
            return;

        if (CurrentRecipe == null || InputSlot.Empty)
            return;

        if (amount <= 0)
            return;

        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null) return;

        float currentPower = beh.PowerSetting;
        if (currentPower <= 0) return;

        int maxAddPerTick = Math.Max(1, (int)(currentPower / 20f));
        int safeAmount = Math.Min(amount, maxAddPerTick);

        float currentTemp = GetInputTemperature();
        if (currentTemp < CraftStartTemp)
        {
            float newTemp = Math.Min(currentTemp + safeAmount * HeatPerEnergy, CraftStartTemp);
            SetInputTemperature(newTemp);
            RecipeProgress = 0f;
            SetForging(false);
            UpdateState(RecipeProgress);
            MarkDirty(true);
            return;
        }

        SetInputTemperature(Math.Min(Math.Max(currentTemp, CraftStartTemp), _maxTargetTemp));
        SetForging(true);

        int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
        if (maxNeeded <= 0)
        {
            ProcessCompletedCraft();
            return;
        }

        int energyToAdd = Math.Min(safeAmount, maxNeeded);
        AccumulatedEnergy += energyToAdd;

        if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
        {
            RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
            UpdateState(RecipeProgress);
        }

        if (AccumulatedEnergy >= CurrentRecipe.EnergyOperation)
        {
            ProcessCompletedCraft();
        }

        MarkDirty(true);
    }

    public float GetInputTemperature()
    {
        var stack = InputSlot?.Itemstack;
        if (stack?.Collectible == null || Api?.World == null)
            return 0f;
        return stack.Collectible.GetTemperature(Api.World, stack);
    }

    private void SetInputTemperature(float temperature)
    {
        var stack = InputSlot?.Itemstack;
        if (stack?.Collectible == null || Api?.World == null)
            return;
        stack.Collectible.SetTemperature(Api.World, stack, temperature);
    }

    private void SetForging(bool forging)
    {
        if (IsForging == forging)
            return;
        IsForging = forging;
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

            _workItemRenderer = new SmithingWorkItemRenderer(_capi, () => Pos, () => InputSlot?.Itemstack);
            _capi.Event.RegisterRenderer(_workItemRenderer, EnumRenderStage.Opaque, "ehammer-workitem");

            // Первоначальное создание мешей
            UpdateMeshes();

            _soundHammer = new AssetLocation("electricalprogressiveindustry:sounds/ehammer/hammer.ogg");

            this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
            EnsureAnimatorReady();
        }
    }

    private void EnsureAnimatorReady()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (!IsFormed)
            return;

        if (_animatorReadyForFormed && AnimUtil.animator != null)
            return;

        PrepareAnimUtil(Api, InventoryClassName);
        AnimUtil.InitializeAnimator(
            InventoryClassName,
            _mesh,
            _resultingShape,
            new Vec3f(0, GetRotation(), 0f));
        _animatorReadyForFormed = true;
    }

    private Vintagestory.API.Common.Block? GetFormedBlockForAnim(ICoreAPI api)
    {
        if (Block?.Variant == null || !Block.Variant.ContainsKey("state"))
            return Block;

        return api.World.GetBlock(Block.CodeWithVariant("state", "formed")) ?? Block;
    }

    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (AnimUtil == null)
            return;

        var shapeBlock = GetFormedBlockForAnim(api) ?? Block;
        if (shapeBlock?.Shape?.Base == null)
            return;

        AssetLocation shapePath = shapeBlock.Shape.Base.Clone()
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");

        Shape shape = Shape.TryGet(api, shapePath);
        if (shape == null)
            return;

        // CreateMesh кэширует локальный меш — клонируем, иначе Translate уедет повторно
        var src = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _resultingShape, null);
        _mesh = src?.Clone();
        _mesh?.Translate(-1f, 0f, 0f);
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
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on") && AnimUtil.animator != null)
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
            UpdateItemParticleOffset(null);
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

        _workItemRenderer?.SetMesh(_meshes[slotid]);
        UpdateItemParticleOffset(_meshes[slotid]);
    }

    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null || slotId != 0)
            return;

        var stack = this.inventory[slotId].Itemstack;
        var origin = new Vec3f(0.5f, 0, 0.5f);
        var orientationRotate = Block.Shape.rotateY;

        if (stack.Class == EnumItemClass.Item)
        {
            var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 1.0F);
            var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 1.0F);
            var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 1.0F);
            var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0.97F);
            var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0.95F);
            var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", -0.59F);
            var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
            var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
            var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

            meshData.Scale(origin, scaleX, scaleY, scaleZ);
            meshData.Translate(translateX, translateY, translateZ);
            meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);

            meshData.Translate(-1f, 0f, 0f);
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }
        else
        {
            meshData.Scale(origin, 0.8f, 0.8f, 0.8f);
            meshData.Translate(0.97f, 0.95f, -0.59f);

            meshData.Translate(-1f, 0f, 0f);
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }
    }

    /// <summary>
    /// Искры бьют в центр отрисованного меша входного предмета.
    /// </summary>
    private void UpdateItemParticleOffset(MeshData? mesh)
    {
        var ep = ElectricalProgressive;
        if (ep?.ParticlesOffsetPos == null)
            return;

        var center = GetMeshCenter(mesh) ?? new Vec3d(0.5, 1.01, 0.5);

        if (ep.ParticlesOffsetPos.Count == 0)
        {
            ep.ParticlesOffsetPos.Add(center);
            return;
        }

        for (var i = 0; i < ep.ParticlesOffsetPos.Count; i++)
            ep.ParticlesOffsetPos[i] = center.Clone();
    }

    private static Vec3d? GetMeshCenter(MeshData? mesh)
    {
        if (mesh?.xyz == null || mesh.VerticesCount <= 0)
            return null;

        var xyz = mesh.xyz;
        var n = mesh.VerticesCount;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (var i = 0; i < n; i++)
        {
            var o = i * 3;
            var x = xyz[o];
            var y = xyz[o + 1];
            var z = xyz[o + 2];
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        return new Vec3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
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

        // Убираем StopAnimation() и лишнюю логику — как в Extruder
        // Анимация будет управляться только из Every1000Ms

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
        if (beh == null || !StructureComplete)
        {
            StopAnimation();
            return;
        }

        var stack = InputSlot?.Itemstack;

        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null)
        {
            SetForging(false);
            StopAnimation();
            return;
        }

        var hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        var hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);

        if (Api.Side == EnumAppSide.Server)
        {
            var isHotEnough = GetInputTemperature() >= CraftStartTemp;
            SetForging(hasPower && hasRecipe && CurrentRecipe != null && isHotEnough);
        }

        var isCraftingNow = hasRecipe && CurrentRecipe != null && IsForging;

        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
                StartAnimation();

            if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
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
            float inputTemp = GetInputTemperature();

            for (int i = 0; i < CurrentRecipe.Outputs.Length; i++)
            {
                var output = CurrentRecipe.Outputs[i];

                if (Api.World.Rand.NextDouble() > output.Chance)
                    continue;

                var outputItem = output.ResolvedItemstack?.Clone();
                if (outputItem == null)
                    continue;

                outputItem.Collectible.SetTemperature(this.Api.World, outputItem, inputTemp);

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
            if (!InputSlot.Empty)
                SetInputTemperature(inputTemp);
            InputSlot.MarkDirty();
            
            // Обнуляем накопленную энергию для следующего рецепта
            AccumulatedEnergy = 0;
            RecipeProgress = 0;
            
            // Проверяем, можно ли продолжить с тем же или новым рецептом
            if (!InputSlot.Empty)
            {
                FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
                if (CurrentRecipe == null)
                {
                    AccumulatedEnergy = 0;
                    RecipeProgress = 0;
                }
            }
            else
            {
                CurrentRecipe = null;
                AccumulatedEnergy = 0;
                RecipeProgress = 0;
                SetForging(false);
                StopAnimation();
            }
            
            UpdateState(RecipeProgress);
            MarkDirty(true);
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
        if (Api?.Side != EnumAppSide.Client || CurrentRecipe == null)
            return;

        EnsureAnimatorReady();
        if (AnimUtil == null) return;

        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null) return;

        if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            AnimUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 2f,
                EaseOutSpeed = 2.0f,
                EaseInSpeed = 1f
            });
        }
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            AnimUtil.StopAnimation("work-on");
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
        if (!StructureComplete)
            return true;

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
        var construct = GetBehavior<MachineConstruct>();
        if (construct is { IsRenderingBlueprint: true })
            return base.OnTesselation(mesher, tesselator);

        if (IsFormed)
            EnsureAnimatorReady();

        base.OnTesselation(mesher, tesselator);

        if (AnimUtil?.activeAnimationsByAnimCode == null ||
            !AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
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
        this.IsForging = tree.GetBool("isForging");

        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);

        if (Api is ICoreClientAPI)
        {
            UpdateMeshes();
            EnsureAnimatorReady();
            if (IsForging)
                StartAnimation();
            else
                StopAnimation();
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
        tree.SetBool("isForging", IsForging);
        tree.SetBool("structureComplete", StructureComplete);
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

        _workItemRenderer?.Dispose();
        _workItemRenderer = null;
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

        _workItemRenderer?.Dispose();
        _workItemRenderer = null;
        _mesh?.Dispose();
        _resultingShape = null;
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
        _capi = null;
    }
}