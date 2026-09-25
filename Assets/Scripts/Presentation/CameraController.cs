using UnityEngine;
using UnityEngine.InputSystem;

namespace ColonySim.Presentation
{
    public class CameraController : MonoBehaviour
    {
        public float panSpeed = 10f;
        public float zoomSpeed = 5f;
        public float minOrthoSize = 3f;
        public float maxOrthoSize = 20f;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Update()
        {
            Vector2 moveInput = Vector2.zero;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveInput.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveInput.y -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
            }

            transform.position += (Vector3)(moveInput * panSpeed * Time.deltaTime);

            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _camera.orthographicSize = Mathf.Clamp(
                        _camera.orthographicSize - scroll * zoomSpeed * Time.deltaTime,
                        minOrthoSize, maxOrthoSize);
                }
            }
        }
    }
}
