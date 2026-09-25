using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Tilemaps;
using ColonySim.Simulation.Generation;
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
        private int _cellCount, _width;
        private int _revision = -1;
        private Color32[] _pixels;

        private void Start() { if (Application.isPlaying && root != null) Build(); }
        private void OnEnable() { if (!Application.isPlaying && (root == null || root.World == null)) Clear(); }

        public void Build()
        {
            Clear();
            if (root == null || root.World == null || root.worldSettings == null) return;
            EntityManager em = root.World.EntityManager;
            GridDimensions dims = em.GetComponentData<GridDimensions>(root.GridEntity);
            _width = dims.Width;
            _cellCount = dims.Width * dims.Height;

            _renderer = GetComponent<SpriteRenderer>();
            _texture = new Texture2D(dims.Width, dims.Height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            _groundSprite = Sprite.Create(_texture, new Rect(0, 0, dims.Width, dims.Height), Vector2.zero, 1);
            _groundSprite.hideFlags = HideFlags.DontSave;
            _renderer.sprite = _groundSprite; _renderer.sortingOrder = -10000;
            _pixels = new Color32[_cellCount]; _propStates = new byte[_cellCount];
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
            if (root == null || root.World == null || _texture == null) return;
            EntityManager em = root.World.EntityManager;
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(root.GridEntity);

            var treeCells = new bool[_cellCount];
            EntityQuery treeQuery = em.CreateEntityQuery(typeof(TreeTag), typeof(TreeCellIndex));
            using (NativeArray<TreeCellIndex> trees = treeQuery.ToComponentDataArray<TreeCellIndex>(Allocator.Temp))
                for (int i = 0; i < trees.Length; i++) treeCells[trees[i].Value] = true;

            for (int i = 0; i < _cellCount; i++)
            {
                CellData cell = cells[i].Value;
                _pixels[i] = root.worldSettings.GroundColor(cell.TerrainId);
                byte state = treeCells[i] ? (byte)1 : cell.HasRock ? (byte)2 : (byte)0;
                if (_propStates[i] == state) continue;
                _propStates[i] = state;
                _tilemap.SetTile(new Vector3Int(i % _width, i / _width, 0), state == 1 ? _tree : state == 2 ? _rock : null);
            }
            _texture.SetPixels32(_pixels); _texture.Apply(false);
            _revision = em.GetComponentData<GridRevision>(root.GridEntity).Value;
        }

        private void Update()
        {
            if (root != null && root.World != null && _revision != root.World.EntityManager.GetComponentData<GridRevision>(root.GridEntity).Value)
                RepaintAll();
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            if (_renderer != null) _renderer.sprite = null;
            Dispose(_props); Dispose(_groundSprite); Dispose(_texture); Dispose(_tree); Dispose(_rock);
            _props = null; _groundSprite = null; _texture = null; _tree = _rock = null;
            _revision = -1;
        }

        private static void Dispose(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
