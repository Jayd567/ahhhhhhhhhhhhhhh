# Roadmap

Full design target: a hyper-realistic colony sim (psychological, medical,
social, historical, biological simulation) that stays fun and legible
through UI/dialogue/behavior rather than exposing raw numbers. Source
design notes: the phase list below is the complete plan; see git history /
`docs/superpowers/` for milestone-level specs and plans.

Status legend: `DONE`, `PARTIAL`, `NOT STARTED`.

## PHASE 0 — Performance Foundation — PARTIAL

- Flattened `Cell[]` grid (`WorldGrid`), no `Cell[,]`. DONE.
- Allocation-free hot paths: `TickManager` uses a cached delegate,
  preallocated path/result storage; 39/39 EditMode tests include
  zero-managed-allocation assertions across mining/chopping cycles. DONE.
- Staggered/batched ticking via `TickManager`/`PawnManager` (centralized,
  no per-cell/per-pawn `Update()`). DONE in spirit, but not yet the
  `pawn.id % N == tick % N` tick-offset system described in the design —
  current staggering is simpler. PARTIAL.
- Unity ECS (DOTS), C# Job System, Burst Compiler: **not adopted**. The
  simulation layer is plain C# (classes/structs, `List`/array-backed
  managers), not `IComponentData`/`IJobEntity`/`[BurstCompile]`. NOT
  STARTED — this is the biggest architectural gap vs. the original design
  and needs a deliberate decision (adopt DOTS now vs. keep hand-rolled
  data-oriented C# and revisit later; current scale of 3 pawns / one map
  doesn't yet demand it).

## PHASE 1 — Core Pawn Data Model — PARTIAL

- `Pawn` (position, path, job state) and `PawnTemplateSO` (modular sprite
  layers/palettes) exist. DONE for movement/identity/appearance.
- Physical condition, psychological condition, personality, disease,
  immune, blood, tissue/bone injury, sensory, metabolic, sleep/circadian,
  social-group, historical-identity state: NOT STARTED.

## PHASE 2 — Social Event Architecture — NOT STARTED
## PHASE 3 — 3D Relationship Vectors — NOT STARTED
## PHASE 4 — Social Tick / Relationship Maintenance — NOT STARTED
## PHASE 5 — Perception, Gossip, Information — NOT STARTED
## PHASE 6 — Mental Break Framework — NOT STARTED
## PHASE 7 — Psychological UI Legibility — NOT STARTED
## PHASE 8 — Environmental Homeostasis — NOT STARTED
## PHASE 9 — Full Medical Anatomy (bones/tissue layers) — NOT STARTED
## PHASE 10 — Blood and Vascular System — NOT STARTED
## PHASE 11 — Blood Pools / Environmental Contamination — NOT STARTED
## PHASE 12 — Healing, Scars, Chronic Pain, Adaptation — NOT STARTED
## PHASE 13 — Immune System and Infection — NOT STARTED
## PHASE 14 — Advanced Disease Genetics — NOT STARTED
## PHASE 15 — Innate and Adaptive Immunity — NOT STARTED
## PHASE 16 — Viral Shedding and Environmental Survival — NOT STARTED
## PHASE 17 — Group Sociology — NOT STARTED

## PHASE 18 — Global Task Management — PARTIAL

- Central task board (`JobBoard`) prevents duplicate claims; ranks
  reachable work by Manhattan distance and region reachability. DONE at a
  basic level (two job types: mine, chop).
- Spatial hashing grid (128x128 buckets / `NativeArray`): NOT STARTED —
  `JobBoard` currently scans/ranks directly rather than bucketing by
  location; fine at current pawn/job counts.
- Priority broadcast queue (`NativeMinHeap`/`NativeQueue`, medical >
  firefighting > hauling): NOT STARTED — no task priority tiers yet, no
  medical/firefighting job types.
- Efficiency Score (`distance x speed modifier x skill`): NOT STARTED —
  current ranking is distance-only, no skill system yet.

## PHASE 19 — Individual Pawn HTN — NOT STARTED

- `TickManager` runs a fixed claim -> path -> work state machine per job
  type, not a general Hierarchical Task Network with multiple
  methods/fallbacks. Pawn-side abort-and-return-to-queue on incapacity
  (19.1) doesn't apply yet since there's no health/injury model to fail
  against.

## PHASE 20 — 2D Flow-Field Pathfinding — NOT STARTED (using A* instead)

- `GridAStar`: indexed binary heap, prepared cost/parent/position arrays,
  region-connectivity pre-check before searching, allocation-free after
  warm-up. DONE as a *per-pawn A\** solution — this is a deliberate
  divergence from the flow-field design, reasonable while few pawns share
  destinations; revisit if colonist count or shared-destination pathing
  (sieges, mass hauling) grows.
- Tile cost array: `WorldGrid`'s flattened `Cell[]` carries walkability
  and rock-blocker state, no yet graded movement cost (rough
  terrain/fire/toxic gas/blood) beyond blocked/unblocked. PARTIAL.

## PHASE 21 — Jobified ECS Implementation — NOT STARTED (depends on Phase 0 DOTS adoption)

## PHASE 22–37 — The Chronicle (procedural history, dynasties, civil wars,
ruins, historical disease, technology, trade, artifacts, battlefields,
environmental damage, legibility, rumor mill, price justification) — NOT STARTED

