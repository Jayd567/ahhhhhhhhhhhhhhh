using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class PawnFactoryTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("PawnFactoryTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void CreatePawn_SetsPositionIdAndIdleState()
        {
            EntityManager em = _world.EntityManager;
            Entity pawn = PawnFactory.CreatePawn(em, id: 3, position: new float2(2, 5), tickOffset: 3);

            Assert.AreEqual(3, em.GetComponentData<PawnId>(pawn).Value);
            Assert.AreEqual(new float3(2, 5, 0), em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.AreEqual(3, em.GetComponentData<TickOffset>(pawn).Value);
            Assert.AreEqual(Entity.Null, em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.AreEqual(Entity.Null, em.GetComponentData<AssignedFlowField>(pawn).Value);
            Assert.IsFalse(em.IsComponentEnabled<IsMoving>(pawn));
            Assert.IsFalse(em.IsComponentEnabled<IsWorking>(pawn));
        }
    }
}
