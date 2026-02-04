namespace Mud.Core;

/// <summary>
/// Methods the server can call on connected clients.
/// </summary>
public interface IGameClient
{
    Task OnWorldUpdate(WorldSnapshot snapshot);
    Task OnXpGain(List<XpGainEvent> xpEvents);
    Task OnProgressionUpdate(ProgressionUpdate progression);
}
