using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ColonySim.Simulation.Pawns
{
    public static class PawnFactory
    {
        public static Entity CreatePawn(EntityManager em, int id, float2 position, int tickOffset)
        {
            Entity pawn = em.CreateEntity(
                typeof(PawnId), typeof(LocalTransform), typeof(MoveSpeedModifier), typeof(TickOffset),
                typeof(CurrentJob), typeof(AssignedFlowField), typeof(IsMoving), typeof(IsWorking));
            em.SetComponentData(pawn, new PawnId { Value = id });
            em.SetComponentData(pawn, LocalTransform.FromPosition(position.x, position.y, 0));
            em.SetComponentData(pawn, new MoveSpeedModifier { Value = 1f });
            em.SetComponentData(pawn, new TickOffset { Value = tickOffset });
            em.SetComponentData(pawn, new CurrentJob { Value = Entity.Null });
            em.SetComponentData(pawn, new AssignedFlowField { Value = Entity.Null });
            em.SetComponentEnabled<IsMoving>(pawn, false);
            em.SetComponentEnabled<IsWorking>(pawn, false);
            return pawn;
        }
    }
}
