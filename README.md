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

- Added `ROADMAP.md`: maps the full 39-phase design roadmap (performance
  foundation through Chronicle/history systems) to current status, and
  records the bare-essentials colony sim milestone as the current state.
  No code changed. Status: complete.

- World generation milestone: 39/39 EditMode tests pass, including zero-allocation mining/chopping cycles; runtime map and CC0 sprites visually verified. README.md usage/source paths and ARCHITECTURE.md dependency/dataflow map updated.

- Generation tests confirmed zero managed allocation across mining/chopping ticks; corrected `WorldGenerator.cs` normalization endpoint rounding (maximum now exactly 1). Verified: 39/39 EditMode tests pass.

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
