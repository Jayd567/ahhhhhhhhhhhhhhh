# ECS/DOTS Migration — Design

## Intent

`ROADMAP.md` Phase 0 requires the simulation layer to use Unity ECS
(DOTS), the C# Job System, and Burst instead of hand-rolled
data-oriented plain C#. The current simulation (`WorldGrid`/`Cell`,
`Pawn`/`PawnManager`, `Job`/`JobBoard`, `GridAStar`, `TickManager`,
`SimplexNoise`/`WorldGenerator`) already hits the *goals* Phase 0
describes (flattened arrays, zero per-tick allocation, centralized
ticking) by hand, but not the *mechanism* the design specifies. The user
chose to adopt ECS/DOTS now rather than defer, to avoid a second
migration once Phase 18–21 (spatial hashing, priority task queues,
jobified matching) build on top of this foundation.

Scope: migrate everything in one pass — grid storage, pawn
movement/state, job board, pathfinding, ticking, and world generation.
Presentation (`GridView`, `PawnView`, `SimulationRoot`,
`DesignationInputController`, `CameraController`) is not simulation
logic and keeps its current responsibilities, but switches from reading
plain C# objects to querying ECS directly (no adapter/copy layer).

Pathfinding is now in scope for this pass too (originally deferred):
`GridAStar`'s per-pawn Manhattan-distance A* is replaced outright with
Phase 20's flow-field pathfinding, including the Tile Cost Array (base
terrain costs) and a dynamic-cost layer (fire/gas/blood) that later
phases write into — built now, populated later.

Success criteria: the existing bare-essentials gameplay loop (right-click
designates mine/chop work, pawns path to it, work it, produce a resource
item; water blocks mining/pathing; world regenerates deterministically
from a seed) behaves identically to a player, verified by rewritten
EditMode tests plus a manual Play-mode check, running on ECS/Jobs/Burst
instead of the current plain-C# classes.

## Packages

- `com.unity.entities` — ECS runtime (World, EntityManager, SystemGroup,
  ISystem, IJobEntity).
- `com.unity.burst` — `[BurstCompile]` for jobs and ISystem OnUpdate.
- `com.unity.collections` — NativeArray/NativeList/DynamicBuffer-adjacent
  native containers used inside jobs (scratch storage for pathfinding).
- `com.unity.entities.graphics` is **not** added — pawn/prop rendering
  stays GameObject-based (`PawnView`'s modular sprite layers, `GridView`'s
  Tilemap) since the art pipeline is 2D sprite-layered, not suited to
  ECS's entities-graphics mesh/material rendering. Presentation reads ECS
  state; it does not render via ECS.
- No subscene/baking workflow is introduced. Nothing today is authored
  as a prefab/subscene; all entities are created procedurally at runtime
  from `WorldGenerationSettingsSO` data, read once at startup and copied
  into Burst-compatible structs/blobs (Burst code cannot dereference
  managed `ScriptableObject` references).

## Data model

### Grid

- One singleton entity (`GridSingleton` tag) holds:
  - `GridDimensions` (`IComponentData`: `int Width, Height`).
  - `GridRevision` (`IComponentData`: `int Value`) — replaces
    `WorldGrid.Revision`; incremented whenever cell data mutates, read by
    Presentation to know when to repaint.
  - `DynamicBuffer<CellElement>` — replaces `Cell[]`. One `CellData`
    struct per flattened index: `ushort TerrainId`, `bool Walkable`,
    `bool HasRock`, `ushort MovementCost`.
- A second singleton (`RegionSingleton` tag) holds
  `DynamicBuffer<int>` region ids per cell index (replaces the
  connectivity flood-fill result currently cached on `WorldGrid`), plus
  scratch buffers reused across regenerations to stay allocation-free.
