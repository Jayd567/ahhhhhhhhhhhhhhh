using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Generation
{
    // Blittable copy of the fields of WorldGenerationSettings that the Burst jobs below need.
    // WorldGenerationSettings itself is a managed class (see EcsWorldGenerator.cs) and cannot be
    // stored in a job struct field directly ("Job structs may not contain any reference types."),
    // so EcsWorldGenerator.Generate copies it into this struct before scheduling.
    public struct GenerationParams
    {
        public int Seed;
        public float NoiseScale;
        public int Octaves;
        public float Persistence, Lacunarity;
        public float WaterThreshold, DirtThreshold, StoneThreshold;
        public float TreeDensity, StoneRockDensity, DirtRockDensity;
        public int WaterId, DirtId, GrassId, StoneId;

        public static GenerationParams From(WorldGenerationSettings settings) => new GenerationParams
        {
            Seed = settings.Seed,
            NoiseScale = settings.NoiseScale,
            Octaves = settings.Octaves,
            Persistence = settings.Persistence,
            Lacunarity = settings.Lacunarity,
            WaterThreshold = settings.WaterThreshold,
            DirtThreshold = settings.DirtThreshold,
            StoneThreshold = settings.StoneThreshold,
            TreeDensity = settings.TreeDensity,
            StoneRockDensity = settings.StoneRockDensity,
            DirtRockDensity = settings.DirtRockDensity,
            WaterId = settings.WaterId,
            DirtId = settings.DirtId,
            GrassId = settings.GrassId,
            StoneId = settings.StoneId,
        };
    }

    [BurstCompile]
    public struct GenerateHeightmapJob : IJob
    {
        public EcsSimplexNoise Noise;
        public GenerationParams Settings;
        public NativeArray<float> Heights; // Size*Size, row-major
        public int Size;

        public void Execute()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float value = 0, amplitude = 1, frequency = 1f / Settings.NoiseScale;
                for (int octave = 0; octave < Settings.Octaves; octave++)
                {
                    value += Noise.Sample((x + 0.5f) * frequency, (y + 0.5f) * frequency) * amplitude;
                    amplitude *= Settings.Persistence;
                    frequency *= Settings.Lacunarity;
                }
                int idx = y * Size + x;
                Heights[idx] = value;
                if (value < min) min = value;
                if (value > max) max = value;
            }
            float range = max - min;
            for (int i = 0; i < Heights.Length; i++)
            {
                float sample = Heights[i];
                Heights[i] = range <= 0 ? 0.5f : sample <= min ? 0f : sample >= max ? 1f
                    : math.max(0f, math.min(1f, (sample - min) / range));
            }
        }
    }

    [BurstCompile]
    public struct TerrainAndPropsJob : IJob
    {
        [ReadOnly] public NativeArray<float> Heights;
        public GenerationParams Settings;
        public NativeArray<CellElement> Cells;

        public void Execute()
        {
            for (int i = 0; i < Heights.Length; i++)
            {
                float h = Heights[i];
                int terrain = h < Settings.WaterThreshold ? Settings.WaterId
                    : h < Settings.DirtThreshold ? Settings.DirtId
                    : h < Settings.StoneThreshold ? Settings.GrassId : Settings.StoneId;
                bool walkable = terrain != Settings.WaterId;
                Cells[i] = new CellElement { Value = new CellData { TerrainId = (ushort)terrain, Walkable = walkable, HasRock = false } };
            }

            uint state = unchecked((uint)Settings.Seed) ^ 0xA511E9B3u;
            for (int i = 0; i < Cells.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float roll = (state >> 8) * (1f / 16777216f);
                CellData cell = Cells[i].Value;
                bool rock = (cell.TerrainId == Settings.StoneId && roll < Settings.StoneRockDensity)
                    || (cell.TerrainId == Settings.DirtId && roll < Settings.DirtRockDensity);
                if (rock) { cell.Walkable = false; cell.HasRock = true; }
                // Note: the current plain-C# WorldGenerator also rolls a separate tree flag here
                // from the same `roll` value; tree placement is Presentation-adjacent job-target
                // data (HasTree lived on Cell), and per this migration's Data model (spec
                // §Grid), trees are represented as job-generating entities, not a CellData flag.
                // Tree entity spawning happens in EcsWorldGenerator.Generate after this job, not
                // inside it, to keep this job free of EntityManager/EntityCommandBuffer access
                // (Burst jobs can't touch EntityManager directly).
                Cells[i] = new CellElement { Value = cell };
            }
        }
    }
}