## PHASE 38 — System Integration — NOT STARTED (no cross-system links yet; nothing to integrate)

## PHASE 39 — Player-Facing Fun and Legibility Pass — NOT STARTED

---

## Where the project actually is right now

Shipped ("bare-essentials colony sim" milestone, 2026-09-23/24):

- 256x256 seeded world generation (simplex noise heightmap, terrain
  thresholds, tree/rock prop placement, largest-connected-region spawn
  selection).
- Flattened-array `WorldGrid`/`Cell` simulation grid with region
  connectivity and revision tracking.
- `PawnManager`/`Pawn`, `JobBoard`/`Job`, `GridAStar`, `TickManager`
  driving a right-click-to-designate mine/chop work loop for 3 colonists.
- Data-driven `ScriptableObject` defs (`TerrainDefSO`, `TreeDefSO`,
  `PawnTemplateSO`, `WorldGenerationSettingsSO`).
- Presentation layer (`SimulationRoot`, `GridView`, `PawnView`,
  `ResourceItemView`, `DesignationInputController`, `CameraController`)
  fully separated from the plain-C# simulation layer.
- Modular nine-layer colonist rendering (DoubtItt CC0 art) with three
  palettes; Kenney CC0 tree/rock props.
- 39/39 EditMode tests passing, including zero-allocation assertions on
  the hot tick path.

Known limitations noted in `ARCHITECTURE.md`: no incremental/dirty-region
mining flood-fill yet, tick staggering affects job search too, no capacity
handling for pawns added after `TickManager` construction, resource
visuals still allocate in `LateUpdate`, no job-release-on-pawn-removal.

Practical next steps toward the full design (suggested, not yet decided):

1. Decide the Phase 0 DOTS question (ECS/Jobs/Burst vs. continued
   hand-rolled data-oriented C#) before Phase 1 grows much further — it's
   much cheaper to decide now than to migrate later.
2. Extend Phase 1 pawn data (health/needs) enough to unlock Phase 18/19
   work (skills for Efficiency Score, HTN fallback plans) without yet
   requiring the full medical/psych stack.
3. Phases 2–17 (social/psych) and 9–16 (medical/disease) are independent
   of each other and of the Chronicle (22–37); either branch can be
   picked up next depending on what's more fun to prototype first.

_This file tracks status only; update it alongside `README.md`'s
Changelog and `ARCHITECTURE.md` whenever a phase's status changes._
