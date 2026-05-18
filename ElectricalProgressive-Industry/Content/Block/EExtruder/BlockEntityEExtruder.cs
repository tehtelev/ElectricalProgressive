﻿using ElectricalProgressive.RecipeSystem;
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

namespace ElectricalProgressive.Content.Block.EExtruder
{
    public class BlockEntityEExtruder : BlockEntityGenericTypedContainer
    {
        // Конфигурация
        internal InventoryDrawing inventory;
        private GuiDialogExtruder _clientDialog;
        public override string InventoryClassName => "edrawing";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        private static MeshData? _mesh; // кеш для меша, который используется в анимации. Кеш нужен, чтобы не загружать меш из ресурсов каждый раз при тесселяции блока, а использовать уже загруженный и обработанный меш.
        private static Shape? _resultingShape; // кеш для формы, которая используется в анимации. Кеш нужен, чтобы не загружать форму из ресурсов каждый раз при тесселяции блока, а использовать уже загруженную и обработанную форму.

        // Состояние крафта
        public ExtruderRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;

        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public override string DialogTitle => Lang.Get("edrawing-title-gui");
        public override InventoryBase Inventory => inventory;

        private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;


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


        private ILoadedSound _ambientSound;
        private AssetLocation _centrifugeSound;

        public BlockEntityEExtruder()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventoryDrawing(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
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
                if (AnimUtil != null)
                {
                    PrepareAnimUtil(api, InventoryClassName);
                    AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
                }

                _centrifugeSound = new AssetLocation("electricalprogressiveindustry:sounds/ecentrifuge/centrifuge.ogg");

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

            MarkDirty();
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref ExtruderRecipe currentRecipe, ref string currentRecipeName, InventoryDrawing inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.ExtruderRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
                {
                    currentRecipe = recipe;
                    // Берем название из первого выхода
                    if (recipe.Outputs != null && recipe.Outputs.Length > 0)
                    {
                        currentRecipeName = recipe.Outputs[0].ResolvedItemstack?.GetName() ?? "Unknown";
                    }
                    return true;
                }
            }

            return false;
        }



        private static bool MatchesRecipe(ExtruderRecipe recipe, InventoryDrawing inventory)
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
                foreach (var output in CurrentRecipe.Outputs)
                {
                    // Проверяем шанс выпадения
                    if (output.Chance < 1.0f && Api.World.Rand.NextDouble() > output.Chance)
                    {
                        continue; // Пропускаем этот выход если не выпал
                    }

                    // Создаем копию выходного предмета
                    var outputStack = output.ResolvedItemstack?.Clone();
                    if (outputStack == null) continue;

                    // Пытаемся разместить в первом выходном слоте
                    if (!TryMergeToOutputSlot(outputStack, OutputSlot1))
                    {
                        // Если не поместилось в первый слот, пробуем второй
                        if (!TryMergeToOutputSlot(outputStack, OutputSlot1))
                        {
                            // Если все слоты заняты, выбрасываем в мир
                            Api.World.SpawnItemEntity(outputStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                        }
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
                Api.Logger.Error($"Crafting error in EDrawing at {Pos}: {ex}");
            }
        }

        private static bool TryMergeToOutputSlot(ItemStack stack, ItemSlot targetSlot)
        {
            if (targetSlot.Empty)
            {
                targetSlot.Itemstack = stack;
                targetSlot.MarkDirty();
                return true;
            }
            else if (targetSlot.Itemstack.Collectible == stack.Collectible &&
                     targetSlot.Itemstack.StackSize < targetSlot.Itemstack.Collectible.MaxStackSize)
            {
                var freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.Itemstack.StackSize;
                var toAdd = Math.Min(freeSpace, stack.StackSize);

                targetSlot.Itemstack.StackSize += toAdd;
                stack.StackSize -= toAdd;

                targetSlot.MarkDirty();
                return stack.StackSize == 0; // Возвращаем true если весь стек поместился
            }

            return false; // Не удалось поместить
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
            var beh = GetBehavior<BEBehaviorEExtruder>();
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
                if (!_wasCraftingLastTick)
                {
                    StartSound();
                }

                StartAnimation();

                RecipeProgress = Math.Min(RecipeProgress + (float)(beh.PowerSetting / CurrentRecipe.EnergyOperation), 1f);
                UpdateState(RecipeProgress);

                if (RecipeProgress >= 1f)
                {
                    ProcessCompletedCraft();

                    
                    if (!HasRequiredItems())
                    {
                        StopAnimation();
                        StopSound();
                    }

                    // в любом случае сбрасываем прогресс
                    RecipeProgress = 0f;
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

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);

            MarkDirty(true);
        }
        #endregion



        /// <summary>
        /// Запуск звука
        /// </summary>
        public void StartSound()
        {
            if (this._ambientSound != null)
                return;
            if ((Api != null ? (Api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
                return;
            this._ambientSound = (this.Api as ICoreClientAPI).World.LoadSound(new SoundParams()
            {
                Location = _centrifugeSound,
                ShouldLoop = true,
                Position = this.Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                DisposeOnFinish = false,
                Volume = 1f,
            });

            this._ambientSound.Start();
        }



        /// <summary>
        /// Остановка звука
        /// </summary>
        public void StopSound()
        {
            if (this._ambientSound == null)
                return;
            this._ambientSound.Stop();
            this._ambientSound?.Dispose();
            this._ambientSound = (ILoadedSound)null;
        }




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
                    AnimationSpeed = 1.0f,
                    EaseOutSpeed = 1.0f,
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
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
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
                _centrifugeSound,
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
                    _clientDialog = new GuiDialogExtruder(DialogTitle, Inventory, Pos, _capi);
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
            this.invDialog = (GuiDialogBlockEntity)null;
        }
        #endregion

        #region Сохранение состояния
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
            RecipeProgress = tree.GetFloat("PowerCurrent");

            if (Api != null)
                try
                {
                    Inventory.AfterBlocksLoaded(Api.World);
                }
                catch
                {

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

            if (ElectricalProgressive == null || byItemStack == null)
                return;

            //задаем электрические параметры блока/проводника
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


        #endregion
    }
}
