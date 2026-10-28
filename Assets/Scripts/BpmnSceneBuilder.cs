using System.Collections;
using System.Collections.Generic;
using Bpmn;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Riceve un BPMN dalle API esclusivamente in RAM, ricostruisce il diagramma in 3D usando le
/// coordinate originali del BPMNDiagram, e all'avvio lo piazza centrato davanti alla camera.
/// </summary>
[DisallowMultipleComponent]
public class BpmnSceneBuilder : MonoBehaviour
{
    [Header("Sorgente")]
    [Tooltip("Scarica il diagramma da apiBaseUrl all'avvio. Spento: il diagramma arriva dalla lista processi Prince (LoadFromXml).")]
    public bool loadFromApiOnStart = false;
    [Tooltip("Indirizzo del server raggiungibile dal Quest sulla stessa rete.")]
    public string apiBaseUrl = "http://192.168.1.55:3000";
    public string diagramId = "richiesta_ferie";
    [Tooltip("Opzionale: inviata nell'header X-Api-Key.")]
    public string apiKey = "";
    [Min(1)] public int requestTimeoutSeconds = 15;
    public string LoadError { get; private set; }
    [Header("Prefab")]
    public GameObject nodePrefab;

    [Header("Vista di lettura")]
    public bool readingLayout = true;
    [Min(0.02f)] public float labelGap = 0.08f;
    [Min(0.1f)] public float readingMaxHeight = 1.6f;
    [Tooltip("Centri di nodi che differiscono meno di questo (metri, prima del ridimensionamento) vengono allineati sulla stessa riga/colonna.")]
    [Min(0f)] public float alignTolerance = 0.03f;
    [Tooltip("Accorcia gli spazi orizzontali fra le colonne di nodi (linee piu' corte, diagramma meno largo). Mantiene l'ordine e non sovrappone i nodi.")]
    public bool compactColumns = true;
    [Tooltip("Spazio minimo fra due colonne di nodi, misurato sulle etichette, in metri prima del ridimensionamento finale.")]
    [Min(0f)] public float columnGap = 0.12f;

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

    [Tooltip("Vista di lettura: profondita' delle linee rispetto al centro dei nodi (+ = dietro). Piccola, cosi' le linee restano attaccate alle forme anche guardandole di lato.")]
    public float readingLineDepth = 0.01f;

    [Header("Grafico live: soglia")]
    [Tooltip("Mostra la linea rossa di soglia e segnala con un triangolo rosso i valori che la superano.")]
    public bool liveChartThresholdEnabled = true;
    [Tooltip("Valore soglia del grafico live, nella stessa unita' dei valori ricevuti.")]
    public float liveChartThreshold = 80f;

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

    [Tooltip("Badge e cornice colorata sui nodi che hanno commenti/documentazione.")]
    public bool highlightDocumented = true;
    public Color documentedAccent = new Color(1f, 0.55f, 0.1f);
    [Tooltip("Diametro del badge, in metri prima del ridimensionamento finale.")]
    public float documentedBadgeSize = 0.075f;
    [Tooltip("Spessore della cornice attorno ai task commentati, in metri prima del ridimensionamento finale.")]
    public float documentedFrame = 0.02f;

    [Header("Disposizione dei pannelli")]
    [Tooltip("Dispone diagramma, lista, grafico, comandi e documentazione su colonne affiancate senza sovrapposizioni (PanelLayout).")]
    public bool useSharedLayout = true;
    [Tooltip("Con la disposizione condivisa: larghezza massima del diagramma in metri, per lasciare spazio ai pannelli laterali.")]
    [Min(0.3f)] public float layoutMaxWidth = 2.0f;
    [Tooltip("Con la disposizione condivisa: altezza massima del diagramma in metri, per tenere i comandi sotto alla portata dello sguardo.")]
    [Min(0.2f)] public float layoutMaxHeight = 1.0f;

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
    Material runtimeAccentMaterial;
    BpmnDocumentationPanel documentationPanel;
    MaterialPropertyBlock propertyBlock;

    public IReadOnlyDictionary<string, GameObject> NodeObjects => nodeObjects;
    public BpmnDocument Document => document;
    public bool IsReady { get; private set; }
    public Transform DiagramRoot => diagramRoot;

