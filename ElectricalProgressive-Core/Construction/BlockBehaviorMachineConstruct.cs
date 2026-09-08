using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Блок-поведение: ПКМ / подсказки / дроп материалов для MachineConstruct.
/// Работает с любого блока multiblock (dummy → контроллер).
/// Важно: GetPlacedBlockInteractionHelp НЕ должен возвращать null —
/// base.GetPlacedBlockInteractionHelpCount делает .Length и падает с NRE.
/// </summary>
public class BlockBehaviorMachineConstruct : BlockBehavior
{
    public BlockBehaviorMachineConstruct(Block block) : base(block)
    {
    }

    private static BEBehaviorMachineConstruct? GetBeh(IWorldAccessor world, BlockPos pos)
    {
        return MachineConstructAccess.GetBehavior(world, pos);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ref EnumHandling handling)
    {
        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, blockSel.Position))
        {
            handling = EnumHandling.PreventSubsequent;
            return true;
        }

        return false;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection,
        IPlayer forPlayer, ref EnumHandling handling)
    {
        var beh = GetBeh(world, selection.Position);
        if (beh == null || !beh.HasConstruction || beh.IsReady)
            return Array.Empty<WorldInteraction>();

        var help = beh.GetInteractionHelp(world, forPlayer);
        if (help is { Length: > 0 })
        {
            handling = EnumHandling.PreventDefault;
            return help;
        }

        return Array.Empty<WorldInteraction>();
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        ref float dropChanceMultiplier, ref EnumHandling handling)
    {
        var beh = GetBeh(world, pos);
        if (beh == null || !beh.HasConstruction)
            return null!; // null = «не перехватываем», пусть идут дефолтные дропы

        // PreventSubsequent (не PreventDefault!): HorizontalOrientable по умолчанию
        // handleDrop=true и тоже кладёт блок в дроп → иначе 2 контроллера.
        handling = EnumHandling.PreventSubsequent;
        var drops = new List<ItemStack>();

        // В креативе мир обычно и так не спавнит дропы; материалы не отдаём
        var creative = byPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

        var controller = beh.GetIncompleteControllerStack();
        if (controller != null)
            drops.Add(controller);

        if (!creative)
        {
            foreach (var mat in beh.GetMaterialDrops())
            {
                if (mat is { StackSize: > 0 })
                    drops.Add(mat);
            }
        }

        return drops.ToArray();
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
    {
        var beh = GetBeh(world, pos);
        if (beh == null || !beh.HasConstruction)
            return null!;

        var stack = beh.GetIncompleteControllerStack();
        if (stack != null)
        {
            // Тоже обрываем цепочку — HO.OnPickBlock иначе подменит стейк
            handling = EnumHandling.PreventSubsequent;
            return stack;
        }

        return null!;
    }

    public override string GetHeldTpIdleAnimation(ItemSlot activeHotbarSlot, Entity forEntity, EnumHand hand,
        ref EnumHandling handling)
    {
        if (!MachineConstructSystem.HasConstructionLevels(block))
            return null!;

        handling = EnumHandling.PreventDefault;
        return "holdbothhands";
    }

    public override string GetHeldReadyAnimation(ItemSlot activeHotbarSlot, Entity forEntity, EnumHand hand,
        ref EnumHandling handling)
    {
        if (!MachineConstructSystem.HasConstructionLevels(block))
            return null!;

        handling = EnumHandling.PreventDefault;
        return "holdbothhands";
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        if (target != EnumItemRenderTarget.HandTp)
            return;

        if (!MachineConstructSystem.HasConstructionLevels(block))
            return;

        var mesh = GetHeldPaperMesh(capi);
        if (mesh == null)
            return;

        renderinfo.ModelRef = mesh;
        renderinfo.Transform = OpenBookHandTransform.Clone();
    }

    private static MultiTextureMeshRef? _paperMesh;
    private static bool _paperFailed;

    // Vanilla clutter book-big-open + holdbothhands (clutter.json tpTf).
    private static readonly ModelTransform OpenBookHandTransform = new()
    {
        Translation = new Vec3f(-1.5f, -0.9f, -0.57f),
        Rotation = new Vec3f(121f, -29f, -65f),
        Origin = new Vec3f(0.5f, 0.5f, 0.5f),
        Scale = 0.53f
    };

    private static MultiTextureMeshRef? GetHeldPaperMesh(ICoreClientAPI capi)
    {
        if (_paperMesh != null && !_paperMesh.Disposed)
            return _paperMesh;
        if (_paperFailed)
            return null;

        try
        {
            var mesh = TessellateOpenBook(capi);
            if (mesh == null || mesh.VerticesCount <= 0)
            {
                _paperFailed = true;
                capi.Logger.Warning("[MachineConstruct] book-big-open shape missing");
                return null;
            }

            _paperMesh = capi.Render.UploadMultiTextureMesh(mesh);
            return _paperMesh;
        }
        catch (Exception ex)
        {
            _paperFailed = true;
            capi.Logger.Error("[MachineConstruct] held open-book mesh: {0}", ex);
            return null;
        }
    }

    private static MeshData? TessellateOpenBook(ICoreClientAPI capi)
    {
        var locs = new[]
        {
            new AssetLocation("game", "shapes/block/clutter/book-big-open.json"),
            new AssetLocation("game", "block/clutter/book-big-open")
        };

        foreach (var loc in locs)
        {
            var shape = Shape.TryGet(capi, loc);
            if (shape == null)
                continue;

            capi.Tesselator.TesselateShape("ep-held-openbook", shape, out var mesh,
                new ShapeTextureSource(capi, shape, "ep-held-openbook"));
            if (mesh is { VerticesCount: > 0 })
                return mesh;
        }

        return null;
    }
}
