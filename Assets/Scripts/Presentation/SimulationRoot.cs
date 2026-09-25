using UnityEngine;
using ColonySim.Simulation.Generation;
using ColonySim.Data;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Presentation
{
    public class SimulationRoot : MonoBehaviour
    {
        public WorldGenerationSettingsSO worldSettings;
        public float[,] Heightmap { get; private set; }
        public int SpawnCell { get; private set; }
        public int mapWidth = 256;
        public int mapHeight = 256;
        public TerrainDefSO floorDef;
        public TerrainDefSO rockDef;
        public TreeDefSO treeDef;
        public PawnTemplateSO colonistTemplate;
        public int startingPawnCount = 3;
        public float ticksPerSecond = 20f;
        public int staggerBucketCount = 5;

        public WorldGrid Grid { get; private set; }
        public PawnManager PawnManager { get; private set; }
        public JobBoard JobBoard { get; private set; }

        private TickManager _tickManager;
        private TerrainJobConfig _floorConfig;
        private TreeJobConfig _treeConfig;
        private float _tickAccumulator;
        private readonly System.Collections.Generic.List<PawnView> _pawnViews = new System.Collections.Generic.List<PawnView>();

        private readonly System.Collections.Generic.Dictionary<int, ColonySim.Simulation.Resources.ResourceItem> _resourceItems
            = new System.Collections.Generic.Dictionary<int, ColonySim.Simulation.Resources.ResourceItem>();

        public bool HasResourceItem(int id) => _resourceItems.ContainsKey(id);

        public System.Action<ColonySim.Simulation.Resources.ResourceItem> OnResourceItemSpawned;

        private readonly System.Collections.Generic.List<ColonySim.Simulation.Resources.ResourceItem> _pendingItems
            = new System.Collections.Generic.List<ColonySim.Simulation.Resources.ResourceItem>();

        public void GenerateWorld()
        {
            if (worldSettings == null) throw new System.InvalidOperationException("Assign World Settings first.");
            // The plain-C# WorldGenerator/GeneratedWorld were deleted in Task 4 of the ECS
            // migration (world generation now runs as Burst jobs via EcsWorldGenerator, which
            // writes into ECS grid components rather than the legacy WorldGrid used here).
            // SimulationRoot's own migration to the ECS grid/pawn/job pipeline is Task 11 of
            // the plan; until then this MonoBehaviour path is intentionally not wired up.
            throw new System.NotSupportedException(
                "SimulationRoot.GenerateWorld is pending migration to EcsWorldGenerator (ECS migration Task 11).");
        }

        private void Awake()
        {
            GenerateWorld();
            _resourceItems.EnsureCapacity(Grid.CellCount);
            _pendingItems.Capacity = Grid.CellCount;
            PawnManager = new PawnManager();
            int spawnSearchStart = SpawnCell;
            int spawnRegion = Grid.GetCell(SpawnCell).ConnectivityId;
            for (int i = 0; i < startingPawnCount; i++)
            {
                int spawnCell = -1;
                for (int offset = 0; offset < Grid.CellCount; offset++)
                {
                    int candidate = (spawnSearchStart + offset) % Grid.CellCount;
                    if (!Grid.GetCell(candidate).IsWalkable || Grid.GetCell(candidate).ConnectivityId != spawnRegion) continue;
                    spawnCell = candidate;
                    spawnSearchStart = candidate + 1;
                    break;
                }
                if (spawnCell < 0) break;
                Grid.TryGetCoordsOf(spawnCell, out int spawnX, out int spawnY);
                int pawnId = PawnManager.SpawnPawn(spawnX, spawnY);
                var pawnGo = new GameObject($"Pawn_{pawnId}");
                pawnGo.transform.SetParent(transform);

                var pawnView = pawnGo.AddComponent<PawnView>();
                pawnView.Initialize(this, pawnId, colonistTemplate);
                _pawnViews.Add(pawnView);
            }

            JobBoard = new JobBoard();
            _tickManager = new TickManager(Grid, PawnManager, JobBoard, new GridAStar(), staggerBucketCount);

            _floorConfig = new TerrainJobConfig
            {
                FloorTerrainTypeId = floorDef.TerrainTypeId,
                YieldResourceType = rockDef.YieldResourceType,
                YieldAmount = rockDef.YieldAmount,
                WorkDurationTicks = rockDef.WorkDurationTicks,
            };
            _treeConfig = new TreeJobConfig
            {
                YieldResourceType = treeDef.YieldResourceType,
                YieldAmount = treeDef.YieldAmount,
                WorkDurationTicks = treeDef.WorkDurationTicks,
            };

            OnResourceItemSpawned += SpawnResourceItemView;
            Camera camera = Camera.main;
            if (camera != null) camera.transform.position = new Vector3(SpawnCell % Grid.Width + 0.5f, SpawnCell / Grid.Width + 0.5f, camera.transform.position.z);
        }

        private void SpawnResourceItemView(ColonySim.Simulation.Resources.ResourceItem item)
        {
            var itemGo = new GameObject($"ResourceItem_{item.Id}");
            itemGo.transform.SetParent(transform);
            itemGo.AddComponent<SpriteRenderer>();
            var itemView = itemGo.AddComponent<ResourceItemView>();
            Color color = item.Type == ColonySim.Simulation.Resources.ResourceType.Stone
                ? rockDef.PlaceholderColor
                : treeDef.PlaceholderColor;
            itemView.Initialize(this, item.Id, item.PositionX, item.PositionY, color);
        }

        private void Update()
        {
            _tickAccumulator += Time.deltaTime;
            float tickInterval = 1f / ticksPerSecond;
            while (_tickAccumulator >= tickInterval)
            {
                _tickAccumulator -= tickInterval;
                _tickManager.Tick(_floorConfig, _treeConfig);

                foreach (var item in _tickManager.SpawnedItemsThisTick)
                {
                    _resourceItems[item.Id] = item;
                    _pendingItems.Add(item);
                }
            }
        }

        private void LateUpdate()
        {
            foreach (var item in _pendingItems) OnResourceItemSpawned?.Invoke(item);
            _pendingItems.Clear();
            for (int i = _pawnViews.Count - 1; i >= 0; i--)
                if (_pawnViews[i] == null || !_pawnViews[i].SyncFromSimulation())
                    _pawnViews.RemoveAt(i);
        }

        public bool TryDesignateMine(int cellIndex)
        {
            if (Grid == null || JobBoard == null || (uint)cellIndex >= (uint)Grid.CellCount) return false;
            Cell cell = Grid.GetCell(cellIndex);
            if (!cell.HasRock) return false;
            return JobBoard.TryAddJob(Grid, JobType.Mine, cellIndex, out _);
        }

        public bool TryDesignateChop(int cellIndex)
        {
            if (Grid == null || JobBoard == null || (uint)cellIndex >= (uint)Grid.CellCount) return false;
            Cell cell = Grid.GetCell(cellIndex);
            if (!cell.HasTree) return false;
            return JobBoard.TryAddJob(Grid, JobType.ChopTree, cellIndex, out _);
        }
    }
}
