#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Solo Editor: permette di usare i pannelli XR col mouse nella Game view, senza visore.
/// Click sinistro: raycast dal mouse e hover/select dell'XR interactable colpito, come farebbe
/// il raggio del controller. Rotellina premuta e trascinata: ruota la visuale sul posto.
/// Non entra nelle build.
/// </summary>
[DefaultExecutionOrder(-100)]
public class EditorMouseInteractor : MonoBehaviour
{
    public float maxDistance = 30f;
    [Tooltip("Gradi di rotazione per pixel di trascinamento con la rotellina premuta.")]
    public float lookSensitivity = 0.2f;
    public float maxPitch = 80f;

    XRBaseInteractable hovered;
    float pitch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<EditorMouseInteractor>() != null) return;
        var go = new GameObject("Editor Mouse Interactor");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<EditorMouseInteractor>();
        Debug.Log("[Editor] Game view: click sinistro sui pannelli XR, rotellina premuta e trascina per guardarti intorno.");
    }

    void Update()
    {
        var cam = Camera.main;
        if (cam == null || !TryGetMouse(out Vector2 position, out bool clicked, out bool looking, out Vector2 delta))
        { SetHovered(null); return; }

        if (looking)
        {
            Look(cam.transform, delta);
            SetHovered(null);
            return;
        }

        XRBaseInteractable target = null;
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(cam.ScreenPointToRay(position), maxDistance, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.distance >= best) continue;
            var interactable = hit.collider.GetComponentInParent<XRBaseInteractable>();
            if (interactable == null || !interactable.isActiveAndEnabled) continue;
            best = hit.distance;
            target = interactable;
        }

        SetHovered(target);
        if (clicked && target != null)
        {
            target.selectEntered.Invoke(new SelectEnterEventArgs());
            target.selectExited.Invoke(new SelectExitEventArgs());
        }
    }

    void SetHovered(XRBaseInteractable target)
    {
        if (target == hovered) return;
        if (hovered != null) hovered.hoverExited.Invoke(new HoverExitEventArgs());
        hovered = target;
        if (hovered != null) hovered.hoverEntered.Invoke(new HoverEnterEventArgs());
    }

    /// <summary>
    /// Imbardata sulla radice del rig (XR Origin) attorno alla posizione della camera, beccheggio
    /// sul genitore diretto della camera (Camera Offset): il Tracked Pose Driver della camera
    /// riscrive solo la sua rotazione locale, quindi le rotazioni dei genitori restano.
    /// </summary>
    void Look(Transform cam, Vector2 delta)
    {
        Transform root = cam.root;
        Transform pitchTarget = cam.parent != null && cam.parent != root ? cam.parent : cam;

        if (root != cam) root.RotateAround(cam.position, Vector3.up, delta.x * lookSensitivity);
        else cam.Rotate(Vector3.up, delta.x * lookSensitivity, Space.World);

        float newPitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -maxPitch, maxPitch);
        float step = newPitch - pitch;
        pitch = newPitch;
        if (!Mathf.Approximately(step, 0f))
        {
            Vector3 pivot = cam.position;
            pitchTarget.RotateAround(pivot, pitchTarget == cam ? cam.right : root.right, step);
        }
    }

    static bool TryGetMouse(out Vector2 position, out bool clicked, out bool looking, out Vector2 delta)
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) { position = default; delta = default; clicked = looking = false; return false; }
        position = mouse.position.ReadValue();
        clicked = mouse.leftButton.wasPressedThisFrame;
        looking = mouse.middleButton.isPressed;
        delta = mouse.delta.ReadValue();
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        clicked = Input.GetMouseButtonDown(0);
        looking = Input.GetMouseButton(2);
        delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        return true;
#else
        position = default; delta = default; clicked = looking = false; return false;
#endif
    }

    void OnDisable() => SetHovered(null);
}
#endif
