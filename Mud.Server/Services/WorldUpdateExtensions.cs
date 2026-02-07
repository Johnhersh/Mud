using System.Collections.Concurrent;
using Mud.Core;
using Mud.Core.Services;
using Mud.Core.World;
using Mud.Server.World;
using Mud.Server.World.Generation;

namespace Mud.Server.Services;

/// <summary>
/// Game logic and infrastructure behavior that operates on WorldState.
/// Separated from WorldState (pure data) so game rules and serialization
/// don't live on the data container itself.
/// </summary>
public static class WorldUpdateExtensions
{
    extension(WorldState world)
    {
        /// <summary>
        /// Process one tick of game logic for this world: dequeue one input per player
        /// and resolve movement, combat, or collision.
        /// </summary>
        public void UpdateWorld(GameState state, ICharacterCache cache)
        {
            foreach (var player in world.GetPlayers().ToList())
            {
                if (!state.PlayerInputQueues.TryGetValue(player.Id, out var queue)
                    || !queue.TryDequeue(out var direction))
                    continue;

                world.ProcessPlayerInput(state, player, queue, direction, cache);
            }
        }

        /// <summary>
        /// Resolve a single player's movement input: bump attack if monster,
        /// move if walkable, or block if wall.
        /// </summary>
        public void ProcessPlayerInput(GameState state, Entity player, ConcurrentQueue<Direction> queue, Direction direction, ICharacterCache cache)
        {
            var newPos = player.Position.Adjacent(direction);
            var monster = world.Entities.Values.FirstOrDefault(e => e.Position == newPos && e.Type == EntityType.Monster);

            if (monster is not null)
            {
                // Bump attack — hit the monster, clear remaining queued moves
                world.ProcessAttack(state, player.Id, monster.Id, isMelee: true, cache);
                while (queue.TryDequeue(out _)) ;
                // Re-fetch player after ProcessAttack since XP may have updated entity
                var updatedPlayer = world.GetEntity(player.Id);
                if (updatedPlayer is not null) world.UpdateEntity(updatedPlayer with { QueuedPath = [] });
            }
            else if (world.IsWalkable(newPos))
            {
                // Move — update position and project queued path for the client
                var queuedPath = world.ProjectQueuedPath(newPos, queue);

                world.UpdateEntity(player with
                {
                    Position = newPos,
                    QueuedPath = queuedPath
                });

                if (state.Sessions.TryGetValue(player.Id, out var session))
                {
                    session.Position = newPos;
                }
            }
            else
            {
                // Blocked by wall — clear queue, stay in place
                while (queue.TryDequeue(out _)) ;
                world.UpdateEntity(player with { QueuedPath = new List<Point>() });
            }
        }

        /// <summary>
        /// Project where queued moves would take the player, stopping at walls or monsters.
        /// Used for rendering transparent queued-path tiles on the client.
        /// </summary>
        public List<Point> ProjectQueuedPath(Point startPos, ConcurrentQueue<Direction> queue)
        {
            var currentPos = startPos;
            var path = new List<Point>();

            foreach (var queuedDir in queue)
            {
                currentPos = currentPos.Adjacent(queuedDir);
                if (!world.IsWalkable(currentPos)) break;
                if (world.Entities.Values.Any(e => e.Position == currentPos && e.Type == EntityType.Monster)) break;
                path.Add(currentPos);
            }

            return path;
        }

        /// <summary>
        /// Process an attack between two entities. Handles damage calculation,
        /// entity death, XP awards for monster kills, and attack event recording.
        /// </summary>
        public void ProcessAttack(GameState state, string attackerId, string targetId, bool isMelee, ICharacterCache cache)
        {
            var attacker = world.GetEntity(attackerId);
            var target = world.GetEntity(targetId);
            if (attacker is null || target is null) return;

            var (strength, dexterity) = world.GetAttackStats(attacker, state, cache);

            int damage = isMelee
                ? ProgressionFormulas.MeleeDamage(strength)
                : ProgressionFormulas.RangedDamage(dexterity);

            var targetPosition = target.Position;
            var newHealth = target.Health - damage;

            if (newHealth <= 0)
            {
                world.RemoveEntity(targetId);

                // Award XP to all players in this instance if a monster was killed
                if (target.Type == EntityType.Monster && attacker.Type == EntityType.Player)
                {
                    world.AwardXpToInstance(state, targetPosition, cache);
                }
            }
            else
            {
                world.UpdateEntity(target with { Health = newHealth });
            }

            // Record attack event for client animation
            var attackEvent = new AttackEvent(attackerId, targetId, damage, isMelee, targetPosition);
            var attackBag = state.AttackEvents.GetOrAdd(world.Id, _ => []);
            attackBag.Add(attackEvent);
        }

