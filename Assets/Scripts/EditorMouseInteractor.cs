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
    BpmnNavigator navigator;
    PrinceProcessListPanel processPanel;

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
        // Do not consume scrolling while the Inspector or Scene view has focus.
        var window = UnityEditor.EditorWindow.focusedWindow;
        if (!Application.isFocused || window == null || window.GetType().Name != "GameView")
        { SetHovered(null); return; }
        var cam = Camera.main;
        if (cam == null || !TryGetMouse(out Vector2 position, out bool clicked, out bool looking, out Vector2 delta))
        { SetHovered(null); return; }

        MoveCamera(cam);
        if (position.x >= 0f && position.y >= 0f && position.x < Screen.width && position.y < Screen.height)
        {
            float scroll = ReadScroll();
            if (!Mathf.Approximately(scroll, 0f))
            {
                if (navigator == null) navigator = FindAnyObjectByType<BpmnNavigator>();
                if (navigator != null) navigator.ZoomDiagram(scroll);
            }
        }

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

    void MoveCamera(Camera cam)
    {
        if (processPanel == null) processPanel = FindAnyObjectByType<PrinceProcessListPanel>();
        if (processPanel != null && processPanel.IsEditingFilter || CameraPanelDrag.AnyDragging) return;
        Vector2 move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        move.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
        move.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
#elif ENABLE_LEGACY_INPUT_MANAGER
        move.x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
        move.y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
#endif
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        cam.transform.root.position += (forward * move.y + right * move.x).normalized * Mathf.Min(Time.unscaledDeltaTime, .05f);
    }
    static float ReadScroll()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        float value = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        // Support both normalized steps and the older 120-unit wheel events.
        return Mathf.Clamp(Mathf.Abs(value) > 10f ? value / 120f : value, -4f, 4f);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Mathf.Clamp(Input.mouseScrollDelta.y, -4f, 4f);
#else
        return 0f;
#endif
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
