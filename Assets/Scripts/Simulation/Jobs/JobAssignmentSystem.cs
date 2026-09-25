using Unity.Collections;
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Jobs
{
    [UpdateInGroup(typeof(SimulationTickGroup))]
    [UpdateAfter(typeof(ConnectivitySystem))]
    public partial struct JobAssignmentSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            Entity grid = SystemAPI.GetSingletonEntity<GridSingleton>();
            GridDimensions dims = em.GetComponentData<GridDimensions>(grid);
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            Entity regionEntity = em.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);

            EntityQuery jobQuery = em.CreateEntityQuery(ComponentType.ReadOnly<JobData>(), ComponentType.ReadOnly<Unclaimed>());
            using NativeArray<Entity> unclaimedJobs = jobQuery.ToEntityArray(Allocator.Temp);
            if (unclaimedJobs.Length == 0) return;

            foreach (var (transform, currentJob, entity) in
                     SystemAPI.Query<RefRO<Unity.Transforms.LocalTransform>, RefRW<CurrentJob>>().WithEntityAccess()
                         .WithDisabled<IsMoving>().WithDisabled<IsWorking>())
            {
                if (currentJob.ValueRO.Value != Entity.Null) continue;

                int px = (int)transform.ValueRO.Position.x, py = (int)transform.ValueRO.Position.y;
                int pawnCellIndex = py * dims.Width + px;
                int pawnRegion = regions[pawnCellIndex].RegionId;

                Entity bestJob = Entity.Null;
                Entity bestField = Entity.Null;
                int bestCost = int.MaxValue;

                for (int i = 0; i < unclaimedJobs.Length; i++)
                {
                    JobData job = em.GetComponentData<JobData>(unclaimedJobs[i]);
                    if (!IsReachable(cells, regions, dims, job.TargetCellIndex, pawnRegion)) continue;

                    Entity field = FlowFieldService.GetOrCreateField(em, grid, job.TargetCellIndex);
                    DynamicBuffer<IntegrationCostElement> costs = em.GetBuffer<IntegrationCostElement>(field);
                    int cost = costs[pawnCellIndex].Value;
                    if (cost >= bestCost) continue;

                    bestCost = cost; bestJob = unclaimedJobs[i]; bestField = field;
                }

                if (bestJob == Entity.Null) continue;

                currentJob.ValueRW.Value = bestJob;
                em.SetComponentData(entity, new AssignedFlowField { Value = bestField });
                em.SetComponentEnabled<Unclaimed>(bestJob, false);
            }
        }

        private static bool IsReachable(DynamicBuffer<CellElement> cells, DynamicBuffer<RegionElement> regions, GridDimensions dims, int targetCellIndex, int pawnRegion)
        {
            CellData target = cells[targetCellIndex].Value;
            if (target.Walkable) return regions[targetCellIndex].RegionId == pawnRegion;

            int x = targetCellIndex % dims.Width, y = targetCellIndex / dims.Width;
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d], ny = y + dy[d];
                if (nx < 0 || nx >= dims.Width || ny < 0 || ny >= dims.Height) continue;
                int n = ny * dims.Width + nx;
                if (cells[n].Value.Walkable && regions[n].RegionId == pawnRegion) return true;
            }
            return false;
        }
    }
}
