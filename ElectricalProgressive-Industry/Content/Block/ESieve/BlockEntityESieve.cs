// BlockEntityESieve.cs (с 25 выходными слотами)
using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.ESieve
{
    public class BlockEntityESieve : BlockEntityGenericTypedContainer
    {
        public const int OUTPUT_SLOTS_COUNT = 25; // 25 выходных слотов
        public const int TOTAL_SLOTS = 1 + OUTPUT_SLOTS_COUNT; // 1 вход + 25 выходов = 26 слотов

        internal InventorySieve inventory;
        private GuiDialogSieve _clientDialog;
        public override string InventoryClassName => "esieve";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasSievingLastTick;

        public SieveRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;
        public int AccumulatedEnergy { get; set; }

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

        public ItemSlot InputSlot => inventory[0];
        public ItemSlot GetOutputSlot(int index) => inventory[1 + index]; // index 0-24
        public override string DialogTitle => Lang.Get("esieve-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh;
        private static Shape? _resultingShape;
        private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private bool _animatorReadyForFormed;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

        private Facing _facing = Facing.None;
        public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

        public Facing Facing
        {
            get => this._facing;
            set
            {
                if (value != this._facing)
                {
                    this.ElectricalProgressive!.Connection = FacingHelper.FullFace(this._facing = value);
                }
            }
        }

        private AssetLocation _soundSieve;

        /// <summary>
        /// Низ барабана InvFrame35 — зона крупинок и старт сыпи на лоток (north, блоки).
        /// </summary>
        private const float DrumBottomX = 0.941f;
        private const float DrumBottomY = 1.672f;
        private const float DrumBottomZ = -0.145f;

        /// <summary>Радиус барабана до панели InvFrame35.</summary>
        private const float DrumRadius = 0.541f;

        /// <summary>Длина зоны частиц вдоль оси барабана (половина).</summary>
        private const float DrumHalfLength = 0.85f;

        /// <summary>Лоток Cube160 — куда ссыпаются частицы.</summary>
        private const float TrayLocalX = -0.631f;
        private const float TrayLocalY = 0.300f;
        private const float TrayLocalZ = -0.500f;

        public BlockEntityESieve()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventorySieve(TOTAL_SLOTS, InventoryClassName, null, null, null, this);
            inventory.SlotModified += OnSlotModified;
        }

        public void AddEnergy(int amount)
        {
            if (!StructureComplete)
                return;

            if (CurrentRecipe == null || InputSlot.Empty)
                return;
    
            if (amount <= 0)
                return;
        
            int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
            if (maxNeeded <= 0)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedSieving();
            
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
                }
                return;
            }
    
            int energyToAdd = Math.Min(amount, maxNeeded);
            AccumulatedEnergy += energyToAdd;
    
            if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                UpdateState(RecipeProgress);
            }
    
            if (AccumulatedEnergy >= CurrentRecipe.EnergyOperation)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedSieving();
            
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
                }
            }
    
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.RegisterGameTickListener(Every1000Ms, 1000);
            // Густой поток крупинок — только клиент, часто
            if (api.Side == EnumAppSide.Client)
                this.RegisterGameTickListener(SievingFxTick, 70);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;
                _soundSieve = new AssetLocation("electricalprogressiveindustry:sounds/esieve/sieve.ogg");
                this.RegisterGameTickListener(CheckAnimationFrame, 50);
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

            _mesh = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _resultingShape, null);
        }

        public int GetRotation()
        {
            var side = Block.Variant["side"];
            var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
            return adjustedIndex * 90;
        }

        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
                _clientDialog?.Update(RecipeProgress);

            if (slotid == 0)
            {
                RecipeProgress = 0f;
                AccumulatedEnergy = 0;
                UpdateState(RecipeProgress);
            }

            MarkDirty();
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref SieveRecipe currentRecipe, ref string currentRecipeName, InventorySieve inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.SieveRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
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

        private static bool MatchesRecipe(SieveRecipe recipe, InventorySieve inventory)
        {
            var inputSlot = inventory[0];
            
            if (inputSlot.Empty) return false;
            
            if (recipe.Ingredients.Length > 0)
            {
                var ingred = recipe.Ingredients[0];
                if (ingred.Quantity > 0)
                {
                    if (!ingred.SatisfiesAsIngredient(inputSlot.Itemstack) ||
                        inputSlot.Itemstack.StackSize < ingred.Quantity)
                        return false;
                }
                else if (ingred.Quantity == 0)
                {
                    if (!ingred.SatisfiesAsIngredient(inputSlot.Itemstack))
                        return false;
                }
            }
            
            return true;
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            if (CurrentRecipe.Ingredients.Length > 0)
            {
                var ingred = CurrentRecipe.Ingredients[0];
                var slot = InputSlot;

                if (ingred.Quantity == 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack))
                        return false;
                }
                else if (ingred.Quantity > 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack) ||
                        slot.Itemstack.StackSize < ingred.Quantity)
                        return false;
                }
            }

            return true;
        }

        private void ProcessCompletedSieving()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
                return;

            try
            {
                // Распределяем выходы по 25 слотам
                for (int i = 0; i < CurrentRecipe.Outputs.Length && i < OUTPUT_SLOTS_COUNT; i++)
                {
                    var output = CurrentRecipe.Outputs[i];
                    if (output == null) continue;
                    
                    if (Api.World.Rand.NextDouble() <= output.Chance)
                    {
                        var outputItem = output.ResolvedItemstack?.Clone();
                        if (outputItem != null && outputItem.StackSize > 0)
                        {
                            TryMergeOrSpawn(outputItem, GetOutputSlot(i));
                        }
                    }
                }

                // Если в рецепте больше выходов чем слотов - выкидываем в мир
                for (int i = OUTPUT_SLOTS_COUNT; i < CurrentRecipe.Outputs.Length; i++)
                {
                    var output = CurrentRecipe.Outputs[i];
                    if (output != null && Api.World.Rand.NextDouble() <= output.Chance)
                    {
                        var outputItem = output.ResolvedItemstack?.Clone();
                        if (outputItem != null)
                        {
                            Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                        }
                    }
                }

                // Расход ингредиентов
                if (CurrentRecipe.Ingredients.Length > 0)
                {
                    var ingred = CurrentRecipe.Ingredients[0];
                    if (ingred.Quantity > 0)
                    {
                        InputSlot.TakeOut(ingred.Quantity);
                        InputSlot.MarkDirty();
                    }
                }

                // Проверяем, можно ли продолжить с тем же рецептом
                if (HasRequiredItems() && CurrentRecipe != null)
                {
                    if (!FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory))
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
                Api.Logger.Error($"Sieving error in ESieve at {Pos}: {ex}");
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

                targetSlot.Itemstack.StackSize += toAdd;
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

        #endregion

        #region Основной цикл работы

        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorESieve>();
            if (beh == null || !StructureComplete)
            {
                StopAnimation();
                _wasSievingLastTick = false;
                return;
            }

            var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            var hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            var isSievingNow = hasPower && hasRecipe && CurrentRecipe != null;

            if (isSievingNow)
            {
                if (!_wasSievingLastTick)
                    StartAnimation();

                if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
                {
                    RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                    UpdateState(RecipeProgress);
                }
            }
            else if (_wasSievingLastTick)
            {
                StopAnimation();
                MarkDirty(true);
            }

            _wasSievingLastTick = isSievingNow;
        }

        /// <summary>
        /// Клиентский FX: крупинки в нижней трети барабана + сыпь на Cube160.
        /// </summary>
        private void SievingFxTick(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || !StructureComplete)
                return;

            var beh = GetBehavior<BEBehaviorESieve>();
            if (beh == null || beh.PowerSetting <= 0 || InputSlot.Empty)
                return;

            var sieving = _wasSievingLastTick ||
                          (beh.PowerSetting >= _maxConsumption * 0.1f &&
                           FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory) &&
                           CurrentRecipe != null);
            if (!sieving)
                return;

            SpawnSieveParticles();
        }

        private void SpawnSieveParticles()
        {
            var stack = InputSlot?.Itemstack;
            if (stack?.Collectible == null || Api == null)
                return;

            var bottom = GetRotatedOffset(DrumBottomX, DrumBottomY, DrumBottomZ);
            var tray = GetRotatedOffset(TrayLocalX, TrayLocalY, TrayLocalZ);

            float fillH = DrumRadius * 2f / 3f; // ~1/3 диаметра

            // --- 1) Крупинки в нижней трети барабана (ворошение) ---
            GetWorldSpawnBox(
                DrumBottomX - DrumHalfLength, DrumBottomY + 0.02f, DrumBottomZ - 0.18f,
                DrumBottomX + DrumHalfLength, DrumBottomY + fillH, DrumBottomZ + 0.18f,
                out var tumbleMin, out var tumbleSize);

            var tumble = new SimpleParticleProperties(
                minQuantity: 10,
                maxQuantity: 18,
                color: ColorUtil.WhiteArgb,
                minPos: tumbleMin,
                maxPos: tumbleMin.AddCopy(tumbleSize.X, tumbleSize.Y, tumbleSize.Z),
                minVelocity: new Vec3f(-0.9f, 0.15f, -0.9f),
                maxVelocity: new Vec3f(0.9f, 1.1f, 0.9f),
                lifeLength: 0.55f,
                gravityEffect: 0.85f,
                minSize: 0.045f,
                maxSize: 0.11f,
                model: EnumParticleModel.Cube
            );
            tumble.AddPos = tumbleSize;
            tumble.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.03f);
            tumble.WindAffected = false;
            tumble.WithTerrainCollision = false;
            ApplyStackColor(tumble, stack);
            Api.World.SpawnParticles(tumble);

            // --- 2) Мелкая пыль ---
            GetWorldSpawnBox(
                DrumBottomX - DrumHalfLength * 0.8f, DrumBottomY + 0.05f, DrumBottomZ - 0.12f,
                DrumBottomX + DrumHalfLength * 0.8f, DrumBottomY + fillH + 0.12f, DrumBottomZ + 0.12f,
                out var dustMin, out var dustSize);

            var dust = new SimpleParticleProperties(
                minQuantity: 3,
                maxQuantity: 6,
                color: ColorUtil.WhiteArgb,
                minPos: dustMin,
                maxPos: dustMin.AddCopy(dustSize.X, dustSize.Y, dustSize.Z),
                minVelocity: new Vec3f(-0.3f, 0.05f, -0.3f),
                maxVelocity: new Vec3f(0.3f, 0.45f, 0.3f),
                lifeLength: 0.7f,
                gravityEffect: 0.15f,
                minSize: 0.18f,
                maxSize: 0.4f,
                model: EnumParticleModel.Quad
            );
            dust.AddPos = dustSize;
            dust.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -180f);
            dust.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 0.4f);
            dust.WindAffected = false;
            dust.WithTerrainCollision = false;
            ApplyStackColor(dust, stack);
            Api.World.SpawnParticles(dust);

            // --- 3) Струя с днища на Cube160 ---
            float dx = (float)(tray.X - bottom.X);
            float dy = (float)(tray.Y - bottom.Y);
            float dz = (float)(tray.Z - bottom.Z);
            float fallTime = 0.65f;
            float vx = dx / fallTime;
            float vy = dy / fallTime;
            float vz = dz / fallTime;

            GetWorldSpawnBox(
                DrumBottomX - 0.35f, DrumBottomY - 0.08f, DrumBottomZ - 0.1f,
                DrumBottomX + 0.35f, DrumBottomY + 0.04f, DrumBottomZ + 0.1f,
                out var streamMin, out var streamSize);

            var stream = new SimpleParticleProperties(
                minQuantity: 8,
                maxQuantity: 14,
                color: ColorUtil.WhiteArgb,
                minPos: streamMin,
                maxPos: streamMin.AddCopy(streamSize.X, streamSize.Y, streamSize.Z),
                minVelocity: new Vec3f(vx - 0.12f, vy - 0.2f, vz - 0.12f),
                maxVelocity: new Vec3f(vx + 0.12f, vy + 0.05f, vz + 0.12f),
                lifeLength: fallTime + 0.1f,
                gravityEffect: 0.45f,
                minSize: 0.04f,
                maxSize: 0.1f,
                model: EnumParticleModel.Cube
            );
            stream.AddPos = streamSize;
            stream.WindAffected = false;
            stream.WithTerrainCollision = false;
            ApplyStackColor(stream, stack);
            Api.World.SpawnParticles(stream);

            // --- 4) Удар о лоток ---
            GetWorldSpawnBox(
                TrayLocalX - 0.1f, TrayLocalY, TrayLocalZ - 0.1f,
                TrayLocalX + 0.1f, TrayLocalY + 0.06f, TrayLocalZ + 0.1f,
                out var landMin, out var landSize);

            var land = new SimpleParticleProperties(
                minQuantity: 2,
                maxQuantity: 5,
                color: ColorUtil.WhiteArgb,
                minPos: landMin,
                maxPos: landMin.AddCopy(landSize.X, landSize.Y, landSize.Z),
                minVelocity: new Vec3f(-0.25f, 0.08f, -0.25f),
                maxVelocity: new Vec3f(0.25f, 0.35f, 0.25f),
                lifeLength: 0.4f,
                gravityEffect: 1.0f,
                minSize: 0.035f,
                maxSize: 0.09f,
                model: EnumParticleModel.Cube
            );
            land.AddPos = landSize;
            land.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -120f);
            land.WindAffected = false;
            land.WithTerrainCollision = false;
            ApplyStackColor(land, stack);
            Api.World.SpawnParticles(land);
        }

        /// <summary>
        /// AABB спавна в мире из двух локальных точек (north), с учётом поворота блока.
        /// </summary>
        private void GetWorldSpawnBox(
            float x0, float y0, float z0,
            float x1, float y1, float z1,
            out Vec3d worldMin,
            out Vec3d size)
        {
            var a = GetRotatedOffset(x0, y0, z0);
            var b = GetRotatedOffset(x1, y1, z1);
            double minX = Math.Min(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double minZ = Math.Min(a.Z, b.Z);
            double maxX = Math.Max(a.X, b.X);
            double maxY = Math.Max(a.Y, b.Y);
            double maxZ = Math.Max(a.Z, b.Z);
            worldMin = Pos.ToVec3d().Add(minX, minY, minZ);
            size = new Vec3d(maxX - minX, maxY - minY, maxZ - minZ);
        }

        private static void ApplyStackColor(SimpleParticleProperties props, ItemStack stack)
        {
            if (stack.Class == EnumItemClass.Item)
                props.ColorByItem = stack.Item;
            else if (stack.Block != null)
                props.ColorByBlock = stack.Block;
        }

        /// <summary>
        /// Локальный offset (north) → смещение относительно блока с тем же RotateY,
        /// что у AnimatableRenderer (GetRotation + Mat4f.RotateY вокруг 0.5, 0.5).
        /// </summary>
        private Vec3d GetRotatedOffset(float lx, float ly, float lz)
        {
            float ox = lx - 0.5f;
            float oz = lz - 0.5f;
            float deg = GetRotation();
            if (deg == 0)
                return new Vec3d(lx, ly, lz);

            float rad = deg * GameMath.DEG2RAD;
            float cos = GameMath.Cos(rad);
            float sin = GameMath.Sin(rad);
            float rx = ox * cos + oz * sin;
            float rz = -ox * sin + oz * cos;
            return new Vec3d(0.5f + rx, ly, 0.5f + rz);
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);

            MarkDirty(true);
        }

        #endregion

        #region Визуальные эффекты

        private void StartAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || CurrentRecipe == null)
                return;

            EnsureAnimatorReady();
            if (AnimUtil == null) return;

            var beh = GetBehavior<BEBehaviorESieve>();
            if (beh == null) return;

            if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            {
                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "work-on",
                    Code = "work-on",
                    AnimationSpeed = 1F,
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
        
        private void CheckAnimationFrame(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            const int startFrame = 280;
            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on") && AnimUtil.animator != null)
            {
                var currentTime = Api.World.ElapsedMilliseconds;
                _lastAnimationCheckTime = currentTime;

                var currentFrame = AnimUtil.animator.Animations[0].CurrentFrame;
                if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
                {
                    PlaySieveSound();
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

        private void PlaySieveSound()
        {
            if (Api?.Side != EnumAppSide.Client)
                return;

            var capi = Api as ICoreClientAPI;
            capi.World.PlaySoundAt(
                _soundSieve,
                Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
                null,
                false,
                32,
                1f
            );
        }

        #endregion

        #region GUI и взаимодействие

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!StructureComplete)
                return true;

            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    _clientDialog = new GuiDialogSieve(DialogTitle, Inventory, Pos, _capi);
                    _clientDialog.Update(RecipeProgress);
                    return _clientDialog;
                });
            }
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
            this.invDialog = (GuiDialogBlockEntity)null!;
        }

        #endregion

        #region Сохранение состояния

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
            RecipeProgress = tree.GetFloat("PowerCurrent");
            AccumulatedEnergy = tree.GetInt("accumulatedEnergy");

            if (Api != null)
                Inventory.AfterBlocksLoaded(Api.World);

            if (Api is ICoreClientAPI)
                EnsureAnimatorReady();

            if (Api?.Side == EnumAppSide.Client && _clientDialog != null)
                _clientDialog.Update(RecipeProgress);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invTree = new TreeAttribute();
            Inventory.ToTreeAttributes(invTree);
            tree["_inventory"] = invTree;
            tree.SetFloat("PowerCurrent", RecipeProgress);
            tree.SetInt("accumulatedEnergy", AccumulatedEnergy);
            tree.SetBool("structureComplete", StructureComplete);
        }

        #endregion

        #region Жизненный цикл

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (ElectricalProgressive == null! || byItemStack == null)
                return;

            LoadEProperties.Load(this.Block, this);
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

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ElectricalProgressive != null!)
            {
                ElectricalProgressive.Connection = Facing.None;
            }

            if (this.Api is ICoreClientAPI && this._clientDialog != null!)
            {
                this._clientDialog?.TryClose();
                this._clientDialog = null;
            }

            StopAnimation();

            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null!)
            {
                this.AnimUtil?.Dispose();
            }

            _mesh?.Dispose();
            _resultingShape = null;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this._clientDialog?.TryClose();

            _mesh?.Dispose();
            _resultingShape = null;
            _capi = null!;
        }

        #endregion
    }
}