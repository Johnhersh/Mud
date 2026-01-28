using Mud.Shared;
using Mud.Shared.World;
using NoiseDotNet;

namespace Mud.Server.World.Generation;

/// <summary>
/// Generates noise maps for terrain generation using NoiseDotNet
/// </summary>
public static class NoiseGenerator
{
    /// <summary>
    /// Generates a noise map from a terrain seed
    /// </summary>
    public static NoiseMap GenerateNoise(this TerrainSeed seed, int ghostPadding)
    {
        int totalWidth = seed.Width + ghostPadding * 2;
        int totalHeight = seed.Height + ghostPadding * 2;
        int totalSize = totalWidth * totalHeight;

        // NoiseDotNet expects flat arrays of x/y coordinates
        var xCoords = new float[totalSize];
        var yCoords = new float[totalSize];
        for (int y = 0; y < totalHeight; y++)
        {
            for (int x = 0; x < totalWidth; x++)
            {
                int i = y * totalWidth + x;
                xCoords[i] = (x - ghostPadding) * WorldConfig.NoiseScale;
                yCoords[i] = (y - ghostPadding) * WorldConfig.NoiseScale;
            }
        }

        // Generate multi-octave noise
        var output = new float[totalSize];
        var octaveBuffer = new float[totalSize];
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int octave = 0; octave < WorldConfig.NoiseOctaves; octave++)
        {
            Noise.GradientNoise2D(xCoords, yCoords, octaveBuffer, frequency, frequency, amplitude, seed.Seed + octave);
            for (int i = 0; i < totalSize; i++)
                output[i] += octaveBuffer[i];

            maxValue += amplitude;
            amplitude *= WorldConfig.NoisePersistence;
            frequency *= WorldConfig.NoiseLacunarity;
        }

        // Find actual min/max for proper normalization
        float actualMin = float.MaxValue;
        float actualMax = float.MinValue;
        for (int i = 0; i < totalSize; i++)
        {
            if (output[i] < actualMin) actualMin = output[i];
            if (output[i] > actualMax) actualMax = output[i];
        }

        // Convert to 2D array and normalize to true 0-1 range using actual min/max
        float actualRange = actualMax - actualMin;
        var values = new float[totalWidth, totalHeight];
        for (int y = 0; y < totalHeight; y++)
        {
            for (int x = 0; x < totalWidth; x++)
            {
                int i = y * totalWidth + x;
                values[x, y] = (output[i] - actualMin) / actualRange;
            }
        }

        return new NoiseMap(values, seed.Width, seed.Height, seed.Seed, ghostPadding);
    }
}
