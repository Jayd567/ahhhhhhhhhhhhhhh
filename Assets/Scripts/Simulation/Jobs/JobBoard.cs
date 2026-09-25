using System.Collections.Generic;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Jobs
{
    public class JobBoard
    {
        private readonly List<Job> _openJobs = new List<Job>();
        private readonly Dictionary<int, Job> _jobsById = new Dictionary<int, Job>();
        private int _nextJobId;

        public int OpenJobCount => _openJobs.Count;

        public bool TryAddJob(WorldGrid grid, JobType type, int targetCellIndex, out int jobId)
        {
            if ((uint)targetCellIndex >= (uint)grid.CellCount || (!grid.GetCell(targetCellIndex).IsWalkable && !grid.GetCell(targetCellIndex).HasRock)) { jobId = -1; return false; }
            for (int i = 0; i < _openJobs.Count; i++)
            {
                if (_openJobs[i].TargetCellIndex == targetCellIndex)
                {
                    jobId = -1;
                    return false;
                }
            }
            foreach (Job job in _jobsById.Values)
            {
                if (job.ClaimedByPawnId >= 0 && job.TargetCellIndex == targetCellIndex)
                {
                    jobId = -1;
                    return false;
                }
            }

            var newJob = new Job { Id = _nextJobId++, Type = type, TargetCellIndex = targetCellIndex };
            _openJobs.Add(newJob);
            _jobsById[newJob.Id] = newJob;
            jobId = newJob.Id;
            return true;
        }

        public Job TryClaimJobFor(WorldGrid grid, Pawn pawn)
        {
            if (_openJobs.Count == 0) return null;

            int pawnCellIndex = grid.IndexOf((int)pawn.PositionX, (int)pawn.PositionY);
            int pawnConnectivityId = grid.GetCell(pawnCellIndex).ConnectivityId;

            int bestListIndex = -1;
            int bestDistance = int.MaxValue;


            for (int i = 0; i < _openJobs.Count; i++)
            {
                Job job = _openJobs[i];
                if (!IsReachable(grid, job.TargetCellIndex, pawnConnectivityId)) continue;

                grid.TryGetCoordsOf(job.TargetCellIndex, out int tx, out int ty);
                int distance = System.Math.Abs(tx - (int)pawn.PositionX) + System.Math.Abs(ty - (int)pawn.PositionY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestListIndex = i;
                }
            }

            if (bestListIndex < 0) return null;

            Job winner = _openJobs[bestListIndex];
            _openJobs.RemoveAt(bestListIndex);
            winner.ClaimedByPawnId = pawn.Id;
            return winner;
        }

        private static bool IsReachable(WorldGrid grid, int targetCellIndex, int pawnConnectivityId)
        {
            Cell targetCell = grid.GetCell(targetCellIndex);
            if (targetCell.IsWalkable)
                return targetCell.ConnectivityId == pawnConnectivityId;

            // Target itself is unwalkable (e.g. a Mine job's rock cell): reachable if any
            // adjacent walkable cell shares the pawn's connectivity id, since the pawn
            // will path next to it rather than onto it.
            foreach (int neighborIndex in grid.GetWalkableNeighbors(targetCellIndex))
            {
                if (grid.GetCell(neighborIndex).ConnectivityId == pawnConnectivityId)
                    return true;
            }
            return false;
        }

        public void ReleaseJob(int jobId)
        {
            if (!_jobsById.TryGetValue(jobId, out Job job)) return;
            job.ClaimedByPawnId = -1;
            if (!_openJobs.Contains(job))
                _openJobs.Add(job);
        }

        public void CompleteJob(int jobId)
        {
            if (_jobsById.TryGetValue(jobId, out Job job))
            {
                _openJobs.Remove(job);
                _jobsById.Remove(jobId);
            }
        }
    }
}
