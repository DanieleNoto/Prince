using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Pannello di documentazione: si costruisce da solo a runtime, compare davanti alla camera
/// quando si clicca un nodo e si chiude con il bottone in alto a destra.
/// </summary>
public class BpmnDocumentationPanel : MonoBehaviour
{
    const float Padding = 0.028f;
    const float TitleHeight = 0.040f;
    const float BodyHeight = 0.030f;
    const float CloseSize = 0.05f;

    public float width = 0.58f;
    public float distanceFromCamera = 0.85f;
    public Color backgroundColor = new Color(0.98f, 0.98f, 0.98f);
    public Color textColor = Color.black;
    public Color closeColor = new Color(0.86f, 0.33f, 0.33f);

    Transform background;
    Transform closeButton;
    TMP_Text title;
    TMP_Text body;
    Material panelMaterial;
    Material closeMaterial;

    public bool IsOpen => gameObject.activeSelf;

    public static BpmnDocumentationPanel Create()
    {
        var go = new GameObject("BPMN Documentation Panel");
        var panel = go.AddComponent<BpmnDocumentationPanel>();
        panel.Build();
        go.SetActive(false);
        return panel;
    }

    void Build()
    {
        panelMaterial = CreateUnlit("BpmnPanel", backgroundColor);
        closeMaterial = CreateUnlit("BpmnPanelClose", closeColor);

        background = CreateQuad("Background", transform, panelMaterial);

        title = CreateText("Title", transform, TitleHeight, FontStyles.Bold);
        body = CreateText("Body", transform, BodyHeight, FontStyles.Normal);

        closeButton = CreateQuad("CloseButton", transform, closeMaterial);
        closeButton.localScale = new Vector3(CloseSize, CloseSize, CloseSize);

        var collider = closeButton.gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 1f, 0.4f);

        TMP_Text cross = CreateText("X", closeButton, 0.022f, FontStyles.Bold);
        cross.text = "X";
        cross.color = Color.white;
        cross.rectTransform.sizeDelta = new Vector2(6f, 6f);

        // La X e' figlia del bottone, che e' gia' scalato a CloseSize: va scontata,
        // altrimenti il testo eredita quella scala e diventa illeggibile.
        cross.transform.localScale = Vector3.one * (0.022f * 10f / CloseSize);
        cross.transform.localPosition = new Vector3(0f, 0f, -0.1f);

        // Senza questo la mesh del testo non esiste al primo frame e la X non si vede.
        cross.ForceMeshUpdate();

        var interactable = closeButton.gameObject.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(_ => Hide());
    }

    public void Show(string heading, string text, Camera viewer)
    {
        gameObject.SetActive(true);

        title.text = heading;
        title.color = textColor;
        body.text = text;
        body.color = textColor;

        float inner = width - Padding * 2f;

        float titleScale = TitleHeight * 10f;
        float bodyScale = BodyHeight * 10f;

        title.rectTransform.sizeDelta = new Vector2(inner / titleScale, 20f);
        body.rectTransform.sizeDelta = new Vector2(inner / bodyScale, 40f);

        title.ForceMeshUpdate();
        body.ForceMeshUpdate();

        float titleBlock = title.textBounds.size.y * titleScale;
        float bodyBlock = body.textBounds.size.y * bodyScale;

        if (!Usable(titleBlock)) titleBlock = TitleHeight;
        if (!Usable(bodyBlock)) bodyBlock = BodyHeight;

        float height = Padding * 2f + titleBlock + Padding * 0.7f + bodyBlock;

        background.localScale = new Vector3(width, height, 1f);
        background.localPosition = Vector3.zero;

        float top = height * 0.5f;
        title.transform.localPosition = new Vector3(0f, top - Padding - titleBlock * 0.5f, -0.003f);
        body.transform.localPosition = new Vector3(0f, top - Padding - titleBlock - Padding * 0.7f - bodyBlock * 0.5f, -0.003f);

        closeButton.localPosition = new Vector3(
            width * 0.5f - CloseSize * 0.7f,
            top - CloseSize * 0.7f,
            -0.006f);

        PlaceInFrontOf(viewer);
    }

    public void Hide() => gameObject.SetActive(false);

    void PlaceInFrontOf(Camera viewer)
    {
        if (viewer == null) return;

        Transform cam = viewer.transform;
        Vector3 forward = cam.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        else forward.Normalize();

        transform.SetPositionAndRotation(
            cam.position + forward * distanceFromCamera,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    Transform CreateQuad(string name, Transform parent, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go.transform;
    }

    static TMP_Text CreateText(string name, Transform parent, float worldScale, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        // fontSize 1 in TMP world-space equivale a circa un decimo di unita' locale per riga.
        go.transform.localScale = Vector3.one * (worldScale * 10f);

        var text = go.AddComponent<TextMeshPro>();
        text.fontSize = 1f;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    static Material CreateUnlit(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");

        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        return material;
    }

    static bool Usable(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f && v < 10f;
}
