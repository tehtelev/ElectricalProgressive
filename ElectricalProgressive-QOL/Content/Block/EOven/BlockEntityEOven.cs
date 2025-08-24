using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EOven;
public class    BlockEntityEOven : BlockEntityDisplay, IHeatSource
{
    public static readonly int BakingStageThreshold = 100;
    public static readonly int maxBakingTemperatureAccepted = 260;


    private bool burning;
    private bool clientSidePrevBurning;
    public float prevOvenTemperature = 20f;
    public float ovenTemperature = 20f;
    public readonly OvenItemData[] bakingData;
    private ItemStack lastRemoved;
    private int rotationDeg;


    internal InventoryEOven ovenInv;

    private int _maxConsumption;

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


    public virtual float maxTemperature => 300f;

    public virtual int bakeableCapacity => 4;

    private BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


    public EnumOvenContentMode OvenContentMode  //как отображать содержимое
    {
        get
        {
            ItemSlot firstNonEmptySlot = this.ovenInv.FirstNonEmptySlot;
            if (firstNonEmptySlot == null)
                return EnumOvenContentMode.Quadrants;

            BakingProperties bakingProperties = BakingProperties.ReadFrom(firstNonEmptySlot.Itemstack);

            if (bakingProperties == null)    //протухло
                return EnumOvenContentMode.Quadrants;
            else
                return !bakingProperties.LargeItem ? EnumOvenContentMode.Quadrants : EnumOvenContentMode.SingleCenter;

        }
    }

    public BlockEntityEOven()
    {
        this.bakingData = new OvenItemData[this.bakeableCapacity];
        for (int index = 0; index < this.bakeableCapacity; ++index)
            this.bakingData[index] = new OvenItemData();
        this.ovenInv = new InventoryEOven("eoven-0", this.bakeableCapacity);
    }

    public override InventoryBase Inventory => (InventoryBase)this.ovenInv;

    public override string InventoryClassName => "eoven";


    public bool IsBurning;

    private long listenerId;

    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        this.capi = api as ICoreClientAPI;
        base.Initialize(api);
        this.ovenInv.LateInitialize(this.InventoryClassName + "-" + this.Pos?.ToString(), api);
        listenerId=this.RegisterGameTickListener(new Action<float>(this.OnBurnTick), 100);

        this.SetRotation();
        
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }


    /// <summary>
    /// Устанавливает поворот духовки в зависимости от ее стороны
    /// </summary>
    private void SetRotation()
    {
        this.rotationDeg = this.Block.Variant["side"] switch
        {
            "south" => 270,
            "west" => 180,
            "east" => 0,
            _ => 90
        };

    }





// ...

/// <summary>
/// Возвращает экземпляр поведения указанного типа, безопасно (через MakeGenericMethod или обход списка behaviors).
/// Возвращает null, если поведение не найдено.
/// </summary>
private object GetBehaviorByType(Type behaviorType)
{
    if (behaviorType == null) return null;

