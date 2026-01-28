# Project v0.5: "The Loot Loop" & Unified World Architecture

## 1. Executive Summary
This phase transitions the project from a "Walking Simulator" to a functional **Roguelike MMO**. The focus is on the core loop (Explore -> Fight -> Loot), the implementation of a "Site-Based" world structure, and a **Unified Terrain Engine** that powers both the persistent Overworld and the disposable Instances.

## 2. Gameplay Architecture

### A. The "Bump" Interaction Loop
Movement is the primary trigger for all actions. When a player attempts to move into a tile:
1.  **Wall/Obstacle:** Movement Blocked.
2.  **Monster:** **Melee Attack** (Deal damage, trigger combat state).
3.  **Item:** **Pickup** (Instant stat boost, e.g., +1 Attack).
4.  **POI Marker (Red Tent/Icon):** **Enter Instance** (Transition to a sub-map).
5.  **Empty:** Move to tile.

### B. Combat & Magic
*   **Melee:** Handled via Bump interactions.
*   **Ranged/Magic:** Uses **Tab Targeting** (No mouse aiming).
    *   **Input:** Player presses `Tab` to lock onto the nearest enemy (weighted by threat/direction).
    *   **Visuals:** A UI reticle appears over the target.
    *   **Action:** Player presses `Cast`.
    *   **Validation:** Server performs a **Raycast** (Line of Sight check). If a wall blocks the path, the spell fails even if locked on.

### C. The "Site-Based" World
*   **Macro (Overworld):** Abstract scale. Players travel between locations. Enemies appear as "Camp Icons," not individual mobs.
*   **Micro (Instances):** 1:1 Tactical scale.
    *   **Story Dungeons:** Persistent, fixed-seed maps (e.g., "The Crypt").
    *   **Hunting Grounds:** Disposable instances generated when entering a generic terrain tile. These are *not* saved to the DB and vanish when the player leaves.

## 3. The Unified Terrain Engine (The Toolbox)

We do not build separate generators for the Overworld and Instances. We build one **Terrain Engine** that is applied at different scales.

### A. The Pipeline (Used for BOTH Macro & Micro)
1.  **The "God Layer" (Manual Config):**
    *   **Macro:** Checks for Capital Cities.
    *   **Micro:** Checks for Boss Arenas or Quest Objectives.
    *   **Action:** Loads a **Tiled Map (JSON)** prefab to override noise.
2.  **The Base Noise (The Canvas):**
    *   **Macro:** Determines Continent shape and Biomes.
    *   **Micro:** Determines local tree placement and terrain roughness.
    *   **Logic:** `Noise < 0.3` = Water; `Noise < 0.6` = Clear; `Noise >= 0.6` = Obstacle.
3.  **The River Pathfinding (A* Walker):**
    *   **Macro:** Connects Towns to Oceans.
    *   **Micro:** Connects the North edge of the instance to the South edge (if the Overworld tile had a river).
    *   **Logic:** Uses **A* Pathfinding** weighted by terrain density (Water seeks soft ground).
4.  **The Influence Mask:**
    *   **Macro:** Towns "push" the forest back to create suburbs.
    *   **Micro:** Boss Tents "push" the trees back to create a fighting arena.

### B. Fractal Consistency (The "Zoom")
When generating a **Disposable Instance**, the engine inherits parameters from the **Overworld Tile**:
*   If Overworld Tile is **Forest** -> Instance Generator sets `DensityThreshold = High`.
*   If Overworld Tile has **River** -> Instance Generator runs `RiverWalker(North, South)`.
*   **Result:** The tactical map visually matches the world map context perfectly.

## 4. Expansion Strategy (The "Hard Edge")

*   **No Infinite Ocean:** The world is not an island. It is a continent with hard borders.
*   **Bounds:** Defined in Config (`MinX`, `MaxX`).
*   **The Edge:** Players see **Ghost Chunks** (rendered terrain) past the limit but are blocked from moving there.
*   **Expansion:** To release new content, update the Config bounds and generate the new chunks.
