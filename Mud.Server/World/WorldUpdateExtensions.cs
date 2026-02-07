using System.Collections.Concurrent;
using Mud.Core;
using Mud.Core.Services;
using Mud.Server.Services;

namespace Mud.Server.World;

/// <summary>
/// Game logic that operates on WorldState + GameState, extracted from GameLoopService
/// so it can be called directly from tests without SignalR, persistence, or tick timing.
/// </summary>
public static class WorldUpdateExtensions
{
    extension(WorldState world)
    {
        /// <summary>
        /// Process one tick of game logic for this world: dequeue player inputs,
        /// handle movement/collision/combat, update queued paths.
        /// </summary>
        public void UpdateWorld(GameState state, ICharacterCache cache)
        {
            var players = world.GetPlayers().ToList();
            foreach (var playerEntity in players)
            {
                var connectionId = playerEntity.Id;
                if (!state.PlayerInputQueues.TryGetValue(connectionId, out var queue) || !queue.TryDequeue(out var direction))
                    continue;

                var player = world.GetEntity(playerEntity.Id);
                if (player == null) continue;

                var newPos = direction switch
                {
                    Direction.Up => player.Position with { Y = player.Position.Y - 1 },
                    Direction.Down => player.Position with { Y = player.Position.Y + 1 },
                    Direction.Left => player.Position with { X = player.Position.X - 1 },
                    Direction.Right => player.Position with { X = player.Position.X + 1 },
                    _ => player.Position
                };

                // Check for monster at destination
                var target = world.Entities.Values.FirstOrDefault(e => e.Position == newPos && e.Type == EntityType.Monster);
                if (target != null)
                {
                    world.ProcessAttack(state, playerEntity.Id, target.Id, isMelee: true, cache);
                    // Clear queue on attack
                    while (queue.TryDequeue(out _)) ;
                    // Re-fetch player after ProcessAttack since it may have been updated with XP
                    var updatedPlayer = world.GetEntity(playerEntity.Id);
                    if (updatedPlayer != null)
                    {
                        world.UpdateEntity(updatedPlayer with { QueuedPath = new List<Point>() });
                    }
                    continue;
                }

                if (world.IsWalkable(newPos))
                {
                    // Update position and recalculate queued path for the client
                    var currentPos = newPos;
                    var queuedPath = new List<Point>();
                    foreach (var queuedDir in queue)
                    {
                        currentPos = queuedDir switch
                        {
                            Direction.Up => currentPos with { Y = currentPos.Y - 1 },
                            Direction.Down => currentPos with { Y = currentPos.Y + 1 },
                            Direction.Left => currentPos with { X = currentPos.X - 1 },
                            Direction.Right => currentPos with { X = currentPos.X + 1 },
                            _ => currentPos
                        };

                        if (!world.IsWalkable(currentPos)) break;
                        // Also break if there's a monster
                        if (world.Entities.Values.Any(e => e.Position == currentPos && e.Type == EntityType.Monster)) break;

                        queuedPath.Add(currentPos);
                    }

                    world.UpdateEntity(player with
                    {
                        Position = newPos,
                        QueuedPath = queuedPath
                    });

                    // Update session position
                    if (state.Sessions.TryGetValue(connectionId, out var session))
                    {
                        session.Position = newPos;
                    }
                }
                else
                {
                    // Blocked: clear the queue
                    while (queue.TryDequeue(out _)) ;
                    world.UpdateEntity(player with { QueuedPath = new List<Point>() });
                }
            }
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
                if (progression != null)
                {
                    return (progression.Strength, progression.Dexterity);
                }
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
    }
}
