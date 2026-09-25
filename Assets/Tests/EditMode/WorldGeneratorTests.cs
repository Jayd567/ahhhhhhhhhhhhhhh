using System;
using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Generation;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class WorldGeneratorTests
    {
        [Test]
        public void Generation_IsSeededNormalizedAndTerrainCorrect()
        {
            var settings = new WorldGenerationSettings();
            var a = WorldGenerator.Generate(settings);
            var b = WorldGenerator.Generate(settings);
            settings.Seed++;
            var c = WorldGenerator.Generate(settings);
            Assert.AreEqual(256, a.Heightmap.GetLength(0));
            Assert.AreEqual(256, a.Heightmap.GetLength(1));
            float min = 1, max = 0;
            int changed = 0, trees = 0, rocks = 0, water = 0;
            for (int i = 0; i < a.Grid.CellCount; i++)
            {
                int x = i % 256, y = i / 256;
                float h = a.Heightmap[x,y]; min = Math.Min(min,h); max = Math.Max(max,h);
                Assert.AreEqual(h, b.Heightmap[x,y]);
                Assert.AreEqual(a.Grid.GetCell(i), b.Grid.GetCell(i));
                if (h != c.Heightmap[x,y]) changed++;
                Cell cell = a.Grid.GetCell(i);
                int expected = h < settings.WaterThreshold ? settings.WaterId : h < settings.DirtThreshold ? settings.DirtId : h < settings.StoneThreshold ? settings.GrassId : settings.StoneId;
                Assert.AreEqual(expected, cell.TerrainTypeId);
                if (cell.HasTree) { trees++; Assert.AreEqual(settings.GrassId, cell.TerrainTypeId); Assert.IsTrue(cell.IsWalkable); }
                if (cell.HasRock) { rocks++; Assert.IsTrue(cell.TerrainTypeId == settings.StoneId || cell.TerrainTypeId == settings.DirtId); Assert.IsFalse(cell.IsWalkable); }
                if (cell.TerrainTypeId == settings.WaterId) { water++; Assert.IsFalse(cell.IsWalkable); Assert.IsFalse(cell.HasRock); }
                foreach (int neighbor in a.Grid.GetWalkableNeighbors(i))
                    if (cell.IsWalkable) Assert.AreEqual(cell.ConnectivityId, a.Grid.GetCell(neighbor).ConnectivityId);
            }
            Assert.AreEqual(0f,min); Assert.AreEqual(1f,max);
            Assert.Greater(changed, 60000); Assert.Greater(trees, 0); Assert.Greater(rocks, 0); Assert.Greater(water, 0);
            Assert.IsTrue(a.Grid.GetCell(a.SpawnCell).IsWalkable);
        }

        [TestCase(JobType.Mine)]
        [TestCase(JobType.ChopTree)]
        public void GeneratedProps_CompleteThroughTicksWithoutManagedAllocations(JobType type)
        {
            var generated = WorldGenerator.Generate(new WorldGenerationSettings());
            WorldGrid grid = generated.Grid;
            int target = -1, start = -1;
            for (int i = 0; i < grid.CellCount && target < 0; i++)
            {
                Cell cell = grid.GetCell(i);
                if (!(type == JobType.Mine ? cell.HasRock : cell.HasTree)) continue;
                foreach (int neighbor in grid.GetWalkableNeighbors(i)) { target = i; start = neighbor; break; }
            }
            Assert.GreaterOrEqual(target, 0);
            int ground = grid.GetCell(target).TerrainTypeId;
            var pawns = new PawnManager();
            pawns.SpawnPawn(start % 256, start / 256);
            var jobs = new JobBoard();
            Assert.IsTrue(jobs.TryAddJob(grid, type, target, out _));
            var ticks = new TickManager(grid, pawns, jobs, new GridAStar(), 1);
            var rock = new TerrainJobConfig { FloorTerrainTypeId = 0, WorkDurationTicks = 1, YieldResourceType = ResourceType.Stone, YieldAmount = 10 };
            var tree = new TreeJobConfig { WorkDurationTicks = 1, YieldResourceType = ResourceType.Wood, YieldAmount = 10 };
            // Warm the runtime/JIT with a separate identical workload before measuring.
            WarmTicks();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int yielded = 0;
            for (int i = 0; i < 12; i++) { ticks.Tick(rock, tree); yielded += ticks.SpawnedItemsThisTick.Count; }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(1, yielded);
            Assert.AreEqual(0, allocated, "Claim, path, work and completion must allocate zero managed bytes.");
            Assert.AreEqual(ground, grid.GetCell(target).TerrainTypeId);
            Assert.IsTrue(grid.GetCell(target).IsWalkable);
            Assert.IsFalse(type == JobType.Mine ? grid.GetCell(target).HasRock : grid.GetCell(target).HasTree);
        }

        private static void WarmTicks()
        {
            var g = new WorldGrid(3,1);
            for (int i=0;i<3;i++) g.SetGeneratedCell(i,0,true,false,i==2);
            g.SetHasTree(0,true); g.CompleteGeneration();
            var p = new PawnManager(); p.SpawnPawn(1,0);
            var b = new JobBoard(); b.TryAddJob(g,JobType.Mine,2,out _); b.TryAddJob(g,JobType.ChopTree,0,out _);
            var t = new TickManager(g,p,b,new GridAStar(),1);
            for(int i=0;i<20;i++) t.Tick(new TerrainJobConfig {WorkDurationTicks=1},new TreeJobConfig {WorkDurationTicks=1});
        }

        [Test]
        public void WaterCannotBeMinedAndDisconnectedPathsAreRejected()
        {
            var grid = new WorldGrid(3,1);
            grid.SetGeneratedCell(0,0,true,false,false);
            grid.SetGeneratedCell(1,2,false,false,false);
            grid.SetGeneratedCell(2,0,true,false,false);
            grid.CompleteGeneration();
            Assert.IsNull(grid.ResolveMineJob(1,0,ResourceType.Stone,10));
            Assert.IsFalse(new JobBoard().TryAddJob(grid,JobType.Mine,1,out _));
            Assert.IsFalse(new GridAStar().TryFindPath(grid,0,2,new List<int>()));
        }

        [Test]
        public void InvalidSettingsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => WorldGenerator.Generate(new WorldGenerationSettings { NoiseScale = 0 }));
            Assert.Throws<ArgumentException>(() => WorldGenerator.Generate(new WorldGenerationSettings { WaterThreshold = 0.9f }));
        }
    }
}
