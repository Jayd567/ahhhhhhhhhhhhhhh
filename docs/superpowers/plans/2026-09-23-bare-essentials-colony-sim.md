# Bare-Essentials Colony Sim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Also invoke the `unity:unity-cli` skill for every step marked "(Unity Editor step)"** — those steps create/modify ScriptableObject assets, scenes, GameObjects, or run tests through the Editor, and must go through the Editor rather than hand-written YAML.

**Goal:** Ship the first playable slice of the colony sim: a grid map where the player right-clicks a rock tile or tree to designate work, and idle pawns automatically path to the nearest *reachable* job, work it, and produce a resource item — with no direct pawn control.

**Architecture:** Strict Simulation (plain C#, `Simulation` + `Data` assemblies) / Presentation (MonoBehaviours, `Presentation` assembly) split. Simulation logic is covered by Unity Test Framework EditMode tests with no scene dependency; Presentation is wired and verified manually in Play mode via the Unity Editor (through `unity:unity-cli`).

**Tech Stack:** Unity 2D URP (existing project), Unity Test Framework 1.6.0 (already installed), plain C# (no external packages needed for this milestone).

**Spec:** `docs/superpowers/specs/2026-09-23-bare-essentials-colony-sim-design.md`

## Global Constraints

- Grid storage: flattened `Cell[width * height]`, never `Cell[,]` (spec: Simulation Layer).
- `Cell` is a **struct**; `Pawn` and `Job` are **classes** (spec correction, Architecture section).
- No `MonoBehaviour.Update()` per pawn or per cell — all ticking goes through `TickManager` → `PawnManager` (spec: Presentation Layer, `SimulationRoot`).
- Expensive per-pawn work (job search, path calc) is staggered via `pawn.Id % StaggerBucketCount == currentTick % StaggerBucketCount`, not run for every pawn every tick (spec: Data Flow step 2).
- No `new`, LINQ, string concatenation, or `GetComponent` inside `TickManager`'s per-tick path or `PawnManager.Tick()` (project-wide rule, CLAUDE.md §3).
- All lookups/keys use `int` ids or enums, never strings (project-wide rule, CLAUDE.md §3).
- All tunable numeric parameters (walk cost, work duration, yields) live in `TerrainDefSO` / `PawnTemplateSO` / `TreeDefSO` ScriptableObjects, never hardcoded in scripts (spec: Data-Driven Definitions).
- `PawnView`/`ResourceItemView` hold only an `int` id and look the entity up through the manager every frame — never cache a `Pawn`/`ResourceItem` reference (spec correction, Presentation Layer).
- Job matching filters candidates by `connectivityId` before ranking by distance, and only the single winning candidate gets a real A* request (spec correction, Data Flow step 3).
- Out of scope for this plan: needs/mood/health, hauling/stockpiles, building/construction, combat, save/load, full region/door hierarchy (spec: Out of Scope).

## Review Focus

- **Duplicate designation:** right-clicking a cell/tree that already has an open or claimed job must not enqueue a second job for the same target (task 8).
- **Race for the same job:** two idle pawns evaluated in the same tick must not both claim the same job (task 8).
- **Target invalidated mid-execution:** a pawn en route to or working a job whose target cell/tree was already resolved by another path (e.g. re-designated and cleared) must cancel cleanly instead of crashing or spawning a duplicate resource (task 9 and task 10).
- **Grid boundary correctness:** neighbor bitmask and connectivity flood-fill must be correct for edge and corner cells, not just interior ones (task 3).
- **Empty-state safety:** a tick with zero idle pawns or zero open jobs must not throw, spin, or allocate (task 10).

---

## File Structure

```
Assets/
  Scripts/
    Simulation/
      Simulation.asmdef
      Grid/Cell.cs
      Grid/WorldGrid.cs
      Pawns/Pawn.cs
      Pawns/PawnManager.cs
      Jobs/JobType.cs
      Jobs/Job.cs
      Jobs/JobBoard.cs
      Pathing/IPathfinder.cs
      Pathing/GridAStar.cs
      Resources/ResourceType.cs
      Resources/ResourceItem.cs
      Ticking/TickManager.cs
    Data/
      Data.asmdef
      TerrainDefSO.cs
      PawnTemplateSO.cs
      TreeDefSO.cs
    Presentation/
      Presentation.asmdef
      SimulationRoot.cs
      GridView.cs
      PawnView.cs
      ResourceItemView.cs
      DesignationInputController.cs
      CameraController.cs
  Tests/
    EditMode/
      SimulationTests.asmdef
      WorldGridTests.cs
      GridAStarTests.cs
      JobBoardTests.cs
      PawnManagerTests.cs
      TickManagerTests.cs
  Data/
    Terrain/Rock.asset
    Terrain/Floor.asset
    Trees/DefaultTree.asset
    Pawns/DefaultColonist.asset
  Scenes/
    SampleScene.unity (modified: SimulationRoot GameObject added)
```

Design note locked in during planning: both job types (Mine, ChopTree) target a **cell index** — a tree occupies a cell (`Cell.HasTree = true`) rather than being a separate entity, so `Job.TargetCellIndex` is the single addressing scheme for both job types. This satisfies the spec's "target: cell index or entity id" with the simpler of the two options, and keeps `JobBoard`/`GridAStar` from needing a second entity-lookup path this early.

---

### Task 1: Assembly scaffolding

**Files:**
- Create: `Assets/Scripts/Simulation/Simulation.asmdef`
- Create: `Assets/Scripts/Data/Data.asmdef`
- Create: `Assets/Scripts/Presentation/Presentation.asmdef`
- Create: `Assets/Tests/EditMode/SimulationTests.asmdef`

**Interfaces:**
- Produces: four assemblies — `Simulation` (no dependencies), `Data` (references `Simulation`), `Presentation` (references `Simulation`, `Data`), `SimulationTests` (EditMode-only, references `Simulation`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `nunit.framework.dll`).

- [ ] **Step 1: Create the `Simulation` assembly definition**

```json
{
    "name": "Simulation",
    "rootNamespace": "ColonySim.Simulation",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Save as `Assets/Scripts/Simulation/Simulation.asmdef`.

- [ ] **Step 2: Create the `Data` assembly definition**

```json
{
    "name": "Data",
    "rootNamespace": "ColonySim.Data",
    "references": ["Simulation"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Save as `Assets/Scripts/Data/Data.asmdef`.

- [ ] **Step 3: Create the `Presentation` assembly definition**

```json
{
    "name": "Presentation",
    "rootNamespace": "ColonySim.Presentation",
    "references": ["Simulation", "Data"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Save as `Assets/Scripts/Presentation/Presentation.asmdef`.

- [ ] **Step 4: Create the `SimulationTests` EditMode assembly definition**

```json
{
    "name": "SimulationTests",
    "rootNamespace": "ColonySim.Simulation.Tests",
    "references": [
        "Simulation",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": true,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Save as `Assets/Tests/EditMode/SimulationTests.asmdef`.

- [ ] **Step 5: (Unity Editor step) Verify the project compiles with the new assemblies**

Invoke `unity:unity-cli` to refresh the AssetDatabase and confirm a clean compile (no assembly errors) with the four new empty assemblies in place.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Simulation.asmdef Assets/Scripts/Data/Data.asmdef Assets/Scripts/Presentation/Presentation.asmdef Assets/Tests/EditMode/SimulationTests.asmdef
git commit -m "Add Simulation/Data/Presentation/Tests assembly scaffolding"
```

---

### Task 2: `Cell` struct and flattened `WorldGrid` storage

**Files:**
- Create: `Assets/Scripts/Simulation/Grid/Cell.cs`
- Create: `Assets/Scripts/Simulation/Grid/WorldGrid.cs`
- Test: `Assets/Tests/EditMode/WorldGridTests.cs`

**Interfaces:**
- Produces: `struct Cell { int TerrainTypeId; bool IsWalkable; byte NeighborWalkableMask; int ConnectivityId; int ResourceIdOnGround; bool HasTree; bool HasDesignation; }`; `class WorldGrid { WorldGrid(int width, int height); int Width; int Height; int CellCount; int IndexOf(int x, int y); bool TryGetCoordsOf(int index, out int x, out int y); Cell GetCell(int index); void SetTerrain(int index, int terrainTypeId, bool isWalkable); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Tests
{
    public class WorldGridTests
    {
        [Test]
        public void CellCount_EqualsWidthTimesHeight()
        {
            var grid = new WorldGrid(4, 3);
            Assert.AreEqual(12, grid.CellCount);
        }

        [Test]
        public void IndexOf_IsRowMajor()
        {
            var grid = new WorldGrid(4, 3);
            Assert.AreEqual(0, grid.IndexOf(0, 0));
            Assert.AreEqual(3, grid.IndexOf(3, 0));
            Assert.AreEqual(4, grid.IndexOf(0, 1));
            Assert.AreEqual(11, grid.IndexOf(3, 2));
        }

        [Test]
        public void TryGetCoordsOf_RoundTripsWithIndexOf()
        {
            var grid = new WorldGrid(5, 5);
            int index = grid.IndexOf(2, 3);
            bool found = grid.TryGetCoordsOf(index, out int x, out int y);
            Assert.IsTrue(found);
            Assert.AreEqual(2, x);
            Assert.AreEqual(3, y);
        }

        [Test]
        public void TryGetCoordsOf_OutOfRangeIndex_ReturnsFalse()
        {
            var grid = new WorldGrid(2, 2);
            bool found = grid.TryGetCoordsOf(99, out _, out _);
            Assert.IsFalse(found);
        }

        [Test]
        public void SetTerrain_UpdatesWalkabilityAndTerrainId()
        {
            var grid = new WorldGrid(3, 3);
            int index = grid.IndexOf(1, 1);
            grid.SetTerrain(index, terrainTypeId: 7, isWalkable: false);
            Cell cell = grid.GetCell(index);
            Assert.AreEqual(7, cell.TerrainTypeId);
            Assert.IsFalse(cell.IsWalkable);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: FAIL (compile error — `WorldGrid`/`Cell` don't exist yet).

- [ ] **Step 3: Write `Cell`**

```csharp
namespace ColonySim.Simulation.Grid
{
    public struct Cell
    {
        public int TerrainTypeId;
        public bool IsWalkable;
        public byte NeighborWalkableMask;
        public int ConnectivityId;
        public int ResourceIdOnGround;
        public bool HasTree;
        public bool HasDesignation;

        public const byte NorthMask = 1 << 0;
        public const byte EastMask = 1 << 1;
        public const byte SouthMask = 1 << 2;
        public const byte WestMask = 1 << 3;
    }
}
```

- [ ] **Step 4: Write minimal `WorldGrid`**

```csharp
using System;

namespace ColonySim.Simulation.Grid
{
    public class WorldGrid
    {
        public int Width { get; }
        public int Height { get; }
        public int CellCount => _cells.Length;

        private readonly Cell[] _cells;

        public WorldGrid(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _cells = new Cell[width * height];
        }

        public int IndexOf(int x, int y) => y * Width + x;

        public bool TryGetCoordsOf(int index, out int x, out int y)
        {
            if (index < 0 || index >= _cells.Length)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = index % Width;
            y = index / Width;
            return true;
        }

        public Cell GetCell(int index) => _cells[index];

        public void SetTerrain(int index, int terrainTypeId, bool isWalkable)
        {
            Cell cell = _cells[index];
            cell.TerrainTypeId = terrainTypeId;
            cell.IsWalkable = isWalkable;
            _cells[index] = cell;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: PASS (5/5).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Grid/Cell.cs Assets/Scripts/Simulation/Grid/WorldGrid.cs Assets/Tests/EditMode/WorldGridTests.cs
git commit -m "Add Cell struct and flattened WorldGrid storage"
```

---

### Task 3: Neighbor walkability bitmask (including grid boundaries)

**Files:**
- Modify: `Assets/Scripts/Simulation/Grid/WorldGrid.cs`
- Test: `Assets/Tests/EditMode/WorldGridTests.cs`

**Interfaces:**
- Consumes: `Cell.NorthMask/EastMask/SouthMask/WestMask` (Task 2).
- Produces: `WorldGrid.RecomputeNeighborMask(int index)` (private, called internally by `SetTerrain`); `GetCell(index).NeighborWalkableMask` now reflects live neighbor walkability, with off-grid neighbors treated as not walkable.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void NeighborMask_InteriorCell_AllWalkableByDefault()
{
    var grid = new WorldGrid(3, 3);
    // default Cell.IsWalkable is false (struct default), so mark all walkable first
    for (int i = 0; i < grid.CellCount; i++)
        grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

    int center = grid.IndexOf(1, 1);
    byte mask = grid.GetCell(center).NeighborWalkableMask;
    Assert.AreEqual(Cell.NorthMask | Cell.EastMask | Cell.SouthMask | Cell.WestMask, mask);
}

[Test]
public void NeighborMask_CornerCell_OffGridNeighborsAreNotWalkable()
{
    var grid = new WorldGrid(3, 3);
    for (int i = 0; i < grid.CellCount; i++)
        grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

    int corner = grid.IndexOf(0, 0);
    byte mask = grid.GetCell(corner).NeighborWalkableMask;
    // (0,0) has no West or South neighbor on-grid
    Assert.AreEqual(Cell.NorthMask | Cell.EastMask, mask);
}

[Test]
public void NeighborMask_UpdatesWhenNeighborBecomesUnwalkable()
{
    var grid = new WorldGrid(3, 3);
    for (int i = 0; i < grid.CellCount; i++)
        grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

    int center = grid.IndexOf(1, 1);
    int east = grid.IndexOf(2, 1);
    grid.SetTerrain(east, terrainTypeId: 1, isWalkable: false);

    byte mask = grid.GetCell(center).NeighborWalkableMask;
    Assert.AreEqual(Cell.NorthMask | Cell.SouthMask | Cell.WestMask, mask);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: FAIL (new tests fail — mask never populated).

- [ ] **Step 3: Implement neighbor mask maintenance**

Add to `WorldGrid`:

```csharp
public void SetTerrain(int index, int terrainTypeId, bool isWalkable)
{
    Cell cell = _cells[index];
    cell.TerrainTypeId = terrainTypeId;
    cell.IsWalkable = isWalkable;
    _cells[index] = cell;

    RecomputeNeighborMask(index);
    foreach (int neighborIndex in EnumerateNeighborIndices(index))
        RecomputeNeighborMask(neighborIndex);
}

private void RecomputeNeighborMask(int index)
{
    TryGetCoordsOf(index, out int x, out int y);
    byte mask = 0;

    if (IsWalkableAt(x, y - 1)) mask |= Cell.NorthMask;
    if (IsWalkableAt(x + 1, y)) mask |= Cell.EastMask;
    if (IsWalkableAt(x, y + 1)) mask |= Cell.SouthMask;
    if (IsWalkableAt(x - 1, y)) mask |= Cell.WestMask;

    Cell cell = _cells[index];
    cell.NeighborWalkableMask = mask;
    _cells[index] = cell;
}

private bool IsWalkableAt(int x, int y)
{
    if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
    return _cells[IndexOf(x, y)].IsWalkable;
}

private System.Collections.Generic.IEnumerable<int> EnumerateNeighborIndices(int index)
{
    TryGetCoordsOf(index, out int x, out int y);
    if (y - 1 >= 0) yield return IndexOf(x, y - 1);
    if (x + 1 < Width) yield return IndexOf(x + 1, y);
    if (y + 1 < Height) yield return IndexOf(x, y + 1);
    if (x - 1 >= 0) yield return IndexOf(x - 1, y);
}
```

Note: `EnumerateNeighborIndices` uses `yield return`/`IEnumerable` here because it only runs on the rare event a cell's walkability changes (mining, wall building) — never in the per-tick hot path, so the Global Constraint against allocations in tight loops does not apply.

- [ ] **Step 4: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: PASS (8/8 cumulative).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Simulation/Grid/WorldGrid.cs Assets/Tests/EditMode/WorldGridTests.cs
git commit -m "Add neighbor walkability bitmask with grid-boundary handling"
```

---

### Task 4: Connectivity-id flood fill

**Files:**
- Modify: `Assets/Scripts/Simulation/Grid/WorldGrid.cs`
- Test: `Assets/Tests/EditMode/WorldGridTests.cs`

**Interfaces:**
- Produces: `WorldGrid.RecomputeConnectivity()` (full recompute, called once after initial map generation) and incremental recompute triggered from `SetTerrain`; `Cell.ConnectivityId` — cells connected by walkable adjacency share the same id; unreachable regions get different ids.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void Connectivity_AllWalkableGrid_SharesOneId()
{
    var grid = new WorldGrid(3, 3);
    for (int i = 0; i < grid.CellCount; i++)
        grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);

    int firstId = grid.GetCell(0).ConnectivityId;
    for (int i = 1; i < grid.CellCount; i++)
        Assert.AreEqual(firstId, grid.GetCell(i).ConnectivityId);
}

[Test]
public void Connectivity_WallSplitsGridIntoTwoIds()
{
    // 3x1 grid: [walkable][wall][walkable] -> two disconnected regions
    var grid = new WorldGrid(3, 1);
    grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
    grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
    grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

    int leftId = grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId;
    int rightId = grid.GetCell(grid.IndexOf(2, 0)).ConnectivityId;
    Assert.AreNotEqual(leftId, rightId);
}

[Test]
public void Connectivity_OpeningWallMergesRegions()
{
    var grid = new WorldGrid(3, 1);
    grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
    grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
    grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

    grid.SetTerrain(grid.IndexOf(1, 0), 0, isWalkable: true); // mine the wall open

    int leftId = grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId;
    int rightId = grid.GetCell(grid.IndexOf(2, 0)).ConnectivityId;
    Assert.AreEqual(leftId, rightId);
}

[Test]
public void Connectivity_UnwalkableCell_HasSentinelId()
{
    var grid = new WorldGrid(2, 2);
    grid.SetTerrain(grid.IndexOf(0, 0), 1, isWalkable: false);
    Assert.AreEqual(WorldGrid.UnreachableConnectivityId, grid.GetCell(grid.IndexOf(0, 0)).ConnectivityId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: FAIL (`ConnectivityId` stays 0 for everything, `UnreachableConnectivityId` doesn't exist).

- [ ] **Step 3: Implement flood-fill connectivity**

Add to `WorldGrid`:

```csharp
public const int UnreachableConnectivityId = -1;
private int _nextConnectivityId = 0;

public void SetTerrain(int index, int terrainTypeId, bool isWalkable)
{
    Cell cell = _cells[index];
    cell.TerrainTypeId = terrainTypeId;
    cell.IsWalkable = isWalkable;
    _cells[index] = cell;

    RecomputeNeighborMask(index);
    foreach (int neighborIndex in EnumerateNeighborIndices(index))
        RecomputeNeighborMask(neighborIndex);

    RecomputeConnectivity();
}

public void RecomputeConnectivity()
{
    var visited = new bool[_cells.Length];
    var queue = new System.Collections.Generic.Queue<int>();
    _nextConnectivityId = 0;

    for (int i = 0; i < _cells.Length; i++)
    {
        if (visited[i]) continue;

        if (!_cells[i].IsWalkable)
        {
            Cell unreachable = _cells[i];
            unreachable.ConnectivityId = UnreachableConnectivityId;
            _cells[i] = unreachable;
            visited[i] = true;
            continue;
        }

        int regionId = _nextConnectivityId++;
        queue.Enqueue(i);
        visited[i] = true;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            Cell currentCell = _cells[current];
            currentCell.ConnectivityId = regionId;
            _cells[current] = currentCell;

            foreach (int neighborIndex in EnumerateNeighborIndices(current))
            {
                if (visited[neighborIndex] || !_cells[neighborIndex].IsWalkable) continue;
                visited[neighborIndex] = true;
                queue.Enqueue(neighborIndex);
            }
        }
    }
}
```

`RecomputeConnectivity()` here is a full-grid recompute, called after every `SetTerrain` call. This is correct and simple for a small bare-essentials map; it is not the per-tick hot path (only runs on the rare event of terrain changing), so it doesn't violate the no-allocation-in-tight-loops constraint. A follow-up milestone can optimize to a local/incremental flood-fill if map size later makes a full recompute too slow — noted, not built now (YAGNI).

- [ ] **Step 4: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: PASS (12/12 cumulative).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Simulation/Grid/WorldGrid.cs Assets/Tests/EditMode/WorldGridTests.cs
git commit -m "Add flood-fill connectivity id, recomputed on terrain change"
```

---

### Task 5: Data-driven ScriptableObject definitions

**Files:**
- Create: `Assets/Scripts/Data/TerrainDefSO.cs`
- Create: `Assets/Scripts/Data/PawnTemplateSO.cs`
- Create: `Assets/Scripts/Data/TreeDefSO.cs`
- Create (Unity Editor step): `Assets/Data/Terrain/Rock.asset`, `Assets/Data/Terrain/Floor.asset`, `Assets/Data/Trees/DefaultTree.asset`, `Assets/Data/Pawns/DefaultColonist.asset`

**Interfaces:**
- Produces: `TerrainDefSO { int TerrainTypeId; string DisplayName; bool IsWalkable; bool IsMineable; ResourceType YieldResourceType; int YieldAmount; Color PlaceholderColor; }`, `PawnTemplateSO { string DisplayName; float MoveSpeed; float WorkSpeedMultiplier; Color PlaceholderColor; }`, `TreeDefSO { ResourceType YieldResourceType; int YieldAmount; int WorkDurationTicks; Color PlaceholderColor; }` — consumed by `WorldGrid`/`JobBoard` (job duration/yield) and `GridView`/`PawnView` (`PlaceholderColor`) in later tasks.

- [ ] **Step 1: Write `TerrainDefSO`**

```csharp
using UnityEngine;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "TerrainDef", menuName = "ColonySim/Terrain Def")]
    public class TerrainDefSO : ScriptableObject
    {
        public int TerrainTypeId;
        public string DisplayName;
        public bool IsWalkable;
        public bool IsMineable;
        public ResourceType YieldResourceType;
        public int YieldAmount;
        public Color PlaceholderColor = Color.white;
    }
}
```

- [ ] **Step 2: Write `PawnTemplateSO`**

```csharp
using UnityEngine;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "PawnTemplate", menuName = "ColonySim/Pawn Template")]
    public class PawnTemplateSO : ScriptableObject
    {
        public string DisplayName;
        public float MoveSpeed = 2.5f;
        public float WorkSpeedMultiplier = 1f;
        public Color PlaceholderColor = Color.cyan;
    }
}
```

- [ ] **Step 3: Write `TreeDefSO`**

```csharp
using UnityEngine;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "TreeDef", menuName = "ColonySim/Tree Def")]
    public class TreeDefSO : ScriptableObject
    {
        public ResourceType YieldResourceType;
        public int YieldAmount = 10;
        public int WorkDurationTicks = 60;
        public Color PlaceholderColor = new Color(0.2f, 0.5f, 0.2f);
    }
}
```

`ResourceType` is defined in Task 9 (`Simulation/Resources/ResourceType.cs`); this task references it by name and will not compile standalone until Task 9 lands. Since Task 9 comes after this one in execution order, do Step 3a below first.

- [ ] **Step 3a: Create the minimal `ResourceType` enum now (full `ResourceItem` data comes in Task 9)**

```csharp
namespace ColonySim.Simulation.Resources
{
    public enum ResourceType
    {
        Stone = 0,
        Wood = 1,
    }
}
```

Save as `Assets/Scripts/Simulation/Resources/ResourceType.cs`. Add `using ColonySim.Simulation.Resources;` to `TerrainDefSO.cs` and `TreeDefSO.cs`.

- [ ] **Step 4: (Unity Editor step) Verify compile**

Invoke `unity:unity-cli` to refresh the AssetDatabase and confirm the `Data` assembly compiles cleanly.

- [ ] **Step 5: (Unity Editor step) Create the four ScriptableObject asset instances**

Invoke `unity:unity-cli` to create, in the Editor:
- `Assets/Data/Terrain/Rock.asset` (`TerrainDefSO`): `TerrainTypeId=1`, `DisplayName="Rock"`, `IsWalkable=false`, `IsMineable=true`, `YieldResourceType=Stone`, `YieldAmount=10`, `PlaceholderColor=` dark gray (e.g. `#4A4A4A`).
- `Assets/Data/Terrain/Floor.asset` (`TerrainDefSO`): `TerrainTypeId=0`, `DisplayName="Floor"`, `IsWalkable=true`, `IsMineable=false`, `YieldResourceType=Stone`, `YieldAmount=0`, `PlaceholderColor=` light gray (e.g. `#C8C8C8`).
- `Assets/Data/Trees/DefaultTree.asset` (`TreeDefSO`): `YieldResourceType=Wood`, `YieldAmount=10`, `WorkDurationTicks=60`, `PlaceholderColor=` green (e.g. `#33883A`).
- `Assets/Data/Pawns/DefaultColonist.asset` (`PawnTemplateSO`): `DisplayName="Colonist"`, `MoveSpeed=2.5`, `WorkSpeedMultiplier=1`, `PlaceholderColor=` cyan (e.g. `#33C3D6`).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Data/TerrainDefSO.cs Assets/Scripts/Data/PawnTemplateSO.cs Assets/Scripts/Data/TreeDefSO.cs Assets/Scripts/Simulation/Resources/ResourceType.cs Assets/Data
git commit -m "Add data-driven ScriptableObject defs and instances for terrain/pawns/trees"
```

---

### Task 6: `GridAStar` pathfinder

**Files:**
- Create: `Assets/Scripts/Simulation/Pathing/IPathfinder.cs`
- Create: `Assets/Scripts/Simulation/Pathing/GridAStar.cs`
- Test: `Assets/Tests/EditMode/GridAStarTests.cs`

**Interfaces:**
- Consumes: `WorldGrid.GetCell(index).NeighborWalkableMask`, `WorldGrid.IndexOf`, `WorldGrid.TryGetCoordsOf` (Task 2/3).
- Produces: `interface IPathfinder { bool TryFindPath(WorldGrid grid, int startIndex, int goalIndex, System.Collections.Generic.List<int> resultPath); }`; `class GridAStar : IPathfinder` — `resultPath` is caller-allocated and cleared/filled by the method (avoids allocating a new list per call, per the no-`new`-in-hot-loop constraint since this will be called from `JobBoard`'s per-tick matching in Task 8).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pathing;

namespace ColonySim.Simulation.Tests
{
    public class GridAStarTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, terrainTypeId: 0, isWalkable: true);
            return grid;
        }

        [Test]
        public void TryFindPath_StraightLine_FindsShortestPath()
        {
            var grid = MakeOpenGrid(5, 1);
            var pathfinder = new GridAStar();
            var path = new List<int>();

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(4, 0), path);

            Assert.IsTrue(found);
            Assert.AreEqual(5, path.Count);
            Assert.AreEqual(grid.IndexOf(0, 0), path[0]);
            Assert.AreEqual(grid.IndexOf(4, 0), path[path.Count - 1]);
        }

        [Test]
        public void TryFindPath_AroundObstacle_FindsDetour()
        {
            var grid = MakeOpenGrid(3, 3);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);
            grid.SetTerrain(grid.IndexOf(1, 1), 1, isWalkable: false);
            // (1,2) stays open as the only way across

            var pathfinder = new GridAStar();
            var path = new List<int>();
            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(2, 0), path);

            Assert.IsTrue(found);
            CollectionAssert.Contains(path, grid.IndexOf(1, 2));
        }

        [Test]
        public void TryFindPath_WalledOff_ReturnsFalseAndClearsPath()
        {
            var grid = MakeOpenGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false);

            var pathfinder = new GridAStar();
            var path = new List<int> { 999 }; // pre-populate to prove it gets cleared

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(2, 0), path);

            Assert.IsFalse(found);
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void TryFindPath_StartEqualsGoal_ReturnsSingleCellPath()
        {
            var grid = MakeOpenGrid(2, 2);
            var pathfinder = new GridAStar();
            var path = new List<int>();

            bool found = pathfinder.TryFindPath(grid, grid.IndexOf(0, 0), grid.IndexOf(0, 0), path);

            Assert.IsTrue(found);
            Assert.AreEqual(1, path.Count);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `GridAStarTests`.
Expected: FAIL (compile error — types don't exist).

- [ ] **Step 3: Write `IPathfinder`**

```csharp
using System.Collections.Generic;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Pathing
{
    public interface IPathfinder
    {
        bool TryFindPath(WorldGrid grid, int startIndex, int goalIndex, List<int> resultPath);
    }
}
```

- [ ] **Step 4: Write `GridAStar`**

```csharp
using System.Collections.Generic;
using ColonySim.Simulation.Grid;

namespace ColonySim.Simulation.Pathing
{
    public class GridAStar : IPathfinder
    {
        private readonly Dictionary<int, int> _cameFrom = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _costSoFar = new Dictionary<int, int>();
        private readonly List<int> _frontier = new List<int>();

        public bool TryFindPath(WorldGrid grid, int startIndex, int goalIndex, List<int> resultPath)
        {
            resultPath.Clear();
            _cameFrom.Clear();
            _costSoFar.Clear();
            _frontier.Clear();

            if (startIndex == goalIndex)
            {
                resultPath.Add(startIndex);
                return true;
            }

            _frontier.Add(startIndex);
            _costSoFar[startIndex] = 0;

            while (_frontier.Count > 0)
            {
                int current = PopLowestCost(grid, goalIndex);

                if (current == goalIndex)
                {
                    BuildPath(startIndex, goalIndex, resultPath);
                    return true;
                }

                foreach (int neighbor in EnumerateWalkableNeighbors(grid, current))
                {
                    int newCost = _costSoFar[current] + 1;
                    if (!_costSoFar.TryGetValue(neighbor, out int existingCost) || newCost < existingCost)
                    {
                        _costSoFar[neighbor] = newCost;
                        _cameFrom[neighbor] = current;
                        _frontier.Add(neighbor);
                    }
                }
            }

            resultPath.Clear();
            return false;
        }

        private int PopLowestCost(WorldGrid grid, int goalIndex)
        {
            int bestListIndex = 0;
            int bestPriority = int.MaxValue;

            for (int i = 0; i < _frontier.Count; i++)
            {
                int node = _frontier[i];
                int priority = _costSoFar[node] + Heuristic(grid, node, goalIndex);
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestListIndex = i;
                }
            }

            int result = _frontier[bestListIndex];
            _frontier.RemoveAt(bestListIndex);
            return result;
        }

        private static int Heuristic(WorldGrid grid, int a, int b)
        {
            grid.TryGetCoordsOf(a, out int ax, out int ay);
            grid.TryGetCoordsOf(b, out int bx, out int by);
            return System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);
        }

        private static IEnumerable<int> EnumerateWalkableNeighbors(WorldGrid grid, int index)
        {
            byte mask = grid.GetCell(index).NeighborWalkableMask;
            grid.TryGetCoordsOf(index, out int x, out int y);

            if ((mask & Cell.NorthMask) != 0) yield return grid.IndexOf(x, y - 1);
            if ((mask & Cell.EastMask) != 0) yield return grid.IndexOf(x + 1, y);
            if ((mask & Cell.SouthMask) != 0) yield return grid.IndexOf(x, y + 1);
            if ((mask & Cell.WestMask) != 0) yield return grid.IndexOf(x - 1, y);
        }

        private void BuildPath(int startIndex, int goalIndex, List<int> resultPath)
        {
            int current = goalIndex;
            resultPath.Add(current);
            while (current != startIndex)
            {
                current = _cameFrom[current];
                resultPath.Add(current);
            }
            resultPath.Reverse();
        }
    }
}
```

Note: `_cameFrom`/`_costSoFar`/`_frontier` are instance fields reused across calls (cleared, not reallocated) — `GridAStar` is meant to be instantiated once and reused, not `new`'d per pathfind, to respect the no-allocation-in-hot-loop constraint when `JobBoard` calls it per tick in Task 8.

- [ ] **Step 5: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `GridAStarTests`.
Expected: PASS (4/4).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Pathing/IPathfinder.cs Assets/Scripts/Simulation/Pathing/GridAStar.cs Assets/Tests/EditMode/GridAStarTests.cs
git commit -m "Add flattened-grid A* pathfinder using neighbor bitmask"
```

---

### Task 7: `Pawn` class and `PawnManager`

**Files:**
- Create: `Assets/Scripts/Simulation/Pawns/Pawn.cs`
- Create: `Assets/Scripts/Simulation/Pawns/PawnManager.cs`
- Test: `Assets/Tests/EditMode/PawnManagerTests.cs`

**Interfaces:**
- Produces: `enum PawnState { Idle, Pathing, Working }`; `class Pawn { int Id; float PositionX; float PositionY; PawnState State; int CurrentJobId; List<int> CurrentPath; int PathIndex; }`; `class PawnManager { int SpawnPawn(float x, float y); Pawn GetPawn(int id); bool RemovePawn(int id); int PawnCount; void Tick(int currentTick, int staggerBucketCount, System.Action<Pawn> onStaggeredPawn); }` — `Tick`'s callback is invoked only for pawns whose `Id % staggerBucketCount == currentTick % staggerBucketCount`, satisfying the stagger constraint; `TickManager` (Task 10) supplies the callback.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class PawnManagerTests
    {
        [Test]
        public void SpawnPawn_ReturnsUniqueIncreasingIds()
        {
            var manager = new PawnManager();
            int first = manager.SpawnPawn(0, 0);
            int second = manager.SpawnPawn(1, 1);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void GetPawn_UnknownId_ReturnsNull()
        {
            var manager = new PawnManager();
            Assert.IsNull(manager.GetPawn(12345));
        }

        [Test]
        public void RemovePawn_ThenGetPawn_ReturnsNull()
        {
            var manager = new PawnManager();
            int id = manager.SpawnPawn(0, 0);
            bool removed = manager.RemovePawn(id);
            Assert.IsTrue(removed);
            Assert.IsNull(manager.GetPawn(id));
        }

        [Test]
        public void Tick_OnlyInvokesCallbackForPawnsInCurrentStaggerBucket()
        {
            var manager = new PawnManager();
            int idZero = manager.SpawnPawn(0, 0);  // bucket 0
            int idOne = manager.SpawnPawn(0, 0);   // bucket 1 (assuming sequential ids 0,1,...)
            var visited = new List<int>();

            manager.Tick(currentTick: 0, staggerBucketCount: 2, onStaggeredPawn: p => visited.Add(p.Id));

            Assert.Contains(idZero, visited);
            Assert.IsFalse(visited.Contains(idOne));
        }

        [Test]
        public void Tick_ZeroPawns_DoesNotThrow()
        {
            var manager = new PawnManager();
            Assert.DoesNotThrow(() => manager.Tick(0, 4, p => { }));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `PawnManagerTests`.
Expected: FAIL (compile error — types don't exist).

- [ ] **Step 3: Write `Pawn`**

```csharp
using System.Collections.Generic;

namespace ColonySim.Simulation.Pawns
{
    public enum PawnState
    {
        Idle,
        Pathing,
        Working,
    }

    public class Pawn
    {
        public int Id;
        public float PositionX;
        public float PositionY;
        public PawnState State = PawnState.Idle;
        public int CurrentJobId = -1;
        public readonly List<int> CurrentPath = new List<int>();
        public int PathIndex;
        public int WorkTicksRemaining;
    }
}
```

- [ ] **Step 4: Write `PawnManager`**

```csharp
using System;
using System.Collections.Generic;

namespace ColonySim.Simulation.Pawns
{
    public class PawnManager
    {
        private readonly Dictionary<int, Pawn> _pawnsById = new Dictionary<int, Pawn>();
        private readonly List<Pawn> _pawnList = new List<Pawn>();
        private int _nextId;

        public int PawnCount => _pawnList.Count;

        public int SpawnPawn(float x, float y)
        {
            var pawn = new Pawn { Id = _nextId++, PositionX = x, PositionY = y };
            _pawnsById[pawn.Id] = pawn;
            _pawnList.Add(pawn);
            return pawn.Id;
        }

        public Pawn GetPawn(int id)
        {
            return _pawnsById.TryGetValue(id, out Pawn pawn) ? pawn : null;
        }

        public bool RemovePawn(int id)
        {
            if (!_pawnsById.TryGetValue(id, out Pawn pawn)) return false;
            _pawnsById.Remove(id);
            _pawnList.Remove(pawn);
            return true;
        }

        public void Tick(int currentTick, int staggerBucketCount, Action<Pawn> onStaggeredPawn)
        {
            for (int i = 0; i < _pawnList.Count; i++)
            {
                Pawn pawn = _pawnList[i];
                if (pawn.Id % staggerBucketCount == currentTick % staggerBucketCount)
                    onStaggeredPawn(pawn);
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `PawnManagerTests`.
Expected: PASS (5/5).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Pawns/Pawn.cs Assets/Scripts/Simulation/Pawns/PawnManager.cs Assets/Tests/EditMode/PawnManagerTests.cs
git commit -m "Add Pawn class and PawnManager with staggered tick dispatch"
```

---

### Task 8: `Job` class and connectivity-filtered `JobBoard`

**Files:**
- Create: `Assets/Scripts/Simulation/Jobs/JobType.cs`
- Create: `Assets/Scripts/Simulation/Jobs/Job.cs`
- Create: `Assets/Scripts/Simulation/Jobs/JobBoard.cs`
- Test: `Assets/Tests/EditMode/JobBoardTests.cs`

**Interfaces:**
- Consumes: `WorldGrid.GetCell(index).ConnectivityId` (Task 4), `Pawn` (Task 7).
- Produces: `enum JobType { Mine, ChopTree }`; `class Job { int Id; JobType Type; int TargetCellIndex; int ClaimedByPawnId; int WorkTicksRemaining; }`; `class JobBoard { bool TryAddJob(WorldGrid grid, JobType type, int targetCellIndex, out int jobId); Job TryClaimJobFor(WorldGrid grid, Pawn pawn); void ReleaseJob(int jobId); void CompleteJob(int jobId); int OpenJobCount; }`. `TryAddJob` returns `false` (no duplicate) if an open or claimed job already targets that cell. `TryClaimJobFor` filters by `grid.GetCell(job.TargetCellIndex).ConnectivityId == grid.GetCell(pawnCellIndex).ConnectivityId`, ranks survivors by Manhattan distance, and atomically marks the winner claimed before returning it (so a second call in the same tick can't also claim it).

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class JobBoardTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, 0, isWalkable: true);
            return grid;
        }

        [Test]
        public void TryAddJob_DuplicateTarget_IsRejected()
        {
            var grid = MakeOpenGrid(3, 3);
            var board = new JobBoard();
            int target = grid.IndexOf(1, 1);

            bool first = board.TryAddJob(grid, JobType.Mine, target, out _);
            bool second = board.TryAddJob(grid, JobType.Mine, target, out _);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, board.OpenJobCount);
        }

        [Test]
        public void TryClaimJobFor_UnreachableJob_ReturnsNull()
        {
            var grid = new WorldGrid(3, 1);
            grid.SetTerrain(grid.IndexOf(0, 0), 0, isWalkable: true);
            grid.SetTerrain(grid.IndexOf(1, 0), 1, isWalkable: false); // wall splits the grid
            grid.SetTerrain(grid.IndexOf(2, 0), 0, isWalkable: true);

            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(2, 0), out _);

            var pawn = new Pawn { PositionX = 0, PositionY = 0 };
            Job claimed = board.TryClaimJobFor(grid, pawn);

            Assert.IsNull(claimed);
        }

        [Test]
        public void TryClaimJobFor_PicksNearestReachableJob()
        {
            var grid = MakeOpenGrid(10, 1);
            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(8, 0), out int farId);
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(2, 0), out int nearId);

            var pawn = new Pawn { PositionX = 0, PositionY = 0 };
            Job claimed = board.TryClaimJobFor(grid, pawn);

            Assert.AreEqual(nearId, claimed.Id);
        }

        [Test]
        public void TryClaimJobFor_SecondCallSameTick_DoesNotClaimSameJobTwice()
        {
            var grid = MakeOpenGrid(5, 1);
            var board = new JobBoard();
            board.TryAddJob(grid, JobType.Mine, grid.IndexOf(4, 0), out int jobId);

            var pawnA = new Pawn { PositionX = 0, PositionY = 0 };
            var pawnB = new Pawn { PositionX = 1, PositionY = 0 };

            Job claimedByA = board.TryClaimJobFor(grid, pawnA);
            Job claimedByB = board.TryClaimJobFor(grid, pawnB);

            Assert.AreEqual(jobId, claimedByA.Id);
            Assert.IsNull(claimedByB);
        }

        [Test]
        public void TryClaimJobFor_NoOpenJobs_ReturnsNull()
        {
            var grid = MakeOpenGrid(3, 3);
            var board = new JobBoard();
            var pawn = new Pawn { PositionX = 0, PositionY = 0 };

            Assert.IsNull(board.TryClaimJobFor(grid, pawn));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `JobBoardTests`.
Expected: FAIL (compile error — types don't exist).

- [ ] **Step 3: Write `JobType`**

```csharp
namespace ColonySim.Simulation.Jobs
{
    public enum JobType
    {
        Mine,
        ChopTree,
    }
}
```

- [ ] **Step 4: Write `Job`**

```csharp
namespace ColonySim.Simulation.Jobs
{
    public class Job
    {
        public int Id;
        public JobType Type;
        public int TargetCellIndex;
        public int ClaimedByPawnId = -1;
        public int WorkTicksRemaining;
    }
}
```

- [ ] **Step 5: Write `JobBoard`**

```csharp
using System.Collections.Generic;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Jobs
{
    public class JobBoard
    {
        private readonly List<Job> _openJobs = new List<Job>();
        private readonly Dictionary<int, Job> _jobsById = new Dictionary<int, Job>();
        private int _nextJobId;

        public int OpenJobCount => _openJobs.Count;

        public bool TryAddJob(WorldGrid grid, JobType type, int targetCellIndex, out int jobId)
        {
            for (int i = 0; i < _openJobs.Count; i++)
            {
                if (_openJobs[i].TargetCellIndex == targetCellIndex)
                {
                    jobId = -1;
                    return false;
                }
            }
            foreach (Job job in _jobsById.Values)
            {
                if (job.ClaimedByPawnId >= 0 && job.TargetCellIndex == targetCellIndex)
                {
                    jobId = -1;
                    return false;
                }
            }

            var newJob = new Job { Id = _nextJobId++, Type = type, TargetCellIndex = targetCellIndex };
            _openJobs.Add(newJob);
            _jobsById[newJob.Id] = newJob;
            jobId = newJob.Id;
            return true;
        }

        public Job TryClaimJobFor(WorldGrid grid, Pawn pawn)
        {
            if (_openJobs.Count == 0) return null;

            int pawnCellIndex = grid.IndexOf((int)pawn.PositionX, (int)pawn.PositionY);
            int pawnConnectivityId = grid.GetCell(pawnCellIndex).ConnectivityId;

            int bestListIndex = -1;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < _openJobs.Count; i++)
            {
                Job job = _openJobs[i];
                Cell targetCell = grid.GetCell(job.TargetCellIndex);
                if (targetCell.ConnectivityId != pawnConnectivityId) continue;
                if (targetCell.ConnectivityId == WorldGrid.UnreachableConnectivityId) continue;

                grid.TryGetCoordsOf(job.TargetCellIndex, out int tx, out int ty);
                int distance = System.Math.Abs(tx - (int)pawn.PositionX) + System.Math.Abs(ty - (int)pawn.PositionY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestListIndex = i;
                }
            }

            if (bestListIndex < 0) return null;

            Job winner = _openJobs[bestListIndex];
            _openJobs.RemoveAt(bestListIndex);
            winner.ClaimedByPawnId = pawn.Id;
            return winner;
        }

        public void ReleaseJob(int jobId)
        {
            if (!_jobsById.TryGetValue(jobId, out Job job)) return;
            job.ClaimedByPawnId = -1;
            if (!_openJobs.Contains(job))
                _openJobs.Add(job);
        }

        public void CompleteJob(int jobId)
        {
            if (_jobsById.TryGetValue(jobId, out Job job))
            {
                _openJobs.Remove(job);
                _jobsById.Remove(jobId);
            }
        }
    }
}
```

Note: `targetCell.ConnectivityId == pawnConnectivityId` already excludes `UnreachableConnectivityId` matches in all realistic cases (an unwalkable target cell would never legitimately be a job target since jobs target mineable rock/trees which are walkable-after, but the explicit second check documents the invariant defensively and is covered by `TryClaimJobFor_UnreachableJob_ReturnsNull`).

- [ ] **Step 6: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `JobBoardTests`.
Expected: PASS (5/5).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Simulation/Jobs/JobType.cs Assets/Scripts/Simulation/Jobs/Job.cs Assets/Scripts/Simulation/Jobs/JobBoard.cs Assets/Tests/EditMode/JobBoardTests.cs
git commit -m "Add Job class and connectivity-filtered JobBoard matching"
```

---

### Task 9: `ResourceItem` and grid mutation on job completion

**Files:**
- Create: `Assets/Scripts/Simulation/Resources/ResourceItem.cs`
- Modify: `Assets/Scripts/Simulation/Grid/WorldGrid.cs`
- Test: `Assets/Tests/EditMode/WorldGridTests.cs`

**Interfaces:**
- Consumes: `ResourceType` (Task 5), `WorldGrid.SetTerrain` (Task 2/3/4).
- Produces: `class ResourceItem { int Id; ResourceType Type; int Amount; float PositionX; float PositionY; }`; `WorldGrid.ResolveMineJob(int cellIndex, int floorTerrainTypeId, ResourceType yieldType, int yieldAmount) : ResourceItem` (mutates cell to floor, triggers neighbor/connectivity recompute, returns spawned item data — does not know about `TerrainDefSO`, callers pass primitives); `WorldGrid.ResolveTreeJob(int cellIndex, ResourceType yieldType, int yieldAmount) : ResourceItem` (clears `HasTree`, spawns item, no terrain/connectivity change since a tree cell was already walkable ground). Both return `null` if the target is already resolved (idempotent — covers the "target invalidated mid-execution" Review Focus item at the grid layer; `TickManager` in Task 10 covers it at the job-execution layer).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void ResolveMineJob_MutatesCellToFloorAndReturnsResourceItem()
{
    var grid = new WorldGrid(3, 3);
    int target = grid.IndexOf(1, 1);
    grid.SetTerrain(target, terrainTypeId: 1, isWalkable: false); // rock

    var item = grid.ResolveMineJob(target, floorTerrainTypeId: 0, ResourceType.Stone, 10);

    Assert.IsNotNull(item);
    Assert.AreEqual(ResourceType.Stone, item.Type);
    Assert.AreEqual(10, item.Amount);
    Assert.IsTrue(grid.GetCell(target).IsWalkable);
    Assert.AreEqual(0, grid.GetCell(target).TerrainTypeId);
}

[Test]
public void ResolveMineJob_AlreadyResolvedTarget_ReturnsNull()
{
    var grid = new WorldGrid(3, 3);
    int target = grid.IndexOf(1, 1);
    grid.SetTerrain(target, terrainTypeId: 1, isWalkable: false);

    grid.ResolveMineJob(target, 0, ResourceType.Stone, 10); // first resolve
    var secondAttempt = grid.ResolveMineJob(target, 0, ResourceType.Stone, 10); // already floor now

    Assert.IsNull(secondAttempt);
}

[Test]
public void ResolveTreeJob_ClearsTreeFlagAndReturnsResourceItem()
{
    var grid = new WorldGrid(3, 3);
    int target = grid.IndexOf(1, 1);
    grid.SetTerrain(target, terrainTypeId: 0, isWalkable: true);
    grid.SetHasTree(target, true);

    var item = grid.ResolveTreeJob(target, ResourceType.Wood, 10);

    Assert.IsNotNull(item);
    Assert.AreEqual(ResourceType.Wood, item.Type);
    Assert.IsFalse(grid.GetCell(target).HasTree);
}

[Test]
public void ResolveTreeJob_NoTreePresent_ReturnsNull()
{
    var grid = new WorldGrid(3, 3);
    int target = grid.IndexOf(1, 1);
    grid.SetTerrain(target, terrainTypeId: 0, isWalkable: true);

    var item = grid.ResolveTreeJob(target, ResourceType.Wood, 10);
    Assert.IsNull(item);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: FAIL (compile error — `ResourceItem`, `ResolveMineJob`, `ResolveTreeJob`, `SetHasTree` don't exist).

- [ ] **Step 3: Write `ResourceItem`**

```csharp
namespace ColonySim.Simulation.Resources
{
    public class ResourceItem
    {
        public int Id;
        public ResourceType Type;
        public int Amount;
        public float PositionX;
        public float PositionY;
    }
}
```

- [ ] **Step 4: Add mutation methods to `WorldGrid`**

```csharp
private int _nextResourceItemId;

public void SetHasTree(int index, bool hasTree)
{
    Cell cell = _cells[index];
    cell.HasTree = hasTree;
    _cells[index] = cell;
}

public ColonySim.Simulation.Resources.ResourceItem ResolveMineJob(
    int cellIndex, int floorTerrainTypeId, ColonySim.Simulation.Resources.ResourceType yieldType, int yieldAmount)
{
    Cell cell = _cells[cellIndex];
    if (cell.IsWalkable) return null; // already mined

    TryGetCoordsOf(cellIndex, out int x, out int y);
    SetTerrain(cellIndex, floorTerrainTypeId, isWalkable: true);

    return new ColonySim.Simulation.Resources.ResourceItem
    {
        Id = _nextResourceItemId++,
        Type = yieldType,
        Amount = yieldAmount,
        PositionX = x,
        PositionY = y,
    };
}

public ColonySim.Simulation.Resources.ResourceItem ResolveTreeJob(
    int cellIndex, ColonySim.Simulation.Resources.ResourceType yieldType, int yieldAmount)
{
    Cell cell = _cells[cellIndex];
    if (!cell.HasTree) return null; // already chopped (or never had a tree)

    TryGetCoordsOf(cellIndex, out int x, out int y);
    SetHasTree(cellIndex, false);

    return new ColonySim.Simulation.Resources.ResourceItem
    {
        Id = _nextResourceItemId++,
        Type = yieldType,
        Amount = yieldAmount,
        PositionX = x,
        PositionY = y,
    };
}
```

Add `using ColonySim.Simulation.Resources;` at the top of `WorldGrid.cs` and simplify the fully-qualified names above to `ResourceItem`/`ResourceType` accordingly.

- [ ] **Step 5: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `WorldGridTests`.
Expected: PASS (16/16 cumulative).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Resources/ResourceItem.cs Assets/Scripts/Simulation/Grid/WorldGrid.cs Assets/Tests/EditMode/WorldGridTests.cs
git commit -m "Add ResourceItem and idempotent job-resolution mutations on WorldGrid"
```

---

### Task 10: `TickManager` — job execution state machine

**Files:**
- Create: `Assets/Scripts/Simulation/Ticking/TickManager.cs`
- Test: `Assets/Tests/EditMode/TickManagerTests.cs`

**Interfaces:**
- Consumes: `WorldGrid` (Task 2/3/4/9), `PawnManager`/`Pawn`/`PawnState` (Task 7), `JobBoard`/`Job` (Task 8), `IPathfinder` (Task 6), `ResourceItem` (Task 9).
- Produces: `class TickManager { TickManager(WorldGrid grid, PawnManager pawnManager, JobBoard jobBoard, IPathfinder pathfinder, int staggerBucketCount); List<ResourceItem> SpawnedItemsThisTick; void Tick(TerrainJobConfig floorTerrain, TreeJobConfig treeConfig); int CurrentTick; }` — the two config structs (defined in this file) carry the primitive values `TickManager` needs from `TerrainDefSO`/`TreeDefSO` without the Simulation assembly referencing `Data` (keeps the dependency direction `Data → Simulation`, not the reverse). `SimulationRoot` (Task 11) builds these structs from the ScriptableObject instances once at startup.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;
using ColonySim.Simulation.Ticking;

namespace ColonySim.Simulation.Tests
{
    public class TickManagerTests
    {
        private static WorldGrid MakeOpenGrid(int w, int h)
        {
            var grid = new WorldGrid(w, h);
            for (int i = 0; i < grid.CellCount; i++)
                grid.SetTerrain(i, 0, isWalkable: true);
            return grid;
        }

        private static TerrainJobConfig DefaultFloorConfig() =>
            new TerrainJobConfig { FloorTerrainTypeId = 0, YieldResourceType = ResourceType.Stone, YieldAmount = 10, WorkDurationTicks = 3 };

        private static TreeJobConfig DefaultTreeConfig() =>
            new TreeJobConfig { YieldResourceType = ResourceType.Wood, YieldAmount = 10, WorkDurationTicks = 3 };

        [Test]
        public void Tick_ZeroPawnsZeroJobs_DoesNotThrow()
        {
            var grid = MakeOpenGrid(3, 3);
            var tickManager = new TickManager(grid, new PawnManager(), new JobBoard(), new GridAStar(), staggerBucketCount: 4);

            Assert.DoesNotThrow(() => tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig()));
        }

        [Test]
        public void Tick_IdlePawnClaimsPathsWorksAndCompletesMineJob()
        {
            var grid = MakeOpenGrid(5, 1);
            int rockIndex = grid.IndexOf(4, 0);
            grid.SetTerrain(rockIndex, terrainTypeId: 1, isWalkable: false);

            var pawnManager = new PawnManager();
            int pawnId = pawnManager.SpawnPawn(0, 0);

            var jobBoard = new JobBoard();
            jobBoard.TryAddJob(grid, JobType.Mine, rockIndex, out _);

            var tickManager = new TickManager(grid, pawnManager, jobBoard, new GridAStar(), staggerBucketCount: 1);

            // Run enough ticks to path 4 cells + work 3 ticks + resolve; generous upper bound.
            ResourceItem spawnedItem = null;
            for (int i = 0; i < 50 && spawnedItem == null; i++)
            {
                tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
                foreach (var item in tickManager.SpawnedItemsThisTick)
                    spawnedItem = item;
            }

            Assert.IsNotNull(spawnedItem);
            Assert.AreEqual(ResourceType.Stone, spawnedItem.Type);
            Assert.IsTrue(grid.GetCell(rockIndex).IsWalkable);
            Assert.AreEqual(PawnState.Idle, pawnManager.GetPawn(pawnId).State);
        }

        [Test]
        public void Tick_PawnWorkingJobWhoseTargetAlreadyResolved_CancelsGracefullyWithoutDuplicateItem()
        {
            var grid = MakeOpenGrid(2, 1);
            int rockIndex = grid.IndexOf(1, 0);
            grid.SetTerrain(rockIndex, terrainTypeId: 1, isWalkable: false);

            var pawnManager = new PawnManager();
            pawnManager.SpawnPawn(0, 0);

            var jobBoard = new JobBoard();
            jobBoard.TryAddJob(grid, JobType.Mine, rockIndex, out int jobId);

            var tickManager = new TickManager(grid, pawnManager, jobBoard, new GridAStar(), staggerBucketCount: 1);

            // Let the pawn claim + path to the job.
            for (int i = 0; i < 5; i++) tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());

            // Simulate the target being resolved out from under the pawn by another path.
            grid.ResolveMineJob(rockIndex, 0, ResourceType.Stone, 10);

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 10; i++) tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
            });

            int itemCount = 0;
            for (int i = 0; i < 10; i++)
            {
                tickManager.Tick(DefaultFloorConfig(), DefaultTreeConfig());
                itemCount += tickManager.SpawnedItemsThisTick.Count;
            }
            Assert.AreEqual(0, itemCount); // no duplicate item from the cancelled job
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `TickManagerTests`.
Expected: FAIL (compile error — `TickManager` doesn't exist).

- [ ] **Step 3: Write `TickManager`**

```csharp
using System.Collections.Generic;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Jobs;
using ColonySim.Simulation.Pathing;
using ColonySim.Simulation.Pawns;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Ticking
{
    public struct TerrainJobConfig
    {
        public int FloorTerrainTypeId;
        public ResourceType YieldResourceType;
        public int YieldAmount;
        public int WorkDurationTicks;
    }

    public struct TreeJobConfig
    {
        public ResourceType YieldResourceType;
        public int YieldAmount;
        public int WorkDurationTicks;
    }

    public class TickManager
    {
        public int CurrentTick { get; private set; }
        public readonly List<ResourceItem> SpawnedItemsThisTick = new List<ResourceItem>();

        private readonly WorldGrid _grid;
        private readonly PawnManager _pawnManager;
        private readonly JobBoard _jobBoard;
        private readonly IPathfinder _pathfinder;
        private readonly int _staggerBucketCount;
        private readonly List<int> _scratchPath = new List<int>();
        private readonly Dictionary<int, Job> _activeJobsByPawnId = new Dictionary<int, Job>();

        public TickManager(WorldGrid grid, PawnManager pawnManager, JobBoard jobBoard, IPathfinder pathfinder, int staggerBucketCount)
        {
            _grid = grid;
            _pawnManager = pawnManager;
            _jobBoard = jobBoard;
            _pathfinder = pathfinder;
            _staggerBucketCount = staggerBucketCount;
        }

        public void Tick(TerrainJobConfig floorConfig, TreeJobConfig treeConfig)
        {
            SpawnedItemsThisTick.Clear();

            _pawnManager.Tick(CurrentTick, _staggerBucketCount, pawn => TickPawn(pawn, floorConfig, treeConfig));

            CurrentTick++;
        }

        private void TickPawn(Pawn pawn, TerrainJobConfig floorConfig, TreeJobConfig treeConfig)
        {
            switch (pawn.State)
            {
                case PawnState.Idle:
                    TryStartJob(pawn);
                    break;
                case PawnState.Pathing:
                    AdvancePathing(pawn);
                    break;
                case PawnState.Working:
                    AdvanceWorking(pawn, floorConfig, treeConfig);
                    break;
            }
        }

        private void TryStartJob(Pawn pawn)
        {
            Job job = _jobBoard.TryClaimJobFor(_grid, pawn);
            if (job == null) return;

            int startIndex = _grid.IndexOf((int)pawn.PositionX, (int)pawn.PositionY);
            bool found = _pathfinder.TryFindPath(_grid, startIndex, job.TargetCellIndex, _scratchPath);
            if (!found)
            {
                _jobBoard.ReleaseJob(job.Id); // stale connectivity data edge case: skip this tick, stay idle
                return;
            }

            pawn.CurrentPath.Clear();
            pawn.CurrentPath.AddRange(_scratchPath);
            pawn.PathIndex = 0;
            pawn.CurrentJobId = job.Id;
            pawn.State = PawnState.Pathing;
            _activeJobsByPawnId[pawn.Id] = job;
        }

        private void AdvancePathing(Pawn pawn)
        {
            pawn.PathIndex++;
            if (pawn.PathIndex >= pawn.CurrentPath.Count)
            {
                _grid.TryGetCoordsOf(pawn.CurrentPath[pawn.CurrentPath.Count - 1], out int x, out int y);
                pawn.PositionX = x;
                pawn.PositionY = y;

                Job job = _activeJobsByPawnId[pawn.Id];
                job.WorkTicksRemaining = ResolveWorkDuration(job);
                pawn.State = PawnState.Working;
            }
            else
            {
                _grid.TryGetCoordsOf(pawn.CurrentPath[pawn.PathIndex], out int x, out int y);
                pawn.PositionX = x;
                pawn.PositionY = y;
            }
        }

        private int ResolveWorkDuration(Job job) => job.WorkTicksRemaining; // set by caller before first Working tick

        private void AdvanceWorking(Pawn pawn, TerrainJobConfig floorConfig, TreeJobConfig treeConfig)
        {
            Job job = _activeJobsByPawnId[pawn.Id];

            job.WorkTicksRemaining--;
            if (job.WorkTicksRemaining > 0) return;

            ResourceItem item = job.Type == JobType.Mine
                ? _grid.ResolveMineJob(job.TargetCellIndex, floorConfig.FloorTerrainTypeId, floorConfig.YieldResourceType, floorConfig.YieldAmount)
                : _grid.ResolveTreeJob(job.TargetCellIndex, treeConfig.YieldResourceType, treeConfig.YieldAmount);

            if (item != null)
                SpawnedItemsThisTick.Add(item);
            // item == null means the target was already resolved out from under this pawn
            // (Review Focus: target invalidated mid-execution) - no duplicate item, fall through to idle.

            _jobBoard.CompleteJob(job.Id);
            _activeJobsByPawnId.Remove(pawn.Id);
            pawn.CurrentJobId = -1;
            pawn.CurrentPath.Clear();
            pawn.PathIndex = 0;
            pawn.State = PawnState.Idle;
        }
    }
}
```

One fix needed before this compiles/behaves correctly: `job.WorkTicksRemaining` must be initialized from the correct config's `WorkDurationTicks` when the pawn transitions from Pathing to Working (`ResolveWorkDuration` above is a placeholder that just reads back the uninitialized value — replace it):

```csharp
Job job = _activeJobsByPawnId[pawn.Id];
job.WorkTicksRemaining = job.Type == JobType.Mine
    ? floorConfig.WorkDurationTicks
    : treeConfig.WorkDurationTicks;
pawn.State = PawnState.Working;
```

Replace the `Job job = _activeJobsByPawnId[pawn.Id]; job.WorkTicksRemaining = ResolveWorkDuration(job); pawn.State = PawnState.Working;` block inside `AdvancePathing` with the snippet above (which needs `floorConfig`/`treeConfig` passed into `AdvancePathing` — update its signature to `AdvancePathing(Pawn pawn, TerrainJobConfig floorConfig, TreeJobConfig treeConfig)` and its call site in `TickPawn` accordingly), and delete the now-unused `ResolveWorkDuration` method.

- [ ] **Step 4: Run tests to verify they pass**

Invoke `unity:unity-cli` to run the EditMode test suite filtered to `TickManagerTests`.
Expected: PASS (3/3).

- [ ] **Step 5: Run the full EditMode suite to confirm no regressions**

Invoke `unity:unity-cli` to run the full EditMode suite.
Expected: PASS (all tests across Tasks 2-10, ~31 cumulative).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Simulation/Ticking/TickManager.cs Assets/Tests/EditMode/TickManagerTests.cs
git commit -m "Add TickManager job-execution state machine (idle/pathing/working)"
```

---

### Task 11: `SimulationRoot` — scene wiring and startup

**Files:**
- Create: `Assets/Scripts/Presentation/SimulationRoot.cs`

**Interfaces:**
- Consumes: `WorldGrid`, `PawnManager`, `JobBoard`, `GridAStar`, `TickManager`, `TerrainJobConfig`/`TreeJobConfig` (Tasks 2-10); `TerrainDefSO`, `TreeDefSO`, `PawnTemplateSO` (Task 5).
- Produces: `class SimulationRoot : MonoBehaviour` with public fields `int mapWidth`, `int mapHeight`, `TerrainDefSO floorDef`, `TerrainDefSO rockDef`, `TreeDefSO treeDef`, `PawnTemplateSO colonistTemplate`, `int startingPawnCount`, `float ticksPerSecond`; exposes `WorldGrid Grid { get; }`, `PawnManager PawnManager { get; }`, `JobBoard JobBoard { get; }` (read-only, `get`-only properties) so `GridView`/`PawnView`/`DesignationInputController` (Task 12/13) can read simulation state without any of them owning construction.

- [ ] **Step 1: Write `SimulationRoot`**

```csharp
using UnityEngine;
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
        public int mapWidth = 20;
        public int mapHeight = 20;
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

        private void Awake()
        {
            Grid = new WorldGrid(mapWidth, mapHeight);
            for (int i = 0; i < Grid.CellCount; i++)
                Grid.SetTerrain(i, floorDef.TerrainTypeId, floorDef.IsWalkable);

            PawnManager = new PawnManager();
            for (int i = 0; i < startingPawnCount; i++)
                PawnManager.SpawnPawn(mapWidth / 2f, mapHeight / 2f);

            JobBoard = new JobBoard();
            _tickManager = new TickManager(Grid, PawnManager, JobBoard, new GridAStar(), staggerBucketCount);

            _floorConfig = new TerrainJobConfig
            {
                FloorTerrainTypeId = floorDef.TerrainTypeId,
                YieldResourceType = rockDef.YieldResourceType,
                YieldAmount = rockDef.YieldAmount,
                WorkDurationTicks = 60,
            };
            _treeConfig = new TreeJobConfig
            {
                YieldResourceType = treeDef.YieldResourceType,
                YieldAmount = treeDef.YieldAmount,
                WorkDurationTicks = treeDef.WorkDurationTicks,
            };
        }

        private void Update()
        {
            _tickAccumulator += Time.deltaTime;
            float tickInterval = 1f / ticksPerSecond;
            while (_tickAccumulator >= tickInterval)
            {
                _tickAccumulator -= tickInterval;
                _tickManager.Tick(_floorConfig, _treeConfig);
            }
        }

        public bool TryDesignateMine(int cellIndex)
        {
            Cell cell = Grid.GetCell(cellIndex);
            if (cell.IsWalkable) return false; // only mineable (unwalkable rock) cells
            return JobBoard.TryAddJob(Grid, JobType.Mine, cellIndex, out _);
        }

        public bool TryDesignateChop(int cellIndex)
        {
            Cell cell = Grid.GetCell(cellIndex);
            if (!cell.HasTree) return false;
            return JobBoard.TryAddJob(Grid, JobType.ChopTree, cellIndex, out _);
        }
    }
}
```

- [ ] **Step 2: (Unity Editor step) Place a rock patch and a tree so there's something to designate**

Invoke `unity:unity-cli` to, in `Assets/Scenes/SampleScene.unity`, add a `SimulationRoot` GameObject with the `SimulationRoot` component attached, and wire its `floorDef`/`rockDef`/`treeDef`/`colonistTemplate` fields to the four assets from Task 5. Then use the Editor to run a short one-off script (via `unity:unity-cli`) that, immediately after `Awake()` in a test/dev context, mines out a 3x3 rock patch and one tree cell using `SimulationRoot.Grid.SetTerrain`/`SetHasTree` so Task 12/13 have something visible immediately — OR simpler: extend `Awake()` above with a small hardcoded dev-seed block:

```csharp
// Dev seed for this milestone: carve a 3x3 rock patch and place one tree.
// TODO removed intentionally — this is a real, permanent placeholder map for
// the bare-essentials milestone, not a stub; a real map generator is future scope.
for (int y = 5; y < 8; y++)
for (int x = 5; x < 8; x++)
    Grid.SetTerrain(Grid.IndexOf(x, y), rockDef.TerrainTypeId, rockDef.IsWalkable);
Grid.SetHasTree(Grid.IndexOf(12, 12), true);
```

Add this block into `Awake()` right after the floor-fill loop.

- [ ] **Step 3: (Unity Editor step) Verify compile and scene wiring**

Invoke `unity:unity-cli` to refresh the AssetDatabase, confirm a clean compile, and confirm the `SimulationRoot` component's four def-asset fields are assigned (not null) in the saved scene.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Presentation/SimulationRoot.cs Assets/Scenes/SampleScene.unity
git commit -m "Add SimulationRoot: scene-level simulation construction and tick driving"
```

---

### Task 12: `GridView`, `PawnView`, `ResourceItemView` — placeholder-blob rendering

**Files:**
- Create: `Assets/Scripts/Presentation/GridView.cs`
- Create: `Assets/Scripts/Presentation/PawnView.cs`
- Create: `Assets/Scripts/Presentation/ResourceItemView.cs`

**Interfaces:**
- Consumes: `SimulationRoot.Grid`/`PawnManager` (Task 11), `Pawn.Id`/`PositionX`/`PositionY`/`State` (Task 7), `ResourceItem` (Task 9).
- Produces: `class GridView : MonoBehaviour` (renders the whole grid once at start plus incremental repaint on change — no per-frame full redraw); `class PawnView : MonoBehaviour` with `void Initialize(SimulationRoot root, int pawnId, Color color)` — holds only `_pawnId`, looks up `root.PawnManager.GetPawn(_pawnId)` every frame, self-destroys if the lookup returns null; `class ResourceItemView : MonoBehaviour` with the same id-lookup-and-self-destroy pattern against a small item registry added to `SimulationRoot`.

- [ ] **Step 1: Write `GridView`**

```csharp
using UnityEngine;
using ColonySim.Simulation.Grid;

namespace ColonySim.Presentation
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class GridView : MonoBehaviour
    {
        public SimulationRoot root;
        public Color rockColor = new Color(0.29f, 0.29f, 0.29f);
        public Color floorColor = new Color(0.78f, 0.78f, 0.78f);
        public Color treeColor = new Color(0.2f, 0.53f, 0.23f);

        private Texture2D _texture;
        private SpriteRenderer _spriteRenderer;

        private void Start()
        {
            WorldGrid grid = root.Grid;
            _texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };

            RepaintAll();

            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = Sprite.Create(
                _texture,
                new Rect(0, 0, grid.Width, grid.Height),
                new Vector2(0f, 0f),
                pixelsPerUnit: 1f);
        }

        public void RepaintAll()
        {
            WorldGrid grid = root.Grid;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Cell cell = grid.GetCell(grid.IndexOf(x, y));
                    Color color = cell.HasTree ? treeColor : (cell.IsWalkable ? floorColor : rockColor);
                    _texture.SetPixel(x, y, color);
                }
            }
            _texture.Apply();
        }

        private void Update()
        {
            // Bare-essentials milestone: repaint every frame is acceptable at this map size
            // (single texture, no per-cell GameObjects/allocations). A dirty-region repaint
            // is a follow-up optimization once map size or cell-change frequency grows.
            RepaintAll();
        }
    }
}
```

- [ ] **Step 2: Write `PawnView`**

```csharp
using UnityEngine;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Presentation
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class PawnView : MonoBehaviour
    {
        private SimulationRoot _root;
        private int _pawnId;
        private SpriteRenderer _spriteRenderer;

        public void Initialize(SimulationRoot root, int pawnId, Color color)
        {
            _root = root;
            _pawnId = pawnId;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = PlaceholderBlobFactory.CreateCircleSprite(color);
        }

        private void Update()
        {
            Pawn pawn = _root.PawnManager.GetPawn(_pawnId);
            if (pawn == null)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = new Vector3(pawn.PositionX + 0.5f, pawn.PositionY + 0.5f, -1f);
        }
    }
}
```

- [ ] **Step 3: Write `ResourceItemView`**

```csharp
using UnityEngine;

namespace ColonySim.Presentation
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class ResourceItemView : MonoBehaviour
    {
        private SimulationRoot _root;
        private int _itemId;
        private SpriteRenderer _spriteRenderer;

        public void Initialize(SimulationRoot root, int itemId, float x, float y, Color color)
        {
            _root = root;
            _itemId = itemId;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = PlaceholderBlobFactory.CreateSquareSprite(color);
            transform.position = new Vector3(x + 0.5f, y + 0.5f, -0.5f);
        }

        private void Update()
        {
            if (!_root.HasResourceItem(_itemId))
                Destroy(gameObject);
        }
    }
}
```

- [ ] **Step 4: Add the shared placeholder-sprite factory**

```csharp
using UnityEngine;

namespace ColonySim.Presentation
{
    public static class PlaceholderBlobFactory
    {
        private const int Size = 32;

        public static Sprite CreateCircleSprite(Color color)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Vector2 center = new Vector2(Size / 2f, Size / 2f);
            float radius = Size / 2f - 1f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool inside = Vector2.Distance(new Vector2(x, y), center) <= radius;
                texture.SetPixel(x, y, inside ? color : Color.clear);
            }
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), pixelsPerUnit: Size);
        }

        public static Sprite CreateSquareSprite(Color color)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                texture.SetPixel(x, y, color);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), pixelsPerUnit: Size);
        }
    }
}
```

Save as `Assets/Scripts/Presentation/PlaceholderBlobFactory.cs`.

- [ ] **Step 5: Add resource-item tracking and spawn/view wiring to `SimulationRoot`**

Add to `SimulationRoot`:

```csharp
private readonly System.Collections.Generic.Dictionary<int, ColonySim.Simulation.Resources.ResourceItem> _resourceItems
    = new System.Collections.Generic.Dictionary<int, ColonySim.Simulation.Resources.ResourceItem>();

public bool HasResourceItem(int id) => _resourceItems.ContainsKey(id);

public System.Action<ColonySim.Simulation.Resources.ResourceItem> OnResourceItemSpawned;
```

In `Update()`, after `_tickManager.Tick(...)`:

```csharp
foreach (var item in _tickManager.SpawnedItemsThisTick)
{
    _resourceItems[item.Id] = item;
    OnResourceItemSpawned?.Invoke(item);
}
```

- [ ] **Step 6: (Unity Editor step) Wire GameObjects in the scene**

Invoke `unity:unity-cli` to, in `Assets/Scenes/SampleScene.unity`:
- Add a `GridView` GameObject (with `SpriteRenderer`) as a child of `SimulationRoot`, with `root` wired to the `SimulationRoot` component.
- Add a small startup script hook (or extend `SimulationRoot.Awake`, after pawn spawning) that instantiates a `PawnView` prefab per spawned pawn and calls `Initialize(this, pawnId, colonistTemplate.PlaceholderColor)`, and subscribes `OnResourceItemSpawned` to instantiate a `ResourceItemView` per spawned item.
- Confirm in Play mode that: the grid renders with distinct floor/rock/tree colors, and 3 pawn blobs appear near the map center.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Presentation/GridView.cs Assets/Scripts/Presentation/PawnView.cs Assets/Scripts/Presentation/ResourceItemView.cs Assets/Scripts/Presentation/PlaceholderBlobFactory.cs Assets/Scripts/Presentation/SimulationRoot.cs Assets/Scenes/SampleScene.unity
git commit -m "Add placeholder-blob GridView/PawnView/ResourceItemView rendering"
```

---

### Task 13: `DesignationInputController` and `CameraController`

**Files:**
- Create: `Assets/Scripts/Presentation/DesignationInputController.cs`
- Create: `Assets/Scripts/Presentation/CameraController.cs`

**Interfaces:**
- Consumes: `SimulationRoot.TryDesignateMine`/`TryDesignateChop`/`Grid` (Task 11), existing `Assets/InputSystem_Actions.inputactions` asset (project default).
- Produces: `class DesignationInputController : MonoBehaviour` (reads mouse position via the New Input System, converts to a cell index, calls the matching `TryDesignate*` method); `class CameraController : MonoBehaviour` (WASD/arrow pan + scroll zoom on the main camera, no simulation coupling).

- [ ] **Step 1: Write `DesignationInputController`**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColonySim.Presentation
{
    public class DesignationInputController : MonoBehaviour
    {
        public SimulationRoot root;
        public Camera targetCamera;

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);
            if (x < 0 || x >= root.Grid.Width || y < 0 || y >= root.Grid.Height) return;

            int cellIndex = root.Grid.IndexOf(x, y);
            if (!root.TryDesignateMine(cellIndex))
                root.TryDesignateChop(cellIndex);
        }
    }
}
```

`TryDesignateMine` returning `false` for a walkable (non-rock) cell is what lets this fall through to `TryDesignateChop` for tree cells — both are cheap boolean checks against the target cell, no wasted work.

- [ ] **Step 2: Write `CameraController`**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColonySim.Presentation
{
    public class CameraController : MonoBehaviour
    {
        public float panSpeed = 10f;
        public float zoomSpeed = 5f;
        public float minOrthoSize = 3f;
        public float maxOrthoSize = 20f;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Update()
        {
            Vector2 moveInput = Vector2.zero;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveInput.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveInput.y -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
            }

            transform.position += (Vector3)(moveInput * panSpeed * Time.deltaTime);

            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _camera.orthographicSize = Mathf.Clamp(
                        _camera.orthographicSize - scroll * zoomSpeed * Time.deltaTime,
                        minOrthoSize, maxOrthoSize);
                }
            }
        }
    }
}
```

- [ ] **Step 3: (Unity Editor step) Wire the scene and manually verify the full loop**

Invoke `unity:unity-cli` to, in `Assets/Scenes/SampleScene.unity`, attach `DesignationInputController` to the Main Camera (wiring `root`/`targetCamera`) and attach `CameraController` to the Main Camera. Then enter Play mode and manually verify:
- Right-clicking a rock-colored cell causes a pawn to walk over and, after a short delay, the cell turns floor-colored and a stone-colored square blob appears.
- Right-clicking the tree cell causes a pawn to walk over and, after a short delay, the tree disappears and a wood-colored square blob appears.
- Right-clicking the same rock cell twice while the job is still open does not spawn a second job (only one pawn ever walks to it).
- With all 3 pawns busy, designating a 4th job leaves it open until a pawn frees up (no crash, no duplicate claim).
- WASD pans the camera and scroll wheel zooms.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Presentation/DesignationInputController.cs Assets/Scripts/Presentation/CameraController.cs Assets/Scenes/SampleScene.unity
git commit -m "Add designation input and camera controls; wire up playable loop"
```

---

### Task 14: Full-suite regression pass and playtest checklist

**Files:**
- None created; verification only.

- [ ] **Step 1: (Unity Editor step) Run the complete EditMode suite**

Invoke `unity:unity-cli` to run every test under `Assets/Tests/EditMode`.
Expected: PASS, 0 failures (cumulative count from Tasks 2-10, ~31 tests).

- [ ] **Step 2: (Unity Editor step) Manual playtest checklist in Play mode**

Invoke `unity:unity-cli` to enter Play mode in `SampleScene` and confirm each Review Focus item live:
- Duplicate designation on an already-open job's cell: no second job created (`JobBoard.OpenJobCount` unchanged — can be checked via a temporary Debug.Log or the Editor's Debugger).
- Two pawns arriving idle in the same tick with one open job: only one pawn claims it, the other stays idle or seeks the next-nearest job.
- Mining a rock tile that a second, faster pawn already finished (contrived by rapid re-designation): no double resource item, no exception in the Console.
- A pawn's job target at the map edge/corner: designate and confirm it's reachable and completes normally.
- Remove all jobs and let all pawns sit idle for several seconds: no Console errors, no performance drop.

- [ ] **Step 3: Fix any issues found, re-run Steps 1-2, then final commit**

If manual playtest surfaces a bug, fix it in the relevant Task's file, re-run the affected EditMode tests, then:

```bash
git add -A
git commit -m "Fix playtest issues found in full-suite regression pass"
```

If no issues are found, no commit is needed for this task — it is verification-only.

---

## Self-Review Notes

- **Spec coverage:** Simulation/Presentation split (Tasks 2-11), flattened grid + struct `Cell` (Task 2), `Pawn`/`Job` as classes (Tasks 7, 8), ScriptableObject data-driven defs (Task 5), designation → job → pathfind → work → resource flow (Tasks 8-11), connectivity-filtered job matching (Task 8), id-lookup Presentation views (Task 12), EditMode test coverage for Simulation + manual Play-mode verification for Presentation (Tasks 2-10 tests; Task 12/13/14 manual) — all present.
- **Placeholder scan:** no TBD/TODO left in final code (Task 11's inline comment explicitly disclaims being a stub); every step shows real code, not descriptions.
- **Type consistency checked:** `WorldGrid.IndexOf/GetCell/TryGetCoordsOf/SetTerrain/SetHasTree/ResolveMineJob/ResolveTreeJob`, `Cell.NorthMask/EastMask/SouthMask/WestMask/ConnectivityId/NeighborWalkableMask`, `PawnManager.SpawnPawn/GetPawn/RemovePawn/Tick`, `Pawn.Id/PositionX/PositionY/State/CurrentJobId/CurrentPath/PathIndex`, `JobBoard.TryAddJob/TryClaimJobFor/ReleaseJob/CompleteJob/OpenJobCount`, `Job.Id/Type/TargetCellIndex/ClaimedByPawnId/WorkTicksRemaining`, `GridAStar.TryFindPath`, `TickManager.Tick/SpawnedItemsThisTick/CurrentTick` — used identically across every task that consumes them.
- **Review Focus coverage:** duplicate designation → `JobBoardTests.TryAddJob_DuplicateTarget_IsRejected` (Task 8); same-tick race → `JobBoardTests.TryClaimJobFor_SecondCallSameTick_DoesNotClaimSameJobTwice` (Task 8); target invalidated mid-execution → `WorldGridTests.ResolveMineJob_AlreadyResolvedTarget_ReturnsNull` (Task 9) + `TickManagerTests.Tick_PawnWorkingJobWhoseTargetAlreadyResolved_CancelsGracefullyWithoutDuplicateItem` (Task 10); grid boundary correctness → `WorldGridTests.NeighborMask_CornerCell_OffGridNeighborsAreNotWalkable` (Task 3); empty-state safety → `PawnManagerTests.Tick_ZeroPawns_DoesNotThrow` (Task 7) + `JobBoardTests.TryClaimJobFor_NoOpenJobs_ReturnsNull` (Task 8) + `TickManagerTests.Tick_ZeroPawnsZeroJobs_DoesNotThrow` (Task 10).
