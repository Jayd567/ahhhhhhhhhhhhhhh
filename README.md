# Rimworld-esc

A bare-essentials RimWorld-style colony sim, built in Unity 2D URP.

Colonists move around a grid map and perform simple work jobs (mining rock,
chopping trees) that the player designates by right-clicking; idle pawns
automatically claim the nearest reachable job, path to it, work it, and
produce a resource item. This is the foundation milestone described in
`docs/superpowers/specs/2026-09-23-bare-essentials-colony-sim-design.md`.

## Project layout

- `Assets/Scripts/Simulation/` — plain C# simulation layer (grid, pawns,
  jobs, pathing, ticking). No MonoBehaviour dependency; covered by EditMode
  tests under `Assets/Tests/EditMode/`.
- `Assets/Scripts/Data/` — ScriptableObject data definitions
  (`TerrainDefSO`, `PawnTemplateSO`, `TreeDefSO`).
- `Assets/Scripts/Presentation/` — MonoBehaviours that render simulation
  state and read player input (`SimulationRoot`, `GridView`, `PawnView`,
  `ResourceItemView`, `DesignationInputController`, `CameraController`).
- `Assets/Data/` — the terrain/tree/pawn ScriptableObject asset instances.
- `docs/superpowers/specs/` and `docs/superpowers/plans/` — the design spec
  and implementation plan this milestone was built from.

## Running it

Open the project in Unity 6000.3.18f1 (or later), open
`Assets/Scenes/SampleScene.unity`, and press Play. Right-click a rock or
tree cell to designate work for the colonists.

## World generation

Select the SimulationRoot object in SampleScene and click **Generate World** in its Inspector to preview without Play. Edit **Assets/Data/WorldGeneration.asset** for the seed, noise scale, octaves, terrain thresholds, tree/rock densities and prop sprites/sizes. Play regenerates the same 256×256 world and spawns three colonists near its centre on the largest connected land region. Right-click props to work them; water cannot be mined. WASD/arrows pan; scroll zooms up to a full-map view.

Full implementation: Assets/Scripts/Simulation/Generation/SimplexNoise.cs, WorldGenerator.cs, and Assets/Scripts/Simulation/Grid/WorldGrid.cs. Editor button: Assets/Scripts/Editor/WorldGeneratorEditor.cs. Settings bridge: Assets/Scripts/Data/WorldGenerationSettingsSO.cs. Prop originals/license: Assets/Art/Props/.

## Testing

EditMode tests run via the Unity Test Runner (Window > General > Test
Runner > EditMode), or headlessly via `unity test --mode EditMode`.

## Changelog

- ECS migration Task 3: added `TileCostComponents.cs` (`TileCostSingleton`, `TileCost.Impassable`, `BaseCostElement`/`DynamicCostElement` buffers) under `Assets/Scripts/Simulation/Grid/`, and `TerrainDefBlob.cs` (`TerrainDefBlob`/`TerrainDefEntry` blob asset, `TerrainDefBlobBuilder.Build`/`PopulateBaseCosts`) under `Assets/Scripts/Data/`; `Data.asmdef` now references `Unity.Entities`/`Unity.Collections` with `allowUnsafeCode: true`. Reordered `PopulateBaseCosts` to perform all archetype-changing `AddBuffer`/`AddComponent` calls before fetching `DynamicBuffer` handles, avoiding an invalidated-safety-handle runtime error. Added `Assets/Tests/EditMode/TerrainDefBlobTests.cs`. Verified: 43/43 EditMode tests pass. Status: complete.

