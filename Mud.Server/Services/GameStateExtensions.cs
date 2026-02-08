using Mud.Core;
using Mud.Core.World;
using Mud.Server.World;

namespace Mud.Server.Services;

/// <summary>
/// Game logic that operates on GameState (cross-world operations).
/// Separated from GameState (pure data) so game rules don't live on the data container.
/// </summary>
public static class GameStateExtensions
{
    extension(GameState state)
    {
        /// <summary>
        /// Remove a player from the game: session, world entity, input queue.
        /// Cleans up empty instances.
        /// </summary>
        public void RemovePlayer(string connectionId)
        {
            if (state.Sessions.TryRemove(connectionId, out var session))
            {
                if (state.Worlds.TryGetValue(session.CurrentWorldId, out var world))
                {
                    world.RemoveEntity(connectionId);

                    if (world.Type == WorldType.Instance && !world.GetPlayers().Any())
                    {
                        state.Worlds.TryRemove(world.Id, out _);
                    }
                }
            }
            state.PlayerInputQueues.TryRemove(connectionId, out _);
        }
    }
}
