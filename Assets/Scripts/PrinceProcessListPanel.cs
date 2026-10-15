using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Pannello 3D con la lista dei processi Prince. All'avvio fa il login in background con le
/// credenziali impostate qui, chiama /princeBe/core/process e mostra un bottone per processo.
/// Il click seleziona il processo e lancia ProcessSelected: il caricamento del BPMN arrivera' dopo.
/// </summary>
[DisallowMultipleComponent]
public partial class PrinceProcessListPanel : MonoBehaviour
{
    const float Pad = 0.025f;
    const float TitleBlock = 0.045f;
    const float StatusBlock = 0.035f;
    const float RowHeight = 0.095f;
    const float RowGap = 0.010f;
    const float FooterHeight = 0.060f;
    const float ButtonGap = 0.012f;

    // SOLO SVILUPPO: credenziali di test. Finiscono nel codice e nell'APK: da sostituire con un login vero.
    public const string DevUsername = "luca";
    public const string DevPassword = "luca";

    /// <summary>Se in scena non c'e' nessuna lista processi, ne crea una automaticamente al Play.</summary>
    public static bool AutoCreate = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (!AutoCreate || FindAnyObjectByType<PrinceProcessListPanel>() != null) return;
        new GameObject("Prince Process List (auto)").AddComponent<PrinceProcessListPanel>();
        Debug.Log("[Prince] Lista processi creata automaticamente.");
    }

    [Header("Accesso in background (solo sviluppo)")]
    [Tooltip("Credenziali di test. Non usare credenziali reali: finiscono nella scena e nell'APK.")]
    public string username = DevUsername;
    public string password = DevPassword;
    public bool loadOnStart = true;

    [Header("Lista")]
    [Min(1)] public int rowsPerPage = 5;
    public float width = 0.62f;

    [Header("Posizione")]
    public float distance = 1.35f;
    [Tooltip("Gradi rispetto alla direzione di sguardo: 0 = dritto davanti, negativo = a sinistra.")]
    public float yawOffset = -65f;
    [Tooltip("Metri rispetto all'altezza degli occhi. Negativo = piu' in basso.")]
    public float verticalOffset = -0.05f;
    [Tooltip("Frame di attesa prima di posizionare: all'avvio il tracking XR non ha ancora una posa valida.")]
    public int trackingWarmupFrames = 10;
    [Tooltip("Se il tracking non arriva entro questi secondi (es. Editor senza visore) il pannello compare comunque.")]
    public float trackingTimeoutSeconds = 3f;

    [Header("Aspetto")]
    [Tooltip("Opzionale: materiale URP Unlit da clonare. Se vuoto usa il lineMaterial del BPMN Viewer, poi lo shader URP Unlit.")]
    public Material baseMaterial;
    public Color backgroundColor = new Color(0.025f, 0.04f, 0.075f);
    public Color rowColor = new Color(0.08f, 0.19f, 0.31f);
    public Color rowHoverColor = new Color(0.08f, 0.43f, 0.58f);
    public Color rowSelectedColor = new Color(0.16f, 0.55f, 0.32f);
    public Color detailsColor = new Color(0.72f, 0.78f, 0.86f);
    public Color errorColor = new Color(1f, 0.55f, 0.5f);
    public Color camundaActiveColor = new Color(0.45f, 0.9f, 0.55f);
    public Color camundaSuspendedColor = new Color(1f, 0.72f, 0.3f);
    public Color camundaMissingColor = new Color(0.55f, 0.6f, 0.68f);



    [Header("Diagramma")]
    [Tooltip("Al click carica il BPMN del processo nel BPMN Viewer della scena.")]
    public bool openDiagramOnSelect = true;
    [Tooltip("Dopo l'apertura del diagramma il pannello si sposta di lato di questi gradi (0 = resta dov'e').")]
    public float asideYawAfterOpen = -65f;

    [Header("Eventi")]
    [Tooltip("Riceve l'id del record del processo selezionato.")]
    public UnityEvent<string> onProcessSelected = new UnityEvent<string>();

    public event Action<PrinceProcess> ProcessSelected;
    public IReadOnlyList<PrinceProcess> Processes => processes;
    public PrinceProcess Selected { get; private set; }
    public bool IsLoading => loading;

    class Row
    {
        public GameObject go;
        public MeshRenderer face;
        public TMP_Text name, details, camunda;
        public PrinceProcess process;
        public bool hovered;
    }

    readonly List<PrinceProcess> processes = new List<PrinceProcess>();
    readonly List<PrinceProcess> filtered = new List<PrinceProcess>();
    readonly List<Row> rows = new List<Row>();
    readonly List<Material> materials = new List<Material>();
    int page;
    bool loading, openingDiagram;
    TMP_Text status;
    TMP_Text processCountTitle;
    Material backgroundMat, rowMat, rowHoverMat, rowSelectedMat;
    Camera viewer;

    bool HasCredentials => !string.IsNullOrWhiteSpace(username) && !string.IsNullOrEmpty(password);
    int PageCount => Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)rowsPerPage));

    IEnumerator Start()
    {
        viewer = Camera.main;
        if (TMP_Settings.defaultFontAsset == null)
            Debug.LogWarning("[Prince] Nessun font TMP di default: i testi della lista non si vedranno.", this);

        BuildPanel();
        SetVisible(false);
        yield return WaitForTracking();
        Recall();
        SetVisible(true);

        if (loadOnStart) Refresh();
        else SetStatus("Premi Aggiorna per caricare i processi");
    }

    /// <summary>Rifa il login se serve e ricarica la lista dal server.</summary>
    [ContextMenu("Aggiorna lista")]
    public void Refresh()
    {
        if (!loading && isActiveAndEnabled) StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        loading = true;
        try
        {
            if (!PrinceLogin.IsAuthenticated)
            {
                yield return LoginRoutine();
                if (!PrinceLogin.IsAuthenticated) yield break;
            }

            SetStatus("Caricamento processi...");
            List<PrinceProcess> result = null;
            string error = null;
            yield return PrinceProcesses.FetchAll(r => result = r, e => error = e);

            // Token rifiutato (GetApi fa logout sul 401): un nuovo login e un solo tentativo in piu'.
            if (error != null && !PrinceLogin.IsAuthenticated && HasCredentials)
            {
                yield return LoginRoutine();
                if (!PrinceLogin.IsAuthenticated) yield break;
                error = null;
                yield return PrinceProcesses.FetchAll(r => result = r, e => error = e);
            }

            if (error != null) { SetStatus(error, true); yield break; }

            // Seconda chiamata: le definizioni pubblicate su Camunda (processi non DRAFT/DEPRECATED).
            SetStatus("Caricamento definizioni Camunda...");
            List<PrinceProcessDefinition> definitions = null;
            string camundaError = null;
            yield return PrinceProcesses.FetchDefinitions(d => definitions = d, e => camundaError = e);
            // Keep Prince records and individual Camunda definitions as separate list entries.
            if (definitions != null) PrinceProcesses.Link(result, definitions);
            var allPrince = new List<PrinceProcess>(result);
            result.RemoveAll(p => string.Equals(p.status?.Trim(), "DEPRECATED", StringComparison.OrdinalIgnoreCase));
            if (definitions != null)
            {
                var camundaRows = PrinceProcesses.FromDefinitions(definitions);
                foreach (var row in camundaRows)
                {
                    var linked = allPrince.Find(p => p.id == row.Latest.processLinked);
                    if (linked != null) row.user = linked.user;
                }
                result.AddRange(camundaRows);
            }
            processes.Clear();
            processes.AddRange(result);
            ApplyFilters(false);

            if (Selected != null) Selected = processes.Find(p => p.id == Selected.id);
            page = 0;
            ShowPage();
            Debug.Log($"[Prince] {processes.Count} processi caricati, "
                + (definitions != null ? $"{definitions.Count} definizioni Camunda." : "definizioni Camunda non disponibili."), this);
            if (camundaError != null) SetStatus("Lista caricata, ma Camunda non risponde: " + camundaError, true);
        }
        finally { loading = false; }
    }

    IEnumerator LoginRoutine()
    {
        if (!HasCredentials)
        {
            SetStatus("Imposta username e password nel componente Prince Process List", true);
            yield break;
        }
        SetStatus("Accesso a Prince...");
        string message = null;
        yield return PrinceLogin.Login(username, password, (ok, m) => message = m);
        if (!PrinceLogin.IsAuthenticated) SetStatus(message ?? "Accesso non riuscito", true);
    }

    public void NextPage() { if (page < PageCount - 1) { page++; ShowPage(); } }
    public void PreviousPage() { if (page > 0) { page--; ShowPage(); } }

    void ShowPage()
    {
        page = Mathf.Clamp(page, 0, PageCount - 1);
        for (int i = 0; i < rows.Count; i++)
        {
            int index = page * rowsPerPage + i;
            Row row = rows[i];
            bool used = index < filtered.Count;
            row.go.SetActive(used);
            row.hovered = false;
            row.process = used ? filtered[index] : null;
            if (!used) continue;
            row.name.text = row.process.DisplayName;
            row.details.text = Details(row.process);
            CamundaLine(row.process, out string camundaText, out Color camundaColor);
            row.camunda.text = camundaText;
            row.camunda.color = camundaColor;
            Paint(row);
        }

        if (processCountTitle != null) processCountTitle.text = $"Processi: {filtered.Count} / {processes.Count}";
        if (filtered.Count == 0) SetStatus("Nessun processo corrisponde ai filtri");
        else SetStatus(StatusLine());
    }

    string StatusLine()
    {
        string line = filtered.Count == 1 ? "1 processo" : filtered.Count + " processi";
        if (PageCount > 1) line += $"  |  pagina {page + 1}/{PageCount}";
        if (Selected != null) line += "  |  selezionato: " + Selected.DisplayName;
        return line;
    }

    static string Details(PrinceProcess p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.status)) parts.Add(p.status);
        if (p.version > 0) parts.Add("v" + p.version);
        if (!string.IsNullOrWhiteSpace(p.user)) parts.Add(p.user);
        if (!string.IsNullOrWhiteSpace(p.uploaded)) parts.Add(p.UploadedShort);
        return string.Join("  |  ", parts);
    }

    void CamundaLine(PrinceProcess p, out string text, out Color color)
    {
        var d = p.Latest;
        if (d == null)
        {
            text = p.IsDraftOrDeprecated ? "Non pubblicato su Camunda  |  apre il file Prince" : "Nessuna definizione Camunda";
            color = camundaMissingColor;
            return;
        }
        var parts = new List<string> { "Camunda v" + d.version };
        if (!string.IsNullOrWhiteSpace(d.versionTag)) parts.Add(d.versionTag);
        parts.Add(d.suspended ? "SOSPESO" : "ATTIVO");
        if (!d.onCamunda) parts.Add("non su Camunda");
        if (p.definitions.Count > 1) parts.Add(p.definitions.Count + " versioni");
        text = string.Join("  |  ", parts);
        color = d.suspended || !d.onCamunda ? camundaSuspendedColor : camundaActiveColor;
    }

    void Select(Row row)
    {
        if (row.process == null) return;
        Selected = row.process;
        foreach (var r in rows) Paint(r);
        SetStatus(StatusLine());
        Debug.Log($"[Prince] Processo selezionato: {Selected.DisplayName} (id {Selected.id}, file {Selected.filename}"
            + (Selected.Latest != null ? $", Camunda {Selected.Latest.id})" : ")"), this);
        ProcessSelected?.Invoke(Selected);
        onProcessSelected?.Invoke(Selected.id);
        if (openDiagramOnSelect) StartCoroutine(OpenDiagram(Selected));
    }

    /// <summary>
    /// Scarica l'XML BPMN e lo apre nel BPMN Viewer. Processi pubblicati: ultima versione da Camunda.
    /// Bozze, deprecati o senza definizione: il file salvato su Prince (getContentFile).
    /// Se la sorgente scelta fallisce si prova l'altra, quando disponibile.
    /// </summary>
    IEnumerator OpenDiagram(PrinceProcess process)
    {
        if (openingDiagram) yield break;
        var builder = FindAnyObjectByType<BpmnSceneBuilder>();
        if (builder == null) { SetStatus("Nessun BPMN Viewer nella scena", true); yield break; }

        var definition = process.Latest;
        bool hasFile = !string.IsNullOrWhiteSpace(process.path) && !string.IsNullOrWhiteSpace(process.filename);
        bool useCamunda = definition != null && (!process.IsDraftOrDeprecated || !hasFile);
        if (definition == null && !hasFile)
        {
            SetStatus($"{process.DisplayName}: nessun diagramma disponibile", true);
            yield break;
        }

        openingDiagram = true;
        try
        {
            string xml = null, error = null, source;
            if (useCamunda)
            {
                source = $"Camunda v{definition.version}";
                SetStatus($"Apertura {process.DisplayName} ({source})...");
                yield return PrinceProcesses.FetchDefinitionXml(definition.id, x => xml = x, e => error = e);
                if (xml == null && hasFile)
                {
                    Debug.LogWarning($"[Prince] Camunda non ha restituito il BPMN ({error}): provo il file Prince.", this);
                    source = "file Prince"; error = null;
                    yield return PrinceProcesses.FetchContentFile(process.path, process.filename, x => xml = x, e => error = e);
                }
            }
            else
            {
                source = "file Prince";
                SetStatus($"Apertura {process.DisplayName} ({source})...");
                yield return PrinceProcesses.FetchContentFile(process.path, process.filename, x => xml = x, e => error = e);
                if (xml == null && definition != null)
                {
                    Debug.LogWarning($"[Prince] File Prince non disponibile ({error}): provo Camunda.", this);
                    source = $"Camunda v{definition.version}"; error = null;
                    yield return PrinceProcesses.FetchDefinitionXml(definition.id, x => xml = x, e => error = e);
                }
            }
            if (xml == null) { SetStatus("Diagramma non scaricato: " + (error ?? "risposta vuota"), true); yield break; }

            bool ok = builder.LoadFromXml(xml, $"{process.DisplayName} ({source})");
            xml = null; // il modello e' gia' stato costruito: niente copie del sorgente in memoria
            if (!ok) { SetStatus(builder.LoadError ?? "Diagramma non valido", true); yield break; }

            SetStatus($"Aperto: {process.DisplayName} ({source})");
            // Keep the list where the user placed it when opening a process.
        }
        finally { openingDiagram = false; }
    }

    void Paint(Row row)
    {
        bool selected = row.process != null && Selected != null && row.process.id == Selected.id;
        row.face.sharedMaterial = selected ? rowSelectedMat : row.hovered ? rowHoverMat : rowMat;
    }

    /// <summary>Riporta il pannello davanti all'utente, spostato di yawOffset gradi.</summary>
    [ContextMenu("Richiama davanti alla camera")]
    public void Recall() => PlaceAt(yawOffset);

    void PlaceAt(float yaw)
    {
        if (viewer == null) viewer = Camera.main;
        if (viewer == null) return;
        Transform cam = viewer.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * forward;
        transform.SetPositionAndRotation(
            cam.position + direction * distance + Vector3.up * verticalOffset,
            Quaternion.LookRotation(direction, Vector3.up));
    }

    IEnumerator WaitForTracking()
    {
        var xrSettings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        bool waitForHead = xrSettings != null && xrSettings.Manager != null && xrSettings.Manager.activeLoader != null;
        int stableFrames = 0;
        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, trackingTimeoutSeconds);
        while (stableFrames < Mathf.Max(1, trackingWarmupFrames))
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[Prince] Tracking della testa non disponibile: pannello posizionato con la camera attuale.", this);
                yield break;
            }
            bool poseValid = !waitForHead;
            if (waitForHead)
            {
                var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                poseValid = head.isValid
                    && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked;
            }
            stableFrames = poseValid ? stableFrames + 1 : 0;
            yield return null;
        }
    }

    void SetStatus(string text, bool isError = false)
    {
        if (status == null) return;
        status.text = text;
        status.color = isError ? errorColor : detailsColor;
        if (isError) Debug.LogWarning("[Prince] " + text, this);
    }

    void SetVisible(bool visible)
    {
        foreach (Transform child in transform) child.gameObject.SetActive(visible);
        if (visible && filterKeyboard != null) filterKeyboard.SetActive(editing >= 0);
        if (visible) ShowPage(); // riapplica le righe vuote nascoste
    }

    // ---------- Costruzione del pannello ----------

    void BuildPanel()
    {
        backgroundMat = MakeMaterial("Prince List Background", backgroundColor);
        rowMat = MakeMaterial("Prince List Row", rowColor);
        rowHoverMat = MakeMaterial("Prince List Row Hover", rowHoverColor);
        rowSelectedMat = MakeMaterial("Prince List Row Selected", rowSelectedColor);

        float inner = width - Pad * 2f;
        float height = Pad * 2f + TitleBlock + StatusBlock + FilterHeight + rowsPerPage * (RowHeight + RowGap) + FooterHeight;
        float top = height * 0.5f;
        PanelMoveBar.Attach(transform, top, width, rowMat);

        Quad(transform, "Background", Vector3.zero, new Vector2(width, height), backgroundMat);

        processCountTitle = Text(transform, "Processi: --", new Vector3(0f, top - Pad - TitleBlock * 0.5f, -0.006f),
            new Vector2(inner, TitleBlock), 0.032f);
        processCountTitle.fontStyle = FontStyles.Bold;

        status = Text(transform, "In attesa del tracking...",
            new Vector3(0f, top - Pad - TitleBlock - StatusBlock * 0.5f, -0.006f),
            new Vector2(inner, StatusBlock), 0.020f);
        status.color = detailsColor;

        BuildFilters(top - Pad - TitleBlock - StatusBlock, inner, -top);
        float firstRowY = top - Pad - TitleBlock - StatusBlock - FilterHeight - RowHeight * 0.5f;
        for (int i = 0; i < rowsPerPage; i++)
            rows.Add(BuildRow(new Vector3(0f, firstRowY - i * (RowHeight + RowGap), -0.004f), inner));

        float buttonWidth = (inner - ButtonGap * 3f) / 4f;
        float footerY = -top + Pad + FooterHeight * 0.5f;
        string[] captions = { "< Prec.", "Aggiorna", "Richiama", "Succ. >" };
        UnityAction[] actions = { PreviousPage, Refresh, Recall, NextPage };
        for (int i = 0; i < captions.Length; i++)
        {
            float x = -inner * 0.5f + buttonWidth * 0.5f + i * (buttonWidth + ButtonGap);
            Button(new Vector3(x, footerY, -0.004f), new Vector2(buttonWidth, FooterHeight * 0.85f), captions[i], actions[i]);
        }
    }

    Row BuildRow(Vector3 position, float inner)
    {
        var row = new Row { go = new GameObject("Process Row") };
        row.go.transform.SetParent(transform, false);
        row.go.transform.localPosition = position;

        row.face = Quad(row.go.transform, "Face", Vector3.zero, new Vector2(inner, RowHeight), rowMat);
        var box = row.go.AddComponent<BoxCollider>();
        box.size = new Vector3(inner, RowHeight, 0.015f);

        float textWidth = inner - 0.04f;
        row.name = Text(row.go.transform, "", new Vector3(0f, 0.026f, -0.006f), new Vector2(textWidth, 0.032f), 0.024f);
        row.name.alignment = TextAlignmentOptions.Left;
        row.name.fontStyle = FontStyles.Bold;
        row.details = Text(row.go.transform, "", new Vector3(0f, -0.002f, -0.006f), new Vector2(textWidth, 0.024f), 0.017f);
        row.details.alignment = TextAlignmentOptions.Left;
        row.details.color = detailsColor;
        row.camunda = Text(row.go.transform, "", new Vector3(0f, -0.027f, -0.006f), new Vector2(textWidth, 0.024f), 0.017f);
        row.camunda.alignment = TextAlignmentOptions.Left;

        var interactable = row.go.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(_ => Select(row));
        interactable.hoverEntered.AddListener(_ => { row.hovered = true; Paint(row); });
        interactable.hoverExited.AddListener(_ => { row.hovered = false; Paint(row); });
        return row;
    }

    void Button(Vector3 position, Vector2 size, string caption, UnityAction action)
    {
        var go = new GameObject(caption);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = position;
        var face = Quad(go.transform, "Face", Vector3.zero, size, rowMat);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, 0.015f);
        Text(go.transform, caption, new Vector3(0f, 0f, -0.006f), size * 0.92f, 0.022f);
        var interactable = go.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(_ => action());
        interactable.hoverEntered.AddListener(_ => face.sharedMaterial = rowHoverMat);
        interactable.hoverExited.AddListener(_ => face.sharedMaterial = rowMat);
    }

    Material MakeMaterial(string name, Color color)
    {
        Material source = baseMaterial;
        if (source == null)
        {
            var builder = FindAnyObjectByType<BpmnSceneBuilder>();
            if (builder != null) source = builder.lineMaterial;
        }
        Material material = source != null ? new Material(source)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        material.name = name;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        materials.Add(material);
        return material;
    }

    static MeshRenderer Quad(Transform parent, string name, Vector3 position, Vector2 size, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    static TMP_Text Text(Transform parent, string caption, Vector3 position, Vector2 size, float height)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        // In TMP world-space una riga con fontSize 1 e' alta circa un decimo di unita' locale.
        float scale = height * 10f;
        go.transform.localScale = Vector3.one * scale;
        var text = go.AddComponent<TextMeshPro>();
        text.text = caption;
        text.richText = false; // i nomi arrivano dal server: niente tag interpretati
        text.fontSize = 1f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.rectTransform.sizeDelta = size / scale;
        return text;
    }

    void OnDestroy()
    {
        foreach (var material in materials)
            if (material != null) Destroy(material);
    }
}
