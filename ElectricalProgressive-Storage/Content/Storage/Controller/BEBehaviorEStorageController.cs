using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Storage.Controller;

/// <summary>
/// Счёт ватт сети. Пока по проводу ещё идёт прежняя мощность, лампа контроллера не гаснет.
/// </summary>
public class BEBehaviorEStorageController : BlockEntityBehavior, IElectricConsumer
{
    public float AvgConsumeCoeff { get; set; } = 1f;
    public bool Powered { get; private set; }
    public float Received { get; private set; }
    public float Demand { get; private set; } = 8f;

    private long _demandAt = -1000;
    private float _requestCached = -1f;
    private long _lowSince = -1;
    private float _shownGot;
    private float _shownNeed;

    public readonly List<ItemStack> Jobs = new();
    public readonly Dictionary<ItemStack, int> InFlight = new();

    public int Flight(ItemStack job) => InFlight.TryGetValue(job, out var count) ? count : 0;

    public void SetFlight(ItemStack job, int count)
    {
        if (count <= 0)
            InFlight.Remove(job);
        else
            InFlight[job] = count;
    }

    public BEBehaviorEStorageController(BlockEntity blockEntity) : base(blockEntity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("storagePowered", Powered);
        tree.SetFloat("storageReceived", Received);
        tree.SetFloat("storageDemand", Demand);
        tree.SetFloat("storageShownGot", _shownGot);
        tree.SetFloat("storageShownNeed", _shownNeed);
        var jobs = new TreeAttribute();
        for (var i = 0; i < Jobs.Count; i++)
            jobs["j" + i.ToString("D2")] = new ItemstackAttribute(Jobs[i]);
        tree["craftJobs"] = jobs;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Powered = tree.GetBool("storagePowered");
        Received = tree.GetFloat("storageReceived");
        Demand = tree.GetFloat("storageDemand", 8f);
        _shownGot = tree.GetFloat("storageShownGot");
        _shownNeed = tree.GetFloat("storageShownNeed");
        Jobs.Clear();
        InFlight.Clear();
        var jobs = tree.GetTreeAttribute("craftJobs");
        if (jobs == null)
            return;

        for (var i = 0; i < 24; i++)
        {
            if (jobs["j" + i.ToString("D2")] is not ItemstackAttribute { value: { } stack })
                continue;
            stack.ResolveBlockOrItem(worldAccessForResolve);
            if (stack.Collectible != null && stack.StackSize > 0)
                Jobs.Add(stack);
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Api?.World == null)
            return;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        if (scan.Controllers.Count == 0)
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-nocontroller"));
        else if (scan.Conflict)
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-conflict"));
        else if (!StorageAccess.HasPower(Api.World, scan))
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-nopower"));
        else
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-online"));

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-channels", scan.ChannelUsed, scan.ChannelSupply));
        var got = (int)Math.Round(_shownGot);
        var need = (int)Math.Round(_shownNeed);
        if (need <= 0 && got <= 0)
            NetworkWatts(scan, out got, out need);
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-power", got, need));
        var terminals = 0;
        foreach (var pos in scan.Terminals)
        {
            var kind = PanelStack.Kind(Api.World, pos, false);
            terminals += kind < 1 ? 1 : kind;
        }

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-devices",
            scan.Drives.Count, terminals));
    }

    public float Consume_request()
    {
        if (Api?.World == null)
            return 8f;

        var now = Api.World.ElapsedMilliseconds;
        if (now == _demandAt && _requestCached >= 0f)
            return _requestCached;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        var demand = StorageAccess.Demand(Api.World, scan);
        var cap = MyMiniLib.GetAttributeFloat(Block, "maxConsumption", 2000f);
        var request = Pays(scan, Math.Min(demand, cap)) ? Math.Min(demand, cap) : 0f;
        _requestCached = request;
        _demandAt = now;
        Demand = request;
        if (Api.Side == EnumAppSide.Server)
            SetLit(Powered || StorageAccess.HasPower(Api.World, scan));
        return request;
    }

    public void Consume_receive(float amount)
    {
        if (Api?.Side != EnumAppSide.Server || Api.World == null)
            return;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        var cap = MyMiniLib.GetAttributeFloat(Block, "maxConsumption", 2000f);
        var live = Math.Min(StorageAccess.Demand(Api.World, scan), cap);
        var pays = Pays(scan, live);
        // Второй сборщик поднимает счёт сразу, а по проводу ещё идёт прежняя мощность:
        // пакет доходит по блоку за тик. Гасить сеть на этом скачке нельзя, иначе сборщики
        // встают, счёт падает и контроллеры загораются снова.
        var now = Api.World.ElapsedMilliseconds;
        var covered = pays && amount + 1f >= live;
        bool powered;
        if (!pays || amount <= 0.5f)
        {
            powered = false;
            _lowSince = -1;
        }
        else if (covered)
        {
            powered = true;
            _lowSince = -1;
        }
        else
        {
            if (_lowSince < 0)
                _lowSince = now;
            powered = Powered && now - _lowSince < 4000;
        }
        var wasPowered = Powered;
        var wasRounded = (int)Math.Round(Received);
        var bill = pays ? live : 0f;
        var billChanged = (int)Math.Round(Demand) != (int)Math.Round(bill);
        Received = amount;
        Powered = powered;
        Demand = bill;
        var shownGot = 0f;
        foreach (var pos in scan.Controllers)
        {
            var beh = Api.World.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEStorageController>();
            if (beh == null)
                continue;
            shownGot += ReferenceEquals(beh, this) ? amount : beh.Received;
        }

        var shownNeed = live;
        if (shownNeed > 0f && shownGot > shownNeed)
            shownGot = shownNeed;
        var shownChanged = (int)Math.Round(_shownGot) != (int)Math.Round(shownGot)
            || (int)Math.Round(_shownNeed) != (int)Math.Round(shownNeed);
        _shownGot = shownGot;
        _shownNeed = shownNeed;
        if (powered != wasPowered)
            StorageAccess.TouchContents();
        SetLit(powered || StorageAccess.HasPower(Api.World, scan));
        if (powered == wasPowered && (int)Math.Round(amount) == wasRounded && !billChanged && !shownChanged)
            return;

        Blockentity.MarkDirty();
    }

    private void NetworkWatts(StorageScan scan, out int got, out int need)
    {
        var sum = 0f;
        var bill = 0f;
        var world = Api?.World;
        if (world != null)
        {
            foreach (var pos in scan.Controllers)
            {
                var beh = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEStorageController>();
                if (beh == null)
                    continue;
                sum += beh.Received;
                if (beh.Demand > bill)
                    bill = beh.Demand;
            }
        }

        if (bill <= 0f)
            bill = Demand;
        if (bill > 0f && sum > bill)
            sum = bill;
        got = (int)Math.Round(sum);
        need = (int)Math.Round(bill);
    }

    private bool Pays(StorageScan scan, float demand)
    {
        BEBehaviorEStorageController? powered = null;
        BEBehaviorEStorageController? fed = null;
        var anyReceipt = false;
        foreach (var pos in scan.Controllers)
        {
            var beh = Api?.World?.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEStorageController>();
            if (beh == null)
                continue;
            if (beh.Received > 0.5f)
                anyReceipt = true;
            if (beh.Powered && beh.Received + 0.5f >= demand && (powered == null || StorageScan.Earlier(pos, powered.Pos)))
                powered = beh;
            if (fed == null
                || beh.Received > fed.Received + 0.01f
                || (Math.Abs(beh.Received - fed.Received) <= 0.01f && StorageScan.Earlier(pos, fed.Pos)))
                fed = beh;
        }

        if (powered != null)
            return powered == this;
        if (!anyReceipt)
            return true;
        return fed == this;
    }

    private void SetLit(bool on)
    {
        if (Api?.World == null)
            return;
        var state = on ? "online" : "offline";
        if (Block.Variant == null || !Block.Variant.TryGetValue("state", out var now) || now == state)
            return;
        var next = Api.World.GetBlock(Block.CodeWithVariant("state", state));
        if (next != null)
            Api.World.BlockAccessor.ExchangeBlock(next.Id, Pos);
    }

    public float getPowerReceive() => Received;

    public float getPowerRequest() => Demand;

    public void Update()
    {
    }
}
