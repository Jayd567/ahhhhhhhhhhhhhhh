# Architecture

## System dependency map

Simulation (plain C#) <- Data (ScriptableObjects) <- Presentation (Unity).
WorldGeneration.Editor references those three assemblies and is Editor-only.
SimulationTests references Simulation and runs without a scene.

## Class relationships

- WorldGenerationSettingsSO owns serialized generation controls, four TerrainDefSO references, and CC0 tree/rock sprites with visual sizes. CreateSettings copies values into pure WorldGenerationSettings; water is blocked and all other ground is walkable.
- SimplexNoise owns a seed-shuffled permutation and samples 2D simplex noise. WorldGenerator combines octaves, normalizes the heightmap to exact 0/1 extrema, thresholds terrain, then uses a separate deterministic random stream to place props. GeneratedWorld contains float[256,256], WorldGrid, and a spawn cell in the largest land region.
- WorldGrid owns flattened Cell[], reusable flood-fill queue, and resource objects prepared when props are placed. SetGeneratedCell batches writes; CompleteGeneration builds neighbor masks and regions once. Cell.HasRock is separate from ground terrain; mining clears the blocker and preserves generated ground. Trees remain walkable and chop jobs target their cell. Water cannot produce mining jobs or yields.
- WorldGrid.SetTerrain is the legacy wall API: blocked cells default to mineable unless isMineable=false. SetGeneratedCell is the explicit generation API. Both mutation paths update Revision after finalizing topology. Neighbor enumeration uses a struct without boxing or iterator allocation.
- GridAStar checks region connectivity before searching. Its indexed binary heap and cost/parent/position arrays are prepared once by TickManager. Standalone callers may prepare or lazily initialize storage. Paths contain start and goal cells.
- PawnManager owns pawns by integer ID and a list for staggered ticking; it reserves each pawn's path storage outside ticking. TickManager owns a cached callback, prepared scratch path, active-job dictionary and per-tick resource result list. JobBoard ranks reachable work by Manhattan distance; blocked targets require a neighboring cell in the pawn's region. ResourceItem instances are transferred from grid placement storage on completion, not allocated in Tick.
- TerrainDefSO owns terrain IDs, walkability, colors, yield data and mining duration. TreeDefSO owns tree yields/duration. Rock.asset supplies mining rewards; Stone.asset represents walkable stone ground.
- SimulationRoot calls GenerateWorld in Awake, spawns colonists in the spawn region, centres the camera and drives fixed simulation ticks. WorldGeneratorEditor provides its Generate World Inspector button, builds matching GridViews and frames Scene view without entering Play.
- GridView owns one ground texture and a prop Tilemap, plus two runtime Tile definitions. It scans/repaints only on grid Revision changes and modifies only changed prop tiles. Generated preview/render objects are DontSave and cleaned up on disable; no per-cell MonoBehaviours exist.
- PawnTemplateSO owns modular sprite layers and palettes; PawnView creates the DoubtItt character layers once and uses SortingGroup. SimulationRoot synchronizes all pawn views in LateUpdate. DesignationInputController handles right-click work; CameraController handles pan/zoom. ResourceItemView still displays simple resource markers.

## Data flow

1. SampleScene -> Assets/Data/WorldGeneration.asset -> pure settings -> seeded heightmap -> terrain pass -> prop pass -> bulk grid masks/connectivity -> spawn selection.
2. Editor button renders transient preview; Play regenerates the same seed and creates pawn/job/tick managers. The scene saves asset references, not generated cells.
3. Right-click -> SimulationRoot designation validation -> JobBoard. Water is rejected; tree and rock work use the existing job types.
4. TickManager staggers claim/path/work by pawn ID. A* rejects disconnected regions; blocked rocks are worked from a reachable neighbor. Completion mutates the grid, increments Revision, and transfers a preallocated ResourceItem to the result list.
5. SimulationRoot.Update records results in pre-sized storage and queues presentation events. LateUpdate drains those events, creates resource visuals, and synchronizes pawn views. Presentation object creation is outside the simulation tick hot path.
6. GridView notices Revision and removes harvested props from its tilemap while retaining underlying ground.

## Art provenance and import

- Assets/Art/Characters/DoubtItt contains original CC0 Modular Test Dummy parts, Instructions and license. DefaultColonist.asset defines nine layers and three palettes.
- Assets/Art/Props contains original Kenney Top-down Shooter PNGs (tile_183 tree, tile_239 rock), LICENSE.txt and SOURCE.txt. https://kenney.nl/assets/top-down-shooter
- Both use 256 PPU, trilinear filtering and mipmaps. Props are uncompressed Single sprites; WorldGenerationSettingsSO compensates for source dimensions with visual size controls.

## Validation and limits

39/39 EditMode tests pass in Unity 6000.3.18f1. New coverage verifies seeded determinism, normalized 256x256 heights, terrain/prop placement, connectivity, water rejection, generated mining/chopping, and zero managed bytes allocated through complete warmed simulation job cycles.

Mining still performs one full-grid allocation-free flood fill, and GridView scans the grid after a revision. These are event-driven, not per-frame full-map work; very high simultaneous mining rates may warrant incremental regions/dirty cells. Tick staggering currently affects movement/work as well as job search. Dynamic population growth after TickManager construction can exceed prepared job/result capacities and needs an explicit preparation step. Resource visuals allocate in LateUpdate. Regeneration is an Editor/startup operation, not a live-world reset; generated preview is deliberately temporary. Pawn removal still does not release claimed jobs.

Live verification additionally confirmed both generated prop types complete through the presentation pipeline and produce two resource views; the custom Inspector preview reports 65,536 cells in Edit mode.
