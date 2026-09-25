using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class JobAssignmentSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("JobAssignmentSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 5, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < 5; i++) cells[i] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(5);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(5);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            for (int i = 0; i < 5; i++) baseCosts[i] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void IdlePawn_ClaimsNearestReachableUnclaimedJob()
        {
            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(0, 0), tickOffset: 0);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 3, out Entity farJob);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 1, out Entity nearJob);

            _world.GetOrCreateSystem<JobAssignmentSystem>().Update(_world.Unmanaged);

            Assert.AreEqual(nearJob, _em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.IsFalse(_em.IsComponentEnabled<Unclaimed>(nearJob));
            Assert.IsTrue(_em.IsComponentEnabled<Unclaimed>(farJob));
        }

        [Test]
        public void IdlePawn_NoReachableJobInRegion_StaysIdleWithoutThrowing()
        {
            // Wall off cell 4 from the rest so a job there is in a different region.
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            CellData wall = cells[2].Value; wall.Walkable = false; cells[2] = new CellElement { Value = wall };
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[2] = new BaseCostElement { Value = TileCost.Impassable };
            _em.SetComponentData(_grid, new GridRevision { Value = 2 });
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(0, 0), tickOffset: 0);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 4, out _);

            Assert.DoesNotThrow(() => _world.GetOrCreateSystem<JobAssignmentSystem>().Update(_world.Unmanaged));
            Assert.AreEqual(Entity.Null, _em.GetComponentData<CurrentJob>(pawn).Value);
        }
    }
}
