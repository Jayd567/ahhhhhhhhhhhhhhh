using Unity.Entities;

namespace ColonySim.Simulation.Resources
{
    public struct ResourceItemData : IComponentData
    {
        public ResourceType Type;
        public int Amount;
        public int CellIndex;
    }

    public struct PendingResourceSpawn : IComponentData { }
}
