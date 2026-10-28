using System.Collections;
using System.Collections.Generic;
using Bpmn;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
[RequireComponent(typeof(BpmnSceneBuilder))]
public class BpmnNavigator : MonoBehaviour
{
    public bool followUser = true;
    public float followDeadZone = 0.25f;
    public float followSpeed = 3f;
    public float moveStep = 0.2f;
    public float closeDistance = 1.15f;
    [Header("Zoom con controller destro")]
    public bool rightStickZoom = true;
    [Range(0.1f, 0.8f)] public float zoomStickDeadZone = 0.25f;
    [Min(0.1f)] public float zoomStepsPerSecond = 5f;

    BpmnSceneBuilder builder;
    readonly List<GameObject> nodes = new List<GameObject>();
    readonly Dictionary<GameObject, bool> visibility = new Dictionary<GameObject, bool>();
    Transform diagram;
    Transform panel;
    TMP_Text status, followLabel, isolateLabel;
    Material backgroundMaterial, buttonMaterial, hoverMaterial;
    Vector3 originalScale, headAnchor;
    bool ready, isolated = true;
    int selected = -1, hovered;
    Camera viewer;

    public int SelectedIndex => selected;
    public int NodeCount => nodes.Count;

    IEnumerator Start()
    {
        builder = GetComponent<BpmnSceneBuilder>();
        viewer = Camera.main;
        builder.DiagramBuilt += OnDiagramBuilt;
        while (!builder.IsReady || builder.Document == null || builder.NodeObjects.Count == 0 ||
               builder.DiagramRoot == null || !builder.DiagramRoot.gameObject.activeSelf)
            yield return null;

        diagram = builder.DiagramRoot;
        originalScale = transform.localScale;
        BuildOrder();
        if (viewer == null) viewer = Camera.main;
        if (viewer == null || nodes.Count == 0) yield break;
        headAnchor = viewer.transform.position;
        BuildPanel();
        ready = true;
        RefreshLabels();
    }

    /// <summary>Un nuovo diagramma ha sostituito il precedente: si riparte dalla panoramica.</summary>
    void OnDiagramBuilt()
    {
        if (!ready) return; // il primo diagramma lo gestisce Start
        visibility.Clear();
        nodes.Clear();
        selected = -1;
        diagram = builder.DiagramRoot;
        originalScale = transform.localScale;
        BuildOrder();
        if (viewer != null) headAnchor = viewer.transform.position;
        RefreshLabels();
    }

