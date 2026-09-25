using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using ColonySim.Simulation.Grid;

namespace ColonySim.Presentation
{
    public class DesignationInputController : MonoBehaviour
    {
        public SimulationRoot root;
        public Camera targetCamera;

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;
            if (root == null || root.World == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);

            EntityManager em = root.World.EntityManager;
            GridDimensions dims = em.GetComponentData<GridDimensions>(root.GridEntity);
            if (x < 0 || x >= dims.Width || y < 0 || y >= dims.Height) return;

            int cellIndex = y * dims.Width + x;
            if (!root.TryDesignateMine(cellIndex))
                root.TryDesignateChop(cellIndex);
        }
    }
}