    /// <summary>Lanciato dopo ogni ricostruzione completa e posizionamento del diagramma.</summary>
    public event System.Action DiagramBuilt;
    Vector3 initialScale = Vector3.one;

    void Awake() => initialScale = transform.localScale;

    public bool enableLiveChart = true;

    void Start()
    {
        if (enableLiveChart && GetComponent<LiveValueChart>() == null) gameObject.AddComponent<LiveValueChart>();
        if (loadFromApiOnStart) StartCoroutine(BuildAndPlace());
    }

    /// <summary>
    /// Costruisce il diagramma da un XML BPMN gia' in memoria (es. ricevuto da Prince/Camunda)
    /// e conserva la posizione del diagramma precedente. Posiziona davanti alla camera solo il primo.
    /// </summary>
    public bool LoadFromXml(string xml, string sourceLabel = null)
    {
        LoadError = null;
        if (nodePrefab == null)
        {
            LoadError = "nodePrefab non assegnato nel BPMN Viewer.";
            Debug.LogError("[BPMN] " + LoadError, this);
            return false;
        }

        BpmnDocument parsed;
        try { parsed = BpmnDocument.Parse(xml); }
        catch (System.Exception e)
        {
            LoadError = "XML BPMN non valido: " + e.Message;
            Debug.LogError("[BPMN] " + LoadError, this);
            return false;
        }

        bool alreadyPlaced = diagramRoot != null;
        document = parsed;
        transform.localScale = initialScale; // annulla lo zoom del navigatore sul diagramma precedente
        Rebuild();
        if (diagramRoot == null)
        {
            LoadError = "Il BPMN non contiene le coordinate del diagramma (BPMNDiagram).";
            Debug.LogError("[BPMN] " + LoadError, this);
            IsReady = false;
            return false;
        }

        if (useSharedLayout)
        {
            // Diagramma nuovo, dimensioni nuove: si ridispongono le colonne senza cambiare il riferimento.
            PanelLayout.Register(transform, PanelLayout.Column.Center, 0);
            PanelLayout.Arrange();
        }
        else if (!alreadyPlaced && placeInFrontOfCameraOnStart) PlaceInFrontOfCamera();
        IsReady = true;
        Debug.Log($"[BPMN] Caricato {sourceLabel ?? "diagramma"} da XML in memoria.", this);
        DiagramBuilt?.Invoke();
        return true;
    }

