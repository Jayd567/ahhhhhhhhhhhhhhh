using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public static class GridBootstrap
    {
        public static Entity CreateGrid(EntityManager em, int width, int height)
        {
            Entity grid = em.CreateEntity(typeof(GridSingleton), typeof(GridDimensions), typeof(GridRevision));
            em.SetComponentData(grid, new GridDimensions { Width = width, Height = height });
            em.SetComponentData(grid, new GridRevision { Value = 0 });
            DynamicBuffer<CellElement> cells = em.AddBuffer<CellElement>(grid);
            cells.ResizeUninitialized(width * height);
            return grid;
        }
    }
}
