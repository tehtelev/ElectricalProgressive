namespace ElectricalProgressive.Content;

public interface IPipeRenderState
{
    string CurrentPipeType { get; }

    string GetBaseBlockCode();
}
