using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>A draggable camera-relative panel. Only the handle starts a drag.</summary>
[DefaultExecutionOrder(200)]
public class CameraPanelDrag : MonoBehaviour
{
    public bool followCamera = true;
    public static bool AnyDragging { get; private set; }
    Vector3 offset = new Vector3(0f, -.22f, 1.5f);
    Transform controller;
    Camera viewer;
    bool dragging, mouseDrag;
    float rayDistance;
    Vector3 previous;
    Plane plane;

    public void Bind(XRSimpleInteractable handle)
    {
        handle.selectEntered.AddListener(Begin);
        handle.selectExited.AddListener(End);
    }

    void Begin(SelectEnterEventArgs args)
    {
        if (dragging || AnyDragging) return;
        viewer = Camera.main;
        if (viewer == null) return;
        controller = args.interactorObject?.transform;
        mouseDrag = controller == null && Application.isEditor;
        if (controller == null && !mouseDrag) return;
        plane = new Plane(viewer.transform.forward, transform.position);
        if (mouseDrag)
        {
            if (!MousePoint(out previous, out bool held) || !held) return;
        }
        else
        {
            rayDistance = Vector3.Distance(controller.position, transform.position);
            previous = controller.position + controller.forward * rayDistance;
        }
        dragging = true; AnyDragging = true;
    }

    void End(SelectExitEventArgs args)
    {
        // The Editor click adapter emits exit immediately; poll button release instead.
        if (mouseDrag) return;
        Finish();
    }

    void Finish()
    {
        if (viewer != null) offset = viewer.transform.InverseTransformPoint(transform.position);
        offset.z = Mathf.Clamp(offset.z, .6f, 5f);
        offset.x = Mathf.Clamp(offset.x, -1.2f, 1.2f);
        offset.y = Mathf.Clamp(offset.y, -.8f, .8f);
        dragging = mouseDrag = false; AnyDragging = false;
        controller = null;
    }

    void LateUpdate()
    {
        if (viewer == null) viewer = Camera.main;
        if (viewer == null) return;
        if (dragging)
        {
            Vector3 point;
            if (mouseDrag)
            {
                if (!MousePoint(out point, out bool held) || !held) { Finish(); return; }
            }
            else
            {
                if (controller == null) { Finish(); return; }
                point = controller.position + controller.forward * rayDistance;
            }
            transform.position += point - previous;
            previous = point;
        }
        else if (followCamera) transform.SetPositionAndRotation(viewer.transform.TransformPoint(offset), viewer.transform.rotation);
    }

    bool MousePoint(out Vector3 point, out bool held)
    {
        point = default; held = false;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !Application.isFocused) return false;
        held = mouse.leftButton.isPressed;
        Ray ray = viewer.ScreenPointToRay(mouse.position.ReadValue());
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
        held = Input.GetMouseButton(0);
        Ray ray = viewer.ScreenPointToRay(Input.mousePosition);
#else
        return false;
#endif
#if UNITY_EDITOR && (ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER)
        if (!plane.Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance); return true;
#endif
    }
    public void MoveDistance(float meters)
    {
        if (dragging) return;
        if (viewer == null) viewer = Camera.main;
        if (viewer == null) return;
        if (followCamera)
        {
            offset.z = Mathf.Clamp(offset.z + meters, .6f, 5f);
            transform.SetPositionAndRotation(viewer.transform.TransformPoint(offset), viewer.transform.rotation);
        }
        else
        {
            Vector3 direction = transform.position - viewer.transform.position;
            float distance = direction.magnitude;
            if (distance < .001f) direction = viewer.transform.forward; else direction /= distance;
            transform.position = viewer.transform.position + direction * Mathf.Clamp(distance + meters, .6f, 8f);
        }
    }

    void OnDisable() { if (dragging) Finish(); }
}
