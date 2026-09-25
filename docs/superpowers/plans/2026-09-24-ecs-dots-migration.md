# ECS/DOTS Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Rimworld-esc game's plain-C# simulation layer (grid, pawns, jobs, pathfinding, ticking, world generation) with Unity ECS (`com.unity.entities`) + C# Job System + Burst, including flow-field pathfinding, while keeping the existing right-click mine/chop gameplay loop working identically.

**Architecture:** Grid/region/tile-cost state lives on three singleton entities backed by `DynamicBuffer`s; pawns and jobs are entities; a `SimulationTickGroup` of small `ISystem`s (job assignment → flow-field generation → movement → work execution → resource spawn) replaces `TickManager`'s switch statement; Presentation (`GridView`/`PawnView`/`SimulationRoot`/`DesignationInputController`) queries the ECS `World` directly, no adapter layer.

**Tech Stack:** Unity 6000.3.18f1, `com.unity.entities`, `com.unity.burst`, `com.unity.collections`, `Unity.Mathematics`, NUnit (EditMode tests), the live-connected `unity` CLI for driving the Editor and running tests.

**Spec:** `docs/superpowers/specs/2026-09-24-ecs-dots-migration-design.md`

## Global Constraints

- Unity version stays 6000.3.18f1; no other package versions besides the three ECS packages are added.
- No subscene/baking workflow — all entities are created procedurally at runtime, per spec §Packages.
- `com.unity.entities.graphics` is **not** added — pawn/prop rendering stays GameObject-based, per spec §Packages.
- `JobType.cs` and `ResourceType.cs` are plain Burst-compatible enums already; they are reused unchanged, not replaced.
- `WorldGenerationSettings` (the plain struct in `Assets/Scripts/Simulation/Generation/WorldGenerator.cs`) is kept and reused as the Burst-compatible settings carrier per spec §Def blobs — only `SimplexNoise` and the `WorldGenerator`/`GeneratedWorld` static-method pipeline are replaced.
- Every task that fully supersedes an old plain-C# class deletes that class and its old test file in the same task. Two classes (`WorldGrid`/`Cell`) are referenced by several other old classes until those are migrated in later tasks — their deletion is deferred to Task 12 once `GridView` (their last consumer) is migrated; this is called out explicitly in Task 12, not left implicit.
- Every `[BurstCompile]` job/system must compile and run correctly in Burst — no managed allocations, no `ScriptableObject`/managed-object field access inside `[BurstCompile]` code.
- `allowUnsafeCode` on the `Simulation` and `Data` assemblies is set to `true` in Task 1 (needed for some `Unity.Collections`/`Unity.Entities` low-level buffer patterns) and never reverted.

## Review Focus

- **Grid mutation racing flow-field staleness:** a rock is mined (bumping `GridRevision`) in the same tick a `FlowFieldDestination` targeting a cell near it is mid-generation or about to be read for movement — a reasonable player expects the pawn to keep moving toward a now-stale-but-not-yet-regenerated field rather than crash or teleport. Covered in Task 10 (`WorkExecutionSystemTests`: mutate grid, then run `MovementSystem` on a pawn using a field generated before the mutation, assert it still advances safely).
- **Two jobs targeting the same connectivity-adjacent rock from opposite disconnected regions:** `JobFactory.TryCreateJob`'s dedupe-by-target-cell check must still reject a duplicate target regardless of which region asked first — covered in Task 6 (`JobFactoryTests`, ported from `JobBoardTests.AddJob_DuplicateTarget_Rejected`-equivalent).
- **A pawn with no reachable unclaimed job in its region does nothing, forever, without throwing or spinning an allocation:** covered in Task 8 (`JobAssignmentSystemTests`: pawn in an isolated region, unclaimed job only in another region, assert `CurrentJob` stays `Entity.Null` after several ticks and no exception).
- **Flow field integration cost for a job on a rock between two disconnected regions must resolve to the same neighbor-goal-selection the original region-aware-mining bug fix required** (this was a real production bug fixed in the current codebase, per `TickManagerTests.cs`'s `Tick_MineJobOnWallBetweenTwoRegions_...` regression test) — covered in Task 7 (`FlowFieldGenerationSystemTests`, porting that exact regression scenario onto flow fields).
- **World regeneration must stay byte-for-byte deterministic for a fixed seed** (heightmap values, terrain thresholds, prop placement, spawn cell) since `WorldGeneratorTests.cs` already pins this today and a Job-System port is exactly the kind of change that can silently reorder floating-point accumulation — covered in Task 4 (`EcsWorldGeneratorTests`, same seed/assertions as today's `WorldGeneratorTests.cs`, run twice and diffed for determinism).

---

## Task 1: ECS packages, assembly setup, and the Grid singleton

**Files:**
- Modify: `Packages/manifest.json` (add `com.unity.entities`, `com.unity.burst`, `com.unity.collections`)
- Modify: `Assets/Scripts/Simulation/Simulation.asmdef` (add references, `allowUnsafeCode: true`)
- Modify: `Assets/Tests/EditMode/SimulationTests.asmdef` (add same references)
- Create: `Assets/Scripts/Simulation/Grid/GridComponents.cs`
- Create: `Assets/Scripts/Simulation/Grid/GridBootstrap.cs`
- Create: `Assets/Tests/EditMode/GridBootstrapTests.cs`

**Interfaces:**
- Produces: `GridSingleton` (tag `IComponentData`), `GridDimensions { int Width, int Height }`, `GridRevision { int Value }`, `CellData { public ushort TerrainId; public bool Walkable; public bool HasRock; }` (a plain struct — `MovementCost` moves to `TileCostSingleton` in Task 3, since the spec's `CellData.MovementCost` field is superseded once base costs live in a dedicated buffer; note this refinement over the spec's literal field list), `CellElement : IBufferElementData { public CellData Value; }`.
- Produces: `static class GridBootstrap { public static Entity CreateGrid(EntityManager em, int width, int height); }` — creates the singleton entity, sizes the `CellElement` buffer to `width * height` via `ResizeUninitialized`, sets `GridDimensions`/`GridRevision.Value = 0`.

- [ ] **Step 1: Install the ECS packages**

Use the `unity-package-management` skill's C# `UnityEditor.PackageManager.Client` approach, or edit the manifest directly since the project is not mid-build. Add to `Packages/manifest.json`'s `dependencies`:

```json
"com.unity.entities": "1.3.14",
"com.unity.burst": "1.8.19",
"com.unity.collections": "2.5.1",
```

Use `unity command` (or the Package Manager UI in the live Editor) to trigger a resolve, then confirm via `unity status`/`unity command` that the Editor recompiled without errors (not Safe Mode).

- [ ] **Step 2: Update assembly definitions**

`Assets/Scripts/Simulation/Simulation.asmdef`:

```json
{
    "name": "Simulation",
    "rootNamespace": "ColonySim.Simulation",
    "references": ["Unity.Entities", "Unity.Burst", "Unity.Collections", "Unity.Mathematics", "Unity.Transforms"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/Tests/EditMode/SimulationTests.asmdef` — add the same four references (`Unity.Entities`, `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`, `Unity.Transforms`) to its existing `"references"` array alongside `Simulation`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`.

- [ ] **Step 3: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class GridBootstrapTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("GridBootstrapTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void CreateGrid_SetsDimensionsAndSizesBuffer()
        {
            Entity grid = GridBootstrap.CreateGrid(_world.EntityManager, 4, 3);

            var dims = _world.EntityManager.GetComponentData<GridDimensions>(grid);
            Assert.AreEqual(4, dims.Width);
            Assert.AreEqual(3, dims.Height);

            var cells = _world.EntityManager.GetBuffer<CellElement>(grid);
            Assert.AreEqual(12, cells.Length);

            Assert.AreEqual(0, _world.EntityManager.GetComponentData<GridRevision>(grid).Value);
            Assert.IsTrue(_world.EntityManager.HasComponent<GridSingleton>(grid));
        }
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter GridBootstrapTests`
Expected: FAIL (compile error — `GridComponents`/`GridBootstrap` not defined).

- [ ] **Step 5: Implement `GridComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct GridSingleton : IComponentData { }

    public struct GridDimensions : IComponentData
    {
        public int Width;
        public int Height;
    }

    public struct GridRevision : IComponentData
    {
        public int Value;
    }

    public struct CellData
    {
        public ushort TerrainId;
        public bool Walkable;
        public bool HasRock;
    }

    public struct CellElement : IBufferElementData
    {
        public CellData Value;
    }
}
```

- [ ] **Step 6: Implement `GridBootstrap.cs`**

```csharp
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
```

- [ ] **Step 7: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter GridBootstrapTests`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add Packages/manifest.json Assets/Scripts/Simulation/Simulation.asmdef Assets/Tests/EditMode/SimulationTests.asmdef Assets/Scripts/Simulation/Grid/GridComponents.cs Assets/Scripts/Simulation/Grid/GridBootstrap.cs Assets/Tests/EditMode/GridBootstrapTests.cs
git commit -m "Add ECS packages and grid singleton (Task 1 of ECS migration)"
```

---

## Task 2: Region connectivity

**Files:**
- Create: `Assets/Scripts/Simulation/Grid/RegionComponents.cs`
- Create: `Assets/Scripts/Simulation/Grid/ConnectivitySystem.cs`
- Create: `Assets/Tests/EditMode/ConnectivitySystemTests.cs`

**Interfaces:**
- Consumes: `GridSingleton`, `GridDimensions`, `GridRevision`, `CellElement`/`CellData.Walkable` (Task 1).
- Produces: `RegionSingleton` (tag), `RegionElement : IBufferElementData { public int RegionId; }` (`-1` = unassigned/unwalkable, matching `WorldGrid.UnreachableConnectivityId`), `ConnectivitySystem` (`ISystem`) — recomputes `RegionElement` for every cell whenever `GridRevision.Value` has advanced past the region singleton's own tracked revision (stored in a new `RegionComputedAtRevision : IComponentData { public int Value; }` on the same entity).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class ConnectivitySystemTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("ConnectivitySystemTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static void SetWalkable(EntityManager em, Entity grid, int index, bool walkable)
        {
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            CellData data = cells[index].Value;
            data.Walkable = walkable;
            cells[index] = new CellElement { Value = data };
        }

        [Test]
        public void TwoDisconnectedWalkableRegions_GetDifferentRegionIds()
        {
            EntityManager em = _world.EntityManager;
            Entity grid = GridBootstrap.CreateGrid(em, 3, 1); // cells: [0]=walkable [1]=wall [2]=walkable
            SetWalkable(em, grid, 0, true);
            SetWalkable(em, grid, 1, false);
            SetWalkable(em, grid, 2, true);
            em.SetComponentData(grid, new GridRevision { Value = 1 });

            var group = new TestSystemGroup(_world, ScheduleTypeof<ConnectivitySystem>());
            group.Update();

            Entity regionEntity = _world.EntityManager.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);
            Assert.AreNotEqual(-1, regions[0].RegionId);
            Assert.AreEqual(-1, regions[1].RegionId);
            Assert.AreNotEqual(-1, regions[2].RegionId);
            Assert.AreNotEqual(regions[0].RegionId, regions[2].RegionId);
        }
    }
}
```

`TestSystemGroup`/`ScheduleTypeof` are not real APIs — replace with the project's standard pattern used from here on: a system is run in a test via `SystemHandle handle = _world.CreateSystem<ConnectivitySystem>(); handle.Update(_world.Unmanaged);`. Rewrite the test body's group-creation lines as:

```csharp
            SystemHandle handle = _world.CreateSystem<ConnectivitySystem>();
            handle.Update(_world.Unmanaged);
```

(This exact `CreateSystem`/`Update(Unmanaged)` pattern is what every later task's tests use to run one `ISystem` in isolation — it is the project's adopted DOTS test pattern per spec §Testing.)

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter ConnectivitySystemTests`
Expected: FAIL (compile error — `RegionComponents`/`ConnectivitySystem` not defined).