- A third singleton (`TileCostSingleton` tag) holds two parallel
  `DynamicBuffer<ushort>`s per cell index: `BaseCost` (terrain-derived —
  water/blocked cells use a sentinel "impassable" value, walkable
  terrain uses `TerrainDefBlob`-sourced cost) and `DynamicCost`
  (initialized to zero; reserved for fire/toxic-gas/blood-pool systems
  added in later roadmap phases — nothing writes to it yet in this
  pass). Pathfinding always reads `BaseCost + DynamicCost`.

### Flow fields

- `FlowFieldDestination` (`IComponentData: int TargetCellIndex`) tags an
  entity representing one generated flow field, one per distinct
  destination cell currently needed by at least one pawn (mirrors the
  roadmap's "one Flow Field per destination, all pawns heading there use
  it" — at current colony scale that's usually one field per active job,
  since jobs rarely share a target cell, but the structure supports many
  pawns sharing one field when they do, e.g. a future stockpile/rally
  point).
- `DynamicBuffer<IntegrationCost>` (`ushort` per cell, `ushort.MaxValue`
  = unreached) and `DynamicBuffer<FlowDirection>` (one of 8 directions,
  or "none" at the destination/unreached cells) live on the same entity.
- `FlowFieldGeneration` (`IComponentData: uint GridRevisionGeneratedAt`)
  — the field is stale and must be regenerated when this no longer
  matches `GridRevision`, or when `DynamicCost` changes (tracked the
  same way once a later phase starts writing to it).

### Pawns

Each pawn is an entity:

- `LocalTransform` (from `com.unity.entities`) — position (replaces
  `Pawn.Position`).
- `MoveSpeedModifier` (`IComponentData: float Value`).
- `TickOffset` (`IComponentData: int Value`) — replaces the
  `pawn.id % N == tick % N` stagger check that today lives in
  `TickManager`'s dispatch loop; each system's query filters on it
  directly.
- `CurrentJob` (`IComponentData: Entity Value`, `Entity.Null` when idle)
  — replaces `Pawn.CurrentJobId`.
- `AssignedFlowField` (`IComponentData: Entity Value`, `Entity.Null`
  when idle) — the `FlowFieldDestination` entity this pawn currently
  follows; replaces `Pawn.Path` (`List<Cell>`) entirely. A moving pawn
  has no per-pawn path buffer at all — it reads
  `FlowDirection[currentCellIndex]` off the shared field each movement
  tick, per Phase 20.3 ("path cost calculated once per destination
  rather than once per pawn").
- Enableable tag components for state instead of an enum switch:
  `IsMoving`, `IsWorking`. A pawn with neither tag enabled is idle and
  eligible for job assignment.

### Jobs

Each job is an entity:

- `JobData` (`IComponentData`: `JobType Type`, `int TargetCellIndex`,
  `float RemainingWork`).
- `Unclaimed` (enableable tag) — toggled off on claim instead of removal
  from `JobBoard`'s list; toggled back on if a pawn aborts (mirrors the
  original design's Phase 19.1 abort-and-requeue behavior, which the
  current `TickManager` already partially supports).

### Def blobs

- `TerrainDefBlob` / `TreeDefBlob`: `BlobAssetReference<T>` built once at
  `SimulationRoot` startup from `TerrainDefSO`/`TreeDefSO` instances
  (walkability, yield, work duration, movement cost). Jobs and Burst
  systems read the blob by reference instead of touching
  `ScriptableObject`s. `WorldGenerationSettingsSO` (noise controls, prop
  sprites/densities) is copied into a plain Burst-compatible struct the
  same way `WorldGenerator` already does today via
  `CreateSettings`/`WorldGenerationSettings` — that conversion step is
  kept, just retargeted to produce blob-friendly data.

## World generation

`SimplexNoise` becomes a Burst-compatible static struct/job-callable type
(it is already close to pure math; the change is `[BurstCompile]` plus
removing any remaining managed-state touches). Generation becomes a
short job chain, run once at `SimulationRoot` startup and from the
Editor preview button (`WorldGeneratorEditor`) via
`Schedule().Complete()` rather than every frame:

