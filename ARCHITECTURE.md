# Architecture

## System dependency map

Simulation (Unity ECS: `com.unity.entities`/`burst`/`collections`, plus a
few remaining plain-C# leftovers not yet migrated) <- Data (ScriptableObjects,
partly ECS-blob-aware) <- Presentation (Unity MonoBehaviours).
WorldGeneration.Editor references those three assemblies and is Editor-only.
SimulationTests references Simulation and Data and runs without a scene, using
the project's standard ECS test pattern (`World.CreateSystem<T>()`/
`handle.Update(world.Unmanaged)` against a scratch `World`).

**Status:** Tasks 1-11 of `docs/superpowers/plans/2026-09-24-ecs-dots-migration.md`
are implemented (grid/region/tile-cost singletons, Burst world generation,
pawn/job entities, flow-field pathfinding, job assignment, movement, work
execution, and `SimulationRoot`'s ECS bootstrap). Tasks 12-14 (rewriting
`GridView`, `PawnView`, `DesignationInputController` onto the new
`SimulationRoot.World`/`GridEntity` surface) are **not** done yet — those
three files still read the removed `SimulationRoot.Grid`/`.PawnManager`
properties, so the project does not compile end-to-end until they land.
`WorldGrid`/`Cell` (plain C#) are deliberately still present because
`GridView` is their last consumer, per the migration plan's explicit
deferral of their deletion to Task 12.

## Class relationships

### Simulation (ECS)

- `GridBootstrap.CreateGrid` creates the `GridSingleton` entity: `GridDimensions`,
  `GridRevision`, and a `DynamicBuffer<CellElement>` (`CellData`: `TerrainId`/
  `Walkable`/`HasRock`) sized to `width * height`.
- `ConnectivitySystem` (`[UpdateInGroup(typeof(SimulationTickGroup))]`) recomputes
  a `RegionSingleton` entity's `DynamicBuffer<RegionElement>` (flood fill over
  walkable cells) whenever `GridRevision` has advanced past its own tracked
  `RegionComputedAtRevision`.
- `TileCostSingleton`'s `BaseCostElement`/`DynamicCostElement` buffers hold
  terrain-derived and (currently unwritten, reserved for future fire/gas/blood
  phases) dynamic per-cell movement costs; `TileCost.Impassable` is the
  blocked sentinel. `TerrainDefBlobBuilder` builds a `BlobAssetReference<TerrainDefBlob>`
  from `TerrainDefSO[]` and populates `BaseCostElement` from it.
- `EcsWorldGenerator.Generate` schedules `GenerateHeightmapJob`/`TerrainAndPropsJob`
  (`[BurstCompile] IJob`, via the blittable `GenerationParams` copied from the
  managed `WorldGenerationSettings`), writes the result into `CellElement`,
  spawns one `TreeTag`/`TreeCellIndex` entity per rolled tree cell, runs
  `ConnectivitySystem` once, and returns the largest-region spawn cell index.
  `EcsSimplexNoise` is the Burst-compatible noise port (unchanged math).
- Each pawn is an entity (`PawnFactory.CreatePawn`): `PawnId`, `LocalTransform`,
  `MoveSpeedModifier`, `TickOffset`, `CurrentJob`/`AssignedFlowField` (`Entity`,
  `Entity.Null` when idle/unassigned), and enableable `IsMoving`/`IsWorking` tags.
- Each job is an entity (`JobFactory.TryCreateJob`): `JobData` (`Type`/
  `TargetCellIndex`/`RemainingWork`) plus an enableable `Unclaimed` tag; creation
  rejects an unwalkable-and-not-rock target or a target cell any existing job
  (claimed or not) already occupies.