    try
    {
        // 1) Попытка вызвать обобщённый GetBehavior<T>() через рефлексию
        MethodInfo genericGetBehavior = null;
        var methods = this.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var m in methods)
        {
            if (m.Name == "GetBehavior" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
            {
                genericGetBehavior = m;
                break;
            }
        }

        if (genericGetBehavior != null)
        {
            var gen = genericGetBehavior.MakeGenericMethod(behaviorType);
            return gen.Invoke(this, null);
        }

        // 2) Fallback: пытаемся найти приватное поле/свойство, которое содержит массив/коллекцию behaviors
        // Возможные имена полей/свойств (разные версии API)
        string[] candidateFieldNames = new[] { "blockEntityBehaviors", "behaviors", "BlockEntityBehaviors", "Behaviors" };

        foreach (var name in candidateFieldNames)
        {
            var fld = this.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fld != null)
            {
                var col = fld.GetValue(this) as System.Collections.IEnumerable;
                if (col != null)
                {
                    foreach (var b in col)
                    {
                        if (b != null && behaviorType.IsAssignableFrom(b.GetType())) return b;
                    }
                }
            }

            var prop = this.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop != null)
            {
                var col = prop.GetValue(this) as System.Collections.IEnumerable;
                if (col != null)
                {
                    foreach (var b in col)
                    {
                        if (b != null && behaviorType.IsAssignableFrom(b.GetType())) return b;
                    }
                }
            }
        }
    }
    catch
    {
        // ничего не делаем — вернём null внизу
    }

    return null;
}




    /// <summary>
    /// Обработка взаимодействия с духовкой
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="bs"></param>
    /// <returns></returns>
    public virtual bool OnInteract(IPlayer byPlayer, BlockSelection bs)
    {
        ItemSlot activeHotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (activeHotbarSlot.Empty) // если слот пустой - пробуем брать
        {
            if (!this.TryTake(byPlayer))
                return false;

            // назначаем владельца при успешном взятии (через рефлексию)
            if (ElectricalProgressiveQOL.xskillsEnabled && ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable != null)
            {
                var ownable = GetBehaviorByType(ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable);
                var ownerProp = ownable?.GetType().GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ownerProp != null && ownerProp.CanWrite) ownerProp.SetValue(ownable, byPlayer);
            }

            byPlayer.InventoryManager.BroadcastHotbarSlot();
            return true;
        }

        CollectibleObject collectible = activeHotbarSlot.Itemstack.Collectible;

        if (activeHotbarSlot.Itemstack.Equals(this.Api.World, this.lastRemoved, GlobalConstants.IgnoredStackAttributes) && !this.ovenInv[0].Empty)
        {
            if (this.TryTake(byPlayer))
            {
                // назначаем владельца при успешном взятии
                if (ElectricalProgressiveQOL.xskillsEnabled && ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable != null)
                {
                    var ownableTake2 = GetBehaviorByType(ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable);
                    var ownerProp2 = ownableTake2?.GetType().GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ownerProp2 != null && ownerProp2.CanWrite) ownerProp2.SetValue(ownableTake2, byPlayer);
                }

                byPlayer.InventoryManager.BroadcastHotbarSlot();
                return true;
            }
        }
        else
        {
            if (this.TryPut(activeHotbarSlot))
            {
                // назначаем владельца при успешной установке
                if (ElectricalProgressiveQOL.xskillsEnabled && ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable != null)
                {
                    var ownablePut = GetBehaviorByType(ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable);
                    var ownerProp3 = ownablePut?.GetType().GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ownerProp3 != null && ownerProp3.CanWrite) ownerProp3.SetValue(ownablePut, byPlayer);
                }

                AssetLocation place = activeHotbarSlot.Itemstack?.Block?.Sounds?.Place!;
                this.Api.World.PlaySoundAt(place != null ? place : new AssetLocation("sounds/player/buildhigh"), (Entity)byPlayer.Entity, byPlayer, true, 16f, 1f);
                byPlayer.InventoryManager.BroadcastHotbarSlot();
                return true;
            }

            if (this.Api is ICoreClientAPI api) // уведомления об ошибках
            {
                if (activeHotbarSlot.Empty) // если слот пустой
                {
                    api.TriggerIngameError((object)this, "notbakeable", Lang.Get("Put-into-1-items"));
                    return true;
                }
                else
                {
                    BakingProperties bakingProperties1 = BakingProperties.ReadFrom(activeHotbarSlot.Itemstack);
                    if (bakingProperties1 == null) // если свойства выпекания не найдены
                    {
                        api.TriggerIngameError((object)this, "notbakeable", Lang.Get("This item is not bakeable."));
                        return true;
                    }

                    if (!activeHotbarSlot.Itemstack.Attributes.GetBool("bakeable", true)) // если аттрибут есть выпекания
                    {
                        api.TriggerIngameError((object)this, "notbakeable", Lang.Get("This item is not bakeable."));
                        return true;
                    }

                    if (activeHotbarSlot.Itemstack?.StackSize < 1 & !bakingProperties1.LargeItem) // если айтемы в стаке меньше 1 
                    {
                        api.TriggerIngameError((object)this, "notbakeable", Lang.Get("Put-into-1-items"));
                        return true;
                    }
                }
            }
        }
        return false;
    }





    /// <summary>
    /// Проверяет валидность предмета для помещения в духовку
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="inv"></param>
    /// <returns></returns>
    public static bool IsValidInput(ItemSlot slot, InventoryEOven inv)
    {
        BakingProperties bakingProperties1 = BakingProperties.ReadFrom(slot.Itemstack);
        if (bakingProperties1 == null || !slot.Itemstack.Attributes.GetBool("bakeable", true)) //если свойства выпекания не найдены
            return false;

        if (!inv[0].Empty) //если в духовке уже что-то лежит в первом слоте
        {
            BakingProperties bakingProperties2 = BakingProperties.ReadFrom(slot.Itemstack);
            if (bakingProperties2!=null && bakingProperties2.LargeItem)  //если уже лежит большое - выход
                return false;

            if (bakingProperties1.LargeItem) //если пытаемся положить большое в духовку, где уже что-то лежит
                return false;
        }


        if (slot.Itemstack.StackSize < 1)   //если айтемы в стаке меньше 1 - выход
            return false;

        return true;
    }



    /// <summary>
    /// Пробуем положить предмет в духовку
    /// </summary>
    /// <param name="slot"></param>
    /// <returns></returns>
    protected virtual bool TryPut(ItemSlot slot)
    {
        // проверка валидности предмета
        if (!IsValidInput(slot, ovenInv))
            return false;

        for (int index = 0; index < this.bakeableCapacity; ++index)
        {
            if (this.ovenInv[index].Empty)
            {
                int num = slot.TryPutInto(this.Api.World, this.ovenInv[index]);
                if (num > 0)
                {
                    this.bakingData[index] = new OvenItemData(this.ovenInv[index].Itemstack);
                    this.updateMesh(index);
                    this.MarkDirty(true);
                    this.lastRemoved = null!;
                }

            }
            if (index == 0)
            {
                BakingProperties bakingProperties2 = BakingProperties.ReadFrom(this.ovenInv[0].Itemstack);
                if (bakingProperties2 != null && bakingProperties2.LargeItem)            //если уже лежит пирог - выход
                {
                    break;
                }
            }
        }
        return true;
    }

    protected virtual bool TryTake(IPlayer byPlayer)
    {
        for (int bakeableCapacity = this.bakeableCapacity; bakeableCapacity >= 0; --bakeableCapacity)
        {
            if (!this.ovenInv[bakeableCapacity].Empty)
            {
                ItemStack itemstack = this.ovenInv[bakeableCapacity].TakeOut(1);
                this.lastRemoved = itemstack == null ? null! : itemstack.Clone();
                if (byPlayer.InventoryManager.TryGiveItemstack(itemstack))
                {
                    AssetLocation place = itemstack?.Block?.Sounds?.Place!;
                    this.Api.World.PlaySoundAt(place != null ? place : new AssetLocation("sounds/player/throw"), (Entity)byPlayer.Entity, byPlayer, true, 16f, 1f);
                }
                if (itemstack?.StackSize > 0)
                    this.Api.World.SpawnItemEntity(itemstack, this.Pos);
                //this.Api.World.Logger.Audit("{0} Took 1x{1} from Clay oven at {2}.", (object)byPlayer.PlayerName, (object)itemstack.Collectible.Code, (object)this.Pos);
                this.bakingData[bakeableCapacity].CurHeightMul = 1f;
                this.bakingData[bakeableCapacity].temp = 20;
                this.updateMesh(bakeableCapacity);
                this.MarkDirty(true);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Отвечает за тепло отдаваемое в окружающую среду
    /// </summary>
    /// <param name="world"></param>
    /// <param name="heatSourcePos"></param>
    /// <param name="heatReceiverPos"></param>
    /// <returns></returns>
    public float GetHeatStrength(
      IWorldAccessor world,
      BlockPos heatSourcePos,
      BlockPos heatReceiverPos)
    {
        return Math.Max((float)(((double)this.ovenTemperature - 20.0) / ((double)this.maxTemperature - 20.0) * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)), 0.0f);
    }

    /// <summary>
    /// Вызывается при каждом тике игры для обработки горения духовки
    /// </summary>
    /// <param name="dt"></param>
    protected virtual void OnBurnTick(float dt)
    {
        dt *= 1.0f;
        if (this.Api is ICoreClientAPI)
            return;

        var ovenBehavior = GetBehavior<BEBehaviorEOven>();

        if (ovenBehavior == null) // если поведение не найдено - выходим, хотя этого быть не должно
            return;

        if (!ovenBehavior.IsBurned && ovenBehavior.PowerSetting > 0)
        {

            if (!IsBurning)
            {
                IsBurning = true;

                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                MarkDirty(true);
            }
        }
        else
        {
            if (ovenBehavior.IsBurned)
            {
                IsBurning = false;
            }

            if (IsBurning)                     //готовка закончилась
            {
                IsBurning = false;

                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                MarkDirty(true);
                if (!ovenInv.Empty)
                    Api.World.PlaySoundAt(new AssetLocation("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            }
        }




        if (!ovenInv.Empty)   //если не пусто
        {

            int EnvTemp = this.EnvironmentTemperature();

            if (this.IsBurning)
            {
                //чем больше энергии тем выше будет максимальная достижимая температура
                int power = ovenBehavior.PowerSetting;
                float toTemp = Math.Max(EnvTemp, power * maxBakingTemperatureAccepted / _maxConsumption);
                this.ovenTemperature = this.ChangeTemperature(this.ovenTemperature, toTemp, dt * 1.5F);

            }
            else
            {
                this.ovenTemperature = ChangeTemperature(ovenTemperature, EnvironmentTemperature(), dt); //выравниваем температуру с окружающей средой

            }


            if (this.ovenTemperature > EnvTemp)  //греем и охлаждаем еду
            {
                this.HeatInput(dt * 1.2f, this.IsBurning);
            }
        }

        else
        {
            this.ovenTemperature = ChangeTemperature(ovenTemperature, EnvironmentTemperature(), dt); //выравниваем температуру с окружающей средой
        }



        //if (++this.syncCount % 5 != 0 || !this.IsBurning && (double) this.prevOvenTemperature == (double) this.ovenTemperature && this.Inventory[0].Empty && this.Inventory[1].Empty && this.Inventory[2].Empty && this.Inventory[3].Empty)
        // return;
        this.MarkDirty();
        this.prevOvenTemperature = this.ovenTemperature;
    }

    /// <summary>
    /// греем содержимое всей печи
    /// </summary>
    /// <param name="dt"></param>
    /// <param name="Up"></param>
    protected virtual void HeatInput(float dt, bool Up)
    {
        for (int index = 0; index < this.bakeableCapacity; ++index)
        {
            ItemStack itemstack = this.ovenInv[index].Itemstack;
            if (itemstack != null && (double)this.HeatStack(itemstack, dt, index, Up) >= 100.0)
                if (Up)                             //если еда остывает, то не выпекаем и активно снижаем температуру в HeatStack
                    this.IncrementallyBake(dt, index);
        }
    }

    /// <summary>
    /// греем конкретно один предмет
    /// </summary>
    /// <param name="stack"></param>
    /// <param name="dt"></param>
    /// <param name="i"></param>
    /// <param name="Up"></param>
    /// <returns></returns>
    protected virtual float HeatStack(ItemStack stack, float dt, int i, bool Up)
    {
        float temp = this.bakingData[i].temp;
        float val2_1 = temp;
        float targetTemp = Up                   //при нагревании тянемся к печи, при остывании к окржающей среде
            ? this.ovenTemperature
            : this.EnvironmentTemperature();

        if ((double)temp < (double)targetTemp)
        {
            float dt1 = (1f + GameMath.Clamp((float)(((double)targetTemp - (double)temp) / 28.0), 0.0f, 1.6f)) * dt;
            val2_1 = this.ChangeTemperature(temp, targetTemp, dt1);
            CombustibleProperties combustibleProps = stack.Collectible.CombustibleProps;
            int maxTemperature = combustibleProps != null ? combustibleProps.MaxTemperature : 0;
            JsonObject itemAttributes = stack.ItemAttributes;
            int val2_2 = itemAttributes != null ? itemAttributes["maxTemperature"].AsInt() : 0;
            int val1 = Math.Max(maxTemperature, val2_2);
            if (val1 > 0)
                val2_1 = Math.Min((float)val1, val2_1);
        }
        else if ((double)temp > (double)targetTemp)
        {
            float dt2 = (1f + GameMath.Clamp((float)(((double)temp - (double)targetTemp) / 28.0), 0.0f, 1.6f)) * dt;
            val2_1 = this.ChangeTemperature(temp, targetTemp, dt2);
        }
        if ((double)temp != (double)val2_1)
            this.bakingData[i].temp = val2_1;
        return val2_1;
    }

    protected virtual void IncrementallyBake(float dt, int slotIndex)
    {
        ItemSlot itemSlot = this.Inventory[slotIndex];
        OvenItemData ovenItemData = this.bakingData[slotIndex];
        ItemStack prevStack = this.ovenInv[slotIndex].Itemstack?.Clone();

        float num1 = ovenItemData.BrowningPoint == 0 ? 160f : ovenItemData.BrowningPoint;
        double val = ovenItemData.temp / num1;
        float num2 = ovenItemData.TimeToBake == 0 ? 1f : ovenItemData.TimeToBake;
        float num3 = (float)GameMath.Clamp((int)val, 1, 30) * dt / num2;

        if (ovenItemData.temp > num1)
            ovenItemData.BakedLevel += num3;

        BakingProperties bakingProperties = BakingProperties.ReadFrom(itemSlot.Itemstack);
        float num5 = bakingProperties?.LevelFrom ?? 0f;
        float num6 = bakingProperties?.LevelTo ?? 1f;
        float num7 = (float)(int)(GameMath.Mix(bakingProperties?.StartScaleY ?? 1f, bakingProperties?.EndScaleY ?? 1f,
            GameMath.Clamp((ovenItemData.BakedLevel - num5) / (num6 - num5), 0f, 1f)) * BlockEntityOven.BakingStageThreshold) / BlockEntityOven.BakingStageThreshold;

        bool flag = num7 != ovenItemData.CurHeightMul;
        ovenItemData.CurHeightMul = num7;

        if (ovenItemData.BakedLevel > num6)
        {
            float temp = ovenItemData.temp;
            string resultCode = bakingProperties?.ResultCode;
            if (resultCode != null)
            {
                ItemStack itemStack = itemSlot.Itemstack.Class == EnumItemClass.Block
                    ? new ItemStack(this.Api.World.GetBlock(new AssetLocation(resultCode)))
                    : new ItemStack(this.Api.World.GetItem(new AssetLocation(resultCode)));

                if (itemStack != null)
                {
                    (this.ovenInv[slotIndex].Itemstack.Collectible as IBakeableCallback)?.OnBaked(this.ovenInv[slotIndex].Itemstack, itemStack);
                    this.ovenInv[slotIndex].Itemstack = itemStack;
                    this.bakingData[slotIndex] = new OvenItemData(itemStack) { temp = temp };
                    flag = true;

                    // XSkills: через глобальные ссылки
                    if (ElectricalProgressiveQOL.xskillsEnabled)
                    {
                        ApplyCookingAbilities(prevStack, slotIndex);
                    }
                }
            }
            else
            {
                ItemSlot outputSlot = new DummySlot(null);
                if (itemSlot.Itemstack.Collectible.CanSmelt(this.Api.World, this.ovenInv, itemSlot.Itemstack, null))
                {
                    itemSlot.Itemstack.Collectible.DoSmelt(this.Api.World, this.ovenInv, this.ovenInv[slotIndex], outputSlot);
                    if (!outputSlot.Empty)
                    {
                        this.ovenInv[slotIndex].Itemstack = outputSlot.Itemstack;
                        this.bakingData[slotIndex] = new OvenItemData(outputSlot.Itemstack) { temp = temp };
                        flag = true;

                        if (ElectricalProgressiveQOL.xskillsEnabled)
                        {
                            ApplyCookingAbilities(prevStack, slotIndex);
                        }
                    }
                }
            }
        }

        if (flag)
        {
            this.updateMesh(slotIndex);
            this.MarkDirty(true);
        }
    }

    private void ApplyCookingAbilities(ItemStack prevStack, int slotIndex)
    {
        try
        {
            // Получаем собственника через поведение (рефлексией)
            object ownable = null;
            if (ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable != null)
                ownable = GetBehaviorByType(ElectricalProgressiveQOL.typeBlockEntityBehaviorOwnable);

            IPlayer ownerPlayer = ownable?.GetType().GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ownable) as IPlayer;

            if (ownerPlayer != null && ElectricalProgressiveQOL.methodGetSkill != null && ElectricalProgressiveQOL.typeCooking != null)
            {
                var skill = ElectricalProgressiveQOL.methodGetSkill.Invoke(ElectricalProgressiveQOL.xLevelingInstance, new object[] { "cooking", false });
                if (ElectricalProgressiveQOL.typeCooking.IsInstanceOfType(skill))
                {
                    var applyAbilities = ElectricalProgressiveQOL.typeCooking.GetMethod("ApplyAbilities", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    applyAbilities?.Invoke(skill, new object[] { this.ovenInv[slotIndex], ownerPlayer, 0f, 1f, new ItemStack[] { prevStack }, 1f });
                }
            }
        }
        catch (Exception ex)
        {
            this.Api.World.Logger.Warning("Error applying cooking abilities: {0}", ex);
        }
    }





    //получает температуру окружающей среды
    protected virtual int EnvironmentTemperature()
    {
        return (int)this.Api.World.BlockAccessor.GetClimateAt(this.Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, this.Api.World.Calendar.TotalDays).Temperature;
    }

    /// <summary>
    /// считает прирост температуры
    /// </summary>
    /// <param name="fromTemp"></param>
    /// <param name="toTemp"></param>
    /// <param name="dt"></param>
    /// <returns></returns>
    public virtual float ChangeTemperature(float fromTemp, float toTemp, float dt)
    {
        float num1 = Math.Abs(fromTemp - toTemp);
        float num2 = num1 * GameMath.Sqrt(num1);
        dt += dt * (num2 / 480f);
        if ((double)num2 < (double)dt)
            return toTemp;
        if ((double)fromTemp > (double)toTemp)
            dt = (float)(-(double)dt / 2.0);
        return (double)Math.Abs(fromTemp - toTemp) < 1.0 ? toTemp : fromTemp + dt;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        this.ovenInv.FromTreeAttributes(tree);
        this.burning = tree.GetInt("burn") > 0;
        this.rotationDeg = tree.GetInt("rota");
        this.ovenTemperature = tree.GetFloat("temp");
        for (int i = 0; i < this.bakeableCapacity; ++i)
            this.bakingData[i] = OvenItemData.ReadFromTree(tree, i);
        ICoreAPI api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
            return;
        this.updateMeshes();
        if (this.clientSidePrevBurning == this.IsBurning)
            return;

        this.clientSidePrevBurning = this.IsBurning;
        this.MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        this.ovenInv.ToTreeAttributes(tree);
        tree.SetInt("burn", this.burning ? 1 : 0);
        tree.SetInt("rota", this.rotationDeg);
        tree.SetFloat("temp", this.ovenTemperature);
        for (int i = 0; i < this.bakeableCapacity; ++i)
            this.bakingData[i].WriteToTree(tree, i);
    }


    /// <summary>
    /// Получение информации о блоке для игрока
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="stringBuilder"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (ElectricalProgressiveQOL.xskillsEnabled && ElectricalProgressiveQOL.methodGetSkill != null && ElectricalProgressiveQOL.typeCooking != null)
        {
            try
            {
                var skill = ElectricalProgressiveQOL.methodGetSkill.Invoke(ElectricalProgressiveQOL.xLevelingInstance, new object[] { "cooking", false });
                if (ElectricalProgressiveQOL.typeCooking.IsInstanceOfType(skill))
                {
                    var getId = ElectricalProgressiveQOL.typeCooking.BaseType.GetProperty("Id");
                    var getSpecId = ElectricalProgressiveQOL.typeCooking.BaseType.GetProperty("SpecialisationID");
                    var id = getId?.GetValue(skill);
                    var specId = getSpecId?.GetValue(skill);

                    var pssType = ElectricalProgressiveQOL.asmXLib.GetType("XLib.XLeveling.PlayerSkillSet");
                    var abilityType = ElectricalProgressiveQOL.asmXLib.GetType("XLib.XLeveling.PlayerAbility");
                    
                    var playerSkillSet = forPlayer?.Entity?.GetBehavior("SkillSet");
                    var indexer = pssType?.GetProperty("Item");
                    var abilitiesForSkill = indexer?.GetValue(playerSkillSet, new object[] { id });
                    var specIndexer = abilitiesForSkill?.GetType().GetProperty("Item");
                    var playerAbility = specIndexer?.GetValue(abilitiesForSkill, new object[] { specId });

                    if (playerAbility != null && (int)(abilityType?.GetProperty("Tier")?.GetValue(playerAbility) ?? 0) >= 1)
                    {
                        for (int slotId = 0; slotId < this.bakeableCapacity; ++slotId)
                        {
                            if (!this.ovenInv[slotId].Empty)
                            {
                                OvenItemData ovenItemData = this.bakingData[slotId];
                                BakingProperties bakingProperties = BakingProperties.ReadFrom(this.ovenInv[slotId].Itemstack);
                                if (bakingProperties != null && ovenItemData != null)
                                {
                                    float num = Math.Min((ovenItemData.BakedLevel - bakingProperties.LevelFrom) /
                                                          (bakingProperties.LevelTo - bakingProperties.LevelFrom), 1f);
                                    stringBuilder.AppendLine(Lang.Get("electricalprogressiveqol:progress", num));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Api.World.Logger.Warning("Error showing cooking progress: {0}", ex);
            }
        }

        stringBuilder.AppendLine();
        for (int slotId = 0; slotId < this.bakeableCapacity; ++slotId)
        {
            if (!this.ovenInv[slotId].Empty)
            {
                ItemStack itemstack = this.ovenInv[slotId].Itemstack;
                stringBuilder.Append(itemstack.GetName());
                stringBuilder.AppendLine($" ({Lang.Get("{0}°C", (int)this.bakingData[slotId].temp)})");
            }
        }
    }







    /// <summary>
    /// Вызывается при установке блока в мир
    /// </summary>
    /// <param name="byItemStack"></param>
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        var electricity = ElectricalProgressive;

        if (electricity == null || byItemStack == null)
            return;

        if (electricity != null)
        {
            electricity.Connection = Facing.AllAll;

            //задаем параметры блока/проводника
            var voltage = MyMiniLib.GetAttributeInt(byItemStack!.Block, "voltage", 32);
            var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack!.Block, "maxCurrent", 5.0F);
            var isolated = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 0);
            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 1);
            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 2);
            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 3);
            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 4);
            electricity!.Eparams = (new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 5);
        }
    }



    public override int DisplayedItems
    {
        get => this.OvenContentMode == EnumOvenContentMode.Quadrants ? 4 : 1;
    }

    protected override float[][] genTransformationMatrices()
    {
        float[][] numArray = new float[this.DisplayedItems][];
        Vec3f[] vec3fArray = new Vec3f[this.DisplayedItems];
        switch (this.OvenContentMode)
        {
            case EnumOvenContentMode.SingleCenter:           //положение пирога
                vec3fArray[0] = new Vec3f(0.0f, 0.4f, 0.0f);
                break;
            case EnumOvenContentMode.Quadrants:             //положение хлеба
                vec3fArray[0] = new Vec3f(-0.125f, 0.4f, -5f / 32f);
                vec3fArray[1] = new Vec3f(-0.125f, 0.4f, 5f / 32f);
                vec3fArray[2] = new Vec3f(3f / 16f, 0.4f, -5f / 32f);
                vec3fArray[3] = new Vec3f(3f / 16f, 0.4f, 5f / 32f);
                break;
        }
        for (int index = 0; index < numArray.Length; ++index)
        {
            Vec3f vec3f = vec3fArray[index];
            float y = this.bakingData[index].CurHeightMul;
            numArray[index] = new Matrixf().Translate(vec3f.X, vec3f.Y, vec3f.Z).Translate(0.5f, 0.0f, 0.5f).RotateYDeg((float)this.rotationDeg).Scale(0.9f, y, 0.9f).Translate(-0.5f, 0.0f, -0.5f).Values;
        }
        return numArray;
    }

    protected override string getMeshCacheKey(ItemStack stack)
    {
        string str = "";
        for (int slotId = 0; slotId < this.bakingData.Length; ++slotId)
        {
            if (this.Inventory[slotId].Itemstack == stack)
            {
                str = "-" + this.bakingData[slotId].CurHeightMul.ToString();
                break;
            }
        }
        return base.getMeshCacheKey(stack) + str;
    }

    /// <summary>
    /// Вызывается при тесселяции блока
    /// </summary>
    /// <param name="mesher"></param>
    /// <param name="tessThreadTesselator"></param>
    /// <returns></returns>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        this.tfMatrices = this.genTransformationMatrices();
        return base.OnTesselation(mesher, tessThreadTesselator);
    }


    /// <summary>
    /// Получает или создает меш для предмета в духовке
    /// </summary>
    /// <param name="stack"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    protected override MeshData getOrCreateMesh(ItemStack stack, int index)
    {
        return base.getOrCreateMesh(stack, index);
    }

    /// <summary>
    /// Вызывается при удалении блока из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        // Очистка мусора
        this.lastRemoved = null!;
        this.capi = null;

    }

    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();

        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive
        // Очистка мусора
        this.lastRemoved = null!;
        this.capi = null;

        // Удаляем слушателя тика игры
        UnregisterGameTickListener(listenerId);

    }

}
