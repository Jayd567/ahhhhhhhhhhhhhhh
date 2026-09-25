namespace ColonySim.Simulation.Jobs
{
    public class Job
    {
        public int Id;
        public JobType Type;
        public int TargetCellIndex;
        public int ClaimedByPawnId = -1;
        public int WorkTicksRemaining;
    }
}
