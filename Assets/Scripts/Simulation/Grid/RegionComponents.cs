using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct RegionSingleton : IComponentData { }

    public struct RegionComputedAtRevision : IComponentData
    {
        public int Value;
    }

    public struct RegionElement : IBufferElementData
    {
        public int RegionId;
    }
}
