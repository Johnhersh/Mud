using System.Collections.Concurrent;
using Mud.Core;
using Mud.Core.Services;
using Mud.Core.World;
using Mud.Server.Services;
using Mud.Server.World;

namespace Mud.Tests;

/// <summary>
/// Helpers for creating minimal game objects in tests.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Create a small walkable world with optional wall positions.
    /// Default: 10x10, all tiles walkable.
    /// </summary>
    public static WorldState CreateWorld(int width = 10, int height = 10, HashSet<Point>? walls = null)
    {
        var tiles = new Tile[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            var isWall = walls?.Contains(new Point(x, y)) ?? false;
            tiles[x, y] = new Tile(TileType.GrassSparse, Walkable: !isWall);
        }

        var terrain = new TileMap(tiles, width, height);
        return new WorldState
        {
            Id = new WorldId("test_world"),
            Type = WorldType.Instance,
            Terrain = terrain
        };
    }

    /// <summary>
    /// Create a player entity and register it in the world, GameState, and character cache.
    /// </summary>
    public static Entity AddPlayer(
        WorldState world,
        GameState state,
        TestCharacterCache cache,
        string connectionId,
        Point position,
        int health = 100,
        int strength = 5,
        int dexterity = 5,
        int stamina = 5,
        int level = 1,
        int experience = 0)
    {
        var characterId = new CharacterId(Guid.NewGuid());

        var entity = new Entity
        {
            Id = connectionId,
            Name = "TestPlayer",
            Position = position,
            QueuedPath = new List<Point>(),
            Type = EntityType.Player,
            Health = health,
            MaxHealth = ProgressionFormulas.MaxHealth(stamina),
            Level = level
        };
        world.AddEntity(entity);

        var session = new PlayerSession
        {
            ConnectionId = connectionId,
            AccountId = new AccountId("test_account"),
            CharacterId = characterId,
            Name = "TestPlayer",
            CurrentWorldId = world.Id,
            Position = position
        };
        state.Sessions[connectionId] = session;

        cache.Set(characterId, new CharacterProgression
        {
            Level = level,
            Experience = experience,
            Strength = strength,
            Dexterity = dexterity,
            Stamina = stamina,
            UnspentPoints = 0
        });

        return entity;
    }

    /// <summary>
    /// Create a monster entity and add it to the world.
    /// </summary>
    public static Entity AddMonster(WorldState world, string id, Point position, int health = 50)
    {
        var monster = new Entity
        {
            Id = id,
            Name = "Goblin",
            Position = position,
            QueuedPath = new List<Point>(),
            Type = EntityType.Monster,
            Health = health,
            MaxHealth = health,
            Level = 1
        };
        world.AddEntity(monster);
        return monster;
    }

    /// <summary>
    /// Enqueue a movement direction for a player.
    /// </summary>
    public static void EnqueueMove(GameState state, string connectionId, Direction direction)
    {
        var queue = state.PlayerInputQueues.GetOrAdd(connectionId, _ => new ConcurrentQueue<Direction>());
        queue.Enqueue(direction);
    }
}
