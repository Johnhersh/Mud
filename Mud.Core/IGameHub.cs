namespace Mud.Core;

/// <summary>
/// Methods the client can call on the server hub.
/// </summary>
public interface IGameHub
{
    Task Join(string name);
    Task Move(Direction direction);
    Task RangedAttack(string targetId);
    Task Interact();
    Task AllocateStat(StatType stat);
}
