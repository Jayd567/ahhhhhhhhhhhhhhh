using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using ColonySim.Data;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Presentation
{
    // Synced centrally; keeps only a pawn ID, never a reference to pawn state.
    public class PawnView : MonoBehaviour
    {
        private SimulationRoot _root;
        private int _pawnId;
        private SortingGroup _sorting;
        private EntityQuery _pawnQuery;

        public void Initialize(SimulationRoot root, int pawnId, PawnTemplateSO template)
        {
            _root = root;
            _pawnId = pawnId;
            _pawnQuery = root.World.EntityManager.CreateEntityQuery(typeof(PawnId), typeof(LocalTransform));
            _sorting = gameObject.AddComponent<SortingGroup>();
            transform.localScale = Vector3.one * template.VisualScale;
            PawnPalette palette = template.Palettes.Length > 0
                ? template.Palettes[pawnId % template.Palettes.Length]
                : new PawnPalette { Skin = Color.white, Clothing = template.PlaceholderColor, Trousers = Color.gray };
            for (int i = 0; i < template.Layers.Length; i++)
            {
                PawnSpriteLayer layer = template.Layers[i];
                if (layer.Sprite == null) continue;
                var part = new GameObject(layer.Name);
                part.transform.SetParent(transform, false);
                part.transform.localPosition = layer.Offset;
                part.transform.localScale = new Vector3(layer.Scale.x, layer.Scale.y, 1f);
                var renderer = part.AddComponent<SpriteRenderer>();
                renderer.sprite = layer.Sprite;
                renderer.sortingOrder = i;
                renderer.color = layer.Tint == PawnTint.Skin ? palette.Skin
                    : layer.Tint == PawnTint.Clothing ? palette.Clothing
                    : layer.Tint == PawnTint.Trousers ? palette.Trousers : Color.white;
            }
            SyncFromSimulation();
        }

        public bool SyncFromSimulation()
        {
            EntityManager em = _root.World.EntityManager;
            using NativeArray<Entity> entities = _pawnQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PawnId> ids = _pawnQuery.ToComponentDataArray<PawnId>(Allocator.Temp);
            int found = -1;
            for (int i = 0; i < ids.Length; i++) if (ids[i].Value == _pawnId) { found = i; break; }
            if (found < 0) { Destroy(gameObject); return false; }

            LocalTransform pawnTransform = em.GetComponentData<LocalTransform>(entities[found]);
            Vector3 position = transform.position;
            position.x = pawnTransform.Position.x + 0.5f;
            position.y = pawnTransform.Position.y + 0.5f;
            position.z = -1f;
            transform.position = position;
            _sorting.sortingOrder = 1000 - Mathf.RoundToInt(position.y * 10f);
            return true;
        }
    }
}
