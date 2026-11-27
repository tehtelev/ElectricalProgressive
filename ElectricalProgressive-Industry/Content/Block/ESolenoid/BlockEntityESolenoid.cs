using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ESolenoid;

public class BlockEntityESolenoid : BlockEntityEFacingBase
{
    public const int MaxPush = 12;
    private const int SoftReplaceable = 6000;
    private const string ExtendedKey = "solenoidExtended";
    private const string BlockedKey = "solenoidBlocked";
    private const string TravelKey = "solenoidTravel";
    private const string AnimExtend = "extend";
    private const string AnimRetract = "retract";
    private const float RodFrames = 8f;
    // 8 кадров при 30 fps и скорости 2. Блоки едут, когда шток уже доехал.
    private const int RodTravelMs = 150;

    public bool Extended { get; private set; }
    public bool Blocked { get; private set; }
    public bool RemovingHead { get; private set; }
    private bool Travel;
    private long _pushAt;

    private bool _clientReady;
    private bool _visualExtended;
    private bool _sawExtended;
    private bool _rodReady;
    private static MeshData? _rodMesh;
    private static Shape? _rodShape;

    private BEBehaviorESolenoid? Power => GetBehavior<BEBehaviorESolenoid>();
    private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    public override Facing GetConnection(Facing value)
    {
        return FacingHelper.FullFace(value);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnServerTick, 50);
        else
        {
            _clientReady = true;
            EnsureRodRenderer();
            SyncRod(true);
            RegisterGameTickListener(OnClientTick, 50);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool(ExtendedKey, Extended);
        tree.SetBool(BlockedKey, Blocked);
        tree.SetBool(TravelKey, Travel);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Extended = tree.GetBool(ExtendedKey);
        Blocked = tree.GetBool(BlockedKey);
        Travel = tree.GetBool(TravelKey);
        if (_clientReady)
            SyncRod(false);
    }

    public void NotifyHeadGone()
    {
        if (RemovingHead)
            return;

        Extended = false;
        MarkDirty();
    }

    /// <summary>Куда выезжает шток: туда же, куда у мотора смотрит вал.</summary>
    public BlockFacing? ExtendDirection()
    {
        if ((Facing & Facing.AllNorth) != 0) return BlockFacing.NORTH;
        if ((Facing & Facing.AllEast) != 0) return BlockFacing.EAST;
        if ((Facing & Facing.AllSouth) != 0) return BlockFacing.SOUTH;
        if ((Facing & Facing.AllWest) != 0) return BlockFacing.WEST;
        if ((Facing & Facing.AllUp) != 0) return BlockFacing.UP;
        if ((Facing & Facing.AllDown) != 0) return BlockFacing.DOWN;
        return null;
    }

