using UnityEngine;
using UnityEngine.InputSystem;

namespace ColonySim.Presentation
{
    public class DesignationInputController : MonoBehaviour
    {
        public SimulationRoot root;
        public Camera targetCamera;

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);
            if (x < 0 || x >= root.Grid.Width || y < 0 || y >= root.Grid.Height) return;

            int cellIndex = root.Grid.IndexOf(x, y);
            if (!root.TryDesignateMine(cellIndex))
                root.TryDesignateChop(cellIndex);
        }
    }
}
