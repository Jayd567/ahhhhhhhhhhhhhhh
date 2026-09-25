using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class TickManagerTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, 0, isWalkable: true);
            return grid;
        }

        private static TerrainJobConfig DefaultFloorConfig() =>
            new TerrainJobConfig { FloorTerrainTypeId = 0, YieldResourceType = ResourceType.Stone, YieldAmount = 10, WorkDurationTicks = 3 };

        private static TreeJobConfig DefaultTreeConfig() =>
            new TreeJobConfig { YieldResourceType = ResourceType.Wood, YieldAmount = 10, WorkDurationTicks = 3 };

        [Test]
        public void Tick_ZeroPawnsZeroJobs_DoesNotThrow()
        {
            var grid = MakeOpenGrid(3, 3);
            var tickManager = new TickManager(grid, new PawnManager(), new JobBoard(), new GridAStar(), staggerBucketCount: 4);

            Assert.DoesNotThrow(() => tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig()));
        }

        [Test]
        public void Tick_IdlePawnClaimsPathsWorksAndCompletesMineJob()
        {
            var grid = MakeOpenGrid(5, 1);
            int rockIndex = grid.IndexOf(4, 0);
            grid.SetTerrain(rockIndex, terrainTypeId: 1, isWalkable: false);

            var pawnManager = new PawnManager();
            int pawnId = pawnManager.SpawnPawn(0, 0);

            var jobBoard = new JobBoard();
            jobBoard.TryAddJob(grid, JobType.Mine, rockIndex, out _);

            var tickManager = new TickManager(grid, pawnManager, jobBoard, new GridAStar(), staggerBucketCount: 1);

            // Run enough ticks to path 4 cells + work 3 ticks + resolve; generous upper bound.
            ResourceItem spawnedItem = null;
            for (int i = 0; i < 50 && spawnedItem == null; i++)
            {
                tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
                foreach (var item in tickManager.SpawnedItemsThisTick)
                    spawnedItem = item;
            }

            Assert.IsNotNull(spawnedItem);
            Assert.AreEqual(ResourceType.Stone, spawnedItem.Type);
            Assert.IsTrue(grid.GetCell(rockIndex).IsWalkable);
            Assert.AreEqual(PawnState.Idle, pawnManager.GetPawn(pawnId).State);
        }

        [Test]
        public void Tick_PawnWorkingJobWhoseTargetAlreadyResolved_CancelsGracefullyWithoutDuplicateItem()
        {
            var grid = MakeOpenGrid(5, 1);
            int rockIndex = grid.IndexOf(4, 0);
            grid.SetTerrain(rockIndex, terrainTypeId: 1, isWalkable: false);

            var pawnManager = new PawnManager();
            int pawnId = pawnManager.SpawnPawn(0, 0);

            var jobBoard = new JobBoard();
            jobBoard.TryAddJob(grid, JobType.Mine, rockIndex, out int jobId);

            var tickManager = new TickManager(grid, pawnManager, jobBoard, new GridAStar(), staggerBucketCount: 1);

            // Run exactly enough ticks (claim tick + 3 path-advance ticks + the
            // path->work transition tick) to land the pawn in Working state with
            // its full work countdown still ahead of it - not yet finished.
            for (int i = 0; i < 5; i++) tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());

            Pawn pawn = pawnManager.GetPawn(pawnId);
            Assert.AreEqual(PawnState.Working, pawn.State,
                "test setup must land the pawn mid-work before invalidating the target, or this test doesn't exercise mid-execution invalidation at all");

            // Simulate the target being resolved out from under the pawn by another path.
            grid.ResolveMineJob(rockIndex, 0, ResourceType.Stone, 10);

            int itemCount = 0;
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
                    itemCount += tickManager.SpawnedItemsThisTick.Count;
                }
            });

            Assert.AreEqual(0, itemCount); // no duplicate item from the cancelled job
            Assert.AreEqual(PawnState.Idle, pawn.State);
        }

        [Test]
        public void Tick_MineJobOnWallBetweenTwoRegions_PawnPathsViaItsOwnRegionNeighbor()
        {
            // 3x3 grid with a full-width rock wall across row y=1, splitting the
            // map into a north region (y=0) and a south region (y=2). The pawn
            // lives in the south region; the rock at (1,1) has a walkable neighbor
            // in EACH region ((1,0) north, (1,2) south). The path goal must be the
            // pawn's own-region neighbor (1,2), not whichever neighbor is found
            // first in scan order.
            var grid = MakeOpenGrid(3, 3);
            grid.SetTerrain(grid.IndexOf(0, 1), 1, isWalkable: false);
            grid.SetTerrain(grid.IndexOf(1, 1), 1, isWalkable: false); // mine target
            grid.SetTerrain(grid.IndexOf(2, 1), 1, isWalkable: false);

            var pawnManager = new PawnManager();
            pawnManager.SpawnPawn(1, 2); // south region

            var jobBoard = new JobBoard();
            int rockIndex = grid.IndexOf(1, 1);
            jobBoard.TryAddJob(grid, JobType.Mine, rockIndex, out _);

            var tickManager = new TickManager(grid, pawnManager, jobBoard, new GridAStar(), staggerBucketCount: 1);

            ResourceItem spawnedItem = null;
            for (int i = 0; i < 30 && spawnedItem == null; i++)
            {
                tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
                foreach (var item in tickManager.SpawnedItemsThisTick)
                    spawnedItem = item;
            }

            Assert.IsNotNull(spawnedItem,
                "pawn must path to its own-region neighbor of the rock; pathing to the far-region neighbor can never succeed and the job would retry forever");
        }
    }
}
