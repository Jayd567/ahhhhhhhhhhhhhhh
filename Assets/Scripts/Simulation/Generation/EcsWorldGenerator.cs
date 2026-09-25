using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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

    public static class EcsWorldGenerator
    {
        public const int Size = 256;

        public static int Generate(EntityManager em, Entity grid, WorldGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            GenerationParams jobParams = GenerationParams.From(settings);

            var heights = new NativeArray<float>(Size * Size, Allocator.TempJob);
            var heightJob = new GenerateHeightmapJob { Noise = new EcsSimplexNoise(settings.Seed), Settings = jobParams, Heights = heights, Size = Size };
            heightJob.Schedule().Complete();

            var cells = new NativeArray<CellElement>(Size * Size, Allocator.TempJob);
            var terrainJob = new TerrainAndPropsJob { Heights = heights, Settings = jobParams, Cells = cells };
            terrainJob.Schedule().Complete();

            DynamicBuffer<CellElement> gridCells = em.GetBuffer<CellElement>(grid);
            gridCells.ResizeUninitialized(cells.Length);
            for (int i = 0; i < cells.Length; i++) gridCells[i] = cells[i];
            em.SetComponentData(grid, new GridRevision { Value = em.GetComponentData<GridRevision>(grid).Value + 1 });

            // Trees: spawned as entities from the same deterministic roll used for rocks,
            // recomputed here (cheap: one pass) since GenerateHeightmapJob's job can't touch
            // EntityManager. Uses the same LCG stream/seed offset as TerrainAndPropsJob so
            // roll values line up per cell.
            uint state = unchecked((uint)settings.Seed) ^ 0xA511E9B3u;
            for (int i = 0; i < cells.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float roll = (state >> 8) * (1f / 16777216f);
                CellData cell = gridCells[i].Value;
                if (cell.TerrainId == settings.GrassId && !cell.HasRock && roll < settings.TreeDensity)
                {
                    Entity tree = em.CreateEntity(typeof(TreeTag), typeof(TreeCellIndex));
                    em.SetComponentData(tree, new TreeCellIndex { Value = i });
                }
            }

            heights.Dispose();
            cells.Dispose();

            var connectivity = new ConnectivitySystem();
            // ConnectivitySystem is an ISystem struct; run its OnUpdate logic via a temporary
            // World-owned SystemHandle rather than calling OnUpdate directly (ISystem methods
            // are called by the ECS scheduler, not invoked as plain instance methods).
            World world = em.World;
            SystemHandle handle = world.GetOrCreateSystem<ConnectivitySystem>();
            handle.Update(world.Unmanaged);

            Entity regionEntity = em.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);

            var counts = new NativeArray<int>(cells.Length, Allocator.Temp);
            int largest = -1;
            for (int i = 0; i < regions.Length; i++)
            {
                int region = regions[i].RegionId;
                if (region < 0) continue;
                counts[region]++;
                if (largest < 0 || counts[region] > counts[largest]) largest = region;
            }

            int spawn = -1, best = int.MaxValue;
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].RegionId != largest) continue;
                int distance = System.Math.Abs(i % Size - Size / 2) + System.Math.Abs(i / Size - Size / 2);
                if (distance >= best) continue;
                best = distance; spawn = i;
            }
            counts.Dispose();
            if (spawn < 0) throw new InvalidOperationException("Settings produced no walkable land; reduce water or rock density.");
            return spawn;
        }
    }

    public struct TreeTag : IComponentData { }
    public struct TreeCellIndex : IComponentData { public int Value; }
}
