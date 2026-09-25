using System.Collections.Generic;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Pathing
{
    public interface IPathfinder
    {
        bool TryFindPath(WorldGrid grid, int startIndex, int goalIndex, List<int> resultPath);
    }
}
