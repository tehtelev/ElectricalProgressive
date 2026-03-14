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

namespace ElectricalProgressive.Content.Block.EPress
{
    public class BlockEntityEPress : BlockEntityGenericTypedContainer, ITexPositionSource
    {
        // Конфигурация
        internal InventoryPress inventory;
        private GuiDialogPress _clientDialog;
        public override string InventoryClassName => "epress";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        // Состояние крафта
        public PressRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;

        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public ItemSlot OutputSlot2 => inventory[3];
        public override string DialogTitle => Lang.Get("epress-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh; // кеш для меша, который используется в анимации. Кеш нужен, чтобы не загружать меш из ресурсов каждый раз при тесселяции блока, а использовать уже загруженный и обработанный меш.
        private static Shape? _resultingShape; // кеш для формы, которая используется в анимации. Кеш нужен, чтобы не загружать форму из ресурсов каждый раз при тесселяции блока, а использовать уже загруженную и обработанную форму.
        private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

        // Новые поля для системы мешей (как в холодильнике)
        private MeshData?[] _meshes;
        private Shape? _nowTesselatingShape;
        private CollectibleObject _nowTesselatingObj;

        //------------------------------------------------------------------------------------------------------------------
        // Электрические параметры
        private Facing _facing = Facing.None;
        public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

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

        //----------------------------------------------------------------------------------------------------------------------------

        private AssetLocation _soundPress;

        public BlockEntityEPress()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventoryPress(4, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
            inventory.SlotModified += OnSlotModified;
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

                _soundPress = new AssetLocation("electricalprogressiveindustry:sounds/epress/press.ogg");

                this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
            }
        }

        /// <summary>
        /// Подготавливает анимационный утилит для блока, загружая меш и форму из ресурсов
        /// </summary>
        /// <param name="api"></param>
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

        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
                _clientDialog?.Update(RecipeProgress);

            if (slotid < 2)
            {
                RecipeProgress = 0f;
                UpdateState(RecipeProgress);
            }

            // Обновляем меш при изменении входного слота
            if (slotid == 1 && Api.Side == EnumAppSide.Client)
            {
                UpdateMesh(1);
            }

            MarkDirty();
        }

        /// <summary>
        /// Необходимо для отрисовки текстур
        /// </summary>
        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                var assetLocation = default(AssetLocation?);

                // Пробуем получить текстуру из item.Textures
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

