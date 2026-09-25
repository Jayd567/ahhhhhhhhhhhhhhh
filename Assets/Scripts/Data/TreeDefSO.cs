using UnityEngine;
using ColonySim.Simulation.Resources;

namespace ColonySim.Data
{
    [CreateAssetMenu(fileName = "TreeDef", menuName = "ColonySim/Tree Def")]
    public class TreeDefSO : ScriptableObject
    {
        public ResourceType YieldResourceType;
        public int YieldAmount = 10;
        public int WorkDurationTicks = 60;
        public Color PlaceholderColor = new Color(0.2f, 0.5f, 0.2f);
    }
}
