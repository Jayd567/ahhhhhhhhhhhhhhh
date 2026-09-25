using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    [BurstCompile]
    public partial struct ConnectivitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity grid = SystemAPI.GetSingletonEntity<GridSingleton>();
            int revision = SystemAPI.GetComponentRO<GridRevision>(grid).ValueRO.Value;

            EntityQuery regionQuery = SystemAPI.QueryBuilder().WithAll<RegionSingleton>().Build();
            Entity regionEntity;
            if (regionQuery.IsEmptyIgnoreFilter)
            {
                regionEntity = state.EntityManager.CreateEntity(typeof(RegionSingleton), typeof(RegionComputedAtRevision));
                state.EntityManager.AddBuffer<RegionElement>(regionEntity);
                state.EntityManager.SetComponentData(regionEntity, new RegionComputedAtRevision { Value = -1 });
            }
            else
            {
                regionEntity = regionQuery.GetSingletonEntity();
            }

            int computedAt = state.EntityManager.GetComponentData<RegionComputedAtRevision>(regionEntity).Value;
            if (computedAt == revision) return;

            DynamicBuffer<CellElement> cells = state.EntityManager.GetBuffer<CellElement>(grid);
            GridDimensions dims = SystemAPI.GetComponentRO<GridDimensions>(grid).ValueRO;
            DynamicBuffer<RegionElement> regions = state.EntityManager.GetBuffer<RegionElement>(regionEntity);
            regions.ResizeUninitialized(cells.Length);

            var queue = new NativeArray<int>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++) regions[i] = new RegionElement { RegionId = -1 };

            int region = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].Value.Walkable || regions[i].RegionId >= 0) continue;
                int head = 0, tail = 0;
                queue[tail++] = i;
                regions[i] = new RegionElement { RegionId = region };
                while (head < tail)
                {
                    int current = queue[head++];
                    int cx = current % dims.Width, cy = current / dims.Width;
                    for (int d = 0; d < 4; d++)
                    {
                        int n = NeighborIndex(current, cx, cy, dims.Width, dims.Height, d);
                        if (n < 0 || !cells[n].Value.Walkable || regions[n].RegionId >= 0) continue;
                        regions[n] = new RegionElement { RegionId = region };
                        queue[tail++] = n;
                    }
                }
                region++;
            }
            queue.Dispose();

            state.EntityManager.SetComponentData(regionEntity, new RegionComputedAtRevision { Value = revision });
        }

        private static int NeighborIndex(int index, int x, int y, int width, int height, int direction)
        {
            switch (direction)
            {
                case 0: return y > 0 ? index - width : -1;
                case 1: return x < width - 1 ? index + 1 : -1;
                case 2: return y < height - 1 ? index + width : -1;
                default: return x > 0 ? index - 1 : -1;
            }
        }
    }
}
