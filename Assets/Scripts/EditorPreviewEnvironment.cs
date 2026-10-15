using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Preview scenery in the Editor; transparent camera and no scenery in players.</summary>
public static class EditorPreviewEnvironment
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Environment") root.SetActive(Application.isEditor);
        }
        Camera camera = Camera.main;
        if (camera == null) return;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Application.isEditor
            ? new Color(0.12f, 0.16f, 0.22f, 1f)
            : new Color(0f, 0f, 0f, 0f);
    }
}
