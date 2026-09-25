using System.Collections.Generic;

namespace ColonySim.Simulation.Pawns
{
    public enum PawnState
    {
        Idle,
        Pathing,
        Working,
    }

    public class Pawn
    {
        public int Id;
        public float PositionX;
        public float PositionY;
        public PawnState State = PawnState.Idle;
        public int CurrentJobId = -1;
        public readonly List<int> CurrentPath = new List<int>();
        public int PathIndex;
        public int WorkTicksRemaining;
    }
}
