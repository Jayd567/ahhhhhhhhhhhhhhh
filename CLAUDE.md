# Configuration Rules

## 1. Automated Documentation Maintenance
Immediately after modifying code, creating files, or changing system structures, update these files:
*   **README.md:** Add a concise bullet point under `## Changelog` detailing the exact change, files impacted, and status.
*   **ARCHITECTURE.md:** Update the system dependency map, data flow tracking, and class relationships to reflect the new state.

## 2. Core Architectural Constraints
*   **Decoupled Simulation:** Separate the Simulation Layer (pure C# logic/structs) from the Presentation Layer (Unity MonoBehaviours, GameObjects, Transforms).
*   **Data-Driven Design:** Use Unity ScriptableObjects for static data definitions (item stats, pawn templates, terrain costs). Do not hardcode parameters.
*   **Centralized Execution:** Do not attach individual update loops to map cells or pawns. Use centralized managers (`GridManager`, `PawnManager`) for batched processing.

## 3. Optimization Requirements
*   **Grid Storage:** Represent the grid as a flattened one-dimensional array (`Cell[width * height]`) instead of a nested array (`Cell[,]`) to optimize cache locality.
*   **Memory Allocations:** Use lightweight structs instead of classes for grid cells where possible to eliminate garbage collection (GC) pressure.
*   **Loop Performance:** Prohibit `new`, LINQ queries, string concatenation, and `GetComponent` inside tight simulation loops, `Update()`, or `FixedUpdate()`.
*   **String Hashing:** Use integer hashes (`int`) or Enums for lookups, keys, and IDs instead of strings.
*   **Time-Slicing:** Stagger expensive calculations (pathfinding, behavior trees, room updates) across frames using a tick offset system (e.g., `pawn.id % 5 == CurrentTick % 5`).
*   **Pathfinding Hierarchy:** Implement a high-level region/room connectivity check before executing micro-level A* or flowfield pathfinding.

## 4. Operational Workflow
1. Read `ARCHITECTURE.md` to verify system compatibility before generating new features.
2. Present proposed file additions or structural modifications to the user for approval.
3. Write targeted, optimized C# code.
4. Update `README.md` and `ARCHITECTURE.md` immediately following implementation.