    // Traverse outgoing flows from start events, then include disconnected nodes.
    // At branches this is a breadth-first browsing order, not process execution.
    void BuildOrder()
    {
        var seen = new HashSet<string>();
        var queue = new Queue<string>();
        foreach (var n in builder.Document.Nodes)
            if (n.Kind == BpmnNodeKind.Start) queue.Enqueue(n.Id);
        foreach (var seed in builder.Document.Nodes)
        {
            queue.Enqueue(seed.Id);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!seen.Add(id)) continue;
                if (builder.NodeObjects.TryGetValue(id, out var go)) nodes.Add(go);
                foreach (var flow in builder.Document.Flows)
                    if (flow.SourceRef == id) queue.Enqueue(flow.TargetRef);
            }
        }
    }

    bool HasTracking()
    {
        var settings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null || settings.Manager.activeLoader == null)
            return true;
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        return head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
    }

    void LateUpdate()
    {
        if (viewer == null) viewer = Camera.main;
        if (!ready || viewer == null || !HasTracking()) return;
        UpdateControllerZoom();
        Vector3 head = viewer.transform.position;
        if (followUser && hovered == 0 && !CameraPanelDrag.AnyDragging)
        {
            Vector3 delta = head - headAnchor;
            if (delta.magnitude > followDeadZone)
            {
                Vector3 step = delta.normalized * (delta.magnitude - followDeadZone)
                    * (1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
                if (builder.useSharedLayout) PanelLayout.Translate(step); // tutti i pannelli insieme
                else transform.position += step;
                headAnchor += step;
            }
        }

    }

    void UpdateControllerZoom()
    {
        if (!rightStickZoom || !Application.isFocused || CameraPanelDrag.AnyDragging) return;
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!right.isValid || !right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)) return;
        float magnitude = Mathf.Abs(axis.y);
        // Ignore resting stick noise and primarily horizontal gestures (e.g. snap turn).
        if (magnitude <= zoomStickDeadZone || magnitude <= Mathf.Abs(axis.x)) return;
        float amount = Mathf.Sign(axis.y) * Mathf.InverseLerp(zoomStickDeadZone, 1f, magnitude);
        ZoomDiagram(amount * zoomStepsPerSecond * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
    }
    Vector3 FlatForward()
    {
        Vector3 f = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up);
        return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
    }

    public void ToggleFollow()
    {
        followUser = !followUser;
        headAnchor = viewer.transform.position;
        RefreshLabels();
    }

    public void Recall()
    {
        if (!ready) return;
        if (selected >= 0) PlaceSelected();
        else builder.PlaceInFrontOfCamera();
        headAnchor = viewer.transform.position;
    }

    public void Overview()
    {
        if (!ready) return;
        RestoreVisibility();
        selected = -1;
        transform.localScale = originalScale;
        builder.PlaceInFrontOfCamera();
        headAnchor = viewer.transform.position;
        RefreshLabels();
    }

    public void NextNode() => Browse(1);
    public void PreviousNode() => Browse(-1);

    void Browse(int direction)
    {
        if (!ready || nodes.Count == 0) return;
        selected = selected < 0 ? (direction > 0 ? 0 : nodes.Count - 1)
            : (selected + direction + nodes.Count) % nodes.Count;
        RestoreVisibility();
        transform.localScale = originalScale;
        var renderers = nodes[selected].GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float factor = Mathf.Clamp(Mathf.Min(0.65f / Mathf.Max(bounds.size.x, 0.01f),
                0.38f / Mathf.Max(bounds.size.y, 0.01f)), 0.25f, 8f);
            transform.localScale = originalScale * factor;
        }
        ApplyIsolation();
        PlaceSelected();
        headAnchor = viewer.transform.position;
        RefreshLabels();
    }

    void PlaceSelected()
    {
        Vector3 forward = FlatForward();
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 target = viewer.transform.position + forward * closeDistance;
        transform.position += target - nodes[selected].transform.position;
    }

    public void ToggleIsolation()
    {
        isolated = !isolated;
        RestoreVisibility();
        ApplyIsolation();
        RefreshLabels();
    }

    void ApplyIsolation()
    {
        if (!isolated || selected < 0 || diagram == null) return;
        foreach (Transform child in diagram)
        {
            visibility[child.gameObject] = child.gameObject.activeSelf;
            child.gameObject.SetActive(child.gameObject == nodes[selected]);
        }
    }

    void RestoreVisibility()
    {
        foreach (var pair in visibility)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        visibility.Clear();
    }

    /// <summary>Scale around the selected node (or diagram center), keeping controls unchanged.</summary>
    public void ZoomDiagram(float wheelSteps)
    {
        if (!ready || Mathf.Approximately(wheelSteps, 0f)) return;
        Transform pivot = selected >= 0 && selected < nodes.Count ? nodes[selected].transform : transform;
        Vector3 anchor = pivot.position;
        float baseline = Mathf.Max(Mathf.Abs(originalScale.x), 0.0001f);
        float current = Mathf.Abs(transform.localScale.x) / baseline;
        float target = Mathf.Clamp(current * Mathf.Exp(wheelSteps * 0.12f), 0.25f, 8f);
        transform.localScale = originalScale * target;
        transform.position += anchor - pivot.position;
    }
    public void MoveDiagram(Vector3 localDirection)
    {
        if (!ready) return;
        Quaternion rotation = Quaternion.LookRotation(FlatForward(), Vector3.up);
        Vector3 step = rotation * localDirection * moveStep;
        Vector3 point = selected >= 0 ? nodes[selected].transform.position : transform.position;
        float depth = Vector3.Dot(point + step - viewer.transform.position, FlatForward());
        if (depth < 0.6f || depth > 8f) return;
        transform.position += step;
    }

    void RefreshLabels()
    {
        if (status == null) return;
        string name = selected < 0 ? "Schema completo" :
            builder.Document.GetNode(nodes[selected].name)?.Name;
        if (selected >= 0 && string.IsNullOrWhiteSpace(name)) name = nodes[selected].name;
        status.text = selected < 0 ? name + " | " + nodes.Count + " blocchi" :
            (selected + 1) + "/" + nodes.Count + "  " + name;
        followLabel.text = followUser ? "Segui: SI" : "Segui: NO";
        isolateLabel.text = isolated ? "Vista: singolo" : "Vista: contesto";
    }

    void BuildPanel()
    {
        panel = new GameObject("BPMN Navigation Controls").transform;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(panel.gameObject, gameObject.scene);
        backgroundMaterial = MakeMaterial("BPMN Controls", new Color(0.025f, 0.04f, 0.075f));
        buttonMaterial = MakeMaterial("BPMN Button", new Color(0.08f, 0.19f, 0.31f));
        hoverMaterial = MakeMaterial("BPMN Button Hover", new Color(0.08f, 0.43f, 0.58f));
        Quad(panel, "Background", new Vector3(0f, -0.039f, 0f), new Vector2(0.86f, 0.508f), backgroundMaterial);
        // Con la disposizione condivisa i comandi stanno fermi sotto al diagramma invece di seguire la testa.
        PanelMoveBar.Attach(panel, .215f, .86f, buttonMaterial, !builder.useSharedLayout);
        status = Text(panel, "Caricamento", new Vector3(0, 0.165f, -0.006f), new Vector2(0.8f, 0.06f), 0.027f);
        followLabel = Button(0, 0, "Segui", ToggleFollow);
        Button(1, 0, "Richiama", Recall);
        Button(2, 0, "Panoramica", Overview);
        Button(0, 1, "Precedente", PreviousNode);
        Button(1, 1, "Successivo", NextNode);
        isolateLabel = Button(2, 1, "Vista", ToggleIsolation);
        Button(0, 2, "Sinistra", () => MoveDiagram(Vector3.left));
        Button(1, 2, "Destra", () => MoveDiagram(Vector3.right));
        Button(2, 2, "Su", () => MoveDiagram(Vector3.up));
        Button(0, 3, "Avvicina", () => MoveDiagram(Vector3.back));
        Button(1, 3, "Allontana", () => MoveDiagram(Vector3.forward));
        Button(2, 3, "Giu", () => MoveDiagram(Vector3.down));
        Button(0, 4, "Zoom -", () => ZoomDiagram(-1f));
        Button(2, 4, "Zoom +", () => ZoomDiagram(1f));

        if (builder.useSharedLayout)
        {
            panel.localScale = Vector3.one * 0.8f;
            PanelLayout.Register(panel, PanelLayout.Column.Center, 1); // sotto al diagramma
            PanelLayout.Arrange();
        }
    }

    TMP_Text Button(int col, int row, string caption, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(caption);
        go.transform.SetParent(panel, false);
        go.transform.localPosition = new Vector3((col - 1) * 0.275f, 0.09f - row * 0.078f, -0.009f);
        var visual = Quad(go.transform, "Face", Vector3.zero, new Vector2(0.255f, 0.065f), buttonMaterial);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.255f, 0.065f, 0.018f);
        var label = Text(go.transform, caption, new Vector3(0, 0, -0.014f), new Vector2(0.24f, 0.055f), 0.026f);
        var interactable = go.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(_ => action());
        interactable.hoverEntered.AddListener(_ => { hovered++; visual.sharedMaterial = hoverMaterial; });
        interactable.hoverExited.AddListener(_ => { hovered = Mathf.Max(0, hovered - 1); visual.sharedMaterial = buttonMaterial; });
        return label;
    }

    Material MakeMaterial(string name, Color color)
    {
        var mat = builder.lineMaterial != null ? new Material(builder.lineMaterial) :
            new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.name = name;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    static MeshRenderer Quad(Transform parent, string name, Vector3 position, Vector2 size, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1);
        go.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    static TMP_Text Text(Transform parent, string caption, Vector3 position, Vector2 size, float height)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        float scale = height * 10f;
        go.transform.localScale = Vector3.one * scale;
        var text = go.AddComponent<TextMeshPro>();
        text.text = caption;
        text.richText = false;
        text.fontSize = 1;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.rectTransform.sizeDelta = size / scale;
        return text;
    }

    void OnDisable()
    {
        RestoreVisibility();
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (panel != null) panel.gameObject.SetActive(true);
        hovered = 0;
        if (viewer != null) headAnchor = viewer.transform.position;
    }

    static void Release(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    void OnDestroy()
    {
        Release(panel != null ? panel.gameObject : null);
        Release(backgroundMaterial);
        Release(buttonMaterial);
        Release(hoverMaterial);
        if (builder != null) builder.DiagramBuilt -= OnDiagramBuilt;
    }
}
