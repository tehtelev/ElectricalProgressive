using ElectricalProgressive.Utils;
using System;
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
public class BlockEntityEOven : BlockEntityDisplay, IHeatSource
{
    public static readonly int BakingStageThreshold = 100;
    public static readonly int MaxBakingTemperatureAccepted = 260;


    private bool _burning;
    private bool _clientSidePrevBurning;
    public float PrevOvenTemperature = 20f;
    public float OvenTemperature = 20f;
    public readonly OvenItemData[] BakingData;
    private ItemStack _lastRemoved;
    private int _rotationDeg;


    internal InventoryEOven OvenInv;

    private int _maxConsumption;



    public virtual float MaxTemperature => 300f;

    public virtual int BakeableCapacity => 4;

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


    public EnumOvenContentMode OvenContentMode  //как отображать содержимое
    {
        get
        {
            var firstNonEmptySlot = this.OvenInv.FirstNonEmptySlot;
            if (firstNonEmptySlot == null)
                return EnumOvenContentMode.Quadrants;

            var bakingProperties = BakingProperties.ReadFrom(firstNonEmptySlot.Itemstack);

            if (bakingProperties == null)    //протухло
                return EnumOvenContentMode.Quadrants;
            else
                return !bakingProperties.LargeItem ? EnumOvenContentMode.Quadrants : EnumOvenContentMode.SingleCenter;

        }
    }

    public BlockEntityEOven()
    {
        this.BakingData = new OvenItemData[this.BakeableCapacity];
        for (var index = 0; index < this.BakeableCapacity; ++index)
            this.BakingData[index] = new OvenItemData();
        this.OvenInv = new InventoryEOven("eoven-0", this.BakeableCapacity);

    }

    public override InventoryBase Inventory => (InventoryBase)this.OvenInv;

    public override string InventoryClassName => "eoven";


    public bool IsBurning;

    private long _listenerId;

    /// <summary>
    /// Инициализация блока
    /// </summary>
    /// <param name="api"></param>
    public override void Initialize(ICoreAPI api)
    {
        this.capi = api as ICoreClientAPI;
        base.Initialize(api);
        this.OvenInv.LateInitialize(this.InventoryClassName + "-" + this.Pos?.ToString(), api);
        _listenerId = this.RegisterGameTickListener(new Action<float>(this.OnBurnTick), 100);

        this.SetRotation();

        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }


    /// <summary>
    /// Устанавливает поворот духовки в зависимости от ее стороны
    /// </summary>
    private void SetRotation()
    {
        this._rotationDeg = this.Block.Variant["side"] switch
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
            string[] candidateFieldNames = ["blockEntityBehaviors", "behaviors", "BlockEntityBehaviors", "Behaviors"];

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

        return null!;
    }




