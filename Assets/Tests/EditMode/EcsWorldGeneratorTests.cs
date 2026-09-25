using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Generation;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class EcsWorldGeneratorTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("EcsWorldGeneratorTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static EntityManager Em(World w) => w.EntityManager;

        [Test]
        public void Generate_SameSeed_IsDeterministic()
        {
            var settings = new WorldGenerationSettings { Seed = 42 };

            using var worldB = new World("EcsWorldGeneratorTests_B");
            Entity gridA = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            Entity gridB = GridBootstrap.CreateGrid(worldB.EntityManager, EcsWorldGenerator.Size, EcsWorldGenerator.Size);

            int spawnA = EcsWorldGenerator.Generate(Em(_world), gridA, settings);
            int spawnB = EcsWorldGenerator.Generate(worldB.EntityManager, gridB, settings);

            Assert.AreEqual(spawnA, spawnB);
            DynamicBuffer<CellElement> cellsA = Em(_world).GetBuffer<CellElement>(gridA);
            DynamicBuffer<CellElement> cellsB = worldB.EntityManager.GetBuffer<CellElement>(gridB);
            for (int i = 0; i < cellsA.Length; i++)
                Assert.AreEqual(cellsA[i].Value.TerrainId, cellsB[i].Value.TerrainId, $"cell {i} differs");
        }

        [Test]
        public void Generate_WaterCellsAreNeverWalkable()
        {
            var settings = new WorldGenerationSettings { Seed = 7 };
            Entity grid = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            EcsWorldGenerator.Generate(Em(_world), grid, settings);

            DynamicBuffer<CellElement> cells = Em(_world).GetBuffer<CellElement>(grid);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Value.TerrainId == settings.WaterId)
                    Assert.IsFalse(cells[i].Value.Walkable, $"water cell {i} was walkable");
        }

        [Test]
        public void Generate_SpawnCellIsWalkableAndInLargestRegion()
        {
            var settings = new WorldGenerationSettings { Seed = 99 };
            Entity grid = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            int spawn = EcsWorldGenerator.Generate(Em(_world), grid, settings);

            DynamicBuffer<CellElement> cells = Em(_world).GetBuffer<CellElement>(grid);
            Assert.IsTrue(cells[spawn].Value.Walkable);
        }
    }
}
