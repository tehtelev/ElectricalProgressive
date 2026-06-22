namespace ElectricalProgressive.Content;

public interface IPipeRenderState
{
    bool[] ConnectedSides { get; }

    bool[] ConnectedToInventory { get; }

    bool UseInserterHead { get; }
}