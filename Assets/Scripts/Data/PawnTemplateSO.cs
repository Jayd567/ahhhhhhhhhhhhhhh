using UnityEngine;

namespace ColonySim.Data
{
    public enum PawnTint { Skin, Clothing, Trousers, Neutral }

    [System.Serializable]
    public class PawnSpriteLayer
    {
        public string Name;
        public Sprite Sprite;
        public Vector2 Offset;
        public Vector2 Scale = Vector2.one;
        public PawnTint Tint;
    }

    [System.Serializable]
    public struct PawnPalette
    {
        public Color Skin;
        public Color Clothing;
        public Color Trousers;
    }

    [CreateAssetMenu(fileName = "PawnTemplate", menuName = "ColonySim/Pawn Template")]
    public class PawnTemplateSO : ScriptableObject
    {
        public string DisplayName;
        public float MoveSpeed = 2.5f;
        public float WorkSpeedMultiplier = 1f;
        public Color PlaceholderColor = Color.cyan;
        [Min(0.1f)] public float VisualScale = 1.25f;
        public PawnSpriteLayer[] Layers = System.Array.Empty<PawnSpriteLayer>();
        public PawnPalette[] Palettes = System.Array.Empty<PawnPalette>();
    }
}
