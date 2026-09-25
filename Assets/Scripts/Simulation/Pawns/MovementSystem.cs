using Unity.Entities;
using Unity.Transforms;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Pawns
{
    [UpdateInGroup(typeof(SimulationTickGroup))]
    [UpdateAfter(typeof(Jobs.JobAssignmentSystem))]
    public partial struct MovementSystem : ISystem
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
            WorkDurationConfig config = SystemAPI.GetSingleton<WorkDurationConfig>();

            foreach (var (transform, assignedField, currentJob, entity) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<AssignedFlowField>, RefRO<CurrentJob>>().WithEntityAccess()
                         .WithAll<IsMoving>())
            {
                Entity field = assignedField.ValueRO.Value;
                DynamicBuffer<FlowDirectionElement> directions = em.GetBuffer<FlowDirectionElement>(field);

                int x = (int)transform.ValueRO.Position.x, y = (int)transform.ValueRO.Position.y;
                int cellIndex = y * dims.Width + x;
                sbyte dir = directions[cellIndex].Value;

                if (dir < 0)
                {
                    em.SetComponentEnabled<IsMoving>(entity, false);
                    em.SetComponentEnabled<IsWorking>(entity, true);
                    JobData job = em.GetComponentData<JobData>(currentJob.ValueRO.Value);
                    job.RemainingWork = job.Type == JobType.Mine ? config.MineDurationTicks : config.ChopDurationTicks;
                    em.SetComponentData(currentJob.ValueRO.Value, job);
                    continue;
                }

                (int dx, int dy) = DirectionOffset(dir);
                transform.ValueRW.Position.x = x + dx;
                transform.ValueRW.Position.y = y + dy;
            }
        }

        private static (int, int) DirectionOffset(sbyte dir) => dir switch
        {
            0 => (0, -1), 1 => (1, 0), 2 => (0, 1), 3 => (-1, 0),
            4 => (1, -1), 5 => (1, 1), 6 => (-1, 1), 7 => (-1, -1),
            _ => (0, 0),
        };
    }
}
