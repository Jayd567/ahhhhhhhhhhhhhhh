using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class JobBoardTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, 0, isWalkable: true);
            return grid;
        }

        [Test]
        public void TryAddJob_DuplicateTarget_IsRejected()
        {
            var grid = MakeOpenGrid(3, 3);
            var board = new JobBoard();
            int target = grid.IndexOf(1, 1);

            bool first = board.TryAddJob(grid, JobType.Mine, target, out _);
            bool second = board.TryAddJob(grid, JobType.Mine, target, out _);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, board.OpenJobCount);
        }

        [Test]
        public void TryClaimJobFor_UnreachableJob_ReturnsNull()
        {
            var grid = new WorldGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false); // wall splits the grid
            grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(2, 0), out _);

            var pawn = new Pawn { PositionX = 0, PositionY = 0 };
            Job claimed = board.TryClaimJobFor(grid, pawn);

            Assert.IsNull(claimed);
        }

        [Test]
        public void TryClaimJobFor_PicksNearestReachableJob()
        {
            var grid = MakeOpenGrid(10, 1);
            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(8, 0), out int farId);
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(2, 0), out int nearId);

            var pawn = new Pawn { PositionX = 0, PositionY = 0 };
            Job claimed = board.TryClaimJobFor(grid, pawn);

            Assert.AreEqual(nearId, claimed.Id);
        }

        [Test]
        public void TryClaimJobFor_SecondCallSameTick_DoesNotClaimSameJobTwice()
        {
            var grid = MakeOpenGrid(5, 1);
            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(4, 0), out int jobId);

            var pawnA = new Pawn { PositionX = 0, PositionY = 0 };
            var pawnB = new Pawn { PositionX = 1, PositionY = 0 };

            Job claimedByA = board.TryClaimJobFor(grid, pawnA);
            Job claimedByB = board.TryClaimJobFor(grid, pawnB);

            Assert.AreEqual(jobId, claimedByA.Id);
            Assert.IsNull(claimedByB);
        }

        [Test]
        public void TryClaimJobFor_NoOpenJobs_ReturnsNull()
        {
            var grid = MakeOpenGrid(3, 3);
            var board = new JobBoard();
            var pawn = new Pawn { PositionX = 0, PositionY = 0 };

            Assert.IsNull(board.TryClaimJobFor(grid, pawn));
        }
    }
}
