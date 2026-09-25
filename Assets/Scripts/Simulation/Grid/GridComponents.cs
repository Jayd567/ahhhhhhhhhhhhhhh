using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct GridSingleton : IComponentData { }

    public struct GridDimensions : IComponentData
    {
        public int Width;
        public int Height;
    }

    public struct GridRevision : IComponentData
    {
        public int Value;
    }

    public struct CellData
    {
        public ushort TerrainId;
        public bool Walkable;
        public bool HasRock;
    }

    public struct CellElement : IBufferElementData
    {
        public CellData Value;
    }
}