    private void OnServerTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Server || ExtendDirection() == null)
            return;

        if (Extended && HeadState() == SolenoidHeadState.Foreign)
        {
            Extended = false;
            MarkDirty();
        }

        var powered = Power != null && Power.HasPower;
        if (!powered)
        {
            CancelTravel();
            if (Extended)
                Retract();
            return;
        }

        if (Extended)
            return;

        var chain = new List<BlockPos>(MaxPush);
        var readiness = Inspect(chain, out var gap, out var dir);
        if (!Travel)
        {
            if (readiness == PushReadiness.Unloaded)
                return;
            if (readiness != PushReadiness.Ready)
            {
                SetBlocked(true);
                return;
            }

            SetBlocked(false);
            Travel = true;
            _pushAt = Api.World.ElapsedMilliseconds + RodTravelMs;
            MarkDirty();
            return;
        }

        if (Api.World.ElapsedMilliseconds < _pushAt)
            return;

        Travel = false;
        _pushAt = 0;
        if (readiness == PushReadiness.Ready && dir != null && gap != null && Api.World != null)
            Push(Api.World, Api.World.BlockAccessor, chain, gap, dir);
        else if (readiness == PushReadiness.Blocked)
            SetBlocked(true);

        if (!Extended)
            MarkDirty();
    }

    private void CancelTravel()
    {
        if (!Travel && _pushAt == 0)
            return;

        Travel = false;
        _pushAt = 0;
        MarkDirty();
    }

    private PushReadiness Inspect(List<BlockPos> chain, out BlockPos? gap, out BlockFacing? dir)
    {
        gap = null;
        dir = ExtendDirection();
        var world = Api?.World;
        if (dir == null || world == null)
            return PushReadiness.Unloaded;

        var ba = world.BlockAccessor;
        var cursor = Pos.AddCopy(dir);
        for (var i = 0; i < MaxPush; i++)
        {
            if (!Loaded(ba, cursor))
                return PushReadiness.Unloaded;

            var block = ba.GetBlock(cursor);
            if (IsSoft(block))
            {
                gap = cursor.Copy();
                return PushReadiness.Ready;
            }

            if (!CanPush(ba, block, cursor))
                return PushReadiness.Blocked;

            chain.Add(cursor.Copy());
            cursor = cursor.AddCopy(dir);
        }

        if (!Loaded(ba, cursor))
            return PushReadiness.Unloaded;
        if (!IsSoft(ba.GetBlock(cursor)))
            return PushReadiness.Blocked;

        gap = cursor.Copy();
        return PushReadiness.Ready;
    }

    private void Push(IWorldAccessor world, IBlockAccessor ba, List<BlockPos> chain, BlockPos gap, BlockFacing dir)
    {
        var headBlock = world.GetBlock(new AssetLocation("electricalprogressiveindustry:esolenoidhead"));
        if (headBlock == null || headBlock.BlockId == 0)
            return;

        if (ba.GetBlock(gap).BlockId != 0)
            ba.BreakBlock(gap, null);

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var from = chain[i];
            var to = from.AddCopy(dir);
            var id = ba.GetBlock(from).BlockId;
            if (ba.GetBlock(to).BlockId != 0)
                ba.BreakBlock(to, null);
            ba.SetBlock(id, to);
            ba.SetBlock(0, from);
        }

        var headPos = Pos.AddCopy(dir);
        RemovingHead = true;
        ba.SetBlock(headBlock.BlockId, headPos);
        RemovingHead = false;

        var head = ba.GetBlockEntity(headPos) as BlockEntityESolenoidHead;
        if (head == null)
        {
            ba.SpawnBlockEntity("BlockEntityESolenoidHead", headPos);
            head = ba.GetBlockEntity(headPos) as BlockEntityESolenoidHead;
        }

        if (head != null)
        {
            head.Facing = Facing;
            head.BasePos = Pos.Copy();
            head.HasBase = true;
            head.MarkDirty(true);
        }

        Extended = true;
        SetBlocked(false);
        world.PlaySoundAt(new AssetLocation("game:sounds/block/metal"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 16f, 0.55f);
        MarkDirty();
    }

    public void Retract()
    {
        if (Api?.World == null)
        {
            Extended = false;
            return;
        }

        var state = HeadState();
        if (state == SolenoidHeadState.Unloaded)
            return;

        if (state == SolenoidHeadState.Ours)
        {
            RemovingHead = true;
            Api.World.BlockAccessor.SetBlock(0, Pos.AddCopy(ExtendDirection()));
            RemovingHead = false;
            if (Extended)
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/metal"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, false, 16f, 0.35f);
        }

        Extended = false;
        SetBlocked(false);
        MarkDirty();
    }

    public override void OnBlockRemoved()
    {
        if (Api?.World != null && HeadState() == SolenoidHeadState.Ours)
        {
            RemovingHead = true;
            Api.World.BlockAccessor.SetBlock(0, Pos.AddCopy(ExtendDirection()));
            RemovingHead = false;
        }

        Extended = false;
        base.OnBlockRemoved();
    }

    private SolenoidHeadState HeadState()
    {
        var dir = ExtendDirection();
        var ba = Api?.World?.BlockAccessor;
        if (dir == null || ba == null)
            return SolenoidHeadState.Foreign;

        var headPos = Pos.AddCopy(dir);
        if (!Loaded(ba, headPos))
            return SolenoidHeadState.Unloaded;

        if (ba.GetBlock(headPos)?.Code?.Path != "esolenoidhead")
            return SolenoidHeadState.Foreign;

        if (ba.GetBlockEntity(headPos) is not BlockEntityESolenoidHead head || !head.HasBase)
            return SolenoidHeadState.Foreign;

        return SamePos(head.BasePos, Pos) ? SolenoidHeadState.Ours : SolenoidHeadState.Foreign;
    }

    private static bool SamePos(BlockPos a, BlockPos b)
    {
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    private enum SolenoidHeadState
    {
        Ours,
        Foreign,
        Unloaded
    }

    private enum PushReadiness
    {
        Ready,
        Blocked,
        Unloaded
    }

    private void SetBlocked(bool blocked)
    {
        if (Blocked == blocked)
            return;

        Blocked = blocked;
        MarkDirty();
    }

    private static bool Loaded(IBlockAccessor ba, BlockPos pos)
    {
        return ba.IsValidPos(pos) && ba.GetChunkAtBlockPos(pos) != null;
    }

    private static bool IsSoft(Vintagestory.API.Common.Block block)
    {
        return block.BlockId == 0 || block.Replaceable >= SoftReplaceable;
    }

    private static bool CanPush(IBlockAccessor ba, Vintagestory.API.Common.Block block, BlockPos pos)
    {
        if (block.MatterState == EnumMatterState.Liquid)
            return false;
        if (ba.GetBlockEntity(pos) != null)
            return false;
        if (block.Code?.Path?.Contains("esolenoid") == true)
            return false;
        if (block.Resistance >= 80f)
            return false;
        return true;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (Api?.Side == EnumAppSide.Client)
            EnsureRodRenderer();

        if (!ClipActive(AnimExtend) && !ClipActive(AnimRetract))
            return base.OnTesselation(mesher, tesselator);

        base.OnTesselation(mesher, tesselator);
        return true;
    }

    private void OnClientTick(float dt)
    {
        if (_visualExtended || !ClipActive(AnimRetract))
            return;

        var animator = AnimUtil?.animator;
        var state = animator?.GetAnimationState(AnimRetract);
        if (state == null || state.CurrentFrame < RodFrames - 0.05f)
            return;

        StopClip(AnimRetract);
        MarkDirty(true);
    }

    private void SyncRod(bool snap)
    {
        if (Api?.Side != EnumAppSide.Client)
            return;

        EnsureRodRenderer();

        if (Extended || Travel)
        {
            StopClip(AnimRetract);
            if (!ClipActive(AnimExtend))
                StartClip(AnimExtend);
            if (snap && Extended)
                SetFrame(AnimExtend, RodFrames);
            if (Extended)
                _sawExtended = true;
            _visualExtended = true;
        }
        else if (_visualExtended)
        {
            StopClip(AnimExtend);
            var playRetract = _sawExtended && !ClipActive(AnimRetract);
            if (playRetract)
                StartClip(AnimRetract);
            _sawExtended = false;
            _visualExtended = false;
            // Обрыв хода: ретракт стартует уже выдвинутым, его не крутим. Рендерер надо снять.
            if (!playRetract)
                MarkDirty(true);
        }

        if (AnimUtil?.renderer != null)
            AnimUtil.renderer.CustomTransform = FacingMatrix();
        if (ClipActive(AnimExtend) || ClipActive(AnimRetract))
            MarkDirty(true);
    }

    private void EnsureRodRenderer()
    {
        var util = AnimUtil;
        if (_rodReady || util == null || Api == null)
            return;

        if (_rodMesh == null)
        {
            var location = Block?.Shape?.Base?.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            var shape = location == null ? null : Shape.TryGet(Api, location);
            if (shape == null)
                return;

            _rodMesh = util.CreateMesh("esolenoid", shape, out _rodShape, null);
        }

        if (_rodMesh == null)
            return;

        util.InitializeAnimator("esolenoid", _rodMesh, _rodShape, Vec3f.Zero);
        _rodReady = true;
    }

    private float[] FacingMatrix()
    {
        var rot = FacingRotations.Rotations.TryGetValue(Facing, out var value) ? value : Vec3f.Zero;
        var matrix = Mat4f.Create();
        Mat4f.Translate(matrix, matrix, 0.5f, 0.5f, 0.5f);
        Mat4f.RotateX(matrix, matrix, rot.X * GameMath.DEG2RAD);
        Mat4f.RotateY(matrix, matrix, rot.Y * GameMath.DEG2RAD);
        Mat4f.RotateZ(matrix, matrix, rot.Z * GameMath.DEG2RAD);
        Mat4f.Translate(matrix, matrix, -0.5f, -0.5f, -0.5f);
        return matrix;
    }

    private bool ClipActive(string code)
    {
        return AnimUtil?.activeAnimationsByAnimCode?.ContainsKey(code) == true;
    }

    private void StartClip(string code)
    {
        AnimUtil?.StartAnimation(new AnimationMetaData
        {
            Animation = code,
            Code = code,
            AnimationSpeed = 2f,
            EaseInSpeed = 10f,
            EaseOutSpeed = 10f
        });
    }

    private void StopClip(string code)
    {
        if (ClipActive(code))
            AnimUtil?.StopAnimation(code);
    }

    private void SetFrame(string code, float frame)
    {
        var animator = AnimUtil?.animator;
        var state = animator?.GetAnimationState(code);
        if (state != null)
            state.CurrentFrame = frame;
    }

}
