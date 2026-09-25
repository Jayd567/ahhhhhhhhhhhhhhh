using Unity.Entities;

namespace ColonySim.Simulation.Pathing
{
    public struct FlowFieldDestination : IComponentData
    {
        public int TargetCellIndex;
    }

    public struct FlowFieldGeneration : IComponentData
    {
        public int GridRevisionGeneratedAt;
    }

    public struct IntegrationCostElement : IBufferElementData
    {
        public ushort Value;
    }

    public struct FlowDirectionElement : IBufferElementData
    {
        public sbyte Value; // -1 = none; 0=N,1=E,2=S,3=W,4=NE,5=SE,6=SW,7=NW
    }
}
