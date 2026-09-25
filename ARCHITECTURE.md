# Architecture

## System dependency map

Simulation (Unity ECS: `com.unity.entities`/`burst`/`collections`) <- Data
(ScriptableObjects, partly ECS-blob-aware) <- Presentation (Unity
MonoBehaviours querying the ECS `World` directly, no adapter layer).
WorldGeneration.Editor references those three assemblies and is Editor-only.
SimulationTests references Simulation and Data and runs without a scene, using
the project's standard ECS test pattern (`World.CreateSystem<T>()`/
`handle.Update(world.Unmanaged)` against a scratch `World`).

**Status:** All 14 code tasks of `docs/superpowers/plans/2026-09-24-ecs-dots-migration.md`
are implemented — grid/region/tile-cost singletons, Burst world generation,
pawn/job entities, flow-field pathfinding, job assignment, movement, work
execution, `SimulationRoot`'s ECS bootstrap, and the `GridView`/`PawnView`/
`DesignationInputController` Presentation rewrite. The plain-C# `WorldGrid`/
`Cell`/`Pawn`/`PawnManager`/`Job`/`JobBoard`/`GridAStar`/`IPathfinder`/
`TickManager` classes this replaced are all deleted; a project-wide grep
confirms no remaining references. **This has not been confirmed against an
actual Unity Editor compile or EditMode test run** — no live Editor was
reachable from the sessions that did this work, so correctness rests on
static review (duplicate-symbol and dangling-reference greps, asmdef
reference checks) rather than the Editor/Burst compiler or test runner
actually agreeing. Treat it as unverified until someone with Editor access
runs the EditMode suite and checks Play mode.

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

### Simulation (plain C#, intentionally kept)

- `JobType.cs`/`ResourceType.cs` — plain Burst-compatible enums, reused unchanged
  by the ECS code (per the migration plan's Global Constraints — not replaced).

### Data

- `WorldGenerationSettingsSO` owns serialized generation controls, four
  `TerrainDefSO` references, and CC0 tree/rock sprites with visual sizes.
  `CreateEcsSettings()` (renamed from `CreateSettings()` in Task 11) copies
  values into a fresh `WorldGenerationSettings` (now defined in
  `EcsWorldGenerator.cs`); water is blocked, all other ground is walkable.
- `TerrainDefSO` owns terrain IDs, walkability, colors, yield data and mining
  duration. `TreeDefSO` owns tree yields/duration. `Rock.asset` supplies mining
  rewards; `Stone.asset` represents walkable stone ground.

### Presentation

- `SimulationRoot` bootstraps the ECS `World` in `Awake`: creates the grid
  entity, runs `EcsWorldGenerator.Generate`, builds/populates the terrain-cost
  blob, creates the `SimulationTick`/`WorkDurationConfig` singletons, spawns
  starting pawn entities + `PawnView`s in the spawn region, and drives
  `SimulationTickGroup.Update()` on a fixed accumulator in `Update`.
  `LateUpdate` drains `PendingResourceSpawn` entities into `OnResourceItemSpawned`
  and syncs pawn views. `TryDesignateMine`/`TryDesignateChop` call
  `JobFactory.TryCreateJob` directly. Exposes `World`/`GridEntity` in place of
  the old `Grid`/`PawnManager`/`JobBoard` properties.
- `GridView` queries `GridDimensions`/`GridRevision`/`CellElement` and
  `TreeTag`/`TreeCellIndex` off `root.World`/`root.GridEntity` each frame
  (repainting its texture/tilemap only when `GridRevision` changes, same as
  before the migration).
- `PawnView` holds only a stable `PawnId` and an `EntityQuery`; each sync it
  looks up its pawn's current `LocalTransform` by matching `PawnId` in that
  query against `root.World`, never holding an `Entity` handle directly.
  `DesignationInputController` reads `GridDimensions` off `root.World`/
  `root.GridEntity` to convert a click to a cell index. `WorldGeneratorEditor`,
  `CameraController`, and `ResourceItemView` are unchanged by the migration.

## Data flow

1. `SimulationRoot.Awake` -> `GridBootstrap.CreateGrid` -> `EcsWorldGenerator.Generate`
   (Burst heightmap/terrain/prop jobs, tree entities, one `ConnectivitySystem` run,
   largest-region spawn selection) -> `TerrainDefBlobBuilder.PopulateBaseCosts`.
2. `SimulationRoot.Update` drives `SimulationTickGroup.Update()` at a fixed
   `ticksPerSecond` interval; the group increments `SimulationTick` and runs
   `ConnectivitySystem -> JobAssignmentSystem -> MovementSystem -> WorkExecutionSystem`
   in order every tick.
3. Right-click -> `DesignationInputController` -> `SimulationRoot.TryDesignateMine`/
   `TryDesignateChop` -> `JobFactory.TryCreateJob`.
4. `JobAssignmentSystem` ranks unclaimed jobs per idle pawn by flow-field
   integration cost (generating/reusing fields via `FlowFieldService` per
   candidate); `MovementSystem` steps assigned pawns along their field;
   `WorkExecutionSystem` mutates the grid and spawns a resource entity on completion.
5. `SimulationRoot.LateUpdate` drains `PendingResourceSpawn` entities into
   `OnResourceItemSpawned` and syncs pawn views — outside the simulation tick
   hot path, same as before the migration.
6. `GridView.Update` compares its cached revision against `GridRevision` on
   `root.GridEntity` each frame and repaints only when it has advanced.

## Art provenance and import

- Assets/Art/Characters/DoubtItt contains original CC0 Modular Test Dummy parts, Instructions and license. DefaultColonist.asset defines nine layers and three palettes.
- Assets/Art/Props contains original Kenney Top-down Shooter PNGs (tile_183 tree, tile_239 rock), LICENSE.txt and SOURCE.txt. https://kenney.nl/assets/top-down-shooter
- Both use 256 PPU, trilinear filtering and mipmaps. Props are uncompressed Single sprites; WorldGenerationSettingsSO compensates for source dimensions with visual size controls.

## Validation and limits

No live Unity Editor was available in any session that did this migration
work (Tasks 7-14, plus the Tasks 4-6 cleanup) to run EditMode tests or
confirm actual Burst compilation. Tasks 1-6 were previously verified locally
(41/41 EditMode tests passing per the committed `test-results.xml`) before
being pushed without Tasks 4-6's required file deletions, which a later pass
corrected. Everything from that cleanup through Task 14 was checked only by
static means: duplicate-symbol grep across `Assets/Scripts`, asmdef
reference/package checks, and a project-wide grep for lingering references
to every deleted type (`WorldGrid`, `Cell`, `Pawn`, `PawnManager`, `Job`,
`JobBoard`, `GridAStar`, `IPathfinder`, `TickManager`) — all confirmed clean.
**None of this is known to actually compile in the Unity Editor or pass
Burst compilation.** Someone with Editor access must run
`unity test ... --mode EditMode` and check Play mode (per the plan's Task 15
manual-verification checklist: three colonists spawn, mine/chop jobs
complete and mutate the grid, water is rejected, duplicate designations are
rejected) before trusting any of this beyond "the source is internally
consistent by inspection."

Known carried-over limitations (from the pre-migration plain-C# implementation,
not yet revisited): tick staggering affects movement/work as well as job
search; dynamic population growth after startup has no explicit capacity-prep
step; resource visuals still allocate in `LateUpdate`; pawn removal doesn't
release claimed jobs; the `DynamicCostElement` buffer exists and is read by
flow-field generation but nothing writes to it yet (reserved for future
fire/gas/blood phases).