    IEnumerator BuildAndPlace()
    {
        yield return LoadAndBuild();

        if (diagramRoot == null) yield break;
        if (!placeInFrontOfCameraOnStart) { IsReady = true; yield break; }

        // Wait for the actual Link head pose, then place once in world space.
        var xrSettings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        bool waitForHead = xrSettings != null && xrSettings.Manager != null
            && xrSettings.Manager.activeLoader != null;
        if (diagramRoot != null && waitForHead) diagramRoot.gameObject.SetActive(false);

        int stableFrames = 0;
        while (stableFrames < Mathf.Max(1, trackingWarmupFrames))
        {
            bool poseValid = !waitForHead;
            if (waitForHead)
            {
                var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                poseValid = head.isValid
                    && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked)
                    && tracked
                    && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out var state)
                    && (state & (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation))
                        == (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation);
            }
            stableFrames = poseValid ? stableFrames + 1 : 0;
            yield return null;
        }

        PlaceInFrontOfCamera();
        if (diagramRoot != null) diagramRoot.gameObject.SetActive(true);
        IsReady = diagramRoot != null;
    }

    public IEnumerator LoadAndBuild()
    {
        if (nodePrefab == null)
        {
            Debug.LogError("[BPMN] nodePrefab non assegnato.", this);
            yield break;
        }

        LoadError = null;
        if (!System.Uri.TryCreate(apiBaseUrl, System.UriKind.Absolute, out var server)
            || (server.Scheme != "http" && server.Scheme != "https")
            || string.IsNullOrWhiteSpace(diagramId))
        {
            LoadError = "Imposta un URL HTTP/HTTPS e l'id del diagramma nel BPMN Viewer.";
            Debug.LogError("[BPMN] " + LoadError, this);
            yield break;
        }

        string url = apiBaseUrl.TrimEnd('/') + "/api/diagrams/"
            + System.Uri.EscapeDataString(diagramId.Trim()) + "/raw";
        string xml;
        // UnityWebRequest.Get uses DownloadHandlerBuffer: no disk cache or local file.
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.Max(1, requestTimeoutSeconds);
            request.SetRequestHeader("Accept", "application/xml");
            if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("X-Api-Key", apiKey);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                LoadError = $"API non raggiungibile o richiesta rifiutata (HTTP {request.responseCode}, {request.error}). Controlla server, Wi-Fi, id e API key.";
                Debug.LogError("[BPMN] " + LoadError, this);
                yield break;
            }
            xml = request.downloadHandler.text;
            Debug.Log($"[BPMN API] Ricevuto {diagramId}: HTTP {request.responseCode}, {request.downloadedBytes} byte, solo RAM.", this);
        }
        try
        {
            document = BpmnDocument.Parse(xml);
        }
        catch (System.Exception e)
        {
            LoadError = "XML BPMN non valido: " + e.Message;
            Debug.LogError("[BPMN] " + LoadError, this);
            yield break;
        }

        xml = null; // Release the raw payload; only the parsed model remains.
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
        if (!readingLayout && maxWidth > 0f && diagramWidth > maxWidth)
        {
            float shrink = maxWidth / diagramWidth;
            diagramRoot.localScale = Vector3.one * shrink;
            diagramWidth = maxWidth;
        }

        BuildNodes();
        if (readingLayout) ArrangeReadableNodes();
        if (highlightDocumented) DecorateDocumentedNodes();

        if (drawConnections) BuildConnections();
        if (showAnnotations) BuildAnnotations();

        float panelTop = 0f;
        foreach (var renderer in diagramRoot.GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = transform.InverseTransformPoint(renderer.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, sign)));
                panelTop = Mathf.Max(panelTop, point.y);
            }
        }
        PanelMoveBar.Attach(transform, panelTop, .86f, ResolveLineMaterial());
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
            if (readingLayout) obj.transform.localScale *= 0.55f;
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

    // Measure actual glyphs/backgrounds in diagram space, including long multiline names.
    Rect ReadableBounds(GameObject node)
    {
        Vector3 anchor = node.transform.localPosition;
        var result = new Rect(anchor.x - .06f, anchor.y - .06f, .12f, .12f);
        foreach (var renderer in node.GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 point = diagramRoot.InverseTransformPoint(renderer.transform.TransformPoint(bounds.center + offset));
                result.xMin = Mathf.Min(result.xMin, point.x); result.xMax = Mathf.Max(result.xMax, point.x);
                result.yMin = Mathf.Min(result.yMin, point.y); result.yMax = Mathf.Max(result.yMax, point.y);
            }
        }
        return result;
    }

    /// <summary>
    /// Raggruppa i nodi in colonne (centri quasi allineati in X) e riduce lo spazio fra colonne
    /// consecutive a columnGap, misurato sulle etichette reali. Accorcia soltanto: dove le colonne
    /// sono gia' vicine o si sovrappongono non sposta nulla, e l'ordine da sinistra a destra resta.
    /// </summary>
    void CompactColumns()
    {
        var nodes = new List<GameObject>(nodeObjects.Values);
        if (nodes.Count < 2) return;
        nodes.Sort((a, b) => a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));

        const float sameColumn = 0.05f;
        var columns = new List<List<GameObject>>();
        foreach (var node in nodes)
        {
            if (columns.Count > 0 && node.transform.localPosition.x
                - columns[columns.Count - 1][0].transform.localPosition.x <= sameColumn)
                columns[columns.Count - 1].Add(node);
            else columns.Add(new List<GameObject> { node });
        }

        float previousRight = float.NegativeInfinity, shift = 0f;
        foreach (var column in columns)
        {
            float left = float.MaxValue, right = float.MinValue;
            foreach (var node in column)
            {
                Rect box = ReadableBounds(node);
                left = Mathf.Min(left, box.xMin);
                right = Mathf.Max(right, box.xMax);
            }
            float gap = left + shift - previousRight;
            if (!float.IsInfinity(previousRight) && gap > columnGap) shift -= gap - columnGap;
            if (!Mathf.Approximately(shift, 0f))
                foreach (var node in column) node.transform.localPosition += Vector3.right * shift;
            previousRight = right + shift;
        }
    }

    /// <summary>
    /// Raggruppa i nodi in colonne (centri quasi allineati in X), da sinistra a destra.
    /// </summary>
    List<List<GameObject>> Columns()
    {
        var nodes = new List<GameObject>(nodeObjects.Values);
        nodes.Sort((a, b) => a.transform.localPosition.x.CompareTo(b.transform.localPosition.x));
        const float sameColumn = 0.05f;
        var columns = new List<List<GameObject>>();
        foreach (var node in nodes)
        {
            if (columns.Count > 0 && node.transform.localPosition.x
                - columns[columns.Count - 1][0].transform.localPosition.x <= sameColumn)
                columns[columns.Count - 1].Add(node);
            else columns.Add(new List<GameObject> { node });
        }
        return columns;
    }

    /// <summary>
    /// Se l'etichetta (o la forma) di un nodo urta quelle delle colonne precedenti sulla stessa
    /// fascia verticale, sposta a destra quella colonna e tutte le successive. Cosi' le righe del
    /// BPMN restano dritte: prima i nodi venivano spinti in basso e uscivano dalla loro riga.
    /// </summary>
    void SeparateColumns()
    {
        const float verticalTolerance = 0.005f;
        var placed = new List<Rect>();
        float push = 0f;
        foreach (var column in Columns())
        {
            if (push != 0f) foreach (var node in column) node.transform.localPosition += Vector3.right * push;
            var boxes = new List<Rect>();
            foreach (var node in column) boxes.Add(ReadableBounds(node));

            float need = 0f;
            foreach (Rect box in boxes)
                foreach (Rect prior in placed)
                {
                    if (box.yMax + verticalTolerance <= prior.yMin || box.yMin >= prior.yMax + verticalTolerance) continue;
                    need = Mathf.Max(need, prior.xMax + labelGap - box.xMin);
                }
            if (need > 0f)
            {
                foreach (var node in column) node.transform.localPosition += Vector3.right * need;
                for (int i = 0; i < boxes.Count; i++) { Rect r = boxes[i]; r.x += need; boxes[i] = r; }
                push += need;
            }
            placed.AddRange(boxes);
        }
    }

    /// <summary>Raggruppa per coordinata (x o y) i nodi entro la tolleranza, in ordine crescente.</summary>
    List<List<GameObject>> Groups(bool byX, float tolerance)
    {
        var nodes = new List<GameObject>(nodeObjects.Values);
        System.Func<GameObject, float> key = n => byX ? n.transform.localPosition.x : n.transform.localPosition.y;
        nodes.Sort((a, b) => key(a).CompareTo(key(b)));
        var groups = new List<List<GameObject>>();
        foreach (var node in nodes)
        {
            var last = groups.Count > 0 ? groups[groups.Count - 1] : null;
            if (last != null && key(node) - key(last[last.Count - 1]) <= tolerance) last.Add(node);
            else groups.Add(new List<GameObject> { node });
        }
        return groups;
    }

    /// <summary>
    /// Allinea al valore mediano i centri quasi uguali: nel BPMN rombi, eventi e task della stessa
    /// riga (o colonna) differiscono spesso di qualche pixel, e le linee ortogonali entrerebbero di
    /// sbieco o fuori dal vertice. Vale per tutti i processi.
    /// </summary>
    void SnapToGrid()
    {
        foreach (bool byX in new[] { false, true })
            foreach (var group in Groups(byX, alignTolerance))
            {
                if (group.Count < 2) continue;
                var values = new List<float>();
                foreach (var n in group) values.Add(byX ? n.transform.localPosition.x : n.transform.localPosition.y);
                values.Sort();
                float median = values[values.Count / 2];
                foreach (var n in group)
                {
                    Vector3 p = n.transform.localPosition;
                    if (byX) p.x = median; else p.y = median;
                    n.transform.localPosition = p;
                }
            }
    }

    /// <summary>
    /// Come SeparateColumns ma in verticale: se una riga urta (forme o etichette) le righe sopra,
    /// scende tutta la riga insieme a quelle sotto. Nessun nodo esce piu' dalla propria riga.
    /// </summary>
    void SeparateRows()
    {
        const float horizontalTolerance = 0.005f;
        var rows = Groups(false, 0.001f);
        rows.Reverse(); // dall'alto verso il basso
        var placed = new List<Rect>();
        float push = 0f;
        foreach (var row in rows)
        {
            if (push != 0f) foreach (var node in row) node.transform.localPosition -= Vector3.up * push;
            var boxes = new List<Rect>();
            foreach (var node in row) boxes.Add(ReadableBounds(node));
            float need = 0f;
            foreach (Rect box in boxes)
                foreach (Rect prior in placed)
                {
                    if (box.xMax + horizontalTolerance <= prior.xMin || box.xMin >= prior.xMax + horizontalTolerance) continue;
                    need = Mathf.Max(need, box.yMax + labelGap - prior.yMin);
                }
            if (need > 0f)
            {
                foreach (var node in row) node.transform.localPosition -= Vector3.up * need;
                for (int i = 0; i < boxes.Count; i++) { Rect r = boxes[i]; r.y -= need; boxes[i] = r; }
                push += need;
            }
            placed.AddRange(boxes);
        }
    }

    void ArrangeReadableNodes()
    {
        SnapToGrid();
        if (compactColumns) CompactColumns();
        // Prima le righe (urti fra righe diverse: si allarga in altezza), poi le colonne (urti nella
        // stessa riga: si allarga in larghezza). Spostare colonne a destra non crea nuovi urti.
        SeparateRows();
        SeparateColumns();
        var ordered = new List<GameObject>(nodeObjects.Values);
        var occupied = new List<Rect>();
        foreach (var node in ordered) occupied.Add(ReadableBounds(node));
        if (occupied.Count == 0) return;
        Rect total = occupied[0];
        foreach (Rect box in occupied) {
            total.xMin = Mathf.Min(total.xMin, box.xMin); total.xMax = Mathf.Max(total.xMax, box.xMax);
            total.yMin = Mathf.Min(total.yMin, box.yMin); total.yMax = Mathf.Max(total.yMax, box.yMax);
        }
        foreach (var node in ordered) node.transform.localPosition -= new Vector3(total.center.x, total.center.y, 0f);
        // Fit the completed layout uniformly, after measuring labels and resolving overlaps.
        float fit = Mathf.Min(1f, Mathf.Max(0.1f, readingMaxHeight) / Mathf.Max(total.height, 0.001f));
        if (maxWidth > 0f) fit = Mathf.Min(fit, maxWidth / Mathf.Max(total.width, 0.001f));
        if (useSharedLayout)
            fit = Mathf.Min(fit, layoutMaxWidth / Mathf.Max(total.width, 0.001f), layoutMaxHeight / Mathf.Max(total.height, 0.001f));
        diagramRoot.localScale = Vector3.one * fit;
        diagramWidth = total.width * fit;
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
                CreateFlowLabel(flow.Name, points, readingLayout);
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

        if (readingLayout && nodeObjects.TryGetValue(flow.SourceRef, out var from)
            && nodeObjects.TryGetValue(flow.TargetRef, out var to))
        {
            BpmnNode sourceNode = document.GetNode(flow.SourceRef);
            BpmnNode targetNode = document.GetNode(flow.TargetRef);
            bool fromGateway = sourceNode != null && sourceNode.Kind == BpmnNodeKind.Gateway;
            bool toGateway = targetNode != null && targetNode.Kind == BpmnNodeKind.Gateway;
            Branch branch = fromGateway ? ClassifyBranch(flow, from, to) : Branch.None;
            return RouteReadable(from, to, branch, toGateway);
        }
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

    /// <summary>Mezza larghezza e mezza altezza della forma del nodo (senza etichetta), in spazio del diagramma.</summary>
    static Vector2 HalfSize(GameObject node)
    {
        var filter = node.GetComponent<MeshFilter>();
        Vector3 scale = node.transform.localScale;
        if (filter == null || filter.sharedMesh == null) return new Vector2(.03f, .03f);
        Vector3 extents = filter.sharedMesh.bounds.extents;
        return new Vector2(Mathf.Abs(extents.x * scale.x), Mathf.Abs(extents.y * scale.y));
    }

    enum Branch { None, Yes, No }

    static readonly HashSet<string> NoWords = new HashSet<string>
        { "no", "ko", "errore", "error", "errori", "false", "falso", "else", "fail", "failed", "negativo", "non" };
    static readonly HashSet<string> YesWords = new HashSet<string>
        { "si", "sì", "yes", "ok", "true", "vero", "positivo" };

    /// <summary>
    /// I rombi sono degli if: ramo SI' = a destra, ramo NO = giu' (e poi a sinistra).
    /// Si decide dal nome del flusso; se manca: unica uscita = SI', altrimenti SI' solo per il
    /// ramo che prosegue dritto a destra nella stessa riga.
    /// </summary>
    Branch ClassifyBranch(BpmnFlow flow, GameObject from, GameObject to)
    {
        string name = (flow.Name ?? string.Empty).ToLowerInvariant();
        var words = System.Text.RegularExpressions.Regex.Split(name, @"[^\p{L}]+");
        foreach (var w in words) if (NoWords.Contains(w)) return Branch.No;
        foreach (var w in words) if (YesWords.Contains(w)) return Branch.Yes;

        int outgoing = 0;
        foreach (BpmnFlow f in document.Flows) if (f.SourceRef == flow.SourceRef) outgoing++;
        if (outgoing <= 1) return Branch.Yes;

        Vector3 a = from.transform.localPosition, b = to.transform.localPosition;
        return Mathf.Abs(b.y - a.y) <= SameRowTolerance && b.x > a.x ? Branch.Yes : Branch.No;
    }

    const float SameRowTolerance = 0.01f;

    /// <summary>
    /// Percorso ortogonale da bordo a bordo, alla stessa profondita' dei nodi (niente parallasse).
    /// Rombi: SI' esce dal vertice destro; NO esce dal vertice basso (alto solo se il bersaglio sta
    /// sopra), scende fino alla riga del bersaglio e ci entra dal fianco rivolto verso il rombo.
    /// Ingresso in un rombo da un'altra riga: dal vertice basso/alto. La freccia termina sul bordo.
    /// </summary>
    List<Vector3> RouteReadable(GameObject from, GameObject to, Branch branch, bool toGateway)
    {
        Vector3 a = from.transform.localPosition, b = to.transform.localPosition;
        Vector2 ha = HalfSize(from), hb = HalfSize(to);
        float z = readingLineDepth;
        float dx = b.x - a.x, dy = b.y - a.y;
        float sx = dx >= 0f ? 1f : -1f, sy = dy >= 0f ? 1f : -1f;
        bool sameRow = Mathf.Abs(dy) <= SameRowTolerance;
        bool overlappingColumns = Mathf.Abs(dx) < ha.x + hb.x;
        var points = new List<Vector3>(5);
        Vector3 P(float x, float y) => new Vector3(x, y, z);

        // ---- Rombo, ramo SI': vertice destro ----
        if (branch == Branch.Yes && dx > 0f && !overlappingColumns)
        {
            Vector3 exit = P(a.x + ha.x, a.y);
            points.Add(exit);
            if (sameRow) { points.Add(P(b.x - hb.x, a.y)); return points; }
            if (toGateway)
            {
                points.Add(P(b.x, a.y));
                points.Add(P(b.x, b.y - sy * hb.y));
                return points;
            }
            float mid = (exit.x + b.x - hb.x) * 0.5f;
            points.Add(P(mid, a.y));
            points.Add(P(mid, b.y));
            points.Add(P(b.x - hb.x, b.y));
            return points;
        }

        // ---- Rombo, ramo NO (o SI' che non puo' andare a destra): vertice basso ----
        if (branch != Branch.None)
        {
            float vy = !sameRow && dy > 0f ? 1f : -1f; // in alto solo se il bersaglio sta sopra
            Vector3 exit = P(a.x, a.y + vy * ha.y);
            points.Add(exit);

            if (sameRow)
            {
                // Bersaglio nella stessa riga (es. ritorno indietro): giu', di lato, su nel suo fondo.
                float lane = a.y + vy * (Mathf.Max(ha.y, hb.y) + 0.08f);
                points.Add(P(a.x, lane));
                points.Add(P(b.x, lane));
                points.Add(P(b.x, b.y + vy * hb.y));
                return points;
            }
            if (overlappingColumns || toGateway)
            {
                // Sotto (o sopra) il rombo: dritto nel bordo alto/basso del bersaglio.
                Vector3 end = P(b.x, b.y - vy * hb.y);
                if (Mathf.Abs(a.x - b.x) > 0.002f)
                {
                    float midY = (exit.y + end.y) * 0.5f;
                    points.Add(P(a.x, midY));
                    points.Add(P(b.x, midY));
                }
                points.Add(end);
                return points;
            }
            // Scende fino alla riga del bersaglio, poi entra dal fianco rivolto verso il rombo.
            points.Add(P(a.x, b.y));
            points.Add(P(b.x - sx * hb.x, b.y));
            return points;
        }

        // ---- Sorgente non rombo ----
        if (sameRow)
        {
            points.Add(P(a.x + sx * ha.x, a.y));
            points.Add(P(b.x - sx * hb.x, a.y));
            return points;
        }

        if (overlappingColumns)
        {
            Vector3 start = P(a.x, a.y + sy * ha.y);
            Vector3 end = P(b.x, b.y - sy * hb.y);
            points.Add(start);
            if (Mathf.Abs(a.x - b.x) > 0.002f)
            {
                float midY = (start.y + end.y) * 0.5f;
                points.Add(P(a.x, midY));
                points.Add(P(b.x, midY));
            }
            points.Add(end);
            return points;
        }

        if (toGateway)
        {
            // Ingresso in un rombo da un'altra riga: sulla riga della sorgente fino sotto/sopra il
            // rombo, poi dritto nel vertice. Non si sovrappone alla linea che entra di lato.
            points.Add(P(a.x + sx * ha.x, a.y));
            points.Add(P(b.x, a.y));
            points.Add(P(b.x, b.y - sy * hb.y));
            return points;
        }

        Vector3 from0 = P(a.x + sx * ha.x, a.y);
        Vector3 entry = P(b.x - sx * hb.x, b.y);
        float middle = (from0.x + entry.x) * 0.5f;
        points.Add(from0);
        points.Add(P(middle, a.y));
        points.Add(P(middle, b.y));
        points.Add(entry);
        return points;
    }

    void CreateFlowLabel(string text, List<Vector3> points, bool nearStart = false)
    {
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogWarning("[BPMN] Nessun font TMP di default: etichette dei flussi saltate.", this);
            return;
        }

        var go = new GameObject("FlowLabel");
        go.transform.SetParent(diagramRoot, false);
        // In TMP world-space una riga con fontSize 1 e' alta circa 0,1 unita' locali: scala = altezza x 10.
        float scale = flowLabelHeight * 10f;
        Vector3 position = MidPoint(points);
        var alignment = TextAlignmentOptions.Center;
        if (nearStart && points.Count >= 2)
        {
            // Come nel modeler web: l'etichetta ("ok", "no"...) sta vicino all'uscita, accanto al primo tratto.
            Vector3 a = points[0], b = points[1];
            bool vertical = Mathf.Abs(b.y - a.y) > Mathf.Abs(b.x - a.x);
            if (vertical)
            {
                position = Vector3.Lerp(a, b, 0.5f) + new Vector3(flowLabelHeight * 0.4f, 0f, 0f);
                alignment = TextAlignmentOptions.Left;
            }
            else
            {
                float along = Mathf.Min(Vector3.Distance(a, b) * 0.5f, 0.12f);
                position = a + (b - a).normalized * along + new Vector3(0f, flowLabelHeight * 0.8f, 0f);
            }
        }
        go.transform.localPosition = position + new Vector3(0f, 0f, -0.01f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;

        var label = go.AddComponent<TextMeshPro>();
        label.text = text;
        label.richText = false;
        label.fontSize = 1f;
        label.fontStyle = FontStyles.Bold;
        label.color = flowLabelColor;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.sizeDelta = new Vector2(0.4f / scale, flowLabelHeight * 1.5f / scale);
        if (alignment == TextAlignmentOptions.Left)
            label.rectTransform.pivot = new Vector2(0f, 0.5f);

        // Testo scuro su riquadro bianco, come le etichette dei nodi: leggibile sia sullo sfondo
        // chiaro dell'Editor sia sul passthrough del visore.
        label.color = nodeLabelColor;
        label.ForceMeshUpdate();
        Bounds textBox = label.textBounds;
        if (!nodeLabelBackground || !IsUsableSize(textBox.size.x * scale) || !IsUsableSize(textBox.size.y * scale)) return;
        var background = new GameObject("FlowLabelBackground");
        background.transform.SetParent(diagramRoot, false);
        background.transform.localPosition = go.transform.localPosition + textBox.center * scale + new Vector3(0f, 0f, 0.003f);
        background.transform.localScale = new Vector3(
            textBox.size.x * scale + nodeLabelPadding.x * 2f, textBox.size.y * scale + nodeLabelPadding.y * 2f, 1f);
        background.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();
        var renderer = background.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ResolveLabelBackgroundMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>Piazza il diagramma davanti alla camera, in piedi e rivolto verso di te.</summary>
    [ContextMenu("Piazza davanti alla camera")]
    public void PlaceInFrontOfCamera()
    {
        if (useSharedLayout)
        {
            PanelLayout.Register(transform, PanelLayout.Column.Center, 0);
            PanelLayout.Recenter();
            return;
        }

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

        float d = !readingLayout && fitDistanceToDiagram
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

        // Destroy e' differito a fine frame: si rinomina subito, cosi' chi cerca "Diagram"
        // trova solo quello nuovo.
        diagramRoot.name = "Diagram (rimosso)";
        diagramRoot.gameObject.SetActive(false);
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

        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.richText = false;
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

    /// <summary>
    /// Rende riconoscibili i nodi con commenti: badge arancione con "!" in alto a destra e, sui task,
    /// una cornice dello stesso colore. Va chiamato dopo il layout, quando le posizioni sono definitive.
    /// </summary>
    void DecorateDocumentedNodes()
    {
        if (runtimeAccentMaterial == null)
            runtimeAccentMaterial = CreateUnlitMaterial("BpmnDocumentedAccent", documentedAccent);
        if (runtimeAccentMaterial == null) return;

        float rootScale = diagramRoot.lossyScale.x;
        Quaternion facing = diagramRoot.rotation;
        Vector3 back = diagramRoot.forward; // +Z locale = lontano da chi guarda

        foreach (BpmnNode node in document.Nodes)
        {
            if (!node.HasDocumentation || !nodeObjects.TryGetValue(node.Id, out var obj)) continue;
            var filter = obj.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Transform t = obj.transform;
            Bounds b = filter.sharedMesh.bounds;
            Vector3 size = Vector3.Scale(b.size, t.lossyScale);
            Vector3 center = t.TransformPoint(b.center);
            Vector3 corner = t.TransformPoint(b.center + new Vector3(b.extents.x, b.extents.y, -b.extents.z));

            if (node.Kind == BpmnNodeKind.Task)
            {
                // Quad appena dietro al box: ne restano visibili solo i bordi.
                float border = documentedFrame * rootScale;
                var frame = NewDecoration("CommentFrame", BpmnNodeShapes.Quad());
                frame.transform.SetPositionAndRotation(center + back * (size.z * 0.5f + 0.003f * rootScale), facing);
                frame.transform.localScale = new Vector3(size.x + border * 2f, size.y + border * 2f, 1f);
                frame.transform.SetParent(t, true);
            }

            float diameter = documentedBadgeSize * rootScale;
            var badge = NewDecoration("CommentBadge", BpmnNodeShapes.Sphere());
            badge.transform.SetPositionAndRotation(corner - back * diameter * 0.5f, facing);
            badge.transform.localScale = Vector3.one * diameter;

            if (TMP_Settings.defaultFontAsset != null)
            {
                var mark = new GameObject("Mark");
                mark.transform.SetPositionAndRotation(badge.transform.position - back * diameter * 0.55f, facing);
                mark.transform.localScale = Vector3.one * (diameter * 6f); // riga TMP ~ 0.1 unita' locali
                var text = mark.AddComponent<TextMeshPro>();
                text.text = "!";
                text.richText = false;
                text.fontSize = 1f;
                text.fontStyle = FontStyles.Bold;
                text.color = Color.white;
                text.alignment = TextAlignmentOptions.Center;
                text.rectTransform.sizeDelta = new Vector2(2f, 2f);
                text.ForceMeshUpdate();
                mark.transform.SetParent(badge.transform, true);
            }
            badge.transform.SetParent(t, true);
        }
    }

    GameObject NewDecoration(string name, Mesh mesh)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = runtimeAccentMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
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
