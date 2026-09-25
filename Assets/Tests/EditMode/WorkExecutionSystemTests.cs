using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class WorkExecutionSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("WorkExecutionSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 2, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = false, HasRock = true, TerrainId = 5 } };
            cells[1] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.CreateEntity(typeof(SimulationTick));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(SimulationTick)).GetSingletonEntity(), new SimulationTick { Value = 0, StaggerBucketCount = 1 });
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void WorkingPawn_CompletesJob_MutatesGridAndSpawnsResourceAndReturnsIdle()
        {
            Entity job = _em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            _em.SetComponentData(job, new JobData { Type = JobType.Mine, TargetCellIndex = 0, RemainingWork = 1 });
            _em.SetComponentEnabled<Unclaimed>(job, false);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(1, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentEnabled<IsWorking>(pawn, true);

            Entity configEntity = _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(configEntity, new WorkDurationConfig { FloorTerrainTypeId = 1 });

            int revisionBefore = _em.GetComponentData<GridRevision>(_grid).Value;
            _world.GetOrCreateSystem<WorkExecutionSystem>().Update(_world.Unmanaged);

            Assert.IsFalse(_em.IsComponentEnabled<IsWorking>(pawn));
            Assert.AreEqual(Entity.Null, _em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.IsFalse(_em.Exists(job));

            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            Assert.IsFalse(cells[0].Value.HasRock);
            Assert.IsTrue(cells[0].Value.Walkable);
            Assert.Greater(_em.GetComponentData<GridRevision>(_grid).Value, revisionBefore);

            EntityQuery pending = _em.CreateEntityQuery(typeof(ResourceItemData), typeof(PendingResourceSpawn));
            Assert.AreEqual(1, pending.CalculateEntityCount());
        }

        [Test]
        public void MovementSystem_AfterGridMutatedMidFlight_StillAdvancesSafely()
        {
            // Review Focus: grid mutation racing flow-field staleness. A field generated
            // before a nearby cell's revision-bumping mutation must not crash MovementSystem
            // when read afterward, even though it is now stale.
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = true } };
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(2);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(2);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[0] = new BaseCostElement { Value = 1 };
            baseCosts[1] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);
            Entity pawn = PawnFactory.CreatePawn(_em, id: 1, position: new float2(1, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new AssignedFlowField { Value = field });
            Entity job = _em.CreateEntity(typeof(JobData));
            _em.SetComponentData(job, new JobData { Type = JobType.ChopTree, TargetCellIndex = 0 });
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentEnabled<IsMoving>(pawn, true);

            // Mutate the grid (bumps revision) without regenerating the field.
            CellData mutated = cells[1].Value; mutated.Walkable = false; cells[1] = new CellElement { Value = mutated };
            _em.SetComponentData(_grid, new GridRevision { Value = 2 });

            _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(WorkDurationConfig)).GetSingletonEntity(), new WorkDurationConfig { ChopDurationTicks = 10 });

            Assert.DoesNotThrow(() => _world.GetOrCreateSystem<MovementSystem>().Update(_world.Unmanaged));
        }
    }
}