- ECS migration Task 4: ported procedural world generation to Burst-compiled jobs and deleted the plain-C# generator. Added `Assets/Scripts/Simulation/Generation/EcsSimplexNoise.cs` (Burst-compatible simplex noise, unchanged math, permutation table in a fixed-size buffer), `WorldGenerationJobs.cs` (`GenerateHeightmapJob`, `TerrainAndPropsJob` as `[BurstCompile] IJob`s writing directly into `CellElement`/`CellData`; introduced a blittable `GenerationParams` struct copied from `WorldGenerationSettings` because job structs cannot hold the managed `WorldGenerationSettings` class), and `EcsWorldGenerator.cs` (`WorldGenerationSettings` moved verbatim from the deleted `WorldGenerator.cs`; `static int EcsWorldGenerator.Generate(EntityManager, Entity, WorldGenerationSettings)` schedules the jobs, writes the buffer, spawns `TreeTag`/`TreeCellIndex` entities via the same deterministic LCG stream/seed offset as the rock roll, runs `ConnectivitySystem`, and returns the spawn cell index). Deleted `Assets/Scripts/Simulation/Generation/SimplexNoise.cs`, `WorldGenerator.cs`, and `Assets/Tests/EditMode/WorldGeneratorTests.cs`; added `Assets/Tests/EditMode/EcsWorldGeneratorTests.cs` (determinism, water-never-walkable, spawn-cell-walkable). Patched the only other reference to the deleted types, `Assets/Scripts/Presentation/SimulationRoot.cs.GenerateWorld()`, to throw `NotSupportedException` pending its own ECS migration in Task 11 (no test exercises this MonoBehaviour path). Verified: 41/41 EditMode tests pass (43 baseline − 5 removed `WorldGeneratorTests` cases + 3 new `EcsWorldGeneratorTests`). Status: complete.

- ECS migration Task 5: added pawn entities and deleted the plain-C# `Pawn`/`PawnManager` classes. Added `Assets/Scripts/Simulation/Pawns/PawnComponents.cs` (`PawnId`, `MoveSpeedModifier`, `TickOffset`, `CurrentJob`, `AssignedFlowField` `IComponentData`; `IsMoving`/`IsWorking` enableable tag components) and `PawnFactory.cs` (`static Entity PawnFactory.CreatePawn(EntityManager, int id, float2 position, int tickOffset)`, creating a pawn entity with `LocalTransform` plus the above, idle/unassigned by default). Deleted `Assets/Scripts/Simulation/Pawns/Pawn.cs`, `PawnManager.cs`, and `Assets/Tests/EditMode/PawnManagerTests.cs`; added `Assets/Tests/EditMode/PawnFactoryTests.cs`. **Expected interim non-compiling state:** this task deliberately leaves the project uncompilable for the full EditMode suite — `Assets/Scripts/Simulation/Ticking/TickManager.cs` and `Assets/Scripts/Simulation/Jobs/JobBoard.cs` (and transitively the Presentation assembly, including `SimulationRoot.cs`/`PawnView.cs`) still reference the deleted `Pawn`/`PawnManager` types and will not compile until Tasks 6-11 migrate them. This is intentional per the migration plan; do not be alarmed by compile errors referencing those files until later tasks land. New files (`PawnComponents.cs`, `PawnFactory.cs`, `PawnFactoryTests.cs`) confirmed free of compile errors by inspection of the Editor compiler log (only `JobBoard.cs`/`TickManager.cs` CS0246 errors present). Status: complete (full-suite test verification deferred to Task 11).

