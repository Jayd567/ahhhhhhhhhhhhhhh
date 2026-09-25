using System.Collections.Generic;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Ticking
{
    public struct TerrainJobConfig
    {
        public int FloorTerrainTypeId;
        public ResourceType YieldResourceType;
        public int YieldAmount;
        public int WorkDurationTicks;
    }

    public struct TreeJobConfig
    {
        public ResourceType YieldResourceType;
        public int YieldAmount;
        public int WorkDurationTicks;
    }

    public class TickManager
    {
        public int CurrentTick { get; private set; }
        public readonly List<ResourceItem> SpawnedItemsThisTick = new List<ResourceItem>();

        private readonly WorldGrid _grid;
        private readonly PawnManager _pawnManager;
        private readonly JobBoard _jobBoard;
        private readonly IPathfinder _pathfinder;
        private readonly int _staggerBucketCount;
        private readonly List<int> _scratchPath = new List<int>();
        private readonly Dictionary<int, Job> _activeJobsByPawnId = new Dictionary<int, Job>();
        private readonly System.Action<Pawn> _tickPawnCallback;

        private TerrainJobConfig _currentFloorConfig;
        private TreeJobConfig _currentTreeConfig;

        public TickManager(WorldGrid grid, PawnManager pawnManager, JobBoard jobBoard, IPathfinder pathfinder, int staggerBucketCount)
        {
            if (staggerBucketCount < 1) throw new System.ArgumentOutOfRangeException(nameof(staggerBucketCount));
            _scratchPath.Capacity = grid.CellCount;
            pawnManager.PreparePaths(grid.CellCount);
            SpawnedItemsThisTick.Capacity = System.Math.Max(1, pawnManager.PawnCount);
            _activeJobsByPawnId = new Dictionary<int, Job>(System.Math.Max(1, pawnManager.PawnCount));
            if (pathfinder is GridAStar astar) astar.Prepare(grid.CellCount);
            _grid = grid;
            _pawnManager = pawnManager;
            _jobBoard = jobBoard;
            _pathfinder = pathfinder;
            _staggerBucketCount = staggerBucketCount;
            _tickPawnCallback = TickPawn; // cached once - Tick() must not allocate a new delegate/closure every call
        }

        public void Tick(TerrainJobConfig floorConfig, TreeJobConfig treeConfig)
        {
            SpawnedItemsThisTick.Clear();
            _currentFloorConfig = floorConfig;
            _currentTreeConfig = treeConfig;

            _pawnManager.Tick(CurrentTick, _staggerBucketCount, _tickPawnCallback);

            CurrentTick++;
        }

        private void TickPawn(Pawn pawn)
        {
            switch (pawn.State)
            {
                case PawnState.Idle:
                    TryStartJob(pawn);
                    break;
                case PawnState.Pathing:
                    AdvancePathing(pawn);
                    break;
                case PawnState.Working:
                    AdvanceWorking(pawn);
                    break;
            }
        }

        private void TryStartJob(Pawn pawn)
        {
            Job job = _jobBoard.TryClaimJobFor(_grid, pawn);
            if (job == null) return;

            int startIndex = _grid.IndexOf((int)pawn.PositionX, (int)pawn.PositionY);
            int pawnConnectivityId = _grid.GetCell(startIndex).ConnectivityId;
            int goalIndex = ResolvePathGoal(job.TargetCellIndex, pawnConnectivityId);
            bool found = goalIndex >= 0 && _pathfinder.TryFindPath(_grid, startIndex, goalIndex, _scratchPath);
            if (!found)
            {
                _jobBoard.ReleaseJob(job.Id); // stale connectivity data edge case: skip this tick, stay idle
                return;
            }

            pawn.CurrentPath.Clear();
            pawn.CurrentPath.AddRange(_scratchPath);
            pawn.PathIndex = 0;
            pawn.CurrentJobId = job.Id;
            pawn.State = PawnState.Pathing;
            _activeJobsByPawnId[pawn.Id] = job;
        }

        private int ResolvePathGoal(int targetCellIndex, int pawnConnectivityId)
        {
            // A walkable target (e.g. a tree, which sits on walkable ground) is pathed to
            // directly. An unwalkable target (e.g. a Mine job's rock) can't be a path goal,
            // so the pawn paths to an adjacent walkable cell and works the target from there.
            // The neighbor MUST share the pawn's own connectivity id: a rock can sit between
            // two disconnected regions, and a neighbor in the far region is one A* can never
            // reach, which would make the job retry forever (JobBoard already only offers this
            // job because at least one neighbor matches the pawn's region - this just has to
            // pick that same one, not an arbitrary reachable-or-not neighbor).
            if (_grid.GetCell(targetCellIndex).IsWalkable) return targetCellIndex;

            foreach (int neighborIndex in _grid.GetWalkableNeighbors(targetCellIndex))
            {
                if (_grid.GetCell(neighborIndex).ConnectivityId == pawnConnectivityId)
                    return neighborIndex;
            }

            return -1;
        }

        private void AdvancePathing(Pawn pawn)
        {
            pawn.PathIndex++;
            if (pawn.PathIndex >= pawn.CurrentPath.Count)
            {
                _grid.TryGetCoordsOf(pawn.CurrentPath[pawn.CurrentPath.Count - 1], out int x, out int y);
                pawn.PositionX = x;
                pawn.PositionY = y;

                Job job = _activeJobsByPawnId[pawn.Id];
                job.WorkTicksRemaining = job.Type == JobType.Mine
                    ? _currentFloorConfig.WorkDurationTicks
                    : _currentTreeConfig.WorkDurationTicks;
                pawn.State = PawnState.Working;
            }
            else
            {
                _grid.TryGetCoordsOf(pawn.CurrentPath[pawn.PathIndex], out int x, out int y);
                pawn.PositionX = x;
                pawn.PositionY = y;
            }
        }

        private void AdvanceWorking(Pawn pawn)
        {
            Job job = _activeJobsByPawnId[pawn.Id];

            job.WorkTicksRemaining--;
            if (job.WorkTicksRemaining > 0) return;

            ResourceItem item = job.Type == JobType.Mine
                ? _grid.ResolveMineJob(job.TargetCellIndex, _currentFloorConfig.FloorTerrainTypeId, _currentFloorConfig.YieldResourceType, _currentFloorConfig.YieldAmount)
                : _grid.ResolveTreeJob(job.TargetCellIndex, _currentTreeConfig.YieldResourceType, _currentTreeConfig.YieldAmount);

            if (item != null)
                SpawnedItemsThisTick.Add(item);
            // item == null means the target was already resolved out from under this pawn
            // (Review Focus: target invalidated mid-execution) - no duplicate item, fall through to idle.

            _jobBoard.CompleteJob(job.Id);
            _activeJobsByPawnId.Remove(pawn.Id);
            pawn.CurrentJobId = -1;
            pawn.CurrentPath.Clear();
            pawn.PathIndex = 0;
            pawn.State = PawnState.Idle;
        }
    }
}
