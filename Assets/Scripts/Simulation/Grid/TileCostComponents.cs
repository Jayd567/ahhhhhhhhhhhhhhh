using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct TileCostSingleton : IComponentData { }

    public static class TileCost
    {
        public const ushort Impassable = ushort.MaxValue;
    }

    public struct BaseCostElement : IBufferElementData
    {
        public ushort Value;
    }

    public struct DynamicCostElement : IBufferElementData
    {
        public ushort Value;
    }
}
