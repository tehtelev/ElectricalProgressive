using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EStove;

public class StoveContentsRenderer : IRenderer, IDisposable
{
    protected ICoreClientAPI capi;
    protected BlockPos Pos;
    public ItemStack ContentStack;
    public IInFirepitRenderer contentStackRenderer;

    public double RenderOrder => 0.5;
    public int RenderRange => 48;

    public StoveContentsRenderer(ICoreClientAPI capi, BlockPos pos)
    {
        this.capi = capi;
        this.Pos = pos;
    }

    public void SetChildRenderer(ItemStack contentStack, IInFirepitRenderer contentStackRenderer)
    {


        this.ContentStack = contentStack?.Clone();

        // Освобождаем старый рендерер
        this.contentStackRenderer?.Dispose();
        this.contentStackRenderer = contentStackRenderer;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        contentStackRenderer?.OnRenderFrame(deltaTime, stage);
    }

    public void OnUpdate(float temp)
    {
        contentStackRenderer?.OnUpdate(temp);
    }

    public void Dispose()
    {
        capi?.Event?.UnregisterRenderer(this, EnumRenderStage.Opaque);
        contentStackRenderer?.Dispose();
        contentStackRenderer = null;
    }
}