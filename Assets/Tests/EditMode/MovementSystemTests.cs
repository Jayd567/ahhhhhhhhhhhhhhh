using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class MovementSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("MovementSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 3, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < 3; i++) cells[i] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(3);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(3);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            for (int i = 0; i < 3; i++) baseCosts[i] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void MovingPawn_StepsTowardDestination_ThenStartsWorking()
        {
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);
            Entity job = _em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            _em.SetComponentData(job, new JobData { Type = JobType.ChopTree, TargetCellIndex = 0, RemainingWork = 0 });
            _em.SetComponentEnabled<Unclaimed>(job, false);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(2, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentData(pawn, new AssignedFlowField { Value = field });
            _em.SetComponentEnabled<IsMoving>(pawn, true);

            _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(WorkDurationConfig)).GetSingletonEntity(),
                new WorkDurationConfig { ChopDurationTicks = 60, MineDurationTicks = 60 });

            SystemHandle handle = _world.GetOrCreateSystem<MovementSystem>();
            handle.Update(_world.Unmanaged);

            Assert.AreEqual(new float3(1, 0, 0), _em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.IsTrue(_em.IsComponentEnabled<IsMoving>(pawn));

            handle.Update(_world.Unmanaged);
            Assert.AreEqual(new float3(0, 0, 0), _em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.IsFalse(_em.IsComponentEnabled<IsMoving>(pawn));
            Assert.IsTrue(_em.IsComponentEnabled<IsWorking>(pawn));
            Assert.AreEqual(60, _em.GetComponentData<JobData>(job).RemainingWork);
        }
    }
}
