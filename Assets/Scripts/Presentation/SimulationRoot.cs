using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ColonySim.Data;
using ColonySim.Simulation.Generation;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Presentation
{
    public class SimulationRoot : MonoBehaviour
    {
        public WorldGenerationSettingsSO worldSettings;
        public int mapWidth = 256;
        public int mapHeight = 256;
        public TerrainDefSO floorDef;
        public TerrainDefSO rockDef;
        public TreeDefSO treeDef;
        public PawnTemplateSO colonistTemplate;
        public int startingPawnCount = 3;
        public float ticksPerSecond = 20f;
        public int staggerBucketCount = 5;

        public World World { get; private set; }
        public Entity GridEntity { get; private set; }
        public int SpawnCell { get; private set; }

        private EntityManager _em;
        private float _tickAccumulator;
        private readonly List<PawnView> _pawnViews = new List<PawnView>();
        private readonly HashSet<int> _resourceItemIds = new HashSet<int>();

        public bool HasResourceItem(int id) => _resourceItemIds.Contains(id);
        public System.Action<int, ResourceType, int, float, float> OnResourceItemSpawned;

        public void GenerateWorld()
        {
            if (worldSettings == null) throw new System.InvalidOperationException("Assign World Settings first.");
            World = World.DefaultGameObjectInjectionWorld;
            _em = World.EntityManager;
            GridEntity = GridBootstrap.CreateGrid(_em, mapWidth, mapHeight);
            var generationSettings = worldSettings.CreateEcsSettings();
            SpawnCell = EcsWorldGenerator.Generate(_em, GridEntity, generationSettings);

            var blob = TerrainDefBlobBuilder.Build(new[] { worldSettings.Water, worldSettings.Dirt, worldSettings.Grass, worldSettings.Stone });
            TerrainDefBlobBuilder.PopulateBaseCosts(_em, GridEntity, blob);
            blob.Dispose();
        }

        private void Awake()
        {
            GenerateWorld();

            Entity tickEntity = _em.CreateEntity(typeof(SimulationTick));
            _em.SetComponentData(tickEntity, new SimulationTick { Value = 0, StaggerBucketCount = staggerBucketCount });

            Entity configEntity = _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(configEntity, new WorkDurationConfig
            {
                MineDurationTicks = rockDef.WorkDurationTicks,
                FloorTerrainTypeId = floorDef.TerrainTypeId,
                MineYieldType = rockDef.YieldResourceType,
                MineYieldAmount = rockDef.YieldAmount,
                ChopDurationTicks = treeDef.WorkDurationTicks,
                ChopYieldType = treeDef.YieldResourceType,
                ChopYieldAmount = treeDef.YieldAmount,
            });

            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(GridEntity);
            GridDimensions dims = _em.GetComponentData<GridDimensions>(GridEntity);
            int spawnSearchStart = SpawnCell;
            var regionEntity = _em.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = _em.GetBuffer<RegionElement>(regionEntity);
            int spawnRegion = regions[SpawnCell].RegionId;

            for (int i = 0; i < startingPawnCount; i++)
            {
                int spawnCell = -1;
                for (int offset = 0; offset < cells.Length; offset++)
                {
                    int candidate = (spawnSearchStart + offset) % cells.Length;
                    if (!cells[candidate].Value.Walkable || regions[candidate].RegionId != spawnRegion) continue;
                    spawnCell = candidate;
                    spawnSearchStart = candidate + 1;
                    break;
                }
                if (spawnCell < 0) break;

                int spawnX = spawnCell % dims.Width, spawnY = spawnCell / dims.Width;
                Entity pawn = PawnFactory.CreatePawn(_em, i, new float2(spawnX, spawnY), i % staggerBucketCount);

                var pawnGo = new GameObject($"Pawn_{i}");
                pawnGo.transform.SetParent(transform);
                var pawnView = pawnGo.AddComponent<PawnView>();
                pawnView.Initialize(this, i, colonistTemplate);
                _pawnViews.Add(pawnView);
            }

            OnResourceItemSpawned += SpawnResourceItemView;
            Camera camera = Camera.main;
            if (camera != null) camera.transform.position = new Vector3(SpawnCell % dims.Width + 0.5f, SpawnCell / dims.Width + 0.5f, camera.transform.position.z);
        }

        private void SpawnResourceItemView(int id, ResourceType type, int amount, float x, float y)
        {
            var itemGo = new GameObject($"ResourceItem_{id}");
            itemGo.transform.SetParent(transform);
            itemGo.AddComponent<SpriteRenderer>();
            var itemView = itemGo.AddComponent<ResourceItemView>();
            Color color = type == ResourceType.Stone ? rockDef.PlaceholderColor : treeDef.PlaceholderColor;
            itemView.Initialize(this, id, x, y, color);
        }

        private void Update()
        {
            _tickAccumulator += Time.deltaTime;
            float tickInterval = 1f / ticksPerSecond;
            while (_tickAccumulator >= tickInterval)
            {
                _tickAccumulator -= tickInterval;
                World.GetExistingSystemManaged<SimulationTickGroup>().Update();
            }
        }

        private void LateUpdate()
        {
            EntityQuery pending = _em.CreateEntityQuery(typeof(ResourceItemData), typeof(PendingResourceSpawn));
            using NativeArray<Entity> spawned = pending.ToEntityArray(Allocator.Temp);
            GridDimensions dims = _em.GetComponentData<GridDimensions>(GridEntity);
            foreach (Entity e in spawned)
            {
                ResourceItemData data = _em.GetComponentData<ResourceItemData>(e);
                int id = e.Index;
                _resourceItemIds.Add(id);
                OnResourceItemSpawned?.Invoke(id, data.Type, data.Amount, data.CellIndex % dims.Width, data.CellIndex / dims.Width);
                _em.RemoveComponent<PendingResourceSpawn>(e);
            }

            for (int i = _pawnViews.Count - 1; i >= 0; i--)
                if (_pawnViews[i] == null || !_pawnViews[i].SyncFromSimulation())
                    _pawnViews.RemoveAt(i);
        }

        public bool TryDesignateMine(int cellIndex)
        {
            if (_em.Equals(default(EntityManager))) return false;
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(GridEntity);
            if ((uint)cellIndex >= (uint)cells.Length || !cells[cellIndex].Value.HasRock) return false;
            return JobFactory.TryCreateJob(_em, GridEntity, JobType.Mine, cellIndex, out _);
        }

        public bool TryDesignateChop(int cellIndex)
        {
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(GridEntity);
            if ((uint)cellIndex >= (uint)cells.Length) return false;
            EntityQuery treeQuery = _em.CreateEntityQuery(typeof(TreeTag), typeof(TreeCellIndex));
            using NativeArray<TreeCellIndex> trees = treeQuery.ToComponentDataArray<TreeCellIndex>(Allocator.Temp);
            bool hasTree = false;
            for (int i = 0; i < trees.Length; i++) if (trees[i].Value == cellIndex) { hasTree = true; break; }
            if (!hasTree) return false;
            return JobFactory.TryCreateJob(_em, GridEntity, JobType.ChopTree, cellIndex, out _);
        }
    }
}
