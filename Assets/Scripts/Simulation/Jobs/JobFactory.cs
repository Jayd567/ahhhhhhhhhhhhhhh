using Unity.Collections;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Jobs
{
    public static class JobFactory
    {
        public static bool TryCreateJob(EntityManager em, Entity grid, JobType type, int targetCellIndex, out Entity job)
        {
            job = Entity.Null;
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            if ((uint)targetCellIndex >= (uint)cells.Length) return false;
            CellData target = cells[targetCellIndex].Value;
            if (!target.Walkable && !target.HasRock) return false;

            EntityQuery existing = em.CreateEntityQuery(ComponentType.ReadOnly<JobData>());
            using NativeArray<JobData> allJobs = existing.ToComponentDataArray<JobData>(Allocator.Temp);
            for (int i = 0; i < allJobs.Length; i++)
                if (allJobs[i].TargetCellIndex == targetCellIndex) return false;

            job = em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            em.SetComponentData(job, new JobData { Type = type, TargetCellIndex = targetCellIndex, RemainingWork = 0 });
            em.SetComponentEnabled<Unclaimed>(job, true);
            return true;
        }
    }
}
