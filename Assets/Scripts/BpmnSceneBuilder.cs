using System.Collections;
using System.Collections.Generic;
using System.IO;
using Bpmn;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Legge un file .bpmn da StreamingAssets, ricostruisce il diagramma in 3D usando le
/// coordinate originali del BPMNDiagram, e all'avvio lo piazza centrato davanti alla camera.
/// </summary>
[DisallowMultipleComponent]
public class BpmnSceneBuilder : MonoBehaviour
{
    [Header("Sorgente")]
    [Tooltip("Nome del file dentro Assets/StreamingAssets.")]
    public string bpmnFileName = "process.bpmn";

    [Header("Prefab")]
    public GameObject nodePrefab;

    [Header("Scala")]
    [Tooltip("Ricava i metri-per-unita-BPMN dalla larghezza del nodePrefab, cosi' i box non si sovrappongono.")]
    public bool autoScaleFromPrefab = true;

    [Tooltip("Metri per unita' BPMN, usato solo se autoScaleFromPrefab e' disattivo.")]
    public float scale = 0.002f;

    [Tooltip("Aria fra i nodi: 1 = i box si toccano alla distanza prevista dal BPMN.")]
    [Range(0.5f, 3f)]
    public float spacing = 1.15f;

    [Tooltip("Larghezza massima in metri: oltre questa il diagramma viene rimpicciolito in blocco. 0 = nessun limite.")]
    public float maxWidth = 2.4f;

    [Header("Connessioni")]
    public bool drawConnections = true;
    public float lineWidth = 0.008f;
    public Color lineColor = new Color(0.82f, 0.85f, 0.92f);

    [Tooltip("Lascialo vuoto solo in Editor: per la build su Quest assegna un materiale vero, altrimenti lo shader puo' essere escluso dalla build.")]
    public Material lineMaterial;

    public bool showArrowHeads = true;
    public float arrowSize = 0.045f;

    [Tooltip("Quanto le linee stanno davanti ai nodi, in metri. Serve perche' i box hanno spessore.")]
    public float lineDepthOffset = 0.1f;

    [Header("Forme per tipo")]
    [Tooltip("Start ed End diventano sfere, i gateway rombi. I task restano i box del prefab.")]
    public bool shapeByType = true;

    [Tooltip("Diametro in metri delle sfere di start e end.")]
    public float eventDiameter = 0.13f;

    [Tooltip("Dimensioni del rombo dei gateway: larghezza, altezza, spessore.")]
    public Vector3 gatewaySize = new Vector3(0.19f, 0.19f, 0.06f);

    [Header("Etichette dei nodi")]
    public Color nodeLabelColor = Color.black;
    public bool nodeLabelBold = true;

    [Tooltip("Larghezza del riquadro di testo in metri, uguale per tutti i nodi.")]
    public float nodeLabelWidth = 0.30f;

    [Tooltip("Aria fra il bordo superiore del nodo e il fondo del testo, in metri.")]
    public float nodeLabelMargin = 0.035f;