        /// <summary>
        /// Get attack stats for an entity. Uses MonsterStats for monsters, ICharacterCache for players.
        /// </summary>
        public (int Strength, int Dexterity) GetAttackStats(Entity attacker, GameState state, ICharacterCache cache)
        {
            if (attacker.Type == EntityType.Monster)
            {
                return (MonsterStats.GetStrength(attacker.Name), MonsterStats.GetDexterity(attacker.Name));
            }

            // For players, get from cache
            if (state.Sessions.TryGetValue(attacker.Id, out var session))
            {
                var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
                if (progression is not null) return (progression.Strength, progression.Dexterity);
            }

            // Fallback to base stats
            return (ProgressionFormulas.BaseStrength, ProgressionFormulas.BaseDexterity);
        }

        /// <summary>
        /// Award XP to all players in this world when a monster is killed.
        /// Handles level-ups, health updates, and queues progression/persistence events.
        /// </summary>
        public void AwardXpToInstance(GameState state, Point killedPosition, ICharacterCache cache)
        {
            var players = world.GetPlayers().ToList();

            foreach (var player in players)
            {
                if (!state.Sessions.TryGetValue(player.Id, out var session)) continue;

                var progression = cache.GetProgressionAsync(session.CharacterId).GetAwaiter().GetResult();
                if (progression == null) continue;

                // Skip players at max level
                if (progression.Level >= ProgressionFormulas.MaxLevel) continue;

                var newXp = progression.Experience + ProgressionFormulas.XpPerKill;
                var newLevel = progression.Level;
                var newUnspent = progression.UnspentPoints;
                var newHealth = player.Health;
                var leveledUp = false;

                // Check for level up(s) - loop handles multiple level-ups from large XP gains
                while (newLevel < ProgressionFormulas.MaxLevel &&
                       newXp >= ProgressionFormulas.ExperienceForLevel(newLevel + 1))
                {
                    newLevel++;
                    newUnspent += ProgressionFormulas.PointsPerLevel;
                    leveledUp = true;
                }

                // Cap XP at max level threshold
                if (newLevel >= ProgressionFormulas.MaxLevel)
                {
                    newXp = ProgressionFormulas.ExperienceForLevel(ProgressionFormulas.MaxLevel);
                }

                // Full heal on level up
                var newMaxHealth = ProgressionFormulas.MaxHealth(progression.Stamina);
                if (leveledUp)
                {
                    newHealth = newMaxHealth;
                    world.RecordLevelUpEvent(state, player.Id, newLevel, player.Position);
                }

                // Record XP gain event for this player
                world.RecordXpGainEvent(state, player.Id, ProgressionFormulas.XpPerKill, killedPosition);

                // Update entity (only volatile fields: Level, Health, MaxHealth)
                var updatedEntity = player with
                {
                    Level = newLevel,
                    Health = newHealth,
                    MaxHealth = newMaxHealth
                };
                world.UpdateEntity(updatedEntity);

                // Record progression update for this player
                var progressionUpdate = new ProgressionUpdate(
                    newLevel,
                    newXp,
                    progression.Strength,
                    progression.Dexterity,
                    progression.Stamina,
                    newUnspent,
                    newMaxHealth
                );
                state.ProgressionUpdates[player.Id] = progressionUpdate;
            }
        }

        public void RecordXpGainEvent(GameState state, string connectionId, int amount, Point position)
        {
            var playerXpEvents = state.XpEvents.GetOrAdd(world.Id, _ => new ConcurrentDictionary<string, List<XpGainEvent>>());
            var xpList = playerXpEvents.GetOrAdd(connectionId, _ => []);
            lock (xpList)
            {
                xpList.Add(new XpGainEvent(amount, position));
            }
        }

        public void RecordLevelUpEvent(GameState state, string playerId, int newLevel, Point position)
        {
            var levelUpBag = state.LevelUpEvents.GetOrAdd(world.Id, _ => []);
            levelUpBag.Add(new LevelUpEvent(playerId, newLevel, position));
        }

        /// <summary>
        /// Find a random walkable position not occupied by any entity.
        /// </summary>
        public Point? FindRandomWalkablePosition(Random random, int maxAttempts = 100)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                var pos = new Point(
                    random.Next(0, world.Terrain.Width),
                    random.Next(0, world.Terrain.Height)
                );

                if (world.IsWalkable(pos) && !world.Entities.Values.Any(e => e.Position == pos))
                {
                    return pos;
                }
            }
            return null;
        }

        /// <summary>
        /// Convert to WorldSnapshot for network transmission.
        /// </summary>
        public WorldSnapshot ToSnapshot(long tick, bool includeTiles, List<AttackEvent> attackEvents, List<LevelUpEvent>? levelUpEvents = null)
        {
            return new WorldSnapshot
            {
                Tick = tick,
                WorldId = world.Id.Value,
                WorldType = world.Type,
                Entities = world.Entities.Values.ToList(),
                Tiles = includeTiles ? world.Terrain.ToTileDataArray() : null,
                POIs = world.POIs,
                ExitMarker = world.ExitMarker,
                Width = world.Terrain.Width,
                Height = world.Terrain.Height,
                GhostPadding = world.Terrain.GhostPadding,
                AttackEvents = attackEvents,
                LevelUpEvents = levelUpEvents ?? []
            };
        }
    }
}
