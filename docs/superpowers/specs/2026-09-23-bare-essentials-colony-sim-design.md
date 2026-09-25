# Bare-Essentials RimWorld-esc Colony Sim — Design Spec

Date: 2026-09-23
Status: Approved for planning

## Purpose

Stand up the smallest playable slice of a RimWorld-style colony sim in this
Unity 2D URP project: pawns move around a grid map and perform simple work
jobs (mining rock, chopping trees) that the player designates, without any
direct pawn control. This is the foundation milestone — needs, combat,
building placement, and raids are explicitly out of scope and will be
separate future specs. The goal is a clean Simulation/Presentation split
that later systems (needs, building, crafting) can be added onto without
rework, per the constraints in `CLAUDE.md`.

## Success Criteria

- A grid-based map exists with at least two terrain types (mineable rock,
  walkable floor) and tree objects.
- The player can right-click a rock tile or tree to designate it as a job.
- Idle pawns automatically claim the nearest *reachable* queued job, path
  to it, work it for a duration, and produce a resource item (stone/wood).
- Multiple pawns (3+) operate concurrently without stepping on each other's
  claimed jobs.
- A pawn never attempts to path to a job that is unreachable (walled off)
  from its current position.
- All simulation state lives in plain C# (no MonoBehaviour dependency);
  Presentation layer only reads simulation state by ID lookup, never holds
  direct object references to pawns/jobs.

## Out of Scope (future specs)

- Needs/mood/health/thoughts
- Hauling and stockpile zones
- Building/construction jobs and blueprints
- Combat, raids, wildlife
- Save/load
- Full region/room hierarchy pathfinding (only a lightweight connectivity
  filter is included here — see Pathfinding & Job Matching below)

## Architecture

### Simulation Layer (pure C#, testable without Unity running)

- `Cell` — **struct**. Per-cell data: terrain type id, walkable flag,
  4-way neighbor walkability bitmask, connectivity id, resource id on
  ground (if any), designation state. Struct is appropriate here: fixed
  shape, high volume, no independent identity — exactly the flattened
  cache-locality case CLAUDE.md calls out.
- `WorldGrid` — owns `Cell[width * height]` (flattened, not `Cell[,]`).
  Responsible for terrain queries, applying designations, mutating a cell
  (e.g. mined rock → floor) and triggering connectivity/neighbor-mask
  recompute for the affected area.
- `Pawn` — **class**. Mutable identity with fluctuating state (position,
  current job id, movement progress) that will grow to include health/
  thoughts/equipment later. Struct semantics would risk value-copy bugs
  once this data is nested in collections and mutated per-tick.
- `PawnManager` — centralized store of all `Pawn` instances keyed by int
  id. Owns the single tick dispatch for all pawns (no per-pawn
  `MonoBehaviour.Update()`). Exposes `GetPawn(int id)` for read-only
  lookup by Presentation.
- `Job` — **class**. Mutable: type, target (cell index or entity id),
  claimed-by-pawn-id, work-progress-ticks. Struct semantics would risk
  value-copy bugs on a job pulled from a list and mutated during
  execution.
- `JobBoard` — holds unclaimed `Job`s. Matches idle pawns to jobs via the
  connectivity-filtered nearest-match described below. Not a strict FIFO
  queue — a priority/nearest scan over currently open jobs.
- `PathfinderService` (`IPathfinder` + `GridAStar`) — flattened-grid A*
  using each `Cell`'s neighbor bitmask for O(1) adjacency checks. Exposed
  behind an interface so a future hierarchical/region-aware implementation
  can swap in without touching callers.
- `TickManager` — fixed-step simulation tick (e.g. 20 Hz) independent of
  frame rate, drives `PawnManager.Tick()` and `JobBoard` matching each
  tick.
- `ResourceItem` — plain data for a dropped resource (type, amount,
  position) spawned when a job completes.

### Presentation Layer (MonoBehaviours)

- `GridView` — single component that renders the whole `WorldGrid` as
  sprites (e.g. via a batched mesh or tilemap-driven approach), refreshed
  only for cells that changed, not per-frame full redraw.
- `PawnView` — one per pawn GameObject. Holds only `int pawnId`. Every
  render frame, calls `PawnManager.Instance.GetPawn(pawnId)` to read
  position/state and update its `Transform` and placeholder-blob sprite.
  Never caches a `Pawn` reference — if the pawn is removed from
  `PawnManager`, the lookup returns null/missing and the view despawns
  itself, avoiding stale-reference leaks.
