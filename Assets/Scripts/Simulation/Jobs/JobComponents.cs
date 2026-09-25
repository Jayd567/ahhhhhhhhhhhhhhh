using Unity.Entities;

namespace ColonySim.Simulation.Jobs
{
    public struct JobData : IComponentData
    {
        public JobType Type;
        public int TargetCellIndex;
        public float RemainingWork;
    }

    public struct Unclaimed : IComponentData, IEnableableComponent { }
}
