using Mud.Shared;

namespace Mud.Client.Rendering;

/// <summary>
/// Tracks the rendering state of an entity for change detection.
/// </summary>
public class EntityRenderState
{
    public string Id { get; }
    public Point Position { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; }
    public EntityType Type { get; }

    public EntityRenderState(Entity entity)
    {
        Id = entity.Id;
        Position = entity.Position;
        Health = entity.Health;
        MaxHealth = entity.MaxHealth;
        Type = entity.Type;
    }

    public void Update(Entity entity)
    {
        Position = entity.Position;
        Health = entity.Health;
    }
}
