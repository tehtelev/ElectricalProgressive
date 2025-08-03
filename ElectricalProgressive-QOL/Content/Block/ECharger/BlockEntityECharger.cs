using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Facing = ElectricalProgressive.Utils.Facing;

namespace ElectricalProgressive.Content.Block.ECharger;

public class BlockEntityECharger : BlockEntityEBase, ITexPositionSource
{
    public InventoryGeneric Inventory { get; private set; }

    MeshData[] toolMeshes = new MeshData[1];

    public Size2i AtlasSize => ((ICoreClientAPI)Api).BlockTextureAtlas.Size;

    CollectibleObject? tmpItem;

    private long listenerId;



    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            if (BlockECharger.ToolTextureSubIds(Api).TryGetValue((Item)tmpItem!, out var toolTextures))
            {
                if (toolTextures.TextureSubIdsByCode.TryGetValue(textureCode, out var textureSubId))
                    return ((ICoreClientAPI)Api).BlockTextureAtlas.Positions[textureSubId];

                return ((ICoreClientAPI)Api).BlockTextureAtlas.Positions[toolTextures.TextureSubIdsByCode.First().Value];
            }

            return null!;
        }
    }

    /// <summary>
    /// Конструктор блока-зарядника
    /// </summary>
    public BlockEntityECharger()
    {
        Inventory = new(1, "charger", null, null, null);
    }


    /// <summary>
    /// Инициализация блока-зарядника
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Inventory.LateInitialize("charger-" + Pos, api);
        Inventory.ResolveBlocksOrItems();

        if (api is ICoreClientAPI)
        {
            loadToolMeshes();
        }
        else
        {
            listenerId=RegisterGameTickListener(OnTick, 500);
        }
    }


    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();


        // Очистка мусора
        toolMeshes[0] = null!;
        tmpItem = null!;

        UnregisterGameTickListener(listenerId); //отменяем слушатель тика, если он есть
    }



    //проверка, нужно ли заряжать
    private void OnTick(float dt)
    {
        if (this.Block.Variant["state"] == "burned") //если прибор сгорел, то нечего тут делать
            return;

        var stack = Inventory[0]?.Itemstack;


        if (stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false))
        {
            var durability = stack.Attributes.GetInt("durability");             //текущая прочность
            var maxDurability = stack.Collectible.GetMaxDurability(stack);       //максимальная прочность

            if (durability < maxDurability && GetBehavior<BEBehaviorECharger>().PowerSetting > 0)
            {
                if (this.Block.Variant["state"] == "disabled")     //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                    MarkDirty(true);
                }


                int maxReceive = GetBehavior<BEBehaviorECharger>().PowerSetting;        // мощность в заряднике
                int consume = MyMiniLib.GetAttributeInt(stack.Item, "consume", 20);     //размер минимальной порции          
                int received = Math.Min(maxDurability - durability, maxReceive / consume); // приращение прочности
                durability += received;                                                 // новая прочность
                stack.Attributes.SetInt("durability", durability);                      // обновляем прочность в атрибутах
            }
            else
            {
                if (this.Block.Variant["state"] == "enabled")   //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                    Api.World.PlaySoundAt(new("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F); //звоним если зарядилось таки
                    MarkDirty(true);
                }
            }
        }
        else if (stack?.Block is IEnergyStorageItem)
        {
            var durability = stack.Attributes.GetInt("durability");             //текущая прочность
            var maxDurability = stack.Collectible.GetMaxDurability(stack);       //максимальная прочность

            if (durability < maxDurability && GetBehavior<BEBehaviorECharger>().PowerSetting > 0)
            {
                if (this.Block.Variant["state"] == "disabled")     //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                    MarkDirty(true);
                }

                ((IEnergyStorageItem)stack.Block).receiveEnergy(stack, GetBehavior<BEBehaviorECharger>().PowerSetting);
            }
            else
            {
                if (this.Block.Variant["state"] == "enabled")   //чтобы лишний раз не обновлять модель
                {
                    Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                    Api.World.PlaySoundAt(new("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F); //звоним если зарядилось таки
                    MarkDirty(true);
                }
            }
        }

        MarkDirty();
    }


    /// <summary>
    /// Загружает меши инструментов 
    /// </summary>
    void loadToolMeshes()
    {
        toolMeshes[0] = null!; //должна быть сброшена сразу же 

        var stack = Inventory[0].Itemstack;
        if (stack == null) //пустой стак нам не интересен
            return;

        tmpItem = stack.Collectible;

        var origin = new Vec3f(0.5f, 0.5f, 0.5f);
        var clientApi = (ICoreClientAPI)Api;

        if (stack.Class == EnumItemClass.Item)
            clientApi.Tesselator.TesselateItem(stack.Item, out toolMeshes[0], this);
        else
            clientApi.Tesselator.TesselateBlock(stack.Block, out toolMeshes[0]);

        clientApi.TesselatorManager.ThreadDispose(); //обязательно

        if (stack.Class == EnumItemClass.Item)
        {
            var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.5F);
            var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.5F);
            var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.5F);
            var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
            var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0.4F);
            var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
            var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
            var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
            var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

            origin.Y = 1f / 30f;
            toolMeshes[0].Scale(origin, scaleX, scaleY, scaleZ);
            toolMeshes[0].Translate(translateX, translateY, translateZ);
            toolMeshes[0].Rotate(origin, rotateX, rotateY, rotateZ);
        }
        else
        {
            toolMeshes[0].Scale(origin, 0.3f, 0.3f, 0.3f);
        }
    }

    internal bool OnPlayerInteract(IPlayer byPlayer, Vec3d hit)
    {
        if (Inventory[0].Itemstack != null)
        {
            return TakeFromSlot(byPlayer, 0);
        }

        return PutInSlot(byPlayer, 0);
    }

    bool PutInSlot(IPlayer player, int slot)
    {
        var stack = player.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (stack == null)
            return false;

        var isValid = stack.Class == EnumItemClass.Block
            ? stack.Block is IEnergyStorageItem
            : stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false);
        if (!isValid)
            return false;

        player.InventoryManager.ActiveHotbarSlot.TryPutInto(Api.World, Inventory[slot]);

        didInteract(player);

        return true;
    }

    /// <summary>
    /// Извлекает предмет из слота инвентаря
    /// </summary>
    /// <param name="player"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    bool TakeFromSlot(IPlayer player, int slot)
    {
        var stack = Inventory[slot].TakeOutWhole();

        if (!player.InventoryManager.TryGiveItemstack(stack))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        // обновляем модель блока
        if (this.Block.Variant["state"] == "enabled") 
            Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
        else if (this.Block.Variant["state"] == "burned")
            Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "burned")).BlockId, Pos);


        didInteract(player);

        return true;
    }

    void didInteract(IPlayer player)
    {
        Api.World.PlaySoundAt(new AssetLocation("sounds/player/buildhigh"), Pos.X, Pos.Y, Pos.Z, player, false);
        if (Api is ICoreClientAPI)
            loadToolMeshes();

        MarkDirty(true);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        var electricity = ElectricalProgressive;

        if (electricity == null || byItemStack == null)
            return;

        electricity.Connection = Facing.DownAll;

        //задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(byItemStack!.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack!.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

        electricity.Eparams = (
            new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }

    /// <summary>
    /// Вызывается при удалении блока из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
    }


    /// <summary>
    /// Вызывается при разрушении блока игроком
    /// </summary>
    /// <param name="byPlayer"></param>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        var stack = Inventory[0].Itemstack;
        if (stack != null)
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }


    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var clientApi = (ICoreClientAPI)Api;
        var block = Api.World.BlockAccessor.GetBlock(Pos);
        var mesh = clientApi.TesselatorManager.GetDefaultBlockMesh(block);

        if (mesh == null || mesher==null)
            return true;

        mesher.AddMeshData(mesh);

        if (toolMeshes[0] != null)
            mesher.AddMeshData(toolMeshes[0]);

        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        
        Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
        {
            Inventory.Api = Api;
            Inventory.ResolveBlocksOrItems();
        }

        if (Api is ICoreClientAPI)
        {
            loadToolMeshes();
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invtree = new TreeAttribute();
        Inventory.ToTreeAttributes(invtree);
        tree["inventory"] = invtree;
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        foreach (var slot in Inventory)
        {
            if (slot.Itemstack == null)
                continue;

            if (slot.Itemstack.Class == EnumItemClass.Item)
            {
                itemIdMapping[slot.Itemstack.Item.Id] = slot.Itemstack.Item.Code;
            }
            else
            {
                blockIdMapping[slot.Itemstack.Block.BlockId] = slot.Itemstack.Block.Code;
            }
        }
    }


    public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        foreach (var slot in Inventory)
        {
            if (slot.Itemstack == null)
                continue;

            if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForNewMappings))
            {
                slot.Itemstack = null;
            }
        }
    }



    /// <summary>
    /// Информация о блоке
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="stringBuilder"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        var stack = Inventory[0]?.Itemstack; //стак инвентаря

        if (stack?.Item != null && stack.Collectible.Attributes["chargable"].AsBool(false)) //предмет
        {
            int consume = MyMiniLib.GetAttributeInt(stack.Item, "consume", 20); //количество энергии, которое потребляет блок порцией
            int energy = stack.Attributes.GetInt("durability") * consume;
            int maxEnergy = stack.Collectible.GetMaxDurability(stack) * consume;

            stringBuilder.AppendLine();
            stringBuilder.AppendLine(stack.GetName());
            stringBuilder.AppendLine(StringHelper.Progressbar(energy * 100.0F / maxEnergy));
            stringBuilder.AppendLine("└ " + Lang.Get("Storage") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("J"));
        }
        else if (stack?.Block is IEnergyStorageItem) //блок
        {

            int consume = MyMiniLib.GetAttributeInt(stack.Block, "consume", 20); //количество энергии, которое потребляет блок порцией
            int energy = stack.Attributes.GetInt("durability") * consume;
            int maxEnergy = stack.Collectible.GetMaxDurability(stack) * consume;

            stringBuilder.AppendLine();
            stringBuilder.AppendLine(stack.GetName());
            stringBuilder.AppendLine(StringHelper.Progressbar(energy * 100.0F / maxEnergy));
            stringBuilder.AppendLine("└ " + Lang.Get("Storage") + ": " + energy + "/" + maxEnergy + " " + Lang.Get("J"));
        }
    }
}

/// <summary>
/// Класс для хранения текстур инструментов
/// </summary>
public class ToolTextures
{
    public Dictionary<string, int> TextureSubIdsByCode = new Dictionary<string, int>();
}