- ECS migration Task 6: added job entities/creation and shrank `JobBoard.cs` to claim-only. Added `Assets/Scripts/Simulation/Jobs/JobComponents.cs` (`JobData` `IComponentData` with `Type`/`TargetCellIndex`/`RemainingWork`; `Unclaimed` enableable tag) and `JobFactory.cs` (`static bool JobFactory.TryCreateJob(EntityManager, Entity grid, JobType, int targetCellIndex, out Entity job)`, porting `JobBoard.TryAddJob`'s two validations — target must be walkable-or-rock, and no existing job, claimed or not, may already target that cell). Deleted `Assets/Scripts/Simulation/Jobs/Job.cs` (fully superseded by `JobData`). Added `Assets/Tests/EditMode/JobFactoryTests.cs` (mineable-cell success, plain-wall-not-mineable failure, duplicate-target rejection). Patched `Assets/Scripts/Simulation/Jobs/JobBoard.cs`: deleted `TryAddJob` and its dedupe fields (fully superseded by `JobFactory.TryCreateJob`); its three remaining methods (`TryClaimJobFor`, `ReleaseJob`, `CompleteJob`) now use a small interim `public class LegacyJob` nested inside `JobBoard` (same fields the old `Job` class held) in place of the deleted `Job` type — a deliberately throwaway shape since `JobBoard.cs` itself is deleted outright in Task 8. **Expected interim non-compiling state (continued from Task 5):** `JobBoard.cs`'s `TryClaimJobFor(WorldGrid, Pawn pawn)` signature still references the `Pawn` class deleted in Task 5, so it still carries one pre-existing CS0246 (`Pawn` not found) unrelated to this task's changes and not fixable without doing Task 8's job-matching migration; `Assets/Scripts/Simulation/Ticking/TickManager.cs` (and transitively `SimulationRoot.cs`/`PawnView.cs`) remain uncompilable for the same Task 5 reasons plus now also referencing the deleted `Job` type directly. New files (`JobComponents.cs`, `JobFactory.cs`, `JobFactoryTests.cs`) confirmed free of compile errors by inspection of the Editor compiler log; confirmed no errors anywhere in `Grid/`, `Data/`, `Generation/`, or `Pawns/`. Status: complete (full-suite test verification deferred to Task 8+, when `JobBoard.cs`/`TickManager.cs` are replaced).

- Added `ROADMAP.md`: maps the full 39-phase design roadmap (performance
  foundation through Chronicle/history systems) to current status, and
  records the bare-essentials colony sim milestone as the current state.
  No code changed. Status: complete.

- World generation milestone: 39/39 EditMode tests pass, including zero-allocation mining/chopping cycles; runtime map and CC0 sprites visually verified. README.md usage/source paths and ARCHITECTURE.md dependency/dataflow map updated.

- Generation tests confirmed zero managed allocation across mining/chopping ticks; corrected `WorldGenerator.cs` normalization endpoint rounding (maximum now exactly 1). Verified: 39/39 EditMode tests pass.

- ECS migration Task 2: added `RegionComponents.cs` (`RegionSingleton`, `RegionComputedAtRevision`, `RegionElement` buffer) and `ConnectivitySystem.cs` (an `ISystem` that flood-fills `GridSingleton`'s `CellElement` buffer into per-cell region ids, recomputing only when `GridRevision.Value` advances past the tracked `RegionComputedAtRevision`) under `Assets/Scripts/Simulation/Grid/`, plus `Assets/Tests/EditMode/ConnectivitySystemTests.cs`. Verified: 42/42 EditMode tests pass. Status: complete.

- Corrected `SimulationRoot.LateUpdate` to dispatch queued resource view events after simulation ticks; generation scripts compile cleanly in Unity 6000.3.18f1.

- Added Editor-only `WorldGeneratorEditor.cs` and assembly with Generate World Inspector preview; added `WorldGeneratorTests.cs` for determinism, normalization, terrain/props, water protection, connectivity and allocation-free harvesting. Verified.

- Added `WorldGenerationSettingsSO.cs` (terrain definitions, noise controls, prop sprites/scales), `TerrainDefSO.WorkDurationTicks`; replaced `GridAStar.cs` with reusable indexed heap and region rejection, prepared paths/yields in `PawnManager.cs`/`TickManager.cs`, blocked water jobs in `JobBoard.cs`. Verified.

- WorldGrid.cs: bulk generation, reusable connectivity queue, allocation-free neighbors, preallocated work yields, rock/water distinction and render revision tracking implemented; verified.

- Completed: added pure C# `Simulation/Generation/SimplexNoise.cs` and `WorldGenerator.cs`, seeded 256×256 heightmap/terrain/prop passes; extended `Cell.cs` with separate rock state. Grid and editor integration complete.

- **2026-09-24** — Fixed a Critical bug found in final code review: `TickManager`
  could send a pawn to path toward a Mine job's neighbor cell in the wrong
  connectivity region (when the target rock separates two disconnected
  areas), causing the job to fail to path and retry forever. Fixed by
  selecting the path goal from the pawn's own connectivity region.
  Files: `Assets/Scripts/Simulation/Ticking/TickManager.cs`,
  `Assets/Tests/EditMode/TickManagerTests.cs`. Status: fixed, verified by
  a new regression test (`Tick_MineJobOnWallBetweenTwoRegions_...`).
- **2026-09-24** — Fixed a per-tick allocation in `TickManager.Tick`: the
  pawn-tick callback was a closure allocated on every call, even for empty
  ticks, violating the "no allocation in the per-tick hot path" rule.
  Fixed by caching a single delegate and storing the current tick's
  job configs in fields instead of capturing them in a lambda.
  File: `Assets/Scripts/Simulation/Ticking/TickManager.cs`. Status: fixed.
- **2026-09-24** — Strengthened a vacuous test: the "target invalidated
  mid-execution" regression test resolved the job's target on its own
  before the invalidation step ran, so it never actually exercised
  mid-execution invalidation. Rewritten to assert the pawn is in the
  `Working` state (with its full work countdown ahead) before invalidating.
  File: `Assets/Tests/EditMode/TickManagerTests.cs`. Status: fixed.
- **2026-09-23** — Initial bare-essentials milestone: flattened-grid
  `WorldGrid`/`Cell`, `PawnManager`/`Pawn`, `JobBoard`/`Job`, `GridAStar`
  pathfinder, `TickManager` job-execution state machine, data-driven
  ScriptableObject defs, and the Presentation layer (`SimulationRoot`,
  `GridView`/`PawnView`/`ResourceItemView`, `DesignationInputController`,
  `CameraController`) wired into `SampleScene`. Files: see
  `docs/superpowers/plans/2026-09-23-bare-essentials-colony-sim.md` for the
  full task-by-task file list. Status: complete, 34/34 EditMode tests
  passing, manual Play-mode loop verified.

- **2026-09-24** — Completed local merge to master and removed the temporary bare-essentials-colony-sim worktree and merged branch. Files: README.md, ARCHITECTURE.md (cleanup record); gameplay files unchanged. Status: complete.

- **2026-09-24** — Imported DoubtItt's CC0 Modular Test Dummy PNG parts and creator instructions/license under Assets/Art/Characters/DoubtItt. Character presentation integrated.

- **2026-09-24** — PawnTemplateSO.cs defines sprite layers/palettes; PawnView.cs builds modular characters with ID lookups; SimulationRoot.cs batches view synchronization and gives starting pawns separate walkable cells. Status: clean compile; three nine-layer colonists verified in Play mode.

- **2026-09-24** — Configured DefaultColonist.asset with nine DoubtItt sprite layers and three palettes; PNG import metadata uses smooth filtering, mipmaps, and no compression. Status: verified at close and overview zoom.


- Added original Kenney CC0 Tree.png/Rock.png and license/source records in Assets/Art/Props; import configuration complete.

- SimulationRoot.cs now generates settings-driven worlds, spawns on the largest land region, rejects water mining and defers resource view creation to LateUpdate; GridView.cs caches ground texture and renders imported props in a transient Tilemap, repainting only on grid revision.

- Configured WorldGeneration.asset plus Water/Dirt/Grass/Stone.asset, SampleScene generation/camera references, and Props PNG import metadata through Unity; 256 PPU, trilinear/mipmaps, uncompressed sprites.
- Final verification: generated rock/tree harvested in Play with two resource views; custom WorldGeneratorEditor renders 65,536 cells outside Play. Removed trailing blank lines from SimulationRoot.cs, JobBoard.cs, PawnManager.cs and TickManager.cs. Status: complete.

- **2026-09-24** — Task 1 of the ECS/DOTS migration (Phase 0, see `ROADMAP.md` and `docs/superpowers/plans/2026-09-24-ecs-dots-migration.md`): added `com.unity.entities` 1.3.14, `com.unity.burst` 1.8.19, and `com.unity.collections` 2.5.1 to `Packages/manifest.json`; enabled `allowUnsafeCode` and added ECS assembly references on `Assets/Scripts/Simulation/Simulation.asmdef`, and referenced them from `Assets/Tests/EditMode/SimulationTests.asmdef`; added the first ECS grid singleton — `GridSingleton`, `GridDimensions`, `GridRevision`, `CellData`, `CellElement` in `Assets/Scripts/Simulation/Grid/GridComponents.cs`, and `GridBootstrap.CreateGrid` in `Assets/Scripts/Simulation/Grid/GridBootstrap.cs`. This is step one of a larger, multi-task migration of the simulation layer to ECS — more tasks follow. Status: complete, 40/40 EditMode tests passing (39 pre-existing + new `GridBootstrapTests`).
