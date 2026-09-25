using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;

namespace ColonySim.Simulation.Tests
{
    public class GridAStarTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);
            return grid;
        }

        [Test]
        public void TryFindPath_StraightLine_FindsShortestPath()
        {
            var grid = MakeOpenGrid(5, 1);
            var pathfinder = new GridAStar();
            var path = new List<int>();

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(4, 0), path);

            Assert.IsTrue(found);
            Assert.AreEqual(5, path.Count);
            Assert.AreEqual(grid.IndexOf(0, 0), path[0]);
            Assert.AreEqual(grid.IndexOf(4, 0), path[path.Count - 1]);
        }

        [Test]
        public void TryFindPath_AroundObstacle_FindsDetour()
        {
            var grid = MakeOpenGrid(3, 3);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
            grid.SetTerrain(grid.IndexOf(1, 1), 1, isWalkable: false);
            // (1,2) stays open as the only way across

            var pathfinder = new GridAStar();
            var path = new List<int>();
            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(2, 0), path);

            Assert.IsTrue(found);
            CollectionAssert.Contains(path, grid.IndexOf(1, 2));
        }

        [Test]
        public void TryFindPath_WalledOff_ReturnsFalseAndClearsPath()
        {
            var grid = MakeOpenGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);

            var pathfinder = new GridAStar();
            var path = new List<int> { 999 }; // pre-populate to prove it gets cleared

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(2, 0), path);

            Assert.IsFalse(found);
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void TryFindPath_StartEqualsGoal_ReturnsSingleCellPath()
        {
            var grid = MakeOpenGrid(2, 2);
            var pathfinder = new GridAStar();
            var path = new List<int>();

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(0, 0), path);

            Assert.IsTrue(found);
            Assert.AreEqual(1, path.Count);
        }
    }
}