- `FlowFieldService.GetOrCreateField` looks up or (re)generates, per destination
  cell, a `FlowFieldDestination` entity holding `IntegrationCostElement`/
  `FlowDirectionElement` buffers — a Dijkstra sweep (`IntegrationSweepJob`, ported
  from the deleted `GridAStar`'s binary-heap structure) over `BaseCost + DynamicCost`,
  regenerated only when the field's `FlowFieldGeneration.GridRevisionGeneratedAt`
  is behind the grid's current `GridRevision`. Fields are shared by every pawn
  currently heading to that destination.
- `JobAssignmentSystem` matches each idle pawn (`IsMoving`/`IsWorking` both
  disabled, `CurrentJob == Entity.Null`) against `Unclaimed` jobs reachable from
  the pawn's own `RegionElement` (a rock target counts as reachable if any of its
  walkable neighbors shares the pawn's region), ranking candidates by
  `IntegrationCostElement` at the pawn's cell rather than straight-line distance.
- `MovementSystem` advances an `IsMoving` pawn one cell per tick by reading
  `FlowDirectionElement` at its current cell off its `AssignedFlowField`; on
  reaching the destination (`-1` direction) it disables `IsMoving`, enables
  `IsWorking`, and sets `JobData.RemainingWork` from the `WorkDurationConfig`
  singleton.
- `WorkExecutionSystem` decrements `RemainingWork` for staggered (`TickOffset`
  vs. `SimulationTick.Value % StaggerBucketCount`) `IsWorking` pawns; on
  completion it mutates `CellElement` (clears the mined rock and sets floor
  terrain, or destroys the chopped cell's `TreeTag` entity), bumps
  `GridRevision`, creates a `ResourceItemData`+`PendingResourceSpawn` entity,
  destroys the job, and returns the pawn to idle — falling through to idle
  without a duplicate spawn if the target was already resolved out from under
  the pawn.
- `SimulationTickGroup` (`[UpdateInGroup(typeof(SimulationSystemGroup))]`)
  increments the `SimulationTick` singleton once per update and orders its
  members `ConnectivitySystem -> JobAssignmentSystem -> MovementSystem ->
  WorkExecutionSystem` via `[UpdateAfter]`.

### Simulation (plain C#, not yet migrated)

- `WorldGrid`/`Cell` — flattened-array grid still used by `GridView` only;
  scheduled for deletion once `GridView` is rewritten onto `CellElement` (Task 12).
- `JobType.cs`/`ResourceType.cs` — plain Burst-compatible enums, reused unchanged
  by both the ECS and remaining plain-C# code.

### Data

- `WorldGenerationSettingsSO` owns serialized generation controls, four
  `TerrainDefSO` references, and CC0 tree/rock sprites with visual sizes.
  `CreateEcsSettings()` (renamed from `CreateSettings()` in Task 11) copies
  values into a fresh `WorldGenerationSettings` (now defined in
  `EcsWorldGenerator.cs`); water is blocked, all other ground is walkable.
- `TerrainDefSO` owns terrain IDs, walkability, colors, yield data and mining
  duration. `TreeDefSO` owns tree yields/duration. `Rock.asset` supplies mining
  rewards; `Stone.asset` represents walkable stone ground.

### Presentation (partially broken pending Tasks 12-14)

- `SimulationRoot` (rewritten, Task 11) bootstraps the ECS `World` in `Awake`:
  creates the grid entity, runs `EcsWorldGenerator.Generate`, builds/populates
  the terrain-cost blob, creates the `SimulationTick`/`WorkDurationConfig`
  singletons, spawns starting pawn entities + `PawnView`s in the spawn region,
  and drives `SimulationTickGroup.Update()` on a fixed accumulator in `Update`.
  `LateUpdate` drains `PendingResourceSpawn` entities into `OnResourceItemSpawned`
  and syncs pawn views. `TryDesignateMine`/`TryDesignateChop` now call
  `JobFactory.TryCreateJob` directly. Exposes `World`/`GridEntity` in place of
  the removed `Grid`/`PawnManager`/`JobBoard` properties.
- `GridView`, `PawnView`, `DesignationInputController` **still reference the
  removed `SimulationRoot.Grid`/`.PawnManager` surface** (`WorldGrid`, `Pawn`)
  and do not compile against the rewritten `SimulationRoot` — this is Tasks
  12-14 of the migration plan, not yet done. `WorldGeneratorEditor` and
  `CameraController`/`ResourceItemView` are unaffected (they don't touch the
  removed surface).

## Data flow

1. `SimulationRoot.Awake` -> `GridBootstrap.CreateGrid` -> `EcsWorldGenerator.Generate`
   (Burst heightmap/terrain/prop jobs, tree entities, one `ConnectivitySystem` run,
   largest-region spawn selection) -> `TerrainDefBlobBuilder.PopulateBaseCosts`.
2. `SimulationRoot.Update` drives `SimulationTickGroup.Update()` at a fixed
   `ticksPerSecond` interval; the group increments `SimulationTick` and runs
   `ConnectivitySystem -> JobAssignmentSystem -> MovementSystem -> WorkExecutionSystem`
   in order every tick.
3. Right-click -> `DesignationInputController` (currently non-compiling, Task 14)
   -> `SimulationRoot.TryDesignateMine`/`TryDesignateChop` -> `JobFactory.TryCreateJob`.
4. `JobAssignmentSystem` ranks unclaimed jobs per idle pawn by flow-field
   integration cost (generating/reusing fields via `FlowFieldService` per
   candidate); `MovementSystem` steps assigned pawns along their field;
   `WorkExecutionSystem` mutates the grid and spawns a resource entity on completion.
5. `SimulationRoot.LateUpdate` drains `PendingResourceSpawn` entities into
   `OnResourceItemSpawned` and syncs pawn views — outside the simulation tick
   hot path, same as before the migration.
6. `GridView` (not yet migrated) still expects to read `WorldGrid.Revision` from
   `SimulationRoot.Grid`, which no longer exists — this link is broken until Task 12.

## Art provenance and import

- Assets/Art/Characters/DoubtItt contains original CC0 Modular Test Dummy parts, Instructions and license. DefaultColonist.asset defines nine layers and three palettes.
- Assets/Art/Props contains original Kenney Top-down Shooter PNGs (tile_183 tree, tile_239 rock), LICENSE.txt and SOURCE.txt. https://kenney.nl/assets/top-down-shooter
- Both use 256 PPU, trilinear filtering and mipmaps. Props are uncompressed Single sprites; WorldGenerationSettingsSO compensates for source dimensions with visual size controls.

## Validation and limits

No live Unity Editor was available in this session (or reachable from this
cloud session generally) to run EditMode tests or confirm actual Burst
compilation. Tasks 1-6 were previously verified locally (41/41 EditMode tests
passing per the committed `test-results.xml`) before being pushed without
Tasks 4-6's required file deletions, which this pass corrected. Tasks 8-11's
new code was checked only by static means: duplicate-symbol grep across
`Assets/Scripts`, asmdef reference/package check, and a cross-file grep for
lingering references to deleted types (confirmed clean outside the
known-pending `GridView.cs`/`PawnView.cs`/`DesignationInputController.cs`).
**The project is not currently known to compile in the Unity Editor** — Tasks
12-14 (Presentation rewrite) are required first, and even once it compiles,
someone with Editor access should run the EditMode suite before trusting any
of this beyond static review.

Known carried-over limitations (from the pre-migration plain-C# implementation,
not yet revisited): tick staggering affects movement/work as well as job
search; dynamic population growth after startup has no explicit capacity-prep
step; resource visuals still allocate in `LateUpdate`; pawn removal doesn't
release claimed jobs; the `DynamicCostElement` buffer exists and is read by
flow-field generation but nothing writes to it yet (reserved for future
fire/gas/blood phases).
