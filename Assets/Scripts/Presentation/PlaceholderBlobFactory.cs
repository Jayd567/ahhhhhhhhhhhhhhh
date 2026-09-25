using UnityEngine;

namespace ColonySim.Presentation
{
    public static class PlaceholderBlobFactory
    {
        private const int Size = 32;

        public static Sprite CreateCircleSprite(Color color)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Vector2 center = new Vector2(Size / 2f, Size / 2f);
            float radius = Size / 2f - 1f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool inside = Vector2.Distance(new Vector2(x, y), center) <= radius;
                texture.SetPixel(x, y, inside ? color : Color.clear);
            }
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), pixelsPerUnit: Size);
        }

        public static Sprite CreateSquareSprite(Color color)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                texture.SetPixel(x, y, color);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), pixelsPerUnit: Size);
        }
    }
}