                // Если не нашли, пробуем из shape.Textures
                if (assetLocation == null && _nowTesselatingShape != null)
                {
                    _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);
                }

                // Если все еще не нашли, используем домен предмета и предполагаемый путь
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

            // берем только base текстуру (первую из кучи наваленных)
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

        /// <summary>
        /// Обновляем mesh для конкретного слота
        /// </summary>
        public void UpdateMesh(int slotid)
        {
            if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
                return;

            if (slotid >= this.inventory.Count)
                return;

            // В прессе отображаем только слот 1 (пресс-форму)
            if (slotid != 1)
            {
                _meshes[slotid] = null;
                return;
            }

            if (this.inventory[slotid].Empty)
            {
                _meshes[slotid] = null;
                return;
            }

            var stack = this.inventory[slotid].Itemstack;

            // Отображаем только пресс-формы
            if (stack == null || stack.Collectible == null || !stack.Collectible.Code.Path.Contains("pressform"))
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

        /// <summary>
        /// Перемещаем mesh в нужную позицию для пресса
        /// </summary>
        public void TranslateMesh(MeshData? meshData, int slotId)
        {
            if (meshData == null || slotId != 1)
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
                meshData.Scale(origin, 0.9f, 0.9f, 0.9f);
                meshData.Translate(0f, 0.9f, 0f);
            }
        }

        /// <summary>
        /// Генерация меша для предмета (как в холодильнике)
        /// </summary>
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

        /// <summary>
        /// Обновляет все meshы в инвентаре
        /// </summary>
        public void UpdateMeshes()
        {
            for (var i = 0; i < this.inventory.Count; i++)
                UpdateMesh(i);

            MarkDirty(true);
        }

        #region Логика рецептов


        public static bool FindMatchingRecipe(ref PressRecipe currentRecipe, ref string currentRecipeName, InventoryPress inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.PressRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
                {
                    currentRecipe = recipe;
                    // Берем название из первого выхода рецепта
                    if (recipe.Outputs.Length > 0 && recipe.Outputs[0].ResolvedItemstack != null)
                    {
                        currentRecipeName = recipe.Outputs[0].ResolvedItemstack.GetName();
                    }
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesRecipe(PressRecipe recipe, InventoryPress inventory)
        {
            var usedSlots = new List<int>();

            for (var ingredIndex = 0; ingredIndex < recipe.Ingredients.Length && ingredIndex < 2; ingredIndex++)
            {
                var ingred = recipe.Ingredients[ingredIndex];
                var foundSlot = false;

                for (var slotIndex = 0; slotIndex < 2; slotIndex++)
                {
                    if (usedSlots.Contains(slotIndex)) continue;

                    var slot = inventory[slotIndex];

                    if (ingred.Quantity == 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                    else if (ingred.Quantity > 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack) &&
                            slot.Itemstack.StackSize >= ingred.Quantity)
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                }

                if (!foundSlot) return false;
            }

            return true;
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            for (var i = 0; i < CurrentRecipe.Ingredients.Length && i < 2; i++)
            {
                var ingred = CurrentRecipe.Ingredients[i];
                var slot = GetInputSlot(i);

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

        private void ProcessCompletedCraft()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
                return;

            try
            {
                var usedSlots = new List<int>();

                // Обработка всех выходов рецепта
                for (int i = 0; i < CurrentRecipe.Outputs.Length; i++)
                {
                    var output = CurrentRecipe.Outputs[i];

                    // Проверяем шанс для каждого выхода
                    if (Api.World.Rand.NextDouble() > output.Chance)
                        continue;

                    var outputItem = output.ResolvedItemstack?.Clone();
                    if (outputItem == null)
                        continue;

                    // Распределяем выходы по слотам
                    if (i == 0)
                    {
                        // Первый выход - основной слот
                        TryMergeOrSpawn(outputItem, OutputSlot1);
                    }
                    else if (i == 1)
                    {
                        // Второй выход - дополнительный слот
                        TryMergeOrSpawn(outputItem, OutputSlot2);
                    }
                    else
                    {
                        // Остальные выходы спавним в мире
                        Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }

                // Удаляем расходуемые ингредиенты
                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    if (ingred.Quantity <= 0) continue;

                    for (var slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            slot.TakeOut(ingred.Quantity);
                            slot.MarkDirty();
                            usedSlots.Add(slotIndex);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Api.Logger.Error($"Crafting error in EPress at {Pos}: {ex}");
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

        private ItemSlot GetInputSlot(int index) => index switch
        {
            0 => InputSlot1,
            1 => InputSlot2,
            _ => throw new ArgumentOutOfRangeException()
        };

        #endregion

        #region Основной цикл работы
        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEPress>();
            if (beh == null)
            {
                StopAnimation();
                return;
            }

            var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            var hasRecipe = !InputSlot1.Empty && !InputSlot2.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            var isCraftingNow = hasPower && hasRecipe && CurrentRecipe != null;


            if (isCraftingNow)
            {
                StartAnimation();

                RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
                UpdateState(RecipeProgress);

                if (RecipeProgress >= 1f)
                {
                    ProcessCompletedCraft();


                    if (!HasRequiredItems())
                    {
                        StopAnimation();
                    }

                    // в любом случае сбрасываем прогресс
                    RecipeProgress = 0f;
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
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null || CurrentRecipe == null)
                return;

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
            {
                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "Animation1",
                    Code = "craft",
                    AnimationSpeed = 2.0f,
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

        private void CheckAnimationFrame(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null!)
                return;

            const int startFrame = 20;
            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("craft"))
            {
                var currentTime = Api.World.ElapsedMilliseconds;
                _lastAnimationCheckTime = currentTime;

                var currentFrame = AnimUtil.animator.Animations[0].CurrentFrame;
                if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
                {
                    PlayPressSound();
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

        private void PlayPressSound()
        {
            if (Api?.Side != EnumAppSide.Client)
                return;

            var capi = Api as ICoreClientAPI;
            capi.World.PlaySoundAt(
                _soundPress,
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
            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    _clientDialog = new GuiDialogPress(DialogTitle, Inventory, Pos, _capi);
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

            if (Api != null)
                Inventory.AfterBlocksLoaded(Api.World);

            if (Api is ICoreClientAPI)
            {
                UpdateMeshes(); // обновляем меши при загрузке
            }

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
        }
        #endregion

        #region Жизненный цикл
        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (ElectricalProgressive == null! || byItemStack == null)
                return;

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this.Block, this);

        }

        /// <summary>
        /// Вызывается при тесселяции блока
        /// </summary>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            base.OnTesselation(mesher, tesselator);

            // Отрисовываем меши предметов (как в холодильнике)
            if (_meshes != null!)
            {
                for (var i = 0; i < _meshes.Length; i++)
                {
                    if (_meshes[i] != null)
                        mesher.AddMeshData(_meshes[i]);
                }
            }

            // если анимации нет, то рисуем блок базовый
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("craft") == false)
            {
                return false;
            }

            return true;  // не рисует базовый блок, если есть анимация
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

            // Очистка как в холодильнике
            _meshes = null!;
            _nowTesselatingShape = null!;
            _nowTesselatingObj = null!;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this._clientDialog?.TryClose();

            // Очищаем ссылки как в холодильнике
            _mesh?.Dispose();
            _resultingShape = null;

            _meshes = null!;
            _nowTesselatingShape = null!;
            _nowTesselatingObj = null!;
            _capi = null!;
        }
        #endregion
    }
}