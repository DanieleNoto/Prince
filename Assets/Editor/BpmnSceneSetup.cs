using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Voce di menu per creare in scena l'oggetto che costruisce il diagramma.</summary>
public static class BpmnSceneSetup
{
    const string NodePrefabPath = "Assets/Prefabs/TaskNode.prefab";

    [MenuItem("Tools/BPMN/Crea BPMN Viewer nella scena")]
    public static void CreateViewer()
    {
        var existing = Object.FindFirstObjectByType<BpmnSceneBuilder>();

        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[BPMN] In scena c'e' gia' un BPMN Viewer, l'ho selezionato.", existing);
            return;
        }

        var go = new GameObject("BPMN Viewer");
        Undo.RegisterCreatedObjectUndo(go, "Crea BPMN Viewer");

        var builder = go.AddComponent<BpmnSceneBuilder>();
        builder.nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NodePrefabPath);

        if (builder.nodePrefab == null)
            Debug.LogWarning($"[BPMN] Prefab non trovato in {NodePrefabPath}: assegnalo a mano.", go);

        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log("[BPMN] BPMN Viewer creato. Salva la scena e premi Play.", go);
    }
}
