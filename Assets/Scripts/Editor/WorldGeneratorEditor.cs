using UnityEditor;
using UnityEngine;
using ColonySim.Presentation;

namespace ColonySim.Editor
{
    [CustomEditor(typeof(SimulationRoot))]
    public class WorldGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var root = (SimulationRoot)target;
            EditorGUILayout.HelpBox("256×256 seeded world. Edit noise, terrain and prop controls in the World Settings asset. Preview is temporary; Play regenerates the same seed.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || root.worldSettings == null))
            {
                if (GUILayout.Button("Generate World"))
                {
                    try
                    {
                        root.GenerateWorld();
                        foreach (var view in Object.FindObjectsByType<GridView>(FindObjectsSortMode.None))
                            if (view.root == root) view.Build();
                        SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(128, 128, 0), new Vector3(256, 256, 1)), false);
                        SceneView.RepaintAll();
                    }
                    catch (System.Exception exception) { Debug.LogException(exception, root); }
                }
            }
        }
    }
}
