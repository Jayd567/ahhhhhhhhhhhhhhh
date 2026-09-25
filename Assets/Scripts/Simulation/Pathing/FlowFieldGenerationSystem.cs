using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Pathing
{
    public static class FlowFieldService
    {
        public static Entity GetOrCreateField(EntityManager em, Entity grid, int destinationCellIndex)
        {
            int currentRevision = em.GetComponentData<GridRevision>(grid).Value;

            EntityQuery query = em.CreateEntityQuery(typeof(FlowFieldDestination));
            using NativeArray<Entity> existing = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
            {
                FlowFieldDestination dest = em.GetComponentData<FlowFieldDestination>(existing[i]);
                if (dest.TargetCellIndex != destinationCellIndex) continue;
                FlowFieldGeneration gen = em.GetComponentData<FlowFieldGeneration>(existing[i]);
                if (gen.GridRevisionGeneratedAt == currentRevision) return existing[i];
                RegenerateInPlace(em, grid, existing[i], destinationCellIndex, currentRevision);
                return existing[i];
            }

            Entity field = em.CreateEntity(typeof(FlowFieldDestination), typeof(FlowFieldGeneration));
            em.SetComponentData(field, new FlowFieldDestination { TargetCellIndex = destinationCellIndex });
            em.AddBuffer<IntegrationCostElement>(field);
            em.AddBuffer<FlowDirectionElement>(field);
            RegenerateInPlace(em, grid, field, destinationCellIndex, currentRevision);
            return field;
        }

        private static void RegenerateInPlace(EntityManager em, Entity grid, Entity field, int destinationCellIndex, int revision)
        {
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            GridDimensions dims = em.GetComponentData<GridDimensions>(grid);
            DynamicBuffer<BaseCostElement> baseCosts = em.GetBuffer<BaseCostElement>(grid);
            DynamicBuffer<DynamicCostElement> dynamicCosts = em.GetBuffer<DynamicCostElement>(grid);

            var integration = new NativeArray<ushort>(cells.Length, Allocator.TempJob);
            var direction = new NativeArray<sbyte>(cells.Length, Allocator.TempJob);
            var costsIn = new NativeArray<ushort>(cells.Length, Allocator.TempJob);
            for (int i = 0; i < cells.Length; i++)
                costsIn[i] = (ushort)math.min(TileCost.Impassable, baseCosts[i].Value + dynamicCosts[i].Value);

            var job = new IntegrationSweepJob
            {
                Width = dims.Width, Height = dims.Height, Destination = destinationCellIndex,
                TotalCost = costsIn, IntegrationCost = integration, FlowDirection = direction,
            };
            job.Schedule().Complete();

            DynamicBuffer<IntegrationCostElement> costBuffer = em.GetBuffer<IntegrationCostElement>(field);
            DynamicBuffer<FlowDirectionElement> dirBuffer = em.GetBuffer<FlowDirectionElement>(field);
            costBuffer.ResizeUninitialized(cells.Length);
            dirBuffer.ResizeUninitialized(cells.Length);
            for (int i = 0; i < cells.Length; i++)
            {
                costBuffer[i] = new IntegrationCostElement { Value = integration[i] };
                dirBuffer[i] = new FlowDirectionElement { Value = direction[i] };
            }

            integration.Dispose(); direction.Dispose(); costsIn.Dispose();
            em.SetComponentData(field, new FlowFieldGeneration { GridRevisionGeneratedAt = revision });
        }
    }

    [BurstCompile]
    public struct IntegrationSweepJob : IJob
    {
        public int Width, Height, Destination;
        [ReadOnly] public NativeArray<ushort> TotalCost;
        public NativeArray<ushort> IntegrationCost;
        public NativeArray<sbyte> FlowDirection;

        // NOTE: the task-7 brief's reference implementation used
        // `private static readonly int[] Dx/Dy` fields for the 8 neighbor offsets. Burst's
        // support for static readonly managed arrays referenced from compiled code is
        // version-sensitive and not guaranteed (see task-7-report.md for the full writeup).
        // With no live Editor connection available to confirm an actual Burst compile in this
        // environment, we can't safely "wait to hit the problem" - so we use the same
        // inline-switch pattern ConnectivitySystem.NeighborIndex already establishes for
        // neighbor offsets in this codebase, extended to 8 directions. This is guaranteed
        // Burst-safe (no managed array indirection at all) and functionally identical.
        private static void Offset(int direction, out int dx, out int dy)
        {
            switch (direction)
            {
                case 0: dx = 0; dy = -1; break;  // N
                case 1: dx = 1; dy = 0; break;   // E
                case 2: dx = 0; dy = 1; break;   // S
                case 3: dx = -1; dy = 0; break;  // W
                case 4: dx = 1; dy = -1; break;  // NE
                case 5: dx = 1; dy = 1; break;   // SE
                case 6: dx = -1; dy = 1; break;  // SW
                default: dx = -1; dy = -1; break; // NW (case 7)
            }
        }

        public void Execute()
        {
            int count = IntegrationCost.Length;
            var heap = new NativeArray<int>(count, Allocator.Temp);
            var position = new NativeArray<int>(count, Allocator.Temp);
            var priority = new NativeArray<int>(count, Allocator.Temp);
            int heapCount = 0;

            for (int i = 0; i < count; i++) { IntegrationCost[i] = ushort.MaxValue; position[i] = -1; FlowDirection[i] = -1; }

            if (TotalCost[Destination] == TileCost.Impassable) { heap.Dispose(); position.Dispose(); priority.Dispose(); return; }

            IntegrationCost[Destination] = 0;
            PushOrDecrease(heap, position, priority, ref heapCount, Destination, 0);

            while (heapCount > 0)
            {
                int current = Pop(heap, position, priority, ref heapCount);
                int cx = current % Width, cy = current / Width;
                for (int d = 0; d < 8; d++)
                {
                    Offset(d, out int dx, out int dy);
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;
                    int n = ny * Width + nx;
                    if (TotalCost[n] == TileCost.Impassable) continue;
                    int candidate = IntegrationCost[current] + TotalCost[n];
                    if (candidate >= IntegrationCost[n]) continue;
                    IntegrationCost[n] = (ushort)candidate;
                    PushOrDecrease(heap, position, priority, ref heapCount, n, candidate);
                }
            }

            // Direction pass: each reached, non-destination cell points at its lowest-cost neighbor.
            for (int i = 0; i < count; i++)
            {
                if (i == Destination || IntegrationCost[i] == ushort.MaxValue) continue;
                int x = i % Width, y = i / Width;
                int best = -1; ushort bestCost = IntegrationCost[i];
                for (int d = 0; d < 8; d++)
                {
                    Offset(d, out int dx, out int dy);
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;
                    int n = ny * Width + nx;
                    if (IntegrationCost[n] >= bestCost) continue;
                    bestCost = IntegrationCost[n]; best = d;
                }
                FlowDirection[i] = (sbyte)best;
            }

            heap.Dispose(); position.Dispose(); priority.Dispose();
        }

        private static void PushOrDecrease(NativeArray<int> heap, NativeArray<int> position, NativeArray<int> priority, ref int count, int node, int prio)
        {
            priority[node] = prio;
            int i = position[node];
            if (i < 0) { i = count++; heap[i] = node; }
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (priority[heap[parent]] <= prio) break;
                heap[i] = heap[parent]; position[heap[i]] = i; i = parent;
            }
            heap[i] = node; position[node] = i;
        }

        private static int Pop(NativeArray<int> heap, NativeArray<int> position, NativeArray<int> priority, ref int count)
        {
            int result = heap[0], node = heap[--count];
            position[result] = -1;
            if (count == 0) return result;
            int i = 0;
            while (i * 2 + 1 < count)
            {
                int child = i * 2 + 1;
                if (child + 1 < count && priority[heap[child + 1]] < priority[heap[child]]) child++;
                if (priority[node] <= priority[heap[child]]) break;
                heap[i] = heap[child]; position[heap[i]] = i; i = child;
            }
            heap[i] = node; position[node] = i;
            return result;
        }
    }
}
