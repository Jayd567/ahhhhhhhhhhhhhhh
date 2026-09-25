using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class GridBootstrapTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("GridBootstrapTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void CreateGrid_SetsDimensionsAndSizesBuffer()
        {
            Entity grid = GridBootstrap.CreateGrid(_world.EntityManager, 4, 3);

            var dims = _world.EntityManager.GetComponentData<GridDimensions>(grid);
            Assert.AreEqual(4, dims.Width);
            Assert.AreEqual(3, dims.Height);

            var cells = _world.EntityManager.GetBuffer<CellElement>(grid);
            Assert.AreEqual(12, cells.Length);

            Assert.AreEqual(0, _world.EntityManager.GetComponentData<GridRevision>(grid).Value);
            Assert.IsTrue(_world.EntityManager.HasComponent<GridSingleton>(grid));
        }
    }
}
