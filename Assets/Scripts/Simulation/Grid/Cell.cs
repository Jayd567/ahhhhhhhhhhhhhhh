namespace ColonySim.Simulation.Grid
{
    public struct Cell
    {
        public int TerrainTypeId;
        public bool IsWalkable;
        public byte NeighborWalkableMask;
        public int ConnectivityId;
        public int ResourceIdOnGround;
        public bool HasTree;
        public bool HasRock;
        public bool PreserveGroundOnMining;
        public bool HasDesignation;

        public const byte NorthMask = 1 << 0;
        public const byte EastMask = 1 << 1;
        public const byte SouthMask = 1 << 2;
        public const byte WestMask = 1 << 3;
    }
}