    /// <summary>
    /// Обработка взаимодействия с духовкой
    /// </summary>
    /// <param name="byPlayer"></param>
    /// <param name="bs"></param>
    /// <returns></returns>
    public virtual bool OnInteract(IPlayer byPlayer, BlockSelection bs)
    {
        var activeHotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
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


        if (activeHotbarSlot.Itemstack.Equals(this.Api.World, this._lastRemoved, GlobalConstants.IgnoredStackAttributes) && !this.OvenInv[0].Empty)
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

                var place = activeHotbarSlot.Itemstack?.Block?.Sounds?.Place.Location;
                this.Api.World.PlaySoundAt(place != null ? place : new AssetLocation("sounds/player/buildhigh"), byPlayer.Entity, byPlayer, true, 16f, 1f);
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
                    var bakingProperties1 = BakingProperties.ReadFrom(activeHotbarSlot.Itemstack);
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
        var bakingProperties1 = BakingProperties.ReadFrom(slot.Itemstack);
        if (bakingProperties1 == null || !slot.Itemstack.Attributes.GetBool("bakeable", true)) //если свойства выпекания не найдены
            return false;

        if (!inv[0].Empty) //если в духовке уже что-то лежит в первом слоте
        {
            var bakingProperties2 = BakingProperties.ReadFrom(slot.Itemstack);
            if (bakingProperties2 != null && bakingProperties2.LargeItem)  //если уже лежит большое - выход
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
        if (!IsValidInput(slot, OvenInv))
            return false;

        for (var index = 0; index < this.BakeableCapacity; ++index)
        {
            if (this.OvenInv[index].Empty)
            {
                var num = slot.TryPutInto(this.Api.World, this.OvenInv[index]);
                if (num > 0)
                {
                    this.BakingData[index] = new OvenItemData(this.OvenInv[index].Itemstack);
                    this.updateMesh(index);
                    this.MarkDirty(true);
                    this._lastRemoved = null!;
                }

            }
            if (index == 0)
            {
                var bakingProperties2 = BakingProperties.ReadFrom(this.OvenInv[0].Itemstack);
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
        for (var bakeableCapacity = this.BakeableCapacity; bakeableCapacity >= 0; --bakeableCapacity)
        {
            if (!this.OvenInv[bakeableCapacity].Empty)
            {
                var itemstack = this.OvenInv[bakeableCapacity].TakeOut(1);
                this._lastRemoved = itemstack == null ? null! : itemstack.Clone();
                if (byPlayer.InventoryManager.TryGiveItemstack(itemstack))
                {
                    var place = itemstack?.Block?.Sounds?.Place.Location;
                    this.Api.World.PlaySoundAt(place != null ? place : new AssetLocation("sounds/player/throw"), (Entity)byPlayer.Entity, byPlayer, true, 16f, 1f);
                }
                if (itemstack?.StackSize > 0)
                    this.Api.World.SpawnItemEntity(itemstack, this.Pos);
                //this.Api.World.Logger.Audit("{0} Took 1x{1} from Clay oven at {2}.", (object)byPlayer.PlayerName, (object)itemstack.Collectible.Code, (object)this.Pos);
                this.BakingData[bakeableCapacity].CurHeightMul = 1f;
                this.BakingData[bakeableCapacity].temp = 20;
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
        return Math.Max((float)(((double)this.OvenTemperature - 20.0) / ((double)this.MaxTemperature - 20.0) * MyMiniLib.GetAttributeFloat(this.Block, "maxHeat", 0.0F)), 0.0f);
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
                if (!OvenInv.Empty)
                    Api.World.PlaySoundAt(new AssetLocation("electricalprogressiveqol:sounds/din_din_din"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            }
        }




        if (!OvenInv.Empty)   //если не пусто
        {

            var envTemp = this.EnvironmentTemperature();

            if (this.IsBurning)
            {
                //чем больше энергии тем выше будет максимальная достижимая температура
                var power = ovenBehavior.PowerSetting;
                float toTemp = Math.Max(envTemp, power * MaxBakingTemperatureAccepted / _maxConsumption);
                this.OvenTemperature = this.ChangeTemperature(this.OvenTemperature, toTemp, dt * 1.5F);

            }
            else
            {
                this.OvenTemperature = ChangeTemperature(OvenTemperature, EnvironmentTemperature(), dt); //выравниваем температуру с окружающей средой

            }


            if (this.OvenTemperature > envTemp)  //греем и охлаждаем еду
            {
                this.HeatInput(dt * 1.2f, this.IsBurning);
            }
        }

        else
        {
            this.OvenTemperature = ChangeTemperature(OvenTemperature, EnvironmentTemperature(), dt); //выравниваем температуру с окружающей средой
        }



        //if (++this.syncCount % 5 != 0 || !this.IsBurning && (double) this.prevOvenTemperature == (double) this.ovenTemperature && this.Inventory[0].Empty && this.Inventory[1].Empty && this.Inventory[2].Empty && this.Inventory[3].Empty)
        // return;
        this.MarkDirty();
        this.PrevOvenTemperature = this.OvenTemperature;
    }

    /// <summary>
    /// греем содержимое всей печи
    /// </summary>
    /// <param name="dt"></param>
    /// <param name="up"></param>
    protected virtual void HeatInput(float dt, bool up)
    {
        for (var index = 0; index < this.BakeableCapacity; ++index)
        {
            var itemstack = this.OvenInv[index].Itemstack;
            if (itemstack != null && (double)this.HeatStack(itemstack, dt, index, up) >= 100.0)
                if (up)                             //если еда остывает, то не выпекаем и активно снижаем температуру в HeatStack
                    this.IncrementallyBake(dt, index);
        }
    }

    /// <summary>
    /// греем конкретно один предмет
    /// </summary>
    /// <param name="stack"></param>
    /// <param name="dt"></param>
    /// <param name="i"></param>
    /// <param name="up"></param>
    /// <returns></returns>
    protected virtual float HeatStack(ItemStack stack, float dt, int i, bool up)
    {
        var temp = this.BakingData[i].temp;
        var val21 = temp;
        var targetTemp = up                   //при нагревании тянемся к печи, при остывании к окржающей среде
            ? this.OvenTemperature
            : this.EnvironmentTemperature();

        if ((double)temp < (double)targetTemp)
        {
            var dt1 = (1f + GameMath.Clamp((float)(((double)targetTemp - (double)temp) / 28.0), 0.0f, 1.6f)) * dt;
            val21 = this.ChangeTemperature(temp, targetTemp, dt1);
            var combustibleProps = stack.Collectible.CombustibleProps;
            var maxTemperature = combustibleProps != null ? combustibleProps.MaxTemperature : 0;
            var itemAttributes = stack.ItemAttributes;
            var val22 = itemAttributes != null ? itemAttributes["maxTemperature"].AsInt() : 0;
            var val1 = Math.Max(maxTemperature, val22);
            if (val1 > 0)
                val21 = Math.Min((float)val1, val21);
        }
        else if ((double)temp > (double)targetTemp)
        {
            var dt2 = (1f + GameMath.Clamp((float)(((double)temp - (double)targetTemp) / 28.0), 0.0f, 1.6f)) * dt;
            val21 = this.ChangeTemperature(temp, targetTemp, dt2);
        }
        if ((double)temp != (double)val21)
            this.BakingData[i].temp = val21;
        return val21;
    }

    protected virtual void IncrementallyBake(float dt, int slotIndex)
    {
        var itemSlot = this.Inventory[slotIndex];
        var ovenItemData = this.BakingData[slotIndex];
        var prevStack = this.OvenInv[slotIndex].Itemstack?.Clone();

        var num1 = ovenItemData.BrowningPoint == 0 ? 160f : ovenItemData.BrowningPoint;
        double val = ovenItemData.temp / num1;
        var num2 = ovenItemData.TimeToBake == 0 ? 1f : ovenItemData.TimeToBake;
        var num3 = (float)GameMath.Clamp((int)val, 1, 30) * dt / num2;

        if (ovenItemData.temp > num1)
            ovenItemData.BakedLevel += num3;

        var bakingProperties = BakingProperties.ReadFrom(itemSlot.Itemstack);
        var num5 = bakingProperties?.LevelFrom ?? 0f;
        var num6 = bakingProperties?.LevelTo ?? 1f;
        var num7 = (float)(int)(GameMath.Mix(bakingProperties?.StartScaleY ?? 1f, bakingProperties?.EndScaleY ?? 1f,
            GameMath.Clamp((ovenItemData.BakedLevel - num5) / (num6 - num5), 0f, 1f)) * BlockEntityOven.BakingStageThreshold) / BlockEntityOven.BakingStageThreshold;

        var flag = num7 != ovenItemData.CurHeightMul;
        ovenItemData.CurHeightMul = num7;

        if (ovenItemData.BakedLevel > num6)
        {
            var temp = ovenItemData.temp;
            var resultCode = bakingProperties?.ResultCode;
            if (resultCode != null)
            {
                var itemStack = (ItemStack)null;
                // на выходе рецепта блок?
                var block = this.Api.World.GetBlock(new AssetLocation(resultCode));
                if (block != null)
                    itemStack = new ItemStack(block, 1);

                // на выходе рецепта предмет?
                var obj = this.Api.World.GetItem(new AssetLocation(resultCode));
                if (obj != null)
                    itemStack = new ItemStack(obj, 1);


                if (itemStack != null)
                {
                    (this.OvenInv[slotIndex].Itemstack.Collectible as IBakeableCallback)?.OnBaked(this.OvenInv[slotIndex].Itemstack, itemStack);
                    this.OvenInv[slotIndex].Itemstack = itemStack;
                    this.BakingData[slotIndex] = new OvenItemData(itemStack) { temp = temp };
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
                if (itemSlot.Itemstack.Collectible.CanSmelt(this.Api.World, this.OvenInv, itemSlot.Itemstack, null))
                {
                    itemSlot.Itemstack.Collectible.DoSmelt(this.Api.World, this.OvenInv, this.OvenInv[slotIndex], outputSlot);
                    if (!outputSlot.Empty)
                    {
                        this.OvenInv[slotIndex].Itemstack = outputSlot.Itemstack;
                        this.BakingData[slotIndex] = new OvenItemData(outputSlot.Itemstack) { temp = temp };
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

            var ownerPlayer = ownable?.GetType().GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ownable) as IPlayer;

            if (ownerPlayer != null && ElectricalProgressiveQOL.methodGetSkill != null && ElectricalProgressiveQOL.typeCooking != null)
            {
                var skill = ElectricalProgressiveQOL.methodGetSkill.Invoke(ElectricalProgressiveQOL.xLevelingInstance,
                    ["cooking", false]);
                if (ElectricalProgressiveQOL.typeCooking.IsInstanceOfType(skill))
                {
                    var applyAbilities = ElectricalProgressiveQOL.typeCooking.GetMethod("ApplyAbilities", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    applyAbilities?.Invoke(skill, [this.OvenInv[slotIndex], ownerPlayer, 0f, 1f, new ItemStack[] { prevStack }, 1f
                    ]);
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
        var num1 = Math.Abs(fromTemp - toTemp);
        var num2 = num1 * GameMath.Sqrt(num1);
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
        this.OvenInv.FromTreeAttributes(tree);
        this._burning = tree.GetInt("burn") > 0;
        this._rotationDeg = tree.GetInt("rota");
        this.OvenTemperature = tree.GetFloat("temp");
        for (var i = 0; i < this.BakeableCapacity; ++i)
            this.BakingData[i] = OvenItemData.ReadFromTree(tree, i);
        var api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0)
            return;
        this.updateMeshes();
        if (this._clientSidePrevBurning == this.IsBurning)
            return;

        this._clientSidePrevBurning = this.IsBurning;
        this.MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        this.OvenInv.ToTreeAttributes(tree);
        tree.SetInt("burn", this._burning ? 1 : 0);
        tree.SetInt("rota", this._rotationDeg);
        tree.SetFloat("temp", this.OvenTemperature);
        for (var i = 0; i < this.BakeableCapacity; ++i)
            this.BakingData[i].WriteToTree(tree, i);
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
                var skill = ElectricalProgressiveQOL.methodGetSkill.Invoke(ElectricalProgressiveQOL.xLevelingInstance,
                    ["cooking", false]);
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
                    var abilitiesForSkill = indexer?.GetValue(playerSkillSet, [id]);
                    var specIndexer = abilitiesForSkill?.GetType().GetProperty("Item");
                    var playerAbility = specIndexer?.GetValue(abilitiesForSkill, [specId]);

                    if (playerAbility != null && (int)(abilityType?.GetProperty("Tier")?.GetValue(playerAbility) ?? 0) >= 1)
                    {
                        for (var slotId = 0; slotId < this.BakeableCapacity; ++slotId)
                        {
                            if (!this.OvenInv[slotId].Empty)
                            {
                                var ovenItemData = this.BakingData[slotId];
                                var bakingProperties = BakingProperties.ReadFrom(this.OvenInv[slotId].Itemstack);
                                if (bakingProperties != null && ovenItemData != null)
                                {
                                    var num = Math.Min((ovenItemData.BakedLevel - bakingProperties.LevelFrom) /
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
        for (var slotId = 0; slotId < this.BakeableCapacity; ++slotId)
        {
            if (!this.OvenInv[slotId].Empty)
            {
                var itemstack = this.OvenInv[slotId].Itemstack;
                stringBuilder.Append(itemstack.GetName());
                stringBuilder.AppendLine($" ({Lang.Get("{0}°C", (int)this.BakingData[slotId].temp)})");
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
            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this.Block, this);
        }
    }



    public override int DisplayedItems
    {
        get => this.OvenContentMode == EnumOvenContentMode.Quadrants ? 4 : 1;
    }

    protected override float[][] genTransformationMatrices()
    {
        var numArray = new float[this.DisplayedItems][];
        var vec3FArray = new Vec3f[this.DisplayedItems];
        switch (this.OvenContentMode)
        {
            case EnumOvenContentMode.SingleCenter:           //положение пирога
                vec3FArray[0] = new Vec3f(0.0f, 0.4f, 0.0f);
                break;
            case EnumOvenContentMode.Quadrants:             //положение хлеба
                vec3FArray[0] = new Vec3f(-0.125f, 0.4f, -5f / 32f);
                vec3FArray[1] = new Vec3f(-0.125f, 0.4f, 5f / 32f);
                vec3FArray[2] = new Vec3f(3f / 16f, 0.4f, -5f / 32f);
                vec3FArray[3] = new Vec3f(3f / 16f, 0.4f, 5f / 32f);
                break;
        }
        for (var index = 0; index < numArray.Length; ++index)
        {
            var vec3F = vec3FArray[index];
            var y = this.BakingData[index].CurHeightMul;
            numArray[index] = new Matrixf().Translate(vec3F.X, vec3F.Y, vec3F.Z).Translate(0.5f, 0.0f, 0.5f).RotateYDeg((float)this._rotationDeg).Scale(0.9f, y, 0.9f).Translate(-0.5f, 0.0f, -0.5f).Values;
        }
        return numArray;
    }

    protected override string getMeshCacheKey(ItemSlot slot)
    {
        var str = "";
        for (var slotId = 0; slotId < this.BakingData.Length; ++slotId)
        {
            if (this.Inventory[slotId].Itemstack == slot.Itemstack)
            {
                str = "-" + this.BakingData[slotId].CurHeightMul.ToString();
                break;
            }
        }
        return base.getMeshCacheKey(slot) + str;
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
    protected override MeshData getOrCreateMesh(ItemSlot slot, int index)
    {
        return base.getOrCreateMesh(slot, index);
    }

    /// <summary>
    /// Вызывается при удалении блока из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        // Очистка мусора
        this._lastRemoved = null!;
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
        this._lastRemoved = null!;
        this.capi = null;

        // Удаляем слушателя тика игры
        UnregisterGameTickListener(_listenerId);

    }

}
