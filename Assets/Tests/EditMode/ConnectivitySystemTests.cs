using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class ConnectivitySystemTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("ConnectivitySystemTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static void SetWalkable(EntityManager em, Entity grid, int index, bool walkable)
        {
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            CellData data = cells[index].Value;
            data.Walkable = walkable;
            cells[index] = new CellElement { Value = data };
        }

        [Test]
        public void TwoDisconnectedWalkableRegions_GetDifferentRegionIds()
        {
            EntityManager em = _world.EntityManager;
            Entity grid = GridBootstrap.CreateGrid(em, 3, 1); // cells: [0]=walkable [1]=wall [2]=walkable
            SetWalkable(em, grid, 0, true);
            SetWalkable(em, grid, 1, false);
            SetWalkable(em, grid, 2, true);
            em.SetComponentData(grid, new GridRevision { Value = 1 });

            SystemHandle handle = _world.CreateSystem<ConnectivitySystem>();
            handle.Update(_world.Unmanaged);

            Entity regionEntity = _world.EntityManager.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);
            Assert.AreNotEqual(-1, regions[0].RegionId);
            Assert.AreEqual(-1, regions[1].RegionId);
            Assert.AreNotEqual(-1, regions[2].RegionId);
            Assert.AreNotEqual(regions[0].RegionId, regions[2].RegionId);
        }

        [Test]
        public void FiveCellRow_WallAtIndexTwo_SplitsIntoTwoRegions()
        {
            EntityManager em = _world.EntityManager;
            Entity grid = GridBootstrap.CreateGrid(em, 5, 1); // cells: [0][1] walkable, [2] wall, [3][4] walkable
            SetWalkable(em, grid, 0, true);
            SetWalkable(em, grid, 1, true);
            SetWalkable(em, grid, 2, false);
            SetWalkable(em, grid, 3, true);
            SetWalkable(em, grid, 4, true);
            em.SetComponentData(grid, new GridRevision { Value = 1 });

            SystemHandle handle = _world.CreateSystem<ConnectivitySystem>();
            handle.Update(_world.Unmanaged);

            Entity regionEntity = _world.EntityManager.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);

            Assert.AreEqual(-1, regions[2].RegionId);
            Assert.AreNotEqual(-1, regions[0].RegionId);
            Assert.AreNotEqual(-1, regions[3].RegionId);

            Assert.AreEqual(regions[0].RegionId, regions[1].RegionId);
            Assert.AreEqual(regions[3].RegionId, regions[4].RegionId);
            Assert.AreNotEqual(regions[0].RegionId, regions[3].RegionId);
        }
    }
}
