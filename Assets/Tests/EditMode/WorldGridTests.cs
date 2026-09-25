using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Tests
{
    public class WorldGridTests
    {
        [Test]
        public void CellCount_EqualsWidthTimesHeight()
        {
            var grid = new WorldGrid(4, 3);
            Assert.AreEqual(12, grid.CellCount);
        }

        [Test]
        public void IndexOf_IsRowMajor()
        {
            var grid = new WorldGrid(4, 3);
            Assert.AreEqual(0, grid.IndexOf(0, 0));
            Assert.AreEqual(3, grid.IndexOf(3, 0));
            Assert.AreEqual(4, grid.IndexOf(0, 1));
            Assert.AreEqual(11, grid.IndexOf(3, 2));
        }

        [Test]
        public void TryGetCoordsOf_RoundTripsWithIndexOf()
        {
            var grid = new WorldGrid(5, 5);
            int index = grid.IndexOf(2, 3);
            bool found = grid.TryGetCoordsOf(index, out int x, out int y);
            Assert.IsTrue(found);
            Assert.AreEqual(2, x);
            Assert.AreEqual(3, y);
        }

        [Test]
        public void TryGetCoordsOf_OutOfRangeIndex_ReturnsFalse()
        {
            var grid = new WorldGrid(2, 2);
            bool found = grid.TryGetCoordsOf(99, out _, out _);
            Assert.IsFalse(found);
        }

        [Test]
        public void SetTerrain_UpdatesWalkabilityAndTerrainId()
        {
            var grid = new WorldGrid(3, 3);
            int index = grid.IndexOf(1, 1);
            grid.SetTerrain(index, terrainTypeId: 7, isWalkable: false);
            Cell cell = grid.GetCell(index);
            Assert.AreEqual(7, cell.TerrainTypeId);
            Assert.IsFalse(cell.IsWalkable);
        }

        [Test]
        public void NeighborMask_InteriorCell_AllWalkableByDefault()
        {
            var grid = new WorldGrid(3, 3);
            // default Cell.IsWalkable is false (struct default), so mark all walkable first
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

            int center = grid.IndexOf(1, 1);
            byte mask = grid.GetCell(center).NeighborWalkableMask;
            Assert.AreEqual(Cell.NorthMask | Cell.EastMask | Cell.SouthMask | Cell.WestMask, mask);
        }

        [Test]
        public void NeighborMask_CornerCell_OffGridNeighborsAreNotWalkable()
        {
            var grid = new WorldGrid(3, 3);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

            int corner = grid.IndexOf(0, 0);
            byte mask = grid.GetCell(corner).NeighborWalkableMask;
            // (0,0) has no North (y-1 off-grid) or West (x-1 off-grid) neighbor on-grid;
            // East (x+1) and South (y+1) are both on-grid for a 3x3 grid.
            Assert.AreEqual(Cell.EastMask | Cell.SouthMask, mask);
        }

        [Test]
        public void NeighborMask_UpdatesWhenNeighborBecomesUnwalkable()
        {
            var grid = new WorldGrid(3, 3);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

            int center = grid.IndexOf(1, 1);
            int east = grid.IndexOf(2, 1);
            grid.SetTerrain(east, terrainTypeId: 1, isWalkable: false);

            byte mask = grid.GetCell(center).NeighborWalkableMask;
            Assert.AreEqual(Cell.NorthMask | Cell.SouthMask | Cell.WestMask, mask);
        }

        [Test]
        public void Connectivity_AllWalkableGrid_SharesOneId()
        {
            var grid = new WorldGrid(3, 3);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

            int firstId = grid.GetCell(0).ConnectivityId;
            for (int i = 1; i < grid.CellCount; i++)
                Assert.AreEqual(firstId, grid.GetCell(i).ConnectivityId);
        }

        [Test]
        public void Connectivity_WallSplitsGridIntoTwoIds()
        {
            // 3x1 grid: [walkable][wall][walkable] -> two disconnected regions
            var grid = new WorldGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
            grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

            int leftId = grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId;
            int rightId = grid.GetCell(grid.IndexOf(2, 0)).ConnectivityId;
            Assert.AreNotEqual(leftId, rightId);
        }

        [Test]
        public void Connectivity_OpeningWallMergesRegions()
        {
            var grid = new WorldGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
            grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

            grid.SetTerrain(grid.IndexOf(1, 0), 0, isWalkable: true); // mine the wall open

            int leftId = grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId;
            int rightId = grid.GetCell(grid.IndexOf(2, 0)).ConnectivityId;
            Assert.AreEqual(leftId, rightId);
        }

        [Test]
        public void Connectivity_UnwalkableCell_HasSentinelId()
        {
            var grid = new WorldGrid(2, 2);
            grid.SetTerrain(grid.IndexOf(0, 0), 1, isWalkable: false);
            Assert.AreEqual(WorldGrid.UnreachableConnectivityId, grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId);
        }

        [Test]
        public void ResolveMineJob_MutatesCellToFloorAndReturnsResourceItem()
        {
            var grid = new WorldGrid(3, 3);
            int target = grid.IndexOf(1, 1);
            grid.SetTerrain(target, terrainTypeId: 1, isWalkable: false); // rock

            var item = grid.ResolveMineJob(target, floorTerrainTypeId: 0, ResourceType.Stone, 10);

            Assert.IsNotNull(item);
            Assert.AreEqual(ResourceType.Stone, item.Type);
            Assert.AreEqual(10, item.Amount);
            Assert.IsTrue(grid.GetCell(target).IsWalkable);
            Assert.AreEqual(0, grid.GetCell(target).TerrainTypeId);
        }

        [Test]
        public void ResolveMineJob_AlreadyResolvedTarget_ReturnsNull()
        {
            var grid = new WorldGrid(3, 3);
            int target = grid.IndexOf(1, 1);
            grid.SetTerrain(target, terrainTypeId: 1, isWalkable: false);

            grid.ResolveMineJob(target, 0, ResourceType.Stone, 10); // first resolve
            var secondAttempt = grid.ResolveMineJob(target, 0, ResourceType.Stone, 10); // already floor now

            Assert.IsNull(secondAttempt);
        }

        [Test]
        public void ResolveTreeJob_ClearsTreeFlagAndReturnsResourceItem()
        {
            var grid = new WorldGrid(3, 3);
            int target = grid.IndexOf(1, 1);
            grid.SetTerrain(target, terrainTypeId: 0, isWalkable: true);
            grid.SetHasTree(target, true);

            var item = grid.ResolveTreeJob(target, ResourceType.Wood, 10);

            Assert.IsNotNull(item);
            Assert.AreEqual(ResourceType.Wood, item.Type);
            Assert.IsFalse(grid.GetCell(target).HasTree);
        }

        [Test]
        public void ResolveTreeJob_NoTreePresent_ReturnsNull()
        {
            var grid = new WorldGrid(3, 3);
            int target = grid.IndexOf(1, 1);
            grid.SetTerrain(target, terrainTypeId: 0, isWalkable: true);

            var item = grid.ResolveTreeJob(target, ResourceType.Wood, 10);
            Assert.IsNull(item);
        }
    }
}