    public bool nodeLabelBackground = true;
    public Color nodeLabelBackgroundColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("Bordo del rettangolo di sfondo attorno al testo, in metri.")]
    public Vector2 nodeLabelPadding = new Vector2(0.018f, 0.010f);

    [Tooltip("Se un nodo non ha l'attributo name, mostra il suo id invece di lasciare l'etichetta vuota.")]
    public bool showIdWhenUnnamed;

    [Tooltip("Ridimensiona a runtime la label del prefab: quella autorata e' alta mezzo metro.")]
    public bool normalizeNodeLabels = true;

    [Tooltip("Altezza di una riga di testo, in metri.")]
    public float nodeLabelHeight = 0.045f;

    [Header("Etichette dei flussi")]
    public bool showFlowLabels = true;
    public float flowLabelHeight = 0.045f;
    public Color flowLabelColor = Color.white;

    [Header("Colori per tipo di nodo")]
    public bool colorizeByType = true;
    public Color startColor = new Color(0.30f, 0.78f, 0.45f);
    public Color endColor = new Color(0.90f, 0.35f, 0.35f);
    public Color taskColor = new Color(0.32f, 0.55f, 0.90f);
    public Color gatewayColor = new Color(0.95f, 0.75f, 0.25f);
    public Color otherColor = new Color(0.65f, 0.65f, 0.70f);

    [Header("Note del diagramma (textAnnotation)")]
    [Tooltip("Disegna sempre le textAnnotation alle loro coordinate, con il trattino verso il nodo.")]
    public bool showAnnotations = true;

    public float annotationTextHeight = 0.028f;
    public Color annotationTextColor = new Color(0.15f, 0.15f, 0.15f);
    public Color annotationBackgroundColor = new Color(1f, 0.97f, 0.86f);
    public float annotationLineWidth = 0.004f;
    public Color annotationLineColor = new Color(0.45f, 0.45f, 0.45f);

    [Header("Documentazione cliccabile")]
    [Tooltip("I nodi che hanno documentazione diventano selezionabili con il controller.")]
    public bool documentationOnClick = true;

    [Tooltip("Tinta fissa sui nodi documentati, usata quando la pulsazione e' spenta.")]
    public float documentedTint = 0.18f;

    [Tooltip("I nodi documentati pulsano per farsi notare.")]
    public bool pulseDocumentedNodes = true;

    [Tooltip("Cicli completi al secondo.")]
    public float pulseSpeed = 0.9f;

    [Tooltip("Quanto il colore si spinge verso l'evidenziazione.")]
    [Range(0f, 1f)]
    public float pulseAmount = 0.55f;

    public Color pulseHighlight = Color.white;

    [Tooltip("Lampeggio netto invece della transizione morbida.")]
    public bool pulseHardBlink;

    [Tooltip("Smette di pulsare dopo che hai aperto la sua documentazione.")]
    public bool stopPulseAfterOpening;

    [Header("Posizionamento all'avvio")]
    public bool placeInFrontOfCameraOnStart = true;

    [Tooltip("Ricava la distanza dalla larghezza del diagramma, per inquadrarlo tutto.")]
    public bool fitDistanceToDiagram = true;

    [Tooltip("Distanza fissa in metri, usata se fitDistanceToDiagram e' disattivo.")]
    public float distance = 1.8f;

    public float minDistance = 1.0f;
    public float maxDistance = 4.0f;

    [Tooltip("Offset verticale rispetto all'altezza degli occhi. Negativo = piu' in basso.")]
    public float verticalOffset = -0.1f;

    [Tooltip("Frame di attesa prima di posizionare: all'avvio il tracking XR non ha ancora una posa valida.")]
    public int trackingWarmupFrames = 10;

    BpmnDocument document;
    Transform diagramRoot;
    readonly Dictionary<string, GameObject> nodeObjects = new Dictionary<string, GameObject>();

    float unitsToMeters = 0.002f;
    Vector2 contentCenter;
    float diagramWidth;
    Material runtimeLineMaterial;
    Material runtimeLabelBackgroundMaterial;
    Material runtimeAnnotationMaterial;
    Material runtimeAnnotationLineMaterial;
    BpmnDocumentationPanel documentationPanel;
    MaterialPropertyBlock propertyBlock;

    public IReadOnlyDictionary<string, GameObject> NodeObjects => nodeObjects;
    public BpmnDocument Document => document;

    void Start()
    {
        StartCoroutine(BuildAndPlace());
    }

    IEnumerator BuildAndPlace()
    {
        yield return LoadAndBuild();

        if (!placeInFrontOfCameraOnStart) yield break;

        for (int i = 0; i < Mathf.Max(1, trackingWarmupFrames); i++)
            yield return null;

        PlaceInFrontOfCamera();
    }

    public IEnumerator LoadAndBuild()
    {
        if (nodePrefab == null)
        {
            Debug.LogError("[BPMN] nodePrefab non assegnato.", this);
            yield break;
        }

        string path = Path.Combine(Application.streamingAssetsPath, bpmnFileName);
        string xml = null;

        // Su Android StreamingAssets sta dentro l'APK: File.ReadAllText non funziona.
        if (path.Contains("://") || path.Contains(":///"))
        {
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[BPMN] Lettura fallita: {path} ({request.error})", this);
                    yield break;
                }

                xml = request.downloadHandler.text;
            }
        }
        else
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"[BPMN] File non trovato: {path}", this);
                yield break;
            }

            xml = File.ReadAllText(path);
        }

        try
        {
            document = BpmnDocument.Parse(xml);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BPMN] XML non valido: {e.Message}", this);
            yield break;
        }

        Rebuild();
    }

    /// <summary>Ricostruisce la geometria dal documento gia' caricato.</summary>
    public void Rebuild()
    {
        if (document == null) return;

        ClearDiagram();

        if (!document.TryGetContentBounds(out Rect content))
        {
            Debug.LogWarning("[BPMN] Il file non ha una sezione BPMNDiagram con le coordinate.", this);
            return;
        }

        contentCenter = content.center;
        unitsToMeters = autoScaleFromPrefab ? DeriveScaleFromPrefab() : Mathf.Max(1e-6f, scale);
        diagramWidth = content.width * unitsToMeters;

        var rootGo = new GameObject("Diagram");
        diagramRoot = rootGo.transform;
        diagramRoot.SetParent(transform, false);

        // Se il diagramma e' troppo largo lo si rimpicciolisce in blocco: scala uniforme,
        // quindi nodi, testi e linee restano proporzionati fra loro.
        if (maxWidth > 0f && diagramWidth > maxWidth)
        {
            float shrink = maxWidth / diagramWidth;
            diagramRoot.localScale = Vector3.one * shrink;
            diagramWidth = maxWidth;
        }

        BuildNodes();

        if (drawConnections) BuildConnections();
        if (showAnnotations) BuildAnnotations();

        int documentati = 0;
        foreach (BpmnNode n in document.Nodes) if (n.HasDocumentation) documentati++;

        Debug.Log($"[BPMN] {document.Nodes.Count} nodi, {document.Flows.Count} flussi, " +
                  $"{document.Annotations.Count} note, {documentati} con documentation, " +
                  $"larghezza {diagramWidth:F2} m.", this);
    }

    void BuildNodes()
    {
        foreach (BpmnNode node in document.Nodes)
        {
            if (!node.HasBounds) continue;

            GameObject obj = Instantiate(nodePrefab, diagramRoot);
            obj.name = node.Id;
            obj.transform.localPosition = ToLocal(node.Center);
            obj.transform.localRotation = Quaternion.identity;

            var view = obj.GetComponent<NodeView>();
            string label = node.Name;
            if (string.IsNullOrEmpty(label)) label = showIdWhenUnnamed ? node.Id : string.Empty;

            if (view != null) view.Setup(node.Id, label, node.TypeName);

            if (shapeByType) ApplyShape(obj, node.Kind);
            if (documentationOnClick && node.HasDocumentation) MakeClickable(obj, node);
            if (normalizeNodeLabels) NormalizeNodeLabel(obj);
            Color nodeColor = ColorFor(node.Kind);
            bool documented = documentationOnClick && node.HasDocumentation;

            if (documented && !pulseDocumentedNodes)
                nodeColor = Color.Lerp(nodeColor, pulseHighlight, documentedTint);

            if (colorizeByType) ApplyColor(obj, nodeColor);

            if (documented && pulseDocumentedNodes)
            {
                var pulse = obj.AddComponent<BpmnDocumentationPulse>();
                pulse.Configure(nodeColor, pulseHighlight, pulseSpeed, pulseAmount, pulseHardBlink);
            }

            nodeObjects[node.Id] = obj;
        }
    }

    void BuildConnections()
    {
        Material material = ResolveLineMaterial();

        foreach (BpmnFlow flow in document.Flows)
        {
            List<Vector3> points = BuildFlowPoints(flow);
            if (points == null || points.Count < 2) continue;

            var go = new GameObject($"Flow_{flow.Id}");
            go.transform.SetParent(diagramRoot, false);

            var connector = go.AddComponent<BpmnConnector>();
            connector.Build(points, lineWidth, lineColor, material, showArrowHeads, arrowSize);

            if (showFlowLabels && !string.IsNullOrEmpty(flow.Name))
                CreateFlowLabel(flow.Name, points);
        }
    }

    /// <summary>
    /// Le textAnnotation sono parte del disegno: si mettono alle loro coordinate, con il
    /// trattino dell'association verso il nodo. Niente click, sono sempre leggibili.
    /// </summary>
    void BuildAnnotations()
    {
        Material noteMaterial = ResolveAnnotationMaterial();

        foreach (BpmnAnnotation annotation in document.Annotations)
        {
            if (!annotation.HasBounds) continue;

            float width = annotation.Bounds.width * unitsToMeters;
            float height = annotation.Bounds.height * unitsToMeters;

            var note = new GameObject("Note_" + annotation.Id);
            note.transform.SetParent(diagramRoot, false);
            note.transform.localPosition = ToLocal(annotation.Center) + LineDepth;
            note.transform.localRotation = Quaternion.identity;

            var background = new GameObject("Background");
            background.transform.SetParent(note.transform, false);
            background.transform.localScale = new Vector3(width, height, 1f);
            background.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();

            var renderer = background.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = noteMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            CreateNoteText(note.transform, annotation.Text, width, height);
        }

        Material lineMat = ResolveAnnotationLineMaterial();

        foreach (BpmnAssociation association in document.Associations)
        {
            if (association.Waypoints.Count < 2) continue;

            var points = new List<Vector3>();
            foreach (Vector2 wp in association.Waypoints) points.Add(ToLocal(wp) + LineDepth);

            var go = new GameObject("Association_" + association.Id);
            go.transform.SetParent(diagramRoot, false);

            var connector = go.AddComponent<BpmnConnector>();
            connector.Build(points, annotationLineWidth, annotationLineColor, lineMat, false, 0f);
        }
    }

    void CreateNoteText(Transform note, string content, float width, float height)
    {
        if (TMP_Settings.defaultFontAsset == null) return;

        float scale = annotationTextHeight * 10f;

        var go = new GameObject("Text");
        go.transform.SetParent(note, false);
        go.transform.localScale = Vector3.one * scale;
        go.transform.localPosition = new Vector3(0f, 0f, -0.004f);

        var text = go.AddComponent<TextMeshPro>();
        text.text = content;
        text.fontSize = 1f;
        text.color = annotationTextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Overflow;
        text.rectTransform.sizeDelta = new Vector2(
            (width * 0.9f) / scale,
            (height * 0.9f) / scale);
    }

    Material ResolveAnnotationMaterial()
    {
        if (runtimeAnnotationMaterial != null) return runtimeAnnotationMaterial;
        runtimeAnnotationMaterial = CreateUnlitMaterial("BpmnNote", annotationBackgroundColor);
        return runtimeAnnotationMaterial;
    }

    Material ResolveAnnotationLineMaterial()
    {
        if (runtimeAnnotationLineMaterial != null) return runtimeAnnotationLineMaterial;
        runtimeAnnotationLineMaterial = CreateUnlitMaterial("BpmnNoteLine", annotationLineColor);
        return runtimeAnnotationLineMaterial;
    }

    static Material CreateUnlitMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");

        if (shader == null) return null;

        var material = new Material(shader) { name = name + " (runtime)" };
        SetColor(material, color);
        return material;
    }

    List<Vector3> BuildFlowPoints(BpmnFlow flow)
    {
        var points = new List<Vector3>();

        if (flow.Waypoints.Count >= 2)
        {
            // I waypoint del BPMNDiagram partono e arrivano gia' sul bordo delle forme.
            foreach (Vector2 wp in flow.Waypoints)
                points.Add(ToLocal(wp) + LineDepth);

            return points;
        }

        // Nessun waypoint: linea diretta fra i due nodi, tagliata sul bordo delle forme.
        BpmnNode source = document.GetNode(flow.SourceRef);
        BpmnNode target = document.GetNode(flow.TargetRef);
        if (source == null || target == null || !source.HasBounds || !target.HasBounds) return null;

        points.Add(ToLocal(EdgePoint(source, target.Center)) + LineDepth);
        points.Add(ToLocal(EdgePoint(target, source.Center)) + LineDepth);
        return points;
    }

    void CreateFlowLabel(string text, List<Vector3> points)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogWarning("[BPMN] Nessun font TMP di default: etichette dei flussi saltate.", this);
            return;
        }

        var go = new GameObject("FlowLabel");
        go.transform.SetParent(diagramRoot, false);
        go.transform.localPosition = MidPoint(points) + new Vector3(0f, 0f, -0.01f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * flowLabelHeight;

        var label = go.AddComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = 1f;
        label.color = flowLabelColor;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(6f, 1.4f);
    }

    /// <summary>Piazza il diagramma davanti alla camera, in piedi e rivolto verso di te.</summary>
    [ContextMenu("Piazza davanti alla camera")]
    public void PlaceInFrontOfCamera()
    {
        Camera camera = Camera.main;

        if (camera == null)
        {
            Debug.LogWarning("[BPMN] Nessuna camera con tag MainCamera: posizionamento saltato.", this);
            return;
        }

        Transform cam = camera.transform;

        Vector3 forward = cam.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        else forward.Normalize();

        float d = fitDistanceToDiagram
            ? Mathf.Clamp(diagramWidth * 0.85f, minDistance, maxDistance)
            : distance;

        transform.SetPositionAndRotation(
            cam.position + forward * d + Vector3.up * verticalOffset,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    public void ClearDiagram()
    {
        nodeObjects.Clear();

        if (diagramRoot == null) return;

        if (Application.isPlaying) Destroy(diagramRoot.gameObject);
        else DestroyImmediate(diagramRoot.gameObject);

        diagramRoot = null;
    }

    /// <summary>Scostamento verso chi guarda: il root ha +Z lontano dall'osservatore.</summary>
    Vector3 LineDepth => new Vector3(0f, 0f, -lineDepthOffset);

    Vector3 ToLocal(Vector2 bpmnPoint)
    {
        // BPMN: x a destra, y verso il BASSO. Unity: y verso l'alto, quindi si inverte.
        return new Vector3(
            (bpmnPoint.x - contentCenter.x) * unitsToMeters,
            -(bpmnPoint.y - contentCenter.y) * unitsToMeters,
            0f);
    }

    float DeriveScaleFromPrefab()
    {
        float prefabWidth = 0.3f;

        var filter = nodePrefab.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            prefabWidth = filter.sharedMesh.bounds.size.x * nodePrefab.transform.localScale.x;

        float referenceWidth = Mathf.Max(1f, document.AverageTaskWidth());
        return prefabWidth * spacing / referenceWidth;
    }

    Material ResolveLineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;

        if (runtimeLineMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError("[BPMN] Nessuno shader disponibile per le linee: assegna lineMaterial.", this);
                return null;
            }

            runtimeLineMaterial = new Material(shader) { name = "BpmnLine (runtime)" };
            SetColor(runtimeLineMaterial, lineColor);
        }

        return runtimeLineMaterial;
    }

    /// <summary>
    /// La label del prefab e' scalata in modo anisotropo e sovradimensionata. Qui le si impone
    /// una scala uniforme in metri, senza toccare l'asset: in TMP world-space una riga di testo
    /// e' alta circa fontSize/10 unita' locali, da cui il fattore.
    /// </summary>
    void NormalizeNodeLabel(GameObject obj)
    {
        var label = obj.GetComponentInChildren<TMP_Text>();
        if (label == null) return;

        // Un nodo senza name (i gateway spesso non ce l'hanno) non ha geometria di testo:
        // TMP restituirebbe bounds degeneri e lo sfondo diventerebbe grande chilometri.
        if (string.IsNullOrWhiteSpace(label.text))
        {
            label.gameObject.SetActive(false);
            return;
        }

        float fontSize = Mathf.Max(1f, label.fontSize);
        float worldScale = 10f * nodeLabelHeight / fontSize;

        Vector3 nodeScale = obj.transform.localScale;
        nodeScale.x = Mathf.Approximately(nodeScale.x, 0f) ? 1f : nodeScale.x;
        nodeScale.y = Mathf.Approximately(nodeScale.y, 0f) ? 1f : nodeScale.y;
        nodeScale.z = Mathf.Approximately(nodeScale.z, 0f) ? 1f : nodeScale.z;

        label.color = nodeLabelColor;
        if (nodeLabelBold) label.fontStyle |= FontStyles.Bold;

        Transform t = label.transform;

        // Scala uniforme nel mondo: si annulla la scala non uniforme del nodo padre.
        t.localScale = new Vector3(
            worldScale / nodeScale.x,
            worldScale / nodeScale.y,
            worldScale / nodeScale.z);

        t.localRotation = Quaternion.identity;

        label.rectTransform.sizeDelta = new Vector2(
            nodeLabelWidth / worldScale,
            nodeLabelHeight * 4f / worldScale);

        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.Center;

        // Misura il testo davvero renderizzato: il numero di righe cambia da nodo a nodo,
        // quindi un offset fisso finirebbe dentro le forme piu' alte (sfere e rombi).
        label.ForceMeshUpdate();
        Bounds textBounds = label.textBounds;
        float textHeight = textBounds.size.y * worldScale;
        float textWidth = textBounds.size.x * worldScale;

        if (!IsUsableSize(textWidth) || !IsUsableSize(textHeight))
        {
            Debug.LogWarning($"[BPMN] Misure di testo non valide su '{obj.name}': sfondo saltato.", obj);
            textWidth = nodeLabelWidth;
            textHeight = nodeLabelHeight;
        }

        float centerY = nodeScale.y * 0.5f + textHeight * 0.5f + nodeLabelMargin;
        t.localPosition = new Vector3(0f, centerY / nodeScale.y, 0f);

        if (nodeLabelBackground)
            CreateLabelBackground(obj.transform, nodeScale, centerY, textWidth, textHeight);
    }

    static bool IsUsableSize(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f && value < 10f;

    /// <summary>Rettangolo pieno dietro al testo, per staccarlo dallo sfondo del passthrough.</summary>
    void CreateLabelBackground(Transform node, Vector3 nodeScale, float centerY, float textWidth, float textHeight)
    {
        var go = new GameObject("LabelBackground");
        go.transform.SetParent(node, false);

        // Il quad e' visibile dal lato -Z, lo stesso da cui si legge il testo.
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(
            (textWidth + nodeLabelPadding.x * 2f) / nodeScale.x,
            (textHeight + nodeLabelPadding.y * 2f) / nodeScale.y,
            1f / nodeScale.z);

        // Appena dietro al testo rispetto a chi guarda (+Z locale).
        go.transform.localPosition = new Vector3(0f, centerY / nodeScale.y, 0.004f / nodeScale.z);

        go.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ResolveLabelBackgroundMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>
    /// Rende il nodo selezionabile con il controller: al click apre il pannello con la
    /// documentazione presa dall'elemento documentation del nodo.
    /// </summary>
    void MakeClickable(GameObject obj, BpmnNode node)
    {
        if (obj.GetComponent<Collider>() == null)
        {
            Debug.LogWarning($"[BPMN] '{obj.name}' non ha collider: non sara' cliccabile.", obj);
            return;
        }

        var interactable = obj.AddComponent<XRSimpleInteractable>();

        string heading = string.IsNullOrEmpty(node.Name) ? node.Id : node.Name;
        string text = node.Documentation;

        interactable.selectEntered.AddListener(_ =>
        {
            ShowDocumentation(heading, text);

            if (!stopPulseAfterOpening) return;

            var pulse = obj.GetComponent<BpmnDocumentationPulse>();
            if (pulse != null) pulse.StopPulsing();
        });
    }

    void ShowDocumentation(string heading, string text)
    {
        if (documentationPanel == null) documentationPanel = BpmnDocumentationPanel.Create();
        documentationPanel.Show(heading, text, Camera.main);
    }

    /// <summary>
    /// Sostituisce la mesh del prefab in base al tipo di nodo. I task restano box perche'
    /// il prefab e' gia' quello; eventi e gateway prendono sfera e rombo.
    /// </summary>
    void ApplyShape(GameObject obj, BpmnNodeKind kind)
    {
        var filter = obj.GetComponent<MeshFilter>();
        if (filter == null) return;

        switch (kind)
        {
            case BpmnNodeKind.Start:
            case BpmnNodeKind.End:
            case BpmnNodeKind.Event:
                filter.sharedMesh = BpmnNodeShapes.Sphere();
                obj.transform.localScale = Vector3.one * eventDiameter;
                break;

            case BpmnNodeKind.Gateway:
                filter.sharedMesh = BpmnNodeShapes.Diamond();
                obj.transform.localScale = gatewaySize;
                break;
        }
    }

    Material ResolveLabelBackgroundMaterial()
    {
        if (runtimeLabelBackgroundMaterial != null) return runtimeLabelBackgroundMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");

        if (shader == null) return null;

        runtimeLabelBackgroundMaterial = new Material(shader) { name = "BpmnLabelBackground (runtime)" };
        SetColor(runtimeLabelBackgroundMaterial, nodeLabelBackgroundColor);
        return runtimeLabelBackgroundMaterial;
    }

    void ApplyColor(GameObject obj, Color color)
    {
        var renderer = obj.GetComponent<Renderer>();
        if (renderer == null) return;

        propertyBlock ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);

        if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor"))
            propertyBlock.SetColor("_BaseColor", color);

        propertyBlock.SetColor("_Color", color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    Color ColorFor(BpmnNodeKind kind)
    {
        switch (kind)
        {
            case BpmnNodeKind.Start: return startColor;
            case BpmnNodeKind.End: return endColor;
            case BpmnNodeKind.Task: return taskColor;
            case BpmnNodeKind.Gateway: return gatewayColor;
            default: return otherColor;
        }
    }

    static void SetColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }

    /// <summary>Punto sul bordo del rettangolo, lungo la direzione verso target.</summary>
    static Vector2 EdgePoint(BpmnNode node, Vector2 target)
    {
        Vector2 center = node.Center;
        Vector2 dir = target - center;

        if (dir.sqrMagnitude < 1e-6f) return center;

        float tx = node.Bounds.width * 0.5f / Mathf.Max(Mathf.Abs(dir.x), 1e-4f);
        float ty = node.Bounds.height * 0.5f / Mathf.Max(Mathf.Abs(dir.y), 1e-4f);

        return center + dir * Mathf.Min(tx, ty);
    }

    static Vector3 MidPoint(List<Vector3> points)
    {
        float total = 0f;
        for (int i = 1; i < points.Count; i++)
            total += Vector3.Distance(points[i - 1], points[i]);

        float half = total * 0.5f;
        float walked = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector3.Distance(points[i - 1], points[i]);
            if (walked + segment >= half)
            {
                float t = segment > 1e-6f ? (half - walked) / segment : 0f;
                return Vector3.Lerp(points[i - 1], points[i], t);
            }

            walked += segment;
        }

        return points[points.Count - 1];
    }
}