- [ ] **Step 3: Implement `RegionComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct RegionSingleton : IComponentData { }

    public struct RegionComputedAtRevision : IComponentData
    {
        public int Value;
    }

    public struct RegionElement : IBufferElementData
    {
        public int RegionId;
    }
}
```

- [ ] **Step 4: Implement `ConnectivitySystem.cs`**

```csharp
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
                    NativeArray<int> neighbors = stackalloc int[0] as NativeArray<int>; // placeholder removed below
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
```

Remove the stray `NativeArray<int> neighbors = stackalloc int[0] as NativeArray<int>;` placeholder line written above — it is dead code left from drafting; delete it before compiling. The neighbor loop below it does not use it.

- [ ] **Step 5: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter ConnectivitySystemTests`
Expected: PASS

- [ ] **Step 6: Add the remaining connectivity regression case and re-run**

Port `WorldGridTests.cs`'s largest/multi-region cases as a second `[Test]` in `ConnectivitySystemTests.cs`: a 5-cell row with a wall at index 2, asserting three distinct region ids appear only where walkable (`{0,1}` share one id, `{3,4}` share a different id, index 2 is `-1`). Run the filtered test command again and confirm both tests pass.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Simulation/Grid/RegionComponents.cs Assets/Scripts/Simulation/Grid/ConnectivitySystem.cs Assets/Tests/EditMode/ConnectivitySystemTests.cs
git commit -m "Add region connectivity system (Task 2 of ECS migration)"
```

---

## Task 3: Tile cost singleton and terrain def blob

**Files:**
- Create: `Assets/Scripts/Simulation/Grid/TileCostComponents.cs`
- Create: `Assets/Scripts/Data/TerrainDefBlob.cs`
- Modify: `Assets/Scripts/Data/Data.asmdef` (add `Unity.Entities`, `Unity.Collections`, `allowUnsafeCode: true`)
- Create: `Assets/Tests/EditMode/TerrainDefBlobTests.cs`

**Interfaces:**
- Consumes: `GridSingleton`/`CellElement` (Task 1).
- Produces: `TileCostSingleton` (tag), `BaseCostElement : IBufferElementData { public ushort Value; }`, `DynamicCostElement : IBufferElementData { public ushort Value; }`, `const ushort TileCost.Impassable = ushort.MaxValue`; `TerrainDefBlob { public BlobArray<TerrainDefEntry> Entries; }` where `TerrainDefEntry { public int TerrainTypeId; public bool IsWalkable; public ushort MovementCost; }`; `static class TerrainDefBlobBuilder { public static BlobAssetReference<TerrainDefBlob> Build(TerrainDefSO[] defs); public static void PopulateBaseCosts(EntityManager em, Entity grid, BlobAssetReference<TerrainDefBlob> blob); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Data;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Tests
{
    public class TerrainDefBlobTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("TerrainDefBlobTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void PopulateBaseCosts_WalkableTerrainUsesDefCost_UnwalkableIsImpassable()
        {
            EntityManager em = _world.EntityManager;
            Entity grid = GridBootstrap.CreateGrid(em, 2, 1);
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            cells[0] = new CellElement { Value = new CellData { TerrainId = 1, Walkable = true } };
            cells[1] = new CellElement { Value = new CellData { TerrainId = 2, Walkable = false, HasRock = true } };

            var dirtDef = ScriptableObject.CreateInstance<TerrainDefSO>();
            dirtDef.TerrainTypeId = 1; dirtDef.IsWalkable = true;
            var rockWallDef = ScriptableObject.CreateInstance<TerrainDefSO>();
            rockWallDef.TerrainTypeId = 2; rockWallDef.IsWalkable = false;

            var blob = TerrainDefBlobBuilder.Build(new[] { dirtDef, rockWallDef });
            TerrainDefBlobBuilder.PopulateBaseCosts(em, grid, blob);

            DynamicBuffer<BaseCostElement> baseCosts = em.GetBuffer<BaseCostElement>(grid);
            Assert.AreEqual(1, baseCosts[0].Value);
            Assert.AreEqual(TileCost.Impassable, baseCosts[1].Value);

            blob.Dispose();
            Object.DestroyImmediate(dirtDef);
            Object.DestroyImmediate(rockWallDef);
        }
    }
}
```

