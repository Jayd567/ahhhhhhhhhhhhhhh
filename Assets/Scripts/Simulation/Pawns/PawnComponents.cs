using Unity.Entities;

namespace ColonySim.Simulation.Pawns
{
    public struct PawnId : IComponentData { public int Value; }
    public struct MoveSpeedModifier : IComponentData { public float Value; }
    public struct TickOffset : IComponentData { public int Value; }
    public struct CurrentJob : IComponentData { public Entity Value; }
    public struct AssignedFlowField : IComponentData { public Entity Value; }
    public struct IsMoving : IComponentData, IEnableableComponent { }
    public struct IsWorking : IComponentData, IEnableableComponent { }
}
