namespace Mud.Shared.World;

/// <summary>
/// Configuration constants for world generation
/// </summary>
public static class WorldConfig
{
    // World seed
    public const int WorldSeed = 12345;

    // Overworld dimensions
    public const int OverworldWidth = 150;
    public const int OverworldHeight = 150;

    // Instance dimensions
    public const int InstanceWidth = 50;
    public const int InstanceHeight = 50;

    // Ghost chunk padding (visible but impassable)
    public const int OverworldGhostPadding = 20;
    public const int InstanceGhostPadding = 5;

    // Biome thresholds (noise value ranges)
    public const float WaterThreshold = 0.3f;
    public const float PlainsThreshold = 0.6f;
    // Forest is >= PlainsThreshold

    // Noise generation parameters
    public const float NoiseScale = 0.05f;
    public const int NoiseOctaves = 4;
    public const float NoisePersistence = 0.5f;
    public const float NoiseLacunarity = 2.0f;

    // POI generation
    public const float POIDensity = 0.02f; // 2% of walkable tiles
    public const int MinPOIDistance = 10; // Minimum tiles between POIs

    // Influence radii
    public const float TownInfluenceRadius = 15f;
    public const float CampInfluenceRadius = 5f;
    public const float ExitInfluenceRadius = 3f;

    // River generation
    public const int RiverCostWater = 1;
    public const int RiverCostPlains = 5;
    public const int RiverCostForest = 10;
}
