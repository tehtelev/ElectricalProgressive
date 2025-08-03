using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public static float maxTemperature = 1350f;

    protected Shape nowTesselatingShape;
    protected CollectibleObject nowTesselatingObj;
    protected MeshData[] meshes;
    ICoreClientAPI? capi;
    ICoreServerAPI? sapi;

    internal InventoryEStove inventory;
    private BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public float prevStoveTemperature = 20;
    
    public int maxConsumption;
    public float stoveTemperature = 20;
    public float inputStackCookingTime;
    GuiDialogBlockEntityEStove? clientDialog;
    bool clientSidePrevBurning;

    #region Config
    public virtual float HeatModifier => 1f;
    public virtual int enviromentTemperature() => 20;
    public virtual float maxCookingTime()
    {
        return inputSlot.Itemstack == null ? 30f : inputSlot.Itemstack.Collectible.GetMeltingDuration(Api.World, inventory, inputSlot);
    }
    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "blockestove";
    public virtual string DialogTitle => Lang.Get("BlockEStove");
    #endregion


    private long listenerId;
    private long listenerId2;

    /// <summary>
    /// Constructor for BlockEntityEStove
    /// </summary>
    public BlockEntityEStove()
    {
        inventory = new InventoryEStove(null!, null!);
        inventory.SlotModified += OnSlotModifid;
        
        meshes = new MeshData[6];
    }

    public (EParams, int) Eparams
    {
        get => this.ElectricalProgressive?.Eparams ?? (new EParams(), 0);
        set => this.ElectricalProgressive!.Eparams = value;
    }

    public EParams[] AllEparams
    {
        get => this.ElectricalProgressive?.AllEparams ?? new EParams[]
        {
            new EParams(), new EParams(), new EParams(), new EParams(), new EParams(), new EParams()
        };
        set
        {
            if (this.ElectricalProgressive != null) this.ElectricalProgressive.AllEparams = value;
        }
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
            sapi = (api as ICoreServerAPI)!;
        else
            capi = (api as ICoreClientAPI)!;

        UpdateMeshes();
        MarkDirty(true);

        listenerId=RegisterGameTickListener(OnBurnTick, 250);
        listenerId2=RegisterGameTickListener(On500msTick, 500);

        maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
    }

    public void UpdateMesh(int slotid)
    {
        if (Api == null || Api.Side == EnumAppSide.Server)
            return;
        if (slotid == 0)
            return;
        if (inventory[slotid].Empty)
        {
            meshes[slotid] = null!;
            return;
        }
        MeshData meshData = GenMesh(inventory[slotid].Itemstack);
        if (meshData != null)
        {
            TranslateMesh(meshData, slotid);
            meshes[slotid] = meshData;
        }
        else
        {
            meshes[slotid] = null!;
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

        int orientationRotate = Block.Variant["horizontalorientation"] switch
        {
            "east" => 270,
            "south" => 180,
            "west" => 90,
            _ => 0
        };

        meshData.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, orientationRotate * GameMath.DEG2RAD, 0);
    }

    public Size2i AtlasSize => capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            AssetLocation assetLocation = null!;

            if (nowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
            }
            else if (nowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
                else if (block.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture.Baked.BakedName;
            }

            if (assetLocation == null && nowTesselatingShape != null)
            {
                nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation!);
            }

            if (assetLocation == null)
            {
                string domain = nowTesselatingObj.Code.Domain;
                assetLocation = new AssetLocation(domain, "textures/item/" + textureCode);
                Api.World.Logger.Warning("Texture {0} not found in item or shape textures, using fallback path: {1}", textureCode, assetLocation);
            }

            return getOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
    {
        TextureAtlasPosition textureAtlasPosition = capi!.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition == null)
        {
            // берем только base текстуру (первую из кучи наваленных)
            int pos = texturePath.Path.IndexOf("++");
            if (pos >= 0)
            {
                texturePath.Path = texturePath.Path.Substring(0, pos);
            }

            IAsset asset = capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset != null)
            {
                int num;
                capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out num, out textureAtlasPosition, null, 0.005f);
            }
            else
            {
                Api.World.Logger.Warning("Texture not found at path: {0}", texturePath);
            }
        }
        return textureAtlasPosition!;
    }

    public MeshData GenMesh(ItemStack stack)
    {
        
        IContainedMeshSource? meshsource = stack.Collectible as IContainedMeshSource;
        MeshData meshData;
        if (meshsource != null)
        {
            meshData = meshsource.GenMesh(stack, capi!.BlockTextureAtlas, Pos);
            meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
        }
        else
        {
            if (stack.Class == EnumItemClass.Block)
            {
                meshData = capi!.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
            }
            else
            {
                nowTesselatingObj = stack.Collectible;
                nowTesselatingShape = null!;
                if (stack.Item.Shape != null!)
                {
                    nowTesselatingShape = capi!.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                }
                try
                {
                    capi!.Tesselator.TesselateItem(stack.Item, out meshData, this);
                    meshData.RenderPassesAndExtraBits.Fill((short)2);
                }
                catch (Exception e)
                {
                    Api.World.Logger.Error("Failed to tessellate item {0}: {1}", stack.Item.Code, e.Message);
                    meshData = null!;
                }
                capi!.TesselatorManager.ThreadDispose();
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
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] != null) mesher.AddMeshData(meshes[i]);
        }
        return false;
    }

    /// <summary>
    /// Обновляет меши для всех слотов
    /// </summary>
    public void UpdateMeshes()
    {
        for (int i = 0; i < inventory.Count - 1; i++)
        {
            UpdateMesh(i);
        }
        MarkDirty(true);
    }

    private void OnSlotModifid(int slotid)
    {
        Block = Api.World.BlockAccessor.GetBlock(Pos);
        MarkDirty(Api.Side == EnumAppSide.Server);
        if (Api is ICoreClientAPI && clientDialog != null)
            SetDialogValues(clientDialog.Attributes);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();
    }

    public bool IsBurning;

    public int getInventoryStackLimit() => 64;

    private void OnBurnTick(float dt)
    {
        if (Api is ICoreClientAPI) return;
        if (IsBurning)
        {
            stoveTemperature = changeTemperature(stoveTemperature, GetBehavior<BEBehaviorEStove>().PowerSetting * 1.0F / maxConsumption * maxTemperature, dt);
        }
        if (canHeatInput())
            heatInput(dt);
        else
            inputStackCookingTime = 0;
        if (canHeatOutput())
            heatOutput(dt);
        if (canSmeltInput() && inputStackCookingTime > maxCookingTime())
            smeltItems();

        if (GetBehavior<BEBehaviorEStove>()?.PowerSetting > 0)
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
        if (!IsBurning) stoveTemperature = changeTemperature(stoveTemperature, enviromentTemperature(), dt);
    }

    private void On500msTick(float dt)
    {
        if (Api is ICoreServerAPI && (IsBurning || prevStoveTemperature != stoveTemperature))
            MarkDirty();

        prevStoveTemperature = stoveTemperature;
    }

    public float changeTemperature(float fromTemp, float toTemp, float dt)
    {
        float diff = Math.Abs(fromTemp - toTemp);
        dt = dt + dt * (diff / 28);
        if (diff < dt) return toTemp;
        if (fromTemp > toTemp) dt = -dt;
        if (Math.Abs(fromTemp - toTemp) < 1) return toTemp;
        return fromTemp + dt;
    }

    public void heatInput(float dt)
    {
        float oldTemp = InputStackTemp, nowTemp = oldTemp;
        float meltingPoint = inputSlot.Itemstack.Collectible.GetMeltingPoint(Api.World, inventory, inputSlot);
        if (oldTemp < stoveTemperature)
        {
            float f = (1 + GameMath.Clamp((stoveTemperature - oldTemp) / 30, 0, 1.6f)) * dt;
            if (nowTemp >= meltingPoint) f /= 11;
            float newTemp = changeTemperature(oldTemp, stoveTemperature, f);
            int maxTemp = 0;
            if (inputStack.ItemAttributes != null)
            {
                maxTemp = Math.Max(inputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0, inputStack.ItemAttributes["maxTemperature"]?.AsInt() ?? 0);
            }
            else
            {
                maxTemp = inputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0;
            }
            if (maxTemp > 0) newTemp = Math.Min(maxTemp, newTemp);
            if (oldTemp != newTemp)
            {
                InputStackTemp = newTemp;
                nowTemp = newTemp;
            }
        }
        if (nowTemp >= meltingPoint)
        {
            float diff = nowTemp / meltingPoint;
            inputStackCookingTime += GameMath.Clamp((int)(diff), 1, 30) * dt;
        }
        else if (inputStackCookingTime > 0) inputStackCookingTime--;
    }

    public void heatOutput(float dt)
    {
        float oldTemp = OutputStackTemp;
        if (oldTemp < stoveTemperature)
        {
            float newTemp = changeTemperature(oldTemp, stoveTemperature, 2 * dt);
            int maxTemp = Math.Max(outputStack.Collectible.CombustibleProps?.MaxTemperature ?? 0, outputStack.ItemAttributes["maxTemperature"]?.AsInt() ?? 0);
            if (maxTemp > 0) newTemp = Math.Min(maxTemp, newTemp);
            if (oldTemp != newTemp) OutputStackTemp = newTemp;
        }
    }

    public float InputStackTemp
    {
        get => GetTemp(inputStack);
        set => SetTemp(inputStack, value);
    }

    public float OutputStackTemp
    {
        get => GetTemp(outputStack);
        set => SetTemp(outputStack, value);
    }

    float GetTemp(ItemStack stack)
    {
        if (stack == null) return enviromentTemperature();
        if (inventory.CookingSlots.Length > 0)
        {
            bool haveStack = false;
            float lowestTemp = 0;
            for (int i = 0; i < inventory.CookingSlots.Length; i++)
            {
                ItemStack cookingStack = inventory.CookingSlots[i].Itemstack;
                if (cookingStack != null)
                {
                    float stackTemp = cookingStack.Collectible.GetTemperature(Api.World, cookingStack);
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
            for (int i = 0; i < inventory.CookingSlots.Length; i++)
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

    public bool canHeatInput()
    {
        return canSmeltInput() || (inputStack != null && inputStack.ItemAttributes?["allowHeating"]?.AsBool() == true);
    }

    public bool canHeatOutput()
    {
        return outputStack?.ItemAttributes?["allowHeating"]?.AsBool() == true;
    }

    public bool canSmeltInput()
    {
        return inputStack != null && inputStack.Collectible.CanSmelt(Api.World, inventory, inputSlot.Itemstack, outputSlot.Itemstack) &&
               (inputStack.Collectible.CombustibleProps == null || !inputStack.Collectible.CombustibleProps.RequiresContainer);
    }

    public void smeltItems()
    {
        inputStack.Collectible.DoSmelt(Api.World, inventory, inputSlot, outputSlot);
        InputStackTemp = enviromentTemperature();
        inputStackCookingTime = 0;
        MarkDirty(true);
        inputSlot.MarkDirty();
    }

    public void OnBlockInteract(IPlayer byPlayer, bool isOwner, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            return;
        byte[] data;
        using (MemoryStream ms = new MemoryStream())
        {
            BinaryWriter writer = new BinaryWriter(ms);
            writer.Write("BlockEntityStove");
            writer.Write(DialogTitle);
            TreeAttribute tree = new TreeAttribute();
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
        stoveTemperature = tree.GetFloat("stoveTemperature");
        inputStackCookingTime = tree.GetFloat("oreCookingTime");
        if (Api != null)
        {
            if (Api.Side == EnumAppSide.Client && clientDialog != null) SetDialogValues(clientDialog.Attributes);
            if (Api.Side == EnumAppSide.Client && clientSidePrevBurning != IsBurning)
            {
                clientSidePrevBurning = IsBurning;
                MarkDirty(true);
            }
            inventory.AfterBlocksLoaded(Api.World);
            if (Api.Side == EnumAppSide.Client) UpdateMeshes();
        }
    }

    void SetDialogValues(ITreeAttribute dialogTree)
    {
        dialogTree.SetFloat("stoveTemperature", stoveTemperature);
        dialogTree.SetFloat("oreCookingTime", inputStackCookingTime);
        if (inputSlot.Itemstack != null)
        {
            float meltingDuration = inputSlot.Itemstack.Collectible.GetMeltingDuration(Api.World, inventory, inputSlot);
            dialogTree.SetFloat("oreTemperature", InputStackTemp);
            dialogTree.SetFloat("maxOreCookingTime", meltingDuration);
        }
        else dialogTree.RemoveAttribute("oreTemperature");
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
        tree.SetFloat("stoveTemperature", stoveTemperature);
        tree.SetFloat("oreCookingTime", inputStackCookingTime);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        var electricity = ElectricalProgressive;
        if (electricity == null || byItemStack == null)
            return;
        electricity.Connection = Facing.DownAll;
        var voltage = MyMiniLib.GetAttributeInt(byItemStack!.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack!.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

        this.ElectricalProgressive!.Eparams =
            (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }

    /// <summary>
    /// Вызывается при удалении блока
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (clientDialog != null)
        {
            clientDialog?.TryClose();
            clientDialog?.Dispose();
            clientDialog = null!;
        }

        // Освобождение ссылок на API
        capi = null;
        sapi = null;

        // Очистка ссылок на меши
        if (meshes != null)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = null!;
            }
        }
        nowTesselatingObj = null!;
        nowTesselatingShape = null!;
    }


    /// <summary>
    /// Вызывается при выгрузке блока
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive

        if (clientDialog != null)
        {
            clientDialog?.TryClose();
            clientDialog?.Dispose();
            clientDialog = null!;
        }

        // Отменяем слушателей тика игры
        UnregisterGameTickListener(listenerId);
        UnregisterGameTickListener(listenerId2);


        // Освобождение ссылок на API
        capi = null!;
        sapi = null!;

        // Очистка ссылок на меши
        if (meshes != null)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = null!;
            }
        }
        nowTesselatingObj = null!;
        nowTesselatingShape = null!;
    }

    /// <summary>
    /// Вызывается при разрушении блока
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (inputStack != null)
            Api.World.SpawnItemEntity(inputStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));


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
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader reader = new BinaryReader(ms);
                string dialogClassName = reader.ReadString();
                string dialogTitle = reader.ReadString();
                TreeAttribute tree = new TreeAttribute();
                tree.FromBytes(reader);
                Inventory.FromTreeAttributes(tree);
                Inventory.ResolveBlocksOrItems();
                IClientWorldAccessor clientWorld = (IClientWorldAccessor)Api.World;
                SyncedTreeAttribute dtree = new SyncedTreeAttribute();
                SetDialogValues(dtree);
                if (clientDialog != null)
                {
                    clientDialog.TryClose();
                    clientDialog = null!;
                }
                else
                {
                    clientDialog = new GuiDialogBlockEntityEStove(dialogTitle, Inventory, Pos, dtree, capi!);
                    clientDialog.OnClosed += () => { clientDialog.Dispose(); clientDialog = null!; };
                    clientDialog.TryOpen();
                }
            }
        }
        if (packetid == (int)EnumBlockEntityPacketId.Close)
        {
            ((IClientWorldAccessor)Api.World).Player.InventoryManager.CloseInventory(Inventory);
        }
    }

    public ItemSlot inputSlot => inventory[1];
    public ItemSlot outputSlot => inventory[2];
    public ItemSlot[] otherCookingSlots => inventory.CookingSlots;
    public ItemStack inputStack
    {
        get => inventory[1].Itemstack;
        set { inventory[1].Itemstack = value; inventory[1].MarkDirty(); }
    }
    public ItemStack outputStack
    {
        get => inventory[2].Itemstack;
        set { inventory[2].Itemstack = value; inventory[2].MarkDirty(); }
    }

    public CombustibleProperties fuelCombustibleOpts => getCombustibleOpts(0);
    public CombustibleProperties getCombustibleOpts(int slotid)
    {
        ItemSlot slot = inventory[slotid];
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
        foreach (ItemSlot slot in inventory.CookingSlots)
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
        foreach (ItemSlot slot in inventory.CookingSlots)
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



        if (inputStack != null)
        {
            var temp = (int)inputStack.Collectible.GetTemperature(Api.World, inputStack);
            stringBuilder.AppendLine();
            if (temp <= 25)
                stringBuilder.AppendLine(Lang.Get("Contents") + " " + inputStack.StackSize + "×" + inputStack.GetName() + "\n└ " + Lang.Get("Temperature") + " " + Lang.Get("Cold"));
            else
                stringBuilder.AppendLine(Lang.Get("Contents") + " " + inputStack.StackSize + "×" + inputStack.GetName() + "\n└ " + Lang.Get("Temperature") + " " + temp + " °C");
        }
    }
}