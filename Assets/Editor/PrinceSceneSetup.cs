using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Voce di menu per aggiungere in scena la lista dei processi Prince.</summary>
public static class PrinceSceneSetup
{
    [MenuItem("Tools/Prince/Crea lista processi nella scena")]
    public static void CreateProcessList()
    {
        var existing = Object.FindAnyObjectByType<PrinceProcessListPanel>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Prince] In scena c'e' gia' una lista processi, l'ho selezionata.", existing);
            return;
        }

        var go = new GameObject("Prince Process List");
        Undo.RegisterCreatedObjectUndo(go, "Crea lista processi Prince");
        var panel = go.AddComponent<PrinceProcessListPanel>();

        var builder = Object.FindAnyObjectByType<BpmnSceneBuilder>();
        if (builder != null && builder.lineMaterial != null) panel.baseMaterial = builder.lineMaterial;

        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[Prince] Lista processi creata: controlla username e password nell'Inspector, salva la scena e premi Play.", go);
    }

    [MenuItem("Tools/Prince/Dimentica sessione salvata")]
    public static void ForgetSession()
    {
        PrinceLogin.Logout();
        Debug.Log("[Prince] Sessione locale rimossa: al prossimo Play verra' rifatto il login.");
    }
}
