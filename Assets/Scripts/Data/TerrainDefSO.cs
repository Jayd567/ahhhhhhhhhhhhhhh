using UnityEngine;
using ColonySim.Simulation.Resources;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "TerrainDef", menuName = "ColonySim/Terrain Def")]
    public class TerrainDefSO : ScriptableObject
    {
        public int TerrainTypeId;
        public string DisplayName;
        public bool IsWalkable;
        public bool IsMineable;
        public ResourceType YieldResourceType;
        public int YieldAmount;
        [Min(1)] public int WorkDurationTicks = 60;
        public Color PlaceholderColor = Color.white;
    }
}
