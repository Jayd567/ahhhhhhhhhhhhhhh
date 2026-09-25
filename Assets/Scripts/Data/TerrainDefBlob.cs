using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Data
{
    public struct TerrainDefEntry
    {
        public int TerrainTypeId;
        public bool IsWalkable;
        public ushort MovementCost;
    }

    public struct TerrainDefBlob
    {
        public BlobArray<TerrainDefEntry> Entries;
    }

    public static class TerrainDefBlobBuilder
    {
        public static BlobAssetReference<TerrainDefBlob> Build(TerrainDefSO[] defs)
        {
            using var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref TerrainDefBlob root = ref builder.ConstructRoot<TerrainDefBlob>();
            BlobBuilderArray<TerrainDefEntry> array = builder.Allocate(ref root.Entries, defs.Length);
            for (int i = 0; i < defs.Length; i++)
            {
                array[i] = new TerrainDefEntry
                {
                    TerrainTypeId = defs[i].TerrainTypeId,
                    IsWalkable = defs[i].IsWalkable,
                    MovementCost = 1,
                };
            }
            return builder.CreateBlobAssetReference<TerrainDefBlob>(Unity.Collections.Allocator.Persistent);
        }

        public static void PopulateBaseCosts(EntityManager em, Entity grid, BlobAssetReference<TerrainDefBlob> blob)
        {
            int cellCount = em.GetBuffer<CellElement>(grid).Length;

            // Ensure the structural (archetype-changing) parts happen before any
            // DynamicBuffer handles are taken, since AddBuffer/AddComponent
            // invalidate previously-fetched buffer safety handles on this entity.
            if (!em.HasBuffer<BaseCostElement>(grid))
                em.AddBuffer<BaseCostElement>(grid);
            if (!em.HasBuffer<DynamicCostElement>(grid))
                em.AddBuffer<DynamicCostElement>(grid);
            if (!em.HasComponent<TileCostSingleton>(grid))
                em.AddComponent<TileCostSingleton>(grid);

            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            DynamicBuffer<BaseCostElement> baseCosts = em.GetBuffer<BaseCostElement>(grid);
            baseCosts.ResizeUninitialized(cellCount);
            DynamicBuffer<DynamicCostElement> dynamicCosts = em.GetBuffer<DynamicCostElement>(grid);
            dynamicCosts.ResizeUninitialized(cellCount);

            ref TerrainDefBlob defBlob = ref blob.Value;
            for (int i = 0; i < cells.Length; i++)
            {
                dynamicCosts[i] = new DynamicCostElement { Value = 0 };
                CellData cell = cells[i].Value;
                if (!cell.Walkable) { baseCosts[i] = new BaseCostElement { Value = TileCost.Impassable }; continue; }

                ushort cost = 1;
                for (int d = 0; d < defBlob.Entries.Length; d++)
                {
                    if (defBlob.Entries[d].TerrainTypeId != cell.TerrainId) continue;
                    cost = defBlob.Entries[d].MovementCost;
                    break;
                }
                baseCosts[i] = new BaseCostElement { Value = cost };
            }
        }
    }
}
