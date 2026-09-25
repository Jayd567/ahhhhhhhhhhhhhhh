using UnityEngine;
using UnityEngine.Tilemaps;
using ColonySim.Simulation.Grid;

namespace ColonySim.Presentation
{
    [ExecuteAlways, RequireComponent(typeof(SpriteRenderer))]
    public class GridView : MonoBehaviour
    {
        public SimulationRoot root;
        private Texture2D _texture;
        private Sprite _groundSprite;
        private SpriteRenderer _renderer;
        private GameObject _props;
        private Tilemap _tilemap;
        private Tile _tree, _rock;
        private byte[] _propStates;
        private WorldGrid _grid;
        private int _revision = -1;
        private Color32[] _pixels;

        private void Start() { if (Application.isPlaying && root != null) Build(); }
        private void OnEnable() { if (!Application.isPlaying && (root == null || root.Grid == null)) Clear(); }
        public void Build()
        {
            Clear();
            if (root == null || root.Grid == null || root.worldSettings == null) return;
            _grid = root.Grid;
            _renderer = GetComponent<SpriteRenderer>();
            _texture = new Texture2D(_grid.Width, _grid.Height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            _groundSprite = Sprite.Create(_texture, new Rect(0, 0, _grid.Width, _grid.Height), Vector2.zero, 1);
            _groundSprite.hideFlags = HideFlags.DontSave;
            _renderer.sprite = _groundSprite; _renderer.sortingOrder = -10000;
            _pixels = new Color32[_grid.CellCount]; _propStates = new byte[_grid.CellCount];
            _props = new GameObject("Generated props", typeof(UnityEngine.Grid)) { hideFlags = HideFlags.DontSave };
            _props.transform.SetParent(transform, false);
            var tiles = new GameObject("Prop tiles", typeof(Tilemap), typeof(TilemapRenderer)) { hideFlags = HideFlags.DontSave };
            tiles.transform.SetParent(_props.transform, false);
            _tilemap = tiles.GetComponent<Tilemap>();
            tiles.GetComponent<TilemapRenderer>().sortingOrder = -9000;
            _tree = CreateTile(root.worldSettings.TreeSprite, root.worldSettings.TreeSize);
            _rock = CreateTile(root.worldSettings.RockSprite, root.worldSettings.RockSize);
            RepaintAll();
        }
        private static Tile CreateTile(Sprite sprite, float size)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.hideFlags = HideFlags.DontSave; tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            float scale = sprite == null ? 1 : size / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            tile.transform = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            return tile;
        }
        public void RepaintAll()
        {
            if (_grid == null || _texture == null) return;
            for (int i = 0; i < _grid.CellCount; i++)
            {
                Cell cell = _grid.GetCell(i);
                _pixels[i] = root.worldSettings.GroundColor(cell.TerrainTypeId);
                byte state = cell.HasTree ? (byte)1 : cell.HasRock ? (byte)2 : (byte)0;
                if (_propStates[i] == state) continue;
                _propStates[i] = state;
                _tilemap.SetTile(new Vector3Int(i % _grid.Width, i / _grid.Width, 0), state == 1 ? _tree : state == 2 ? _rock : null);
            }
            _texture.SetPixels32(_pixels); _texture.Apply(false);
            _revision = _grid.Revision;
        }
        private void Update()
        {
            if (_grid != null && _revision != _grid.Revision) RepaintAll();
        }
        private void OnDisable() => Clear();
        private void Clear()
        {
            if (_renderer != null) _renderer.sprite = null;
            Dispose(_props); Dispose(_groundSprite); Dispose(_texture); Dispose(_tree); Dispose(_rock);
            _props = null; _groundSprite = null; _texture = null; _tree = _rock = null; _grid = null;
            _revision = -1;
        }
        private static void Dispose(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
