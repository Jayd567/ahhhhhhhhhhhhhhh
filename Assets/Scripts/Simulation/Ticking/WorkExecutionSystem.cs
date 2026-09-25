using Unity.Entities;
using ColonySim.Simulation.Generation;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Ticking
{
    public struct SimulationTick : IComponentData
    {
        public int Value;
        public int StaggerBucketCount;
    }

    public struct WorkDurationConfig : IComponentData
    {
        public int MineDurationTicks;
        public int ChopDurationTicks;
        public int FloorTerrainTypeId;
        public ResourceType MineYieldType;
        public int MineYieldAmount;
        public ResourceType ChopYieldType;
        public int ChopYieldAmount;
    }

    [UpdateInGroup(typeof(SimulationTickGroup))]
    [UpdateAfter(typeof(Pawns.MovementSystem))]
    public partial struct WorkExecutionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSingleton>();
            state.RequireForUpdate<SimulationTick>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            Entity grid = SystemAPI.GetSingletonEntity<GridSingleton>();
            SimulationTick tick = SystemAPI.GetSingleton<SimulationTick>();
            WorkDurationConfig config = SystemAPI.GetSingleton<WorkDurationConfig>();
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);

            foreach (var (currentJob, tickOffset, entity) in
                     SystemAPI.Query<RefRW<CurrentJob>, RefRO<TickOffset>>().WithEntityAccess().WithAll<IsWorking>())
            {
                if (tickOffset.ValueRO.Value != tick.Value % tick.StaggerBucketCount) continue;

                Entity job = currentJob.ValueRO.Value;
                if (!em.Exists(job)) { ReturnToIdle(em, entity, currentJob); continue; }

                JobData jobData = em.GetComponentData<JobData>(job);
                jobData.RemainingWork -= 1;
                if (jobData.RemainingWork > 0) { em.SetComponentData(job, jobData); continue; }

                CellData cell = cells[jobData.TargetCellIndex].Value;
                bool resolved = jobData.Type == JobType.Mine ? cell.HasRock : true;
                if (jobData.Type == JobType.ChopTree)
                {
                    EntityQuery treeQuery = em.CreateEntityQuery(typeof(TreeTag), typeof(TreeCellIndex));
                    resolved = false;
                    using var trees = treeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                    foreach (Entity t in trees)
                        if (em.GetComponentData<TreeCellIndex>(t).Value == jobData.TargetCellIndex) { resolved = true; em.DestroyEntity(t); break; }
                }

                if (resolved)
                {
                    ResourceType yieldType = jobData.Type == JobType.Mine ? config.MineYieldType : config.ChopYieldType;
                    int yieldAmount = jobData.Type == JobType.Mine ? config.MineYieldAmount : config.ChopYieldAmount;

                    if (jobData.Type == JobType.Mine)
                    {
                        cell.HasRock = false;
                        cell.Walkable = true;
                        cell.TerrainId = (ushort)config.FloorTerrainTypeId;
                        cells[jobData.TargetCellIndex] = new CellElement { Value = cell };
                        GridRevision rev = em.GetComponentData<GridRevision>(grid);
                        em.SetComponentData(grid, new GridRevision { Value = rev.Value + 1 });
                    }

                    Entity resourceEntity = em.CreateEntity(typeof(ResourceItemData), typeof(PendingResourceSpawn));
                    em.SetComponentData(resourceEntity, new ResourceItemData { Type = yieldType, Amount = yieldAmount, CellIndex = jobData.TargetCellIndex });
                }
                // resolved == false: target invalidated mid-execution (Review Focus) - no
                // duplicate item, fall through to idle, matching the previous TickManager behavior.

                em.DestroyEntity(job);
                ReturnToIdle(em, entity, currentJob);
            }
        }

        private static void ReturnToIdle(EntityManager em, Entity pawn, RefRW<CurrentJob> currentJob)
        {
            currentJob.ValueRW.Value = Entity.Null;
            em.SetComponentData(pawn, new AssignedFlowField { Value = Entity.Null });
            em.SetComponentEnabled<IsWorking>(pawn, false);
        }
    }
}
