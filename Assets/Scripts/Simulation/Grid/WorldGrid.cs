using System;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Grid
{
    public class WorldGrid
    {
        public int Width { get; }
        public int Height { get; }
        public int CellCount => _cells.Length;
        public int Revision { get; private set; }
        public const int UnreachableConnectivityId = -1;
        private readonly Cell[] _cells;
        private readonly int[] _queue;
        private readonly ResourceItem[] _yields;
        private int _nextResourceItemId;

        public WorldGrid(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width; Height = height;
            _cells = new Cell[checked(width * height)];
            _queue = new int[_cells.Length];
            _yields = new ResourceItem[_cells.Length];
            for (int i = 0; i < CellCount; i++) _cells[i].ConnectivityId = -1;
        }

        public int IndexOf(int x, int y) => y * Width + x;
        public bool TryGetCoordsOf(int index, out int x, out int y)
        {
            x = y = 0;
            if ((uint)index >= (uint)CellCount) return false;
            x = index % Width; y = index / Width;
            return true;
        }
        public Cell GetCell(int index) => _cells[index];

        // Generation-only bulk write; finalize before pathfinding.
        public void SetGeneratedCell(int index, int terrainTypeId, bool groundWalkable, bool tree, bool rock)
        {
            _cells[index] = new Cell {
                TerrainTypeId = terrainTypeId, IsWalkable = groundWalkable && !rock,
                HasTree = tree && groundWalkable && !rock, HasRock = rock && groundWalkable,
                PreserveGroundOnMining = true, ConnectivityId = -1
            };
            PrepareYield(index);
        }
        public void CompleteGeneration()
        {
            for (int i = 0; i < CellCount; i++) RecomputeNeighborMask(i);
            RecomputeConnectivity();
            Revision++;
        }

        // Legacy walls remain mineable unless explicitly disabled (e.g. water).
        public void SetTerrain(int index, int terrainTypeId, bool isWalkable, bool isMineable = true)
        {
            _cells[index].TerrainTypeId = terrainTypeId;
            _cells[index].IsWalkable = isWalkable;
            _cells[index].HasRock = !isWalkable && isMineable;
            _cells[index].PreserveGroundOnMining = false;
            PrepareYield(index);
            RefreshTopology(index);
            Revision++;
        }
        private void PrepareYield(int index)
        {
            // Resource objects are allocated on placement, never on work completion.
            if ((_cells[index].HasTree || _cells[index].HasRock) && _yields[index] == null)
                _yields[index] = new ResourceItem();
        }
        private void RefreshTopology(int index)
        {
            RecomputeNeighborMask(index);
            for (int d = 0; d < 4; d++) {
                int n = NeighborIndex(index, d);
                if (n >= 0) RecomputeNeighborMask(n);
            }
            RecomputeConnectivity();
        }
        public void RecomputeConnectivity()
        {
            for (int i = 0; i < CellCount; i++) _cells[i].ConnectivityId = -1;
            int region = 0;
            for (int i = 0; i < CellCount; i++) {
                if (!_cells[i].IsWalkable || _cells[i].ConnectivityId >= 0) continue;
                int head = 0, tail = 0;
                _queue[tail++] = i; _cells[i].ConnectivityId = region;
                while (head < tail) {
                    int current = _queue[head++];
                    foreach (int n in GetWalkableNeighbors(current)) {
                        if (_cells[n].ConnectivityId >= 0) continue;
                        _cells[n].ConnectivityId = region;
                        _queue[tail++] = n;
                    }
                }
                region++;
            }
        }
        public void SetHasTree(int index, bool hasTree)
        {
            _cells[index].HasTree = hasTree;
            PrepareYield(index);
            Revision++;
        }
        public ResourceItem ResolveMineJob(int cellIndex, int floorTerrainTypeId, ResourceType yieldType, int yieldAmount)
        {
            if (!_cells[cellIndex].HasRock) return null;
            if (!_cells[cellIndex].PreserveGroundOnMining) _cells[cellIndex].TerrainTypeId = floorTerrainTypeId;
            _cells[cellIndex].HasRock = false;
            _cells[cellIndex].IsWalkable = true;
            RefreshTopology(cellIndex);
            Revision++;
            return TakeYield(cellIndex, yieldType, yieldAmount);
        }
        public ResourceItem ResolveTreeJob(int cellIndex, ResourceType yieldType, int yieldAmount)
        {
            if (!_cells[cellIndex].HasTree) return null;
            _cells[cellIndex].HasTree = false;
            Revision++;
            return TakeYield(cellIndex, yieldType, yieldAmount);
        }
        private ResourceItem TakeYield(int index, ResourceType type, int amount)
        {
            ResourceItem item = _yields[index];
            _yields[index] = null;
            item.Id = _nextResourceItemId++;
            item.Type = type; item.Amount = amount;
            item.PositionX = index % Width; item.PositionY = index / Width;
            return item;
        }
        private int NeighborIndex(int index, int direction)
        {
            switch (direction) {
                case 0: return index >= Width ? index - Width : -1;
                case 1: return index % Width < Width - 1 ? index + 1 : -1;
                case 2: return index < CellCount - Width ? index + Width : -1;
                default: return index % Width > 0 ? index - 1 : -1;
            }
        }
        private void RecomputeNeighborMask(int index)
        {
            byte mask = 0;
            for (int d = 0; d < 4; d++) {
                int n = NeighborIndex(index, d);
                if (n >= 0 && _cells[n].IsWalkable) mask |= (byte)(1 << d);
            }
            _cells[index].NeighborWalkableMask = mask;
        }
        public NeighborEnumerator GetWalkableNeighbors(int index) => new NeighborEnumerator(this, index);
        // Pattern-based struct enumeration: foreach does not allocate or box.
        public struct NeighborEnumerator
        {
            private readonly WorldGrid _grid;
            private readonly int _index;
            private int _direction;
            public int Current { get; private set; }
            internal NeighborEnumerator(WorldGrid grid, int index)
            { _grid = grid; _index = index; _direction = -1; Current = -1; }
            public NeighborEnumerator GetEnumerator() => this;
            public bool MoveNext()
            {
                while (++_direction < 4) {
                    int n = _grid.NeighborIndex(_index, _direction);
                    if (n < 0 || !_grid._cells[n].IsWalkable) continue;
                    Current = n; return true;
                }
                return false;
            }
        }
    }
}