1. `IJobParallelFor` over 65,536 cells computing octave-combined noise
   into a `NativeArray<float>` heightmap.
2. A normalization + terrain-threshold pass (parallel or single job;
   determined during implementation based on whether normalization needs
   the global min/max first — same two-pass structure `WorldGenerator`
   already uses).
3. A deterministic prop-placement pass (same random-stream approach as
   today, ported into Burst-compatible `Unity.Mathematics.Random`).
4. A final job that writes heightmap/terrain/prop results into the
   `GridSingleton`'s `CellElement` buffer and spawns prop/job entities,
   then selects the spawn region using the existing largest-connected-
   region logic (ported onto the `RegionSingleton` buffers).

## Pathfinding — Flow Fields

`GridAStar` and its per-pawn Manhattan-distance search are removed
entirely, replaced by Phase 20's flow-field approach: path cost is
calculated once per destination, not once per pawn.

- `FlowFieldGenerationSystem` — for each destination cell that has at
  least one pawn assigned or requesting it but no up-to-date
  `FlowFieldDestination` entity (per `GridRevisionGeneratedAt`),
  schedules one Burst job per missing/stale field. Multiple destinations
  needed in the same tick run as an `IJobParallelFor` over the
  destination list; each individual field's integration sweep
  (Dijkstra-style outward fill from the destination over
  `BaseCost + DynamicCost`) is inherently sequential/wavefront-shaped,
  so parallelism is across destinations, not within one field's sweep.
  Each job instance owns scratch `NativeArray`s sized to
  `Width * Height`, released at job completion.
- The existing region-connectivity pre-check (reject unreachable
  destinations before doing any expensive work) stays as a cheap
  early-out read from `RegionSingleton` before a field is generated:
  a pawn in an unreachable region never triggers or waits on generation
  for a destination it can't reach.
- Direction encoding: 8-way `FlowDirection` per cell, computed from the
  lowest-integration-cost walkable neighbor once the integration sweep
  completes; destination and unreached cells store a "none" sentinel.
- Field caching/reuse: a `FlowFieldDestination` entity is shared by every
  pawn currently assigned to that destination cell (`AssignedFlowField`
  points at it); it is regenerated only when `GridRevision` advances past
  `GridRevisionGeneratedAt` (terrain/rock mutated) or the dynamic-cost
  layer changes (no writer yet in this pass, so in practice today that
  means: terrain mutation only), and is despawned once no pawn is
  assigned to it and no job currently targets it.

## Job matching and ticking

`TickManager`'s single switch-statement state machine is replaced by a
`SimulationTickGroup` (`ComponentSystemGroup`) containing small
`ISystem`s, updated at the existing fixed tick interval:

