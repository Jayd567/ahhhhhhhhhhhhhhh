using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;

namespace ColonySim.Simulation.Tests
{
    public class JobFactoryTests
    {
        private World _world;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("JobFactoryTests");
            _grid = GridBootstrap.CreateGrid(_world.EntityManager, 3, 1);
            DynamicBuffer<CellElement> cells = _world.EntityManager.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = false, HasRock = true } }; // mineable
            cells[1] = new CellElement { Value = new CellData { Walkable = true } };
            cells[2] = new CellElement { Value = new CellData { Walkable = false, HasRock = false } }; // plain wall, not mineable
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void TryCreateJob_MineableCell_Succeeds()
        {
            bool created = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out Entity job);
            Assert.IsTrue(created);
            JobData data = _world.EntityManager.GetComponentData<JobData>(job);
            Assert.AreEqual(JobType.Mine, data.Type);
            Assert.AreEqual(0, data.TargetCellIndex);
            Assert.IsTrue(_world.EntityManager.IsComponentEnabled<Unclaimed>(job));
        }

        [Test]
        public void TryCreateJob_PlainWallNotMineable_Fails()
        {
            bool created = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 2, out _);
            Assert.IsFalse(created);
        }

        [Test]
        public void TryCreateJob_DuplicateTarget_Rejected()
        {
            JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out _);
            bool secondCreated = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out _);
            Assert.IsFalse(secondCreated);
        }
    }
}