- `ResourceItemView` — same id-lookup pattern for dropped resource items.
- `DesignationInputController` — reads mouse input, translates a
  right-click on a valid target into a `WorldGrid` designation +
  `JobBoard.Add(job)` call. The only Presentation code that writes into
  Simulation.
- `CameraController` — basic pan/zoom, no simulation coupling.
- `GridManager` / a root `SimulationRoot` MonoBehaviour — owns
  construction and wiring of `WorldGrid`, `PawnManager`, `JobBoard`,
  `TickManager`, `PathfinderService` at scene start, and drives
  `TickManager.Update()` from Unity's `Update()` (the one and only
  MonoBehaviour `Update()` driving simulation time).

### Data-Driven Definitions (ScriptableObjects)

- `TerrainDefSO` — walk cost, mineable flag, yield resource type/amount,
  placeholder color. Instances: Rock, Floor.
- `PawnTemplateSO` — base movement speed, work speed, placeholder blob
  color. Instances: default colonist template.
- `TreeDefSO` — yield resource type/amount, work duration, placeholder
  color.

No gameplay-tunable numeric parameter is hardcoded in a script; each is
read from a def asset assigned in the inspector.

## Data Flow

1. **Designation**: player right-clicks a rock cell or tree object.
   `DesignationInputController` validates target, creates a `Job`
   (type, target), pushes it to `JobBoard`. No pawn is bound yet.
2. **Tick loop** (`TickManager`, fixed-step, independent of frame rate):
   - `PawnManager.Tick()` iterates all pawns. Expensive per-pawn work
     (job search, path (re)calc) is staggered via
     `pawn.Id % StaggerBucketCount == currentTick % StaggerBucketCount`
     rather than every pawn every tick, per CLAUDE.md's time-slicing
     rule.
   - For each idle pawn whose stagger bucket is active this tick,
     `JobBoard` is asked for a job.
3. **Pathfinding & Job Matching** (the corrected design):
   - Each `Cell` carries a `connectivityId` (int), recomputed via
     incremental flood-fill only for the affected region whenever a
     cell's walkability changes (mined open, wall built). This is
     *not* the full room/door hierarchy region system — it is the
     minimum needed to answer "is this job reachable from here at all"
     in O(1) instead of via a full A* probe.
   - `JobBoard.FindJobFor(pawn)` first filters the open-job list to
     jobs whose target cell shares `pawn`'s current `connectivityId`
     (rejects trapped/walled-off jobs immediately, no pathfinding
     spent on them).
   - Among reachable candidates, it ranks by cheap Manhattan-distance
     heuristic (no A* yet) and picks the best.
   - Only the single winning candidate gets a real `PathfinderService`
     A* request. If that path unexpectedly fails (edge case: stale
     connectivity data), the job is skipped this tick and the pawn
     stays idle rather than retrying it in a hot loop.
   - On successful path, pawn claims the job (`Job.ClaimedByPawnId`
     set, removed from open pool) and begins walking.
4. **Job execution**: pawn walks the path (simulation-side position
   update per tick). On arrival, pawn "works" the target for the def's
   configured duration (ticked down each simulation tick). On
   completion: target cell/tree mutates (rock → floor, tree removed),
   triggers neighbor-mask/connectivity recompute for the affected cells,
   and a `ResourceItem` is spawned at that position. Pawn returns to
   idle.
5. **Presentation sync**: `PawnView`/`GridView`/`ResourceItemView` read
   simulation state once per rendered frame via ID lookup and update
   `Transform.position` / sprite state. Presentation never mutates
   Simulation directly except through step 1.

## Testing Approach

- Simulation-layer types (`WorldGrid`, `JobBoard`, `GridAStar`,
  `PawnManager` tick logic) are plain C# with no UnityEngine
  MonoBehaviour dependency, so they are covered with Unity Test
  Framework **EditMode** tests (no scene/play-mode needed):
  - Grid: connectivity id recompute correctness after a wall/rock is
    opened or closed.
  - JobBoard: rejects unreachable jobs, picks nearest reachable job,
    doesn't double-claim.
  - GridAStar: finds a path when one exists, returns failure when
    walled off, uses the neighbor bitmask correctly at map edges.
- Presentation layer (views, input controller) is verified manually in
  the Editor (Play mode): designate a rock/tree, watch a pawn walk over
  and clear it, confirm a resource item appears. No automated PlayMode
  tests for this milestone — out of scope until the loop stabilizes.

## Open Follow-Up (explicitly deferred, not part of this milestone)

- Full region/room graph with doorway nodes (only the lightweight
  connectivity-id flood-fill is built now).
- Hauling resource items to stockpiles.
- Priority-based job types (currently: simple nearest-reachable match,
  no player-set priority list).