1. `JobAssignmentSystem` — `IJobEntity` over idle pawns (no `IsMoving`/
   `IsWorking`), matches against `Unclaimed` job entities ranked by
   flow-field integration cost (the true terrain-aware travel cost from
   `FlowFieldGenerationSystem`'s output, not straight-line distance) —
   this is a direct forerunner of Phase 18's Efficiency Score (cost ×
   speed × skill, once skills exist), and already more accurate than the
   Manhattan-distance ranking it replaces since it respects walls/water/
   terrain cost instead of straight-line distance. Because ranking needs
   a field per *candidate* job, not just the eventually-chosen one, this
   system requests fields for all of a pawn's nearby unclaimed jobs
   (bounded by the pawn's connectivity region) before comparing; small
   colony-scale job counts keep this cheap. Writes `CurrentJob` and
   `AssignedFlowField`, disables `Unclaimed`.
2. `MovementSystem` — each moving pawn reads `FlowDirection` at its
   current cell off its `AssignedFlowField` buffer and steps that
   direction; enables/disables `IsMoving`. No per-pawn path buffer to
   maintain or invalidate.
3. `WorkExecutionSystem` — decrements `JobData.RemainingWork` for pawns
   with `IsWorking`; on completion, mutates the `CellElement` buffer
   (clear rock/tree), bumps `GridRevision`, spawns a resource entity.
4. `ResourceSpawnSystem` — turns completed-job resource entities into
   whatever Presentation needs to visualize (mirrors
   `SimulationRoot`/`ResourceItemView`'s current LateUpdate hand-off,
   kept as a queue Presentation drains, not a copy-adapter of simulation
   state).

Per-pawn stagger (today: `pawn.id % 5 == CurrentTick % 5` in
`TickManager`) becomes each system's `EntityQuery` filtering on
`TickOffset` matching the current tick, rather than one manual dispatch
loop.

## Presentation boundary

`GridView`, `PawnView`, `SimulationRoot`, `DesignationInputController`
become `MonoBehaviour`s that build an `EntityQuery` once (`Awake`/
`OnEnable`) against `World.DefaultGameObjectInjectionWorld.EntityManager`
and read via `EntityManager.GetComponentData`/`GetBuffer` — on the
existing per-frame cadence for pawn view sync, and on `GridRevision`
change for `GridView`'s repaint, matching today's revision-gated repaint
behavior. No adapter/copy-to-plain-struct layer is introduced, per the
approved design.

`DesignationInputController`'s right-click handling creates job entities
directly (`EntityManager.CreateEntity` + `SetComponentData`) instead of
calling `JobBoard.AddJob`.

## Testing

Each EditMode test converts to the standard DOTS test pattern: create a
`World`, add the system(s) under test to a temporary `SystemGroup`,
populate entities/singletons via `EntityManager`, call
`world.Update()` (or the specific system's `Update(ref state)` via
`SystemHandle`), assert via `EntityManager.GetComponentData`/
`GetBuffer`. Zero-allocation assertions (currently
`Assert.That(GC.GetTotalMemory...)`-style checks around `TickManager.Tick`)
become the equivalent wrapped around `World.Update()` for the tick
group. Tests are rewritten incrementally, one PR-sized chunk per system,
per the user's approved test strategy — old and new tests may briefly
coexist within a chunk but the chunk isn't done until its plain-C# class
and test are deleted.

## Rollout

- Work happens in a git worktree off `master` (existing project
  convention per `docs/superpowers/plans/2026-09-23-*`), not directly on
  `master`.
- "Everything at once" per the user's answer: this is not a long-lived
  side-by-side period. Each implementation-plan task replaces one
  plain-C# class with its ECS equivalent and deletes the old
  class + its old test in the same chunk, so the tree never carries two
  parallel implementations of the same responsibility for long.
- `README.md`/`ARCHITECTURE.md` are updated at the end of the migration
  to describe the new ECS architecture, per the project's
  documentation-maintenance rule (`CLAUDE.md`/`AGENTS.md`).

## Out of scope

- Phase 18's spatial hashing grid and priority broadcast queue — job
  lookup stays a direct query over job entities (fine at colony scale);
  `Unclaimed` jobs aren't yet bucketed spatially or ordered by priority
  tier (medical > firefighting > hauling), since only two job types
  (mine/chop) exist. Flow-field integration cost (this spec) replaces
  the distance term of the Efficiency Score early, but the skill
  multiplier and priority ordering are deferred to Phase 18 proper.
- Phase 19's full HTN (multiple methods/fallback plans) — kept as
  today's single fixed claim → path → work sequence, just ECS-shaped.
- Dynamic tile costs actually being written (fire, toxic gas, blood
  pools) — the `DynamicCost` buffer and staleness tracking exist and are
  read by flow-field generation, but nothing populates them until the
  phases that add fire/disease/blood systems.
- Any Phase 1–17/22–39 gameplay system (social, psychological, medical,
  disease, historical). This spec is Phase 0 (+ Phase 20 flow fields,
  pulled forward per the user's request) only.
