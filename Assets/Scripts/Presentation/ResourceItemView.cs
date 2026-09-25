using UnityEngine;

namespace ColonySim.Presentation
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class ResourceItemView : MonoBehaviour
    {
        private SimulationRoot _root;
        private int _itemId;
        private SpriteRenderer _spriteRenderer;

        public void Initialize(SimulationRoot root, int itemId, float x, float y, Color color)
        {
            _root = root;
            _itemId = itemId;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = PlaceholderBlobFactory.CreateSquareSprite(color);
            transform.position = new Vector3(x + 0.5f, y + 0.5f, -0.5f);
        }

        private void Update()
        {
            if (!_root.HasResourceItem(_itemId))
                Destroy(gameObject);
        }
    }
}
