using UnityEngine;
using ColonySim.Simulation.Generation;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "WorldGeneration", menuName = "ColonySim/World Generation")]
    public class WorldGenerationSettingsSO : ScriptableObject
    {
        public WorldGenerationSettings Settings = new WorldGenerationSettings();
        public TerrainDefSO Water, Dirt, Grass, Stone;
        public Sprite TreeSprite, RockSprite;
        [Min(0.1f)] public float TreeSize = 1.4f, RockSize = 0.85f;

        public WorldGenerationSettings CreateSettings()
        {
            if (!Water || !Dirt || !Grass || !Stone)
                throw new System.InvalidOperationException("Assign all four terrain definitions.");
            if (Water.IsWalkable || !Dirt.IsWalkable || !Grass.IsWalkable || !Stone.IsWalkable)
                throw new System.InvalidOperationException("Water must be blocked; Dirt, Grass and Stone must be walkable ground.");
            // Copy: generating a preview never mutates the settings asset.
            var s = Settings;
            return new WorldGenerationSettings {
                Seed = s.Seed, NoiseScale = s.NoiseScale, Octaves = s.Octaves,
                Persistence = s.Persistence, Lacunarity = s.Lacunarity,
                WaterThreshold = s.WaterThreshold, DirtThreshold = s.DirtThreshold, StoneThreshold = s.StoneThreshold,
                TreeDensity = s.TreeDensity, StoneRockDensity = s.StoneRockDensity, DirtRockDensity = s.DirtRockDensity,
                WaterId = Water.TerrainTypeId, DirtId = Dirt.TerrainTypeId, GrassId = Grass.TerrainTypeId, StoneId = Stone.TerrainTypeId
            };
        }
        public Color GroundColor(int terrainId)
        {
            if (terrainId == Water.TerrainTypeId) return Water.PlaceholderColor;
            if (terrainId == Grass.TerrainTypeId) return Grass.PlaceholderColor;
            if (terrainId == Stone.TerrainTypeId) return Stone.PlaceholderColor;
            return Dirt.PlaceholderColor;
        }
    }
}
