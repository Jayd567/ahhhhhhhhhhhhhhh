using System;
using System.Collections.Generic;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Pathing
{
    public class GridAStar : IPathfinder
    {
        private int[] _parent, _cost, _heap, _position, _priority;
        private int _count;
        public void Prepare(int cellCount)
        {
            if (_parent != null && _parent.Length >= cellCount) return;
            _parent = new int[cellCount]; _cost = new int[cellCount];
            _heap = new int[cellCount]; _position = new int[cellCount]; _priority = new int[cellCount];
        }
        public bool TryFindPath(WorldGrid grid, int startIndex, int goalIndex, List<int> resultPath)
        {
            resultPath.Clear();
            if ((uint)startIndex >= (uint)grid.CellCount || (uint)goalIndex >= (uint)grid.CellCount) return false;
            Cell start = grid.GetCell(startIndex), goal = grid.GetCell(goalIndex);
            if (!start.IsWalkable || !goal.IsWalkable || start.ConnectivityId != goal.ConnectivityId) return false;
            // TickManager prepares these once; standalone callers may lazily initialize.
            Prepare(grid.CellCount);
            for (int i = 0; i < grid.CellCount; i++) { _cost[i] = int.MaxValue; _position[i] = -1; }
            _count = 0; _cost[startIndex] = 0;
            PushOrDecrease(startIndex, Heuristic(grid, startIndex, goalIndex));
            while (_count > 0)
            {
                int current = Pop();
                if (current == goalIndex)
                {
                    while (current != startIndex) { resultPath.Add(current); current = _parent[current]; }
                    resultPath.Add(startIndex); resultPath.Reverse(); return true;
                }
                foreach (int neighbor in grid.GetWalkableNeighbors(current))
                {
                    int cost = _cost[current] + 1;
                    if (cost >= _cost[neighbor]) continue;
                    _cost[neighbor] = cost; _parent[neighbor] = current;
                    PushOrDecrease(neighbor, cost + Heuristic(grid, neighbor, goalIndex));
                }
            }
            return false;
        }
        private static int Heuristic(WorldGrid grid, int a, int b)
            => Math.Abs(a % grid.Width - b % grid.Width) + Math.Abs(a / grid.Width - b / grid.Width);
        private void PushOrDecrease(int node, int priority)
        {
            _priority[node] = priority;
            int i = _position[node];
            if (i < 0) { i = _count++; _heap[i] = node; }
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_priority[_heap[parent]] <= priority) break;
                _heap[i] = _heap[parent]; _position[_heap[i]] = i; i = parent;
            }
            _heap[i] = node; _position[node] = i;
        }
        private int Pop()
        {
            int result = _heap[0], node = _heap[--_count];
            _position[result] = -1;
            if (_count == 0) return result;
            int i = 0;
            while (i * 2 + 1 < _count)
            {
                int child = i * 2 + 1;
                if (child + 1 < _count && _priority[_heap[child + 1]] < _priority[_heap[child]]) child++;
                if (_priority[node] <= _priority[_heap[child]]) break;
                _heap[i] = _heap[child]; _position[_heap[i]] = i; i = child;
            }
            _heap[i] = node; _position[node] = i;
            return result;
        }
    }
}