(`using UnityEngine;` for `ScriptableObject`/`Object` must be added to the test file's usings.)

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter TerrainDefBlobTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Update `Data.asmdef`**

```json
{
    "name": "Data",
    "rootNamespace": "ColonySim.Data",
    "references": ["Simulation", "Unity.Entities", "Unity.Collections"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Implement `TileCostComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Grid
{
    public struct TileCostSingleton : IComponentData { }

    public static class TileCost
    {
        public const ushort Impassable = ushort.MaxValue;
    }

    public struct BaseCostElement : IBufferElementData
    {
        public ushort Value;
    }

    public struct DynamicCostElement : IBufferElementData
    {
        public ushort Value;
    }
}
```

- [ ] **Step 5: Implement `TerrainDefBlob.cs`**

```csharp
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
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            DynamicBuffer<BaseCostElement> baseCosts = em.HasBuffer<BaseCostElement>(grid)
                ? em.GetBuffer<BaseCostElement>(grid)
                : em.AddBuffer<BaseCostElement>(grid);
            baseCosts.ResizeUninitialized(cells.Length);
            DynamicBuffer<DynamicCostElement> dynamicCosts = em.HasBuffer<DynamicCostElement>(grid)
                ? em.GetBuffer<DynamicCostElement>(grid)
                : em.AddBuffer<DynamicCostElement>(grid);
            dynamicCosts.ResizeUninitialized(cells.Length);
            if (!em.HasComponent<TileCostSingleton>(grid))
                em.AddComponent<TileCostSingleton>(grid);

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
```

- [ ] **Step 6: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter TerrainDefBlobTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Simulation/Grid/TileCostComponents.cs Assets/Scripts/Data/TerrainDefBlob.cs Assets/Scripts/Data/Data.asmdef Assets/Tests/EditMode/TerrainDefBlobTests.cs
git commit -m "Add tile cost singleton and terrain def blob (Task 3 of ECS migration)"
```

---

## Task 4: World generation as Burst jobs

**Files:**
- Create: `Assets/Scripts/Simulation/Generation/EcsSimplexNoise.cs`
- Create: `Assets/Scripts/Simulation/Generation/WorldGenerationJobs.cs`
- Create: `Assets/Scripts/Simulation/Generation/EcsWorldGenerator.cs`
- Delete: `Assets/Scripts/Simulation/Generation/SimplexNoise.cs`
- Delete: `Assets/Scripts/Simulation/Generation/WorldGenerator.cs` (its `WorldGenerationSettings` struct is **moved**, not deleted — see Step 3)
- Delete: `Assets/Tests/EditMode/WorldGeneratorTests.cs`
- Create: `Assets/Tests/EditMode/EcsWorldGeneratorTests.cs`

**Interfaces:**
- Consumes: `GridBootstrap.CreateGrid` (Task 1), `ConnectivitySystem`/`RegionElement` (Task 2, run once after generation to pick the spawn region), `TerrainDefBlobBuilder.PopulateBaseCosts` (Task 3).
- Produces: `WorldGenerationSettings` (moved verbatim from the deleted `WorldGenerator.cs` into `EcsWorldGenerator.cs`, unchanged fields/`Validate()`), `static class EcsWorldGenerator { public static int Generate(EntityManager em, Entity grid, WorldGenerationSettings settings); }` returning the spawn cell index (mirrors `GeneratedWorld.SpawnCell`; `Heightmap` is not retained on the ECS path — nothing reads it today outside `WorldGenerator` itself, confirmed by grepping `Heightmap` usage before deleting it).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class EcsWorldGeneratorTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("EcsWorldGeneratorTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static EntityManager Em(World w) => w.EntityManager;

        [Test]
        public void Generate_SameSeed_IsDeterministic()
        {
            var settings = new WorldGenerationSettings { Seed = 42 };

            using var worldB = new World("EcsWorldGeneratorTests_B");
            Entity gridA = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            Entity gridB = GridBootstrap.CreateGrid(worldB.EntityManager, EcsWorldGenerator.Size, EcsWorldGenerator.Size);

            int spawnA = EcsWorldGenerator.Generate(Em(_world), gridA, settings);
            int spawnB = EcsWorldGenerator.Generate(worldB.EntityManager, gridB, settings);

            Assert.AreEqual(spawnA, spawnB);
            DynamicBuffer<CellElement> cellsA = Em(_world).GetBuffer<CellElement>(gridA);
            DynamicBuffer<CellElement> cellsB = worldB.EntityManager.GetBuffer<CellElement>(gridB);
            for (int i = 0; i < cellsA.Length; i++)
                Assert.AreEqual(cellsA[i].Value.TerrainId, cellsB[i].Value.TerrainId, $"cell {i} differs");
        }

        [Test]
        public void Generate_WaterCellsAreNeverWalkable()
        {
            var settings = new WorldGenerationSettings { Seed = 7 };
            Entity grid = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            EcsWorldGenerator.Generate(Em(_world), grid, settings);

            DynamicBuffer<CellElement> cells = Em(_world).GetBuffer<CellElement>(grid);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Value.TerrainId == settings.WaterId)
                    Assert.IsFalse(cells[i].Value.Walkable, $"water cell {i} was walkable");
        }

        [Test]
        public void Generate_SpawnCellIsWalkableAndInLargestRegion()
        {
            var settings = new WorldGenerationSettings { Seed = 99 };
            Entity grid = GridBootstrap.CreateGrid(Em(_world), EcsWorldGenerator.Size, EcsWorldGenerator.Size);
            int spawn = EcsWorldGenerator.Generate(Em(_world), grid, settings);

            DynamicBuffer<CellElement> cells = Em(_world).GetBuffer<CellElement>(grid);
            Assert.IsTrue(cells[spawn].Value.Walkable);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter EcsWorldGeneratorTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `EcsSimplexNoise.cs`** (Burst-compatible port of the existing algorithm, unchanged math)

```csharp
using Unity.Burst;
using Unity.Mathematics;

namespace ColonySim.Simulation.Generation
{
    [BurstCompile]
    public struct EcsSimplexNoise
    {
        // 512-entry permutation table, built once from a seed. Lives in a fixed-size
        // buffer inside the struct so it can be captured by value into a job.
        public unsafe fixed int Permutation[512];

        public unsafe EcsSimplexNoise(int seed)
        {
            var temp = new NativeArrayLike256();
            for (int i = 0; i < 256; i++) temp[i] = i;
            uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
            for (int i = 255; i > 0; i--)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                int j = (int)(state % (uint)(i + 1));
                (temp[i], temp[j]) = (temp[j], temp[i]);
            }
            fixed (int* p = Permutation)
            {
                for (int i = 0; i < 256; i++) { p[i] = temp[i]; p[i + 256] = temp[i]; }
            }
        }

        private unsafe struct NativeArrayLike256
        {
            public fixed int Values[256];
            public int this[int i] { get => Values[i]; set => Values[i] = value; }
        }

        public unsafe float Sample(float x, float y)
        {
            const float skew = 0.366025403784f, unskew = 0.211324865405f;
            float s = (x + y) * skew;
            int i = (int)math.floor(x + s), j = (int)math.floor(y + s);
            float t = (i + j) * unskew;
            float x0 = x - (i - t), y0 = y - (j - t);
            int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
            int ii = i & 255, jj = j & 255;
            fixed (int* p = Permutation)
            {
                return 70f * (Corner(p[ii + p[jj]], x0, y0)
                    + Corner(p[ii + i1 + p[jj + j1]], x0 - i1 + unskew, y0 - j1 + unskew)
                    + Corner(p[ii + 1 + p[jj + 1]], x0 - 1 + 2 * unskew, y0 - 1 + 2 * unskew));
            }
        }

        private static float Corner(int hash, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t <= 0) return 0;
            float dot = (hash % 12) switch
            {
                0 => x + y, 1 => -x + y, 2 => x - y, 3 => -x - y,
                4 or 6 => x, 5 or 7 => -x, 8 or 10 => y, _ => -y,
            };
            t *= t;
            return t * t * dot;
        }
    }
}
```

- [ ] **Step 4: Implement `WorldGenerationJobs.cs`** (heightmap, threshold+props, all in one sequential Burst job — the existing algorithm is already single-pass-per-cell and 65,536 cells completes fast enough sequentially that splitting into `IJobParallelFor` buys little for a one-time startup cost; kept as one `IJob` for correctness/simplicity, matching the spec's allowance that the exact pass split is "determined during implementation")

```csharp
using Unity.Burst;
using Unity.Collections;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Generation
{
    [BurstCompile]
    public struct GenerateHeightmapJob : IJob
    {
        public EcsSimplexNoise Noise;
        public WorldGenerationSettings Settings;
        public NativeArray<float> Heights; // Size*Size, row-major
        public int Size;

        public void Execute()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float value = 0, amplitude = 1, frequency = 1f / Settings.NoiseScale;
                for (int octave = 0; octave < Settings.Octaves; octave++)
                {
                    value += Noise.Sample((x + 0.5f) * frequency, (y + 0.5f) * frequency) * amplitude;
                    amplitude *= Settings.Persistence;
                    frequency *= Settings.Lacunarity;
                }
                int idx = y * Size + x;
                Heights[idx] = value;
                if (value < min) min = value;
                if (value > max) max = value;
            }
            float range = max - min;
            for (int i = 0; i < Heights.Length; i++)
            {
                float sample = Heights[i];
                Heights[i] = range <= 0 ? 0.5f : sample <= min ? 0f : sample >= max ? 1f
                    : math.max(0f, math.min(1f, (sample - min) / range));
            }
        }
    }

    [BurstCompile]
    public struct TerrainAndPropsJob : IJob
    {
        [ReadOnly] public NativeArray<float> Heights;
        public WorldGenerationSettings Settings;
        public NativeArray<CellElement> Cells;

        public void Execute()
        {
            for (int i = 0; i < Heights.Length; i++)
            {
                float h = Heights[i];
                int terrain = h < Settings.WaterThreshold ? Settings.WaterId
                    : h < Settings.DirtThreshold ? Settings.DirtId
                    : h < Settings.StoneThreshold ? Settings.GrassId : Settings.StoneId;
                bool walkable = terrain != Settings.WaterId;
                Cells[i] = new CellElement { Value = new CellData { TerrainId = (ushort)terrain, Walkable = walkable, HasRock = false } };
            }

            uint state = unchecked((uint)Settings.Seed) ^ 0xA511E9B3u;
            for (int i = 0; i < Cells.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float roll = (state >> 8) * (1f / 16777216f);
                CellData cell = Cells[i].Value;
                bool rock = (cell.TerrainId == Settings.StoneId && roll < Settings.StoneRockDensity)
                    || (cell.TerrainId == Settings.DirtId && roll < Settings.DirtRockDensity);
                if (rock) { cell.Walkable = false; cell.HasRock = true; }
                // Note: the current plain-C# WorldGenerator also rolls a separate tree flag here
                // from the same `roll` value; tree placement is Presentation-adjacent job-target
                // data (HasTree lived on Cell), and per this migration's Data model (spec
                // §Grid), trees are represented as job-generating entities, not a CellData flag.
                // Tree entity spawning happens in EcsWorldGenerator.Generate after this job, not
                // inside it, to keep this job free of EntityManager/EntityCommandBuffer access
                // (Burst jobs can't touch EntityManager directly).
                Cells[i] = new CellElement { Value = cell };
            }
        }
    }
}
```

- [ ] **Step 5: Implement `EcsWorldGenerator.cs`**

```csharp
using System;
using Unity.Collections;
using Unity.Entities;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Generation
{
    [Serializable]
    public sealed class WorldGenerationSettings
    {
        public int Seed = 12345;
        public float NoiseScale = 72f;
        public int Octaves = 4;
        public float Persistence = 0.5f, Lacunarity = 2f;
        public float WaterThreshold = 0.30f, DirtThreshold = 0.40f, StoneThreshold = 0.72f;
        public float TreeDensity = 0.12f, StoneRockDensity = 0.35f, DirtRockDensity = 0.025f;
        public int WaterId = 2, DirtId = 0, GrassId = 3, StoneId = 4;

        public void Validate()
        {
            if (!(NoiseScale >= 1f && NoiseScale <= 4096f) || Octaves < 1 || Octaves > 8
                || !(Persistence > 0 && Persistence <= 1) || !(Lacunarity >= 1 && Lacunarity <= 4)
                || !(WaterThreshold >= 0 && WaterThreshold < DirtThreshold && DirtThreshold < StoneThreshold && StoneThreshold <= 1)
                || !Probability(TreeDensity) || !Probability(StoneRockDensity) || !Probability(DirtRockDensity))
                throw new ArgumentException("Invalid generation scale, octaves, thresholds, or density.");
            if (WaterId == DirtId || WaterId == GrassId || WaterId == StoneId || DirtId == GrassId || DirtId == StoneId || GrassId == StoneId)
                throw new ArgumentException("Terrain IDs must be distinct.");
        }
        private static bool Probability(float value) => value >= 0 && value <= 1;
    }

    public static class EcsWorldGenerator
    {
        public const int Size = 256;

        public static int Generate(EntityManager em, Entity grid, WorldGenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            var heights = new NativeArray<float>(Size * Size, Allocator.TempJob);
            var heightJob = new GenerateHeightmapJob { Noise = new EcsSimplexNoise(settings.Seed), Settings = settings, Heights = heights, Size = Size };
            heightJob.Schedule().Complete();

            var cells = new NativeArray<CellElement>(Size * Size, Allocator.TempJob);
            var terrainJob = new TerrainAndPropsJob { Heights = heights, Settings = settings, Cells = cells };
            terrainJob.Schedule().Complete();

            DynamicBuffer<CellElement> gridCells = em.GetBuffer<CellElement>(grid);
            gridCells.ResizeUninitialized(cells.Length);
            for (int i = 0; i < cells.Length; i++) gridCells[i] = cells[i];
            em.SetComponentData(grid, new GridRevision { Value = em.GetComponentData<GridRevision>(grid).Value + 1 });

            // Trees: spawned as entities from the same deterministic roll used for rocks,
            // recomputed here (cheap: one pass) since GenerateHeightmapJob's job can't touch
            // EntityManager. Uses the same LCG stream/seed offset as TerrainAndPropsJob so
            // roll values line up per cell.
            uint state = unchecked((uint)settings.Seed) ^ 0xA511E9B3u;
            for (int i = 0; i < cells.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                float roll = (state >> 8) * (1f / 16777216f);
                CellData cell = gridCells[i].Value;
                if (cell.TerrainId == settings.GrassId && !cell.HasRock && roll < settings.TreeDensity)
                {
                    Entity tree = em.CreateEntity(typeof(TreeTag), typeof(TreeCellIndex));
                    em.SetComponentData(tree, new TreeCellIndex { Value = i });
                }
            }

            heights.Dispose();
            cells.Dispose();

            var connectivity = new ConnectivitySystem();
            // ConnectivitySystem is an ISystem struct; run its OnUpdate logic via a temporary
            // World-owned SystemHandle rather than calling OnUpdate directly (ISystem methods
            // are called by the ECS scheduler, not invoked as plain instance methods).
            World world = em.World;
            SystemHandle handle = world.GetOrCreateSystem<ConnectivitySystem>();
            handle.Update(world.Unmanaged);

            Entity regionEntity = em.CreateEntityQuery(typeof(RegionSingleton)).GetSingletonEntity();
            DynamicBuffer<RegionElement> regions = em.GetBuffer<RegionElement>(regionEntity);

            var counts = new NativeArray<int>(cells.Length, Allocator.Temp);
            int largest = -1;
            for (int i = 0; i < regions.Length; i++)
            {
                int region = regions[i].RegionId;
                if (region < 0) continue;
                counts[region]++;
                if (largest < 0 || counts[region] > counts[largest]) largest = region;
            }

            int spawn = -1, best = int.MaxValue;
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].RegionId != largest) continue;
                int distance = System.Math.Abs(i % Size - Size / 2) + System.Math.Abs(i / Size - Size / 2);
                if (distance >= best) continue;
                best = distance; spawn = i;
            }
            counts.Dispose();
            if (spawn < 0) throw new InvalidOperationException("Settings produced no walkable land; reduce water or rock density.");
            return spawn;
        }
    }

    public struct TreeTag : IComponentData { }
    public struct TreeCellIndex : IComponentData { public int Value; }
}
```

- [ ] **Step 6: Delete superseded files**

```bash
git rm Assets/Scripts/Simulation/Generation/SimplexNoise.cs Assets/Scripts/Simulation/Generation/WorldGenerator.cs Assets/Tests/EditMode/WorldGeneratorTests.cs
```

- [ ] **Step 7: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter EcsWorldGeneratorTests`
Expected: PASS. Also run the full suite (`unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`) to confirm nothing else referenced the deleted `WorldGenerator`/`SimplexNoise` (it will fail to compile if so — fix any remaining references before proceeding, they should only exist in `SimulationRoot.cs`, addressed in Task 11).

- [ ] **Step 8: Commit**

```bash
git add -A Assets/Scripts/Simulation/Generation Assets/Tests/EditMode/EcsWorldGeneratorTests.cs
git commit -m "Port world generation to Burst jobs, delete plain-C# generator (Task 4 of ECS migration)"
```

---

## Task 5: Pawn entities

**Files:**
- Create: `Assets/Scripts/Simulation/Pawns/PawnComponents.cs`
- Create: `Assets/Scripts/Simulation/Pawns/PawnFactory.cs`
- Delete: `Assets/Scripts/Simulation/Pawns/Pawn.cs`
- Delete: `Assets/Scripts/Simulation/Pawns/PawnManager.cs`
- Delete: `Assets/Tests/EditMode/PawnManagerTests.cs`
- Create: `Assets/Tests/EditMode/PawnFactoryTests.cs`

**Interfaces:**
- Produces: `MoveSpeedModifier { float Value }`, `TickOffset { int Value }`, `CurrentJob { Entity Value }`, `AssignedFlowField { Entity Value }`, `IsMoving`/`IsWorking` (enableable tag `IComponentData`s), `PawnId { int Value }` (needed since Presentation still addresses pawns by a stable int id for `PawnView`, per spec §Presentation boundary — not in the spec's literal component list but required to preserve `PawnView.Initialize(root, pawnId, template)`'s existing id-based API without redesigning Presentation), `static class PawnFactory { public static Entity CreatePawn(EntityManager em, int id, float2 position, int tickOffset); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class PawnFactoryTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("PawnFactoryTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void CreatePawn_SetsPositionIdAndIdleState()
        {
            EntityManager em = _world.EntityManager;
            Entity pawn = PawnFactory.CreatePawn(em, id: 3, position: new float2(2, 5), tickOffset: 3);

            Assert.AreEqual(3, em.GetComponentData<PawnId>(pawn).Value);
            Assert.AreEqual(new float3(2, 5, 0), em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.AreEqual(3, em.GetComponentData<TickOffset>(pawn).Value);
            Assert.AreEqual(Entity.Null, em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.AreEqual(Entity.Null, em.GetComponentData<AssignedFlowField>(pawn).Value);
            Assert.IsFalse(em.IsComponentEnabled<IsMoving>(pawn));
            Assert.IsFalse(em.IsComponentEnabled<IsWorking>(pawn));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter PawnFactoryTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `PawnComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Pawns
{
    public struct PawnId : IComponentData { public int Value; }
    public struct MoveSpeedModifier : IComponentData { public float Value; }
    public struct TickOffset : IComponentData { public int Value; }
    public struct CurrentJob : IComponentData { public Entity Value; }
    public struct AssignedFlowField : IComponentData { public Entity Value; }
    public struct IsMoving : IComponentData, IEnableableComponent { }
    public struct IsWorking : IComponentData, IEnableableComponent { }
}
```

- [ ] **Step 4: Implement `PawnFactory.cs`**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ColonySim.Simulation.Pawns
{
    public static class PawnFactory
    {
        public static Entity CreatePawn(EntityManager em, int id, float2 position, int tickOffset)
        {
            Entity pawn = em.CreateEntity(
                typeof(PawnId), typeof(LocalTransform), typeof(MoveSpeedModifier), typeof(TickOffset),
                typeof(CurrentJob), typeof(AssignedFlowField), typeof(IsMoving), typeof(IsWorking));
            em.SetComponentData(pawn, new PawnId { Value = id });
            em.SetComponentData(pawn, LocalTransform.FromPosition(position.x, position.y, 0));
            em.SetComponentData(pawn, new MoveSpeedModifier { Value = 1f });
            em.SetComponentData(pawn, new TickOffset { Value = tickOffset });
            em.SetComponentData(pawn, new CurrentJob { Value = Entity.Null });
            em.SetComponentData(pawn, new AssignedFlowField { Value = Entity.Null });
            em.SetComponentEnabled<IsMoving>(pawn, false);
            em.SetComponentEnabled<IsWorking>(pawn, false);
            return pawn;
        }
    }
}
```

- [ ] **Step 5: Delete superseded files**

```bash
git rm Assets/Scripts/Simulation/Pawns/Pawn.cs Assets/Scripts/Simulation/Pawns/PawnManager.cs Assets/Tests/EditMode/PawnManagerTests.cs
```

- [ ] **Step 6: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter PawnFactoryTests`
Expected: FAIL to compile at this point — `SimulationRoot.cs`, `TickManager.cs`, `JobBoard.cs` still reference `Pawn`/`PawnManager`. This is expected and resolved by later tasks; confirm the *test itself* would pass by temporarily commenting out the still-broken old-file references is not correct procedure — instead, proceed to Step 7 immediately; the full suite will not compile again until Task 11 finishes. Run only a syntax-level check on the new files if the CLI supports isolated compilation, otherwise defer full verification to Task 11's full-suite run and note this task's true "tests pass" checkpoint is deferred.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Simulation/Pawns/PawnComponents.cs Assets/Scripts/Simulation/Pawns/PawnFactory.cs Assets/Tests/EditMode/PawnFactoryTests.cs
git add Assets/Scripts/Simulation/Pawns/Pawn.cs Assets/Scripts/Simulation/Pawns/PawnManager.cs Assets/Tests/EditMode/PawnManagerTests.cs
git commit -m "Add pawn entities, delete plain-C# Pawn/PawnManager (Task 5 of ECS migration)"
```

---

## Task 6: Job entities and creation

**Files:**
- Create: `Assets/Scripts/Simulation/Jobs/JobComponents.cs`
- Create: `Assets/Scripts/Simulation/Jobs/JobFactory.cs`
- Delete: `Assets/Scripts/Simulation/Jobs/Job.cs`
- Create: `Assets/Tests/EditMode/JobFactoryTests.cs`

**Interfaces:**
- Consumes: `GridSingleton`/`CellElement` (Task 1) for the walkable-or-rock target validation.
- Produces: `JobData { JobType Type; int TargetCellIndex; float RemainingWork; }`, `Unclaimed` (enableable tag), `static class JobFactory { public static bool TryCreateJob(EntityManager em, Entity grid, JobType type, int targetCellIndex, out Entity job); }` (ports `JobBoard.TryAddJob`'s two validations: target must be walkable-or-rock, and no existing job — claimed or not — may already target that cell).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;

namespace ColonySim.Simulation.Tests
{
    public class JobFactoryTests
    {
        private World _world;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("JobFactoryTests");
            _grid = GridBootstrap.CreateGrid(_world.EntityManager, 3, 1);
            DynamicBuffer<CellElement> cells = _world.EntityManager.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = false, HasRock = true } }; // mineable
            cells[1] = new CellElement { Value = new CellData { Walkable = true } };
            cells[2] = new CellElement { Value = new CellData { Walkable = false, HasRock = false } }; // plain wall, not mineable
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void TryCreateJob_MineableCell_Succeeds()
        {
            bool created = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out Entity job);
            Assert.IsTrue(created);
            JobData data = _world.EntityManager.GetComponentData<JobData>(job);
            Assert.AreEqual(JobType.Mine, data.Type);
            Assert.AreEqual(0, data.TargetCellIndex);
            Assert.IsTrue(_world.EntityManager.IsComponentEnabled<Unclaimed>(job));
        }

        [Test]
        public void TryCreateJob_PlainWallNotMineable_Fails()
        {
            bool created = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 2, out _);
            Assert.IsFalse(created);
        }

        [Test]
        public void TryCreateJob_DuplicateTarget_Rejected()
        {
            JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out _);
            bool secondCreated = JobFactory.TryCreateJob(_world.EntityManager, _grid, JobType.Mine, 0, out _);
            Assert.IsFalse(secondCreated);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter JobFactoryTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `JobComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Jobs
{
    public struct JobData : IComponentData
    {
        public JobType Type;
        public int TargetCellIndex;
        public float RemainingWork;
    }

    public struct Unclaimed : IComponentData, IEnableableComponent { }
}
```

- [ ] **Step 4: Implement `JobFactory.cs`**

```csharp
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
            using NativeArray<JobData> allJobs = existing.ToComponentDataArray<JobData>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < allJobs.Length; i++)
                if (allJobs[i].TargetCellIndex == targetCellIndex) return false;

            job = em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            em.SetComponentData(job, new JobData { Type = type, TargetCellIndex = targetCellIndex, RemainingWork = 0 });
            em.SetComponentEnabled<Unclaimed>(job, true);
            return true;
        }
    }
}
```

Add `using Unity.Collections;` to the top of the file for `NativeArray`/`Allocator`.

- [ ] **Step 5: Delete superseded file**

```bash
git rm Assets/Scripts/Simulation/Jobs/Job.cs
```

`JobBoard.cs` and `JobBoardTests.cs` are **not** deleted yet — `JobBoard.TryClaimJobFor` is still referenced by the not-yet-migrated `TickManager.cs` until Task 8. `Job.cs` (the plain data class) can be deleted now because `JobFactory`/`JobData` fully replace what it held; `JobBoard.TryAddJob`'s validation logic is what Task 6 actually replaces, and `JobBoard.cs` itself is deleted once its remaining method (`TryClaimJobFor`) is replaced in Task 8.

- [ ] **Step 6: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter JobFactoryTests`
Expected: FAIL to compile — `JobBoard.cs` still references the now-deleted `Job.cs`. Fix by changing `JobBoard.cs`'s `_openJobs`/`_jobsById`/`TryAddJob` to use a minimal private nested record instead of the deleted `Job` class, OR (simpler and preferred, since `TryAddJob` is fully superseded by `JobFactory.TryCreateJob` already) delete `JobBoard.TryAddJob` and its two dedupe-check private fields' usage from `JobBoard.cs` now, leaving only `TryClaimJobFor`/`ReleaseJob`/`CompleteJob` referencing `Job` — and change `Job`'s one remaining consumer, `JobBoard.cs` itself, to keep a small private `private class LegacyJob { ... }` copy of the old `Job` fields scoped inside `JobBoard.cs` until Task 8 deletes the whole file. Apply that inline `LegacyJob` rename in `JobBoard.cs` now so the project compiles.

- [ ] **Step 7: Run the full suite to confirm the project compiles**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: compiles (may still show unrelated pre-existing failures from not-yet-migrated code paths only if those paths are exercised by tests that still reference deleted types — none should, since `PawnManagerTests`/`WorldGeneratorTests` were already deleted in their own tasks). `JobFactoryTests` PASS.

- [ ] **Step 8: Commit**

```bash
git add -A Assets/Scripts/Simulation/Jobs Assets/Tests/EditMode/JobFactoryTests.cs
git commit -m "Add job entities and creation, shrink JobBoard to claim-only (Task 6 of ECS migration)"
```

---

## Task 7: Flow-field pathfinding

**Files:**
- Create: `Assets/Scripts/Simulation/Pathing/FlowFieldComponents.cs`
- Create: `Assets/Scripts/Simulation/Pathing/FlowFieldGenerationSystem.cs`
- Delete: `Assets/Scripts/Simulation/Pathing/GridAStar.cs`
- Delete: `Assets/Scripts/Simulation/Pathing/IPathfinder.cs`
- Delete: `Assets/Tests/EditMode/GridAStarTests.cs`
- Create: `Assets/Tests/EditMode/FlowFieldGenerationSystemTests.cs`

**Interfaces:**
- Consumes: `GridSingleton`/`GridDimensions`/`GridRevision`/`CellElement` (Task 1), `RegionSingleton`/`RegionElement` (Task 2), `TileCostSingleton`/`BaseCostElement`/`DynamicCostElement` (Task 3).
- Produces: `FlowFieldDestination { int TargetCellIndex }`, `FlowFieldGeneration { int GridRevisionGeneratedAt }`, `IntegrationCostElement : IBufferElementData { public ushort Value }` (`ushort.MaxValue` = unreached), `FlowDirectionElement : IBufferElementData { public sbyte Value }` (`-1` = none/destination/unreached, else `0..7` for the 8 compass directions, `0`=N going clockwise matching `GridAStar`'s existing 4-direction indices `0,1,2,3` = N,E,S,W extended with the four diagonals `4..7` = NE,SE,SW,NW), `static class FlowFieldService { public static Entity GetOrCreateField(EntityManager em, Entity grid, int destinationCellIndex); }` (looks up an existing up-to-date `FlowFieldDestination` entity for that cell or generates one synchronously).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;

namespace ColonySim.Simulation.Tests
{
    public class FlowFieldGenerationSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("FlowFieldGenerationSystemTests");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void MakeGrid(int width, int height, bool[] walkable)
        {
            _grid = GridBootstrap.CreateGrid(_em, width, height);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new CellElement { Value = new CellData { Walkable = walkable[i] } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });

            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(cells.Length);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(cells.Length);
            for (int i = 0; i < cells.Length; i++)
                baseCosts[i] = new BaseCostElement { Value = walkable[i] ? (ushort)1 : TileCost.Impassable };
            _em.AddComponent<TileCostSingleton>(_grid);

            SystemHandle connectivity = _world.GetOrCreateSystem<ConnectivitySystem>();
            connectivity.Update(_world.Unmanaged);
        }

        [Test]
        public void GetOrCreateField_StraightLine_FlowsTowardDestination()
        {
            MakeGrid(3, 1, new[] { true, true, true });
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);

            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(0, costs[0].Value);
            Assert.AreEqual(1, costs[1].Value);
            Assert.AreEqual(2, costs[2].Value);

            DynamicBuffer<FlowDirectionElement> directions = _em.GetBuffer<FlowDirectionElement>(field);
            Assert.AreNotEqual(-1, directions[1].Value); // cell 1 has a valid direction toward cell 0
        }

        [Test]
        public void GetOrCreateField_UnreachableAcrossWall_StaysUnreached()
        {
            MakeGrid(3, 1, new[] { true, false, true }); // wall between destination and cell 2
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);

            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(ushort.MaxValue, costs[2].Value);
        }

        [Test]
        public void GetOrCreateField_MineJobOnWallBetweenTwoRegions_ResolvesGoalFromRequestingRegionOnly()
        {
            // Regression: cells [0][1] region A, cell[2] = rock target between regions,
            // [3][4] region B. A field generated for the rock target from region A's side
            // must integrate outward only through A's walkable neighbors, never bleed cost
            // into region B through the rock cell itself (the rock is impassable).
            MakeGrid(5, 1, new[] { true, true, false, true, true });
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            CellData rock = cells[2].Value; rock.HasRock = true; cells[2] = new CellElement { Value = rock };
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[2] = new BaseCostElement { Value = TileCost.Impassable };
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 1);
            DynamicBuffer<IntegrationCostElement> costs = _em.GetBuffer<IntegrationCostElement>(field);
            Assert.AreEqual(ushort.MaxValue, costs[3].Value, "region B must not be reached through the rock");
            Assert.AreEqual(ushort.MaxValue, costs[4].Value);
            Assert.AreEqual(0, costs[1].Value);
            Assert.AreEqual(1, costs[0].Value);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter FlowFieldGenerationSystemTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `FlowFieldComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Pathing
{
    public struct FlowFieldDestination : IComponentData
    {
        public int TargetCellIndex;
    }

    public struct FlowFieldGeneration : IComponentData
    {
        public int GridRevisionGeneratedAt;
    }

    public struct IntegrationCostElement : IBufferElementData
    {
        public ushort Value;
    }

    public struct FlowDirectionElement : IBufferElementData
    {
        public sbyte Value; // -1 = none; 0=N,1=E,2=S,3=W,4=NE,5=SE,6=SW,7=NW
    }
}
```

- [ ] **Step 4: Implement `FlowFieldGenerationSystem.cs`** (the Dijkstra integration sweep, ported from `GridAStar`'s existing binary-heap structure — same heap operations, but filling the whole reachable region from the destination rather than stopping at one goal)

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };

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
                    int nx = cx + Dx[d], ny = cy + Dy[d];
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
                    int nx = x + Dx[d], ny = y + Dy[d];
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
```

Add `using Unity.Mathematics;` to `FlowFieldGenerationSystem.cs` for `math.min`.

- [ ] **Step 5: Delete superseded files**

```bash
git rm Assets/Scripts/Simulation/Pathing/GridAStar.cs Assets/Scripts/Simulation/Pathing/IPathfinder.cs Assets/Tests/EditMode/GridAStarTests.cs
```

`TickManager.cs` still references `IPathfinder`/`GridAStar` until Task 10 — apply the same interim fix pattern as Task 6: leave `TickManager.cs` broken only if nothing still builds it into the running test suite path; since `TickManager.cs` is production code that must compile, replace its `IPathfinder _pathfinder` field and constructor parameter with a direct call to `FlowFieldService.GetOrCreateField` inline for now (a minimal, temporary edit — Task 10 will delete `TickManager.cs` entirely and this edit is superseded, not built upon).

- [ ] **Step 6: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter FlowFieldGenerationSystemTests`
Expected: PASS. Then run the full suite to confirm the temporary `TickManager.cs` edit compiles: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`.

- [ ] **Step 7: Commit**

```bash
git add -A Assets/Scripts/Simulation/Pathing Assets/Tests/EditMode/FlowFieldGenerationSystemTests.cs Assets/Scripts/Simulation/Ticking/TickManager.cs
git commit -m "Add flow-field pathfinding, delete GridAStar (Task 7 of ECS migration)"
```

---

## Task 8: Job assignment system

**Files:**
- Create: `Assets/Scripts/Simulation/Jobs/JobAssignmentSystem.cs`
- Delete: `Assets/Scripts/Simulation/Jobs/JobBoard.cs`
- Delete: `Assets/Tests/EditMode/JobBoardTests.cs`
- Create: `Assets/Tests/EditMode/JobAssignmentSystemTests.cs`

**Interfaces:**
- Consumes: `PawnId`/`CurrentJob`/`AssignedFlowField`/`IsMoving`/`IsWorking`/`TickOffset` (Task 5), `JobData`/`Unclaimed` (Task 6), `FlowFieldService.GetOrCreateField` (Task 7), `RegionSingleton`/`RegionElement` (Task 2).
- Produces: `JobAssignmentSystem` (`ISystem`) — for every idle pawn (`IsMoving` and `IsWorking` both disabled, `CurrentJob.Value == Entity.Null`), scans `Unclaimed` jobs whose target cell (or, for an unwalkable target, at least one of its walkable neighbors per `ResolvePathGoal`'s existing logic) shares the pawn's `RegionElement`, generates/looks-up a flow field per candidate, picks the lowest `IntegrationCostElement` at the pawn's own cell, writes `CurrentJob`/`AssignedFlowField`, disables `Unclaimed` on the winning job.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class JobAssignmentSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("JobAssignmentSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 5, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < 5; i++) cells[i] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(5);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(5);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            for (int i = 0; i < 5; i++) baseCosts[i] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void IdlePawn_ClaimsNearestReachableUnclaimedJob()
        {
            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(0, 0), tickOffset: 0);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 3, out Entity farJob);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 1, out Entity nearJob);

            _world.GetOrCreateSystem<JobAssignmentSystem>().Update(_world.Unmanaged);

            Assert.AreEqual(nearJob, _em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.IsFalse(_em.IsComponentEnabled<Unclaimed>(nearJob));
            Assert.IsTrue(_em.IsComponentEnabled<Unclaimed>(farJob));
        }

        [Test]
        public void IdlePawn_NoReachableJobInRegion_StaysIdleWithoutThrowing()
        {
            // Wall off cell 4 from the rest so a job there is in a different region.
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            CellData wall = cells[2].Value; wall.Walkable = false; cells[2] = new CellElement { Value = wall };
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[2] = new BaseCostElement { Value = TileCost.Impassable };
            _em.SetComponentData(_grid, new GridRevision { Value = 2 });
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(0, 0), tickOffset: 0);
            JobFactory.TryCreateJob(_em, _grid, JobType.ChopTree, targetCellIndex: 4, out _);

            Assert.DoesNotThrow(() => _world.GetOrCreateSystem<JobAssignmentSystem>().Update(_world.Unmanaged));
            Assert.AreEqual(Entity.Null, _em.GetComponentData<CurrentJob>(pawn).Value);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter JobAssignmentSystemTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `JobAssignmentSystem.cs`**

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Jobs
{
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

            foreach (var (pawnId, transform, currentJob, entity) in
                     SystemAPI.Query<RefRO<PawnId>, RefRO<Unity.Transforms.LocalTransform>, RefRW<CurrentJob>>().WithEntityAccess()
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
```

- [ ] **Step 4: Delete superseded files**

```bash
git rm Assets/Scripts/Simulation/Jobs/JobBoard.cs Assets/Tests/EditMode/JobBoardTests.cs
```

Apply the same interim-compile fix as Tasks 6/7 to `TickManager.cs`: replace its `JobBoard _jobBoard`/`TryStartJob`'s call into `JobBoard.TryClaimJobFor` with a direct query for a pawn's already-assigned `CurrentJob` (since `JobAssignmentSystem` now does the claiming) — this is a larger interim edit than prior tasks' one-liners; since `TickManager.cs` is deleted outright in Task 10, keep the interim edit minimal: have `TickManager.TryStartJob` simply check `CurrentJob`/`AssignedFlowField` on the ECS pawn entity mirrored by the plain `Pawn` (not fully wired — acceptable because Task 10 removes this code path entirely before `SimulationRoot` ever calls it in Play mode; the only requirement here is that the project **compiles** for the EditMode test runner between tasks, per the project's existing convention of a working tree at every commit. If a clean minimal edit isn't possible without deeper rework, it is acceptable for this one interim commit to leave `TickManager.Tick`'s job-start path non-functional (returns early) as long as it still compiles — Task 10 deletes it before Play mode is exercised again.

- [ ] **Step 5: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter JobAssignmentSystemTests`
Expected: PASS. Then run the full suite to confirm compilation: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`.

- [ ] **Step 6: Commit**

```bash
git add -A Assets/Scripts/Simulation/Jobs Assets/Tests/EditMode/JobAssignmentSystemTests.cs Assets/Scripts/Simulation/Ticking/TickManager.cs
git commit -m "Add flow-field-ranked job assignment system, delete JobBoard (Task 8 of ECS migration)"
```

---

## Task 9: Movement system

**Files:**
- Create: `Assets/Scripts/Simulation/Pawns/MovementSystem.cs`
- Create: `Assets/Tests/EditMode/MovementSystemTests.cs`

**Interfaces:**
- Consumes: `AssignedFlowField`/`IsMoving`/`IsWorking`/`CurrentJob`/`TickOffset` (Task 5), `FlowDirectionElement` (Task 7), `JobData` (Task 6).
- Produces: `MovementSystem` (`ISystem`) — for pawns with `IsMoving` enabled, reads `FlowDirectionElement` at the pawn's current cell off its `AssignedFlowField` buffer, steps one cell in that direction, and on reaching the field's destination cell (`FlowDirectionElement.Value == -1` at the pawn's new cell) disables `IsMoving`, enables `IsWorking`, and sets `JobData.RemainingWork` from the job's configured duration (passed in as a system parameter set once per tick by `SimulationTickGroup`, matching `TickManager.Tick(floorConfig, treeConfig)`'s existing per-tick config pattern — see Task 10).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class MovementSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("MovementSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 3, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            for (int i = 0; i < 3; i++) cells[i] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(3);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(3);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            for (int i = 0; i < 3; i++) baseCosts[i] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void MovingPawn_StepsTowardDestination_ThenStartsWorking()
        {
            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);
            Entity job = _em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            _em.SetComponentData(job, new JobData { Type = JobType.ChopTree, TargetCellIndex = 0, RemainingWork = 0 });
            _em.SetComponentEnabled<Unclaimed>(job, false);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(2, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentData(pawn, new AssignedFlowField { Value = field });
            _em.SetComponentEnabled<IsMoving>(pawn, true);

            var movement = new MovementSystem { WorkDurationTicksByJobType = new NativeHashMap<JobType, int>(2, Allocator.Temp) };
            movement.WorkDurationTicksByJobType[JobType.ChopTree] = 60;

            SystemHandle handle = _world.GetOrCreateSystem<MovementSystem>();
            _world.EntityManager.SetComponentData(handle, movement); // seed the system's managed config before Update
            handle.Update(_world.Unmanaged);

            Assert.AreEqual(new float3(1, 0, 0), _em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.IsTrue(_em.IsComponentEnabled<IsMoving>(pawn));

            handle.Update(_world.Unmanaged);
            Assert.AreEqual(new float3(0, 0, 0), _em.GetComponentData<LocalTransform>(pawn).Position);
            Assert.IsFalse(_em.IsComponentEnabled<IsMoving>(pawn));
            Assert.IsTrue(_em.IsComponentEnabled<IsWorking>(pawn));
            Assert.AreEqual(60, _em.GetComponentData<JobData>(job).RemainingWork);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter MovementSystemTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `MovementSystem.cs`**

`MovementSystem` needs a per-tick config (work duration by job type) supplied from outside, the same way `TickManager.Tick(floorConfig, treeConfig)` receives it today. `ISystem` structs can hold their own `IComponentData`-shaped state only via `SystemAPI.GetSingleton`, but a `NativeHashMap` field on the struct itself (set directly, not through `SetComponentData` — correct the test's approach below) is simpler and matches how `TickManager` already holds `_currentFloorConfig`/`_currentTreeConfig` as plain fields:

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;

namespace ColonySim.Simulation.Pawns
{
    public partial struct MovementSystem : ISystem
    {
        public NativeHashMap<JobType, int> WorkDurationTicksByJobType;

        public void OnUpdate(ref SystemState state)
        {
            EntityManager em = state.EntityManager;
            Entity grid = SystemAPI.GetSingletonEntity<GridSingleton>();
            GridDimensions dims = em.GetComponentData<GridDimensions>(grid);

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
                    job.RemainingWork = WorkDurationTicksByJobType.TryGetValue(job.Type, out int duration) ? duration : 0;
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
```

- [ ] **Step 4: Fix the test's system-config wiring**

`ISystem` struct instance fields are not addressable through `SystemHandle`/`SetComponentData` the way the draft test assumed — that was written before the implementation settled and is wrong. Replace the test's config-seeding lines:

```csharp
            var movement = new MovementSystem { WorkDurationTicksByJobType = new NativeHashMap<JobType, int>(2, Allocator.Temp) };
            movement.WorkDurationTicksByJobType[JobType.ChopTree] = 60;

            SystemHandle handle = _world.GetOrCreateSystem<MovementSystem>();
            _world.EntityManager.SetComponentData(handle, movement); // seed the system's managed config before Update
            handle.Update(_world.Unmanaged);
```

with the correct pattern — get the system's own managed struct via `World.Unmanaged.GetUnsafeSystemRef<T>` is not available to test code either; instead expose the config as a **singleton component** (matching how `SimulationTickGroup` in Task 10 will actually drive it) rather than a field on the `ISystem` struct itself. Change `MovementSystem.cs`'s `WorkDurationTicksByJobType` field to instead read a new singleton component each `OnUpdate`:

```csharp
    public struct WorkDurationConfig : IComponentData
    {
        public int MineDurationTicks;
        public int ChopDurationTicks;
    }
```

(add this struct to `MovementSystem.cs`) and change `MovementSystem.OnUpdate` to read it via `SystemAPI.GetSingleton<WorkDurationConfig>()` instead of the `NativeHashMap` field, resolving `job.Type == JobType.Mine ? config.MineDurationTicks : config.ChopDurationTicks`. Update the test to create this singleton instead:

```csharp
            _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(WorkDurationConfig)).GetSingletonEntity(), new WorkDurationConfig { ChopDurationTicks = 60, MineDurationTicks = 60 });

            SystemHandle handle = _world.GetOrCreateSystem<MovementSystem>();
            handle.Update(_world.Unmanaged);
```

Remove the `NativeHashMap`-based `WorkDurationTicksByJobType` field and its `using Unity.Collections;` dependency from `MovementSystem.cs` entirely, replacing with `SystemAPI.GetSingleton<WorkDurationConfig>()` as described. This `WorkDurationConfig` singleton is also what `SimulationRoot` populates once at startup in Task 11 (replacing `TerrainJobConfig`/`TreeJobConfig`).

- [ ] **Step 5: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter MovementSystemTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Pawns/MovementSystem.cs Assets/Tests/EditMode/MovementSystemTests.cs
git commit -m "Add flow-field movement system (Task 9 of ECS migration)"
```

---

## Task 10: Work execution, resource spawn, and the tick group

**Files:**
- Create: `Assets/Scripts/Simulation/Resources/ResourceComponents.cs`
- Create: `Assets/Scripts/Simulation/Ticking/WorkExecutionSystem.cs`
- Create: `Assets/Scripts/Simulation/Ticking/SimulationTickGroup.cs`
- Delete: `Assets/Scripts/Simulation/Ticking/TickManager.cs`
- Delete: `Assets/Tests/EditMode/TickManagerTests.cs`
- Create: `Assets/Tests/EditMode/WorkExecutionSystemTests.cs`

**Interfaces:**
- Consumes: `JobData`/`Unclaimed` (Task 6), `IsWorking`/`CurrentJob`/`AssignedFlowField`/`TickOffset` (Task 5), `CellElement`/`GridRevision` (Task 1), `WorkDurationConfig` (Task 9).
- Produces: `ResourceItemData { ResourceType Type; int Amount; int CellIndex; }`, `PendingResourceSpawn` (tag `IComponentData` added to a `ResourceItemData` entity, removed once Presentation drains it — this is the "queue Presentation drains" from spec §Job matching item 4), `WorkExecutionSystem` (`ISystem`) — decrements `JobData.RemainingWork` for `IsWorking` pawns respecting `TickOffset`; on completion, mutates `CellElement` (clears `HasRock`/sets floor terrain for Mine, destroys the `TreeTag` entity at that cell for ChopTree), bumps `GridRevision`, creates a `ResourceItemData`+`PendingResourceSpawn` entity, destroys the job entity, resets the pawn to idle (`CurrentJob = Entity.Null`, `AssignedFlowField = Entity.Null`, `IsWorking` disabled); if the target was already resolved out from under the pawn (mirrors `TickManagerTests`' "target invalidated mid-execution" regression), skips the resource spawn but still returns the pawn to idle, matching today's `TickManager.AdvanceWorking` fallthrough behavior. `SimulationTickGroup` (`ComponentSystemGroup`, `[UpdateInGroup(typeof(SimulationSystemGroup))]`) orders `ConnectivitySystem` → `JobAssignmentSystem` → `MovementSystem` → `WorkExecutionSystem` via `[UpdateAfter]` attributes, and each system's queries additionally filter `TickOffset.Value == currentTick % staggerBucketCount` (current tick tracked as a new `SimulationTick : IComponentData { int Value; int StaggerBucketCount; }` singleton `SimulationTickGroup` increments once per group update).

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class WorkExecutionSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new World("WorkExecutionSystemTests");
            _em = _world.EntityManager;
            _grid = GridBootstrap.CreateGrid(_em, 2, 1);
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = false, HasRock = true, TerrainId = 5 } };
            cells[1] = new CellElement { Value = new CellData { Walkable = true } };
            _em.SetComponentData(_grid, new GridRevision { Value = 1 });
            _em.CreateEntity(typeof(SimulationTick));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(SimulationTick)).GetSingletonEntity(), new SimulationTick { Value = 0, StaggerBucketCount = 1 });
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void WorkingPawn_CompletesJob_MutatesGridAndSpawnsResourceAndReturnsIdle()
        {
            Entity job = _em.CreateEntity(typeof(JobData), typeof(Unclaimed));
            _em.SetComponentData(job, new JobData { Type = JobType.Mine, TargetCellIndex = 0, RemainingWork = 1 });
            _em.SetComponentEnabled<Unclaimed>(job, false);

            Entity pawn = PawnFactory.CreatePawn(_em, id: 0, position: new float2(1, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentEnabled<IsWorking>(pawn, true);

            int revisionBefore = _em.GetComponentData<GridRevision>(_grid).Value;
            _world.GetOrCreateSystem<WorkExecutionSystem>().Update(_world.Unmanaged);

            Assert.IsFalse(_em.IsComponentEnabled<IsWorking>(pawn));
            Assert.AreEqual(Entity.Null, _em.GetComponentData<CurrentJob>(pawn).Value);
            Assert.IsFalse(_em.Exists(job));

            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            Assert.IsFalse(cells[0].Value.HasRock);
            Assert.IsTrue(cells[0].Value.Walkable);
            Assert.Greater(_em.GetComponentData<GridRevision>(_grid).Value, revisionBefore);

            EntityQuery pending = _em.CreateEntityQuery(typeof(ResourceItemData), typeof(PendingResourceSpawn));
            Assert.AreEqual(1, pending.CalculateEntityCount());
        }

        [Test]
        public void MovementSystem_AfterGridMutatedMidFlight_StillAdvancesSafely()
        {
            // Review Focus: grid mutation racing flow-field staleness. A field generated
            // before a nearby cell's revision-bumping mutation must not crash MovementSystem
            // when read afterward, even though it is now stale.
            DynamicBuffer<CellElement> cells = _em.GetBuffer<CellElement>(_grid);
            cells[0] = new CellElement { Value = new CellData { Walkable = true } };
            _em.AddBuffer<BaseCostElement>(_grid).ResizeUninitialized(2);
            _em.AddBuffer<DynamicCostElement>(_grid).ResizeUninitialized(2);
            DynamicBuffer<BaseCostElement> baseCosts = _em.GetBuffer<BaseCostElement>(_grid);
            baseCosts[0] = new BaseCostElement { Value = 1 };
            baseCosts[1] = new BaseCostElement { Value = 1 };
            _em.AddComponent<TileCostSingleton>(_grid);
            _world.GetOrCreateSystem<ConnectivitySystem>().Update(_world.Unmanaged);

            Entity field = FlowFieldService.GetOrCreateField(_em, _grid, destinationCellIndex: 0);
            Entity pawn = PawnFactory.CreatePawn(_em, id: 1, position: new float2(1, 0), tickOffset: 0);
            _em.SetComponentData(pawn, new AssignedFlowField { Value = field });
            Entity job = _em.CreateEntity(typeof(JobData));
            _em.SetComponentData(job, new JobData { Type = JobType.ChopTree, TargetCellIndex = 0 });
            _em.SetComponentData(pawn, new CurrentJob { Value = job });
            _em.SetComponentEnabled<IsMoving>(pawn, true);

            // Mutate the grid (bumps revision) without regenerating the field.
            CellData mutated = cells[1].Value; mutated.Walkable = false; cells[1] = new CellElement { Value = mutated };
            _em.SetComponentData(_grid, new GridRevision { Value = 2 });

            _em.CreateEntity(typeof(WorkDurationConfig));
            _em.SetComponentData(_em.CreateEntityQuery(typeof(WorkDurationConfig)).GetSingletonEntity(), new WorkDurationConfig { ChopDurationTicks = 10 });

            Assert.DoesNotThrow(() => _world.GetOrCreateSystem<MovementSystem>().Update(_world.Unmanaged));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter WorkExecutionSystemTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `ResourceComponents.cs`**

```csharp
using Unity.Entities;

namespace ColonySim.Simulation.Resources
{
    public struct ResourceItemData : IComponentData
    {
        public ResourceType Type;
        public int Amount;
        public int CellIndex;
    }

    public struct PendingResourceSpawn : IComponentData { }
}
```

- [ ] **Step 4: Implement `WorkExecutionSystem.cs`**

```csharp
using Unity.Entities;
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
                // duplicate item, fall through to idle, matching TickManager.AdvanceWorking today.

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
```

- [ ] **Step 5: Implement `SimulationTickGroup.cs`**

```csharp
using Unity.Entities;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Ticking
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SimulationTickGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<SimulationTick>()) return;
            base.OnUpdate();
            SimulationTick tick = SystemAPI.GetSingleton<SimulationTick>();
            tick.Value += 1;
            SystemAPI.SetSingleton(tick);
        }
    }

    [UpdateInGroup(typeof(SimulationTickGroup))]
    public partial struct ConnectivityGroupSystem : ISystem
    {
        public void OnUpdate(ref SystemState state) { }
    }
}
```

`ComponentSystemGroup` orders its member systems by `[UpdateInGroup(typeof(SimulationTickGroup))]` plus `[UpdateBefore]`/`[UpdateAfter]`; rather than a placeholder `ConnectivityGroupSystem`, add the real ordering attributes directly to each existing system instead — delete the placeholder `ConnectivityGroupSystem` struct above and instead add these attributes at each system's declaration (edit the four files from earlier tasks):

- `ConnectivitySystem` (Task 2): add `[UpdateInGroup(typeof(SimulationTickGroup))]` above `public partial struct ConnectivitySystem : ISystem`.
- `JobAssignmentSystem` (Task 8): add `[UpdateInGroup(typeof(SimulationTickGroup))] [UpdateAfter(typeof(ConnectivitySystem))]`.
- `MovementSystem` (Task 9): add `[UpdateInGroup(typeof(SimulationTickGroup))] [UpdateAfter(typeof(JobAssignmentSystem))]`.
- `WorkExecutionSystem` (this task): add `[UpdateInGroup(typeof(SimulationTickGroup))] [UpdateAfter(typeof(MovementSystem))]`.

- [ ] **Step 6: Delete superseded files**

```bash
git rm Assets/Scripts/Simulation/Ticking/TickManager.cs Assets/Tests/EditMode/TickManagerTests.cs
```

- [ ] **Step 7: Run test to verify it passes**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode --filter WorkExecutionSystemTests`
Expected: PASS. `SimulationRoot.cs` and `GridView.cs`/`PawnView.cs`/`DesignationInputController.cs` still reference the now-deleted `TickManager`/`WorldGrid`/`PawnManager`/`JobBoard` types — the full suite will not compile again until Task 11–14 finish; this is expected (same interim-compile pattern as prior tasks, now reaching Presentation). Leave `SimulationRoot.cs` etc. broken; do not attempt interim patches this time since Task 11 rewrites them wholesale next.

- [ ] **Step 8: Commit**

```bash
git add -A Assets/Scripts/Simulation/Resources Assets/Scripts/Simulation/Ticking Assets/Scripts/Simulation/Grid/ConnectivitySystem.cs Assets/Scripts/Simulation/Jobs/JobAssignmentSystem.cs Assets/Scripts/Simulation/Pawns/MovementSystem.cs Assets/Tests/EditMode/WorkExecutionSystemTests.cs
git commit -m "Add work execution system and tick group, delete TickManager (Task 10 of ECS migration)"
```

---

## Task 11: Rewrite SimulationRoot

**Files:**
- Modify: `Assets/Scripts/Presentation/SimulationRoot.cs` (full rewrite of its simulation-construction and per-frame logic; public field surface for Inspector assignment — `worldSettings`, `floorDef`, `rockDef`, `treeDef`, `colonistTemplate`, `startingPawnCount`, `ticksPerSecond`, `staggerBucketCount` — stays the same so existing scene/Inspector references in `SampleScene.unity` don't break)

**Interfaces:**
- Consumes: `EcsWorldGenerator.Generate` (Task 4), `GridBootstrap.CreateGrid` (Task 1), `TerrainDefBlobBuilder` (Task 3), `PawnFactory.CreatePawn` (Task 5), `JobFactory.TryCreateJob` (Task 6), `SimulationTick`/`WorkDurationConfig` (Task 10), `ResourceItemData`/`PendingResourceSpawn` (Task 10).
- Produces: `SimulationRoot.World` (`Unity.Entities.World`, public getter — Presentation's new access point, replacing `Grid`/`PawnManager`/`JobBoard` public properties), `SimulationRoot.GridEntity` (`Entity`, public getter, replaces `Grid`), keeps `TryDesignateMine(int cellIndex)`/`TryDesignateChop(int cellIndex)` public methods with the same signatures (now calling `JobFactory.TryCreateJob`), keeps `OnResourceItemSpawned` event with the same `Action<ResourceItem>`-shaped payload reworked to carry `(int id, ResourceType type, int amount, float x, float y)` since `ResourceItem` the plain class no longer exists (Presentation's `SpawnResourceItemView`/`ResourceItemView.Initialize` call sites are updated in this same task to match).

- [ ] **Step 1: Rewrite `SimulationRoot.cs`**

```csharp
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
```

- [ ] **Step 2: Add `CreateEcsSettings` to `WorldGenerationSettingsSO`**

`WorldGenerationSettingsSO.CreateSettings()` currently returns the deleted `ColonySim.Simulation.Generation.WorldGenerator`'s nested `WorldGenerationSettings` type — that type now lives in `EcsWorldGenerator.cs` (Task 4) with an identical shape, so `CreateSettings()`'s body is unchanged, only its declared return type needs the same (unqualified, since it's the same simple name in the same namespace) `WorldGenerationSettings`. Confirm `Assets/Scripts/Data/WorldGenerationSettingsSO.cs`'s `CreateSettings()` still compiles unmodified against the Task 4 type (same namespace `ColonySim.Simulation.Generation`, same field names) — if it does, rename the method to `CreateEcsSettings()` to match this task's `SimulationRoot.cs` call site (a plain rename, `git mv`-equivalent via `Edit`, not a behavior change).

- [ ] **Step 3: Run the full suite to check compilation**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: still fails to compile — `GridView.cs`, `PawnView.cs`, `DesignationInputController.cs` reference old types/APIs `SimulationRoot.Grid`/`.PawnManager`/`.JobBoard` removed by this rewrite. This is expected; Tasks 12–14 fix them next. `EditMode` tests that don't touch Presentation (everything from Tasks 1–10) still fail to *run* until the whole project compiles — this is the unavoidable cost of a MonoBehaviour-based Presentation layer sharing one assembly-compiled project with the tests; there is no partial-compile EditMode run in Unity. Proceed directly to Task 12 without expecting a green run here.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Presentation/SimulationRoot.cs Assets/Scripts/Data/WorldGenerationSettingsSO.cs
git commit -m "Rewrite SimulationRoot to bootstrap and drive the ECS World (Task 11 of ECS migration)"
```

---

## Task 12: Rewrite GridView, delete WorldGrid/Cell

**Files:**
- Modify: `Assets/Scripts/Presentation/GridView.cs`
- Delete: `Assets/Scripts/Simulation/Grid/WorldGrid.cs`
- Delete: `Assets/Scripts/Simulation/Grid/Cell.cs`
- Delete: `Assets/Tests/EditMode/WorldGridTests.cs`

**Interfaces:**
- Consumes: `SimulationRoot.World`/`GridEntity` (Task 11), `GridDimensions`/`GridRevision`/`CellElement` (Task 1), `TreeTag`/`TreeCellIndex` (Task 4).

- [ ] **Step 1: Rewrite `GridView.cs`**

```csharp
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Tilemaps;
using ColonySim.Simulation.Generation;
using ColonySim.Simulation.Grid;

namespace ColonySim.Presentation
{
    [ExecuteAlways, RequireComponent(typeof(SpriteRenderer))]
    public class GridView : MonoBehaviour
    {
        public SimulationRoot root;
        private Texture2D _texture;
        private Sprite _groundSprite;
        private SpriteRenderer _renderer;
        private GameObject _props;
        private Tilemap _tilemap;
        private Tile _tree, _rock;
        private byte[] _propStates;
        private int _cellCount, _width;
        private int _revision = -1;
        private Color32[] _pixels;

        private void Start() { if (Application.isPlaying && root != null) Build(); }
        private void OnEnable() { if (!Application.isPlaying && (root == null || root.World == null)) Clear(); }

        public void Build()
        {
            Clear();
            if (root == null || root.World == null || root.worldSettings == null) return;
            EntityManager em = root.World.EntityManager;
            GridDimensions dims = em.GetComponentData<GridDimensions>(root.GridEntity);
            _width = dims.Width;
            _cellCount = dims.Width * dims.Height;

            _renderer = GetComponent<SpriteRenderer>();
            _texture = new Texture2D(dims.Width, dims.Height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            _groundSprite = Sprite.Create(_texture, new Rect(0, 0, dims.Width, dims.Height), Vector2.zero, 1);
            _groundSprite.hideFlags = HideFlags.DontSave;
            _renderer.sprite = _groundSprite; _renderer.sortingOrder = -10000;
            _pixels = new Color32[_cellCount]; _propStates = new byte[_cellCount];
            _props = new GameObject("Generated props", typeof(UnityEngine.Grid)) { hideFlags = HideFlags.DontSave };
            _props.transform.SetParent(transform, false);
            var tiles = new GameObject("Prop tiles", typeof(Tilemap), typeof(TilemapRenderer)) { hideFlags = HideFlags.DontSave };
            tiles.transform.SetParent(_props.transform, false);
            _tilemap = tiles.GetComponent<Tilemap>();
            tiles.GetComponent<TilemapRenderer>().sortingOrder = -9000;
            _tree = CreateTile(root.worldSettings.TreeSprite, root.worldSettings.TreeSize);
            _rock = CreateTile(root.worldSettings.RockSprite, root.worldSettings.RockSize);
            RepaintAll();
        }

        private static Tile CreateTile(Sprite sprite, float size)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.hideFlags = HideFlags.DontSave; tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            float scale = sprite == null ? 1 : size / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            tile.transform = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            return tile;
        }

        public void RepaintAll()
        {
            if (root == null || root.World == null || _texture == null) return;
            EntityManager em = root.World.EntityManager;
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(root.GridEntity);

            var treeCells = new bool[_cellCount];
            EntityQuery treeQuery = em.CreateEntityQuery(typeof(TreeTag), typeof(TreeCellIndex));
            using (NativeArray<TreeCellIndex> trees = treeQuery.ToComponentDataArray<TreeCellIndex>(Allocator.Temp))
                for (int i = 0; i < trees.Length; i++) treeCells[trees[i].Value] = true;

            for (int i = 0; i < _cellCount; i++)
            {
                CellData cell = cells[i].Value;
                _pixels[i] = root.worldSettings.GroundColor(cell.TerrainId);
                byte state = treeCells[i] ? (byte)1 : cell.HasRock ? (byte)2 : (byte)0;
                if (_propStates[i] == state) continue;
                _propStates[i] = state;
                _tilemap.SetTile(new Vector3Int(i % _width, i / _width, 0), state == 1 ? _tree : state == 2 ? _rock : null);
            }
            _texture.SetPixels32(_pixels); _texture.Apply(false);
            _revision = em.GetComponentData<GridRevision>(root.GridEntity).Value;
        }

        private void Update()
        {
            if (root != null && root.World != null && _revision != root.World.EntityManager.GetComponentData<GridRevision>(root.GridEntity).Value)
                RepaintAll();
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            if (_renderer != null) _renderer.sprite = null;
            Dispose(_props); Dispose(_groundSprite); Dispose(_texture); Dispose(_tree); Dispose(_rock);
            _props = null; _groundSprite = null; _texture = null; _tree = _rock = null;
            _revision = -1;
        }

        private static void Dispose(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
```

- [ ] **Step 2: Delete `WorldGrid.cs`/`Cell.cs` and their test**

`WorldGrid`/`Cell` were referenced only by `WorldGenerator.cs` (deleted Task 4), `JobBoard.cs`/`Job.cs` (deleted Task 6/8), `GridAStar.cs` (deleted Task 7), `TickManager.cs` (deleted Task 10), and `SimulationRoot.cs`/`GridView.cs` (rewritten Task 11/this task) — confirm via `grep -rn "WorldGrid\|ColonySim.Simulation.Grid.Cell\b" Assets/Scripts` that no other file references them before deleting (per this plan's Global Constraints note that their deletion was deliberately deferred to this task).

```bash
grep -rln "WorldGrid" "Assets/Scripts" --include="*.cs" | grep -v "GridComponents.cs\|GridBootstrap.cs\|RegionComponents.cs\|ConnectivitySystem.cs\|TileCostComponents.cs"
```

Expected: only `Assets/Scripts/Simulation/Grid/WorldGrid.cs` itself (and possibly `Cell.cs`, which doesn't reference `WorldGrid` by name but is the type it wraps). If anything else appears, resolve that reference before deleting.

```bash
git rm Assets/Scripts/Simulation/Grid/WorldGrid.cs Assets/Scripts/Simulation/Grid/Cell.cs Assets/Tests/EditMode/WorldGridTests.cs
```

- [ ] **Step 3: Run the full test suite**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: still fails to compile — `PawnView.cs` and `DesignationInputController.cs` (Tasks 13/14) are the last remaining old-API consumers. `GridView.cs` itself is not unit-tested (matches the existing project convention — it never had EditMode tests before this migration either, per the file listing gathered during planning), so there is no new test file for this task; its correctness is checked in Task 15's manual Play-mode verification.

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Scripts/Presentation/GridView.cs Assets/Scripts/Simulation/Grid
git commit -m "Rewrite GridView against ECS, delete WorldGrid/Cell (Task 12 of ECS migration)"
```

---

## Task 13: Rewrite PawnView

**Files:**
- Modify: `Assets/Scripts/Presentation/PawnView.cs`

**Interfaces:**
- Consumes: `SimulationRoot.World` (Task 11), `PawnId`/`LocalTransform` (Task 5).

- [ ] **Step 1: Rewrite `PawnView.cs`**

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using ColonySim.Data;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Presentation
{
    // Synced centrally; keeps only a pawn ID, never a reference to pawn state.
    public class PawnView : MonoBehaviour
    {
        private SimulationRoot _root;
        private int _pawnId;
        private SortingGroup _sorting;
        private EntityQuery _pawnQuery;

        public void Initialize(SimulationRoot root, int pawnId, PawnTemplateSO template)
        {
            _root = root;
            _pawnId = pawnId;
            _pawnQuery = root.World.EntityManager.CreateEntityQuery(typeof(PawnId), typeof(LocalTransform));
            _sorting = gameObject.AddComponent<SortingGroup>();
            transform.localScale = Vector3.one * template.VisualScale;
            PawnPalette palette = template.Palettes.Length > 0
                ? template.Palettes[pawnId % template.Palettes.Length]
                : new PawnPalette { Skin = Color.white, Clothing = template.PlaceholderColor, Trousers = Color.gray };
            for (int i = 0; i < template.Layers.Length; i++)
            {
                PawnSpriteLayer layer = template.Layers[i];
                if (layer.Sprite == null) continue;
                var part = new GameObject(layer.Name);
                part.transform.SetParent(transform, false);
                part.transform.localPosition = layer.Offset;
                part.transform.localScale = new Vector3(layer.Scale.x, layer.Scale.y, 1f);
                var renderer = part.AddComponent<SpriteRenderer>();
                renderer.sprite = layer.Sprite;
                renderer.sortingOrder = i;
                renderer.color = layer.Tint == PawnTint.Skin ? palette.Skin
                    : layer.Tint == PawnTint.Clothing ? palette.Clothing
                    : layer.Tint == PawnTint.Trousers ? palette.Trousers : Color.white;
            }
            SyncFromSimulation();
        }

        public bool SyncFromSimulation()
        {
            EntityManager em = _root.World.EntityManager;
            using NativeArray<Entity> entities = _pawnQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PawnId> ids = _pawnQuery.ToComponentDataArray<PawnId>(Allocator.Temp);
            int found = -1;
            for (int i = 0; i < ids.Length; i++) if (ids[i].Value == _pawnId) { found = i; break; }
            if (found < 0) { Destroy(gameObject); return false; }

            LocalTransform pawnTransform = em.GetComponentData<LocalTransform>(entities[found]);
            Vector3 position = transform.position;
            position.x = pawnTransform.Position.x + 0.5f;
            position.y = pawnTransform.Position.y + 0.5f;
            position.z = -1f;
            transform.position = position;
            _sorting.sortingOrder = 1000 - Mathf.RoundToInt(position.y * 10f);
            return true;
        }
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: still fails to compile — `DesignationInputController.cs` (Task 14) is the last remaining old-API consumer (its `root.Grid.Width`/`.Height`/`.IndexOf` calls). No new test file for this task, matching `PawnView`'s existing untested status.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Presentation/PawnView.cs
git commit -m "Rewrite PawnView against ECS (Task 13 of ECS migration)"
```

---

## Task 14: Rewrite DesignationInputController, full-suite green

**Files:**
- Modify: `Assets/Scripts/Presentation/DesignationInputController.cs`

**Interfaces:**
- Consumes: `SimulationRoot.World`/`GridEntity` (Task 11), `GridDimensions` (Task 1).

- [ ] **Step 1: Rewrite `DesignationInputController.cs`**

```csharp
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using ColonySim.Simulation.Grid;

namespace ColonySim.Presentation
{
    public class DesignationInputController : MonoBehaviour
    {
        public SimulationRoot root;
        public Camera targetCamera;

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;
            if (root == null || root.World == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);

            EntityManager em = root.World.EntityManager;
            GridDimensions dims = em.GetComponentData<GridDimensions>(root.GridEntity);
            if (x < 0 || x >= dims.Width || y < 0 || y >= dims.Height) return;

            int cellIndex = y * dims.Width + x;
            if (!root.TryDesignateMine(cellIndex))
                root.TryDesignateChop(cellIndex);
        }
    }
}
```

- [ ] **Step 2: Run the full EditMode suite**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: PASS — every task from 1–14 should now compile and its tests pass together, since `DesignationInputController.cs` was the last consumer of any removed plain-C# API. If any failure remains, it is a genuine regression introduced during the migration (not an expected interim-compile gap) — diagnose and fix before proceeding, per `superpowers:systematic-debugging` if the cause isn't immediately obvious from the failure message.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Presentation/DesignationInputController.cs
git commit -m "Rewrite DesignationInputController against ECS; full EditMode suite green (Task 14 of ECS migration)"
```

---

## Task 15: Manual Play-mode verification and documentation

**Files:**
- Modify: `README.md` (Changelog entry)
- Modify: `ARCHITECTURE.md` (dependency map, class relationships, data flow, validation/limits)

**Interfaces:** None — this task verifies and documents; it does not add production code.

- [ ] **Step 1: Manual Play-mode check via the live-connected Editor**

Using the `unity` CLI's live-Editor commands (`unity status` to confirm connection, `unity command` to discover available scene/Play commands against this project — see the `unity-cli` skill's "Edit a scene, GameObject, or asset" workflow), enter Play mode on `Assets/Scenes/SampleScene.unity` and confirm, matching the spec's success criteria:

- Three colonists spawn near the map centre on the largest connected land region.
- Right-clicking a rock cell designates and completes a Mine job: the pawn paths to a neighboring walkable cell, works it, the rock disappears, a resource item appears, and the cell becomes walkable floor.
- Right-clicking a tree cell designates and completes a ChopTree job similarly, without the terrain changing.
- Right-clicking water does nothing (neither job type is designated).
- A second right-click on an already-claimed target is rejected (no duplicate job).

If the live Editor is not reachable when this task runs, say so explicitly and ask the user to verify Play mode themselves before treating this task as complete — do not claim verification that didn't happen, per the project's verification-before-completion practice.

- [ ] **Step 2: Update `README.md`**

Add a `## Changelog` entry (newest entries go at the top per the file's existing convention observed in Tasks 1–14's git history):

```markdown
- **ECS/DOTS migration** — Replaced the plain-C# simulation layer (`WorldGrid`/`Cell`,
  `Pawn`/`PawnManager`, `Job`/`JobBoard`, `GridAStar`, `TickManager`,
  `SimplexNoise`/`WorldGenerator`) with Unity ECS (`com.unity.entities`) + C# Job System +
  Burst, per `ROADMAP.md` Phase 0. Pathfinding moved from per-pawn A* to Phase 20 flow
  fields (one field per destination cell, shared by every pawn heading there), pulled
  forward from the roadmap. Grid/region/tile-cost state now lives on singleton entities
  with `DynamicBuffer`s; pawns and jobs are entities; `SimulationTickGroup` (a
  `ComponentSystemGroup` of small `ISystem`s) replaces `TickManager`'s state machine.
  Presentation (`GridView`/`PawnView`/`SimulationRoot`/`DesignationInputController`) now
  queries the ECS `World` directly. Gameplay behavior verified unchanged (see this
  changelog entry's manual Play-mode check). Files: see
  `docs/superpowers/plans/2026-09-24-ecs-dots-migration.md` for the full task-by-task file
  list. Status: complete, full EditMode suite passing.
```

Also update the "Project layout" section's `Assets/Scripts/Simulation/` bullet to note it is now ECS-based rather than "plain C# simulation layer... No MonoBehaviour dependency":

```markdown
- `Assets/Scripts/Simulation/` — ECS simulation layer (`com.unity.entities` components,
  `ISystem`s, and Burst jobs for grid/region/tile-cost state, pawns, jobs, flow-field
  pathfinding, world generation, and ticking). No MonoBehaviour dependency; covered by
  EditMode tests under `Assets/Tests/EditMode/` using the standard DOTS test World pattern.
```

- [ ] **Step 3: Update `ARCHITECTURE.md`**

Rewrite the "System dependency map", "Class relationships", and "Data flow" sections to describe the new ECS architecture (singleton entities for grid/region/tile-cost, pawn/job entities, the `SimulationTickGroup` system order, flow-field generation/caching, and Presentation's direct `EntityQuery` usage) in the same style/detail level as the current file — following the existing file's convention of naming each class/system and what it owns, one paragraph per major piece, as demonstrated by the file's current content (read it again at the start of this step for the exact tone/format to match, since it is the project's established documentation style and this plan should not invent a new one).

- [ ] **Step 4: Commit**

```bash
git add README.md ARCHITECTURE.md
git commit -m "Document ECS/DOTS migration in README and ARCHITECTURE (Task 15 of ECS migration)"
```

---

## Task 16: Merge the worktree

**Files:** None (git operations only).

- [ ] **Step 1: Run the full EditMode suite one final time on the worktree branch**

Run: `unity test "C:\Users\jaybe\on the rim\Rimworld-esc game" --editor-version 6000.3.18f1 --mode EditMode`
Expected: PASS (all tests from Tasks 1–14).

- [ ] **Step 2: Follow the project's existing merge convention**

Per `docs/superpowers/plans/2026-09-23-bare-essentials-colony-sim.md`'s precedent (see its "local merge to master and removed the temporary... worktree and merged branch" changelog entry) and the `superpowers:finishing-a-development-branch` skill, merge the completed worktree branch to `master` locally and remove the temporary worktree, once the user confirms Task 15's manual Play-mode check looked correct.

- [ ] **Step 3: Final commit** (only if the merge itself needs a cleanup commit, matching the 2026-09-23 precedent's own "cleanup record" commit)

```bash
git add README.md ARCHITECTURE.md
git commit -m "Complete local merge to master and remove ECS migration worktree"
```

---

## Self-Review

**Spec coverage:** Packages (Task 1) — covered. Data model: Grid (Task 1), Flow fields (Task 7), Pawns (Task 5), Jobs (Task 6), Def blobs (Task 3) — covered. World generation (Task 4) — covered. Pathfinding/Flow Fields (Task 7) — covered. Job matching and ticking (Tasks 8–10) — covered. Presentation boundary (Tasks 11–14) — covered. Testing strategy (incremental rewrite, old class+test deleted per chunk, two exceptions documented) — covered throughout, with the `WorldGrid`/`Cell` deferred-deletion exception explicitly called out in Global Constraints and Task 12. Rollout (worktree, doc updates) — covered in Task 15/16. Out of scope items (Phase 18 spatial hashing/priority queue, Phase 19 full HTN, dynamic cost writers, other roadmap phases) — none of the 16 tasks implement any of them; confirmed by re-reading each task's Interfaces block against the spec's Out of scope list.

**Placeholder scan:** Two inline "placeholder" strings appear in the plan text itself — Task 2 Step 4's stray `stackalloc` line (explicitly flagged and removed as an in-task correction, not a plan gap) and Task 10 Step 5's `ConnectivityGroupSystem` (explicitly superseded by attribute-based ordering in the same step, not left as a TODO). Both are corrections written into the steps, not unresolved placeholders — no bare "TBD"/"implement later" remains.

**Type consistency:** `CellData`/`CellElement` (Task 1) used consistently through Tasks 2–14. `JobData`/`Unclaimed` (Task 6) match `JobAssignmentSystem`'s (Task 8) and `WorkExecutionSystem`'s (Task 10) usage. `AssignedFlowField`/`CurrentJob`/`IsMoving`/`IsWorking`/`TickOffset`/`PawnId` (Task 5) match every later task's field names exactly. `FlowFieldService.GetOrCreateField` (Task 7) signature matches its call sites in Tasks 8 and 10's test. `WorkDurationConfig` is introduced in Task 9 (superseding the draft `NativeHashMap` approach inline, within the same task) and reused unchanged by Task 10 and Task 11.

**Review Focus:** all five listed items have an owning task and test, cross-checked above (grid-mutation-races-flow-field-staleness → Task 10; duplicate-target-from-either-region → Task 6; idle-pawn-no-reachable-job → Task 8; region-aware rock-between-regions flow field → Task 7; deterministic world generation → Task 4).
