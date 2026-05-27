using ElectricalProgressive.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EStove;

public class BlockEntityEStove : BlockEntityContainer, IHeatSource, ITexPositionSource
{

    StoveContentsRenderer renderer;  // Кастомный рендерер для динамики котелка

    public const float MaxTemperature = 1350f;

    protected Shape NowTesselatingShape;
    protected CollectibleObject NowTesselatingObj;
    protected MeshData[] Meshes;
    ICoreClientAPI? _capi;
    ICoreServerAPI? _sapi;

    internal InventoryEStove inventory;
    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public float PrevStoveTemperature = 20;
    
    public int MaxConsumption;
    public float StoveTemperature = 20;
    public float InputStackCookingTime;
    GuiDialogBlockEntityEStove? _clientDialog;
    bool _clientSidePrevBurning;

    #region Config


    public virtual float HeatModifier => 1f;
    public virtual int EnviromentTemperature() => 20;
    // Полностью заменяемый maxCookingTime
    public virtual float MaxCookingTime()
    {
        if (InputSlot.Itemstack == null) return 30f;

        var baseTime = InputSlot.Itemstack.Collectible.GetMeltingDuration(Api.World, inventory, InputSlot);

        if (!ElectricalProgressiveQOL.xskillsEnabled || !this.ContainsFood())
            return baseTime;

        // Если xskills включен
        try
        {
            var result = ElectricalProgressiveQOL.methodGetCookingTimeMultiplier?.Invoke(
                null,
                [(BlockEntity)this]
            );

            if (result is float multiplier)
                return baseTime * multiplier;
        }
        catch (Exception ex)
        {
            Api.World.Logger.Warning("Error computing cooking time multiplier (maxCookingTime): {0}", ex);
        }

        return baseTime;
    }

    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "blockestove";
    public virtual string DialogTitle => Lang.Get("electricalprogressiveqol:BlockEStove");
    #endregion


    private long _listenerId;
    private long _listenerId2;

