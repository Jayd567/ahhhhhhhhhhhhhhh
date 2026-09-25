using System.Collections.Generic;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Jobs
{
    public class JobBoard
    {
        // Interim throwaway holder for the fields the old plain Job class held, kept only so
        // TryClaimJobFor/ReleaseJob/CompleteJob keep compiling until Task 8 deletes this whole file
        // and replaces job matching with ECS systems.
        public class LegacyJob
        {
            public int Id;
            public JobType Type;
            public int TargetCellIndex;
            public int ClaimedByPawnId = -1;
            public int WorkTicksRemaining;
        }

        private readonly List<LegacyJob> _openJobs = new List<LegacyJob>();
        private readonly Dictionary<int, LegacyJob> _jobsById = new Dictionary<int, LegacyJob>();

        public int OpenJobCount => _openJobs.Count;

        public LegacyJob TryClaimJobFor(WorldGrid grid, Pawn pawn)
        {
            if (_openJobs.Count == 0) return null;

            int pawnCellIndex = grid.IndexOf((int)pawn.PositionX, (int)pawn.PositionY);
            int pawnConnectivityId = grid.GetCell(pawnCellIndex).ConnectivityId;

            int bestListIndex = -1;
            int bestDistance = int.MaxValue;


            for (int i = 0; i < _openJobs.Count; i++)
            {
                LegacyJob job = _openJobs[i];
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

            LegacyJob winner = _openJobs[bestListIndex];
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
            if (!_jobsById.TryGetValue(jobId, out LegacyJob job)) return;
            job.ClaimedByPawnId = -1;
            if (!_openJobs.Contains(job))
                _openJobs.Add(job);
        }

        public void CompleteJob(int jobId)
        {
            if (_jobsById.TryGetValue(jobId, out LegacyJob job))
            {
                _openJobs.Remove(job);
                _jobsById.Remove(jobId);
            }
        }
    }
}
