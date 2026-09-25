using System;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Generation
{
    [Serializable]
    public sealed class WorldGenerationSettings
    {
        public int Seed = 12345;
        public float NoiseScale = 72f;
        public int Octaves = 4;
        public float Persistence = 0.5f, Lacunarity = 2f;
        public float WaterThreshold = 0.30f, DirtThreshold = 0.40f, StoneThreshold = 0.72f;
        public float TreeDensity = 0.12f, StoneRockDensity = 0.35f, DirtRockDensity = 0.025f;
        public int WaterId = 2, DirtId = 0, GrassId = 3, StoneId = 4;

        public void Validate()
        {
            if (!(NoiseScale >= 1f && NoiseScale <= 4096f) || Octaves < 1 || Octaves > 8
                || !(Persistence > 0 && Persistence <= 1) || !(Lacunarity >= 1 && Lacunarity <= 4)
                || !(WaterThreshold >= 0 && WaterThreshold < DirtThreshold && DirtThreshold < StoneThreshold && StoneThreshold <= 1)
                || !Probability(TreeDensity) || !Probability(StoneRockDensity) || !Probability(DirtRockDensity))
                throw new ArgumentException("Invalid generation scale, octaves, thresholds, or density.");
            if (WaterId == DirtId || WaterId == GrassId || WaterId == StoneId || DirtId == GrassId || DirtId == StoneId || GrassId == StoneId)
                throw new ArgumentException("Terrain IDs must be distinct.");
        }
        private static bool Probability(float value) => value >= 0 && value <= 1;
    }

    public sealed class GeneratedWorld
    {
        public readonly WorldGrid Grid;
        public readonly float[,] Heightmap;
        public readonly int SpawnCell;
        internal GeneratedWorld(WorldGrid grid, float[,] heightmap, int spawnCell)
        { Grid = grid; Heightmap = heightmap; SpawnCell = spawnCell; }
    }

    public static class WorldGenerator
    {
        public const int Size = 256;

        public static GeneratedWorld Generate(WorldGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            var noise = new SimplexNoise(settings.Seed);
            var heights = new float[Size, Size];
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float value = 0, amplitude = 1, frequency = 1 / settings.NoiseScale;
                for (int octave = 0; octave < settings.Octaves; octave++)
                {
                    value += noise.Sample((x + 0.5f) * frequency, (y + 0.5f) * frequency) * amplitude;
                    amplitude *= settings.Persistence;
                    frequency *= settings.Lacunarity;
                }
                heights[x, y] = value;
                min = Math.Min(min, value); max = Math.Max(max, value);
            }

            var grid = new WorldGrid(Size, Size);
            float range = max - min;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float sample = heights[x, y];
                float h = range <= 0 ? 0.5f : sample <= min ? 0f : sample >= max ? 1f
                    : Math.Max(0f, Math.Min(1f, (sample - min) / range));
                heights[x, y] = h;
                int terrain = h < settings.WaterThreshold ? settings.WaterId
                    : h < settings.DirtThreshold ? settings.DirtId
                    : h < settings.StoneThreshold ? settings.GrassId : settings.StoneId;
                grid.SetGeneratedCell(grid.IndexOf(x, y), terrain, terrain != settings.WaterId, false, false);
            }

            // Independent deterministic stream: props never change the heightmap.
            uint state = unchecked((uint)settings.Seed) ^ 0xA511E9B3u;
            for (int i = 0; i < grid.CellCount; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float roll = (state >> 8) * (1f / 16777216f);
                Cell cell = grid.GetCell(i);
                bool tree = cell.TerrainTypeId == settings.GrassId && roll < settings.TreeDensity;
                bool rock = (cell.TerrainTypeId == settings.StoneId && roll < settings.StoneRockDensity)
                    || (cell.TerrainTypeId == settings.DirtId && roll < settings.DirtRockDensity);
                grid.SetGeneratedCell(i, cell.TerrainTypeId, cell.IsWalkable, tree, rock);
            }
            grid.CompleteGeneration();

            // Choose the largest land region, then its nearest clear cell to map centre.
            var counts = new int[grid.CellCount];
            int largest = -1;
            for (int i = 0; i < grid.CellCount; i++)
            {
                int region = grid.GetCell(i).ConnectivityId;
                if (region < 0) continue;
                counts[region]++;
                if (largest < 0 || counts[region] > counts[largest]) largest = region;
            }
            int spawn = -1, best = int.MaxValue;
            for (int i = 0; i < grid.CellCount; i++)
            {
                Cell cell = grid.GetCell(i);
                if (!cell.IsWalkable || cell.ConnectivityId != largest) continue;
                int distance = Math.Abs(i % Size - Size / 2) + Math.Abs(i / Size - Size / 2);
                if (distance >= best) continue;
                best = distance; spawn = i;
            }
            if (spawn < 0) throw new InvalidOperationException("Settings produced no walkable land; reduce water or rock density.");
            grid.SetHasTree(spawn, false);
            return new GeneratedWorld(grid, heights, spawn);
        }
    }
}