    /// <summary>
    /// Constructor for BlockEntityEStove
    /// </summary>
    public BlockEntityEStove()
    {
        inventory = new InventoryEStove(null!, null!);
        inventory.SlotModified += OnSlotModifid;
        
        Meshes = new MeshData[6];
    }




    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory.pos = Pos;
        inventory.LateInitialize("smelting-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

        if (api.Side == EnumAppSide.Server)
            _sapi = (api as ICoreServerAPI)!;
        else
            _capi = (api as ICoreClientAPI)!;

        

        _listenerId = RegisterGameTickListener(OnBurnTick, 250);
        _listenerId2 = RegisterGameTickListener(On500msTick, 500);

        MaxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);

        
        if (api is ICoreClientAPI capi)
        {
            // Регистрируем рендерер как в оригинале BEFirepit
            renderer = new StoveContentsRenderer(capi, Pos);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "estove");

            
        }

        UpdateMeshes();
        MarkDirty(true);
    }


    // Вспомогательный метод 
    private bool ContainsFood()
    {
        var collectible = this.InputSlot?.Itemstack?.Collectible;
        if (collectible == null) return false;

        if (collectible is BlockCookingContainer || collectible is BlockBucket) return true;

        // Если предмет при переплавке даёт объект с NutritionProps -> считаем это "едой"
        var smelted = collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible;
        if (smelted?.NutritionProps != null) return true;

        return false;
    }

    private bool forceStaticMesh()
    {
        return (InputStack != null && StoveTemperature < 50) && (InputStack.Block?.Code.Path.Contains("claypot") ?? false);
    }


    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server)
            return;

        // 0 слот для топлива, которого у нас нет
        // слоты 3+ для содержимого емкости
        if (slotid == 0 || slotid>2)
            return;

        if (slotid == 1 && inventory[1].Itemstack != null && forceStaticMesh())
        {
            // Удаляем динамический рендерер, если он был активен
            if (renderer.contentStackRenderer != null)
            {
                renderer.contentStackRenderer?.Dispose();
                renderer.contentStackRenderer = null;
            }
            // Сбрасываем статический меш (будет добавлен позже в OnTesselation)
            Meshes[slotid] = null;
            return;
        }

        // если тут пусто
        if (inventory[slotid].Empty)
        {
            Meshes[slotid] = null!;

            // чистим рендерер если внутри уже нет емкости
            if (slotid == 1)
            {
                if (inventory[2].Empty || !(inventory[2].Itemstack?.Collectible is IInFirepitRendererSupplier))
                {
                    renderer.contentStackRenderer?.Dispose();
                    renderer.contentStackRenderer = null;
                }
            }

            if (slotid == 2)
            {
                if (inventory[1].Empty || !(inventory[1].Itemstack?.Collectible is IInFirepitRendererSupplier))
                {
                    renderer.contentStackRenderer?.Dispose();
                    renderer.contentStackRenderer = null;
                }
            }


            return;
        }

        // тут емкость?
        if (inventory[slotid].Itemstack?.Collectible is IInFirepitRendererSupplier)
        {
            Meshes[slotid] = null;  // Не рендерим статично, пусть рендерер handles

            // Обновляем динамический рендерер

            if (slotid == 1)
                UpdateRenderer(slotid, false);
            else if (slotid == 2)
                UpdateRenderer(slotid, true);
            return;
        }

        // проверяем, чтобы не рисовались одновременно входные и выходные вещи
        if (slotid == 1)
        {
            if (!inventory[2].Empty)
            {
                Meshes[slotid] = null;
                return;
            }
        }

        if (slotid == 2)
        {
            //if (!inventory[1].Empty)
            //{
                //Meshes[slotid] = null;
               // return;
            //}
        }

        // генерируем статичный мэш тут
        var meshData = GenMesh(inventory[slotid]);
        if (meshData != null)
        {
            TranslateMesh(meshData, slotid);
            Meshes[slotid] = meshData;
        }
        else
        {
            Meshes[slotid] = null!;
        }
    }


    private void AddPotStaticMesh(ITerrainMeshPool mesher, ITesselatorAPI tesselator, ItemStack potStack)
    {
        Vintagestory.API.Common.Block potBlock = potStack.Block;
        if (potBlock == null) return;

        string potKey = "estove-pot-static-" + potBlock.Code;
        string lidKey = "estove-lid-static-" + potBlock.Code;

        MeshData potMesh = ObjectCacheUtil.GetOrCreate<MeshData>(Api, potKey, () =>
        {
            Shape shape = Shape.TryGet(Api, "shapes/block/clay/pot-opened-empty.json");
            if (shape == null) return null;
            tesselator.TesselateShape(potBlock, shape, out MeshData mesh);
            mesh.Translate(0, 1.04f, 0);
            return mesh;
        });

        MeshData lidMesh = ObjectCacheUtil.GetOrCreate<MeshData>(Api, lidKey, () =>
        {
            Shape shape = Shape.TryGet(Api, "shapes/block/clay/pot-part-lid.json");
            if (shape == null) return null;
            tesselator.TesselateShape(potBlock, shape, out MeshData mesh);
            mesh.Translate(0, 1.38375f, 0);
            return mesh;
        });

        if (potMesh != null) mesher.AddMeshData(potMesh);
        if (lidMesh != null) mesher.AddMeshData(lidMesh);
    }

    private void UpdateRenderer(int slotId, bool outt)
    {
        if (renderer == null || Api?.Side != EnumAppSide.Client)
            return;

        ItemStack contentStack = inventory[slotId].Itemstack;

        bool useOldRenderer =
            renderer.ContentStack != null &&
            renderer.contentStackRenderer != null &&
            contentStack != null &&
            renderer.ContentStack.Equals(Api.World, contentStack, GlobalConstants.IgnoredStackAttributes);

        if (useOldRenderer)
            return;

        renderer.contentStackRenderer?.Dispose();
        renderer.contentStackRenderer = null;

        if (contentStack?.Collectible is IInFirepitRendererSupplier)
        {
            
            var be = new BlockEntityFirepit();
            be.Pos =  new BlockPos(new Vec3i(Pos.X, Pos.Y+1,Pos.Z),Pos.dimension);
            

            IInFirepitRenderer childrenderer = (contentStack?.Collectible as IInFirepitRendererSupplier).GetRendererWhenInFirepit(contentStack, be, outt);
            (be as IDisposable)?.Dispose();
            if (childrenderer != null)
            {
                renderer.SetChildRenderer(contentStack, childrenderer);
                
                return;
            }
            
        }


    }


    /// <summary>
    /// Позиционирование меша в зависимости от слота
    /// </summary>
    /// <param name="meshData"></param>
    /// <param name="slotId"></param>
    public void TranslateMesh(MeshData meshData, int slotId)
    {
        if (meshData == null)
            return;
        float x = 0, y = 0;
        switch (slotId)
        {
            case 1: y = 1.04f; break;
            case 2: y = 1.04f; break;
        }

        if (!Inventory[slotId].Empty)
        {
            if (Inventory[slotId].Itemstack.Class == EnumItemClass.Block)
            {
                meshData.Scale(new Vec3f(0.5f, 0, 0.5f), 0.93f, 0.93f, 0.93f);
                meshData.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, 8 * GameMath.DEG2RAD, 0);
            }
            else
            {
                meshData.Scale(new Vec3f(0.5f, 0, 0.5f), 1.0f, 1.0f, 1.0f);
                meshData.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, 15 * GameMath.DEG2RAD, 0);
            }
        }
        meshData.Translate(x, y, 0.025f);

        var orientationRotate = Block.Variant["horizontalorientation"] switch
        {
            "east" => 270,
            "south" => 180,
            "west" => 90,
            _ => 0
        };

        meshData.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, orientationRotate * GameMath.DEG2RAD, 0);
    }

    public Size2i AtlasSize => _capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            AssetLocation assetLocation = null!;

            if (NowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
            }
            else if (NowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
                else if (block.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
            }

            if (assetLocation == null && NowTesselatingShape != null)
            {
                NowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation!);
            }

            if (assetLocation == null)
            {
                var domain = NowTesselatingObj.Code.Domain;
                assetLocation = new AssetLocation(domain, "textures/item/" + textureCode);
                Api.World.Logger.Warning("Texture {0} not found in item or shape textures, using fallback path: {1}", textureCode, assetLocation);
            }

            return GetOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition GetOrCreateTexPos(AssetLocation texturePath)
    {
        var textureAtlasPosition = _capi!.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition == null)
        {
            // берем только base текстуру (первую из кучи наваленных)
            var pos = texturePath.Path.IndexOf("++");
            if (pos >= 0)
            {
                texturePath.Path = texturePath.Path.Substring(0, pos);
            }

            var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset != null)
            {
                _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out var num, out textureAtlasPosition, null, 0.005f);
            }
            else
            {
                Api.World.Logger.Warning("Texture not found at path: {0}", texturePath);
            }
        }
        return textureAtlasPosition!;
    }

    public MeshData? GenMesh(ItemSlot slot)
    {
        var stack = slot.Itemstack;
        
        var meshsource = stack.Collectible as IContainedMeshSource;
        MeshData meshData;
        if (meshsource != null)
        {
            meshData = meshsource.GenMesh(slot, _capi!.BlockTextureAtlas, Pos);
            meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
        }
        else
        {
            if (stack.Class == EnumItemClass.Block)
            {
                meshData = _capi!.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
            }
            else
            {
                NowTesselatingObj = stack.Collectible;
                NowTesselatingShape = null!;
                if (stack.Item.Shape != null!)
                {
                    NowTesselatingShape = _capi!.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                }
                try
                {
                    _capi!.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
                catch (Exception e)
                {
                    Api.World.Logger.Error("Failed to tessellate item {0}: {1}", stack.Item.Code, e.Message);
                    meshData = null!;
                }
                _capi!.TesselatorManager.ThreadDispose();
            }
        }
        return meshData!;
    }

    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tessThreadTesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        for (var i = 0; i < Meshes.Length; i++)
        {
            if (Meshes[i] != null) mesher.AddMeshData(Meshes[i]);
        }

        if (forceStaticMesh())
        {
            AddPotStaticMesh(mesher, tessThreadTesselator, InputStack);
        }

        return false;
    }

    /// <summary>
    /// Обновляет меши для всех слотов
    /// </summary>
    public void UpdateMeshes()
    {
        for (var i = 0; i < inventory.Count - 1; i++)
        {
            UpdateMesh(i);
        }
        
        MarkDirty(true);
    }

    private void OnSlotModifid(int slotid)
    {
        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server);
        if (Api is ICoreClientAPI && _clientDialog != null)
            SetDialogValues(_clientDialog.Attributes);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();
    }

    public bool IsBurning;

    private void OnBurnTick(float dt)
    {
        
        if (Api is ICoreClientAPI)
        {
            renderer?.OnUpdate(InputStackTemp);  // Обновляем для анимации/звуков котелка
        }
        
        if (Api is ICoreClientAPI)
            return;

        var beh = GetBehavior<BEBehaviorEStove>();

        if (beh == null) // если нет поведения то все плохо
            return;

        if (IsBurning)
        {
            StoveTemperature = ChangeTemperature(StoveTemperature, beh.PowerSetting * 1.0F / MaxConsumption * MaxTemperature, dt);
        }
        if (CanHeatInput())
            HeatInput(dt);
        else
            InputStackCookingTime = 0;
        if (CanHeatOutput())
            HeatOutput(dt);
        
        // Проверяем, завершен ли процесс плавки (особенно для топленого жира)
        bool isCompleted = IsInputSlotCompleted();
        
        if (CanSmeltInput() && InputStackCookingTime > MaxCookingTime() && !isCompleted)
            SmeltItems();

        if (beh.PowerSetting > 0)
        {
            if (!IsBurning)
            {
                IsBurning = true;
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                MarkDirty(true);
            }
        }
        else if (IsBurning)
        {
            IsBurning = false;
            Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
            MarkDirty(true);
            Api.World.PlaySoundAt(new AssetLocation("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
        }
        if (!IsBurning) StoveTemperature = ChangeTemperature(StoveTemperature, EnviromentTemperature(), dt);
    }

    private void On500msTick(float dt)
    {
        // лишний раз не синхронизируем 
        if (Api is ICoreServerAPI && (IsBurning || PrevStoveTemperature != StoveTemperature))
            MarkDirty();

        PrevStoveTemperature = StoveTemperature;
    }

    public static float ChangeTemperature(float fromTemp, float toTemp, float dt)
    {
        var diff = Math.Abs(fromTemp - toTemp);
        dt = dt + dt * (diff / 28);
        if (diff < dt) return toTemp;
        if (fromTemp > toTemp) dt = -dt;
        if (Math.Abs(fromTemp - toTemp) < 1) return toTemp;
        return fromTemp + dt;
    }


    public void HeatInput(float dt)
    {
        float oldTemp = InputStackTemp, nowTemp = oldTemp;
        var meltingPoint = InputSlot.Itemstack.Collectible.GetMeltingPoint(Api.World, inventory, InputSlot);
        int stackSize = InputSlot.Itemstack.StackSize;  

        if (oldTemp < StoveTemperature)
        {
            var f = (1 + GameMath.Clamp((StoveTemperature - oldTemp) / 30, 0, 1.6f)) * dt;
            if (nowTemp >= meltingPoint) f /= 11;

            var newTemp = ChangeTemperature(oldTemp, StoveTemperature, f);
            // Усреднение температуры по всему стаку (как в костре)
            newTemp = (newTemp + (stackSize - 1) * oldTemp) / stackSize;   

            var maxTemp = 0;
            if (InputStack.ItemAttributes != null)
            {
                maxTemp = Math.Max(InputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0, InputStack.ItemAttributes["maxTemperature"]?.AsInt() ?? 0);
            }
            else
            {
                maxTemp = InputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0;
            }
            if (maxTemp > 0) newTemp = Math.Min(maxTemp, newTemp);
            if (oldTemp != newTemp)
            {
                InputStackTemp = newTemp;
                nowTemp = newTemp;
            }
        }
        
        // Если процесс уже завершен (топленый жир готов), не увеличиваем время готовки
        if (IsInputSlotCompleted())
        {
            InputStackCookingTime = 0;
            return;
        }
        
        if (nowTemp >= meltingPoint)
        {
            var diff = nowTemp / meltingPoint;
            InputStackCookingTime += GameMath.Clamp((int)(diff), 1, 30) * dt;
        }
        else if (InputStackCookingTime > 0) InputStackCookingTime--;
    }

    public void HeatOutput(float dt)
    {
        var oldTemp = OutputStackTemp;
        if (oldTemp < StoveTemperature)
        {
            var newTemp = ChangeTemperature(oldTemp, StoveTemperature, 2 * dt);
            var maxTemp = Math.Max(OutputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0, OutputStack.ItemAttributes["maxTemperature"]?.AsInt() ?? 0);
            if (maxTemp > 0) newTemp = Math.Min(maxTemp, newTemp);
            if (oldTemp != newTemp) OutputStackTemp = newTemp;
        }
    }

    public float InputStackTemp
    {
        get => GetTemp(InputStack);
        set => SetTemp(InputStack, value);
    }

    public float OutputStackTemp
    {
        get => GetTemp(OutputStack);
        set => SetTemp(OutputStack, value);
    }

    float GetTemp(ItemStack stack)
    {
        if (stack == null) return EnviromentTemperature();
        if (inventory.CookingSlots.Length > 0)
        {
            var haveStack = false;
            float lowestTemp = 0;
            for (var i = 0; i < inventory.CookingSlots.Length; i++)
            {
                var cookingStack = inventory.CookingSlots[i].Itemstack;
                if (cookingStack != null)
                {
                    var stackTemp = cookingStack.Collectible.GetTemperature(Api.World, cookingStack);
                    lowestTemp = haveStack ? Math.Min(lowestTemp, stackTemp) : stackTemp;
                    haveStack = true;
                }
            }
            return lowestTemp;
        }
        return stack.Collectible.GetTemperature(Api.World, stack);
    }

    void SetTemp(ItemStack stack, float value)
    {
        if (stack == null) return;
        if (inventory.CookingSlots.Length > 0)
        {
            for (var i = 0; i < inventory.CookingSlots.Length; i++)
            {
                if (inventory.CookingSlots[i].Itemstack != null)
                    inventory.CookingSlots[i].Itemstack.Collectible.SetTemperature(Api.World, inventory.CookingSlots[i].Itemstack, value);
            }
        }
        else stack.Collectible.SetTemperature(Api.World, stack, value);
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return IsBurning ? MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F) : 0;
    }

    public bool CanHeatInput()
    {
        // Если входной слот пуст - не греем
        if (InputSlot?.Itemstack == null)
            return false;
        
        // Если процесс плавки уже завершен (топленый жир готов) - не греем
        if (IsInputSlotCompleted())
            return false;
        
        return CanSmeltInput() || (InputStack != null && InputStack?.ItemAttributes?["allowHeating"] != null && InputStack.ItemAttributes["allowHeating"].AsBool());
    }

    public bool CanHeatOutput()
    {
        return OutputStack?.ItemAttributes?["allowHeating"] != null && OutputStack.ItemAttributes["allowHeating"].AsBool();
    }

    public bool CanSmeltInput()
    {
        if (InputStack == null)
            return false;

        if (InputStack.Collectible.OnSmeltAttempt(inventory)) MarkDirty(true);

        CombustibleProperties combustibleProps = InputStack.Collectible.GetCombustibleProperties(Api.World, InputStack, null);

        return
            InputStack.Collectible.CanSmelt(Api.World, inventory, InputSlot.Itemstack, OutputSlot.Itemstack)
            && (combustibleProps == null || !combustibleProps.RequiresContainer);
    }

    /// <summary>
    /// Проверяет, завершен ли процесс плавки во входном слоте
    /// (особенно важно для случаев, когда предмет не перемещается в выходной слот, как с топленым жиром)
    /// </summary>
    public bool IsInputSlotCompleted()
    {
        if (InputSlot?.Itemstack == null)
            return true; // Пустой слот считаем завершенным
        
        // Проверяем, можно ли переплавить предмет дальше
        bool canSmelt = InputSlot.Itemstack.Collectible.CanSmelt(Api.World, inventory, InputSlot.Itemstack, null);
        
        // Если предмет нельзя переплавить - процесс завершен
        if (!canSmelt)
            return true;
        
        // Проверяем, достигнута ли температура плавления и прошло ли достаточно времени
        float meltingPoint = InputSlot.Itemstack.Collectible.GetMeltingPoint(Api.World, inventory, InputSlot);
        if (InputStackTemp >= meltingPoint && InputStackCookingTime >= MaxCookingTime())
        {
            // Дополнительная проверка: изменится ли предмет после плавки?
            var smeltedStack = InputSlot.Itemstack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack;
            if (smeltedStack != null && smeltedStack.Collectible.Code == InputSlot.Itemstack.Collectible.Code)
            {
                // Результат плавки - тот же предмет (как с топленым жиром)
                return true;
            }
        }
        
        return false;
    }

    public void SmeltItems()
    {
        if (InputSlot.Empty)
            return;

        // Сохраняем информацию до плавки
        float inputTemp = InputStackTemp;
        ItemStack oldInputStack = InputSlot.Itemstack.Clone();
        int oldStackSize = InputSlot.Itemstack.StackSize;

        // Запоминаем температуру входного стака до плавки
        ItemSlot outputSlot = OutputSlot;
        ItemStack oldOutputStack = outputSlot.Itemstack;
        float oldOutputTemp = (oldOutputStack != null) ? GetTemp(oldOutputStack) : 0;
        int oldOutputSize = oldOutputStack?.StackSize ?? 0;

        // Вызываем стандартную логику переплавки
        InputStack.Collectible.DoSmelt(Api.World, inventory, InputSlot, outputSlot);

        // Определяем, что произошло
        bool itemCodeChanged = InputSlot.Itemstack != null &&
                               oldInputStack.Collectible.Code != InputSlot.Itemstack?.Collectible.Code;

        bool stackSizeDecreased = InputSlot.Itemstack != null &&
                                  oldStackSize > InputSlot.Itemstack.StackSize;

        // Если предмет остался во входном слоте
        if (InputSlot.Itemstack != null)
        {
            // Случай 1: Стек уменьшился, но предмет тот же (пережарка) - СОХРАНЯЕМ температуру
            if (stackSizeDecreased && !itemCodeChanged)
            {
                InputSlot.Itemstack.Collectible.SetTemperature(Api.World, InputSlot.Itemstack, inputTemp);
            }
            // Случай 2: Предмет изменился (известняк → негашеная известь) - СОХРАНЯЕМ температуру
            else if (itemCodeChanged)
            {
                InputSlot.Itemstack.Collectible.SetTemperature(Api.World, InputSlot.Itemstack, inputTemp);
            }
            // Случай 3: Топленый жир и подобное (все осталось то же, стек не изменился) - СБРАСЫВАЕМ
            else if (!stackSizeDecreased && !itemCodeChanged)
            {
                InputSlot.Itemstack.Collectible.SetTemperature(Api.World, InputSlot.Itemstack, EnviromentTemperature());
            }

            InputStackCookingTime = 0;
            InputSlot.MarkDirty();
        }
        else
        {
            // Входной слот опустел
            InputStackCookingTime = 0;
        }

        // Обработка выходного слота (без изменений)
        ItemStack newOutputStack = outputSlot.Itemstack;
        if (newOutputStack != null)
        {
            int addedCount;
            if (oldOutputStack == null)
            {
                addedCount = newOutputStack.StackSize;
            }
            else if (oldOutputStack.Equals(Api.World, newOutputStack, GlobalConstants.IgnoredStackAttributes))
            {
                addedCount = newOutputStack.StackSize - oldOutputSize;
                if (addedCount <= 0) addedCount = newOutputStack.StackSize;
            }
            else
            {
                addedCount = newOutputStack.StackSize;
            }

            if (addedCount > 0)
            {
                float newTemp;
                if (oldOutputStack != null &&
                    oldOutputStack.Equals(Api.World, newOutputStack, GlobalConstants.IgnoredStackAttributes))
                {
                    newTemp = (inputTemp * addedCount + oldOutputTemp * oldOutputSize) / newOutputStack.StackSize;
                }
                else
                {
                    newTemp = inputTemp;
                }

                SetTemp(newOutputStack, newTemp);
            }
        }

        MarkDirty(true);
        InputSlot.MarkDirty();
        OutputSlot?.MarkDirty();
    }

    public void OnBlockInteract(IPlayer byPlayer, bool isOwner, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            return;
        byte[] data;
        using (var ms = new MemoryStream())
        {
            var writer = new BinaryWriter(ms);
            writer.Write("BlockEntityStove");
            writer.Write(DialogTitle);
            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            tree.ToBytes(writer);
            data = ms.ToArray();
        }
        ((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, blockSel.Position, (int)EnumBlockStovePacket.OpenGUI, data);
        byPlayer.InventoryManager.OpenInventory(inventory);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null) Inventory.AfterBlocksLoaded(Api.World);
        StoveTemperature = tree.GetFloat("stoveTemperature");
        InputStackCookingTime = tree.GetFloat("oreCookingTime");
        if (Api != null)
        {
            if (Api.Side == EnumAppSide.Client && _clientDialog != null) SetDialogValues(_clientDialog.Attributes);
            if (Api.Side == EnumAppSide.Client && _clientSidePrevBurning != IsBurning)
            {
                _clientSidePrevBurning = IsBurning;
                MarkDirty(true);
            }
            inventory.AfterBlocksLoaded(Api.World);
            
            if (Api.Side == EnumAppSide.Client)
            {
                UpdateMeshes();
                
            }
            
        }
    }

    // Полностью заменяемый SetDialogValues
    void SetDialogValues(ITreeAttribute dialogTree)
    {
        dialogTree.SetFloat("stoveTemperature", StoveTemperature);
        dialogTree.SetFloat("oreCookingTime", InputStackCookingTime);

        if (InputSlot.Itemstack != null)
        {
            var meltingDuration = InputSlot.Itemstack.Collectible.GetMeltingDuration(Api.World, inventory, InputSlot);
            dialogTree.SetFloat("oreTemperature", InputStackTemp);

            var maxCooking = meltingDuration;

            // Если xskills включен
            if (ElectricalProgressiveQOL.xskillsEnabled && this.ContainsFood())
            {
                try
                {
                    var result = ElectricalProgressiveQOL.methodGetCookingTimeMultiplier?.Invoke(
                        null,
                        [(BlockEntity)this]
                    );

                    if (result is float multiplier)
                        maxCooking *= multiplier;
                }
                catch (Exception ex)
                {
                    Api.World.Logger.Warning("Error computing cooking time multiplier (SetDialogValues): {0}", ex);
                }
            }

            dialogTree.SetFloat("maxOreCookingTime", maxCooking);
        }
        else
        {
            dialogTree.RemoveAttribute("oreTemperature");
            dialogTree.RemoveAttribute("maxOreCookingTime");
        }

        dialogTree.SetString("outputText", inventory.GetOutputText());
        dialogTree.SetInt("haveCookingContainer", inventory.HaveCookingContainer ? 1 : 0);
        dialogTree.SetInt("quantityCookingSlots", inventory.CookingSlots.Length);
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        Inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
        tree.SetFloat("stoveTemperature", StoveTemperature);
        tree.SetFloat("oreCookingTime", InputStackCookingTime);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        var electricity = ElectricalProgressive;
        if (electricity == null || byItemStack == null)
            return;

        //задаем электрические параметры блока/проводника
        LoadEProperties.Load(this.Block, this);
    }

    /// <summary>
    /// Вызывается при удалении блока
    /// </summary>
    public override void OnBlockRemoved()
    {
        
        base.OnBlockRemoved();


        renderer?.Dispose();
        renderer = null;

        if (_clientDialog != null)
        {
            _clientDialog?.TryClose();
            _clientDialog?.Dispose();
            _clientDialog = null!;
        }




        // Освобождение ссылок на API
        _capi = null;
        _sapi = null;

        // Очистка ссылок на меши
        if (Meshes != null)
        {
            for (var i = 0; i < Meshes.Length; i++)
            {
                Meshes[i] = null!;
            }
        }
        NowTesselatingObj = null!;
        NowTesselatingShape = null!;
    }


    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        

        base.OnBlockUnloaded();

        renderer?.Dispose();
        renderer = null;

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        if (_clientDialog != null)
        {
            _clientDialog?.TryClose();
            _clientDialog?.Dispose();
            _clientDialog = null!;
        }

        // Отменяем слушателей тика игры
        UnregisterGameTickListener(_listenerId);
        UnregisterGameTickListener(_listenerId2);



        // Освобождение ссылок на API
        _capi = null!;
        _sapi = null!;

        // Очистка ссылок на меши
        if (Meshes != null)
        {
            for (var i = 0; i < Meshes.Length; i++)
            {
                Meshes[i] = null!;
            }
        }
        NowTesselatingObj = null!;
        NowTesselatingShape = null!;
    }

    /// <summary>
    /// Вызывается при разрушении блока
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (InputStack != null && Api.Side==EnumAppSide.Server)
            Api.World.SpawnItemEntity(InputStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));


    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        if (packetid < 1000)
        {
            Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();
            return;
        }
        if (packetid == (int)EnumBlockStovePacket.CloseGUI)
        {
            if (player.InventoryManager != null) player.InventoryManager.CloseInventory(Inventory);
        }
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        if (packetid == (int)EnumBlockStovePacket.OpenGUI)
        {
            using var ms = new MemoryStream(data);
            var reader = new BinaryReader(ms);
            var dialogClassName = reader.ReadString();
            var dialogTitle = reader.ReadString();
            var tree = new TreeAttribute();
            tree.FromBytes(reader);
            Inventory.FromTreeAttributes(tree);
            Inventory.ResolveBlocksOrItems();
            var clientWorld = (IClientWorldAccessor)Api.World;
            var dtree = new SyncedTreeAttribute();
            SetDialogValues(dtree);
            if (_clientDialog != null)
            {
                _clientDialog?.TryClose();
                _clientDialog = null!;
            }
            else
            {
                _clientDialog = new GuiDialogBlockEntityEStove(dialogTitle, Inventory, Pos, dtree, _capi!);
                _clientDialog.OnClosed += () => { _clientDialog?.Dispose(); _clientDialog = null!; };
                _clientDialog.TryOpen();
            }
        }
        if (packetid == (int)EnumBlockEntityPacketId.Close)
        {
            ((IClientWorldAccessor)Api.World).Player.InventoryManager.CloseInventory(Inventory);
        }
    }

    public ItemSlot InputSlot => inventory[1];
    public ItemSlot OutputSlot => inventory[2];
    public ItemSlot[] OtherCookingSlots => inventory.CookingSlots;
    public ItemStack InputStack
    {
        get => inventory[1].Itemstack;
        set { inventory[1].Itemstack = value; inventory[1].MarkDirty(); }
    }
    public ItemStack OutputStack
    {
        get => inventory[2].Itemstack;
        set { inventory[2].Itemstack = value; inventory[2].MarkDirty(); }
    }

    public CombustibleProperties FuelCombustibleOpts => GetCombustibleOpts(0);
    public CombustibleProperties GetCombustibleOpts(int slotid)
    {
        var slot = inventory[slotid];
        return slot.Itemstack.Collectible.CombustibleProps!;
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        foreach (var slot in Inventory)
        {
            if (slot.Itemstack == null) continue;
            if (slot.Itemstack.Class == EnumItemClass.Item)
                itemIdMapping[slot.Itemstack.Item.Id] = slot.Itemstack.Item.Code;
            else
                blockIdMapping[slot.Itemstack.Block.BlockId] = slot.Itemstack.Block.Code;
            slot.Itemstack.Collectible.OnStoreCollectibleMappings(Api.World, slot, blockIdMapping, itemIdMapping);
        }
        foreach (var slot in inventory.CookingSlots)
        {
            if (slot.Itemstack == null) continue;
            if (slot.Itemstack.Class == EnumItemClass.Item)
                itemIdMapping[slot.Itemstack.Item.Id] = slot.Itemstack.Item.Code;
            else
                blockIdMapping[slot.Itemstack.Block.BlockId] = slot.Itemstack.Block.Code;
            slot.Itemstack.Collectible.OnStoreCollectibleMappings(Api.World, slot, blockIdMapping, itemIdMapping);
        }
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        foreach (var slot in Inventory)
        {
            if (slot.Itemstack == null)
                continue;
            if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve))
                slot.Itemstack = null;
            else
                slot.Itemstack.Collectible.OnLoadCollectibleMappings(worldForResolve, slot, oldBlockIdMapping, oldItemIdMapping, false);
        }
        foreach (var slot in inventory.CookingSlots)
        {
            if (slot.Itemstack == null)
                continue;
            if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, Api.World))
                slot.Itemstack = null;
            else
                slot.Itemstack.Collectible.OnLoadCollectibleMappings(worldForResolve, slot, oldBlockIdMapping, oldItemIdMapping, false);
        }

    }


    /// <summary>
    /// Получает информацию о блоке для игрока
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="stringBuilder"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);



        if (InputStack != null)
        {
            var temp = (int)InputStack.Collectible.GetTemperature(Api.World, InputStack);
            stringBuilder.AppendLine();
            if (temp <= 25)
                stringBuilder.AppendLine(Lang.Get("Contents") + " " + InputStack.StackSize + "×" + InputStack.GetName() + "\n└ " + Lang.Get("Temperature") + " " + Lang.Get("Cold"));
            else
                stringBuilder.AppendLine(Lang.Get("Contents") + " " + InputStack.StackSize + "×" + InputStack.GetName() + "\n└ " + Lang.Get("Temperature") + " " + temp + " °C");
        }
    }
}