using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;

namespace ColonySim.Simulation.Tests
{
    public class FlowFieldGenerationSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("FlowFieldGenerationSystemTests");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void MakeGrid(int width, int height, bool[] walkable)
        {
            _grid = GridBootstrap.CreateGrid(_em, width, height);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new CellElement { Value = new CellData { Walkable = walkable[i] } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });

            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(cells.Length);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(cells.Length);
            for (int i = 0; i < cells.Length; i++)
                baseCosts[i] = new BaseCostElement { Value = walkable[i] ? (ushort)1 : TileCost.Impassable };
            _em.AddComponent<TileCostSingleton>(_grid);

            SystemHandle connectivity = _world.GetOrCreateSystem<ConnectivitySystem>();
            connectivity.Update(_world.Unmanaged);
        }

        [Test]
        public void GetOrCreateField_StraightLine_FlowsTowardDestination()
        {
            MakeGrid(3, 1, new[] { true, true, true });
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);

            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(0, costs[0].Value);
            Assert.AreEqual(1, costs[1].Value);
            Assert.AreEqual(2, costs[2].Value);

            DynamicBuffer<FlowDirectionElement> directions = _em.GetBuffer<FlowDirectionElement>(field);
            Assert.AreNotEqual(-1, directions[1].Value); // cell 1 has a valid direction toward cell 0
        }

        [Test]
        public void GetOrCreateField_UnreachableAcrossWall_StaysUnreached()
        {
            MakeGrid(3, 1, new[] { true, false, true }); // wall between destination and cell 2
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);

            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(ushort.MaxValue, costs[2].Value);
        }

        [Test]
        public void GetOrCreateField_MineJobOnWallBetweenTwoRegions_ResolvesGoalFromRequestingRegionOnly()
        {
            // Regression: cells [0][1] region A, cell[2] = rock target between regions,
            // [3][4] region B. A field generated for the rock target from region A's side
            // must integrate outward only through A's walkable neighbors, never bleed cost
            // into region B through the rock cell itself (the rock is impassable).
            MakeGrid(5, 1, new[] { true, true, false, true, true });
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            CellData rock = cells[2].Value; rock.HasRock = true; cells[2] = new CellElement { Value = rock };
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[2] = new BaseCostElement { Value = TileCost.Impassable };
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 1);
            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(ushort.MaxValue, costs[3].Value, "region B must not be reached through the rock");
            Assert.AreEqual(ushort.MaxValue, costs[4].Value);
            Assert.AreEqual(0, costs[1].Value);
            Assert.AreEqual(1, costs[0].Value);
        }
    }
}
