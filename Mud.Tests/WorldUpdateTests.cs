using Mud.Core;
using Mud.Server.Services;
using Mud.Server.World;

namespace Mud.Tests;

public class MovementTests
{
    [Test]
    public async Task Player_MovesToWalkableTile()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5));
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        var player = world.GetEntity("player1")!;
        await Assert.That(player.Position).IsEqualTo(new Point(6, 5));
    }

    [Test]
    public async Task Player_BlockedByWall_StaysAtOriginalPosition()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var walls = new HashSet<Point> { new(6, 5) };
        var world = TestHelpers.CreateWorld(walls: walls);
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5));
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        var player = world.GetEntity("player1")!;
        await Assert.That(player.Position).IsEqualTo(new Point(5, 5));
    }

    [Test]
    public async Task Player_EmptyQueue_DoesNotMove()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5));
        // No input enqueued

        world.UpdateWorld(state, cache);

        var player = world.GetEntity("player1")!;
        await Assert.That(player.Position).IsEqualTo(new Point(5, 5));
    }
}

public class CombatTests
{
    [Test]
    public async Task BumpAttack_DealsDamage_EqualTo5PlusStrength()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        int playerStrength = 7;
        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5), strength: playerStrength);
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: 50);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        int expectedDamage = ProgressionFormulas.MeleeDamage(playerStrength); // 5 + 7 = 12
        var monster = world.GetEntity("monster1")!;
        await Assert.That(monster.Health).IsEqualTo(50 - expectedDamage);
    }

    [Test]
    public async Task BumpAttack_PlayerStaysInPlace()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5));
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: 50);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        var player = world.GetEntity("player1")!;
        await Assert.That(player.Position).IsEqualTo(new Point(5, 5));
    }

    [Test]
    public async Task BumpAttack_RecordsAttackEvent()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5));
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: 50);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        await Assert.That(state.AttackEvents).ContainsKey(world.Id);
        var events = state.AttackEvents[world.Id].ToList();
        await Assert.That(events).Count().IsEqualTo(1);
        await Assert.That(events[0].AttackerId).IsEqualTo("player1");
        await Assert.That(events[0].IsMelee).IsTrue();
    }

    [Test]
    public async Task MonsterDeath_RemovedFromWorld()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        // Player with enough strength to one-shot the monster
        int strength = 50;
        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5), strength: strength);
        int monsterHp = 10;
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: monsterHp);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        var monster = world.GetEntity("monster1");
        await Assert.That(monster).IsNull();
    }
}

public class XpTests
{
    [Test]
    public async Task MonsterKill_RecordsXpEvent_ForAllPlayers()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        // Player 1 will kill the monster
        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5), strength: 50);
        // Player 2 is elsewhere in the same world
        TestHelpers.AddPlayer(world, state, cache, "player2", new Point(1, 1));
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: 10);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        // Both players should have XP events
        await Assert.That(state.XpEvents).ContainsKey(world.Id);
        var worldXpEvents = state.XpEvents[world.Id];
        await Assert.That(worldXpEvents).ContainsKey("player1");
        await Assert.That(worldXpEvents).ContainsKey("player2");
        await Assert.That(worldXpEvents["player1"][0].Amount).IsEqualTo(ProgressionFormulas.XpPerKill);
        await Assert.That(worldXpEvents["player2"][0].Amount).IsEqualTo(ProgressionFormulas.XpPerKill);
    }

    [Test]
    public async Task MonsterKill_RecordsProgressionUpdate_ForAllPlayers()
    {
        var state = new GameState();
        var cache = new TestCharacterCache();
        var world = TestHelpers.CreateWorld();
        state.Worlds[world.Id] = world;

        TestHelpers.AddPlayer(world, state, cache, "player1", new Point(5, 5), strength: 50);
        TestHelpers.AddPlayer(world, state, cache, "player2", new Point(1, 1));
        TestHelpers.AddMonster(world, "monster1", new Point(6, 5), health: 10);
        TestHelpers.EnqueueMove(state, "player1", Direction.Right);

        world.UpdateWorld(state, cache);

        await Assert.That(state.ProgressionUpdates).ContainsKey("player1");
        await Assert.That(state.ProgressionUpdates).ContainsKey("player2");
        await Assert.That(state.ProgressionUpdates["player1"].Experience).IsEqualTo(ProgressionFormulas.XpPerKill);
    }
}
